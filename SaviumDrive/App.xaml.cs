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

#if DEBUG
            // Solo en Debug: abrir el aviso del detector o las novedades sin jugar ni actualizar (para revisarlas)
            if (AbrirVentanaDePrueba()) return;
#endif

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

            // saviumdrive:// para volver a la app desde el navegador al iniciar sesión
            Task.Run(ProtocolService.Registrar);
        }

        /// <summary>
        /// Otra Savium Drive se abrió mientras esta ya estaba abierta (desde el navegador con saviumdrive://,
        /// otro doble clic en el .exe...) y le pasó su activación: se trae esta ventana al frente.
        /// Llega en un hilo de fondo.
        /// </summary>
        public static void AlActivarDesdeOtraInstancia(object sender, AppActivationArguments e)
        {
            string argumentos = (e?.Data as Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs)?.Arguments ?? "";

            // El arranque con Windows no debe abrir la ventana de una app que ya está en marcha
            if (argumentos.Contains(SettingsService.StartupArgument, StringComparison.OrdinalIgnoreCase)) return;

            MostrarVentanaPrincipal();
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
                    // ¿Ya está en la biblioteca? Entonces se ofrece guardar su backup en vez de configurarlo
                    var enBiblioteca = BuscarEnBiblioteca(session.Name);
                    var reminderWindow = new BackupReminderWindow(session.Name, enBiblioteca?.DisplayName);
                    reminderWindow.Closed += (s, args) =>
                    {
                        if (!reminderWindow.Result) return;
                        if (enBiblioteca != null) MostrarVentanaPrincipal(new SolicitudGuardarBackup { JuegoId = enBiblioteca.Id });
                        else MostrarVentanaPrincipal(session); // abrir la biblioteca con los datos del juego detectado
                    };
                    reminderWindow.Activate();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Error mostrando el recordatorio de backup: {ex}");
                }
            });
        }

        /// <summary>Juego de la biblioteca con el mismo nombre (sin mayúsculas, espacios ni símbolos), o null.</summary>
#if DEBUG
        /// <summary>"--probar-aviso" o "--probar-novedades": abre esa ventana sola y cierra la app al cerrarla.</summary>
        private bool AbrirVentanaDePrueba()
        {
            var args = Environment.GetCommandLineArgs();
            Window ventana = null;
            if (args.Contains("--probar-aviso"))
                ventana = new BackupReminderWindow("Alan Wake", args.Contains("--en-biblioteca") ? "ALAN WAKE V1" : null);
            else if (args.Contains("--probar-novedades"))
                ventana = new ChangelogWindow("1.1.0",
                    "## Novedades\n- **Progreso real** al subir, con velocidad y tiempo restante\n- La portada se colorea mientras se sube, como en Epic Games\n" +
                    "  - También al actualizar o restaurar\n- Corrección del botón `Cancelar`\n\n### Mejoras\n1. El aviso del detector ya no se puede redimensionar\n" +
                    "2. Esta ventana se ve bien con cualquier escala de pantalla\n\n---\n**Full Changelog**: https://github.com/eliestudioteam/SaviumDrive/compare/v1.0.0...v1.1.0");
            if (ventana == null) return false;

            ventana.Closed += (_, __) => Exit();
            ventana.Activate();
            return true;
        }
#endif

        private static Models.GameConfig BuscarEnBiblioteca(string nombre)
        {
            string buscado = LudusaviIndex.Normalizar(nombre);
            if (buscado.Length == 0) return null;
            try
            {
                return GameLibraryService.LeerBibliotecaLocal()
                    .FirstOrDefault(j => LudusaviIndex.Normalizar(j.Name) == buscado);
            }
            catch { return null; }
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
            // La nueva instancia espera a que esta termine de cerrarse (ver Program)
            AppInstance.Restart(Program.ArgumentoReinicio);
        }
    }
}
