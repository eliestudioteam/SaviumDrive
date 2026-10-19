// Services/UpdateVersioning.cs
// Lógica pura de versiones (sin WinUI) para poder probarla desde SaviumDrive.Tests.

namespace SaviumDrive.Services
{
    public static class UpdateVersioning
    {
        /// <summary>
        /// Compara dos versiones en formato semántico (ej: 1.2.3)
        /// </summary>
        public static bool IsNewerVersion(string newVersion, string currentVersion)
        {
            try
            {
                var newParts = newVersion.Split('.');
                var currentParts = currentVersion.Split('.');

                for (int i = 0; i < System.Math.Max(newParts.Length, currentParts.Length); i++)
                {
                    int newNum = i < newParts.Length ? int.Parse(newParts[i]) : 0;
                    int currentNum = i < currentParts.Length ? int.Parse(currentParts[i]) : 0;

                    if (newNum > currentNum) return true;
                    if (newNum < currentNum) return false;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Decide si esta ejecución es la primera tras una actualización.
        /// </summary>
        /// <param name="lastRunVersion">Versión que se ejecutó la última vez (null en instalaciones nuevas o anteriores a este campo).</param>
        /// <param name="lastUpdateVersion">Versión que descargó el actualizador integrado (puede ser null).</param>
        /// <param name="currentVersion">Versión de este ejecutable.</param>
        public static bool IsFirstRunAfterUpdate(string lastRunVersion, string lastUpdateVersion, string currentVersion)
        {
            if (!string.IsNullOrEmpty(lastRunVersion))
                return lastRunVersion != currentVersion;

            // Sin registro de la última versión ejecutada: o es una instalación nueva (no mostrar nada)
            // o viene de una versión antigua que no guardaba ese dato pero sí instaló con el actualizador.
            return !string.IsNullOrEmpty(lastUpdateVersion) && lastUpdateVersion == currentVersion;
        }
    }
}
