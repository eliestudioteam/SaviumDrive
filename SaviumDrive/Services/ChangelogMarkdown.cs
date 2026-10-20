// Services/ChangelogMarkdown.cs
// Convierte las notas de una versión de GitHub (Markdown) en bloques sencillos para la ventana de novedades.
// Sin WinUI: se prueba desde SaviumDrive.Tests.
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SaviumDrive.Services
{
    public enum TipoBloqueMd { Titulo, Vineta, Numerado, Parrafo, Separador }
    public enum TipoTramoMd { Texto, Negrita, Cursiva, Codigo, Enlace, Salto }

    public sealed class TramoMd
    {
        public TipoTramoMd Tipo { get; set; }
        public string Texto { get; set; } = "";
        public string Url { get; set; }
        public override string ToString() => $"{Tipo}:{Texto}";
    }

    public sealed class BloqueMd
    {
        public TipoBloqueMd Tipo { get; set; }
        /// <summary>Títulos: 1 a 3. Listas: sangría (0 = primer nivel).</summary>
        public int Nivel { get; set; }
        /// <summary>Listas numeradas: "1.", "2."...</summary>
        public string Numero { get; set; }
        public List<TramoMd> Tramos { get; } = new List<TramoMd>();
        public string TextoPlano => string.Concat(Tramos.Select(t => t.Tipo == TipoTramoMd.Salto ? "\n" : t.Texto));
    }

    public static class ChangelogMarkdown
    {
        private static readonly Regex Titulo = new Regex(@"^(#{1,6})\s+(.*?)\s*#*\s*$");
        private static readonly Regex Vineta = new Regex(@"^(\s*)[-*+]\s+(?:\[[ xX]\]\s+)?(.*)$");
        private static readonly Regex Numerado = new Regex(@"^(\s*)(\d+)[.)]\s+(.*)$");
        private static readonly Regex Separador = new Regex(@"^\s*([-*_])(\s*\1){2,}\s*$");
        private static readonly Regex Comentarios = new Regex(@"<!--.*?-->", RegexOptions.Singleline);

        // Negrita, código, enlaces [texto](url), enlaces sueltos y cursiva
        private static readonly Regex EnLinea = new Regex(
            @"\*\*(?<negrita>.+?)\*\*|__(?<negrita>.+?)__" +
            @"|`(?<codigo>[^`]+)`" +
            @"|\[(?<texto>[^\]]+)\]\((?<url>https?://[^)\s]+)\)" +
            @"|(?<suelto>https?://[^\s<>()]+[^\s<>().,;:!?'""])" +
            @"|(?<![\w*])\*(?<cursiva>[^*\s](?:[^*]*[^*\s])?)\*(?![\w*])" +
            @"|(?<![\w_])_(?<cursiva>[^_\s](?:[^_]*[^_\s])?)_(?![\w_])");

        public static List<BloqueMd> Analizar(string markdown)
        {
            var bloques = new List<BloqueMd>();
            if (string.IsNullOrWhiteSpace(markdown)) return bloques;

            string texto = Comentarios.Replace(markdown.Replace("\r\n", "\n").Replace('\r', '\n'), "");
            BloqueMd parrafo = null;

            foreach (var linea in texto.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(linea)) { parrafo = null; continue; }

                Match m;
                if ((m = Titulo.Match(linea)).Success)
                {
                    parrafo = null;
                    bloques.Add(ConTramos(new BloqueMd { Tipo = TipoBloqueMd.Titulo, Nivel = System.Math.Min(3, m.Groups[1].Length) }, m.Groups[2].Value));
                }
                else if (Separador.IsMatch(linea))
                {
                    parrafo = null;
                    bloques.Add(new BloqueMd { Tipo = TipoBloqueMd.Separador });
                }
                else if ((m = Vineta.Match(linea)).Success)
                {
                    parrafo = null;
                    bloques.Add(ConTramos(new BloqueMd { Tipo = TipoBloqueMd.Vineta, Nivel = Sangria(m.Groups[1].Value) }, m.Groups[2].Value));
                }
                else if ((m = Numerado.Match(linea)).Success)
                {
                    parrafo = null;
                    bloques.Add(ConTramos(new BloqueMd { Tipo = TipoBloqueMd.Numerado, Nivel = Sangria(m.Groups[1].Value), Numero = m.Groups[2].Value + "." }, m.Groups[3].Value));
                }
                else if (parrafo != null)
                {
                    // Las notas de GitHub respetan los saltos de línea dentro de un párrafo
                    parrafo.Tramos.Add(new TramoMd { Tipo = TipoTramoMd.Salto });
                    parrafo.Tramos.AddRange(Tramos(linea.Trim()));
                }
                else
                {
                    parrafo = ConTramos(new BloqueMd { Tipo = TipoBloqueMd.Parrafo }, linea.Trim());
                    bloques.Add(parrafo);
                }
            }
            return bloques;
        }

        /// <summary>Texto con formato en línea: **negrita**, *cursiva*, `código` y enlaces.</summary>
        public static List<TramoMd> Tramos(string texto)
        {
            var tramos = new List<TramoMd>();
            int ultimo = 0;
            foreach (Match m in EnLinea.Matches(texto ?? ""))
            {
                if (m.Index > ultimo) tramos.Add(new TramoMd { Texto = texto.Substring(ultimo, m.Index - ultimo) });

                if (m.Groups["negrita"].Success) tramos.Add(new TramoMd { Tipo = TipoTramoMd.Negrita, Texto = m.Groups["negrita"].Value });
                else if (m.Groups["codigo"].Success) tramos.Add(new TramoMd { Tipo = TipoTramoMd.Codigo, Texto = m.Groups["codigo"].Value });
                else if (m.Groups["url"].Success) tramos.Add(new TramoMd { Tipo = TipoTramoMd.Enlace, Texto = m.Groups["texto"].Value, Url = m.Groups["url"].Value });
                else if (m.Groups["suelto"].Success) tramos.Add(new TramoMd { Tipo = TipoTramoMd.Enlace, Texto = m.Groups["suelto"].Value, Url = m.Groups["suelto"].Value });
                else tramos.Add(new TramoMd { Tipo = TipoTramoMd.Cursiva, Texto = m.Groups["cursiva"].Value });

                ultimo = m.Index + m.Length;
            }
            if (ultimo < (texto ?? "").Length) tramos.Add(new TramoMd { Texto = texto.Substring(ultimo) });
            return tramos;
        }

        private static BloqueMd ConTramos(BloqueMd bloque, string texto)
        {
            bloque.Tramos.AddRange(Tramos(texto.Trim()));
            return bloque;
        }

        // Dos espacios (o un tabulador) por nivel, como mucho 3 niveles
        private static int Sangria(string espacios) => System.Math.Min(3, espacios.Replace("\t", "  ").Length / 2);
    }
}
