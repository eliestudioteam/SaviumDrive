using SaviumDrive.Services;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SaviumDrive.Tests
{
    public class DriveManagerTests : IDisposable
    {
        private readonly string _tempDir;

        public DriveManagerTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "SaviumDriveTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        private string CrearArchivo(string nombre, string contenido)
        {
            string ruta = Path.Combine(_tempDir, nombre);
            File.WriteAllText(ruta, contenido);
            return ruta;
        }

        // --- Escapado de consultas ---

        [Theory]
        [InlineData("Baldur's Gate", "Baldur\\'s Gate")]
        [InlineData("Assassin's Creed", "Assassin\\'s Creed")]
        [InlineData("C:\\Saves", "C:\\\\Saves")]
        [InlineData("Normal", "Normal")]
        [InlineData(null, "")]
        public void EscaparQuery_EscapaApostrofosYBarras(string entrada, string esperado)
        {
            Assert.Equal(esperado, DriveManager.EscaparQuery(entrada));
        }

        [Fact]
        public void GetOrCreateFolder_ConApostrofo_EnviaConsultaEscapada()
        {
            var handler = new FakeDriveHandler(req =>
                req.Method == HttpMethod.Get
                    ? FakeDriveHandler.Json("{\"files\":[]}")
                    : FakeDriveHandler.Json("{\"id\":\"carpeta-nueva\"}"));
            var manager = new DriveManager(FakeDrive.CreateService(handler));

            string id = manager.GetOrCreateFolder("Baldur's Gate", "padre-1");

            Assert.Equal("carpeta-nueva", id);
            string q = FakeDrive.QueryParam(handler.Requests[0], "q");
            Assert.Contains("name = 'Baldur\\'s Gate'", q);
            Assert.Contains("'padre-1' in parents", q);
        }

        // --- Paginación ---

        [Fact]
        public async Task ListarArchivos_RecorreTodasLasPaginas()
        {
            var handler = new FakeDriveHandler(req =>
            {
                string pageToken = FakeDrive.QueryParam(req, "pageToken");
                return pageToken switch
                {
                    null => FakeDriveHandler.Json("{\"nextPageToken\":\"p2\",\"files\":[{\"id\":\"1\"},{\"id\":\"2\"}]}"),
                    "p2" => FakeDriveHandler.Json("{\"nextPageToken\":\"p3\",\"files\":[{\"id\":\"3\"}]}"),
                    "p3" => FakeDriveHandler.Json("{\"files\":[{\"id\":\"4\"}]}"),
                    _ => FakeDriveHandler.Error(HttpStatusCode.BadRequest)
                };
            });
            var manager = new DriveManager(FakeDrive.CreateService(handler));

            var archivos = await manager.ListarArchivos("'carpeta' in parents", "id, name");

            Assert.Equal(new[] { "1", "2", "3", "4" }, archivos.Select(f => f.Id));
            Assert.Equal(3, handler.Requests.Count);
            Assert.Contains("nextPageToken", FakeDrive.QueryParam(handler.Requests[0], "fields"));
        }

        [Fact]
        public async Task ListarContenidoCarpeta_MasDe100Elementos_DevuelveTodos()
        {
            // Simula una carpeta con 250 archivos repartidos en páginas de 100
            var handler = new FakeDriveHandler(req =>
            {
                int pagina = int.Parse(FakeDrive.QueryParam(req, "pageToken") ?? "0");
                int inicio = pagina * 100;
                int fin = Math.Min(inicio + 100, 250);
                var files = string.Join(",", Enumerable.Range(inicio, fin - inicio).Select(i => $"{{\"id\":\"f{i}\"}}"));
                string next = fin < 250 ? $"\"nextPageToken\":\"{pagina + 1}\"," : "";
                return FakeDriveHandler.Json($"{{{next}\"files\":[{files}]}}");
            });
            var manager = new DriveManager(FakeDrive.CreateService(handler));

            var archivos = await manager.ListarContenidoCarpeta("carpeta");

            Assert.Equal(250, archivos.Count);
            Assert.Equal(250, archivos.Select(f => f.Id).Distinct().Count());
        }

        // --- Papelera ---

        [Fact]
        public async Task EnviarAPapelera_MarcaComoTrashedSinBorrarDefinitivamente()
        {
            string cuerpo = null;
            var handler = new FakeDriveHandler(req =>
            {
                if (req.Method == HttpMethod.Patch) cuerpo = req.Content?.ReadAsStringAsync().Result;
                return FakeDriveHandler.Json("{\"id\":\"juego-1\",\"trashed\":true}");
            });
            var manager = new DriveManager(FakeDrive.CreateService(handler));

            await manager.EnviarAPapelera("juego-1");

            var peticion = Assert.Single(handler.Requests);
            Assert.Equal(HttpMethod.Patch, peticion.Method);
            Assert.EndsWith("/files/juego-1", peticion.RequestUri.AbsolutePath);
            Assert.Contains("\"trashed\":true", cuerpo);
            Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Delete);
        }

        [Fact]
        public async Task EliminarArchivoOCarpeta_BorraDefinitivamente()
        {
            var handler = new FakeDriveHandler(req => new HttpResponseMessage(HttpStatusCode.NoContent));
            var manager = new DriveManager(FakeDrive.CreateService(handler));

            await manager.EliminarArchivoOCarpeta("temp-1");

            Assert.Equal(HttpMethod.Delete, Assert.Single(handler.Requests).Method);
        }

        // --- Descargas ---

        [Fact]
        public async Task DescargarArchivo_Exito_EscribeElContenido()
        {
            var handler = new FakeDriveHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("partida guardada"))
            });
            var manager = new DriveManager(FakeDrive.CreateService(handler));
            string destino = Path.Combine(_tempDir, "save.dat");

            await manager.DescargarArchivo("archivo-1", destino, CancellationToken.None);

            Assert.Equal("partida guardada", File.ReadAllText(destino));
        }

        [Theory]
        [InlineData(HttpStatusCode.NotFound)]
        [InlineData(HttpStatusCode.Forbidden)]
        [InlineData(HttpStatusCode.InternalServerError)]
        public async Task DescargarArchivo_Fallo_LanzaExcepcion(HttpStatusCode status)
        {
            var handler = new FakeDriveHandler(req => FakeDriveHandler.Error(status));
            var manager = new DriveManager(FakeDrive.CreateService(handler));
            string destino = Path.Combine(_tempDir, "save.dat");

            await Assert.ThrowsAsync<IOException>(() => manager.DescargarArchivo("archivo-1", destino, CancellationToken.None));
        }

        // --- Subidas ---

        [Fact]
        public async Task SubirArchivo_Exito_NoLanzaExcepcion()
        {
            const string sesion = "https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&upload_id=abc";
            var handler = new FakeDriveHandler(req =>
            {
                if (req.Method == HttpMethod.Post)
                {
                    var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") };
                    r.Headers.Location = new Uri(sesion);
                    return r;
                }
                return FakeDriveHandler.Json("{\"id\":\"subido-1\",\"name\":\"save.dat\"}");
            });
            var manager = new DriveManager(FakeDrive.CreateService(handler));
            string archivo = CrearArchivo("save.dat", "datos");

            await manager.SubirArchivo(archivo, "carpeta-1", CancellationToken.None);

            Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Put && r.RequestUri.ToString() == sesion);
        }

        [Theory]
        [InlineData(HttpStatusCode.Forbidden)]
        [InlineData(HttpStatusCode.NotFound)]
        [InlineData(HttpStatusCode.BadRequest)]
        public async Task SubirArchivo_Fallo_LanzaExcepcion(HttpStatusCode status)
        {
            var handler = new FakeDriveHandler(req => FakeDriveHandler.Error(status));
            var manager = new DriveManager(FakeDrive.CreateService(handler));
            string archivo = CrearArchivo("save.dat", "datos");

            await Assert.ThrowsAsync<IOException>(() => manager.SubirArchivo(archivo, "carpeta-1", CancellationToken.None));
        }

        [Fact]
        public async Task SubirArchivo_ArchivoAbiertoPorElJuego_SePuedeLeer()
        {
            // El juego puede mantener el archivo abierto con escritura compartida
            string archivo = CrearArchivo("save.dat", "datos");
            const string sesion = "https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&upload_id=abc";
            var handler = new FakeDriveHandler(req =>
            {
                if (req.Method == HttpMethod.Post)
                {
                    var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") };
                    r.Headers.Location = new Uri(sesion);
                    return r;
                }
                return FakeDriveHandler.Json("{\"id\":\"subido-1\"}");
            });
            var manager = new DriveManager(FakeDrive.CreateService(handler));

            using (new FileStream(archivo, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
            {
                await manager.SubirArchivo(archivo, "carpeta-1", CancellationToken.None);
            }
        }

        [Fact]
        public async Task SubirArchivo_Cancelado_LanzaOperationCanceled()
        {
            var handler = new FakeDriveHandler(req => FakeDriveHandler.Error(HttpStatusCode.InternalServerError));
            var manager = new DriveManager(FakeDrive.CreateService(handler));
            string archivo = CrearArchivo("save.dat", "datos");
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.SubirArchivo(archivo, "carpeta-1", cts.Token));
            Assert.Empty(handler.Requests);
        }
    }
}
