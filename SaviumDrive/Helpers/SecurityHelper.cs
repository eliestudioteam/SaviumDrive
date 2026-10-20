using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Responses;

namespace SaviumDrive.Core
{
    /// <summary>
    /// Comprobaciones sobre la sesión guardada de Google y el identificador de este equipo.
    /// </summary>
    public static class SecurityHelpers
    {
        /// <summary>
        /// Comprueba que los archivos de la sesión guardada existan y no estén vacíos (un archivo vacío o
        /// cortado haría fallar el inicio de sesión automático).
        /// </summary>
        public static async Task<bool> ValidarIntegridadToken(string tokenPath)
        {
            try
            {
                if (!Directory.Exists(tokenPath)) return false;

                var files = Directory.GetFiles(tokenPath);
                if (files.Length == 0) return false;

                // Verificar que los archivos no estén corruptos
                foreach (var file in files)
                {
                    var content = await File.ReadAllTextAsync(file);
                    if (string.IsNullOrWhiteSpace(content)) return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// True si el token de acceso todavía no ha caducado (con 5 minutos de margen).
        /// </summary>
        public static bool TokenEsValido(TokenResponse token)
        {
            if (token == null) return false;

            if (token.IssuedUtc != default && token.ExpiresInSeconds.HasValue)
            {
                var expiration = token.IssuedUtc.AddSeconds(token.ExpiresInSeconds.Value);

                if (DateTime.UtcNow >= expiration.AddMinutes(-5))
                {
                    return false;
                }
            }

            return !string.IsNullOrEmpty(token.AccessToken);
        }

        /// <summary>
        /// Borra los archivos de sesión que llevan más de 90 días sin modificarse.
        /// </summary>
        public static void LimpiarDatosAntiguos(string tokenPath)
        {
            try
            {
                if (!Directory.Exists(tokenPath)) return;

                var files = Directory.GetFiles(tokenPath);
                foreach (var file in files)
                {
                    var info = new FileInfo(file);
                    if (DateTime.Now - info.LastWriteTime > TimeSpan.FromDays(90))
                    {
                        File.Delete(file);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error limpiando datos: {ex.Message}");
            }
        }

        /// <summary>
        /// Identificador estable de este equipo y usuario de Windows (hash del nombre del equipo y del usuario).
        /// Permite que cada PC recuerde su propia carpeta de partidas para un mismo juego
        /// (ver <c>GameConfig.RutasPorEquipo</c>). No se envía a ningún servidor.
        /// </summary>
        public static string ObtenerDeviceId()
        {
            try
            {
                string computerName = Environment.MachineName;
                string userName = Environment.UserName;
                string combined = $"{computerName}-{userName}";

                using (var sha256 = SHA256.Create())
                {
                    var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));
                    return Convert.ToBase64String(hash).Substring(0, 16);
                }
            }
            catch
            {
                return "unknown-device";
            }
        }

        /// <summary>
        /// True si el Client ID tiene el formato de Google (termina en .apps.googleusercontent.com).
        /// </summary>
        public static bool ValidarClientId(string clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId)) return false;

            return clientId.EndsWith(".apps.googleusercontent.com");
        }
    }
}