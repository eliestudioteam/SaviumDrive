using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SaviumDrive.Core;
using SaviumDrive.Helpers;
using SaviumDrive.Models;
using SaviumDrive.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SaviumDrive.Views
{
    public sealed partial class LibraryPage : Page
    {
        public ObservableCollection<GameConfig> Juegos { get; set; }
        private CancellationTokenSource _cts;
        private bool _isBusy = false;

        public LibraryPage()
        {
            this.InitializeComponent();
            Juegos = new ObservableCollection<GameConfig>();
            this.Loaded += LibraryPage_Loaded;
        }

        // 🔥 AL ENTRAR: RECUPERAR ESTADO O INICIAR SYNC
        // 🔥 ACTUALIZAR OnNavigatedTo para detectar parámetro "openDialog"
        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (App.LibraryService != null && App.LibraryService.IsBusy)
            {
                SetBusyState(true, "MsgPreparing");
                var last = App.LibraryService.LastProgressArgs;
                if (last != null)
                {
                    MainProgressBar.IsIndeterminate = (last.Percentage < 0);
                    if (last.Percentage >= 0) MainProgressBar.Value = last.Percentage;
                    TxtProgressDetail.Text = last.DetailMessage;
                }
                App.LibraryService.ProgressChanged += OnGlobalProgress;
            }

            // 🔥 DETECTAR SI VIENE DESDE HOMEPAGE CON ACCIÓN RÁPIDA
            if (e.Parameter is string param && param == "openDialog")
            {
                await Task.Delay(300); // Pequeña pausa para que la página se cargue
                await AgregarJuegoFlow();
            }
            else if (e.Parameter is GameSession session)
            {
                await Task.Delay(200);
                await AgregarJuegoFlow(session);
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            if (App.LibraryService != null) App.LibraryService.ProgressChanged -= OnGlobalProgress;
        }

        private void OnGlobalProgress(object sender, GlobalProgressArgs e)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                if (e.IsActive)
                {
                    // Mostrar barra si está oculta
                    if (SyncIndicator.Visibility == Visibility.Collapsed) 
                        SetBusyState(true, "MsgPreparing");

                    // Actualizar barra
                    if (e.Percentage >= 0)
                    {
                        MainProgressBar.IsIndeterminate = false;
                        MainProgressBar.Value = e.Percentage;
                    }
                    else
                    {
                        MainProgressBar.IsIndeterminate = true;
                    }

                    TxtProgressDetail.Text = e.DetailMessage;
                }
                else
                {
                    // Ocultar barra
                    SetBusyState(false, "MsgIdle");
                    _ = RecargarLista();
                }
            });
        }

        private async Task RecargarLista()
        {
            try
            {
                var juegos = await App.LibraryService.CargarSoloLocal();
                ActualizarLista(juegos);
            }
            catch { }
        }

        private async void LibraryPage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!App.LibraryService.IsBusy) BtnText.Text = ResourceHelper.GetString("BtnAddGame");

                if (App.LibraryService == null) return;

                // 1. Carga rápida local
                var local = await App.LibraryService.CargarSoloLocal();
                ActualizarLista(local);

                // 2. 🔥 SINCRONIZAR SI NO ESTÁ OCUPADO
                if (!App.LibraryService.IsBusy)
                {
                    // Nos aseguramos de suscribirnos para ver la animación "Sincronizando..."
                    App.LibraryService.ProgressChanged -= OnGlobalProgress; // Evitar dobles
                    App.LibraryService.ProgressChanged += OnGlobalProgress;

                    // Llamada a la sincronización (ahora reportará progreso a través del evento)
                    var cloudList = await App.LibraryService.SincronizarYActualizar();
                    ActualizarLista(cloudList);
                }
            }
            catch { }
        }

        private void ActualizarLista(System.Collections.Generic.List<GameConfig> lista)
        {
            Juegos.Clear();
            foreach (var j in lista) Juegos.Add(j);
            ActualizarEstadoVacio();
        }

        private void ActualizarEstadoVacio()
        {
            if (EmptyState == null) return;
            EmptyState.Visibility = Juegos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private bool NombreYaExiste(string nombre)
        {
            return Juegos.Any(j => j.Name.Equals(nombre, StringComparison.OrdinalIgnoreCase));
        }

        private Style GetStandardButtonStyle() => Application.Current.Resources["StandardBtnStyle"] as Style;

        private void SetBusyState(bool busy, string messageKey)
        {
            _isBusy = busy;
            if (busy)
            {
                _cts = new CancellationTokenSource();

                // 🔥 CAMBIAR A ESTILO DE CANCELAR
                BtnMainAction.Style = Application.Current.Resources["CancelButtonStyle"] as Style;
                BtnText.Text = ResourceHelper.GetString("BtnCancel");
                BtnIcon.Glyph = "\uE711";

                if (SyncIndicator != null) SyncIndicator.Visibility = Visibility.Visible;
            }
            else
            {
                _cts?.Dispose();
                _cts = null;

                // 🔥 CAMBIAR A ESTILO DE ACCIÓN (Agregar)
                BtnMainAction.Style = Application.Current.Resources["ActionButtonStyle"] as Style;
                BtnText.Text = ResourceHelper.GetString("BtnAddGame");
                BtnIcon.Glyph = "\uE710";

                if (SyncIndicator != null) SyncIndicator.Visibility = Visibility.Collapsed;
            }
        }

        private async void BtnMainAction_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy)
            {
                // 🔥 CANCELAR LA OPERACIÓN ACTUAL
                _cts?.Cancel();
                SetBusyState(false, "MsgIdle");
            }
            else
            {
                await AgregarJuegoFlow();
            }
        }

        private async Task AgregarJuegoFlow(GameSession session = null)
        {
            try
            {
                var window = App.MainWindow;
                if (window == null) window = new MainWindow();
                var dialog = new AddGameDialog(window);
                dialog.XamlRoot = this.XamlRoot;
                if (session != null) dialog.AutoFill(session.Name, session.ExecutablePath);

                var result = await dialog.ShowAsync();

                if (result == ContentDialogResult.Primary)
                {
                    if (!string.IsNullOrEmpty(dialog.GameName) && !string.IsNullOrEmpty(dialog.GamePath))
                    {
                        if (NombreYaExiste(dialog.GameName))
                        {
                            await MostrarAlerta(ResourceHelper.GetString("AlertError"), ResourceHelper.GetString("AlertDuplicateName"));
                            return;
                        }

                        SetBusyState(true, "MsgPreparing");
                        App.LibraryService.ProgressChanged += OnGlobalProgress;

                        try
                        {
                            // 🔥 PASAR EL TOKEN (_cts.Token)
                            var nuevoJuego = await App.LibraryService.AgregarJuegoNuevo(
                                dialog.GameName,
                                dialog.GamePath,
                                dialog.SelectedCoverUrl,
                                _cts.Token // ⬅️ ESTO FALTABA
                            );

                            Juegos.Add(nuevoJuego);
                            ActualizarEstadoVacio();
                            await MostrarAlerta(ResourceHelper.GetString("AlertSuccess"), ResourceHelper.GetString("AlertBackupCreated"));
                        }
                        catch (OperationCanceledException)
                        {
                            await MostrarAlerta(ResourceHelper.GetString("AlertCancel"), ResourceHelper.GetString("AlertOpCancelled"));
                        }
                        finally
                        {
                            SetBusyState(false, "MsgIdle");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SetBusyState(false, "MsgIdle");
                await MostrarAlerta(ResourceHelper.GetString("AlertError"), ex.Message);
            }
        }

        private async void BtnUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is GameConfig juego)
            {
                ContentDialog updateDialog = new ContentDialog
                {
                    Title = ResourceHelper.GetString("ConfirmUpdateTitle"),
                    Content = ResourceHelper.GetString("ConfirmUpdateMsg", juego.Name),
                    PrimaryButtonText = ResourceHelper.GetString("ConfirmUpdateBtn"),
                    CloseButtonText = ResourceHelper.GetString("ConfirmCancelBtn"),
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot,
                    RequestedTheme = SettingsService.Current.DarkMode ? ElementTheme.Dark : ElementTheme.Light,
                    PrimaryButtonStyle = GetStandardButtonStyle(),
                    CloseButtonStyle = GetStandardButtonStyle()
                };

                if (await updateDialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    SetBusyState(true, "MsgUpdating");
                    App.LibraryService.ProgressChanged += OnGlobalProgress;

                    try
                    {
                        // 🔥 PASAR EL TOKEN
                        await App.LibraryService.ActualizarBackup(juego, _cts.Token);
                        await MostrarAlerta(ResourceHelper.GetString("AlertSuccess"), ResourceHelper.GetString("AlertUpdateSuccess"));
                    }
                    catch (OperationCanceledException)
                    {
                        await MostrarAlerta(ResourceHelper.GetString("AlertCancel"), ResourceHelper.GetString("AlertOpCancelled"));
                    }
                    catch (Exception ex)
                    {
                        await MostrarAlerta(ResourceHelper.GetString("AlertError"), ex.Message);
                    }
                    finally
                    {
                        SetBusyState(false, "MsgIdle");
                    }
                }
            }
        }

        private async void BtnRestore_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is GameConfig juego)
            {
                ContentDialog restoreDialog = new ContentDialog
                {
                    Title = ResourceHelper.GetString("ConfirmRestoreTitle"),
                    Content = ResourceHelper.GetString("ConfirmRestoreMsg", juego.LocalPath),
                    PrimaryButtonText = ResourceHelper.GetString("ConfirmRestoreBtn"),
                    CloseButtonText = ResourceHelper.GetString("ConfirmCancelBtn"),
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot,
                    RequestedTheme = SettingsService.Current.DarkMode ? ElementTheme.Dark : ElementTheme.Light,
                    PrimaryButtonStyle = GetStandardButtonStyle(),
                    CloseButtonStyle = GetStandardButtonStyle()
                };

                if (await restoreDialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    SetBusyState(true, "MsgConnecting");
                    App.LibraryService.ProgressChanged += OnGlobalProgress;

                    try
                    {
                        // 🔥 PASAR EL TOKEN
                        await App.LibraryService.RestaurarJuego(juego, _cts.Token);
                        await MostrarAlerta(ResourceHelper.GetString("AlertSuccess"), ResourceHelper.GetString("AlertRestoreSuccess"));
                    }
                    catch (OperationCanceledException)
                    {
                        await MostrarAlerta(ResourceHelper.GetString("AlertCancel"), ResourceHelper.GetString("AlertOpCancelled"));
                    }
                    catch (Exception ex)
                    {
                        await MostrarAlerta(ResourceHelper.GetString("AlertError"), ex.Message);
                    }
                    finally
                    {
                        SetBusyState(false, "MsgIdle");
                    }
                }
            }
        }

        private async void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is GameConfig juego)
            {
                ContentDialog deleteDialog = new ContentDialog
                {
                    Title = ResourceHelper.GetString("ConfirmDeleteTitle", juego.Name),
                    Content = ResourceHelper.GetString("ConfirmDeleteMsg"),
                    PrimaryButtonText = ResourceHelper.GetString("ConfirmDeleteBtn"),
                    CloseButtonText = ResourceHelper.GetString("ConfirmCancelBtn"),
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot,
                    RequestedTheme = SettingsService.Current.DarkMode ? ElementTheme.Dark : ElementTheme.Light,
                    PrimaryButtonStyle = GetStandardButtonStyle(),
                    CloseButtonStyle = GetStandardButtonStyle()
                };

                if (await deleteDialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    // 🔥 CONTADOR DE 5 SEGUNDOS CON POSIBILIDAD DE CANCELAR
                    var countdownDialog = new ContentDialog
                    {
                        Title = ResourceHelper.GetString("AlertWarning"),
                        PrimaryButtonText = ResourceHelper.GetString("BtnCancel"),
                        DefaultButton = ContentDialogButton.Primary,
                        XamlRoot = this.XamlRoot,
                        RequestedTheme = SettingsService.Current.DarkMode ? ElementTheme.Dark : ElementTheme.Light,
                        PrimaryButtonStyle = GetStandardButtonStyle()
                    };

                    // 🔥 CREAR UN STACKPANEL PARA MOSTRAR MÚLTIPLES ELEMENTOS
                    var contentPanel = new StackPanel
                    {
                        Spacing = 12,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };

                    // 🔥 TEXTO DE CUENTA REGRESIVA
                    var countdownText = new TextBlock
                    {
                        Text = string.Format(ResourceHelper.GetString("MsgDeletingIn"), 5),
                        FontSize = 18,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        TextWrapping = TextWrapping.WrapWholeWords
                    };

                    // 🔥 MOSTRAR EL NOMBRE DEL JUEGO/BACKUP
                    var pathText = new TextBlock
                    {
                        Text = juego.Name,
                        FontSize = 16,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 500,
                        TextAlignment = TextAlignment.Center
                    };

                    contentPanel.Children.Add(countdownText);
                    contentPanel.Children.Add(pathText);

                    countdownDialog.Content = contentPanel;

                    int segundos = 5;
                    bool cancelado = false;
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };

                    timer.Tick += (s, args) =>
                    {
                        segundos--;
                        if (segundos <= 0)
                        {
                            timer.Stop();
                            countdownDialog.Hide();
                        }
                        else
                        {
                            countdownText.Text = string.Format(ResourceHelper.GetString("MsgDeletingIn"), segundos);
                        }
                    };

                    timer.Start();

                    var dialogTask = countdownDialog.ShowAsync();
                    var result = await dialogTask;

                    timer.Stop();

                    if (result == ContentDialogResult.Primary)
                    {
                        // Usuario canceló
                        cancelado = true;
                    }

                    if (!cancelado)
                    {
                        // Proceder a eliminar
                        SetBusyState(true, "MsgDeleting");
                        App.LibraryService.ProgressChanged += OnGlobalProgress;

                        try
                        {
                            // 🔥 ELIMINAR DEL SERVICIO (Drive + JSON local)
                            await App.LibraryService.EliminarJuego(juego);

                            // 🔥 ASEGURAR ACTUALIZACIÓN EN EL HILO DE UI
                            this.DispatcherQueue.TryEnqueue(() =>
                            {
                                // Buscar el juego en la colección ObservableCollection
                                var juegoEnLista = Juegos.FirstOrDefault(j => j.Id == juego.Id);
                                if (juegoEnLista != null)
                                {
                                    Juegos.Remove(juegoEnLista);
                                }
                                ActualizarEstadoVacio();
                            });

                            // Pequeña pausa para que la UI se actualice antes de mostrar la alerta
                            await Task.Delay(100);

                            await MostrarAlerta(
                                ResourceHelper.GetString("AlertSuccess"),
                                ResourceHelper.GetString("AlertDeleteSuccess")
                            );
                        }
                        catch (Exception ex)
                        {
                            await MostrarAlerta(ResourceHelper.GetString("AlertError"), ex.Message);
                        }
                        finally
                        {
                            SetBusyState(false, "MsgIdle");
                            App.LibraryService.ProgressChanged -= OnGlobalProgress;
                        }
                    }
                    else
                    {
                        await MostrarAlerta(ResourceHelper.GetString("AlertCancel"), ResourceHelper.GetString("AlertOpCancelled"));
                    }
                }
            }
        }

        private async Task MostrarAlerta(string titulo, string mensaje)
        {
            ContentDialog dialog = new ContentDialog
            {
                Title = titulo,
                Content = mensaje,
                CloseButtonText = ResourceHelper.GetString("DlgCloseBtn"),
                XamlRoot = this.Content.XamlRoot,
                RequestedTheme = SettingsService.Current.DarkMode ? ElementTheme.Dark : ElementTheme.Light,
                CloseButtonStyle = GetStandardButtonStyle()
            };
            await dialog.ShowAsync();
        }
    }
}