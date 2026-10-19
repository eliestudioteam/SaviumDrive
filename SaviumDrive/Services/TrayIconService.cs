using H.NotifyIcon;
using H.NotifyIcon.EfficiencyMode;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.Drawing;
using System.Windows.Input;

namespace SaviumDrive.Services
{
    /// <summary>
    /// Icono de la bandeja. No depende de la ventana principal: así la ventana se puede
    /// destruir al minimizar (liberando su memoria) y volver a crear al abrirla.
    /// </summary>
    public class TrayIconService : IDisposable
    {
        private readonly DispatcherQueue _dispatcher;
        private TaskbarIcon _trayIcon;
        private bool _disposed = false;

        public TrayIconService(DispatcherQueue dispatcher)
        {
            _dispatcher = dispatcher;
        }

        /// <summary>Crea el icono de la bandeja si todavía no existe.</summary>
        public void MostrarIcono()
        {
            if (_disposed) return;
            _dispatcher.TryEnqueue(() =>
            {
                if (_trayIcon == null && !_disposed) InicializarTray();
            });
        }

        /// <summary>Oculta la ventana sin destruirla (solo mientras termina una subida o descarga).</summary>
        public void OcultarVentana(Window ventana)
        {
            MostrarIcono();
            try
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(ventana);
                PInvoke.User32.ShowWindow(hwnd, PInvoke.User32.WindowShowStyle.SW_HIDE);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error ocultando ventana: {ex.Message}");
            }
        }

        /// <summary>Modo eficiencia de Windows (menos prioridad de CPU) mientras la app está en la bandeja.</summary>
        public void SetModoEficiencia(bool activar)
        {
            try { EfficiencyModeUtilities.SetEfficiencyMode(activar); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"⚠️ Modo eficiencia no disponible: {ex.Message}"); }
        }

        private void InicializarTray()
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory ?? string.Empty, "Assets", "tray-icon1024x1024.ico");

            try
            {
                _trayIcon = new TaskbarIcon
                {
                    ToolTipText = "Savium Drive",
                    // Menú nativo de Windows: funciona aunque no haya ninguna ventana XAML abierta
                    ContextMenuMode = ContextMenuMode.PopupMenu,
                    NoLeftClickDelay = true
                };

                try
                {
                    if (File.Exists(iconPath))
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

                // En modo PopupMenu solo se usan los Command (no los eventos Click)
                var menuFlyout = new MenuFlyout();
                menuFlyout.Items.Add(new MenuFlyoutItem { Text = "Abrir Savium Drive", Command = new RelayCommand(() => App.MostrarVentanaPrincipal()) });
                menuFlyout.Items.Add(new MenuFlyoutSeparator());
                menuFlyout.Items.Add(new MenuFlyoutItem { Text = "Salir", Command = new RelayCommand(CerrarApp) });
                _trayIcon.ContextFlyout = menuFlyout;

                // Click izquierdo para mostrar ventana
                _trayIcon.LeftClickCommand = new RelayCommand(() => App.MostrarVentanaPrincipal());

                // El modo eficiencia lo controla App (activo solo en la bandeja)
                _trayIcon.ForceCreate(enablesEfficiencyMode: false);

                System.Diagnostics.Debug.WriteLine("✅ TrayIcon inicializado correctamente");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error fatal inicializando TrayIcon: {ex.Message}");
            }
        }

        private void CerrarApp()
        {
            if (_disposed) return;

            System.Diagnostics.Debug.WriteLine("🔄 Cerrando aplicación...");
            App.SalirAplicacion();

            // Fallback: forzar terminación del proceso si Exit() no funciona
            System.Threading.Tasks.Task.Delay(2000).ContinueWith(_ =>
            {
                try { Environment.Exit(0); } catch { }
            });
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
                        _trayIcon?.Dispose();
                        _trayIcon = null;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Error en Dispose de TrayIcon: {ex.Message}");
                    }
                }

                _disposed = true;
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
