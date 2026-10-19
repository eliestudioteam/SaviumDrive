// Services/UpdateService.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace SaviumDrive.Services
{
    public class UpdateInfo
    {
        public string Version { get; set; }
        public string DownloadUrl { get; set; }
        public string Changelog { get; set; }
        public string ReleaseDate { get; set; }
        public long Size { get; set; }
    }

    public class UpdateService
    {
        // Endpoint JSON de la API (la URL github.com/.../releases/latest devuelve HTML)
        private const string GITHUB_API_URL = "https://api.github.com/repos/eliestudioteam/SaviumDrive/releases/latest";
        private const string GITHUB_RELEASES_BY_TAG_URL = "https://api.github.com/repos/eliestudioteam/SaviumDrive/releases/tags/";
        private const string CURRENT_VERSION = "1.0.0"; // Actualiza esto con cada release
        private static readonly HttpClient _httpClient = CreateHttpClient();

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
        /// Verifica si hay una nueva versión disponible
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

                // Obtener versión (tag_name sin el prefijo 'v')
                string tagName = root.GetProperty("tag_name").GetString();
                string version = tagName.TrimStart('v');

                // Comparar versiones
                if (!UpdateVersioning.IsNewerVersion(version, CURRENT_VERSION))
                    return null;

                // Obtener URL de descarga del instalador (.exe)
                string downloadUrl = null;
                long fileSize = 0;

                if (root.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        string name = asset.GetProperty("name").GetString();
                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset.GetProperty("browser_download_url").GetString();
                            fileSize = asset.GetProperty("size").GetInt64();
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(downloadUrl))
                    return null;

                return new UpdateInfo
                {
                    Version = version,
                    DownloadUrl = downloadUrl,
                    Changelog = root.GetProperty("body").GetString(),
                    ReleaseDate = root.GetProperty("published_at").GetString(),
                    Size = fileSize
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
            foreach (string tag in new[] { "v" + version, version })
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
        /// Descarga el instalador con reporte de progreso
        /// </summary>
        public async Task<string> DownloadUpdateAsync(string downloadUrl, IProgress<int> progress)
        {
            try
            {
                // Crear carpeta temporal
                string tempFolder = Path.Combine(Path.GetTempPath(), "SaviumDrive");
                Directory.CreateDirectory(tempFolder);
                string fileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
                string filePath = Path.Combine(tempFolder, fileName);

                // Descargar con progreso
                using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
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

                return filePath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error downloading update: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Instala la actualización descargada
        /// </summary>
        public void InstallUpdate(string installerPath)
        {
            try
            {
                // Crear un script batch que espere a que la app se cierre y ejecute el instalador
                string batchPath = Path.Combine(Path.GetTempPath(), "update_savium.bat");
                string batchContent = $@"
@echo off
timeout /t 2 /nobreak >nul
start """" ""{installerPath}""
del ""{batchPath}""
";
                File.WriteAllText(batchPath, batchContent);

                // Ejecutar el batch
                Process.Start(new ProcessStartInfo
                {
                    FileName = batchPath,
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error installing update: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Guarda información de la última actualización instalada
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
        /// Verifica si es la primera ejecución después de actualizar
        /// </summary>
        public (bool isFirstRun, string version, string changelog) CheckFirstRunAfterUpdate()
        {
            try
            {
                var config = SettingsService.Current;
                bool isFirstRun = UpdateVersioning.IsFirstRunAfterUpdate(
                    config.LastRunVersion, config.LastUpdateVersion, CURRENT_VERSION);

                // Recordar qué versión se ejecutó para no volver a mostrar el changelog
                if (config.LastRunVersion != CURRENT_VERSION)
                {
                    config.LastRunVersion = CURRENT_VERSION;
                    SettingsService.Save();
                }

                if (!isFirstRun)
                    return (false, null, null);

                // El changelog guardado solo corresponde si el actualizador descargó esta misma versión
                string changelog = config.LastUpdateVersion == CURRENT_VERSION ? config.LastUpdateChangelog : null;
                return (true, CURRENT_VERSION, changelog);
            }
            catch
            {
                return (false, null, null);
            }
        }

        /// <summary>
        /// Formatea el tamaño del archivo
        /// </summary>
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