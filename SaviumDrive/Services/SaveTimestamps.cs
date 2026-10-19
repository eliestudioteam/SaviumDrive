// Services/SaveTimestamps.cs
// Lógica pura (sin WinUI ni Drive) para comparar las partidas locales con el backup; se prueba desde SaviumDrive.Tests.
using System;
using System.IO;

namespace SaviumDrive.Services
{
    public static class SaveTimestamps
    {
        /// <summary>Margen para no avisar por diferencias mínimas de reloj o de redondeo de fechas.</summary>
        public static readonly TimeSpan ToleranciaPorDefecto = TimeSpan.FromMinutes(2);

        /// <summary>Fecha (UTC) del archivo modificado más recientemente en la carpeta, o null si no hay archivos.</summary>
        public static DateTime? UltimaModificacion(string carpeta)
        {
            if (string.IsNullOrEmpty(carpeta) || !Directory.Exists(carpeta)) return null;

            DateTime? masReciente = null;
            try
            {
                foreach (var archivo in Directory.EnumerateFiles(carpeta, "*", SearchOption.AllDirectories))
                {
                    var fecha = File.GetLastWriteTimeUtc(archivo);
                    if (masReciente == null || fecha > masReciente) masReciente = fecha;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Si no se puede leer alguna subcarpeta, comparar con lo que sí se pudo leer
            }
            return masReciente;
        }

        /// <summary>
        /// True si las partidas locales se modificaron después del último guardado en la nube
        /// (restaurar haría perder ese progreso).
        /// </summary>
        public static bool LocalEsMasReciente(DateTime? ultimaModificacionLocal, DateTime ultimoBackup, TimeSpan? tolerancia = null)
        {
            if (ultimaModificacionLocal == null || ultimoBackup == default) return false;

            DateTime local = ComoUtc(ultimaModificacionLocal.Value);
            DateTime backup = ComoUtc(ultimoBackup);
            return local > backup + (tolerancia ?? ToleranciaPorDefecto);
        }

        private static DateTime ComoUtc(DateTime fecha) =>
            fecha.Kind == DateTimeKind.Local ? fecha.ToUniversalTime() : DateTime.SpecifyKind(fecha, DateTimeKind.Utc);
    }
}
