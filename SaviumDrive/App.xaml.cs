using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using SaviumDrive.Services;
using SaviumDrive.Views;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Windows.Globalization;

namespace SaviumDrive
{
    public partial class App : Application
    {
        /// <summary>Ventana principal, o null mientras la app está solo en la bandeja.</summary>
        public static Window MainWindow { get; private set; }
        public static TrayIconService TrayService { get; private set; }
        public static GameDetectorService GameDetector { get; private set; }
        public static NetworkService NetworkService { get; private set; }
        public static GameLibraryService LibraryService { get; set; }
        public static Google.Apis.Drive.v3.DriveService DriveService { get; set; }

        private static DispatcherQueue _dispatcher;

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
            _dispatcher = DispatcherQueue.GetForCurrentThread();

            // La app sigue viva en la bandeja aunque no quede ninguna ventana abierta;
            // para salir se llama a SalirAplicacion()
            DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;

            // 🔥 DETECTAR si se inició con Windows (la entrada del registro pasa este argumento)
            _startedWithWindows = Environment.GetCommandLineArgs()
                .Any(a => a.Equals(SettingsService.StartupArgument, StringComparison.OrdinalIgnoreCase));

            // 1️⃣ Servicios básicos inmediatos
            NetworkService = new NetworkService();
            TrayService = new TrayIconService(_dispatcher);

            // El recordatorio de backup lo gestiona App: debe funcionar aunque no haya ventana
            GameDetector = new GameDetectorService();
            GameDetector.GameClosed += OnGameClosed;

            if (_startedWithWindows && SettingsService.Current.MinimizeOnStartup)
            {
                // 🎯 INICIO CON WINDOWS OCULTO: solo el icono de la bandeja.
                // La ventana no se crea hasta que el usuario la abre (no ocupa memoria).
                TrayService.MostrarIcono();
                TrayService.SetModoEficiencia(true);

                // Devolver a Windows la memoria del arranque (igual que al minimizar a la bandeja)
                ProgramarLiberarMemoria();
            }
            else
            {
                // 🎯 INICIO NORMAL: Mostrar ventana
                CrearVentanaPrincipal().Activate();
            }

            IniciarServiciosCompletos();
        }

        // 🔥 NUEVO: Iniciar servicios completos (detector de juegos, etc.)
        private async void IniciarServiciosCompletos()
        {
            // ⏰ Al arrancar con Windows, esperar 3 segundos para no competir con otros programas
            if (_startedWithWindows) await Task.Delay(3000);

            // Solo iniciar si está activado en configuración
            if (SettingsService.Current.AutoDetectGames)
            {
                GameDetector.Start();
            }
        }

        // 🔥 NUEVO: Método para saber si se inició con Windows
        public static bool StartedWithWindows() => _startedWithWindows;

        // ===================== VENTANA PRINCIPAL Y BANDEJA =====================

        private static MainWindow CrearVentanaPrincipal()
        {
            var ventana = new MainWindow();
            ventana.Closed += (s, e) =>
            {
                // Si la ventana se destruyó de verdad (no solo se ocultó), olvidarla
                if (!e.Handled && ReferenceEquals(MainWindow, ventana)) MainWindow = null;
            };
            MainWindow = ventana;
            return ventana;
        }

        /// <summary>
        /// Muestra la ventana principal (creándola si estaba liberada en la bandeja).
        /// Si se pasa un parámetro, abre la biblioteca con él (ej. el juego detectado).
        /// </summary>
        public static void MostrarVentanaPrincipal(object parametroBiblioteca = null)
        {
            _dispatcher?.TryEnqueue(() =>
            {
                TrayService?.SetModoEficiencia(false);

                if (MainWindow is MainWindow existente)
                {
                    existente.RestaurarYMostrarApp();
                    if (parametroBiblioteca != null) existente.AbrirBiblioteca(parametroBiblioteca);
                }
                else
                {
                    var nueva = CrearVentanaPrincipal();
                    nueva.NavegacionPendiente = parametroBiblioteca;
                    nueva.Activate();
                }
            });
        }

        /// <summary>
        /// La ventana se cierra con "Minimizar a la bandeja" activo.
        /// Devuelve true si puede destruirse para liberar memoria, o false si solo se ocultó
        /// porque hay una subida/descarga en curso (se liberará al terminar).
        /// </summary>
        public static bool PrepararCierreABandeja(MainWindow ventana)
        {
            TrayService?.MostrarIcono();

            if (LibraryService != null && LibraryService.IsBusy)
            {
                TrayService?.OcultarVentana(ventana);
                EventHandler<GlobalProgressArgs> alTerminar = null;
                alTerminar = (s, e) =>
                {
                    if (e.IsActive) return;
                    LibraryService.ProgressChanged -= alTerminar;
                    _dispatcher?.TryEnqueue(() =>
                    {
                        // Si el usuario no la volvió a abrir mientras tanto, liberarla ahora
                        if (ReferenceEquals(MainWindow, ventana) && !ventana.EstaVisible) ventana.CerrarParaLiberar();
                    });
                };
                LibraryService.ProgressChanged += alTerminar;
                return false;
            }

            ProgramarLiberarMemoria();
            return true;
        }

        /// <summary>Tras destruir la ventana, recoger la memoria que dejó y pasar a modo eficiencia.</summary>
        private static void ProgramarLiberarMemoria()
        {
            _dispatcher?.TryEnqueue(DispatcherQueuePriority.Low, async () =>
            {
                // Dar tiempo a XAML a soltar el árbol visual de la ventana cerrada
                await Task.Delay(1500);
                if (MainWindow != null) return; // se volvió a abrir

                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

                // Devolver a Windows las páginas que ya no se usan (vuelven solas si se necesitan)
                try { EmptyWorkingSet(System.Diagnostics.Process.GetCurrentProcess().Handle); } catch { }

                TrayService?.SetModoEficiencia(true);
            });
        }

        [System.Runtime.InteropServices.DllImport("psapi.dll")]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        // --- DETECCIÓN DE JUEGOS ---
        private static void OnGameClosed(object sender, GameSession session)
        {
            _dispatcher?.TryEnqueue(() =>
            {
                // Un fallo al crear el recordatorio no debe tumbar la app entera
                try
                {
                    var reminderWindow = new BackupReminderWindow();
                    reminderWindow.Closed += (s, args) =>
                    {
                        // Si el usuario dijo "SÍ": abrir la biblioteca con los datos del juego detectado
                        if (reminderWindow.Result) MostrarVentanaPrincipal(session);
                    };
                    reminderWindow.Activate();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Error mostrando el recordatorio de backup: {ex}");
                }
            });
        }

        public static void SalirAplicacion()
        {
            GameDetector?.Stop();
            TrayService?.Dispose();
            try { Current.Exit(); } catch { }
        }

        public static void ReiniciarAplicacion()
        {
            GameDetector?.Stop();
            TrayService?.Dispose();
            AppInstance.Restart("");
        }
    }
}
