using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using SaviumDrive.Helpers;
using SaviumDrive.Services;
using System;

namespace SaviumDrive.Views
{
    /// <summary>
    /// Aviso que aparece al cerrar un juego. Ventana pequeña, siempre encima y de tamaño fijo.
    /// <see cref="Result"/> indica si el usuario aceptó (guardar o agregar el juego).
    /// </summary>
    public sealed partial class BackupReminderWindow : Window
    {
        private AppWindow _appWindow;
        public bool Result { get; private set; } = false;

        /// <summary>
        /// Recordatorio al cerrar un juego. Si <paramref name="nombreEnBiblioteca"/> no es null, el juego ya
        /// tiene backup y se ofrece guardarlo ahora; si no, configurarlo.
        /// </summary>
        public BackupReminderWindow(string nombreJuego, string nombreEnBiblioteca) : this()
        {
            if (string.IsNullOrWhiteSpace(nombreJuego)) return;

            TxtMessage.Text = ResourceHelper.GetString("DetectorMsgGame", nombreJuego);
            if (nombreEnBiblioteca != null)
            {
                TxtSubMessage.Text = ResourceHelper.GetString("DetectorSubMsgSave", nombreEnBiblioteca);
                BtnYes.Content = ResourceHelper.GetString("BtnSaveNow");
            }
        }

        public BackupReminderWindow()
        {
            this.InitializeComponent();

            if (this.Content is FrameworkElement root)
            {
                root.RequestedTheme = SettingsService.Current.DarkMode ? ElementTheme.Dark : ElementTheme.Light;
            }

            // Primero la barra de título: extenderla después de configurar la ventana
            // puede devolverle el borde redimensionable
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            this.Title = "Savium Drive";

            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            Microsoft.UI.WindowId wndId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            _appWindow = AppWindow.GetFromWindowId(wndId);

            // Tamaño fijo: no se puede agrandar, achicar, maximizar ni minimizar
            var presenter = _appWindow.Presenter as OverlappedPresenter ?? OverlappedPresenter.Create();
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
            if (_appWindow.Presenter != presenter) _appWindow.SetPresenter(presenter);

            // En píxeles lógicos: se ve igual con cualquier escala de Windows, centrada en su pantalla
            _tamanoFijo = WindowSizing.AjustarYCentrar(_appWindow, hWnd, 420, 230);

            // Aunque no tenga borde, Windows aún puede maximizarla o encajarla (doble clic en la barra,
            // Windows + flechas): se devuelve siempre a su tamaño
            _appWindow.Changed += MantenerTamano;
            Closed += (_, __) => _appWindow.Changed -= MantenerTamano;
        }

        private Windows.Graphics.SizeInt32 _tamanoFijo;

        private void MantenerTamano(AppWindow ventana, AppWindowChangedEventArgs cambio)
        {
            if (!cambio.DidSizeChange && !cambio.DidPresenterChange) return;
            if (ventana.Presenter is OverlappedPresenter p && p.State != OverlappedPresenterState.Restored) p.Restore();
            if (ventana.Size.Width != _tamanoFijo.Width || ventana.Size.Height != _tamanoFijo.Height) ventana.Resize(_tamanoFijo);
        }

        private void BtnNo_Click(object sender, RoutedEventArgs e)
        {
            Result = false;
            this.Close();
        }

        private void BtnYes_Click(object sender, RoutedEventArgs e)
        {
            Result = true;
            this.Close();
        }
    }
}