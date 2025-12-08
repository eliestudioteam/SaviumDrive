// Services/UpdateService.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage;

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
        private const string GITHUB_API_URL = "https://github.com/eliestudioteam/SaviumDrive/releases/latest";
        private const string CURRENT_VERSION = "1.0.0"; // Actualiza esto con cada release
        private static readonly HttpClient _httpClient = new HttpClient();

        public UpdateService()
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "SaviumDrive-Updater");
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
                if (!IsNewerVersion(version, CURRENT_VERSION))
                    return null;

                // Obtener URL de descarga del instalador (.msix o .exe)
                string downloadUrl = null;
                long fileSize = 0;

                if (root.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        string name = asset.GetProperty("name").GetString();
                        if (name.EndsWith(".msix") || name.EndsWith(".exe"))
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
        /// Descarga el instalador con reporte de progreso
        /// </summary>
        public async Task<string> DownloadUpdateAsync(string downloadUrl, IProgress<int> progress)
        {
            try
            {
                // Crear carpeta temporal
                var tempFolder = ApplicationData.Current.TemporaryFolder;
                string fileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
                var tempFile = await tempFolder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
                string filePath = tempFile.Path;

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
        /// Compara dos versiones en formato semántico (ej: 1.2.3)
        /// </summary>
        private bool IsNewerVersion(string newVersion, string currentVersion)
        {
            try
            {
                var newParts = newVersion.Split('.');
                var currentParts = currentVersion.Split('.');

                for (int i = 0; i < Math.Max(newParts.Length, currentParts.Length); i++)
                {
                    int newNum = i < newParts.Length ? int.Parse(newParts[i]) : 0;
                    int currentNum = i < currentParts.Length ? int.Parse(currentParts[i]) : 0;

                    if (newNum > currentNum) return true;
                    if (newNum < currentNum) return false;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Guarda información de la última actualización instalada
        /// </summary>
        public void SaveUpdateInfo(UpdateInfo updateInfo)
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                settings.Values["LastUpdateVersion"] = updateInfo.Version;
                settings.Values["LastUpdateChangelog"] = updateInfo.Changelog;
                settings.Values["LastUpdateDate"] = DateTime.Now.ToString("O");
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
                var settings = ApplicationData.Current.LocalSettings;
                string lastVersion = settings.Values["LastUpdateVersion"] as string;
                string lastChangelog = settings.Values["LastUpdateChangelog"] as string;

                if (string.IsNullOrEmpty(lastVersion))
                    return (false, null, null);

                // Si la última versión guardada es diferente a la actual, es primera ejecución
                if (lastVersion != CURRENT_VERSION)
                {
                    // Actualizar a la versión actual
                    settings.Values["LastUpdateVersion"] = CURRENT_VERSION;
                    return (true, lastVersion, lastChangelog);
                }

                return (false, null, null);
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