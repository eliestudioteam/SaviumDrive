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
        public async Task<List<string>> BuscarPortadas(string nombreJuego)
        {
            var resultados = new List<string>();
            try
            {
                var client = _client;
                {
                    // 1. Buscar ID
                    string searchUrl = $"{BASE_URL}/search/autocomplete/{Uri.EscapeDataString(nombreJuego)}";
                    var responseSearch = await client.GetStringAsync(searchUrl);
                    var jsonSearch = JObject.Parse(responseSearch);

                    if ((bool)jsonSearch["success"] && jsonSearch["data"].HasValues)
                    {
                        var primerJuego = jsonSearch["data"][0];
                        int gameId = (int)primerJuego["id"];

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
    }
}