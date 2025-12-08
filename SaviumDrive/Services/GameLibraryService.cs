using Newtonsoft.Json;
using SaviumDrive.Core;
using SaviumDrive.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.Resources;

namespace SaviumDrive.Services
{
    public class GlobalProgressArgs
    {
        public string GameName { get; set; }
        public int Percentage { get; set; }
        public string OperationName { get; set; } // "Subiendo..." o "Descargando..."
        public string DetailMessage { get; set; } // "Archivo.dat (12MB/50MB)"
        public bool IsActive { get; set; }
    }

    public class LibraryContainer
    {
        public DateTime LastUpdated { get; set; }
        public List<GameConfig> Games { get; set; } = new List<GameConfig>();
    }

    public class GameLibraryService
    {
        private readonly string _localFilePath;
        private DriveManager _driveManager;
        private const string CLOUD_CONFIG_FILE = "savium_config.json";
        private readonly ResourceLoader _loader;

        public bool IsBusy { get; private set; } = false;

        // 🔥 MEMORIA DE ÚLTIMO ESTADO (Para restaurar UI al navegar)
        public GlobalProgressArgs LastProgressArgs { get; private set; }

        public event EventHandler<GlobalProgressArgs> ProgressChanged;

        public GameLibraryService(Google.Apis.Drive.v3.DriveService driveService)
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(appData, "SaviumDrive");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            _localFilePath = Path.Combine(folder, "games.json");
            _driveManager = new DriveManager(driveService);
            _loader = ResourceLoader.GetForViewIndependentUse();
        }

        private string GetStr(string key) => _loader.GetString(key);

        // Notificar a la UI
        private void NotifyProgress(string gameName, int percent, string opKey, string detail, bool active)
        {
            IsBusy = active;

            string opText = "";
            if (!string.IsNullOrEmpty(opKey)) opText = GetStr(opKey);

            var args = new GlobalProgressArgs
            {
                GameName = gameName,
                Percentage = percent,
                OperationName = opText,
                DetailMessage = detail,
                IsActive = active
            };

            // Guardar estado para cuando LibraryPage se recargue
            LastProgressArgs = args;

            ProgressChanged?.Invoke(this, args);
        }

        // ... (GetDirectorySize, FormatBytes, CargarSoloLocal, SincronizarYActualizar IGUAL QUE ANTES) ...

        // --- REPETIMOS MÉTODOS AUXILIARES PARA COMPLETITUD ---
        private long GetDirectorySize(string path)
        {
            try
            {
                long size = 0;
                var info = new DirectoryInfo(path);
                foreach (var file in info.GetFiles("*", SearchOption.AllDirectories)) size += file.Length;
                return size;
            }
            catch { return 0; }
        }

