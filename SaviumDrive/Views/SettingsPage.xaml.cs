using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using SaviumDrive.Helpers;
using SaviumDrive.Services;
using System;
using Windows.Globalization;

namespace SaviumDrive.Views
{
    /// <summary>Configuración: inicio con Windows, bandeja, tema, idioma, detector de juegos y cerrar sesión.</summary>
    public sealed partial class SettingsPage : Page
    {
        private bool _isLoaded = false;

        public SettingsPage()
        {
            this.InitializeComponent();
            this.Loaded += SettingsPage_Loaded;
        }

        private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            CargarConfiguracion();
            CargarDatosUsuario();
        }

        private void CargarConfiguracion()
        {
            var config = SettingsService.Current;
            if (TglWindowsStart != null) TglWindowsStart.IsOn = config.StartWithWindows;
            if (TglTray != null) TglTray.IsOn = config.MinimizeToTray;
            if (TglVersionNotice != null) TglVersionNotice.IsOn = config.ShowVersionSwitchNotice;

            if (TglMinimizeOnStartup != null) TglMinimizeOnStartup.IsOn = config.MinimizeOnStartup;

            if (TglDarkMode != null) TglDarkMode.IsOn = config.DarkMode;
            if (TglAutoDetect != null) TglAutoDetect.IsOn = config.AutoDetectGames;

            if (CmbLanguage != null)
                CmbLanguage.SelectedIndex = config.Language.StartsWith("en") ? 1 : 0;

            _isLoaded = true;
        }

        /// <summary>Nombre, correo y foto de la cuenta (los tiene la ventana principal).</summary>
        private void CargarDatosUsuario()
        {
            if (App.MainWindow is MainWindow main)
            {
                // Acceder a los controles públicos de MainWindow
                TxtUserNameSettings.Text = main.GetUserName();
                TxtUserEmailSettings.Text = main.GetUserEmail();

                var photoUri = main.GetUserPhotoUri();
                if (!string.IsNullOrEmpty(photoUri))
                {
                    UserAvatarSettings.ProfilePicture = new BitmapImage(new Uri(photoUri));
                }
            }
        }

        /// <summary>Guarda cualquier interruptor al cambiarlo y aplica su efecto (tema, inicio con Windows, detector).</summary>
        private async void SettingChanged(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;

            var config = SettingsService.Current;
            if (TglWindowsStart != null) config.StartWithWindows = TglWindowsStart.IsOn;
            if (TglTray != null) config.MinimizeToTray = TglTray.IsOn;
            if (TglVersionNotice != null) config.ShowVersionSwitchNotice = TglVersionNotice.IsOn;

            if (TglMinimizeOnStartup != null) config.MinimizeOnStartup = TglMinimizeOnStartup.IsOn;

            if (TglAutoDetect != null)
            {
                config.AutoDetectGames = TglAutoDetect.IsOn;

                if (config.AutoDetectGames)
                {
                    App.GameDetector?.Start();
                }
                else
                {
                    App.GameDetector?.Stop();
                }
            }

            if (TglDarkMode != null)
            {
                bool modoOscuroCambio = config.DarkMode != TglDarkMode.IsOn;
                config.DarkMode = TglDarkMode.IsOn;

                if (modoOscuroCambio)
                {
                    SettingsService.AplicarTema();
                    if (App.MainWindow is MainWindow main)
                    {
                        main.ActualizarColoresTema();
                    }
                }
            }

            SettingsService.Save();
            if (sender == TglWindowsStart) await SettingsService.ConfigurarArranqueWindows(config.StartWithWindows);
        }

        /// <summary>Cambia el idioma. Los textos XAML se cargan al arrancar, así que se ofrece reiniciar la app.</summary>
        private async void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            if (CmbLanguage.SelectedItem is ComboBoxItem item)
            {
                string newLang = item.Tag.ToString();
                if (SettingsService.Current.Language != newLang)
                {
                    SettingsService.Current.Language = newLang;
                    SettingsService.Save();

                    ContentDialog restartDialog = new ContentDialog
                    {
                        Title = ResourceHelper.GetString("RestartTitle"),
                        Content = ResourceHelper.GetString("RestartMsg"),
                        PrimaryButtonText = ResourceHelper.GetString("RestartNow"),
                        CloseButtonText = ResourceHelper.GetString("RestartLater"),
                        DefaultButton = ContentDialogButton.Primary,
                        XamlRoot = this.XamlRoot,
                        RequestedTheme = SettingsService.Current.DarkMode ? ElementTheme.Dark : ElementTheme.Light,
                        PrimaryButtonStyle = (Style)Application.Current.Resources["StandardBtnStyle"],
                        CloseButtonStyle = (Style)Application.Current.Resources["StandardBtnStyle"]
                    };

                    var result = await restartDialog.ShowAsync();
                    if (result == ContentDialogResult.Primary) App.ReiniciarAplicacion();
                }
            }
        }

        private async void BtnLogout_Click(object sender, RoutedEventArgs e)
        {
            ContentDialog dialog = new ContentDialog
            {
                Title = ResourceHelper.GetString("DlgLogoutTitle"),
                Content = ResourceHelper.GetString("DlgLogoutMsg"),
                PrimaryButtonText = ResourceHelper.GetString("DlgYes"),
                CloseButtonText = ResourceHelper.GetString("DlgNo"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot,
                RequestedTheme = SettingsService.Current.DarkMode ? ElementTheme.Dark : ElementTheme.Light,
                PrimaryButtonStyle = (Style)Application.Current.Resources["StandardBtnStyle"],
                CloseButtonStyle = (Style)Application.Current.Resources["StandardBtnStyle"]
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                if (App.MainWindow is MainWindow main) main.CerrarSesion();
            }
        }

        private void BtnAbout_Click(object sender, RoutedEventArgs e)
        {
            this.Frame.Navigate(typeof(AboutPage));
        }
    }
}