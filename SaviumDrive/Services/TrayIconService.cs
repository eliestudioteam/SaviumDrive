using H.NotifyIcon;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.Drawing;
using System.Windows.Input;

namespace SaviumDrive.Services
{
    public class TrayIconService : IDisposable
    {
        private TaskbarIcon _trayIcon;
        private Window _mainWindow;
        private bool _disposed = false;

        // Constructor: no inicializar el tray hasta que se minimice/cierre
        public TrayIconService(Window mainWindow)
        {
            _mainWindow = mainWindow;
            // No InicializarTray() aquí: crear el icono solo al minimizar/cerrar
        }

        private void InicializarTray()
        {
            if (_disposed)
            {
                System.Diagnostics.Debug.WriteLine("⚠️ TrayIconService: Intento de inicializar después de Dispose");
                return;
            }

            // Determinar la posible ruta al icono pero abrir el archivo en el hilo UI
            string iconPath = null;
            try
            {
                iconPath = Path.Combine(AppContext.BaseDirectory ?? string.Empty, "Assets", "tray-icon1024x1024.ico");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Error buscando icono: {ex.Message}");
                iconPath = null;
            }

            try
            {
                // Crear controles UI directamente (se asume que el caller está en la cola del UI)
                _trayIcon = new TaskbarIcon { ToolTipText = "Savium Drive" };

                // Asignar icono si existe la ruta
                try
                {
                    if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                    {
                        using var fs = File.OpenRead(iconPath);
                        _trayIcon.Icon = new Icon(fs);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ Icono no encontrado en: {iconPath}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Error cargando icono: {ex.Message}");
                }

                // Crear menú contextual
                var menuFlyout = new MenuFlyout();

                var openItem = new MenuFlyoutItem { Text = "Abrir Savium Drive" };
                openItem.Command = new RelayCommand(MostrarVentana);
                openItem.Click += (s, e) => MostrarVentana();

                var exitItem = new MenuFlyoutItem { Text = "Salir" };
                exitItem.Command = new RelayCommand(CerrarApp);
                exitItem.Click += (s, e) => CerrarApp();

                menuFlyout.Items.Add(openItem);
                menuFlyout.Items.Add(new MenuFlyoutSeparator());
                menuFlyout.Items.Add(exitItem);

                // Configurar XamlRoot para el menú
                try
                {
                    if (_mainWindow?.Content != null)
                        menuFlyout.XamlRoot = _mainWindow.Content.XamlRoot;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Error configurando XamlRoot: {ex.Message}");
                }

                // Asociar el menú con el icono de bandeja
                try { TaskbarIcon.SetParentTaskbarIcon(menuFlyout, _trayIcon); }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Error asociando flyout: {ex.Message}");
                }

                try { TaskbarIcon.SetParentTaskbarIcon(openItem, _trayIcon); }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Error asociando openItem: {ex.Message}");
                }

                try { TaskbarIcon.SetParentTaskbarIcon(exitItem, _trayIcon); }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Error asociando exitItem: {ex.Message}");
                }

                _trayIcon.ContextFlyout = menuFlyout;

                // Forzar creación del icono
                try
                {
                    _trayIcon.ForceCreate();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Error creando icono: {ex.Message}");
                }

                // Click izquierdo para mostrar ventana
                _trayIcon.LeftClickCommand = new RelayCommand(MostrarVentana);

