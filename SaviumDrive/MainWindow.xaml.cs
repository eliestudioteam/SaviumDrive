using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SaviumDrive.Core;
using SaviumDrive.Services;
using SaviumDrive.Views;
using System;
using SaviumDrive.Helpers;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI;

namespace SaviumDrive
{
    public sealed partial class MainWindow : Window
    {
        // --- 1. CONFIGURACIÓN DEL TAMAÑO MÍNIMO ---
        private const int MinWidth = 1100;
        private const int MinHeight = 600;

        // 🆕 NUEVO: Tamaño inicial de la ventana
        private const int InitialWidth = 1280;
        private const int InitialHeight = 720;

        private CancellationTokenSource _loginCts;

        // --- P/INVOKE: Para restaurar la ventana ---
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_RESTORE = 9;

        // --- P/INVOKE: Para limitar el tamaño (NUEVO) ---
        // SetWindowLongPtr solo existe en el user32 de 64 bits; en x86 hay que usar SetWindowLong
        [DllImport("User32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, WinProc newProc);

        [DllImport("User32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, WinProc newProc);

        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, WinProc newProc) =>
            IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, newProc) : SetWindowLong32(hWnd, nIndex, newProc);

        [DllImport("User32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private const int GWL_WNDPROC = -4;
        private const int WM_GETMINMAXINFO = 0x0024;

        private delegate IntPtr WinProc(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        private WinProc _newWndProc; // Mantenemos referencia para evitar Garbage Collection
        private IntPtr _oldWndProc;

        // --- VARIABLES TUYAS ---
        private DispatcherTimer _splashTimer;
        private object _originalButtonContent;
        private bool _forceClosing = false;
        private string _paginaActual = "Home";
        private AppWindow _appWindow;

        public MainWindow()
        {
            this.InitializeComponent();

            // Suscribirse a cambios de conectividad
            if (App.NetworkService != null)
            {
                App.NetworkService.ConnectivityChanged += OnConnectivityChanged;
                _ = CheckInitialConnectivity();
            }

            // Obtener referencia a la ventana nativa
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

            // Conectar el limitador de tamaño
            _newWndProc = new WinProc(NewWindowProc);
            _oldWndProc = SetWindowLongPtr(hWnd, GWL_WNDPROC, _newWndProc);

            WindowId wndId = Win32Interop.GetWindowIdFromWindow(hWnd);
            _appWindow = AppWindow.GetFromWindowId(wndId);

            // 🔥 CENTRAR LA VENTANA AL INICIAR
            CentrarVentana();

            // 1. Cargar configuración
            SettingsService.LoadSettings();

            // Aplicar tema inicial
            if (this.Content is FrameworkElement root)
            {
                root.RequestedTheme = SettingsService.Current.DarkMode ? ElementTheme.Dark : ElementTheme.Light;
            }

            // Aplicar colores a los botones de la ventana
            ActualizarColoresTitleBar();

            string iconPath = System.IO.Path.Combine(
                AppContext.BaseDirectory,
                "Assets", "tray-icon1024x1024.ico");

            this.AppWindow.SetIcon(iconPath);

            // Extender contenido a la barra de título
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            this.Title = "Savium Drive";

            _originalButtonContent = btnLogin.Content;
            this.Closed += MainWindow_Closed;

            // 2. Suscribirse al Detector de Juegos
            if (App.GameDetector != null)
            {
                App.GameDetector.GameClosed += OnGameClosed;
            }

            ActualizarEstiloBotones("Home");
            TxtLoginBtn.Text = ResourceHelper.GetString("Login_Button_Start");
            IniciarCargaInicial();
        }


        // =====================================================================
        // [NUEVO] MÉTODOS Y ESTRUCTURAS PARA EL TAMAÑO DE VENTANA
        // (Pon esto antes de cerrar la clase, o al final junto a tus otros métodos)
        // =====================================================================

        private IntPtr NewWindowProc(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam)
        {
            if (Msg == WM_GETMINMAXINFO)
            {
                var minMaxInfo = Marshal.PtrToStructure<MINMAXINFO>(lParam);

                // Detectar escala de pantalla (DPI) para monitores 4K o con Zoom
                var scale = this.Content != null && this.Content.XamlRoot != null
                            ? this.Content.XamlRoot.RasterizationScale
                            : 1.0;

                minMaxInfo.ptMinTrackSize.x = (int)(MinWidth * scale);
                minMaxInfo.ptMinTrackSize.y = (int)(MinHeight * scale);

                Marshal.StructureToPtr(minMaxInfo, lParam, true);
            }
            return CallWindowProc(_oldWndProc, hWnd, Msg, wParam, lParam);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        /// <summary>
        /// Centra la ventana en la pantalla actual
        /// </summary>
        private void CentrarVentana()
        {
            try
            {
                if (_appWindow == null) return;

                var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
                    _appWindow.Id,
                    Microsoft.UI.Windowing.DisplayAreaFallback.Primary
                );

                if (displayArea == null) return;

                var workArea = displayArea.WorkArea;
                int screenWidth = workArea.Width;
                int screenHeight = workArea.Height;

                // 🔥 USAR LAS CONSTANTES
                int windowWidth = InitialWidth;
                int windowHeight = InitialHeight;

                // 🔥 VALIDAR: Si la pantalla es más pequeña, ajustar al tamaño mínimo
                if (screenWidth < windowWidth)
                    windowWidth = Math.Max(MinWidth, screenWidth - 100);

                if (screenHeight < windowHeight)
                    windowHeight = Math.Max(MinHeight, screenHeight - 100);

                // Calcular posición centrada
                int x = (screenWidth - windowWidth) / 2 + workArea.X;
                int y = (screenHeight - windowHeight) / 2 + workArea.Y;

                // Aplicar tamaño y posición
                _appWindow.MoveAndResize(new Windows.Graphics.RectInt32
                {
                    X = x,
                    Y = y,
                    Width = windowWidth,
                    Height = windowHeight
                });

                System.Diagnostics.Debug.WriteLine($"✅ Ventana centrada: {windowWidth}x{windowHeight} en ({x}, {y})");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Error centrando ventana: {ex.Message}");
            }
        }
        private void GuardarPosicionVentana()
        {
            try
            {
                if (_appWindow == null) return;

                var position = _appWindow.Position;
                var size = _appWindow.Size;

                // Guardar en configuración (si tienes estas propiedades en SettingsService)
                SettingsService.Current.WindowX = position.X;
                SettingsService.Current.WindowY = position.Y;
                SettingsService.Current.WindowWidth = size.Width;
                SettingsService.Current.WindowHeight = size.Height;

                SettingsService.Save();

                System.Diagnostics.Debug.WriteLine($"✅ Posición guardada: {position.X}, {position.Y} | Tamaño: {size.Width}x{size.Height}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Error guardando posición: {ex.Message}");
            }
        }
        private void RestaurarOCentrarVentana()
        {
            try
            {
                if (_appWindow == null) return;

                // Si hay posición guardada y es válida, restaurarla
                if (SettingsService.Current.WindowX > 0 && SettingsService.Current.WindowY > 0)
                {
                    _appWindow.MoveAndResize(new Windows.Graphics.RectInt32
                    {
                        X = SettingsService.Current.WindowX,
                        Y = SettingsService.Current.WindowY,
                        Width = Math.Max(MinWidth, SettingsService.Current.WindowWidth),
                        Height = Math.Max(MinHeight, SettingsService.Current.WindowHeight)
                    });

                    System.Diagnostics.Debug.WriteLine("✅ Posición restaurada desde configuración");
                }
                else
                {
                    // Si no hay posición guardada, centrar
                    CentrarVentana();
                }
            }
            catch
            {
                // Si falla la restauración, centrar por defecto
                CentrarVentana();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize; // Límite mínimo
            public POINT ptMaxTrackSize;
        }



        // 🔥 MÉTODOS PÚBLICOS PARA ACCEDER A DATOS DEL USUARIO
        public string GetUserName()
        {
            return TxtUserName?.Text ?? "Usuario";
        }

        public string GetUserEmail()
        {
            return TxtUserEmail?.Text ?? "";
        }

        public string GetUserPhotoUri()
        {
            if (UserAvatar?.ProfilePicture is BitmapImage bitmapImage)
            {
                return bitmapImage.UriSource?.ToString() ?? "";
            }
            return "";
        }

        private async Task CheckInitialConnectivity()
        {
            if (App.NetworkService == null) return;

            bool isConnected = await App.NetworkService.IsConnectedAsync();

            this.DispatcherQueue.TryEnqueue(() =>
            {
                NoInternetBar.IsOpen = !isConnected;
            });
        }

        private void OnConnectivityChanged(object sender, bool isConnected)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                NoInternetBar.IsOpen = !isConnected;
            });
        }

        // --- DETECCIÓN DE JUEGOS ---
        private void OnGameClosed(object sender, GameSession session)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                // Abrir ventana de recordatorio independiente
                var reminderWindow = new BackupReminderWindow(); // Mensaje genérico

                reminderWindow.Closed += (s, args) =>
                {
                    if (reminderWindow.Result)
                    {
                        // Si el usuario dijo "SÍ":
                        RestaurarYMostrarApp();

                        // Navegar a la librería pasando los datos del juego detectado
                        ContentFrame.Navigate(typeof(LibraryPage), session);
                        ActualizarEstiloBotones("Library");
                    }
                };

                reminderWindow.Activate();
            });
        }

