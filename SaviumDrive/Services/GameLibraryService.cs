using Newtonsoft.Json;
using SaviumDrive.Core;
using SaviumDrive.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SaviumDrive.Helpers;

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
        }

        private string GetStr(string key) => ResourceHelper.GetString(key);

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

                var ahora = DateTime.UtcNow;
                var nuevoJuego = new GameConfig
                {
                    Name = nombre,
                    LocalPath = rutaLocal,
                    DriveFolderId = idJuegoRaiz,
                    CoverUrl = coverUrl,
                    LastBackup = ahora
                };

                // V1 vive en la carpeta raíz del juego (igual que los backups anteriores a las versiones)
                var v1 = new GameVersion
                {
                    Number = 1,
                    CreatedAt = ahora,
                    LastBackup = ahora,
                    DriveFolderId = idJuegoRaiz
                };
                nuevoJuego.Versions.Add(v1);
                nuevoJuego.ActiveVersionId = v1.Id;
                nuevoJuego.LastVersionNumber = 1;

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
        public async Task<ResultadoActualizacion> ActualizarBackup(GameConfig juego, GameVersion version, CancellationToken token)
        {
            string idCarpetaTemporal = null;
            string nombreCarpetaOriginal = null;
            string nombreProgreso = $"{juego.Name} {version?.Label}";

            try
            {
                NotifyProgress(nombreProgreso, 0, "StatusUploading", GetStr("MsgPrepUpdate"), true);

                if (version == null || string.IsNullOrEmpty(version.DriveFolderId)) throw new Exception("ID Inválido");
                string idBase = version.DriveFolderId;

                // 1️⃣ OBTENER CARPETA ORIGINAL (la que contiene los archivos del juego)
                var itemsRaiz = await _driveManager.ListarContenidoCarpeta(idBase);
                var infoCarpetaLocal = new DirectoryInfo(juego.LocalPath);
                var carpetaOriginal = itemsRaiz.FirstOrDefault(i => i.Name == infoCarpetaLocal.Name && i.MimeType == "application/vnd.google-apps.folder");

                if (carpetaOriginal == null) throw new Exception("Carpeta original no encontrada en Drive");
                nombreCarpetaOriginal = carpetaOriginal.Name;

                // 2️⃣ COMPARAR CON DRIVE (ruta + tamaño + MD5): solo se sube lo que cambió
                NotifyProgress(nombreProgreso, -1, "StatusUploading", GetStr("MsgComparing"), true);
                var remoto = await _driveManager.ListarArbolArchivos(carpetaOriginal.Id, token);
                var local = await Task.Run(() => BackupDiff.LeerCarpetaLocal(juego.LocalPath), token);
                var huellasRemotas = remoto.ToDictionary(
                    par => par.Key,
                    par => new HuellaArchivo(par.Value.Size ?? -1, par.Value.Md5Checksum),
                    StringComparer.OrdinalIgnoreCase);
                var diferencias = BackupDiff.Calcular(local, huellasRemotas);

                var resultado = new ResultadoActualizacion
                {
                    SinCambios = diferencias.SinCambios.Count,
                    Subidos = diferencias.Modificados.Count + diferencias.Nuevos.Count,
                    Eliminados = diferencias.Eliminados.Count
                };

                if (diferencias.YaAlDia)
                {
                    // Nada que subir: el backup ya coincide con el PC
                    resultado.YaAlDia = true;
                    await MarcarGuardado(juego, version);
                    return resultado;
                }

                // 3️⃣ CREAR CARPETA TEMPORAL EN DRIVE (aquí se arma el nuevo backup)
                // Borrar restos de intentos anteriores para no mezclar archivos viejos
                string nombreTemporal = $"TEMP_{nombreCarpetaOriginal}";
                foreach (var resto in itemsRaiz.Where(i => i.Name == nombreTemporal && i.MimeType == "application/vnd.google-apps.folder"))
                {
                    await _driveManager.EliminarArchivoOCarpeta(resto.Id);
                }
                idCarpetaTemporal = _driveManager.GetOrCreateFolder(nombreTemporal, idBase);

                // 4️⃣ ARMAR EL NUEVO BACKUP: los archivos sin cambios se copian dentro de Drive
                // (sin volver a subirlos) y solo se suben los nuevos o modificados
                long totalBytes = local.Values.Sum(h => h.Tamano);
                long bytesListos = 0;
                string totalStr = FormatBytes(totalBytes);
                var sinCambios = new HashSet<string>(diferencias.SinCambios, StringComparer.OrdinalIgnoreCase);

                // Misma estructura de carpetas (también las vacías); los padres van antes que los hijos
                var carpetasDrive = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [""] = idCarpetaTemporal };
                foreach (var dir in Directory.GetDirectories(juego.LocalPath, "*", SearchOption.AllDirectories).OrderBy(d => d.Length))
                {
                    token.ThrowIfCancellationRequested();
                    string rel = BackupDiff.NormalizarRuta(Path.GetRelativePath(juego.LocalPath, dir));
                    carpetasDrive[rel] = _driveManager.GetOrCreateFolder(Path.GetFileName(dir), carpetasDrive[CarpetaPadre(rel)]);
                }

                foreach (var par in local)
                {
                    token.ThrowIfCancellationRequested();
                    string rel = par.Key;
                    string nombreArchivo = rel.Contains('/') ? rel.Substring(rel.LastIndexOf('/') + 1) : rel;
                    string idPadre = carpetasDrive[CarpetaPadre(rel)];

                    if (sinCambios.Contains(rel))
                    {
                        var original = remoto[rel];
                        await _driveManager.CopiarArchivo(original.Id, idPadre, nombreArchivo, original.ModifiedTimeDateTimeOffset, token);
                    }
                    else
                    {
                        string rutaLocal = Path.Combine(juego.LocalPath, rel.Replace('/', Path.DirectorySeparatorChar));
                        await Task.Run(() => _driveManager.SubirArchivo(rutaLocal, idPadre, token));
                    }

                    bytesListos += par.Value.Tamano;
                    int porcentaje = totalBytes > 0 ? (int)((double)bytesListos / totalBytes * 100) : 0;
                    string progresoStr = $"{FormatBytes(bytesListos)} / {totalStr}";
                    NotifyProgress(nombreProgreso, porcentaje, "StatusUploading", string.Format(GetStr("MsgUploadingSize"), nombreArchivo, progresoStr), true);
                }

                // 5️⃣ TODO BIEN: Intercambiar carpetas sin perder el backup viejo si algo falla
                // a) Apartar la vieja con otro nombre
                await _driveManager.RenombrarArchivo(carpetaOriginal.Id, $"OLD_{nombreCarpetaOriginal}");
                try
                {
                    // b) La temporal pasa a ser la oficial
                    await _driveManager.RenombrarArchivo(idCarpetaTemporal, nombreCarpetaOriginal);
                }
                catch
                {
                    // Revertir: la vieja vuelve a su nombre
                    try { await _driveManager.RenombrarArchivo(carpetaOriginal.Id, nombreCarpetaOriginal); } catch { }
                    throw;
                }
                idCarpetaTemporal = null; // Ya no es temporal, no borrarla en el rollback

                // c) Borrar la vieja (si falla, solo queda una carpeta OLD_ sobrante)
                try { await _driveManager.EliminarArchivoOCarpeta(carpetaOriginal.Id); } catch { }

                // 6️⃣ Actualizar fecha (de la versión y del juego)
                await MarcarGuardado(juego, version);
                return resultado;
            }
            catch (Exception)
            {
                // 🔥 ROLLBACK (cancelación o error): Eliminar carpeta temporal (archivos viejos permanecen intactos)
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


        /// <summary>Guarda la fecha del último backup (de la versión y del juego).</summary>
        private async Task MarcarGuardado(GameConfig juego, GameVersion version)
        {
            var ahora = DateTime.UtcNow;
            juego.LastBackup = ahora;
            version.LastBackup = ahora;
            await ModificarJuego(juego.Id, j =>
            {
                j.LastBackup = ahora;
                var v = j.FindVersion(version.Id);
                if (v != null) v.LastBackup = ahora;
            });
        }

        /// <summary>"a/b/c.sav" → "a/b"; "c.sav" → "".</summary>
        private static string CarpetaPadre(string rutaRelativa)
        {
            int i = rutaRelativa.LastIndexOf('/');
            return i < 0 ? "" : rutaRelativa.Substring(0, i);
        }

        // 🔥 RESTAURAR JUEGO CON CARPETA TEMPORAL LOCAL Y PROGRESO DETALLADO
        public async Task RestaurarJuego(GameConfig juego, GameVersion version, CancellationToken token)
        {
            string rutaCarpetaTemporal = null;
            string rutaCarpetaOriginal = juego.LocalPath;
            string nombreCarpetaTemporal = $"TEMP_{new DirectoryInfo(juego.LocalPath).Name}";
            string nombreProgreso = $"{juego.Name} {version?.Label}";

            try
            {
                NotifyProgress(nombreProgreso, 0, "StatusDownloading", GetStr("MsgConnectingDrive"), true);

                if (version == null || string.IsNullOrEmpty(version.DriveFolderId)) throw new Exception("ID inválido");

                // 1️⃣ CREAR CARPETA TEMPORAL LOCAL (junto a la carpeta original)
                string directorioContenedor = Path.GetDirectoryName(juego.LocalPath);
                if (string.IsNullOrEmpty(directorioContenedor)) throw new Exception("Ruta local inválida");
                rutaCarpetaTemporal = Path.Combine(directorioContenedor, nombreCarpetaTemporal);

                // Si ya existe la carpeta temporal (de un intento previo), eliminarla
                if (Directory.Exists(rutaCarpetaTemporal))
                {
                    Directory.Delete(rutaCarpetaTemporal, true);
                }
                Directory.CreateDirectory(rutaCarpetaTemporal);

                // 2️⃣ OBTENER CARPETA ORIGEN Y CALCULAR TAMAÑO TOTAL
                var itemsRaiz = await _driveManager.ListarContenidoCarpeta(version.DriveFolderId);
                var nombreCarpetaLocal = new DirectoryInfo(juego.LocalPath).Name;
                var carpetaContenedora = itemsRaiz.FirstOrDefault(i => i.Name == nombreCarpetaLocal && i.MimeType == "application/vnd.google-apps.folder");

                // Sin respaldo a la carpeta del juego: en V1 contiene también las otras versiones
                if (carpetaContenedora == null) throw new Exception("El backup de esta versión no se encontró en Drive");
                string idOrigen = carpetaContenedora.Id;

                // 🔥 CALCULAR TAMAÑO TOTAL ANTES DE DESCARGAR
                long totalBytes = await CalcularTamañoDirectorioEnDrive(idOrigen);
                long bytesDescargados = 0;
                int archivosDescargados = 0;
                string totalStr = FormatBytes(totalBytes);

                NotifyProgress(nombreProgreso, 0, "StatusDownloading", GetStr("MsgScanning"), true);

                // 3️⃣ DESCARGAR ARCHIVOS A LA CARPETA TEMPORAL CON PROGRESO
                await DescargarDirectorioRecursivo(idOrigen, rutaCarpetaTemporal, token, (rutaArchivo, pesoArchivo) =>
                {
                    token.ThrowIfCancellationRequested();
                    archivosDescargados++;
                    bytesDescargados += pesoArchivo;
                    int porcentaje = totalBytes > 0 ? (int)((double)bytesDescargados / totalBytes * 100) : 0;
                    string progresoStr = $"{FormatBytes(bytesDescargados)} / {totalStr}";
                    string nombreArchivo = Path.GetFileName(rutaArchivo);

                    NotifyProgress(nombreProgreso, porcentaje, "StatusDownloading",
                        string.Format(GetStr("MsgDownloadingSize"), nombreArchivo, progresoStr), true);
                });

                // 🛡️ No reemplazar las partidas locales con un backup vacío
                if (archivosDescargados == 0) throw new Exception("El backup en Drive está vacío o no se encontró");

                // 4️⃣ TODO BIEN: Intercambiar carpetas sin borrar nada antes de tiempo
                string rutaCarpetaVieja = Path.Combine(directorioContenedor, $"OLD_{nombreCarpetaLocal}");
                if (Directory.Exists(rutaCarpetaVieja)) Directory.Delete(rutaCarpetaVieja, true);

                // a) Apartar la original (si hay archivos bloqueados por el juego, falla aquí sin tocar nada)
                bool habiaOriginal = Directory.Exists(rutaCarpetaOriginal);
                if (habiaOriginal) Directory.Move(rutaCarpetaOriginal, rutaCarpetaVieja);

                try
                {
                    // b) La temporal pasa a ser la oficial
                    Directory.Move(rutaCarpetaTemporal, rutaCarpetaOriginal);
                }
                catch
                {
                    // Revertir: la original vuelve a su sitio
                    if (habiaOriginal) { try { Directory.Move(rutaCarpetaVieja, rutaCarpetaOriginal); } catch { } }
                    throw;
                }

                // c) Borrar la vieja (si falla, solo queda una carpeta OLD_ sobrante)
                if (habiaOriginal) { try { Directory.Delete(rutaCarpetaVieja, true); } catch { } }
            }
            catch (Exception)
            {
                // 🔥 ROLLBACK (cancelación o error): Eliminar carpeta temporal (archivos viejos permanecen intactos)
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
                    if (container != null && container.Games != null) return MigrarVersiones(container.Games);
                    var listaVieja = JsonConvert.DeserializeObject<List<GameConfig>>(json);
                    return MigrarVersiones(listaVieja ?? new List<GameConfig>());
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
                request.Fields = "files(id, modifiedTime)";
                request.OrderBy = "modifiedTime desc"; // Si hay duplicados, usar la más reciente
                var result = await request.ExecuteAsync(token);

                if (result.Files.Count > 0)
                {
                    string fileId = result.Files[0].Id;
                    string jsonNube = "";

                    using (var stream = new MemoryStream())
                    {
                        var progreso = await _driveManager.Service.Files.Get(fileId).DownloadAsync(stream, token);
                        if (progreso.Status != Google.Apis.Download.DownloadStatus.Completed)
                        {
                            token.ThrowIfCancellationRequested();
                            throw new IOException("No se pudo descargar la configuración de la nube", progreso.Exception);
                        }
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

                    var fusion = LibraryMerge.Fusionar(juegosLocales, fechaLocal,
                                                       nubeContainer.Games, nubeContainer.LastUpdated);

                    if (fusion.UsarNube)
                    {
                        await GuardarEnDisco(nubeContainer.Games, nubeContainer.LastUpdated);
                    }

                    if (fusion.HayCambios)
                    {
                        await GuardarLibreria(fusion.Juegos);
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
        // ===================== VERSIONES =====================

        private static List<GameConfig> MigrarVersiones(List<GameConfig> juegos)
        {
            foreach (var j in juegos) j?.EnsureVersions();
            return juegos;
        }

        /// <summary>Carga la librería, aplica el cambio al juego indicado y la guarda (local + nube).</summary>
        private async Task<GameConfig> ModificarJuego(string juegoId, Action<GameConfig> cambio)
        {
            var juegos = await CargarSoloLocal();
            var existente = juegos.FirstOrDefault(j => j.Id == juegoId);
            if (existente == null) return null;

            cambio(existente);
            await GuardarLibreria(juegos);
            return existente;
        }

        /// <summary>Sube la carpeta local como una versión nueva (V2, V3...) y la deja como activa.</summary>
        public async Task<GameVersion> CrearNuevaVersion(GameConfig juego, string descripcion, CancellationToken token)
        {
            string idCarpetaVersion = null;
            int numero = juego.NextVersionNumber;
            string nombreProgreso = $"{juego.Name} V{numero}";

            try
            {
                NotifyProgress(nombreProgreso, 0, "StatusUploading", GetStr("MsgScanning"), true);

                if (string.IsNullOrEmpty(juego.DriveFolderId)) throw new Exception("ID Inválido");
                if (!Directory.Exists(juego.LocalPath)) throw new DirectoryNotFoundException(juego.LocalPath);

                // Drive: <Juego>/Version_N/<carpeta de partidas>
                idCarpetaVersion = _driveManager.GetOrCreateFolder($"Version_{numero}", juego.DriveFolderId);
                string idContenido = _driveManager.GetOrCreateFolder(new DirectoryInfo(juego.LocalPath).Name, idCarpetaVersion);

                long totalBytes = GetDirectorySize(juego.LocalPath);
                long bytesSubidos = 0;
                string totalStr = FormatBytes(totalBytes);

                await SubirDirectorioRecursivo(juego.LocalPath, idContenido, token, (nombreArchivo, pesoArchivo) =>
                {
                    token.ThrowIfCancellationRequested();
                    bytesSubidos += pesoArchivo;
                    int porcentaje = totalBytes > 0 ? (int)((double)bytesSubidos / totalBytes * 100) : 0;
                    string progresoStr = $"{FormatBytes(bytesSubidos)} / {totalStr}";

                    NotifyProgress(nombreProgreso, porcentaje, "StatusUploading", string.Format(GetStr("MsgUploadingSize"), nombreArchivo, progresoStr), true);
                });

                var ahora = DateTime.UtcNow;
                var nueva = new GameVersion
                {
                    Number = numero,
                    Description = (descripcion ?? "").Trim(),
                    CreatedAt = ahora,
                    LastBackup = ahora,
                    DriveFolderId = idCarpetaVersion
                };

                await ModificarJuego(juego.Id, j =>
                {
                    j.Versions.Add(nueva);
                    j.ActiveVersionId = nueva.Id;
                    j.LastVersionNumber = Math.Max(j.LastVersionNumber, numero);
                    j.LastBackup = ahora;
                });

                return nueva;
            }
            catch (Exception)
            {
                // ROLLBACK (cancelación o error): no dejar una versión a medias en Drive
                if (!string.IsNullOrEmpty(idCarpetaVersion))
                {
                    try { await _driveManager.EliminarArchivoOCarpeta(idCarpetaVersion); } catch { }
                }
                throw;
            }
            finally
            {
                NotifyProgress("", 0, "", "", false);
            }
        }

        /// <summary>
        /// Borra una sola versión. Si es la única, se elimina el juego completo.
        /// Devuelve true si se eliminó el juego entero.
        /// </summary>
        public async Task<bool> EliminarVersion(GameConfig juego, GameVersion version)
        {
            if (juego.Versions == null || juego.Versions.Count <= 1)
            {
                await EliminarJuego(juego);
                return true;
            }

            try
            {
                NotifyProgress($"{juego.Name} {version.Label}", -1, "StatusDeleting", GetStr("MsgDeleting"), true);

                if (version.DriveFolderId == juego.DriveFolderId)
                {
                    // V1 vive en la raíz del juego junto a las demás versiones:
                    // borrar todo lo de la raíz excepto las carpetas de las otras versiones
                    var idsOtrasVersiones = juego.Versions
                        .Where(v => v.Id != version.Id)
                        .Select(v => v.DriveFolderId)
                        .ToHashSet();

                    var items = await _driveManager.ListarContenidoCarpeta(juego.DriveFolderId);
                    foreach (var item in items.Where(i => !idsOtrasVersiones.Contains(i.Id)))
                    {
                        await _driveManager.EliminarArchivoOCarpeta(item.Id);
                    }
                }
                else if (!string.IsNullOrEmpty(version.DriveFolderId))
                {
                    await _driveManager.EliminarArchivoOCarpeta(version.DriveFolderId);
                }
            }
            finally
            {
                NotifyProgress("", 0, "", "", false);
            }

            await ModificarJuego(juego.Id, j =>
            {
                j.Versions.RemoveAll(v => v.Id == version.Id);
                if (j.ActiveVersionId == version.Id)
                {
                    // Pasar a la versión más reciente que queda
                    j.ActiveVersionId = j.Versions.OrderByDescending(v => v.Number).First().Id;
                }
            });

            return false;
        }

        public async Task ActualizarDescripcion(GameConfig juego, GameVersion version, string descripcion)
        {
            string texto = (descripcion ?? "").Trim();
            version.Description = texto;
            await ModificarJuego(juego.Id, j =>
            {
                var v = j.FindVersion(version.Id);
                if (v != null) v.Description = texto;
            });
        }

        public async Task CambiarVersionActiva(GameConfig juego, GameVersion version)
        {
            juego.ActiveVersionId = version.Id;
            await ModificarJuego(juego.Id, j =>
            {
                if (j.FindVersion(version.Id) != null) j.ActiveVersionId = version.Id;
            });
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

                    // 🔥 Primero subir la nueva y luego borrar las viejas (si la subida falla, la nube conserva la anterior)
                    var fileMetadata = new Google.Apis.Drive.v3.Data.File()
                    {
                        Name = CLOUD_CONFIG_FILE,
                        Parents = new List<string> { idMadre },
                        MimeType = "application/json"
                    };

                    using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(jsonContent)))
                    {
                        var createReq = _driveManager.Service.Files.Create(fileMetadata, stream, "application/json");
                        var progreso = await createReq.UploadAsync(cts.Token);
                        if (progreso.Status != Google.Apis.Upload.UploadStatus.Completed)
                        {
                            cts.Token.ThrowIfCancellationRequested();
                            throw new IOException("No se pudo subir la configuración", progreso.Exception);
                        }
                    }

                    foreach (var f in result.Files)
                    {
                        try
                        {
                            await _driveManager.EliminarArchivoOCarpeta(f.Id);
                        }
                        catch { }
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

                var archivos = await _driveManager.ListarArchivos(
                    $"'{idCarpeta}' in parents and trashed = false", "id, name, mimeType, size", token);

                foreach (var item in archivos)
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

            // 🔥 Solicitar explícitamente el campo 'size' (y todas las páginas)
            var archivos = await _driveManager.ListarArchivos(
                $"'{idCarpetaDrive}' in parents and trashed = false", "id, name, mimeType, size", token);

            foreach (var item in archivos)
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