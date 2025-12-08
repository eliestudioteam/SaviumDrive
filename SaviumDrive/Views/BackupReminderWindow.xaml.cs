using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using SaviumDrive.Services;
using System;

namespace SaviumDrive.Views
{
    public sealed partial class BackupReminderWindow : Window
    {
        private AppWindow _appWindow;
        public bool Result { get; private set; } = false;

        public BackupReminderWindow()
        {
            this.InitializeComponent();

            if (this.Content is FrameworkElement root)
            {
                root.RequestedTheme = SettingsService.Current.DarkMode ? ElementTheme.Dark : ElementTheme.Light;
            }

            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            Microsoft.UI.WindowId wndId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            _appWindow = AppWindow.GetFromWindowId(wndId);

            // 1. Definir tamaño fijo
            var windowSize = new Windows.Graphics.SizeInt32(400, 220);
            _appWindow.Resize(windowSize);

            // 2. 🔥 CENTRAR EN PANTALLA
            CenterWindow(windowSize);

            var presenter = OverlappedPresenter.Create();
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
            presenter.IsAlwaysOnTop = true;
            _appWindow.SetPresenter(presenter);

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            this.Title = "Savium Drive";
        }

        // 🔥 MÉTODO PARA CENTRAR
        private void CenterWindow(Windows.Graphics.SizeInt32 size)
        {
            try
            {
                // Obtener la pantalla donde debería aparecer la ventana (Primary por defecto)
                DisplayArea displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);

                // Calcular el centro relativo al área de trabajo (excluyendo barra de tareas)
                int x = ((displayArea.WorkArea.Width - size.Width) / 2) + displayArea.WorkArea.X;
                int y = ((displayArea.WorkArea.Height - size.Height) / 2) + displayArea.WorkArea.Y;

                _appWindow.Move(new Windows.Graphics.PointInt32(x, y));
            }
            catch { /* Ignorar errores de cálculo en entornos extraños */ }
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