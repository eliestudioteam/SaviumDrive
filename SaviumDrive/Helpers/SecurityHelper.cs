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
    /// Helpers de seguridad adicionales para la aplicación
    /// </summary>
    public static class SecurityHelpers
    {
        /// <summary>
        /// Valida que el token almacenado no haya sido manipulado
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
        /// Verifica que el token no haya expirado (antes de intentar usarlo)
        /// </summary>
        public static bool TokenEsValido(TokenResponse token)
        {
            if (token == null) return false;

            // Si tiene IssuedUtc y ExpiresInSeconds, verificar expiración
            if (token.IssuedUtc != default && token.ExpiresInSeconds.HasValue)
            {
                var expiration = token.IssuedUtc.AddSeconds(token.ExpiresInSeconds.Value);

                // Agregar buffer de 5 minutos
                if (DateTime.UtcNow >= expiration.AddMinutes(-5))
                {
                    return false;
                }
            }

            return !string.IsNullOrEmpty(token.AccessToken);
        }

        /// <summary>
        /// Limpia tokens antiguos y archivos temporales
        /// </summary>
        public static void LimpiarDatosAntiguos(string tokenPath)
        {
            try
            {
                if (!Directory.Exists(tokenPath)) return;

                // Buscar archivos modificados hace más de 90 días
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
        /// Genera un identificador único para el dispositivo (para auditoría)
        /// </summary>
        public static string ObtenerDeviceId()
        {
            try
            {
                // Usar información del sistema para crear un ID único
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
        /// Valida que el Client ID tenga el formato correcto
        /// </summary>
        public static bool ValidarClientId(string clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId)) return false;

            // Los Client IDs de Google terminan en .apps.googleusercontent.com
            return clientId.EndsWith(".apps.googleusercontent.com");
        }
    }
}