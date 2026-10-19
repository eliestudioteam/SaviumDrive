using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using SaviumDrive.Services;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Windows.Globalization;

namespace SaviumDrive
{
    public partial class App : Application
    {
        public static Window MainWindow { get; private set; }
        public static TrayIconService TrayService { get; private set; }
        public static GameDetectorService GameDetector { get; private set; }
        public static NetworkService NetworkService { get; private set; }
        public static GameLibraryService LibraryService { get; set; }
        public static Google.Apis.Drive.v3.DriveService DriveService { get; set; }

        // 🔥 NUEVO: Flag para saber si se inició con Windows
        private static bool _startedWithWindows = false;

        public App()
        {
            // ⚡ OPTIMIZACIÓN: Solo cargar lo ESENCIAL en el constructor
            SettingsService.LoadSettings();

            // Cada PC usa su propia carpeta de partidas (los juegos se sincronizan entre equipos)
            SaviumDrive.Models.GameConfig.EquipoActual = SaviumDrive.Core.SecurityHelpers.ObtenerDeviceId();
            AplicarIdioma();

            this.InitializeComponent();
        }

        private void AplicarIdioma()
        {
            try
            {
                string lang = SettingsService.Current.Language;
                if (lang == "Español") lang = "es-ES";
                if (lang == "English") lang = "en-US";
                ApplicationLanguages.PrimaryLanguageOverride = lang;
            }
            catch { }
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            // 🔥 DETECTAR si se inició con Windows (la entrada del registro pasa este argumento)
            _startedWithWindows = Environment.GetCommandLineArgs()
                .Any(a => a.Equals(SettingsService.StartupArgument, StringComparison.OrdinalIgnoreCase));

            // 1️⃣ Servicios básicos inmediatos
            NetworkService = new NetworkService();

            // El detector debe existir ANTES de crear la ventana: MainWindow se suscribe
            // a GameClosed en su constructor. Crearlo es barato (solo un timer parado);
            // Start() se llama más tarde en IniciarServiciosCompletos.
            GameDetector = new GameDetectorService();

            // 2️⃣ Crear ventana
            MainWindow = new MainWindow();

            if (_startedWithWindows)
            {
                // 🎯 INICIO CON WINDOWS: Minimizar y cargar en segundo plano
                IniciarConWindows();
            }
            else
            {
                // 🎯 INICIO NORMAL: Mostrar ventana y cargar todo
                MainWindow.Activate();
                TrayService = new TrayIconService(MainWindow);
                IniciarServiciosCompletos();
            }
        }

        // 🔥 NUEVO: Inicio optimizado cuando arranca con Windows
        private async void IniciarConWindows()
        {
            // 🔥 Verificar si el usuario quiere minimizar al inicio
            if (SettingsService.Current.MinimizeOnStartup)
            {
                // Opción 1: Iniciar OCULTO en bandeja (sin mostrar ventana)
                // NO llamamos MainWindow.Activate(), la ventana permanece oculta
                TrayService = new TrayIconService(MainWindow);
                // TrayService.MostrarTray() no es necesario porque la ventana nunca se mostró
            }
            else
            {
                // Opción 2: Iniciar VISIBLE normalmente
                MainWindow.Activate();
                TrayService = new TrayIconService(MainWindow);
            }

            // ⏰ ESPERAR 3 segundos antes de iniciar servicios pesados
            // Esto permite que Windows termine de cargar otros programas
            await Task.Delay(3000);

            // Ahora sí, cargar servicios pesados en segundo plano
            IniciarServiciosCompletos();
        }

        // 🔥 NUEVO: Iniciar servicios completos (detector de juegos, etc.)
        private void IniciarServiciosCompletos()
        {
            // Crear detector de juegos
            if (GameDetector == null)
            {
                GameDetector = new GameDetectorService();
            }

            // Solo iniciar si está activado en configuración
            if (SettingsService.Current.AutoDetectGames)
            {
                GameDetector.Start();
            }
        }

        // 🔥 NUEVO: Método para saber si se inició con Windows
        public static bool StartedWithWindows() => _startedWithWindows;

        public static void ReiniciarAplicacion()
        {
            GameDetector?.Stop();
            TrayService?.Dispose();
            AppInstance.Restart("");
        }
    }
}