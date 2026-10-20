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

        // --- Árbol de archivos y copias (subida incremental) ---

        [Fact]
        public async Task ListarArbolArchivos_RecorreSubcarpetasConRutasRelativas()
        {
            var handler = new FakeDriveHandler(req =>
            {
                string q = FakeDrive.QueryParam(req, "q") ?? "";
                if (q.Contains("'raiz' in parents"))
                    return FakeDriveHandler.Json("{\"files\":[" +
                        "{\"id\":\"f1\",\"name\":\"config.ini\",\"mimeType\":\"text/plain\",\"size\":\"3\",\"md5Checksum\":\"aaa\"}," +
                        "{\"id\":\"d1\",\"name\":\"perfil\",\"mimeType\":\"application/vnd.google-apps.folder\"}]}");
                if (q.Contains("'d1' in parents"))
                    return FakeDriveHandler.Json("{\"files\":[{\"id\":\"f2\",\"name\":\"slot1.sav\",\"mimeType\":\"application/octet-stream\",\"size\":\"20\",\"md5Checksum\":\"bbb\"}]}");
                return FakeDriveHandler.Json("{\"files\":[]}");
            });
            var manager = new DriveManager(FakeDrive.CreateService(handler));

            var arbol = await manager.ListarArbolArchivos("raiz");

            Assert.Equal(2, arbol.Count);
            Assert.Equal("aaa", arbol["config.ini"].Md5Checksum);
            Assert.Equal(20, arbol["perfil/slot1.sav"].Size);
            Assert.Contains("md5Checksum", FakeDrive.QueryParam(handler.Requests[0], "fields"));
        }

        [Fact]
        public async Task CopiarArchivo_CopiaDentroDeDriveSinSubirContenido()
        {
            string cuerpo = null;
            var handler = new FakeDriveHandler(req =>
            {
                cuerpo = req.Content?.ReadAsStringAsync().Result;
                return FakeDriveHandler.Json("{\"id\":\"copia-1\"}");
            });
            var manager = new DriveManager(FakeDrive.CreateService(handler));
            var fecha = new DateTimeOffset(2026, 3, 14, 9, 30, 0, TimeSpan.Zero);

            await manager.CopiarArchivo("original-1", "temp-1", "slot1.sav", fecha, CancellationToken.None);

            var peticion = Assert.Single(handler.Requests);
            Assert.Equal(HttpMethod.Post, peticion.Method);
            Assert.EndsWith("/files/original-1/copy", peticion.RequestUri.AbsolutePath);
            Assert.DoesNotContain("upload", peticion.RequestUri.AbsolutePath);
            Assert.Contains("\"parents\":[\"temp-1\"]", cuerpo);
            Assert.Contains("\"name\":\"slot1.sav\"", cuerpo);
            Assert.Contains("\"modifiedTime\":\"2026-03-14T09:30:00", cuerpo);
        }

        // --- Tamaño de carpetas (historial de versiones) ---

        /// <summary>
        /// Carpeta raíz de un juego con V1 dentro (saves/) junto a otra versión (Version_2/) y restos TEMP_/OLD_.
        /// </summary>
        private static FakeDriveHandler DriveConVersiones() => new FakeDriveHandler(req =>
        {
            string q = FakeDrive.QueryParam(req, "q") ?? "";
            if (q.Contains("'raiz' in parents"))
                return FakeDriveHandler.Json("{\"files\":[" +
                    "{\"id\":\"saves\",\"name\":\"saves\",\"mimeType\":\"application/vnd.google-apps.folder\"}," +
                    "{\"id\":\"v2\",\"name\":\"Version_2\",\"mimeType\":\"application/vnd.google-apps.folder\"}," +
                    "{\"id\":\"temp\",\"name\":\"TEMP_saves\",\"mimeType\":\"application/vnd.google-apps.folder\"}," +
                    "{\"id\":\"suelto\",\"name\":\"leeme.txt\",\"mimeType\":\"text/plain\",\"size\":\"5\"}]}");
            if (q.Contains("'saves' in parents"))
                return FakeDriveHandler.Json("{\"files\":[" +
                    "{\"id\":\"a\",\"name\":\"a.sav\",\"mimeType\":\"application/octet-stream\",\"size\":\"1000\"}," +
                    "{\"id\":\"sub\",\"name\":\"perfil\",\"mimeType\":\"application/vnd.google-apps.folder\"}]}");
            if (q.Contains("'sub' in parents"))
                return FakeDriveHandler.Json("{\"files\":[{\"id\":\"b\",\"name\":\"b.sav\",\"mimeType\":\"application/octet-stream\",\"size\":\"250\"}]}");
            if (q.Contains("'v2' in parents"))
                return FakeDriveHandler.Json("{\"files\":[{\"id\":\"c\",\"name\":\"c.sav\",\"mimeType\":\"application/octet-stream\",\"size\":\"9000\"}]}");
            if (q.Contains("'temp' in parents"))
                return FakeDriveHandler.Json("{\"files\":[{\"id\":\"d\",\"name\":\"d.sav\",\"mimeType\":\"application/octet-stream\",\"size\":\"777\"}]}");
            return FakeDriveHandler.Json("{\"files\":[]}");
        });

        [Fact]
        public async Task CalcularTamanoCarpeta_SumaArchivosYSubcarpetas()
        {
            var manager = new DriveManager(FakeDrive.CreateService(DriveConVersiones()));

            Assert.Equal(1250, await manager.CalcularTamanoCarpeta("saves"));
            Assert.Equal(9000, await manager.CalcularTamanoCarpeta("v2"));
        }

        [Fact]
        public async Task CalcularTamanoCarpeta_FiltroEnRaiz_ExcluyeOtrasVersionesYRestos()
        {
            var manager = new DriveManager(FakeDrive.CreateService(DriveConVersiones()));

            // Tamaño de V1 (en la raíz del juego): sin Version_2 ni TEMP_
            long v1 = await manager.CalcularTamanoCarpeta("raiz", item =>
                item.Id != "v2" && !item.Name.StartsWith("TEMP_") && !item.Name.StartsWith("OLD_"));

            Assert.Equal(1000 + 250 + 5, v1);
        }

        [Fact]
        public async Task CalcularTamanoCarpeta_SinFiltro_CuentaTodo()
        {
            var manager = new DriveManager(FakeDrive.CreateService(DriveConVersiones()));
            Assert.Equal(1000 + 250 + 9000 + 777 + 5, await manager.CalcularTamanoCarpeta("raiz"));
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

        [Fact]
        public async Task DescargarArchivo_ConFechaOriginal_LaRestauraEnElArchivo()
        {
            var handler = new FakeDriveHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("partida"))
            });
            var manager = new DriveManager(FakeDrive.CreateService(handler));
            string destino = Path.Combine(_tempDir, "save.dat");
            var original = new DateTimeOffset(2026, 3, 14, 9, 30, 0, TimeSpan.Zero);

            await manager.DescargarArchivo("archivo-1", destino, CancellationToken.None, original);

            // Sin esto el archivo tendría la fecha de la descarga y parecería más nuevo que el backup
            Assert.Equal(original.UtcDateTime, File.GetLastWriteTimeUtc(destino));
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

        [Fact]
        public async Task SubirArchivo_AvisaDeLosBytesEnviados()
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
                return FakeDriveHandler.Json("{\"id\":\"subido-1\"}");
            });
            var manager = new DriveManager(FakeDrive.CreateService(handler));
            string archivo = CrearArchivo("save.dat", "datos del juego");
            var avisos = new System.Collections.Generic.List<long>();

            await manager.SubirArchivo(archivo, "carpeta-1", CancellationToken.None, b => avisos.Add(b));

            Assert.NotEmpty(avisos);
            Assert.Equal(new FileInfo(archivo).Length, avisos[avisos.Count - 1]);
        }

        [Fact]
        public async Task SubirArchivo_EnviaLaFechaOriginalDelArchivo()
        {
            const string sesion = "https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&upload_id=abc";
            string metadatos = null;
            var handler = new FakeDriveHandler(req =>
            {
                if (req.Method == HttpMethod.Post)
                {
                    metadatos = req.Content?.ReadAsStringAsync().Result;
                    var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") };
                    r.Headers.Location = new Uri(sesion);
                    return r;
                }
                return FakeDriveHandler.Json("{\"id\":\"subido-1\"}");
            });
            var manager = new DriveManager(FakeDrive.CreateService(handler));
            string archivo = CrearArchivo("save.dat", "datos");
            File.SetLastWriteTimeUtc(archivo, new DateTime(2026, 3, 14, 9, 30, 0, DateTimeKind.Utc));

            await manager.SubirArchivo(archivo, "carpeta-1", CancellationToken.None);

            Assert.NotNull(metadatos);
            Assert.Contains("\"modifiedTime\":\"2026-03-14T09:30:00", metadatos);
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