                System.Diagnostics.Debug.WriteLine("✅ TrayIcon inicializado correctamente");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error fatal inicializando TrayIcon: {ex.Message}");
            }
        }

        public void MostrarTray()
        {
            if (_disposed)
            {
                System.Diagnostics.Debug.WriteLine("⚠️ TrayIconService: Intento de mostrar tray después de Dispose");
                return;
            }

            if (_mainWindow == null)
            {
                System.Diagnostics.Debug.WriteLine("⚠️ TrayIconService: MainWindow es null");
                return;
            }

            // Ejecutar todo en la cola del UI para asegurar que el tray y el flyout se creen antes de ocultar
            _mainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    if (_trayIcon == null)
                    {
                        InicializarTray();
                    }

                    try
                    {
                        _trayIcon?.ForceCreate();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ Error forzando creación: {ex.Message}");
                    }

                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_mainWindow);
                    PInvoke.User32.ShowWindow(hwnd, PInvoke.User32.WindowShowStyle.SW_HIDE);

                    System.Diagnostics.Debug.WriteLine("✅ Ventana ocultada a bandeja");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Error en MostrarTray: {ex.Message}");
                }
            });
        }

        public void MostrarVentana()
        {
            if (_disposed)
            {
                System.Diagnostics.Debug.WriteLine("⚠️ TrayIconService: Intento de mostrar ventana después de Dispose");
                return;
            }

            if (_mainWindow == null)
            {
                System.Diagnostics.Debug.WriteLine("⚠️ TrayIconService: MainWindow es null");
                return;
            }

            _mainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_mainWindow);
                    PInvoke.User32.ShowWindow(hwnd, PInvoke.User32.WindowShowStyle.SW_SHOW);
                    PInvoke.User32.SetForegroundWindow(hwnd);

                    try
                    {
                        _mainWindow.Activate();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ Error activando ventana: {ex.Message}");
                    }

                    System.Diagnostics.Debug.WriteLine("✅ Ventana restaurada");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Error en MostrarVentana: {ex.Message}");
                }
            });
        }

        private void CerrarApp()
        {
            if (_disposed)
            {
                System.Diagnostics.Debug.WriteLine("⚠️ TrayIconService: Intento de cerrar app después de Dispose");
                return;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine("🔄 Cerrando aplicación...");

                // Limpiar icono de bandeja primero
                try
                {
                    _trayIcon?.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Error disposing tray icon: {ex.Message}");
                }

                _trayIcon = null;

                // Intentar cerrar la aplicación
                if (_mainWindow?.DispatcherQueue != null)
                {
                    _mainWindow.DispatcherQueue.TryEnqueue(() =>
                    {
                        try
                        {
                            Application.Current.Exit();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"⚠️ Error llamando Exit(): {ex.Message}");
                        }
                    });
                }
                else
                {
                    try
                    {
                        Application.Current.Exit();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ Error llamando Exit() directo: {ex.Message}");
                    }
                }

                // Fallback: forzar terminación del proceso si Exit() no funciona
                System.Threading.Tasks.Task.Delay(2000).ContinueWith(_ =>
                {
                    try
                    {
                        Environment.Exit(0);
                    }
                    catch { }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error fatal en CerrarApp: {ex.Message}");
                try
                {
                    Environment.Exit(0);
                }
                catch { }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    try
                    {
                        if (_trayIcon != null)
                        {
                            System.Diagnostics.Debug.WriteLine("🧹 Limpiando TrayIcon...");
                            _trayIcon.Dispose();
                            _trayIcon = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Error en Dispose de TrayIcon: {ex.Message}");
                    }
                }

                _mainWindow = null;
                _disposed = true;
                System.Diagnostics.Debug.WriteLine("✅ TrayIconService disposed correctamente");
            }
        }

        ~TrayIconService()
        {
            Dispose(false);
        }
    }

    // ============================================================================
    // COMANDO AUXILIAR SIMPLE
    // ============================================================================
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        public event EventHandler CanExecuteChanged;

        public RelayCommand(Action execute)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        }

        public bool CanExecute(object parameter) => true;

        public void Execute(object parameter)
        {
            try
            {
                _execute();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error ejecutando comando: {ex.Message}");
            }
        }
    }

    // ============================================================================
    // P/INVOKE PARA MOSTRAR/OCULTAR VENTANAS
    // ============================================================================
    internal static class PInvoke
    {
        internal static class User32
        {
            [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
            internal static extern bool ShowWindow(IntPtr hWnd, WindowShowStyle nCmdShow);

            [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
            internal static extern bool SetForegroundWindow(IntPtr hWnd);

            internal enum WindowShowStyle : int
            {
                SW_HIDE = 0,
                SW_SHOW = 5,
            }
        }
    }
}