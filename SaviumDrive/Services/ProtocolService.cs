// Services/ProtocolService.cs
// Protocolo saviumdrive:// para que el navegador pueda volver a la app ("¿Abrir Savium Drive?")
// al terminar el inicio de sesión. Se registra para el usuario actual (sin permisos de administrador).
using Microsoft.Win32;
using System;
using System.Diagnostics;

namespace SaviumDrive.Services
{
    public static class ProtocolService
    {
        public const string Esquema = "saviumdrive";

        /// <summary>Enlace que abre (o trae al frente) la app.</summary>
        public const string EnlaceAbrir = Esquema + "://abrir";

        /// <summary>
        /// Registra el protocolo apuntando a este .exe. Se llama al arrancar: si la app se movió de carpeta,
        /// el registro se actualiza solo. No escribe nada si ya está bien.
        /// </summary>
        public static void Registrar()
        {
            try
            {
                string exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return;
                string comando = $"\"{exe}\" \"%1\"";

                using var clave = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Esquema}");
                using var abrir = clave.CreateSubKey(@"shell\open\command");
                if (string.Equals(abrir.GetValue("") as string, comando, StringComparison.OrdinalIgnoreCase)) return;

                clave.SetValue("", "URL:Savium Drive");
                clave.SetValue("URL Protocol", "");
                using (var icono = clave.CreateSubKey("DefaultIcon")) icono.SetValue("", $"\"{exe}\",0");
                abrir.SetValue("", comando);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠️ No se pudo registrar {Esquema}://: {ex.Message}");
            }
        }

        /// <summary>True si la app se abrió desde un enlace saviumdrive:// (ej. desde el navegador).</summary>
        public static bool EsEnlaceDeLaApp(string argumentos) =>
            !string.IsNullOrEmpty(argumentos) && argumentos.IndexOf(Esquema + "://", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