        private string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = (decimal)bytes;
            while (Math.Round(number / 1024) >= 1) { number = number / 1024; counter++; }
            return string.Format("{0:n1} {1}", number, suffixes[counter]);
        }

       

        // --- OPERACIONES (Actualizadas con StatusUploading / StatusDownloading) ---

        public async Task<GameConfig> AgregarJuegoNuevo(string nombre, string rutaLocal, string coverUrl, CancellationToken token)
        {
            string idJuegoRaiz = null; // Para hacer rollback

            try
            {
                NotifyProgress(nombre, 0, "StatusUploading", GetStr("MsgScanning"), true);

                string idMadre = _driveManager.GetOrCreateFolder("SaviumBackups");
                idJuegoRaiz = _driveManager.GetOrCreateFolder(nombre, idMadre);
                var infoCarpeta = new DirectoryInfo(rutaLocal);
                string idCarpetaContenido = _driveManager.GetOrCreateFolder(infoCarpeta.Name, idJuegoRaiz);

                long totalBytes = GetDirectorySize(rutaLocal);
                long bytesSubidos = 0;
                string totalStr = FormatBytes(totalBytes);

                await SubirDirectorioRecursivo(rutaLocal, idCarpetaContenido, token, (nombreArchivo, pesoArchivo) =>
                {
                    token.ThrowIfCancellationRequested(); // Verificar cancelación en cada archivo
                    bytesSubidos += pesoArchivo;
                    int porcentaje = totalBytes > 0 ? (int)((double)bytesSubidos / totalBytes * 100) : 0;
                    string progresoStr = $"{FormatBytes(bytesSubidos)} / {totalStr}";

                    NotifyProgress(nombre, porcentaje, "StatusUploading", string.Format(GetStr("MsgUploadingSize"), nombreArchivo, progresoStr), true);
                });

                var nuevoJuego = new GameConfig
                {
                    Name = nombre,
                    LocalPath = rutaLocal,
                    DriveFolderId = idJuegoRaiz,
                    CoverUrl = coverUrl,
                    LastBackup = DateTime.UtcNow
                };

                var juegos = await CargarSoloLocal();
                juegos.Add(nuevoJuego);
                await GuardarLibreria(juegos);

                return nuevoJuego;
            }
            catch (OperationCanceledException)
            {
                // 🔥 ROLLBACK: Eliminar la carpeta que se creó en Drive
                if (!string.IsNullOrEmpty(idJuegoRaiz))
                {
                    try
                    {
                        await _driveManager.EliminarArchivoOCarpeta(idJuegoRaiz);
                    }
                    catch { }
                }
                throw; // Relanzar para que la UI lo capture
            }
            finally
            {
                NotifyProgress("", 0, "", "", false);
            }
        }

        // 🔥 AGREGAR ESTE MÉTODO PÚBLICO EN GameLibraryService.cs

        /// <summary>
        /// Calcula el tamaño total ocupado por los backups en Drive
        /// </summary>
        public async Task<long> CalcularEspacioBackupsEnDrive()
        {
            try
            {
                if (_driveManager == null || _driveManager.Service == null)
                    return 0;

                // 🔥 TIMEOUT de 15 segundos para evitar bloqueos
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                {
                    try
                    {
                        string idMadre = _driveManager.BuscarCarpetaPorNombre("SaviumBackups");

                        if (string.IsNullOrEmpty(idMadre))
                            return 0;

                        return await CalcularTamañoDirectorioEnDrive(idMadre, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        System.Diagnostics.Debug.WriteLine("⏱️ Cálculo de espacio timeout");
                        return 0;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error calculando espacio: {ex.Message}");
                return 0;
            }
        }


        /// <summary>
        /// Obtiene la información de cuota de almacenamiento del usuario en Drive
        /// </summary>
        public async Task<(long usado, long total)?> ObtenerCuotaDrive()
        {
            try
            {
                if (_driveManager == null || _driveManager.Service == null)
                    return null;

                // 🔥 TIMEOUT de 10 segundos
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                {
                    try
                    {
                        var aboutRequest = _driveManager.Service.About.Get();
                        aboutRequest.Fields = "storageQuota";
                        var about = await aboutRequest.ExecuteAsync(cts.Token);

                        if (about.StorageQuota != null &&
                            about.StorageQuota.Usage.HasValue &&
                            about.StorageQuota.Limit.HasValue)
                        {
                            return (about.StorageQuota.Usage.Value, about.StorageQuota.Limit.Value);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        System.Diagnostics.Debug.WriteLine("⏱️ Consulta de cuota timeout");
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error obteniendo cuota: {ex.Message}");
                return null;
            }
        }

        // 🔥 ACTUALIZAR BACKUP CON CARPETA TEMPORAL (No destruir archivos viejos)
        public async Task ActualizarBackup(GameConfig juego, CancellationToken token)
        {
            string idCarpetaTemporal = null;
            string nombreCarpetaOriginal = null;

            try
            {
                NotifyProgress(juego.Name, 0, "StatusUploading", GetStr("MsgPrepUpdate"), true);

                if (string.IsNullOrEmpty(juego.DriveFolderId)) throw new Exception("ID Inválido");

                // 1️⃣ OBTENER CARPETA ORIGINAL (la que contiene los archivos del juego)
                var itemsRaiz = await _driveManager.ListarContenidoCarpeta(juego.DriveFolderId);
                var infoCarpetaLocal = new DirectoryInfo(juego.LocalPath);
                var carpetaOriginal = itemsRaiz.FirstOrDefault(i => i.Name == infoCarpetaLocal.Name && i.MimeType == "application/vnd.google-apps.folder");

                if (carpetaOriginal == null) throw new Exception("Carpeta original no encontrada en Drive");
                nombreCarpetaOriginal = carpetaOriginal.Name;

                // 2️⃣ CREAR CARPETA TEMPORAL EN DRIVE (aquí subiremos el nuevo backup)
                idCarpetaTemporal = _driveManager.GetOrCreateFolder($"TEMP_{nombreCarpetaOriginal}", juego.DriveFolderId);

                // 3️⃣ SUBIR CONTENIDO NUEVO A LA CARPETA TEMPORAL
                long totalBytes = GetDirectorySize(juego.LocalPath);
                long bytesSubidos = 0;
                string totalStr = FormatBytes(totalBytes);

                await SubirDirectorioRecursivo(juego.LocalPath, idCarpetaTemporal, token, (nombreArchivo, pesoArchivo) =>
                {
                    token.ThrowIfCancellationRequested();
                    bytesSubidos += pesoArchivo;
                    int porcentaje = totalBytes > 0 ? (int)((double)bytesSubidos / totalBytes * 100) : 0;
                    string progresoStr = $"{FormatBytes(bytesSubidos)} / {totalStr}";

                    NotifyProgress(juego.Name, porcentaje, "StatusUploading", string.Format(GetStr("MsgUploadingSize"), nombreArchivo, progresoStr), true);
                });

                // 4️⃣ TODO BIEN: Eliminar carpeta vieja y renombrar la temporal
                await _driveManager.EliminarArchivoOCarpeta(carpetaOriginal.Id);
                await _driveManager.RenombrarArchivo(idCarpetaTemporal, nombreCarpetaOriginal);

                // 5️⃣ Actualizar fecha
                juego.LastBackup = DateTime.UtcNow;
                var juegos = await CargarSoloLocal();
                var existente = juegos.FirstOrDefault(j => j.Id == juego.Id);
                if (existente != null)
                {
                    existente.LastBackup = juego.LastBackup;
                    await GuardarLibreria(juegos);
                }
            }
            catch (OperationCanceledException)
            {
                // 🔥 ROLLBACK: Eliminar carpeta temporal (archivos viejos permanecen intactos)
                if (!string.IsNullOrEmpty(idCarpetaTemporal))
                {
                    try
                    {
                        await _driveManager.EliminarArchivoOCarpeta(idCarpetaTemporal);
                    }
                    catch { }
                }
                throw;
            }
            finally
            {
                NotifyProgress("", 0, "", "", false);
            }
        }


        // 🔥 RESTAURAR JUEGO CON CARPETA TEMPORAL LOCAL Y PROGRESO DETALLADO
        public async Task RestaurarJuego(GameConfig juego, CancellationToken token)
        {
            string rutaCarpetaTemporal = null;
            string rutaCarpetaOriginal = juego.LocalPath;
            string nombreCarpetaTemporal = $"TEMP_{new DirectoryInfo(juego.LocalPath).Name}";

            try
            {
                NotifyProgress(juego.Name, 0, "StatusDownloading", GetStr("MsgConnectingDrive"), true);

                if (string.IsNullOrEmpty(juego.DriveFolderId)) throw new Exception("ID inválido");

                // 1️⃣ CREAR CARPETA TEMPORAL LOCAL (junto a la carpeta original)
                string directorioContenedor = Path.GetDirectoryName(juego.LocalPath);
                rutaCarpetaTemporal = Path.Combine(directorioContenedor, nombreCarpetaTemporal);

                // Si ya existe la carpeta temporal (de un intento previo), eliminarla
                if (Directory.Exists(rutaCarpetaTemporal))
                {
                    Directory.Delete(rutaCarpetaTemporal, true);
                }
                Directory.CreateDirectory(rutaCarpetaTemporal);

                // 2️⃣ OBTENER CARPETA ORIGEN Y CALCULAR TAMAÑO TOTAL
                var itemsRaiz = await _driveManager.ListarContenidoCarpeta(juego.DriveFolderId);
                var nombreCarpetaLocal = new DirectoryInfo(juego.LocalPath).Name;
                var carpetaContenedora = itemsRaiz.FirstOrDefault(i => i.Name == nombreCarpetaLocal && i.MimeType == "application/vnd.google-apps.folder");
                string idOrigen = carpetaContenedora != null ? carpetaContenedora.Id : juego.DriveFolderId;

                // 🔥 CALCULAR TAMAÑO TOTAL ANTES DE DESCARGAR
                long totalBytes = await CalcularTamañoDirectorioEnDrive(idOrigen);
                long bytesDescargados = 0;
                string totalStr = FormatBytes(totalBytes);

                NotifyProgress(juego.Name, 0, "StatusDownloading", GetStr("MsgScanning"), true);

                // 3️⃣ DESCARGAR ARCHIVOS A LA CARPETA TEMPORAL CON PROGRESO
                await DescargarDirectorioRecursivo(idOrigen, rutaCarpetaTemporal, token, (rutaArchivo, pesoArchivo) =>
                {
                    token.ThrowIfCancellationRequested();
                    bytesDescargados += pesoArchivo;
                    int porcentaje = totalBytes > 0 ? (int)((double)bytesDescargados / totalBytes * 100) : 0;
                    string progresoStr = $"{FormatBytes(bytesDescargados)} / {totalStr}";
                    string nombreArchivo = Path.GetFileName(rutaArchivo);

                    NotifyProgress(juego.Name, porcentaje, "StatusDownloading",
                        string.Format(GetStr("MsgDownloadingSize"), nombreArchivo, progresoStr), true);
                });

                // 4️⃣ TODO BIEN: Eliminar carpeta vieja y renombrar la temporal
                if (Directory.Exists(rutaCarpetaOriginal))
                {
                    Directory.Delete(rutaCarpetaOriginal, true);
                }

                Directory.Move(rutaCarpetaTemporal, rutaCarpetaOriginal);
            }
            catch (OperationCanceledException)
            {
                // 🔥 ROLLBACK: Eliminar carpeta temporal (archivos viejos permanecen intactos)
                if (!string.IsNullOrEmpty(rutaCarpetaTemporal) && Directory.Exists(rutaCarpetaTemporal))
                {
                    try
                    {
                        Directory.Delete(rutaCarpetaTemporal, true);
                    }
                    catch { }
                }
                throw;
            }
            finally
            {
                NotifyProgress("", 0, "", "", false);
            }
        }


        // ... (Resto de métodos privados SubirDirectorioRecursivo, DescargarYFusionarNube, etc. MANTENER IGUAL) ...
        private async Task SubirDirectorioRecursivo(string rutaLocal, string idCarpetaPadreDrive, CancellationToken token, Action<string, long> onFileUploaded)
        {
            token.ThrowIfCancellationRequested();
            var archivos = Directory.GetFiles(rutaLocal);
            foreach (var archivo in archivos)
            {
                long peso = new FileInfo(archivo).Length;
                await Task.Run(() => _driveManager.SubirArchivo(archivo, idCarpetaPadreDrive, token));
                onFileUploaded?.Invoke(Path.GetFileName(archivo), peso);
            }
            var subCarpetas = Directory.GetDirectories(rutaLocal);
            foreach (var carpeta in subCarpetas)
            {
                string idNueva = _driveManager.GetOrCreateFolder(Path.GetFileName(carpeta), idCarpetaPadreDrive);
                await SubirDirectorioRecursivo(carpeta, idNueva, token, onFileUploaded);
            }
        }

        // --- SINCRONIZACIÓN Y CARGA ---
        public async Task<List<GameConfig>> CargarSoloLocal()
        {
            if (File.Exists(_localFilePath))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(_localFilePath);
                    var container = JsonConvert.DeserializeObject<LibraryContainer>(json);
                    if (container != null && container.Games != null) return container.Games;
                    var listaVieja = JsonConvert.DeserializeObject<List<GameConfig>>(json);
                    return listaVieja ?? new List<GameConfig>();
                }
                catch { }
            }
            return new List<GameConfig>();
        }

        // 🔥 MÉTODO RESTAURADO: Sincronizar con la nube
        public async Task<List<GameConfig>> SincronizarYActualizar()
        {
            try
            {
                NotifyProgress("", -1, "StatusDownloading", GetStr("MsgSyncing"), true);

                // 🔥 TIMEOUT: Si tarda más de 30 segundos, cancelar
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                {
                    try
                    {
                        await DescargarYFusionarNube(cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        System.Diagnostics.Debug.WriteLine("⏱️ Sincronización timeout después de 30 segundos");
                        // No lanzar - continuar con datos locales
                    }
                    catch (System.Net.Http.HttpRequestException ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"🌐 Error de red en sincronización: {ex.Message}");
                        // No lanzar - continuar con datos locales
                    }
                }

                return await CargarSoloLocal();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error general en sincronización: {ex.Message}");
                // Devolver datos locales aunque haya error
                return await CargarSoloLocal();
            }
            finally
            {
                NotifyProgress("", 0, "", "", false);
            }
        }


        private async Task DescargarYFusionarNube(CancellationToken token = default)
        {
            try
            {
                token.ThrowIfCancellationRequested();

                string idMadre = _driveManager.GetOrCreateFolder("SaviumBackups");

                token.ThrowIfCancellationRequested();

                var request = _driveManager.Service.Files.List();
                request.Q = $"name = '{CLOUD_CONFIG_FILE}' and '{idMadre}' in parents and trashed = false";
                var result = await request.ExecuteAsync(token);

                if (result.Files.Count > 0)
                {
                    string fileId = result.Files[0].Id;
                    string jsonNube = "";

                    using (var stream = new MemoryStream())
                    {
                        await _driveManager.Service.Files.Get(fileId).DownloadAsync(stream, token);
                        stream.Position = 0;
                        using (var reader = new StreamReader(stream))
                            jsonNube = await reader.ReadToEndAsync();
                    }

                    token.ThrowIfCancellationRequested();

                    var nubeContainer = JsonConvert.DeserializeObject<LibraryContainer>(jsonNube);
                    if (nubeContainer == null) return;

                    var juegosLocales = await CargarSoloLocal();
                    DateTime fechaLocal = File.Exists(_localFilePath)
                        ? File.GetLastWriteTimeUtc(_localFilePath)
                        : DateTime.MinValue;

                    if (nubeContainer.LastUpdated > fechaLocal)
                    {
                        await GuardarEnDisco(nubeContainer.Games, nubeContainer.LastUpdated);
                    }

                    bool cambios = false;
                    foreach (var jNube in nubeContainer.Games)
                    {
                        if (!juegosLocales.Any(j => j.Id == jNube.Id))
                        {
                            juegosLocales.Add(jNube);
                            cambios = true;
                        }
                    }

                    if (cambios)
                    {
                        await GuardarLibreria(juegosLocales);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("✋ Sincronización cancelada por el usuario o timeout");
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error sincronizando: {ex.Message}");
                // No re-lanzar - permitir que la app continúe con datos locales
            }
        }

        public async Task EliminarJuego(GameConfig juego)
        {
            try
            {
                NotifyProgress(juego.Name, -1, "StatusDeleting", GetStr("MsgDeleting"), true);

                if (!string.IsNullOrEmpty(juego.DriveFolderId))
                {
                    await _driveManager.EliminarArchivoOCarpeta(juego.DriveFolderId);
                }
            }
            catch { }
            finally
            {
                NotifyProgress("", 0, "", "", false);
            }

            var juegos = await CargarSoloLocal();
            var eliminar = juegos.FirstOrDefault(j => j.Id == juego.Id);
            if (eliminar != null)
            {
                juegos.Remove(eliminar);
                await GuardarLibreria(juegos);
            }
        }
        public async Task GuardarLibreria(List<GameConfig> juegos)
        {
            DateTime ahora = DateTime.UtcNow;
            await GuardarEnDisco(juegos, ahora);
            var container = new LibraryContainer { LastUpdated = ahora, Games = juegos };
            string json = JsonConvert.SerializeObject(container, Formatting.Indented);
            try { await SubirConfiguracionNube(json); } catch { }
        }
        private async Task GuardarEnDisco(List<GameConfig> juegos, DateTime fecha)
        {
            var container = new LibraryContainer { LastUpdated = fecha, Games = juegos };
            string json = JsonConvert.SerializeObject(container, Formatting.Indented);
            await File.WriteAllTextAsync(_localFilePath, json);
            File.SetLastWriteTimeUtc(_localFilePath, fecha);
        }
        private async Task SubirConfiguracionNube(string jsonContent)
        {
            try
            {
                // 🔥 TIMEOUT de 20 segundos
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
                {
                    string idMadre = _driveManager.GetOrCreateFolder("SaviumBackups");

                    var request = _driveManager.Service.Files.List();
                    request.Q = $"name = '{CLOUD_CONFIG_FILE}' and '{idMadre}' in parents and trashed = false";
                    var result = await request.ExecuteAsync(cts.Token);

                    if (result.Files.Count > 0)
                    {
                        foreach (var f in result.Files)
                        {
                            try
                            {
                                await _driveManager.EliminarArchivoOCarpeta(f.Id);
                            }
                            catch { }
                        }
                    }

                    var fileMetadata = new Google.Apis.Drive.v3.Data.File()
                    {
                        Name = CLOUD_CONFIG_FILE,
                        Parents = new List<string> { idMadre },
                        MimeType = "application/json"
                    };

                    using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(jsonContent)))
                    {
                        var createReq = _driveManager.Service.Files.Create(fileMetadata, stream, "application/json");
                        await createReq.UploadAsync(cts.Token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("⏱️ Subida de configuración timeout");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error subiendo configuración: {ex.Message}");
            }
        }

        // 🔥 NUEVO MÉTODO: Calcular tamaño total de una carpeta en Drive (recursivo)
        private async Task<long> CalcularTamañoDirectorioEnDrive(string idCarpeta, CancellationToken token = default)
        {
            long totalSize = 0;

            try
            {
                token.ThrowIfCancellationRequested();

                var request = _driveManager.Service.Files.List();
                request.Q = $"'{idCarpeta}' in parents and trashed = false";
                request.Fields = "files(id, name, mimeType, size)";

                var result = await request.ExecuteAsync(token);

                foreach (var item in result.Files)
                {
                    token.ThrowIfCancellationRequested();

                    if (item.MimeType == "application/vnd.google-apps.folder")
                    {
                        totalSize += await CalcularTamañoDirectorioEnDrive(item.Id, token);
                    }
                    else
                    {
                        totalSize += item.Size ?? 0;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("✋ Cálculo de tamaño cancelado");
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error calculando tamaño: {ex.Message}");
            }

            return totalSize;
        }

        private async Task DescargarDirectorioRecursivo(string idCarpetaDrive, string rutaLocalDestino,
           CancellationToken token, Action<string, long> onFileDownloaded)
        {
            token.ThrowIfCancellationRequested();

            // 🔥 Solicitar explícitamente el campo 'size'
            var request = _driveManager.Service.Files.List();
            request.Q = $"'{idCarpetaDrive}' in parents and trashed = false";
            request.Fields = "files(id, name, mimeType, size)"; // ⬅️ Crucial

            var result = await request.ExecuteAsync();

            foreach (var item in result.Files)
            {
                if (item.MimeType == "application/vnd.google-apps.folder")
                {
                    string nuevaRuta = Path.Combine(rutaLocalDestino, item.Name);
                    if (!Directory.Exists(nuevaRuta)) Directory.CreateDirectory(nuevaRuta);
                    await DescargarDirectorioRecursivo(item.Id, nuevaRuta, token, onFileDownloaded);
                }
                else
                {
                    string rutaArchivo = Path.Combine(rutaLocalDestino, item.Name);
                    long pesoArchivo = item.Size ?? 0;

                    await _driveManager.DescargarArchivo(item.Id, rutaArchivo, token);

                    // 🔥 Pasar tanto la ruta como el peso del archivo
                    onFileDownloaded?.Invoke(rutaArchivo, pesoArchivo);
                }
            }
        }
    }
}