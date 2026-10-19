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
                SuscribirProgreso();
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

        // Quitar antes de añadir: así el handler nunca queda suscrito dos veces
        private void SuscribirProgreso()
        {
            if (App.LibraryService == null) return;
            App.LibraryService.ProgressChanged -= OnGlobalProgress;
            App.LibraryService.ProgressChanged += OnGlobalProgress;
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
                if (App.LibraryService == null) return;

                if (!App.LibraryService.IsBusy) BtnText.Text = ResourceHelper.GetString("BtnAddGame");

                // 1. Carga rápida local
                var local = await App.LibraryService.CargarSoloLocal();
                ActualizarLista(local);

                // 2. 🔥 SINCRONIZAR SI NO ESTÁ OCUPADO
                if (!App.LibraryService.IsBusy)
                {
                    // Nos aseguramos de suscribirnos para ver la animación "Sincronizando..."
                    SuscribirProgreso();

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
                        SuscribirProgreso();

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
                if (juego.NeedsRelink && !await PedirCarpetaEnEstePc(juego, paraRestaurar: false)) return;

                ContentDialog updateDialog = new ContentDialog
                {
                    Title = ResourceHelper.GetString("ConfirmUpdateTitle"),
                    Content = ResourceHelper.GetString("ConfirmUpdateMsg", juego.DisplayName),
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
                    SuscribirProgreso();

                    try
                    {
                        // 🔥 PASAR EL TOKEN
                        var resultado = await App.LibraryService.ActualizarBackup(juego, juego.ActiveVersion, _cts.Token);
                        await MostrarAlerta(ResourceHelper.GetString("AlertSuccess"),
                            resultado.YaAlDia
                                ? ResourceHelper.GetString("AlertUpdateNoChanges")
                                : ResourceHelper.GetString("AlertUpdateIncremental", resultado.Subidos, resultado.SinCambios, resultado.Eliminados));
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
                if (juego.NeedsRelink && !await PedirCarpetaEnEstePc(juego, paraRestaurar: true)) return;

                // ¿Las partidas de este PC son más nuevas que el backup? Restaurar perdería ese progreso
                var version = juego.ActiveVersion;
                DateTime? ultimaLocal = SaveTimestamps.UltimaModificacion(juego.RutaEnEstePc);
                bool localMasReciente = version != null && SaveTimestamps.LocalEsMasReciente(ultimaLocal, version.LastBackup);

                ContentDialog restoreDialog = new ContentDialog
                {
                    Title = localMasReciente
                        ? ResourceHelper.GetString("RestoreNewerTitle")
                        : $"{ResourceHelper.GetString("ConfirmRestoreTitle")} — {juego.DisplayName}",
                    Content = localMasReciente
                        ? ResourceHelper.GetString("RestoreNewerMsg",
                            ultimaLocal.Value.ToLocalTime().ToString("g"),
                            juego.DisplayName,
                            version.LastBackup.ToLocalTime().ToString("g"))
                        : ResourceHelper.GetString("ConfirmRestoreMsg", juego.RutaEnEstePc),
                    PrimaryButtonText = localMasReciente
                        ? ResourceHelper.GetString("RestoreNewerBtn")
                        : ResourceHelper.GetString("ConfirmRestoreBtn"),
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
                    SuscribirProgreso();

                    try
                    {
                        // 🔥 PASAR EL TOKEN
                        await App.LibraryService.RestaurarJuego(juego, juego.ActiveVersion, _cts.Token);
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
                var version = juego.ActiveVersion;
                bool soloEstaVersion = juego.HasMultipleVersions;

                ContentDialog deleteDialog = new ContentDialog
                {
                    Title = ResourceHelper.GetString("ConfirmDeleteTitle", juego.DisplayName),
                    Content = soloEstaVersion
                        ? ResourceHelper.GetString("ConfirmDeleteVersionMsg", version.Label)
                        : ResourceHelper.GetString("ConfirmDeleteMsg"),
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
                        Text = juego.DisplayName,
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
                        SuscribirProgreso();

                        try
                        {
                            // 🔥 ELIMINAR DEL SERVICIO (Drive + JSON local)
                            // Solo la versión activa; si era la única se elimina el juego
                            bool juegoEliminado = await App.LibraryService.EliminarVersion(juego, version);

                            // 🔥 ASEGURAR ACTUALIZACIÓN EN EL HILO DE UI
                            await RecargarLista();

                            // Pequeña pausa para que la UI se actualice antes de mostrar la alerta
                            await Task.Delay(100);

                            await MostrarAlerta(
                                ResourceHelper.GetString("AlertSuccess"),
                                juegoEliminado
                                    ? ResourceHelper.GetString("AlertDeleteSuccess")
                                    : ResourceHelper.GetString("AlertVersionDeleted", version.Label)
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

        // ===================== VERSIONES =====================

        private async void CmbVersion_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Ignorar la selección inicial al crear la tarjeta
            if (e.RemovedItems.Count == 0) return;
            if (sender is not ComboBox cmb || cmb.Tag is not GameConfig juego) return;
            if (cmb.SelectedItem is not GameVersion elegida || elegida.Id == juego.ActiveVersionId) return;

            if (_isBusy)
            {
                // No cambiar de versión a mitad de una subida o descarga
                cmb.SelectedItem = juego.ActiveVersion;
                await MostrarAlerta(ResourceHelper.GetString("AlertWarning"), ResourceHelper.GetString("MsgBusyVersion"));
                return;
            }

            try
            {
                await App.LibraryService.CambiarVersionActiva(juego, elegida);
                await VersionDialogs.MostrarAvisoCambioAsync(this.XamlRoot, juego.DisplayName);
            }
            catch (Exception ex)
            {
                await MostrarAlerta(ResourceHelper.GetString("AlertError"), ex.Message);
            }

            await RecargarLista();
        }

        private async void BtnNewVersion_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not GameConfig juego) return;

            if (_isBusy)
            {
                await MostrarAlerta(ResourceHelper.GetString("AlertWarning"), ResourceHelper.GetString("MsgBusyVersion"));
                return;
            }

            if (juego.NeedsRelink && !await PedirCarpetaEnEstePc(juego, paraRestaurar: false)) return;

            var dialog = new NewVersionDialog(juego) { XamlRoot = this.XamlRoot };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            SetBusyState(true, "MsgUploading");
            SuscribirProgreso();

            try
            {
                var nueva = await App.LibraryService.CrearNuevaVersion(juego, dialog.Description, _cts.Token);
                await RecargarLista();
                await MostrarAlerta(ResourceHelper.GetString("AlertSuccess"),
                    ResourceHelper.GetString("AlertVersionCreated", $"{juego.Name} {nueva.Label}"));
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

        private void BtnHistory_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is GameConfig juego && App.MainWindow is MainWindow main)
            {
                main.NavegarExterna(typeof(VersionHistoryPage), "Library", juego.Id);
            }
        }

        // ===================== CARPETA EN ESTE PC =====================

        private async void BtnRelink_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not GameConfig juego) return;
            if (await ElegirCarpetaEnEstePc(juego)) await RecargarLista();
        }

        /// <summary>
        /// La carpeta de partidas no existe en este PC: explicarlo y pedir que se elija.
        /// Para restaurar puede ser una carpeta vacía (ahí se descargará el backup).
        /// </summary>
        private async Task<bool> PedirCarpetaEnEstePc(GameConfig juego, bool paraRestaurar)
        {
            var dialogo = new ContentDialog
            {
                Title = ResourceHelper.GetString("RelinkTitle"),
                Content = ResourceHelper.GetString(paraRestaurar ? "RelinkMsgRestore" : "RelinkMsg", juego.RutaEnEstePc),
                PrimaryButtonText = ResourceHelper.GetString("RelinkChooseBtn"),
                CloseButtonText = ResourceHelper.GetString("ConfirmCancelBtn"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot,
                RequestedTheme = SettingsService.Current.DarkMode ? ElementTheme.Dark : ElementTheme.Light,
                PrimaryButtonStyle = GetStandardButtonStyle(),
                CloseButtonStyle = GetStandardButtonStyle()
            };

            if (await dialogo.ShowAsync() != ContentDialogResult.Primary) return false;
            bool elegida = await ElegirCarpetaEnEstePc(juego);
            if (elegida) await RecargarLista();
            return elegida;
        }

        /// <summary>Abre el selector de carpetas y guarda la elegida solo para este PC.</summary>
        private async Task<bool> ElegirCarpetaEnEstePc(GameConfig juego)
        {
            if (App.MainWindow == null) return false;

            var picker = new Windows.Storage.Pickers.FolderPicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary
            };
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));

            var carpeta = await picker.PickSingleFolderAsync();
            if (carpeta == null) return false;

            try
            {
                await App.LibraryService.CambiarRutaEnEstePc(juego, carpeta.Path);
                return true;
            }
            catch (Exception ex)
            {
                await MostrarAlerta(ResourceHelper.GetString("AlertError"), ex.Message);
                return false;
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