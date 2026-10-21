// Services/UpdateService.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace SaviumDrive.Services
{
    /// <summary>Datos de una versión publicada en GitHub Releases.</summary>
    public class UpdateInfo
    {
        public string Version { get; set; }
        public string DownloadUrl { get; set; }
        public string FileName { get; set; }
        public string Changelog { get; set; }
        public string ReleaseDate { get; set; }
        public long Size { get; set; }

        /// <summary>SHA256SUMS.txt de la release; null en releases antiguas que no lo traen.</summary>
        public string ChecksumsUrl { get; set; }
    }

    /// <summary>
    /// Actualizaciones a partir de las releases de GitHub: busca si hay una versión más nueva, descarga
    /// el instalador, lo ejecuta y, en el primer arranque tras actualizar, aporta las notas de la versión
    /// para la ventana de novedades.
    /// </summary>
    public class UpdateService
    {
        // Endpoint JSON de la API (la URL github.com/.../releases/latest devuelve HTML)
        private const string GITHUB_API_URL = "https://api.github.com/repos/eliestudioteam/SaviumDrive/releases/latest";
        private const string GITHUB_RELEASES_BY_TAG_URL = "https://api.github.com/repos/eliestudioteam/SaviumDrive/releases/tags/";
        private static readonly HttpClient _httpClient = CreateHttpClient();

        /// <summary>
        /// Versión de esta compilación ("1.2.3"). Sale de &lt;Version&gt; en SaviumDrive.csproj, que debe
        /// coincidir con la etiqueta de la release (1.2.3, sin "v").
        /// </summary>
        public static string CurrentVersion { get; } = UpdateRelease.NumericVersion(typeof(UpdateService).Assembly);

        /// <summary>Versión para mostrar en la interfaz ("1.0.0 Beta").</summary>
        public static string DisplayVersion { get; } = UpdateRelease.DisplayVersion(
            typeof(UpdateService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? CurrentVersion);

        /// <summary>
        /// Página de la versión en GitHub (notas completas, descargas). Las etiquetas de las releases son la
        /// versión sin prefijo: 1.2.3, no v1.2.3.
        /// </summary>
        public static string PaginaDeVersion(string version) =>
            $"https://github.com/eliestudioteam/SaviumDrive/releases/tag/{(version ?? "").TrimStart('v')}";

        // Las cabeceras se configuran una sola vez: el cliente es estático y compartido
        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            // La API de GitHub rechaza peticiones sin User-Agent
            client.DefaultRequestHeaders.Add("User-Agent", "SaviumDrive-Updater");
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
            return client;
        }

        /// <summary>
        /// Datos de la última release si es más nueva que esta versión y trae un instalador (.exe); si no, null.
        /// </summary>
        public async Task<UpdateInfo> CheckForUpdatesAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync(GITHUB_API_URL);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // La etiqueta de la release es la versión (1.2.0); se admite también con "v" (v1.2.0)
                string tagName = root.GetProperty("tag_name").GetString();
                string version = tagName.TrimStart('v');

                if (!UpdateVersioning.IsNewerVersion(version, CurrentVersion))
                    return null;

                var assets = new List<ReleaseAsset>();
                if (root.TryGetProperty("assets", out var assetsJson))
                {
                    foreach (var asset in assetsJson.EnumerateArray())
                    {
                        assets.Add(new ReleaseAsset
                        {
                            Name = asset.GetProperty("name").GetString(),
                            DownloadUrl = asset.GetProperty("browser_download_url").GetString(),
                            Size = asset.GetProperty("size").GetInt64()
                        });
                    }
                }

                var installer = UpdateRelease.PickInstaller(assets);
                if (installer == null)
                    return null;

                return new UpdateInfo
                {
                    Version = version,
                    DownloadUrl = installer.DownloadUrl,
                    FileName = installer.Name,
                    Size = installer.Size,
                    ChecksumsUrl = UpdateRelease.FindChecksums(assets)?.DownloadUrl,
                    Changelog = root.GetProperty("body").GetString(),
                    ReleaseDate = root.GetProperty("published_at").GetString()
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking updates: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Obtiene las notas de la release de una versión concreta (para instalaciones manuales,
        /// donde el actualizador no guardó el changelog). Devuelve null si no se encuentra.
        /// </summary>
        public async Task<string> GetReleaseNotesAsync(string version)
        {
            // Primero la etiqueta sin prefijo (la que se usa ahora) y luego con "v", por si alguna antigua la lleva
            foreach (string tag in new[] { version, "v" + version })
            {
                try
                {
                    var response = await _httpClient.GetAsync($"{GITHUB_RELEASES_BY_TAG_URL}{tag}");
                    if (!response.IsSuccessStatusCode) continue;

                    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    if (doc.RootElement.TryGetProperty("body", out var body))
                        return body.GetString();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error getting release notes: {ex.Message}");
                }
            }
            return null;
        }

        /// <summary>
        /// Descarga el instalador a %TEMP%\SaviumDrive informando del porcentaje y comprueba que no llegó
        /// dañado ni alterado (tamaño y, si la release trae SHA256SUMS.txt, su SHA-256). Devuelve la ruta.
        /// </summary>
        public async Task<string> DownloadUpdateAsync(UpdateInfo update, IProgress<int> progress)
        {
            string tempFolder = Path.Combine(Path.GetTempPath(), "SaviumDrive");
            Directory.CreateDirectory(tempFolder);
            string filePath = Path.Combine(tempFolder, Path.GetFileName(update.FileName));

            try
            {
                using (var response = await _httpClient.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                    var buffer = new byte[8192];
                    long totalRead = 0;

                    using var contentStream = await response.Content.ReadAsStreamAsync();
                    using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                    int bytesRead;
                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        totalRead += bytesRead;

                        if (totalBytes > 0)
                        {
                            int percentage = (int)((totalRead * 100) / totalBytes);
                            progress?.Report(percentage);
                        }
                    }
                }

                await VerifyDownloadAsync(update, filePath);
                return filePath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error downloading update: {ex.Message}");
                // Nunca dejar a mano un instalador a medias o que no pasó la verificación
                try { File.Delete(filePath); } catch { }
                throw;
            }
        }

        private async Task VerifyDownloadAsync(UpdateInfo update, string filePath)
        {
            if (update.Size > 0 && new FileInfo(filePath).Length != update.Size)
                throw new InvalidDataException("El instalador descargado está incompleto.");

            if (update.ChecksumsUrl == null)
                return; // releases antiguas, sin SHA256SUMS.txt

            string checksums = await _httpClient.GetStringAsync(update.ChecksumsUrl);
            string expected = UpdateRelease.FindSha256(checksums, update.FileName)
                ?? throw new InvalidDataException($"{UpdateRelease.ChecksumsFileName} no incluye {update.FileName}.");

            if (UpdateRelease.ComputeSha256(filePath) != expected)
                throw new InvalidDataException("El SHA-256 del instalador descargado no coincide.");
        }

        /// <summary>
        /// Lanza el instalador en modo actualización. Justo después hay que cerrar la app: el instalador
        /// espera a que se cierre, actualiza sin preguntar y la vuelve a abrir.
        /// </summary>
        public void InstallUpdate(string installerPath)
        {
            try
            {
                Process.Start(new ProcessStartInfo(installerPath, UpdateRelease.InstallerUpdateArgument)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error installing update: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Recuerda la versión descargada y sus notas para mostrarlas en el primer arranque tras instalarla.
        /// </summary>
        public void SaveUpdateInfo(UpdateInfo updateInfo)
        {
            try
            {
                SettingsService.Current.LastUpdateVersion = updateInfo.Version;
                SettingsService.Current.LastUpdateChangelog = updateInfo.Changelog;
                SettingsService.Save();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving update info: {ex.Message}");
            }
        }

        /// <summary>
        /// True la primera vez que se abre esta versión tras una actualización, con la versión y sus notas
        /// (null si el actualizador no las guardó; entonces se piden a GitHub).
        /// </summary>
        public (bool isFirstRun, string version, string changelog) CheckFirstRunAfterUpdate()
        {
            try
            {
                var config = SettingsService.Current;
                bool isFirstRun = UpdateVersioning.IsFirstRunAfterUpdate(
                    config.LastRunVersion, config.LastUpdateVersion, CurrentVersion);

                // Recordar qué versión se ejecutó para no volver a mostrar el changelog
                if (config.LastRunVersion != CurrentVersion)
                {
                    config.LastRunVersion = CurrentVersion;
                    SettingsService.Save();
                }

                if (!isFirstRun)
                    return (false, null, null);

                // El changelog guardado solo corresponde si el actualizador descargó esta misma versión
                string changelog = config.LastUpdateVersion == CurrentVersion ? config.LastUpdateChangelog : null;
                return (true, CurrentVersion, changelog);
            }
            catch
            {
                return (false, null, null);
            }
        }

        /// <summary>Tamaño legible ("12.5 MB").</summary>
        public static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}