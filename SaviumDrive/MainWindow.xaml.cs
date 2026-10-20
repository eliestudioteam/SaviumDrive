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
    /// <summary>
    /// Ventana principal: pantalla de carga, inicio de sesión con Google y, una vez dentro, el menú lateral
    /// con las páginas (Inicio, Mis respaldos, Configuración, Acerca de) y el indicador flotante de progreso.
    /// Se puede destruir y volver a crear mientras la app sigue en la bandeja (ver App).
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        // Tamaño mínimo de la ventana (en píxeles lógicos)
        private const int MinWidth = 1100;
        private const int MinHeight = 600;

        // Tamaño inicial si no hay una posición guardada
        private const int InitialWidth = 1280;
        private const int InitialHeight = 720;

        private CancellationTokenSource _loginCts;

        // Funciones de Windows para restaurar la ventana y traerla al frente
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_RESTORE = 9;

        // Funciones de Windows para limitar el tamaño mínimo (subclase del procedimiento de ventana)
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

        private DispatcherTimer _splashTimer;
        private object _originalButtonContent;
        private bool _forceClosing = false;
        private bool _liberandoABandeja = false;
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

            // Posición y tamaño guardados, o centrada si no hay
            CentrarVentana();

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

            // El recordatorio del detector de juegos lo gestiona App (funciona aunque no haya ventana)

            ActualizarEstiloBotones("Home");
            TxtLoginBtn.Text = ResourceHelper.GetString("Login_Button_Start");
            IniciarCargaInicial();
        }

        // --- TAMAÑO MÍNIMO DE LA VENTANA ---

        /// <summary>
        /// Procedimiento de ventana propio: responde a WM_GETMINMAXINFO con el tamaño mínimo de la ventana
        /// (WinUI 3 no tiene una propiedad para eso). El resto de mensajes van al procedimiento original.
        /// </summary>
        private IntPtr NewWindowProc(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam)
        {
            if (Msg == WM_GETMINMAXINFO)
            {
                var minMaxInfo = Marshal.PtrToStructure<MINMAXINFO>(lParam);

                // El mínimo se escala con el DPI de la pantalla (monitores 4K o con zoom de Windows)
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

                int windowWidth = InitialWidth;
                int windowHeight = InitialHeight;

                // En pantallas más pequeñas que el tamaño inicial, se reduce sin bajar del mínimo
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

                System.Diagnostics.Debug.WriteLine($"Ventana centrada: {windowWidth}x{windowHeight} en ({x}, {y})");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error centrando ventana: {ex.Message}");
            }
        }
        private void GuardarPosicionVentana()
        {
            try
            {
                if (_appWindow == null) return;

                var position = _appWindow.Position;
                var size = _appWindow.Size;

                SettingsService.Current.WindowX = position.X;
                SettingsService.Current.WindowY = position.Y;
                SettingsService.Current.WindowWidth = size.Width;
                SettingsService.Current.WindowHeight = size.Height;

                SettingsService.Save();

                System.Diagnostics.Debug.WriteLine($"Posición guardada: {position.X}, {position.Y} | Tamaño: {size.Width}x{size.Height}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error guardando posición: {ex.Message}");
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

                    System.Diagnostics.Debug.WriteLine("Posición restaurada desde configuración");
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



        // Datos de la cuenta para la página de configuración
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

        // --- VENTANA Y BANDEJA ---

        /// <summary>Página de biblioteca a abrir en cuanto termine el inicio de sesión (ej. juego detectado).</summary>
        public object NavegacionPendiente { get; set; }

        public bool EstaVisible
        {
            get
            {
                try { return _appWindow != null && _appWindow.IsVisible; }
                catch { return false; }
            }
        }

        /// <summary>Abre la biblioteca con un parámetro (ahora, o al terminar el inicio de sesión).</summary>
        public void AbrirBiblioteca(object parametro)
        {
            if (MainGrid.Visibility == Visibility.Visible)
                NavegarExterna(typeof(LibraryPage), "Library", parametro);
            else
                NavegacionPendiente = parametro;
        }

        /// <summary>Destruye la ventana para liberar su memoria (la app sigue en la bandeja).</summary>
        public void CerrarParaLiberar()
        {
            _liberandoABandeja = true;
            try { this.Close(); } catch { }
        }

        /// <summary>Muestra la ventana si estaba oculta o minimizada y la trae al frente.</summary>
        public void RestaurarYMostrarApp()
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
            // Al reabrir desde la bandeja la sesión ya está cargada: casi sin espera
            _splashTimer.Interval = App.LibraryService != null ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromSeconds(1);
            _splashTimer.Tick += async (s, e) => { _splashTimer.Stop(); await VerificarSesionYRedirigir(); };
            _splashTimer.Start();
        }

        private async Task VerificarSesionYRedirigir()
        {
            // Ventana recreada desde la bandeja: reutilizar la sesión de Drive ya abierta
            if (App.LibraryService != null)
            {
                try
                {
                    await CargarDatosUsuario(App.LibraryService.DriveService);
                    this.DispatcherQueue.TryEnqueue(() => { IrAlMain(); });
                    return;
                }
                catch { }
            }

            if (GoogleAuth.ExisteSesionGuardada())
            {
                try
                {
                    // Inicio de sesión automático con la sesión guardada: no hace falta poder cancelarlo
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
            // Si ya hay un inicio de sesión en marcha, el botón lo cancela
            if (_loginCts != null)
            {
                TxtLoginBtn.Text = ResourceHelper.GetString("Login_Button_Cancelling");
                btnLogin.IsEnabled = false;

                try { _loginCts.Cancel(); } catch { }

                // El botón se restaura aquí sin esperar a que termine la tarea cancelada
                var oldCts = _loginCts;
                _loginCts = null;

                // Un segundo para que se pueda leer "Cancelando..."
                await Task.Delay(1000);

                btnLogin.IsEnabled = true;

                // Clave de recurso sin ".Text": desde código no se pueden leer las claves de propiedades XAML
                TxtLoginBtn.Text = ResourceHelper.GetString("Login_Button_Start");

                try { oldCts.Dispose(); } catch { }

                return;
            }

            // Iniciar sesión
            // 1. Tras demasiados intentos fallidos hay que esperar unos minutos
            var (intentos, minutosRestantes) = GoogleAuth.ObtenerEstadoRateLimit();
            if (minutosRestantes > 0)
            {
                string msg = ResourceHelper.GetString("Login_Wait_Message", minutosRestantes);
                await MostrarError(ResourceHelper.GetString("Login_Error_Title"), msg);
                return;
            }

            // 2. El botón pasa a ser "Cancelar"
            TxtLoginBtn.Text = ResourceHelper.GetString("Login_Button_Cancel");

            // 3. Si el usuario no termina en el navegador en 60 segundos, se cancela
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
                    // Mensaje genérico más el detalle técnico, útil para reportar el error
                    // (por ejemplo, un cortafuegos que bloquea el puerto local)
                    string mensajeUsuario = ResourceHelper.GetString("Login_Error_Generic");
                    string mensajeTecnico = $"\n\nDetalle técnico:\n{ex.Message}";

                    await MostrarError(ResourceHelper.GetString("Login_Error_Title"), mensajeUsuario + mensajeTecnico);
                }
            }
            finally
            {
                // 4. Restaurar el botón, salvo que ya haya empezado otro intento
                this.DispatcherQueue.TryEnqueue(() =>
                {
                    if (_loginCts == currentCts)
                    {
                        _loginCts.Dispose();
                        _loginCts = null;

                        btnLogin.IsEnabled = true;

                        TxtLoginBtn.Text = ResourceHelper.GetString("Login_Button_Start");
                    }
                    else
                    {
                        try { currentCts.Dispose(); } catch { }
                    }
                });
            }
        }

        /// <summary>Diálogo de aviso con un solo botón.</summary>
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


        /// <summary>
        /// Tras iniciar sesión: muestra el nombre, el correo y la foto de la cuenta, y crea el servicio de
        /// backups que usa toda la app.
        /// </summary>
        private async Task CargarDatosUsuario(Google.Apis.Drive.v3.DriveService service)
        {
            var aboutRequest = service.About.Get();
            aboutRequest.Fields = "user(displayName, emailAddress, photoLink)";
            var about = await aboutRequest.ExecuteAsync();

            // Servicio de backups único para toda la app; se reutiliza si la ventana se vuelve a crear
            if (App.LibraryService == null)
            {
                App.LibraryService = new GameLibraryService(service);
            }

            // Indicador flotante de progreso (cada ventana nueva se vuelve a suscribir)
            App.LibraryService.ProgressChanged -= OnGlobalUploadProgress;
            App.LibraryService.ProgressChanged += OnGlobalUploadProgress;

            this.DispatcherQueue.TryEnqueue(() =>
            {
                TxtUserName.Text = about.User.DisplayName;
                TxtUserEmail.Text = about.User.EmailAddress;
                if (!string.IsNullOrEmpty(about.User.PhotoLink)) UserAvatar.ProfilePicture = new BitmapImage(new Uri(about.User.PhotoLink));
            });
        }

        // --- INDICADOR FLOTANTE DE PROGRESO ---
        private void OnGlobalUploadProgress(object sender, GlobalProgressArgs e)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                if (e.IsActive)
                {
                    GlobalUploadIndicator.Visibility = Visibility.Visible;

                    // -1: progreso que no se puede medir (anillo indeterminado); 0 a 100: porcentaje
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

        /// <summary>Navega a una página desde otra (por ejemplo, desde Inicio a la biblioteca) y marca su botón del menú.</summary>
        public void NavegarExterna(Type pagina, string tagMenu, object parametro = null)
        {
            ContentFrame.Navigate(pagina, parametro);

            ActualizarEstiloBotones(tagMenu);
        }

        /// <summary>Oculta el inicio de sesión y muestra la app (página de inicio o la pendiente).</summary>
        private void IrAlMain()
        {
            LoginGrid.Visibility = Visibility.Collapsed;
            MainGrid.Visibility = Visibility.Visible;
            try
            {
                if (NavegacionPendiente != null)
                {
                    // Abierta desde el recordatorio de backup: ir directo a la biblioteca
                    var parametro = NavegacionPendiente;
                    NavegacionPendiente = null;
                    NavegarExterna(typeof(LibraryPage), "Library", parametro);
                }
                else
                {
                    if (!ContentFrame.Navigate(typeof(HomePage))) ContentFrame.Content = new HomePage();
                    ActualizarEstiloBotones("Home");
                }

                // Novedades de la versión si es el primer arranque tras actualizar
                CheckFirstRunAfterUpdate();
            }
            catch { }
        }

        /// <summary>Muestra la ventana de novedades la primera vez que se abre una versión nueva.</summary>
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

        /// <summary>Vuelve a aplicar los colores del tema a la barra de título y a los botones del menú.</summary>
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
                LimpiarAntesDeDestruir();
                App.SalirAplicacion();
                return;
            }

            // Recordar la posición y el tamaño para la próxima vez
            GuardarPosicionVentana();

            // Cierre diferido tras terminar una subida/descarga con la ventana oculta
            if (_liberandoABandeja)
            {
                LimpiarAntesDeDestruir();
                return;
            }

            if (SettingsService.Current.MinimizeToTray)
            {
                if (App.PrepararCierreABandeja(this))
                {
                    // La ventana se destruye para liberar memoria; la app sigue en la bandeja
                    LimpiarAntesDeDestruir();
                }
                else
                {
                    // Hay una operación en curso: solo se oculta y se liberará al terminar
                    args.Handled = true;
                }
            }
            else
            {
                LimpiarAntesDeDestruir();
                App.SalirAplicacion();
            }
        }

        /// <summary>
        /// Suelta todo lo que mantiene viva a esta ventana desde los servicios (eventos),
        /// para que su memoria se pueda recoger al destruirla.
        /// </summary>
        private void LimpiarAntesDeDestruir()
        {
            _splashTimer?.Stop();

            if (App.NetworkService != null)
                App.NetworkService.ConnectivityChanged -= OnConnectivityChanged;

            if (App.LibraryService != null)
                App.LibraryService.ProgressChanged -= OnGlobalUploadProgress;

            // Salir de la página actual: sus OnNavigatedFrom/Unloaded quitan sus propias suscripciones
            try
            {
                ContentFrame.Navigate(typeof(Page));
                ContentFrame.BackStack.Clear();
                ContentFrame.Content = null;
            }
            catch { }
        }

        /// <summary>Cierra la ventana sin pasar por "minimizar a la bandeja".</summary>
        public void ForceClose()
        {
            _forceClosing = true;
            try { this.Close(); } catch { }
        }

        /// <summary>
        /// Cierra la sesión: cancela la operación en curso, revoca el acceso en Google, borra la biblioteca
        /// local (la copia de Drive se mantiene) y vuelve a la pantalla de inicio de sesión.
        /// </summary>
        public async void CerrarSesion()
        {
            try
            {
                // 1. Si hay una subida, actualización o restauración en curso, cancelarla y esperar a que
                // termine (borra en Drive lo subido a medias) antes de revocar el acceso
                var servicio = App.LibraryService;
                if (servicio != null && servicio.IsBusy)
                {
                    servicio.CancelarOperacionActual();
                    for (int i = 0; i < 50 && servicio.IsBusy; i++) await Task.Delay(200);
                }

                // 2. Revocar el acceso en Google y borrar el token
                await GoogleAuth.RevocarAcceso();

                // 3. Borrar la biblioteca local (games.json); la de Drive se mantiene
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string folder = Path.Combine(appData, "SaviumDrive");
                string gamesJsonPath = Path.Combine(folder, "games.json");

                if (File.Exists(gamesJsonPath))
                {
                    try
                    {
                        File.Delete(gamesJsonPath);
                        System.Diagnostics.Debug.WriteLine("games.json eliminado correctamente");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error eliminando games.json: {ex.Message}");
                    }
                }

                // 4. Quitar los datos de la cuenta de la interfaz
                TxtUserName.Text = "Cargando...";
                TxtUserEmail.Text = "...";
                UserAvatar.ProfilePicture = null;

                // 5. Soltar el servicio de backups de esta sesión
                if (App.LibraryService != null)
                {
                    App.LibraryService.ProgressChanged -= OnGlobalUploadProgress;
                    App.LibraryService = null;
                }

                // 6. Volver a la pantalla de inicio de sesión
                MainGrid.Visibility = Visibility.Collapsed;
                SplashGrid.Visibility = Visibility.Collapsed;
                LoginGrid.Visibility = Visibility.Visible;

                System.Diagnostics.Debug.WriteLine("Sesión cerrada completamente");
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

        /// <summary>Vuelve a cargar la página de configuración (tras cambiar el tema).</summary>
        public void RecargarCompleta()
        {
            ContentFrame.Navigate(typeof(SettingsPage), null, new Microsoft.UI.Xaml.Media.Animation.SuppressNavigationTransitionInfo());
        }
    }
}