// Views/AboutPage.xaml.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SaviumDrive.Services;
using System;
using System.Threading.Tasks;
using SaviumDrive.Helpers;

namespace SaviumDrive.Views
{
    /// <summary>Página "Acerca de": versión instalada y búsqueda e instalación de actualizaciones.</summary>
    public sealed partial class AboutPage : Page
    {
        private UpdateService _updateService;
        private UpdateInfo _currentUpdate;
        private Brush _originalTextBrush; // Guardar el color original

        public AboutPage()
        {
            this.InitializeComponent();
            _updateService = new UpdateService();

            // Guardar el color original del tema
            _originalTextBrush = (Brush)Application.Current.Resources["ActionText"];

            // Mostrar versión actual
            TxtVersion.Text = $"v{GetCurrentVersion()}";

            // Texto inicial del botón con el color del tema
            TxtUpdate.Text = ResourceHelper.GetString("AbtUpdate");

            // Aplicar colores iniciales
            ApplyThemeColors();
        }

        private void ApplyThemeColors()
        {
            var updateTextBrush = (Brush)Application.Current.Resources["UpdateBtnText"];

            // El texto y el icono del botón no llevan color propio: lo heredan del botón. Si se les asignara
            // uno, dejarían de cambiar de color al pasar el ratón por encima.

            TxtUpdate.ClearValue(TextBlock.ForegroundProperty);
            IconUpdate.ClearValue(FontIcon.ForegroundProperty);

            BtnUpdate.Background = (Brush)Application.Current.Resources["ActionBackground"];
            BtnUpdate.Foreground = updateTextBrush;
        }

        private string GetCurrentVersion()
        {
            return "1.0.0 Beta";
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            if (this.Frame.CanGoBack)
                this.Frame.GoBack();
            else
                this.Frame.Navigate(typeof(SettingsPage));
        }

        private async void BtnUpdate_Click(object sender, RoutedEventArgs e)
        {
            BtnUpdate.IsEnabled = false;

            // Cambiar a estado "buscando" - Usar colores visibles
            TxtUpdate.Text = ResourceHelper.GetString("AbtSearching");

            // En modo claro: texto negro, modo oscuro: texto blanco
            var searchingTextColor = new SolidColorBrush(
                SettingsService.Current.DarkMode ?
                Windows.UI.Color.FromArgb(255, 255, 255, 255) : // Blanco en modo oscuro
                Windows.UI.Color.FromArgb(255, 0, 0, 0)         // Negro en modo claro
            );

            TxtUpdate.Foreground = searchingTextColor;
            IconUpdate.Foreground = searchingTextColor;

            IconUpdate.Glyph = "\uE72C"; // Icono de búsqueda

            try
            {
                _currentUpdate = await _updateService.CheckForUpdatesAsync();

                if (_currentUpdate == null)
                {
                    // No hay actualizaciones - Usar verde personalizado
                    TxtUpdate.Text = ResourceHelper.GetString("AbtUpToDate");
                    IconUpdate.Glyph = "\uE73E"; // Check

                    // Color blanco para el texto sobre fondo verde
                    var whiteBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
                    TxtUpdate.Foreground = whiteBrush;
                    IconUpdate.Foreground = whiteBrush;

                    // Fondo verde personalizado
                    BtnUpdate.Background = (Brush)Application.Current.Resources["CustomUpdateBrush"];

                    await Task.Delay(3000);

                    // Restaurar estado original
                    RestoreCheckButton();
                }
                else
                {
                    // Hay actualización disponible
                    ShowUpdateAvailable();
                }
            }
            catch (Exception ex)
            {
                await ShowErrorDialog(
                    ResourceHelper.GetString("AlertError"),
                    ResourceHelper.GetString("UpdateCheckError")
                );
                RestoreCheckButton();
            }

            BtnUpdate.IsEnabled = true;
        }

        private void ShowUpdateAvailable()
        {
            BtnUpdate.Visibility = Visibility.Collapsed;
            UpdateInfoCard.Visibility = Visibility.Visible;

            TxtNewVersion.Text = string.Format(
                ResourceHelper.GetString("UpdateNewVersion"),
                _currentUpdate.Version
            );

            TxtFileSize.Text = string.Format(
                ResourceHelper.GetString("UpdateFileSize"),
                UpdateService.FormatFileSize(_currentUpdate.Size)
            );
        }

        private async void BtnDownload_Click(object sender, RoutedEventArgs e)
        {
            if (_currentUpdate == null) return;

            BtnDownload.IsEnabled = false;
            DownloadProgress.Visibility = Visibility.Visible;

            try
            {
                var progress = new Progress<int>(percent =>
                {
                    this.DispatcherQueue.TryEnqueue(() =>
                    {
                        ProgressBar.Value = percent;
                        TxtProgress.Text = $"{percent}%";
                    });
                });

                string installerPath = await _updateService.DownloadUpdateAsync(_currentUpdate.DownloadUrl, progress);

                await ShowSuccessDialog(
                    ResourceHelper.GetString("UpdateDownloadComplete"),
                    ResourceHelper.GetString("UpdateInstallPrompt")
                );

                _updateService.SaveUpdateInfo(_currentUpdate);
                _updateService.InstallUpdate(installerPath);
                Application.Current.Exit();
            }
            catch (Exception ex)
            {
                await ShowErrorDialog(
                    ResourceHelper.GetString("AlertError"),
                    ResourceHelper.GetString("UpdateDownloadError")
                );

                DownloadProgress.Visibility = Visibility.Collapsed;
                BtnDownload.IsEnabled = true;
            }
        }

        private void RestoreCheckButton()
        {
            // Restaurar texto
            TxtUpdate.Text = ResourceHelper.GetString("AbtUpdate");
            IconUpdate.Glyph = "\uE896"; // Icono original

            // Restaurar colores del tema
            ApplyThemeColors();

            BtnUpdate.Visibility = Visibility.Visible;
            UpdateInfoCard.Visibility = Visibility.Collapsed;
        }

        private async Task ShowErrorDialog(string title, string message)
        {
            ContentDialog dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = ResourceHelper.GetString("DlgCloseBtn"),
                XamlRoot = this.Content.XamlRoot
            };

            dialog.RequestedTheme = SettingsService.Current.DarkMode ? ElementTheme.Dark : ElementTheme.Light;

            if (Application.Current.Resources.TryGetValue("StandardBtnStyle", out object style))
            {
                dialog.CloseButtonStyle = (Style)style;
            }

            await dialog.ShowAsync();
        }

        private async Task<bool> ShowSuccessDialog(string title, string message)
        {
            ContentDialog dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                PrimaryButtonText = ResourceHelper.GetString("UpdateInstallNow"),
                CloseButtonText = ResourceHelper.GetString("ConfirmCancelBtn"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content.XamlRoot
            };

            dialog.RequestedTheme = SettingsService.Current.DarkMode ? ElementTheme.Dark : ElementTheme.Light;

            if (Application.Current.Resources.TryGetValue("StandardBtnStyle", out object style))
            {
                dialog.PrimaryButtonStyle = (Style)style;
                dialog.CloseButtonStyle = (Style)style;
            }

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }
    }
}