// Services/LudusaviIndex.cs
// Lógica pura (sin WinUI ni red) para la base de datos de Ludusavi (https://github.com/mtkennerly/ludusavi-manifest, MIT):
// leer su manifest.yaml en streaming, guardar un índice compacto y buscar dónde guarda partidas un juego.
// Se prueba desde SaviumDrive.Tests.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SaviumDrive.Services
{
    public sealed class JuegoLudusavi
    {
        public string Nombre { get; set; }
        public List<string> Rutas { get; } = new List<string>();
        /// <summary>ID del juego en Steam (si lo tiene): permite encontrarlo sin depender de cómo se escriba el nombre.</summary>
        public string SteamId { get; set; }
    }

    public static class LudusaviIndex
    {
        private const char SeparadorRutas = '\u001F';

        // Prefijos que se pueden resolver en Windows (<home> solo si la entrada es para Windows)
        private static readonly string[] PrefijosWindows =
        {
            "<winAppData>", "<winLocalAppData>", "<winLocalAppDataLow>", "<winDocuments>",
            "<winPublic>", "<winProgramData>", "<winDir>"
        };

        // ===================== LECTURA DEL MANIFEST (streaming, línea a línea) =====================

        /// <summary>
        /// Recorre manifest.yaml sin cargarlo entero y devuelve, por juego, las rutas de partidas de Windows
        /// (etiquetadas "save" o sin etiqueta; se descartan las que solo son "config").
        /// </summary>
        public static IEnumerable<JuegoLudusavi> LeerManifest(TextReader lector)
        {
            JuegoLudusavi actual = null;
            bool enFiles = false, enSteam = false;
            string ruta = null;
            bool enTags = false, enWhen = false;
            var tags = new List<string>();
            var sistemas = new List<string>();

            void CerrarRuta()
            {
                if (ruta != null && actual != null && EsRutaDePartidaWindows(ruta, tags, sistemas))
                    actual.Rutas.Add(ruta);
                ruta = null;
                tags.Clear();
                sistemas.Clear();
                enTags = enWhen = false;
            }

            string linea;
            while ((linea = lector.ReadLine()) != null)
            {
                if (linea.Length == 0 || linea.StartsWith("---") || linea.TrimStart().StartsWith("#")) continue;

                int sangria = linea.Length - linea.TrimStart(' ').Length;
                string contenido = linea.Substring(sangria);

                if (sangria == 0)
                {
                    // Una clave sin sangría es el nombre de otro juego
                    CerrarRuta();
                    if (actual != null && actual.Rutas.Count > 0) yield return actual;
                    actual = new JuegoLudusavi { Nombre = Clave(contenido) };
                    enFiles = enSteam = false;
                    continue;
                }

                if (sangria == 2)
                {
                    CerrarRuta();
                    enFiles = contenido == "files:";
                    enSteam = contenido == "steam:";
                    continue;
                }

                if (enSteam)
                {
                    // Sección "  steam:" con la línea "    id: 108710"
                    if (sangria == 4 && contenido.StartsWith("id:") && actual != null)
                        actual.SteamId = contenido.Substring(3).Trim();
                    continue;
                }

                if (!enFiles) continue;

                if (sangria == 4)
                {
                    CerrarRuta();
                    ruta = Clave(contenido);
                    continue;
                }

                if (ruta == null) continue;

                if (sangria == 6)
                {
                    enTags = contenido == "tags:";
                    enWhen = contenido == "when:";
                    continue;
                }

                if (enTags && contenido.StartsWith("- "))
                {
                    tags.Add(contenido.Substring(2).Trim());
                }
                else if (enWhen)
                {
                    // "- os: windows" o, en elementos con varias claves, "os: windows"
                    string valor = contenido.StartsWith("- ") ? contenido.Substring(2) : contenido;
                    if (valor.StartsWith("os:")) sistemas.Add(valor.Substring(3).Trim());
                }
            }

            CerrarRuta();
            if (actual != null && actual.Rutas.Count > 0) yield return actual;
        }

        private static bool EsRutaDePartidaWindows(string ruta, List<string> tags, List<string> sistemas)
        {
            if (tags.Count > 0 && !tags.Contains("save")) return false;
            if (sistemas.Count > 0 && !sistemas.Contains("windows")) return false;
            if (PrefijosWindows.Any(p => ruta.StartsWith(p, StringComparison.Ordinal))) return true;
            return ruta.StartsWith("<home>", StringComparison.Ordinal) && sistemas.Contains("windows");
        }

        /// <summary>Clave de una línea YAML "clave:" o "clave: {}", quitando comillas.</summary>
        private static string Clave(string contenido)
        {
            string c = contenido.TrimEnd();
            if (c.EndsWith(": {}")) c = c.Substring(0, c.Length - 4);
            else if (c.EndsWith(":")) c = c.Substring(0, c.Length - 1);
            return QuitarComillas(c.Trim());
        }

        private static string QuitarComillas(string s)
        {
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
            {
                try { return System.Text.Json.JsonSerializer.Deserialize<string>(s); }
                catch { return s.Substring(1, s.Length - 2); }
            }
            if (s.Length >= 2 && s[0] == '\'' && s[s.Length - 1] == '\'')
                return s.Substring(1, s.Length - 2).Replace("''", "'");
            return s;
        }

        // ===================== ÍNDICE COMPACTO =====================

        /// <summary>Escribe el índice: una línea por juego "nombreNormalizado\tNombre\truta1␟ruta2". Devuelve cuántos juegos.</summary>
        public static int EscribirIndice(TextReader manifest, TextWriter indice)
        {
            int total = 0;
            foreach (var juego in LeerManifest(manifest))
            {
                if (juego.Nombre.Contains('\t') || juego.Rutas.Any(r => r.Contains('\t'))) continue;
                indice.Write(Normalizar(juego.Nombre));
                indice.Write('\t');
                indice.Write(juego.Nombre);
                indice.Write('\t');
                indice.Write(string.Join(SeparadorRutas, juego.Rutas));
                indice.Write('\t');
                indice.WriteLine(juego.SteamId ?? "");
                total++;
            }
            return total;
        }

        /// <summary>
        /// Busca un juego en el índice leyéndolo línea a línea (sin cargarlo en memoria).
        /// Primero por nombre exacto (sin mayúsculas, espacios ni símbolos); si no, el nombre más corto que empiece igual.
        /// </summary>
        public static JuegoLudusavi Buscar(TextReader indice, string nombre) =>
            BuscarCandidatos(indice, nombre, 1).FirstOrDefault();

        /// <summary>
        /// Candidatos ordenados: primero el nombre exacto y después los que empiezan igual, del más corto al más largo
        /// (por ejemplo, "Minecraft" encuentra "Minecraft Dungeons" y "Minecraft: Java Edition"). Lee el índice línea a línea.
        /// </summary>
        public static List<JuegoLudusavi> BuscarCandidatos(TextReader indice, string nombre, int maximo = 10)
        {
            var resultado = new List<JuegoLudusavi>();
            string buscado = Normalizar(nombre);
            if (buscado.Length < 2 || maximo <= 0) return resultado;

            string exacta = null;
            var prefijos = new List<(int largo, string linea)>();

            string linea;
            while ((linea = indice.ReadLine()) != null)
            {
                int tab = linea.IndexOf('\t');
                if (tab <= 0) continue;
                string clave = linea.Substring(0, tab);

                if (clave == buscado) { exacta ??= linea; continue; }

                if (buscado.Length >= 4 && clave.StartsWith(buscado, StringComparison.Ordinal))
                {
                    // Guardar solo los más cortos (no cargar miles de coincidencias en memoria)
                    prefijos.Add((clave.Length, linea));
                    if (prefijos.Count > maximo * 4)
                    {
                        prefijos.Sort((a, b) => a.largo.CompareTo(b.largo));
                        prefijos.RemoveRange(maximo, prefijos.Count - maximo);
                    }
                }
            }

            if (exacta != null) AgregarDesdeLinea(resultado, exacta);
            foreach (var p in prefijos.OrderBy(p => p.largo).Take(maximo - resultado.Count))
                AgregarDesdeLinea(resultado, p.linea);
            return resultado;
        }

        private static void AgregarDesdeLinea(List<JuegoLudusavi> lista, string linea)
        {
            var partes = linea.Split('\t');
            if (partes.Length < 3) return;
            lista.Add(DesdeLinea(partes));
        }

        private static JuegoLudusavi DesdeLinea(string[] partes)
        {
            var juego = new JuegoLudusavi { Nombre = partes[1], SteamId = partes.Length > 3 && partes[3].Length > 0 ? partes[3] : null };
            juego.Rutas.AddRange(partes[2].Split(SeparadorRutas, StringSplitOptions.RemoveEmptyEntries));
            return juego;
        }

        /// <summary>Busca un juego por su ID de Steam (leyendo el índice línea a línea).</summary>
        public static JuegoLudusavi BuscarPorSteamId(TextReader indice, string steamId)
        {
            if (string.IsNullOrWhiteSpace(steamId)) return null;
            string sufijo = "\t" + steamId.Trim();
            string linea;
            while ((linea = indice.ReadLine()) != null)
            {
                if (!linea.EndsWith(sufijo, StringComparison.Ordinal)) continue;
                var partes = linea.Split('\t');
                if (partes.Length > 3 && partes[3] == steamId.Trim()) return DesdeLinea(partes);
            }
            return null;
        }

        /// <summary>Clave de búsqueda: "ELDEN RING" y "Elden Ring" dan "eldenring"; también quita acentos y símbolos.</summary>
        public static string Normalizar(string nombre)
        {
            if (string.IsNullOrEmpty(nombre)) return "";
            var sb = new StringBuilder(nombre.Length);
            foreach (char c in nombre.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        // ===================== RUTAS EN ESTE PC =====================

        /// <summary>Carpetas especiales de Windows para cada prefijo de Ludusavi.</summary>
        public static Dictionary<string, string> CarpetasDeEstePc()
        {
            string perfil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return new Dictionary<string, string>
            {
                ["<winAppData>"] = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                ["<winLocalAppData>"] = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                ["<winLocalAppDataLow>"] = Path.Combine(perfil, "AppData", "LocalLow"),
                ["<winDocuments>"] = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                ["<winPublic>"] = Environment.GetEnvironmentVariable("PUBLIC") ?? @"C:\Users\Public",
                ["<winProgramData>"] = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                ["<winDir>"] = Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                ["<home>"] = perfil,
            };
        }

        /// <summary>
        /// Convierte una ruta de Ludusavi en una carpeta que exista en este PC, o null.
        /// Resuelve comodines y &lt;storeUserId&gt; si hay una sola carpeta posible; los archivos se cambian por su carpeta.
        /// </summary>
        public static string ResolverCarpeta(string plantilla, IReadOnlyDictionary<string, string> carpetas, string usuario = null)
        {
            if (string.IsNullOrEmpty(plantilla)) return null;

            int fin = plantilla.IndexOf('>');
            if (!plantilla.StartsWith("<") || fin < 0) return null;
            string prefijo = plantilla.Substring(0, fin + 1);
            if (!carpetas.TryGetValue(prefijo, out string baseDir) || string.IsNullOrEmpty(baseDir) || !Directory.Exists(baseDir)) return null;

            string actual = baseDir;
            var segmentos = plantilla.Substring(fin + 1).Split('/', StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < segmentos.Length; i++)
            {
                string seg = segmentos[i].Replace("<osUserName>", usuario ?? Environment.UserName);
                bool esUltimo = i == segmentos.Length - 1;

                if (seg == "**" || seg.Contains("<game>") || seg.Contains("<base>")) break;

                bool esPatron = seg.Contains('*') || seg.Contains('?') || seg.Contains("<storeUserId>") || seg.Contains("<storeGameId>");
                if (esPatron)
                {
                    string patron = seg.Replace("<storeUserId>", "*").Replace("<storeGameId>", "*");
                    if (patron.Contains('<')) break;
                    string[] opciones;
                    try { opciones = Directory.GetDirectories(actual, patron); } catch { break; }
                    if (opciones.Length == 1) { actual = opciones[0]; continue; }
                    break; // varias (o ninguna, p. ej. un patrón de archivos): quedarse en la carpeta actual
                }

                if (seg.Contains('<')) break;

                string siguiente = Path.Combine(actual, seg);
                if (Directory.Exists(siguiente)) { actual = siguiente; continue; }
                if (esUltimo && File.Exists(siguiente)) break; // es un archivo: usar su carpeta
                return null; // la carpeta del juego no existe en este PC
            }

            // No sugerir una carpeta especial entera (ej. todo AppData)
            if (string.Equals(Path.GetFullPath(actual).TrimEnd('\\'), Path.GetFullPath(baseDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                return null;

            return Directory.Exists(actual) ? actual : null;
        }

        /// <summary>Primera carpeta del juego que exista en este PC.</summary>
        public static string PrimeraCarpetaExistente(JuegoLudusavi juego, IReadOnlyDictionary<string, string> carpetas, string usuario = null)
        {
            if (juego == null) return null;
            foreach (var ruta in juego.Rutas)
            {
                string carpeta = ResolverCarpeta(ruta, carpetas, usuario);
                if (carpeta != null) return carpeta;
            }
            return null;
        }
    }
}
