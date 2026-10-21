// Services/InstallerLanguage.cs
// Idioma elegido en el instalador (Instalator-setup). Lógica pura (sin WinUI) para probarla desde SaviumDrive.Tests.

#nullable enable
using System;
using System.IO;
using System.Text.Json;

namespace SaviumDrive.Services
{
    /// <summary>
    /// El instalador guarda el idioma que eligió el usuario en Uninstall.json, junto al .exe
    /// ("language": "es" o "en"). La app lo usa la primera vez que se abre, antes de que exista settings.json.
    /// </summary>
    public static class InstallerLanguage
    {
        public const string RecordFileName = "Uninstall.json";

        /// <summary>
        /// Idioma de la app ("es-ES", "en-US") que corresponde al del instalador, o null si no hay
        /// Uninstall.json (p. ej. al ejecutar desde Visual Studio) o el idioma no está disponible en la app.
        /// </summary>
        public static string? Read(string appDirectory)
        {
            try
            {
                string path = Path.Combine(appDirectory, RecordFileName);
                return File.Exists(path) ? FromRecordJson(File.ReadAllText(path)) : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>Extrae "language" del contenido de Uninstall.json y lo traduce a un idioma de la app.</summary>
        public static string? FromRecordJson(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("language", out var language) || language.ValueKind != JsonValueKind.String)
                    return null;
                return ToAppLanguage(language.GetString());
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>"es" → "es-ES", "en" → "en-US"; cualquier otro idioma no está en la app.</summary>
        public static string? ToAppLanguage(string? installerLanguage) =>
            (installerLanguage ?? "").Trim().ToLowerInvariant() switch
            {
                "es" => "es-ES",
                "en" => "en-US",
                _ => null,
            };
    }
}
