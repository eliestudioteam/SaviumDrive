using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace SaviumDrive.Helpers
{
    public class SteamGridApi
    {
        private const string API_KEY = "785959dce88bcfdbd344872dc36ac19b"; // Tu API Key
        private const string BASE_URL = "https://www.steamgriddb.com/api/v2";

        // Un solo cliente para toda la app: crear uno por búsqueda deja sockets y memoria sin liberar
        private static readonly HttpClient _client = CrearCliente();

        private static HttpClient CrearCliente()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", API_KEY);
            return client;
        }

        // 🔥 CAMBIO: Ahora devuelve una Lista de strings
        public async Task<List<string>> BuscarPortadas(string nombreJuego, string steamAppId = null)
        {
            var resultados = new List<string>();
            try
            {
                var client = _client;
                {
                    // 1. Buscar ID: por el de Steam si se conoce (exacto) y, si no, por el nombre
                    int? gameId = await IdPorSteam(steamAppId);
                    if (gameId == null)
                    {
                        string searchUrl = $"{BASE_URL}/search/autocomplete/{Uri.EscapeDataString(nombreJuego.Trim())}";
                        var jsonSearch = JObject.Parse(await client.GetStringAsync(searchUrl));
                        if ((bool)jsonSearch["success"] && jsonSearch["data"].HasValues)
                            gameId = (int)jsonSearch["data"][0]["id"];
                    }

                    if (gameId != null)
                    {

                        // 2. Buscar imágenes verticales (Grid)
                        // style=alternate,material,blurred para variedad
                        string gridUrl = $"{BASE_URL}/grids/game/{gameId}?dimensions=600x900&styles=alternate,material,blurred";
                        var responseGrid = await client.GetStringAsync(gridUrl);
                        var jsonGrid = JObject.Parse(responseGrid);

                        if ((bool)jsonGrid["success"] && jsonGrid["data"].HasValues)
                        {
                            // 🔥 Recoger hasta 5 imágenes
                            foreach (var item in jsonGrid["data"])
                            {
                                string url = (string)item["url"];
                                if (!string.IsNullOrEmpty(url))
                                {
                                    resultados.Add(url);
                                    if (resultados.Count >= 5) break; // Límite de 5
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            return resultados;
        }

        private static async Task<int?> IdPorSteam(string steamAppId)
        {
            if (string.IsNullOrEmpty(steamAppId) || !long.TryParse(steamAppId, out _)) return null;
            try
            {
                var json = JObject.Parse(await _client.GetStringAsync($"{BASE_URL}/games/steam/{steamAppId}"));
                return (bool)json["success"] ? (int?)json["data"]?["id"] : null;
            }
            catch { return null; } // SteamGridDB no conoce ese ID: se busca por nombre
        }
    }
}