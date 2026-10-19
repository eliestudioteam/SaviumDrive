using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SaviumDrive.Helpers;
using SaviumDrive.Models;
using SaviumDrive.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SaviumDrive.Views
{
    public sealed partial class HomePage : Page
    {
        private bool _isConnected = true;

        public HomePage()
        {
            this.InitializeComponent();
            this.Loaded += HomePage_Loaded;
            this.Unloaded += HomePage_Unloaded;

            // Suscribirse a cambios de conectividad
            if (App.NetworkService != null)
            {
                App.NetworkService.ConnectivityChanged += OnConnectivityChanged;
            }
        }

        private async void HomePage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // 🔥 VERIFICAR CONECTIVIDAD PRIMERO
                if (App.NetworkService != null)
                {
                    _isConnected = await App.NetworkService.IsConnectedAsync();
                    ActualizarEstadoConexion();
                }

                // Limpiar visualmente
                TxtGamesCount.Text = "-";
                TxtStorageUsed.Text = "-";
                TxtStorageRemaining.Text = "-";
                StorageProgressBar.Value = 0;
                StorageLoadingBar.Visibility = Visibility.Visible;

                if (App.LibraryService == null) return;

                // 1️⃣ CARGAR JUEGOS LOCALES (instantáneo)
                var juegos = await App.LibraryService.CargarSoloLocal();
                int total = juegos.Count;

                // Contador de juegos
                TxtGamesCount.Text = ResourceHelper.GetString("MsgGamesCount", total);

                // 2️⃣ MOSTRAR LOS ÚLTIMOS 3 BACKUPS INSTANTÁNEAMENTE
                var ultimosBackups = juegos
                    .OrderByDescending(j => j.LastBackup)
                    .Take(3)
                    .ToList();

                if (ultimosBackups.Any())
                {
                    EmptyRecentState.Visibility = Visibility.Collapsed;
                    RecentBackupsPanel.Visibility = Visibility.Visible;
                    RecentBackupsPanel.Children.Clear();

                    foreach (var juego in ultimosBackups)
                    {
                        RecentBackupsPanel.Children.Add(CrearItemBackup(juego));
                    }
                }
                else
                {
                    EmptyRecentState.Visibility = Visibility.Visible;
                    RecentBackupsPanel.Visibility = Visibility.Collapsed;
                }

                // 3️⃣ CALCULAR ALMACENAMIENTO EN SEGUNDO PLANO (sin trabar UI)
                _ = Task.Run(async () =>
                {
                    await CalcularAlmacenamientoDrive(juegos);
                });
            }
            catch (Exception ex)
            {
                TxtGamesCount.Text = "-";
                System.Diagnostics.Debug.WriteLine($"Error en HomePage_Loaded: {ex.Message}");
            }
        }

        private void HomePage_Unloaded(object sender, RoutedEventArgs e)
        {
            // Desuscribirse al salir de la página
            if (App.NetworkService != null)
            {
                App.NetworkService.ConnectivityChanged -= OnConnectivityChanged;
            }
        }

        private void OnConnectivityChanged(object sender, bool isConnected)
        {
            _isConnected = isConnected;

            DispatcherQueue.TryEnqueue(() =>
            {
                ActualizarEstadoConexion();
            });
        }

        // 🔥 MÉTODO SIMPLIFICADO - USA LOS NOMBRES DIRECTAMENTE
        private void ActualizarEstadoConexion()
        {
            if (_isConnected)
            {
                TxtConnectionStatus.Text = ResourceHelper.GetString("StatusConnected");
                TxtConnectionMessage.Text = ResourceHelper.GetString("StatusSyncReady");
                TxtConnectionMessage.Foreground = App.Current.Resources["SuccessColor"] as Microsoft.UI.Xaml.Media.Brush;
            }
            else
            {
                TxtConnectionStatus.Text = ResourceHelper.GetString("StatusDisconnected");
                TxtConnectionMessage.Text = ResourceHelper.GetString("StatusNoConnection");
                TxtConnectionMessage.Foreground = App.Current.Resources["ErrorColor"] as Microsoft.UI.Xaml.Media.Brush;
            }
        }

        private async Task CalcularAlmacenamientoDrive(List<GameConfig> juegos)
        {
            try
            {
                if (App.LibraryService == null) return;

                // 1️⃣ Calcular espacio total de backups
                long espacioBackups = await App.LibraryService.CalcularEspacioBackupsEnDrive();

                // 2️⃣ Obtener cuota real del usuario (Drive + Gmail + Fotos + todo)
                var cuota = await App.LibraryService.ObtenerCuotaDrive();

                if (!cuota.HasValue)
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        TxtStorageUsed.Text = FormatBytes(espacioBackups);
                        TxtStorageRemaining.Text = ResourceHelper.GetString("StorageUnavailable");
                        StorageProgressBar.Value = 0;
                        StorageLoadingBar.Visibility = Visibility.Collapsed;
                    });
                    return;
                }

                long usadoTotalCuenta = cuota.Value.usado;
                long totalCuenta = cuota.Value.total;

                // 3️⃣ Progreso basado solo en backups
                double percent = totalCuenta > 0 ? (double)espacioBackups / totalCuenta * 100 : 0;

                // 4️⃣ Actualizar UI
                DispatcherQueue.TryEnqueue(() =>
                {
                    TxtStorageUsed.Text = $"Backups: {FormatBytes(espacioBackups)}";
                    TxtStorageRemaining.Text = $"{FormatBytes(usadoTotalCuenta)} / {FormatBytes(totalCuenta)}";
                    StorageProgressBar.Value = percent;
                    StorageLoadingBar.Visibility = Visibility.Collapsed;
                });
            }
            catch (Exception ex)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    TxtStorageUsed.Text = "-";
                    TxtStorageRemaining.Text = ResourceHelper.GetString("StorageUnavailable");
                    StorageProgressBar.Value = 0;
                    StorageLoadingBar.Visibility = Visibility.Collapsed;
                });
                System.Diagnostics.Debug.WriteLine($"Error calculando almacenamiento: {ex.Message}");
            }
        }

        private Grid CrearItemBackup(GameConfig juego)
        {
            // Detectar tema actual
            bool isDark = this.ActualTheme == ElementTheme.Dark;

            var grid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(15),
                // ✅ Color directo según tema
                Background = new SolidColorBrush(isDark ?
                    Windows.UI.Color.FromArgb(255, 58, 58, 58) :  // Dark
                    Windows.UI.Color.FromArgb(255, 231, 230, 230)),   // Light
                CornerRadius = new CornerRadius(6)
            };

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });

            // Imagen de portada
            var image = new Image
            {
                Width = 50,
                Height = 75,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
                Margin = new Thickness(0, 0, 12, 0)
            };

            if (!string.IsNullOrEmpty(juego.SafeCoverUrl))
            {
                image.Source = new BitmapImage(new Uri(juego.SafeCoverUrl));
            }

            Grid.SetColumn(image, 0);
            grid.Children.Add(image);

            // Información del juego
            var infoPanel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 4
            };

            var nombreText = new TextBlock
            {
                Text = juego.DisplayName,
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                // ✅ Blanco en ambos temas
                Foreground = new SolidColorBrush(isDark ?
                    Windows.UI.Color.FromArgb(255, 255, 255, 255) :
                    Microsoft.UI.Colors.Black),
            };

            var rutaText = new TextBlock
            {
                Text = juego.LocalPath,
                FontSize = 11,
                // ✅ Blanco en Light, gris claro en Dark
                Foreground = new SolidColorBrush(isDark ?
                    Windows.UI.Color.FromArgb(255, 255, 255, 255) : 
                    Microsoft.UI.Colors.Black),
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            infoPanel.Children.Add(nombreText);
            infoPanel.Children.Add(rutaText);

            Grid.SetColumn(infoPanel, 1);
            grid.Children.Add(infoPanel);

            // Fecha de actualización
            var datePanel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var fechaText = new TextBlock
            {
                Text = juego.LastBackup.ToLocalTime().ToString("g"),
                Foreground = new SolidColorBrush(isDark ?
                    Windows.UI.Color.FromArgb(255, 184, 184, 184) :
                    Microsoft.UI.Colors.Black),
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var labelText = new TextBlock
            {
                Text = ResourceHelper.GetString("MsgUpdated"),
                FontSize = 10,
                Foreground = new SolidColorBrush(isDark ?
                    Windows.UI.Color.FromArgb(255, 184, 184, 184) :
                    Microsoft.UI.Colors.Black),
                HorizontalAlignment = HorizontalAlignment.Right
            };

            datePanel.Children.Add(fechaText);
            datePanel.Children.Add(labelText);

            Grid.SetColumn(datePanel, 2);
            grid.Children.Add(datePanel);

            return grid;
        }

        private void BtnQuickAddBackup_Click(object sender, RoutedEventArgs e)
        {
            // Obtenemos la referencia a la ventana principal
            if (App.MainWindow is MainWindow main)
            {
                // Le pedimos que navegue a LibraryPage, marque el botón "Library" y pase el parámetro
                main.NavegarExterna(typeof(LibraryPage), "Library", "openDialog");
            }
        }

        private string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = (decimal)bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number = number / 1024;
                counter++;
            }
            return string.Format("{0:n1} {1}", number, suffixes[counter]);
        }
    }
}