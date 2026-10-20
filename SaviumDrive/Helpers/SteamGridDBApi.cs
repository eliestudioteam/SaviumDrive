using Newtonsoft.Json.Linq;
using SaviumDrive.Core;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace SaviumDrive.Helpers
{
    /// <summary>
    /// Búsqueda de portadas en SteamGridDB (https://www.steamgriddb.com), una base de datos comunitaria
    /// de imágenes de juegos. Se usan las portadas verticales de 600x900.
    /// </summary>
    public class SteamGridApi
    {
        private const string BASE_URL = "https://www.steamgriddb.com/api/v2";

        // Un solo cliente para toda la app: crear uno por búsqueda deja sockets y memoria sin liberar
        private static readonly HttpClient _client = CrearCliente();

        private static HttpClient CrearCliente()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            // La clave no está en el código: se inyecta al compilar (ver Core/AppSecrets.cs)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AppSecrets.SteamGridDbApiKey);
            return client;
        }

        /// <summary>
        /// Hasta 5 URL de portadas del juego. Con el ID de Steam la búsqueda es exacta; si no, se usa el
        /// primer resultado de buscar por nombre. Devuelve una lista vacía si no hay resultados o falla la red.
        /// </summary>
        public async Task<List<string>> BuscarPortadas(string nombreJuego, string steamAppId = null)
        {
            var resultados = new List<string>();
            // Compilación sin clave de SteamGridDB: sin portadas, como si no hubiera resultados
            if (string.IsNullOrEmpty(AppSecrets.SteamGridDbApiKey)) return resultados;
            try
            {
                var client = _client;
                {
                    // 1. ID del juego en SteamGridDB: por el de Steam si se conoce y, si no, por el nombre
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

                        // 2. Portadas verticales (grids) de varios estilos para que el usuario pueda elegir
                        string gridUrl = $"{BASE_URL}/grids/game/{gameId}?dimensions=600x900&styles=alternate,material,blurred";
                        var responseGrid = await client.GetStringAsync(gridUrl);
                        var jsonGrid = JObject.Parse(responseGrid);

                        if ((bool)jsonGrid["success"] && jsonGrid["data"].HasValues)
                        {
                            foreach (var item in jsonGrid["data"])
                            {
                                string url = (string)item["url"];
                                if (!string.IsNullOrEmpty(url))
                                {
                                    resultados.Add(url);
                                    if (resultados.Count >= 5) break;
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            return resultados;
        }

        /// <summary>ID de SteamGridDB del juego con ese ID de Steam, o null si no lo conoce.</summary>
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