        private void RestaurarYMostrarApp()
        {
            try
            {
                // Restaurar visualmente si estaba minimizada
                if (_appWindow != null) _appWindow.Show();

                // Forzar restauración de sistema (Win32)
                IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                ShowWindow(hWnd, SW_RESTORE);
                SetForegroundWindow(hWnd);

                this.Activate(); // Traer al frente y dar foco
            }
            catch { }
        }

        // --- GESTIÓN DE USUARIO E INICIO ---

        private void IniciarCargaInicial()
        {
            _splashTimer = new DispatcherTimer();
            _splashTimer.Interval = TimeSpan.FromSeconds(1);
            _splashTimer.Tick += async (s, e) => { _splashTimer.Stop(); await VerificarSesionYRedirigir(); };
            _splashTimer.Start();
        }

        private async Task VerificarSesionYRedirigir()
        {
            if (GoogleAuth.ExisteSesionGuardada())
            {
                try
                {
                    // CORRECCIÓN AQUÍ: Agregamos 'CancellationToken.None'
                    // Esto indica que esta llamada específica no se cancelará manualmente.
                    var service = await GoogleAuth.Autenticar(CancellationToken.None);

                    await CargarDatosUsuario(service);
                    this.DispatcherQueue.TryEnqueue(() => { IrAlMain(); });
                    return;
                }
                catch { }
            }
            this.DispatcherQueue.TryEnqueue(() => { SplashGrid.Visibility = Visibility.Collapsed; LoginGrid.Visibility = Visibility.Visible; });
        }

