using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using SaviumDrive.Helpers;
using SaviumDrive.Models;
using SaviumDrive.Services;
using System;

namespace SaviumDrive.Views
{
    /// <summary>
    /// Ventana "Versiones": avisa de que ya hay una versión en la nube y pide la descripción de la nueva.
    /// </summary>
    public sealed partial class NewVersionDialog : ContentDialog
    {
        public string Description => TxtDescription.Text?.Trim() ?? "";

        public NewVersionDialog(GameConfig juego)
        {
            this.InitializeComponent();
            this.RequestedTheme = SettingsService.Current.DarkMode ? ElementTheme.Dark : ElementTheme.Light;

            int cantidad = juego.Versions?.Count ?? 0;
            string siguiente = $"V{juego.NextVersionNumber}";

            TxtGameTitle.Text = juego.Name;
            TxtNextVersion.Text = ResourceHelper.GetString("DlgNewVersionNext", siguiente);
            TxtMessage.Text = cantidad == 1
                ? ResourceHelper.GetString("DlgNewVersionMsgOne", juego.Name, siguiente)
                : ResourceHelper.GetString("DlgNewVersionMsgMany", juego.Name, cantidad, siguiente);

            try { ImgCover.Source = new BitmapImage(new Uri(juego.SafeCoverUrl)); } catch { }
        }

        private void TxtDescription_TextChanged(object sender, TextChangedEventArgs e)
        {
            int largo = TxtDescription.Text?.Length ?? 0;
            TxtCounter.Text = $"{largo}/{TxtDescription.MaxLength}";

            // La descripción es obligatoria: es lo que diferencia una versión de otra
            IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(TxtDescription.Text);
        }
    }
}
