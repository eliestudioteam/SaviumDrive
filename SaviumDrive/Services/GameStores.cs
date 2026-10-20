// Services/GameStores.cs
// Lectura (sin WinUI) de los juegos instalados que registran Steam, Epic y GOG; se prueba desde SaviumDrive.Tests.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SaviumDrive.Services
{
    /// <summary>Un juego instalado según su tienda: nombre real y carpeta de instalación.</summary>
    public sealed class JuegoInstalado
    {
        public string Nombre { get; set; }
        public string CarpetaInstalacion { get; set; }
        public string Tienda { get; set; }
        public string SteamAppId { get; set; }
    }

    public static class GameStores
    {
        private static readonly Regex ParVdf = new Regex("\"(?<clave>[^\"]+)\"\\s+\"(?<valor>(?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);

        /// <summary>Pares "clave" "valor" de un archivo VDF/ACF de Steam (sin anidamiento: basta para estos archivos).</summary>
        private static IEnumerable<(string clave, string valor)> ParesVdf(string contenido)
        {
            foreach (Match m in ParVdf.Matches(contenido ?? ""))
                yield return (m.Groups["clave"].Value, m.Groups["valor"].Value.Replace("\\\\", "\\"));
        }

        /// <summary>Carpetas de bibliotecas de Steam en steamapps/libraryfolders.vdf.</summary>
        public static List<string> BibliotecasSteam(string libraryfoldersVdf)
        {
            return ParesVdf(libraryfoldersVdf)
                .Where(p => p.clave.Equals("path", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.valor)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Juego de un appmanifest_*.acf (en la carpeta steamapps de su biblioteca).</summary>
        public static JuegoInstalado LeerManifestSteam(string acf, string carpetaSteamapps)
        {
            string nombre = null, carpeta = null, appId = null;
            foreach (var (clave, valor) in ParesVdf(acf))
            {
                if (nombre == null && clave.Equals("name", StringComparison.OrdinalIgnoreCase)) nombre = valor;
                else if (carpeta == null && clave.Equals("installdir", StringComparison.OrdinalIgnoreCase)) carpeta = valor;
                else if (appId == null && clave.Equals("appid", StringComparison.OrdinalIgnoreCase)) appId = valor;
            }
            if (string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(carpeta)) return null;

            return new JuegoInstalado
            {
                Nombre = nombre.Trim(),
                CarpetaInstalacion = Path.Combine(carpetaSteamapps, "common", carpeta),
                Tienda = "Steam",
                SteamAppId = appId
            };
        }

        /// <summary>Juego de un manifiesto .item de Epic Games (ProgramData\Epic\EpicGamesLauncher\Data\Manifests).</summary>
        public static JuegoInstalado LeerManifestEpic(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var raiz = doc.RootElement;
                string nombre = raiz.TryGetProperty("DisplayName", out var n) ? n.GetString() : null;
                string carpeta = raiz.TryGetProperty("InstallLocation", out var c) ? c.GetString() : null;
                if (string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(carpeta)) return null;
                return new JuegoInstalado { Nombre = nombre.Trim(), CarpetaInstalacion = carpeta, Tienda = "Epic" };
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// El juego del catálogo cuya carpeta de instalación contiene el ejecutable
        /// (si hay varias, la más concreta: la de ruta más larga).
        /// </summary>
        public static JuegoInstalado BuscarPorEjecutable(IEnumerable<JuegoInstalado> catalogo, string rutaExe)
        {
            if (string.IsNullOrEmpty(rutaExe) || catalogo == null) return null;
            return catalogo
                .Where(j => !string.IsNullOrEmpty(j?.CarpetaInstalacion) && EstaDentroDe(rutaExe, j.CarpetaInstalacion))
                .OrderByDescending(j => j.CarpetaInstalacion.Length)
                .FirstOrDefault();
        }

        /// <summary>True si la ruta está dentro de la carpeta (sin confundir "Juego" con "Juego 2").</summary>
        public static bool EstaDentroDe(string ruta, string carpeta)
        {
            if (string.IsNullOrEmpty(ruta) || string.IsNullOrEmpty(carpeta)) return false;
            string r = Normalizar(ruta);
            string c = Normalizar(carpeta).TrimEnd('\\') + "\\";
            return r.StartsWith(c, StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalizar(string ruta)
        {
            string r = ruta.Replace('/', '\\');
            try { r = Path.GetFullPath(r); } catch { }
            return r;
        }
    }
}
