// Services/LibraryFilter.cs
// Lógica pura (sin WinUI) para buscar y ordenar la biblioteca; se prueba desde SaviumDrive.Tests.
using SaviumDrive.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SaviumDrive.Services
{
    /// <summary>Órdenes de la biblioteca. El nombre se guarda en la configuración (LibrarySort).</summary>
    public enum OrdenBiblioteca
    {
        NombreAZ,
        NombreZA,
        UltimoBackup
    }

    public static class LibraryFilter
    {
        /// <summary>Juegos cuyo nombre contiene el texto (sin distinguir mayúsculas ni acentos), en el orden pedido.</summary>
        public static List<GameConfig> Aplicar(IEnumerable<GameConfig> juegos, string texto, OrdenBiblioteca orden)
        {
            string buscado = Normalizar(texto);
            var filtrados = (juegos ?? Enumerable.Empty<GameConfig>())
                .Where(j => j != null && (buscado.Length == 0 || Normalizar(j.Name).Contains(buscado, StringComparison.Ordinal)));

            var comparador = StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);

            return orden switch
            {
                OrdenBiblioteca.NombreZA => filtrados.OrderByDescending(j => j.Name ?? "", comparador).ToList(),
                OrdenBiblioteca.UltimoBackup => filtrados
                    .OrderByDescending(j => j.LastBackup)
                    .ThenBy(j => j.Name ?? "", comparador)
                    .ToList(),
                _ => filtrados.OrderBy(j => j.Name ?? "", comparador).ToList()
            };
        }

        /// <summary>Minúsculas y sin acentos ("Pokémon" da "pokemon"); conserva los espacios para buscar varias palabras.</summary>
        public static string Normalizar(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return "";
            var sb = new StringBuilder(texto.Length);
            foreach (char c in texto.Trim().Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        /// <summary>Orden guardado en la configuración; si no es válido, de la A a la Z.</summary>
        public static OrdenBiblioteca DesdeTexto(string valor) =>
            Enum.TryParse(valor, out OrdenBiblioteca orden) ? orden : OrdenBiblioteca.NombreAZ;
    }
}
