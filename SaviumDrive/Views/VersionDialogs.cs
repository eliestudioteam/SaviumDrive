using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SaviumDrive.Helpers;
using SaviumDrive.Models;
using SaviumDrive.Services;
using System;
using System.Threading.Tasks;

namespace SaviumDrive.Views
{
    /// <summary>Diálogos de versiones compartidos por la biblioteca y el historial.</summary>
    public static class VersionDialogs
    {
        private static Style StandardButtonStyle => Application.Current.Resources["StandardBtnStyle"] as Style;
        private static ElementTheme Theme => SettingsService.Current.DarkMode ? ElementTheme.Dark : ElementTheme.Light;

        /// <summary>"Ahora todas las acciones que hagas serán sobre esta versión", con opción de no volver a mostrarlo.</summary>
        public static async Task MostrarAvisoCambioAsync(XamlRoot root, string nombreConVersion)
        {
            if (!SettingsService.Current.ShowVersionSwitchNotice) return;

            var chkNoMostrar = new CheckBox { Content = ResourceHelper.GetString("VerSwitchDontShow") };
            var panel = new StackPanel { Spacing = 14, MaxWidth = 420 };
            panel.Children.Add(new TextBlock
            {
                Text = ResourceHelper.GetString("VerSwitchMsg", nombreConVersion),
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(chkNoMostrar);

            var dialog = new ContentDialog
            {
                Title = ResourceHelper.GetString("VerSwitchTitle"),
                Content = panel,
                CloseButtonText = ResourceHelper.GetString("DlgCloseBtn"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = root,
                RequestedTheme = Theme,
                CloseButtonStyle = StandardButtonStyle
            };

            await dialog.ShowAsync();

            if (chkNoMostrar.IsChecked == true)
            {
                SettingsService.Current.ShowVersionSwitchNotice = false;
                SettingsService.Save();
            }
        }

        /// <summary>Muestra la descripción completa. Devuelve true si el usuario quiere editarla.</summary>
        public static async Task<bool> MostrarDescripcionAsync(XamlRoot root, GameConfig juego, GameVersion version)
        {
            string texto = version.HasDescription ? version.Description : ResourceHelper.GetString("HistNoDesc");

            var dialog = new ContentDialog
            {
                Title = ResourceHelper.GetString("HistDescTitle", $"{juego.Name} {version.Label}"),
                Content = new ScrollViewer
                {
                    MaxHeight = 360,
                    Content = new TextBlock
                    {
                        Text = texto,
                        TextWrapping = TextWrapping.Wrap,
                        IsTextSelectionEnabled = true,
                        MaxWidth = 440
                    }
                },
                PrimaryButtonText = ResourceHelper.GetString("HistEditBtn"),
                CloseButtonText = ResourceHelper.GetString("DlgCloseBtn"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = root,
                RequestedTheme = Theme,
                PrimaryButtonStyle = StandardButtonStyle,
                CloseButtonStyle = StandardButtonStyle
            };

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        /// <summary>Pide la nueva descripción. Devuelve null si se cancela.</summary>
        public static async Task<string> EditarDescripcionAsync(XamlRoot root, GameConfig juego, GameVersion version)
        {
            var txt = new TextBox
            {
                Text = version.Description ?? "",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MaxLength = 500,
                Height = 140,
                Width = 420,
                PlaceholderText = ResourceHelper.GetString("HistDescPlaceholder")
            };

            var dialog = new ContentDialog
            {
                Title = ResourceHelper.GetString("HistEditTitle", $"{juego.Name} {version.Label}"),
                Content = txt,
                PrimaryButtonText = ResourceHelper.GetString("HistSaveBtn"),
                CloseButtonText = ResourceHelper.GetString("ConfirmCancelBtn"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = root,
                RequestedTheme = Theme,
                PrimaryButtonStyle = StandardButtonStyle,
                CloseButtonStyle = StandardButtonStyle
            };

            return await dialog.ShowAsync() == ContentDialogResult.Primary ? txt.Text.Trim() : null;
        }
    }
}
