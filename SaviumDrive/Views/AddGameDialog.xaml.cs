using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using SaviumDrive.Helpers;
using SaviumDrive.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage.Pickers;

namespace SaviumDrive.Views
{
    public sealed partial class AddGameDialog : ContentDialog
    {
        public string GameName => TxtGameName.Text;
        public string GamePath => TxtGamePath.Text;
        public string SelectedCoverUrl { get; private set; } = "";

        // Carrusel
        private List<string> _foundCovers = new List<string>();
        private int _currentCoverIndex = 0;

        private Window _mainWindow;
        private DispatcherTimer _searchTimer;
        // Cada búsqueda de portada lleva un número: una respuesta vieja no pisa a la del último título
        private int _busquedaPortada;

        // Ruta de partidas rellenada sola (Ludusavi): se puede reemplazar al cambiar el nombre,
        // pero nunca una carpeta que el usuario eligió con "Examinar"
        private bool _rutaAutomatica = false;
        private CancellationTokenSource _ctsRuta;

        // Juego detectado al cerrarse: con su ID de Steam la carpeta se encuentra aunque el nombre varíe
        private string _nombreDetectado;
        private string _steamAppIdDetectado;

        public AddGameDialog(Window window)
        {
            this.InitializeComponent();
            this.RequestedTheme = SettingsService.Current.DarkMode ? ElementTheme.Dark : ElementTheme.Light;
            _mainWindow = window;

            _searchTimer = new DispatcherTimer();
            _searchTimer.Interval = TimeSpan.FromMilliseconds(800);
            _searchTimer.Tick += SearchTimer_Tick;
        }

        public void AutoFill(string name, string exePath, string steamAppId = null)
        {
            _nombreDetectado = name;
            _steamAppIdDetectado = steamAppId;
            TxtGameName.Text = name;
            TxtGamePath.PlaceholderText = "Por favor, selecciona la carpeta de saves...";

            // El diálogo aún no se muestra y TextChanged no siempre llega: buscar la portada al abrirse
            Opened += BuscarAlAbrir;

            // Primero la base de datos de Ludusavi (por ID de Steam o por nombre); si no, las carpetas habituales
            _ = BuscarRutaAutomatica(name);
        }

        private void BuscarAlAbrir(ContentDialog sender, ContentDialogOpenedEventArgs args)
        {
            Opened -= BuscarAlAbrir;
            if (TxtGameName.Text.Length > 2 && _foundCovers.Count == 0)
            {
                _searchTimer.Stop();
                _ = BuscarPortadas(buscarRuta: false); // la ruta ya se está buscando
            }
        }

        /// <summary>Busca en la base de datos de Ludusavi dónde guarda partidas el juego y rellena la carpeta si existe.</summary>
        private async System.Threading.Tasks.Task BuscarRutaAutomatica(string nombre)
        {
            if (!string.IsNullOrEmpty(TxtGamePath.Text) && !_rutaAutomatica) return;

            _ctsRuta?.Cancel();
            var cts = new CancellationTokenSource();
            _ctsRuta = cts;

            MostrarPistaRuta(LudusaviService.IndiceDisponible ? "PathHintSearching" : "PathHintDownloading");

            string carpeta = null;
            // El ID de Steam solo vale mientras el nombre sea el del juego detectado
            string steamAppId = string.Equals(nombre, _nombreDetectado, StringComparison.Ordinal) ? _steamAppIdDetectado : null;
            try { carpeta = await LudusaviService.BuscarCarpetaAsync(nombre, steamAppId, cts.Token); }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"⚠️ Ludusavi: {ex.Message}"); }

            // El usuario siguió escribiendo o eligió una carpeta mientras se buscaba
            if (cts.IsCancellationRequested || _ctsRuta != cts) return;
            if (!string.IsNullOrEmpty(TxtGamePath.Text) && !_rutaAutomatica) { MostrarPistaRuta(null); return; }

            string pista = "PathHintFound";
            if (carpeta == null)
            {
                // Sin datos en Ludusavi: probar las carpetas habituales (Mis juegos, AppData, Saved Games...)
                carpeta = IntentarAdivinarRutaSave(nombre);
                pista = "PathHintGuessed";
            }

