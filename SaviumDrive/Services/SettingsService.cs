using Newtonsoft.Json;
using System;
using System.IO;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Microsoft.UI.Xaml;

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

        public static async Task ConfigurarArranqueWindows(bool activar)
        {
            try
            {
                var startupTask = await StartupTask.GetAsync("SaviumDriveStartupId");

                if (activar)
                {
                    if (startupTask.State != StartupTaskState.Enabled)
                    {
                        var result = await startupTask.RequestEnableAsync();

                        // 🔥 NUEVO: Verificar el resultado
                        if (result != StartupTaskState.Enabled)
                        {
                            System.Diagnostics.Debug.WriteLine($"⚠️ No se pudo habilitar el inicio: {result}");
                        }
                    }
                }
                else
                {
                    if (startupTask.State == StartupTaskState.Enabled)
                    {
                        startupTask.Disable();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error configurando inicio: {ex.Message}");
            }
        }
    }
}