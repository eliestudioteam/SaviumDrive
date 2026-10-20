// Services/BackupDiff.cs
// Lógica pura (sin WinUI ni Drive) para saber qué archivos cambiaron respecto al backup; se prueba desde SaviumDrive.Tests.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace SaviumDrive.Services
{
    /// <summary>Tamaño y MD5 de un archivo (local o en Drive).</summary>
    public readonly record struct HuellaArchivo(long Tamano, string Md5);

    /// <summary>Qué pasó al actualizar un backup.</summary>
    public class ResultadoActualizacion
    {
        public bool YaAlDia { get; set; }
        public int Subidos { get; set; }
        public int SinCambios { get; set; }
        public int Eliminados { get; set; }
    }

    public static class BackupDiff
    {
        /// <summary>Rutas relativas de los archivos según cómo están respecto al backup.</summary>
        public sealed class Resultado
        {
            public List<string> SinCambios { get; } = new List<string>();
            public List<string> Modificados { get; } = new List<string>();
            public List<string> Nuevos { get; } = new List<string>();
            public List<string> Eliminados { get; } = new List<string>();

            /// <summary>El backup ya coincide con el PC: no hace falta subir nada.</summary>
            public bool YaAlDia => Modificados.Count == 0 && Nuevos.Count == 0 && Eliminados.Count == 0;
        }

        /// <summary>Ruta relativa con '/' y sin barras sobrantes (igual en local y en Drive).</summary>
        public static string NormalizarRuta(string rutaRelativa) =>
            (rutaRelativa ?? "").Replace('\\', '/').Trim('/');

        /// <summary>
        /// Compara archivos locales con los del backup (clave: ruta relativa, sin distinguir mayúsculas
        /// como Windows). Un archivo sin cambios tiene el mismo tamaño y el mismo MD5.
        /// </summary>
        public static Resultado Calcular(IReadOnlyDictionary<string, HuellaArchivo> local,
                                         IReadOnlyDictionary<string, HuellaArchivo> remoto)
        {
            var resultado = new Resultado();
            var remotoNormalizado = new Dictionary<string, HuellaArchivo>(StringComparer.OrdinalIgnoreCase);
            foreach (var par in remoto ?? new Dictionary<string, HuellaArchivo>())
                remotoNormalizado[NormalizarRuta(par.Key)] = par.Value;

            var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var par in local ?? new Dictionary<string, HuellaArchivo>())
            {
                string ruta = NormalizarRuta(par.Key);
                vistos.Add(ruta);

                if (!remotoNormalizado.TryGetValue(ruta, out var enDrive))
                    resultado.Nuevos.Add(ruta);
                else if (enDrive.Tamano == par.Value.Tamano &&
                         !string.IsNullOrEmpty(enDrive.Md5) &&
                         string.Equals(enDrive.Md5, par.Value.Md5, StringComparison.OrdinalIgnoreCase))
                    resultado.SinCambios.Add(ruta);
                else
                    resultado.Modificados.Add(ruta);
            }

            resultado.Eliminados.AddRange(remotoNormalizado.Keys.Where(r => !vistos.Contains(r)));
            return resultado;
        }

        /// <summary>MD5 en hexadecimal minúscula (el mismo formato que md5Checksum de Drive).</summary>
        public static string Md5Hex(string ruta)
        {
            using var md5 = MD5.Create();
            using var stream = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Convert.ToHexString(md5.ComputeHash(stream)).ToLowerInvariant();
        }

        /// <summary>Huellas de todos los archivos de una carpeta local (clave: ruta relativa normalizada).</summary>
        public static Dictionary<string, HuellaArchivo> LeerCarpetaLocal(string carpeta)
        {
            var huellas = new Dictionary<string, HuellaArchivo>(StringComparer.OrdinalIgnoreCase);
            foreach (var archivo in Directory.EnumerateFiles(carpeta, "*", SearchOption.AllDirectories))
            {
                string relativa = NormalizarRuta(Path.GetRelativePath(carpeta, archivo));
                huellas[relativa] = new HuellaArchivo(new FileInfo(archivo).Length, Md5Hex(archivo));
            }
            return huellas;
        }
    }
}
