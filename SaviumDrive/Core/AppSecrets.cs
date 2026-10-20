// Core/AppSecrets.cs
// Claves de los servicios externos (SteamGridDB y el cliente OAuth de Google). No están en el código:
// se inyectan al compilar desde SaviumDrive/Secrets.props (archivo local que git ignora; plantilla en
// Secrets.props.example) o desde variables de entorno con el mismo nombre, como hace GitHub Actions.
// Acaban dentro del .exe (una app de escritorio no puede esconderlas del todo), pero no en el repositorio.

using System.Linq;
using System.Reflection;

namespace SaviumDrive.Core
{
    internal static class AppSecrets
    {
        /// <summary>Clave de la API de SteamGridDB (portadas). Vacía si la compilación no la incluye.</summary>
        public static string SteamGridDbApiKey { get; } = Leer("SteamGridDbApiKey");

        /// <summary>ID del cliente OAuth "app de escritorio" del proyecto en Google Cloud.</summary>
        public static string GoogleClientId { get; } = Leer("GoogleClientId");

        public static string GoogleClientSecret { get; } = Leer("GoogleClientSecret");

        // SaviumDrive.csproj las guarda como [assembly: AssemblyMetadata("Nombre", "valor")]
        private static string Leer(string nombre) =>
            typeof(AppSecrets).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == nombre)?.Value ?? "";
    }
}
