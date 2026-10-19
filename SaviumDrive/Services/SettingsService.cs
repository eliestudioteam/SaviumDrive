using Newtonsoft.Json;
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.Win32;

namespace SaviumDrive.Services
{
    public class AppConfig
    {
        public bool StartWithWindows { get; set; } = false;
        public bool DarkMode { get; set; } = true;
        public string Language { get; set; } = "es-ES";
        public bool MinimizeToTray { get; set; } = false;
        public bool AutoDetectGames { get; set; } = false;

        // 🔥 NUEVO: Minimizar al inicio si arranca con Windows
        public bool MinimizeOnStartup { get; set; } = true;

        public int WindowX { get; set; } = 0;
        public int WindowY { get; set; } = 0;
        public int WindowWidth { get; set; } = 1280;
        public int WindowHeight { get; set; } = 720;

        // Última actualización descargada (para mostrar el changelog)
        public string LastUpdateVersion { get; set; }
        public string LastUpdateChangelog { get; set; }

        // Última versión que se ejecutó (para detectar la primera ejecución tras actualizar)
        public string LastRunVersion { get; set; }
    }

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

        public static void LoadSettings()
        {
            if (File.Exists(_filePath))
            {
                try
                {
                    string json = File.ReadAllText(_filePath);
                    Current = JsonConvert.DeserializeObject<AppConfig>(json) ?? new AppConfig();
                    if (Current.Language == "Español") Current.Language = "es-ES";
                    if (Current.Language == "English") Current.Language = "en-US";
                }
                catch { Current = new AppConfig(); }
            }
            else { Current = new AppConfig(); }
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

        // 🔥 Inicio con Windows mediante HKCU\...\Run (no requiere permisos de administrador)
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
                System.Diagnostics.Debug.WriteLine($"❌ Error configurando inicio: {ex.Message}");
            }

            return Task.CompletedTask;
        }
    }
}