        private async void btnLogin_Click(object sender, RoutedEventArgs e)
        {
            // --- ESCENARIO 1: EL USUARIO QUIERE CANCELAR ---
            if (_loginCts != null)
            {
                // 1. Feedback visual inmediato
                TxtLoginBtn.Text = ResourceHelper.GetString("Login_Button_Cancelling");
                btnLogin.IsEnabled = false;

                // 2. Ejecutar la cancelación
                try { _loginCts.Cancel(); } catch { }

                // 3. 🔥 FORZAR RESTAURACIÓN MANUAL
                var oldCts = _loginCts;
                _loginCts = null;

                // Esperamos un segundo para que el usuario lea "Cancelando..."
                await Task.Delay(1000);

                // Restauramos el botón
                btnLogin.IsEnabled = true;

                // ✅ CORREGIDO: Usamos la clave SIN PUNTOS que C# sí puede leer perfectamente
                TxtLoginBtn.Text = ResourceHelper.GetString("Login_Button_Start");

                try { oldCts.Dispose(); } catch { }

                return;
            }

            // --- ESCENARIO 2: INICIAR SESIÓN (Lógica Normal) ---

            // 1. Validaciones
            var (intentos, minutosRestantes) = GoogleAuth.ObtenerEstadoRateLimit();
            if (minutosRestantes > 0)
            {
                string msg = ResourceHelper.GetString("Login_Wait_Message", minutosRestantes);
                await MostrarError(ResourceHelper.GetString("Login_Error_Title"), msg);
                return;
            }

            // 2. Configurar UI
            // Usamos la clave de cancelar
            TxtLoginBtn.Text = ResourceHelper.GetString("Login_Button_Cancel");

            // 3. Configurar Timeout y Token
            var currentCts = new CancellationTokenSource();
            _loginCts = currentCts;

            currentCts.CancelAfter(TimeSpan.FromSeconds(60));

            try
            {
                var service = await GoogleAuth.Autenticar(currentCts.Token);

                await CargarDatosUsuario(service);
                this.DispatcherQueue.TryEnqueue(() => { IrAlMain(); });
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("Inicio de sesión cancelado.");
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("Demasiados intentos"))
                {
                    await MostrarError(ResourceHelper.GetString("Login_Error_Title"), ex.Message);
                }
                else
                {
                    // MODIFICACIÓN RECOMENDADA:
                    // Muestra el título genérico, pero agrega el mensaje técnico al final 
                    // para que el usuario pueda reportártelo o entender si es firewall.
                    string mensajeUsuario = ResourceHelper.GetString("Login_Error_Generic");
                    string mensajeTecnico = $"\n\nDetalle técnico:\n{ex.Message}";

                    await MostrarError(ResourceHelper.GetString("Login_Error_Title"), mensajeUsuario + mensajeTecnico);
                }
            }
            finally
            {
                // 4. RESTAURACIÓN SEGURA DEL ESTADO
                this.DispatcherQueue.TryEnqueue(() =>
                {
                    if (_loginCts == currentCts)
                    {
                        _loginCts.Dispose();
                        _loginCts = null;

                        btnLogin.IsEnabled = true;

                        // ✅ CORREGIDO: Usamos la clave SIN PUNTOS aquí también
                        TxtLoginBtn.Text = ResourceHelper.GetString("Login_Button_Start");
                    }
                    else
                    {
                        try { currentCts.Dispose(); } catch { }
                    }
                });
            }
        }

