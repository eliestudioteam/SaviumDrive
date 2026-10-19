using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Google.Apis.Upload;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SaviumDrive.Services
{
    public class DriveManager
    {
        public DriveService Service { get; private set; }

        public DriveManager(DriveService service)
        {
            Service = service;
        }

        // 🔥 Escapar valores para consultas 'q' de Drive (ej: "Assassin's Creed")
        public static string EscaparQuery(string valor)
        {
            return (valor ?? "").Replace("\\", "\\\\").Replace("'", "\\'");
        }

        public string GetOrCreateFolder(string folderName, string parentId = null)
        {
            var request = Service.Files.List();
            string query = $"mimeType = 'application/vnd.google-apps.folder' and name = '{EscaparQuery(folderName)}' and trashed = false";
            if (parentId != null) query += $" and '{parentId}' in parents";
            request.Q = query;
            var result = request.Execute();
            if (result.Files.Count > 0) return result.Files[0].Id;

            var fileMetadata = new Google.Apis.Drive.v3.Data.File()
            {
                Name = folderName,
                MimeType = "application/vnd.google-apps.folder"
            };
            if (parentId != null) fileMetadata.Parents = new List<string> { parentId };
            return Service.Files.Create(fileMetadata).Execute().Id;
        }

        // --- SUBIDA CANCELABLE ---
        public async Task SubirArchivo(string rutaLocal, string folderId, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); // Verificar antes de empezar

            var fileMetadata = new Google.Apis.Drive.v3.Data.File()
            {
                Name = Path.GetFileName(rutaLocal),
                Parents = new List<string> { folderId },
                // Conservar la fecha real del archivo (no la de subida) para poder comparar con el PC
                ModifiedTimeDateTimeOffset = new DateTimeOffset(File.GetLastWriteTimeUtc(rutaLocal), TimeSpan.Zero)
            };

            // FileShare.ReadWrite: el juego puede tener el archivo abierto
            using (var stream = new FileStream(rutaLocal, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var request = Service.Files.Create(fileMetadata, stream, "application/octet-stream");
                // Conectamos el token a la subida de Google
                var progreso = await request.UploadAsync(token);

                // 🔥 UploadAsync NO lanza excepción al fallar: hay que revisar el estado
                if (progreso.Status != UploadStatus.Completed)
                {
                    token.ThrowIfCancellationRequested();
                    throw new IOException($"No se pudo subir '{Path.GetFileName(rutaLocal)}'", progreso.Exception);
                }
            }
        }

        public string BuscarCarpetaPorNombre(string nombre)
        {
            var request = Service.Files.List();
            request.Q =
                $"name = '{EscaparQuery(nombre)}' and mimeType = 'application/vnd.google-apps.folder' and trashed = false";
            request.Fields = "files(id, name)";
            var result = request.Execute();

            if (result.Files.Count > 0)
                return result.Files[0].Id;

            return null;
        }

        public async Task RenombrarArchivo(string fileId, string nuevoNombre)
        {
            var file = new Google.Apis.Drive.v3.Data.File
            {
                Name = nuevoNombre
            };

            var request = Service.Files.Update(file, fileId);
            await request.ExecuteAsync();

        }
        // --- DESCARGA CANCELABLE ---
        public async Task DescargarArchivo(string fileId, string rutaDestinoLocal, CancellationToken token, DateTimeOffset? fechaOriginal = null)
        {
            token.ThrowIfCancellationRequested();
            var request = Service.Files.Get(fileId);
            using (var stream = new FileStream(rutaDestinoLocal, FileMode.Create))
            {
                var progreso = await request.DownloadAsync(stream, token);

                // 🔥 DownloadAsync NO lanza excepción al fallar: hay que revisar el estado
                if (progreso.Status != DownloadStatus.Completed)
                {
                    token.ThrowIfCancellationRequested();
                    throw new IOException($"No se pudo descargar '{Path.GetFileName(rutaDestinoLocal)}'", progreso.Exception);
                }
            }

            // Devolver al archivo su fecha original (si no, parecería recién modificado)
            if (fechaOriginal.HasValue)
            {
                try { File.SetLastWriteTimeUtc(rutaDestinoLocal, fechaOriginal.Value.UtcDateTime); } catch { }
            }
        }

        // Agregar este método en DriveManager.cs
        public async Task MoverArchivo(string fileId, string nuevoParentId)
        {
            var file = await Service.Files.Get(fileId).ExecuteAsync();
            var padresAntiguos = string.Join(",", file.Parents);

            var updateRequest = Service.Files.Update(new Google.Apis.Drive.v3.Data.File(), fileId);
            updateRequest.AddParents = nuevoParentId;
            updateRequest.RemoveParents = padresAntiguos;
            updateRequest.Fields = "id, parents";

            await updateRequest.ExecuteAsync();
        }

        /// <summary>Borrado permanente: solo para restos internos (TEMP_, OLD_, configuración vieja).</summary>
        public async Task EliminarArchivoOCarpeta(string fileId)
        {
            await Service.Files.Delete(fileId).ExecuteAsync();
        }

        /// <summary>
        /// Mueve a la papelera de Drive (se puede recuperar durante 30 días).
        /// Para lo que elimina el usuario: juegos y versiones.
        /// </summary>
        public async Task EnviarAPapelera(string fileId)
        {
            var request = Service.Files.Update(new Google.Apis.Drive.v3.Data.File { Trashed = true }, fileId);
            request.Fields = "id, trashed";
            await request.ExecuteAsync();
        }

        // 🔥 Lista TODAS las páginas (Drive devuelve máximo 100 por defecto)
        public async Task<List<Google.Apis.Drive.v3.Data.File>> ListarArchivos(string query, string campos, CancellationToken token = default)
        {
            var todos = new List<Google.Apis.Drive.v3.Data.File>();
            string pageToken = null;

            do
            {
                token.ThrowIfCancellationRequested();

                var request = Service.Files.List();
                request.Q = query;
                request.Fields = $"nextPageToken, files({campos})";
                request.PageSize = 1000;
                request.PageToken = pageToken;

                var result = await request.ExecuteAsync(token);
                if (result.Files != null) todos.AddRange(result.Files);
                pageToken = result.NextPageToken;
            }
            while (!string.IsNullOrEmpty(pageToken));

            return todos;
        }

        public async Task<IList<Google.Apis.Drive.v3.Data.File>> ListarContenidoCarpeta(string folderId, CancellationToken token = default)
        {
            return await ListarArchivos($"'{folderId}' in parents and trashed = false", "id, name, mimeType", token);
        }

        /// <summary>
        /// Todos los archivos bajo una carpeta (recursivo), con tamaño y MD5.
        /// Clave: ruta relativa con '/' (ej. "perfil/slot1.sav").
        /// </summary>
        public async Task<Dictionary<string, Google.Apis.Drive.v3.Data.File>> ListarArbolArchivos(string folderId, CancellationToken token = default)
        {
            var archivos = new Dictionary<string, Google.Apis.Drive.v3.Data.File>(StringComparer.OrdinalIgnoreCase);
            await ListarArbolRecursivo(folderId, "", archivos, token);
            return archivos;
        }

        private async Task ListarArbolRecursivo(string folderId, string prefijo, Dictionary<string, Google.Apis.Drive.v3.Data.File> archivos, CancellationToken token)
        {
            var items = await ListarArchivos($"'{folderId}' in parents and trashed = false",
                                             "id, name, mimeType, size, md5Checksum, modifiedTime", token);
            foreach (var item in items)
            {
                string ruta = prefijo + item.Name;
                if (item.MimeType == "application/vnd.google-apps.folder")
                    await ListarArbolRecursivo(item.Id, ruta + "/", archivos, token);
                else
                    archivos[ruta] = item;
            }
        }

        /// <summary>Copia un archivo dentro de Drive (sin volver a subirlo), conservando su fecha.</summary>
        public async Task CopiarArchivo(string fileId, string carpetaDestinoId, string nombre, DateTimeOffset? fechaModificacion, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var copia = new Google.Apis.Drive.v3.Data.File
            {
                Name = nombre,
                Parents = new List<string> { carpetaDestinoId },
                ModifiedTimeDateTimeOffset = fechaModificacion
            };
            var request = Service.Files.Copy(copia, fileId);
            request.Fields = "id";
            await request.ExecuteAsync(token);
        }
    }
}
