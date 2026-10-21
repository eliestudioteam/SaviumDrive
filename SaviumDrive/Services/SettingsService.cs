using Newtonsoft.Json;
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.Win32;

namespace SaviumDrive.Services
{
    /// <summary>Configuración de la app. Se guarda en %APPDATA%\SaviumDrive\settings.json.</summary>
    public class AppConfig
    {
        public bool StartWithWindows { get; set; } = false;
        public bool DarkMode { get; set; } = true;
        public string Language { get; set; } = "es-ES";
        public bool MinimizeToTray { get; set; } = false;
        public bool AutoDetectGames { get; set; } = false;

        // Al arrancar con Windows, quedarse solo en la bandeja sin abrir la ventana
        public bool MinimizeOnStartup { get; set; } = true;

        // Posición y tamaño de la ventana principal (se restauran al abrirla)
        public int WindowX { get; set; } = 0;
        public int WindowY { get; set; } = 0;
        public int WindowWidth { get; set; } = 1280;
        public int WindowHeight { get; set; } = 720;

        // Orden de la biblioteca (NombreAZ, NombreZA, UltimoBackup)
        public string LibrarySort { get; set; } = "NombreAZ";

        // Mostrar "Ahora todas las acciones serán sobre esta versión" al cambiar de versión
        public bool ShowVersionSwitchNotice { get; set; } = true;

        // Última actualización descargada (para mostrar el changelog)
        public string LastUpdateVersion { get; set; }
        public string LastUpdateChangelog { get; set; }

        // Última versión que se ejecutó (para detectar la primera ejecución tras actualizar)
        public string LastRunVersion { get; set; }
    }

    /// <summary>Carga y guarda la configuración, el tema y el inicio automático con Windows.</summary>
    public static class SettingsService
    {
        private static string _filePath;
        public static AppConfig Current { get; private set; }
        public static event Action LanguageChanged;

        static SettingsService()
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SaviumDrive");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            _filePath = Path.Combine(folder, "settings.json");
            LoadSettings();
        }

        /// <summary>Lee settings.json. Si no existe o está dañado, usa los valores por defecto.</summary>
        public static void LoadSettings()
        {
            if (File.Exists(_filePath))
            {
                try
                {
                    string json = File.ReadAllText(_filePath);
                    Current = JsonConvert.DeserializeObject<AppConfig>(json) ?? new AppConfig();
                    // Versiones antiguas guardaban el nombre del idioma en vez del código
                    if (Current.Language == "Español") Current.Language = "es-ES";
                    if (Current.Language == "English") Current.Language = "en-US";
                }
                catch { Current = new AppConfig(); }
            }
            else
            {
                // Primera vez que se abre: el idioma que el usuario eligió en el instalador, si la app lo tiene.
                // Después manda siempre lo que elija en Configuración (una actualización no lo cambia).
                Current = new AppConfig();
                string idiomaInstalador = InstallerLanguage.Read(AppContext.BaseDirectory);
                if (idiomaInstalador != null) Current.Language = idiomaInstalador;
            }
        }

        public static void Save()
        {
            try
            {
                string json = JsonConvert.SerializeObject(Current, Formatting.Indented);
                File.WriteAllText(_filePath, json);
            }
            catch { }
        }

        public static void NotifyLanguageChanged() => LanguageChanged?.Invoke();

        /// <summary>Aplica el tema claro u oscuro a la ventana principal abierta.</summary>
        public static void AplicarTema()
        {
            if (App.MainWindow != null && App.MainWindow.Content is FrameworkElement rootElement)
            {
                rootElement.RequestedTheme = Current.DarkMode ? ElementTheme.Dark : ElementTheme.Light;
            }
        }

        // Argumento que se pasa al .exe cuando arranca con Windows
        public const string StartupArgument = "--startup";
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "SaviumDrive";

        /// <summary>
        /// Activa o desactiva el inicio con Windows mediante HKCU\Software\Microsoft\Windows\CurrentVersion\Run
        /// (solo para el usuario actual, sin permisos de administrador). Al arrancar así, el .exe recibe
        /// <see cref="StartupArgument"/>.
        /// </summary>
        public static Task ConfigurarArranqueWindows(bool activar)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

                if (activar)
                {
                    string exePath = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        key.SetValue(RunValueName, $"\"{exePath}\" {StartupArgument}");
                    }
                }
                else
                {
                    key.DeleteValue(RunValueName, throwOnMissingValue: false);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error configurando inicio: {ex.Message}");
            }

            return Task.CompletedTask;
        }
    }
}