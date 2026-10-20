// Core/OAuthScopes.cs
// Permisos de Google que pide la app. Sin dependencias: se prueba desde SaviumDrive.Tests.
using System;
using System.Linq;

namespace SaviumDrive.Core
{
    public static class OAuthScopes
    {
        /// <summary>
        /// Solo los archivos y carpetas que crea Savium Drive (SaviumBackups y savium_config.json).
        /// No da acceso al resto del Drive del usuario, ni a compartir archivos ni a ver con quién se comparten.
        /// Basta para todo lo que hace la app: crear carpetas, subir, descargar, copiar, renombrar,
        /// mandar a la papelera y leer la cuota y el nombre de la cuenta (about.get).
        /// </summary>
        public const string ArchivosDeLaApp = "https://www.googleapis.com/auth/drive.file";

        /// <summary>Permiso de versiones anteriores: todo el Drive.</summary>
        public const string TodoElDrive = "https://www.googleapis.com/auth/drive";

        public static readonly string[] Actuales = { ArchivosDeLaApp };

        /// <summary>
        /// True si una sesión guardada se concedió con los permisos actuales. Las sesiones antiguas (con acceso
        /// a todo el Drive) hay que renovarlas: Google mantiene el permiso que se concedió aunque el código pida menos.
        /// </summary>
        public static bool SesionAlDia(string scopeConcedido)
        {
            if (string.IsNullOrWhiteSpace(scopeConcedido)) return false;
            var concedidos = scopeConcedido.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return concedidos.Contains(ArchivosDeLaApp) && !concedidos.Contains(TodoElDrive);
        }
    }
}