        // 🆕 AGREGAR ESTE NUEVO MÉTODO (después de MostrarError)
        private async Task MostrarAdvertencia(string titulo, string detalle)
        {
            ContentDialog dialog = new ContentDialog
            {
                Title = titulo,
                Content = detalle,
                CloseButtonText = "Entendido",
                XamlRoot = this.Content.XamlRoot
            };

            dialog.RequestedTheme = SettingsService.Current.DarkMode ? ElementTheme.Dark : ElementTheme.Light;

            // Usar el estilo de advertencia si existe, sino usar el estándar
            if (Application.Current.Resources.ContainsKey("StandardBtnStyle"))
            {
                dialog.CloseButtonStyle = (Style)Application.Current.Resources["StandardBtnStyle"];
            }

            await dialog.ShowAsync();
        }


        private async Task CargarDatosUsuario(Google.Apis.Drive.v3.DriveService service)
        {
            var aboutRequest = service.About.Get();
            aboutRequest.Fields = "user(displayName, emailAddress, photoLink)";
            var about = await aboutRequest.ExecuteAsync();

            // 🔥 INICIALIZACIÓN DEL SERVICIO GLOBAL
            // Aquí creamos la instancia única que usará toda la app para subir archivos
            if (App.LibraryService == null)
            {
                App.LibraryService = new GameLibraryService(service);

                // Nos suscribimos para actualizar el Círculo Flotante
                App.LibraryService.ProgressChanged += OnGlobalUploadProgress;
            }

            this.DispatcherQueue.TryEnqueue(() =>
            {
                TxtUserName.Text = about.User.DisplayName;
                TxtUserEmail.Text = about.User.EmailAddress;
                if (!string.IsNullOrEmpty(about.User.PhotoLink)) UserAvatar.ProfilePicture = new BitmapImage(new Uri(about.User.PhotoLink));
            });
        }