            if (carpeta != null)
            {
                TxtGamePath.Text = carpeta;
                _rutaAutomatica = true;
                MostrarPistaRuta(pista);
            }
            else
            {
                if (_rutaAutomatica) { TxtGamePath.Text = ""; _rutaAutomatica = false; }
                MostrarPistaRuta("PathHintNotFound");
            }
        }

        private void MostrarPistaRuta(string clave)
        {
            if (clave == null)
            {
                TxtPathHint.Visibility = Visibility.Collapsed;
                return;
            }
            TxtPathHint.Text = ResourceHelper.GetString(clave);
            TxtPathHint.Visibility = Visibility.Visible;
        }

        private string IntentarAdivinarRutaSave(string gameName)
        {
            string cleanName = gameName.Replace(".exe", "").Trim();
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string savedGames = Path.Combine(userProfile, "Saved Games");

            string[] posiblesRutas = {
                Path.Combine(docs, "My Games", cleanName),
                Path.Combine(docs, cleanName),
                Path.Combine(appData, cleanName),
                Path.Combine(localAppData, cleanName),
                Path.Combine(localAppData, cleanName, "Saved"),
                Path.Combine(savedGames, cleanName)
            };

            foreach (var ruta in posiblesRutas)
            {
                if (Directory.Exists(ruta)) return ruta;
            }

            try
            {
                string myGames = Path.Combine(docs, "My Games");
                if (Directory.Exists(myGames))
                {
                    var match = Directory.GetDirectories(myGames)
                                         .FirstOrDefault(d => d.Contains(cleanName, StringComparison.OrdinalIgnoreCase));
                    if (match != null) return match;
                }
            }
            catch { }

            return null;
        }

        private async void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var folderPicker = new FolderPicker();
            folderPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            folderPicker.FileTypeFilter.Add("*");

            if (_mainWindow == null) return;

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_mainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                _ctsRuta?.Cancel();
                _rutaAutomatica = false;
                MostrarPistaRuta(null);
                TxtGamePath.Text = folder.Path;
            }
        }

        private void TxtGameName_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchTimer.Stop();
            _busquedaPortada++; // descarta la búsqueda en curso
            OcultarControlesCarrusel();

            if (TxtGameName.Text.Length > 2)
            {
                TxtCoverHint.Text = "🔍 ...";
                TxtCoverHint.Visibility = Visibility.Visible;
                _searchTimer.Start();
            }
        }

        private void SearchTimer_Tick(object sender, object e)
        {
            _searchTimer.Stop();
            _ = BuscarPortadas(buscarRuta: true);
        }

        private async Task BuscarPortadas(bool buscarRuta)
        {
            int busqueda = ++_busquedaPortada;
            string nombre = TxtGameName.Text;
            LoadingRing.IsActive = true;
            TxtCoverHint.Visibility = Visibility.Collapsed;

            // Buscar a la vez la carpeta de partidas (no espera a la portada)
            if (buscarRuta) _ = BuscarRutaAutomatica(nombre);

            // Con el ID de Steam del juego detectado, la portada es la exacta aunque el nombre sea ambiguo
            string steamAppId = string.Equals(nombre, _nombreDetectado, StringComparison.Ordinal) ? _steamAppIdDetectado : null;
            var portadas = await new SteamGridApi().BuscarPortadas(nombre, steamAppId);
            if (busqueda != _busquedaPortada) return; // el usuario cambió el título mientras tanto

            _foundCovers = portadas;
            _currentCoverIndex = 0;
            LoadingRing.IsActive = false;

            if (_foundCovers != null && _foundCovers.Count > 0)
            {
                ActualizarVistaPortada();

                if (_foundCovers.Count > 1)
                {
                    BtnPrevCover.Visibility = Visibility.Visible;
                    BtnNextCover.Visibility = Visibility.Visible;

                    // 🔥 CORRECCIÓN: Mostrar el borde contenedor
                    IndexBadge.Visibility = Visibility.Visible;
                }
            }
            else
            {
                TxtCoverHint.Text = "❌";
                TxtCoverHint.Visibility = Visibility.Visible;
                ImgCoverPreview.Source = null;
                SelectedCoverUrl = "";
            }
        }

        private void ActualizarVistaPortada()
        {
            if (_foundCovers.Count == 0) return;

            string url = _foundCovers[_currentCoverIndex];
            SelectedCoverUrl = url;
            ImgCoverPreview.Source = new BitmapImage(new Uri(url)) { DecodePixelWidth = 150, DecodePixelType = DecodePixelType.Logical };

            TxtIndexIndicator.Text = $"{_currentCoverIndex + 1}/{_foundCovers.Count}";
        }

        private void OcultarControlesCarrusel()
        {
            BtnPrevCover.Visibility = Visibility.Collapsed;
            BtnNextCover.Visibility = Visibility.Collapsed;

            // 🔥 CORRECCIÓN: Ocultar el borde contenedor
            IndexBadge.Visibility = Visibility.Collapsed;

            ImgCoverPreview.Source = null;
            _foundCovers.Clear();
        }

        private void BtnPrevCover_Click(object sender, RoutedEventArgs e)
        {
            if (_foundCovers.Count <= 1) return;

            _currentCoverIndex--;
            if (_currentCoverIndex < 0) _currentCoverIndex = _foundCovers.Count - 1;

            ActualizarVistaPortada();
        }

        private void BtnNextCover_Click(object sender, RoutedEventArgs e)
        {
            if (_foundCovers.Count <= 1) return;

            _currentCoverIndex++;
            if (_currentCoverIndex >= _foundCovers.Count) _currentCoverIndex = 0;

            ActualizarVistaPortada();
        }
    }
}