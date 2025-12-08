using Google.Apis.Drive.v3;
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

        public string GetOrCreateFolder(string folderName, string parentId = null)
        {
            var request = Service.Files.List();
            string query = $"mimeType = 'application/vnd.google-apps.folder' and name = '{folderName}' and trashed = false";
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
                Parents = new List<string> { folderId }
            };

            using (var stream = new FileStream(rutaLocal, FileMode.Open))
            {
                var request = Service.Files.Create(fileMetadata, stream, "application/octet-stream");
                // Conectamos el token a la subida de Google
                await request.UploadAsync(token);
            }
        }

        public string BuscarCarpetaPorNombre(string nombre)
        {
            var request = Service.Files.List();
            request.Q =
                $"name = '{nombre}' and mimeType = 'application/vnd.google-apps.folder' and trashed = false";
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
        public async Task DescargarArchivo(string fileId, string rutaDestinoLocal, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var request = Service.Files.Get(fileId);
            using (var stream = new FileStream(rutaDestinoLocal, FileMode.Create))
            {
                await request.DownloadAsync(stream, token);
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

        public async Task EliminarArchivoOCarpeta(string fileId)
        {
            await Service.Files.Delete(fileId).ExecuteAsync();
        }

        public async Task<IList<Google.Apis.Drive.v3.Data.File>> ListarContenidoCarpeta(string folderId)
        {
            var request = Service.Files.List();
            request.Q = $"'{folderId}' in parents and trashed = false";
            request.Fields = "files(id, name, mimeType)";
            var result = await request.ExecuteAsync();
            return result.Files;
        }
    }
}