        // --- INDICADOR GLOBAL DE SUBIDA (Círculo Flotante) ---
        private void OnGlobalUploadProgress(object sender, GlobalProgressArgs e)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                if (e.IsActive)
                {
                    GlobalUploadIndicator.Visibility = Visibility.Visible;

                    // Si es descarga (-1), es indeterminado. Si es subida (>=0), mostramos %
                    GlobalRing.IsIndeterminate = (e.Percentage < 0);

                    if (e.Percentage >= 0)
                    {
                        GlobalRing.Value = e.Percentage;
                        GlobalPercentText.Text = $"{e.Percentage}%";
                    }
                    else
                    {
                        GlobalPercentText.Text = ""; // Limpiar texto en descargas indeterminadas
                    }

                    // Actualizar textos laterales
                    GlobalStatusText.Text = e.OperationName; // "Subiendo..."
                    GlobalGameName.Text = e.GameName;        // "GTA IV"
                }
                else
                {
                    GlobalUploadIndicator.Visibility = Visibility.Collapsed;
                }
            });
        }

        // --- NAVEGACIÓN Y UI ---

        // 🔥 MÉTODO PÚBLICO PARA NAVEGAR DESDE OTRAS PÁGINAS Y ACTUALIZAR EL MENÚ
        public void NavegarExterna(Type pagina, string tagMenu, object parametro = null)
        {
            // 1. Navegar el Frame
            ContentFrame.Navigate(pagina, parametro);

            // 2. Actualizar visualmente los botones laterales
            ActualizarEstiloBotones(tagMenu);
        }

        private void IrAlMain()
        {
            LoginGrid.Visibility = Visibility.Collapsed;
            MainGrid.Visibility = Visibility.Visible;
            try
            {
                if (!ContentFrame.Navigate(typeof(HomePage))) ContentFrame.Content = new HomePage();
                ActualizarEstiloBotones("Home");

                // 🔥 Verificar si hay changelog para mostrar
                CheckFirstRunAfterUpdate();
            }
            catch { }
        }

        // 🔥 NUEVO: Verificar si es primera ejecución después de actualizar
        private async void CheckFirstRunAfterUpdate()
        {
            await Task.Delay(1500); // Esperar a que cargue la UI

            var updateService = new UpdateService();
            var (isFirstRun, version, changelog) = updateService.CheckFirstRunAfterUpdate();

            // Instalación manual: no hay changelog guardado, pedirlo a GitHub
            if (isFirstRun && string.IsNullOrEmpty(changelog))
            {
                changelog = await updateService.GetReleaseNotesAsync(version);
            }

            if (isFirstRun && !string.IsNullOrEmpty(changelog))
            {
                this.DispatcherQueue.TryEnqueue(() =>
                {
                    var changelogWindow = new ChangelogWindow(version, changelog);
                    changelogWindow.Activate();
                });
            }
        }

        private void Nav_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                Type pagina = null;
                switch (tag)
                {
                    case "Home": pagina = typeof(HomePage); break;
                    case "Library": pagina = typeof(LibraryPage); break;
                    case "Settings": pagina = typeof(SettingsPage); break;
                    case "About": pagina = typeof(AboutPage); break;
                }
                if (pagina != null)
                {
                    ContentFrame.Navigate(pagina);
                    ActualizarEstiloBotones(tag);
                }
            }
        }

        // --- GESTIÓN DE TEMAS Y CIERRE ---

        public void ActualizarColoresTema()
        {
            ActualizarEstiloBotones(_paginaActual);
            ActualizarColoresTitleBar();
        }

        private void ActualizarColoresTitleBar()
        {
            if (_appWindow != null)
            {
                bool esOscuro = SettingsService.Current.DarkMode;
                var titleBar = _appWindow.TitleBar;

                var foregroundColor = esOscuro ? Colors.White : Colors.Black;
                var hoverBackgroundColor = esOscuro ? Color.FromArgb(20, 255, 255, 255) : Color.FromArgb(20, 0, 0, 0);

                titleBar.ButtonForegroundColor = foregroundColor;
                titleBar.ButtonInactiveForegroundColor = Colors.Gray;

                titleBar.ButtonHoverBackgroundColor = hoverBackgroundColor;
                titleBar.ButtonHoverForegroundColor = foregroundColor;

                titleBar.ButtonPressedBackgroundColor = esOscuro ? Color.FromArgb(30, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0);
                titleBar.ButtonPressedForegroundColor = foregroundColor;
            }
        }

        private void ActualizarEstiloBotones(string tagActivo)
        {
            _paginaActual = tagActivo;
            bool esOscuro = SettingsService.Current.DarkMode;

            var colorActivo = esOscuro ? Colors.White : Colors.Black;
            var colorInactivo = esOscuro ? Colors.Gray : Colors.DarkGray;

            NavHome.Foreground = new SolidColorBrush(colorInactivo);
            NavLibrary.Foreground = new SolidColorBrush(colorInactivo);
            NavSettings.Foreground = new SolidColorBrush(colorInactivo);
            NavAbout.Foreground = new SolidColorBrush(colorInactivo);

            if (tagActivo == "Home") NavHome.Foreground = new SolidColorBrush(colorActivo);
            if (tagActivo == "Library") NavLibrary.Foreground = new SolidColorBrush(colorActivo);
            if (tagActivo == "Settings") NavSettings.Foreground = new SolidColorBrush(colorActivo);
            if (tagActivo == "About") NavAbout.Foreground = new SolidColorBrush(colorActivo);
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            if (_forceClosing)
            {
                App.TrayService?.Dispose();
                return;
            }

            // 🆕 GUARDAR POSICIÓN ANTES DE CERRAR
            GuardarPosicionVentana();

            // Desuscribirse del evento
            if (App.NetworkService != null)
            {
                App.NetworkService.ConnectivityChanged -= OnConnectivityChanged;
            }

            if (SettingsService.Current.MinimizeToTray)
            {
                args.Handled = true;
                App.TrayService?.MostrarTray();
            }
            else
            {
                App.TrayService?.Dispose();
            }
        }

        public void ForceClose()
        {
            _forceClosing = true;
            try { this.Close(); } catch { }
        }

        // En el método CerrarSesion(), reemplaza con:
        // 🔥 REEMPLAZA ESTE MÉTODO COMPLETO en MainWindow.xaml.cs

        public async void CerrarSesion()
        {
            try
            {
                // 1️ Revocar acceso en Google y eliminar token
                await GoogleAuth.RevocarAcceso();

                // 2️ Eliminar archivo de juegos local (games.json)
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string folder = Path.Combine(appData, "SaviumDrive");
                string gamesJsonPath = Path.Combine(folder, "games.json");

                if (File.Exists(gamesJsonPath))
                {
                    try
                    {
                        File.Delete(gamesJsonPath);
                        System.Diagnostics.Debug.WriteLine("✅ games.json eliminado correctamente");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ Error eliminando games.json: {ex.Message}");
                    }
                }

                // 4️ Limpiar UI
                TxtUserName.Text = "Cargando...";
                TxtUserEmail.Text = "...";
                UserAvatar.ProfilePicture = null;

                // 5️ Limpiar servicio global
                if (App.LibraryService != null)
                {
                    App.LibraryService.ProgressChanged -= OnGlobalUploadProgress;
                    App.LibraryService = null;
                }

                // 6️ Actualizar UI
                MainGrid.Visibility = Visibility.Collapsed;
                SplashGrid.Visibility = Visibility.Collapsed;
                LoginGrid.Visibility = Visibility.Visible;

                System.Diagnostics.Debug.WriteLine("✅ Sesión cerrada completamente");
            }
            catch (Exception ex)
            {
                await MostrarError("Error", $"No se pudo cerrar sesión completamente: {ex.Message}");
            }
        }

        private async Task MostrarError(string titulo, string detalle)
        {
            ContentDialog dialog = new ContentDialog { Title = titulo, Content = detalle, CloseButtonText = "Entendido", XamlRoot = this.Content.XamlRoot };
            dialog.RequestedTheme = SettingsService.Current.DarkMode ? ElementTheme.Dark : ElementTheme.Light;
            dialog.CloseButtonStyle = (Style)Application.Current.Resources["StandardBtnStyle"];
            await dialog.ShowAsync();
        }

        public void RecargarCompleta()
        {
            ContentFrame.Navigate(typeof(SettingsPage), null, new Microsoft.UI.Xaml.Media.Animation.SuppressNavigationTransitionInfo());
        }
    }
}