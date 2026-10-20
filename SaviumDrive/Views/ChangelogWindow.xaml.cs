// Views/ChangelogWindow.xaml.cs
// Novedades de la versión instalada: se muestra una vez, al abrir la app tras actualizar.
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using SaviumDrive.Helpers;
using SaviumDrive.Services;
using System;

namespace SaviumDrive.Views
{
    /// <summary>Ventana de novedades con las notas de la versión (Markdown de GitHub Releases).</summary>
    public sealed partial class ChangelogWindow : Window
    {
        private readonly string _version;

        public ChangelogWindow(string version, string changelog)
        {
            this.InitializeComponent();
            _version = version;

            Raiz.RequestedTheme = SettingsService.Current.DarkMode ? ElementTheme.Dark : ElementTheme.Light;
            ConfigurarVentana();

            TxtVersionInfo.Text = string.Format(ResourceHelper.GetString("ChangelogVersionInfo"), version);
            Mostrar(changelog);
        }

        private void ConfigurarVentana()
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            Title = ResourceHelper.GetString("ChangelogTitle/Text") + " - Savium Drive";

            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var appWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd));
            if (appWindow == null) return;

            // Se puede agrandar para leer notas largas, pero no hacerla tan pequeña que no se lea
            var tamano = WindowSizing.AjustarYCentrar(appWindow, hWnd, 640, 720);
            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsMinimizable = false;
                presenter.PreferredMinimumWidth = Math.Min(tamano.Width, (int)(480 * WindowSizing.Escala(hWnd)));
                presenter.PreferredMinimumHeight = Math.Min(tamano.Height, (int)(420 * WindowSizing.Escala(hWnd)));
            }
        }

        // ===================== DE MARKDOWN A TEXTO CON FORMATO =====================

        private static readonly Brush Acento = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x00, 0x8E, 0xFF));

        private void Mostrar(string markdown)
        {
            ChangelogContent.Blocks.Clear();
            var bloques = ChangelogMarkdown.Analizar(markdown);
            if (bloques.Count == 0)
            {
                var vacio = new Paragraph();
                vacio.Inlines.Add(new Run { Text = ResourceHelper.GetString("ChangelogNoInfo") });
                ChangelogContent.Blocks.Add(vacio);
                return;
            }

            bool primero = true;
            foreach (var bloque in bloques)
            {
                var p = new Paragraph();
                switch (bloque.Tipo)
                {
                    case TipoBloqueMd.Titulo:
                        p.FontWeight = FontWeights.SemiBold;
                        p.FontSize = bloque.Nivel <= 2 ? 18 : 15;
                        p.Margin = new Thickness(0, primero ? 0 : 16, 0, 6);
                        AgregarTramos(p, bloque);
                        break;

                    case TipoBloqueMd.Vineta:
                    case TipoBloqueMd.Numerado:
                        // Sangría colgante: el texto largo se alinea con el de la primera línea, no con la viñeta
                        double sangria = 18 + bloque.Nivel * 20;
                        p.Margin = new Thickness(sangria, 3, 0, 3);
                        p.TextIndent = -16;
                        string marca = bloque.Tipo == TipoBloqueMd.Numerado ? bloque.Numero : (bloque.Nivel == 0 ? "•" : "◦");
                        p.Inlines.Add(new Run { Text = marca + "  ", Foreground = Acento, FontWeight = FontWeights.Bold });
                        AgregarTramos(p, bloque);
                        break;

                    case TipoBloqueMd.Separador:
                        p.Margin = new Thickness(0, 6, 0, 6);
                        p.Inlines.Add(new Run { Text = "———", Foreground = Acento });
                        break;

                    default:
                        p.Margin = new Thickness(0, 4, 0, 8);
                        AgregarTramos(p, bloque);
                        break;
                }
                ChangelogContent.Blocks.Add(p);
                primero = false;
            }
        }

        private static void AgregarTramos(Paragraph p, BloqueMd bloque)
        {
            foreach (var t in bloque.Tramos)
            {
                switch (t.Tipo)
                {
                    case TipoTramoMd.Negrita: p.Inlines.Add(new Run { Text = t.Texto, FontWeight = FontWeights.Bold }); break;
                    case TipoTramoMd.Cursiva: p.Inlines.Add(new Run { Text = t.Texto, FontStyle = Windows.UI.Text.FontStyle.Italic }); break;
                    case TipoTramoMd.Codigo: p.Inlines.Add(new Run { Text = t.Texto, FontFamily = new FontFamily("Consolas"), Foreground = Acento }); break;
                    case TipoTramoMd.Salto: p.Inlines.Add(new LineBreak()); break;
                    case TipoTramoMd.Enlace when Uri.TryCreate(t.Url, UriKind.Absolute, out var uri):
                        var enlace = new Hyperlink { NavigateUri = uri, Foreground = Acento };
                        enlace.Inlines.Add(new Run { Text = t.Texto });
                        p.Inlines.Add(enlace);
                        break;
                    default: p.Inlines.Add(new Run { Text = t.Texto }); break;
                }
            }
        }

        // ===================== BOTONES =====================

        private async void BtnGitHub_Click(object sender, RoutedEventArgs e)
        {
            try { await Windows.System.Launcher.LaunchUriAsync(new Uri(UpdateService.PaginaDeVersion(_version))); }
            catch { }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
