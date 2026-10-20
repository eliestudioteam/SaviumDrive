using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using SaviumDrive.Helpers;
using SaviumDrive.Models;
using SaviumDrive.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace SaviumDrive.Views
{
    /// <summary>Fila del historial (datos ya formateados para la vista).</summary>
    public sealed class VersionItem : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        private string _tamanoText;
        /// <summary>Tamaño en Drive; se rellena después de mostrar la lista.</summary>
        public string TamanoText
        {
            get => _tamanoText;
            set
            {
                _tamanoText = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(TamanoText)));
            }
        }

        public GameVersion Version { get; set; }
        public string Label { get; set; }
        public bool IsActive { get; set; }
        public bool IsNotActive => !IsActive;
        public string CreatedText { get; set; }
        public string LastBackupText { get; set; }
        public string DescriptionText { get; set; }
        public double DescriptionOpacity { get; set; } = 1;
    }

    /// <summary>Historial de versiones de un juego (parámetro de navegación: Id del juego).</summary>
    public sealed partial class VersionHistoryPage : Page
    {
        public ObservableCollection<VersionItem> Items { get; } = new ObservableCollection<VersionItem>();

        private string _juegoId;
        private GameConfig _juego;
        private bool _ocupado;
        private System.Threading.CancellationTokenSource _ctsTamanos;

        public VersionHistoryPage()
        {
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _juegoId = e.Parameter as string;
            await Cargar();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            _ctsTamanos?.Cancel();
        }

        private async Task Cargar()
        {
            if (App.LibraryService == null || string.IsNullOrEmpty(_juegoId))
            {
                VolverABiblioteca();
                return;
            }

            var juegos = await App.LibraryService.CargarSoloLocal();
            _juego = juegos.FirstOrDefault(j => j.Id == _juegoId);
            if (_juego == null)
            {
                VolverABiblioteca();
                return;
            }

            TxtGameName.Text = _juego.Name;
            TxtSummary.Text = ResourceHelper.GetString("HistSummary", _juego.Versions.Count, _juego.ActiveVersion?.Label);
            ImgCover.Source = new BitmapImage(_juego.SafeCoverUri) { DecodePixelWidth = 64, DecodePixelType = DecodePixelType.Logical };

            Items.Clear();
            foreach (var v in _juego.Versions.OrderByDescending(v => v.Number))
            {
                Items.Add(new VersionItem
                {
                    Version = v,
                    Label = v.Label,
                    IsActive = v.Id == _juego.ActiveVersion?.Id,
                    CreatedText = ResourceHelper.GetString("HistCreated", FormatearFecha(v.CreatedAt)),
                    LastBackupText = ResourceHelper.GetString("HistLastSaved", FormatearFecha(v.LastBackup)),
                    TamanoText = ResourceHelper.GetString("HistSizeCalculating"),
                    DescriptionText = v.HasDescription ? v.Description : ResourceHelper.GetString("HistNoDesc"),
                    DescriptionOpacity = v.HasDescription ? 1 : 0.6
                });
            }

            _ = CargarTamanos(_juego, Items.ToList());
        }

        /// <summary>Calcula en segundo plano cuánto ocupa cada versión en Drive y el total.</summary>
        private async Task CargarTamanos(GameConfig juego, System.Collections.Generic.List<VersionItem> filas)
        {
            _ctsTamanos?.Cancel();
            var cts = new System.Threading.CancellationTokenSource();
            _ctsTamanos = cts;

            TxtTotalSize.Text = ResourceHelper.GetString("HistTotalCalculating");
            long total = 0;
            bool completo = true;

            var servicio = App.LibraryService; // la sesión puede cerrarse mientras se calcula
            if (servicio == null) return;

            foreach (var fila in filas)
            {
                try
                {
                    long bytes = await servicio.CalcularTamanoVersion(juego, fila.Version, cts.Token);
                    if (cts.IsCancellationRequested) return;
                    fila.TamanoText = ResourceHelper.GetString("HistSize", GameLibraryService.FormatBytes(bytes));
                    total += bytes;
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"No se pudo calcular el tamaño de {fila.Label}: {ex.Message}");
                    fila.TamanoText = ResourceHelper.GetString("HistSizeUnavailable");
                    completo = false;
                }
            }

            if (cts.IsCancellationRequested) return;
            TxtTotalSize.Text = completo
                ? ResourceHelper.GetString("HistTotalSize", GameLibraryService.FormatBytes(total))
                : ResourceHelper.GetString("HistTotalPartial", GameLibraryService.FormatBytes(total));
        }

        private static string FormatearFecha(DateTime fecha)
        {
            return fecha == default ? "-" : fecha.ToLocalTime().ToString("g");
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e) => VolverABiblioteca();

        private void VolverABiblioteca()
        {
            if (App.MainWindow is MainWindow main)
                main.NavegarExterna(typeof(LibraryPage), "Library");
            else if (Frame.CanGoBack)
                Frame.GoBack();
        }

        // Evita lanzar dos acciones a la vez (o una mientras la biblioteca sube/descarga)
        private async Task<bool> PuedeActuar()
        {
            if (_ocupado || _juego == null) return false;
            if (App.LibraryService.IsBusy)
            {
                await MostrarAlerta(ResourceHelper.GetString("AlertWarning"), ResourceHelper.GetString("MsgBusyVersion"));
                return false;
            }
            return true;
        }

        private void SetOcupado(bool ocupado)
        {
            _ocupado = ocupado;
            BusyRing.IsActive = ocupado;
            VersionsList.IsEnabled = !ocupado;
        }

        private async void BtnUseVersion_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not VersionItem item || !await PuedeActuar()) return;

            try
            {
                SetOcupado(true);
                await App.LibraryService.CambiarVersionActiva(_juego, item.Version);
                await Cargar();
                SetOcupado(false);
                await VersionDialogs.MostrarAvisoCambioAsync(this.XamlRoot, _juego.DisplayName);
            }
            catch (Exception ex)
            {
                SetOcupado(false);
                await MostrarAlerta(ResourceHelper.GetString("AlertError"), ex.Message);
            }
        }

        private async void BtnShowDescription_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not VersionItem item || _ocupado || _juego == null) return;

            if (await VersionDialogs.MostrarDescripcionAsync(this.XamlRoot, _juego, item.Version))
            {
                await EditarDescripcion(item);
            }
        }

        private async void BtnEditDescription_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not VersionItem item || _ocupado || _juego == null) return;
            await EditarDescripcion(item);
        }

        private async Task EditarDescripcion(VersionItem item)
        {
            string nueva = await VersionDialogs.EditarDescripcionAsync(this.XamlRoot, _juego, item.Version);
            if (nueva == null || nueva == (item.Version.Description ?? "")) return;

            try
            {
                SetOcupado(true);
                await App.LibraryService.ActualizarDescripcion(_juego, item.Version, nueva);
                await Cargar();
            }
            catch (Exception ex)
            {
                await MostrarAlerta(ResourceHelper.GetString("AlertError"), ex.Message);
            }
            finally
            {
                SetOcupado(false);
            }
        }

        private async void BtnDeleteVersion_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not VersionItem item || !await PuedeActuar()) return;

            bool esLaUnica = _juego.Versions.Count <= 1;
            var confirmar = new ContentDialog
            {
                Title = ResourceHelper.GetString("ConfirmDeleteTitle", $"{_juego.Name} {item.Label}"),
                Content = esLaUnica
                    ? ResourceHelper.GetString("ConfirmDeleteMsg")
                    : ResourceHelper.GetString("ConfirmDeleteVersionMsg", item.Label),
                PrimaryButtonText = ResourceHelper.GetString("ConfirmDeleteBtn"),
                CloseButtonText = ResourceHelper.GetString("ConfirmCancelBtn"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot,
                RequestedTheme = SettingsService.Current.DarkMode ? ElementTheme.Dark : ElementTheme.Light,
                PrimaryButtonStyle = Application.Current.Resources["StandardBtnStyle"] as Style,
                CloseButtonStyle = Application.Current.Resources["StandardBtnStyle"] as Style
            };

            if (await confirmar.ShowAsync() != ContentDialogResult.Primary) return;

            bool juegoEliminado = false;
            try
            {
                SetOcupado(true);
                juegoEliminado = await App.LibraryService.EliminarVersion(_juego, item.Version);
            }
            catch (Exception ex)
            {
                SetOcupado(false);
                await MostrarAlerta(ResourceHelper.GetString("AlertError"), ex.Message);
                return;
            }

            SetOcupado(false);

            if (juegoEliminado)
            {
                VolverABiblioteca();
                return;
            }

            await Cargar();
            await MostrarAlerta(ResourceHelper.GetString("AlertSuccess"), ResourceHelper.GetString("AlertVersionDeleted", item.Label));
        }

        private async Task MostrarAlerta(string titulo, string mensaje)
        {
            var dialog = new ContentDialog
            {
                Title = titulo,
                Content = mensaje,
                CloseButtonText = ResourceHelper.GetString("DlgCloseBtn"),
                XamlRoot = this.XamlRoot,
                RequestedTheme = SettingsService.Current.DarkMode ? ElementTheme.Dark : ElementTheme.Light,
                CloseButtonStyle = Application.Current.Resources["StandardBtnStyle"] as Style
            };
            await dialog.ShowAsync();
        }
    }
}
