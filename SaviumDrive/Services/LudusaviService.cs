// Services/LudusaviService.cs
// Descarga la base de datos de Ludusavi (https://github.com/mtkennerly/ludusavi-manifest, licencia MIT)
// y busca dónde guarda las partidas un juego. Pensado para gastar poca memoria:
// el manifest (~18 MB) se lee en streaming y solo se guarda en disco un índice compacto.
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SaviumDrive.Services
{
    public static class LudusaviService
    {
        private const string UrlManifest = "https://raw.githubusercontent.com/mtkennerly/ludusavi-manifest/master/data/manifest.yaml";
        private static readonly TimeSpan Caducidad = TimeSpan.FromDays(7);
        private static readonly SemaphoreSlim _bloqueo = new SemaphoreSlim(1, 1);
        private static readonly HttpClient _http = CrearCliente();

        private static string CarpetaDatos =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SaviumDrive");

        private static string RutaIndice => Path.Combine(CarpetaDatos, "ludusavi.tsv");
        private static string RutaEtag => Path.Combine(CarpetaDatos, "ludusavi.etag");

        private static HttpClient CrearCliente()
        {
            var cliente = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
            cliente.DefaultRequestHeaders.UserAgent.ParseAdd("SaviumDrive");
            return cliente;
        }

        public static bool IndiceDisponible => File.Exists(RutaIndice);

        /// <summary>
        /// Busca la carpeta de partidas de un juego que exista en este PC, o null.
        /// Descarga o actualiza la base de datos si hace falta (si no hay internet, usa la que haya).
        /// </summary>
        public static async Task<string> BuscarCarpetaAsync(string nombreJuego, CancellationToken token = default)
        {
            if (string.IsNullOrWhiteSpace(nombreJuego)) return null;

            try { await AsegurarIndiceAsync(token); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Ludusavi: no se pudo actualizar la base de datos: {ex.Message}");
            }

            if (!IndiceDisponible) return null;

            return await Task.Run(() =>
            {
                using var lector = new StreamReader(RutaIndice);
                var carpetas = LudusaviIndex.CarpetasDeEstePc();

                // El primer candidato cuya carpeta exista en este PC (ej. "Minecraft" → Java Edition si es la instalada)
                foreach (var juego in LudusaviIndex.BuscarCandidatos(lector, nombreJuego))
                {
                    string carpeta = LudusaviIndex.PrimeraCarpetaExistente(juego, carpetas);
                    if (carpeta != null) return carpeta;
                }
                return null;
            }, token);
        }

        /// <summary>Descarga el manifest si no hay índice o tiene más de 7 días (solo si cambió en GitHub).</summary>
        private static async Task AsegurarIndiceAsync(CancellationToken token)
        {
            if (IndiceDisponible && DateTime.UtcNow - File.GetLastWriteTimeUtc(RutaIndice) < Caducidad) return;

            await _bloqueo.WaitAsync(token);
            try
            {
                if (IndiceDisponible && DateTime.UtcNow - File.GetLastWriteTimeUtc(RutaIndice) < Caducidad) return;
                Directory.CreateDirectory(CarpetaDatos);

                using var peticion = new HttpRequestMessage(HttpMethod.Get, UrlManifest);
                if (IndiceDisponible && File.Exists(RutaEtag))
                    peticion.Headers.TryAddWithoutValidation("If-None-Match", File.ReadAllText(RutaEtag).Trim());

                using var respuesta = await _http.SendAsync(peticion, HttpCompletionOption.ResponseHeadersRead, token);

                if (respuesta.StatusCode == System.Net.HttpStatusCode.NotModified)
                {
                    File.SetLastWriteTimeUtc(RutaIndice, DateTime.UtcNow); // sigue vigente otra semana
                    return;
                }
                respuesta.EnsureSuccessStatusCode();

                // Leer en streaming y escribir el índice compacto a un archivo temporal
                string temporal = RutaIndice + ".tmp";
                await using (var flujo = await respuesta.Content.ReadAsStreamAsync(token))
                {
                    using var lector = new StreamReader(flujo);
                    using var escritor = new StreamWriter(temporal, false);
                    int juegos = LudusaviIndex.EscribirIndice(lector, escritor);
                    if (juegos == 0) throw new InvalidDataException("La base de datos de Ludusavi llegó vacía");
                }

                File.Move(temporal, RutaIndice, overwrite: true);
                if (respuesta.Headers.ETag != null) File.WriteAllText(RutaEtag, respuesta.Headers.ETag.ToString());
            }
            finally
            {
                _bloqueo.Release();
            }
        }
    }
}
