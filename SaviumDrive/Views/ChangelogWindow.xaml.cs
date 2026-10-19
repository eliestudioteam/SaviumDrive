// Views/ChangelogWindow.xaml.cs
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using SaviumDrive.Services;
using System;
using System.Text.RegularExpressions;
using SaviumDrive.Helpers;

namespace SaviumDrive.Views
{
    public sealed partial class ChangelogWindow : Window
    {

        public ChangelogWindow(string version, string changelog)
        {
            this.InitializeComponent();

            // Configurar ventana
            SetupWindow();

            // Aplicar tema
            if (this.Content is FrameworkElement root)
            {
                root.RequestedTheme = SettingsService.Current.DarkMode ? ElementTheme.Dark : ElementTheme.Light;
            }

            // Mostrar información
            TxtVersionInfo.Text = string.Format(
                ResourceHelper.GetString("ChangelogVersionInfo"),
                version
            );

            // Parsear y mostrar changelog
            ParseAndDisplayChangelog(changelog);
        }

        private void SetupWindow()
        {
            // Obtener AppWindow para personalización
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WindowId wndId = Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = AppWindow.GetFromWindowId(wndId);

            if (appWindow != null)
            {
                // Tamaño de ventana
                appWindow.Resize(new Windows.Graphics.SizeInt32(600, 700));

                // Centrar ventana
                var displayArea = DisplayArea.GetFromWindowId(wndId, DisplayAreaFallback.Primary);
                if (displayArea != null)
                {
                    var centerX = (displayArea.WorkArea.Width - 600) / 2;
                    var centerY = (displayArea.WorkArea.Height - 700) / 2;
                    appWindow.Move(new Windows.Graphics.PointInt32(centerX, centerY));
                }
            }

            this.Title = "What's New - Savium Drive";
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(null); // Sin barra de título personalizada
        }

        private void ParseAndDisplayChangelog(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown))
            {
                markdown = ResourceHelper.GetString("ChangelogNoInfo");
            }

            ChangelogContent.Blocks.Clear();

            // Parsear Markdown simple a RichTextBlock
            var lines = markdown.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            Paragraph currentParagraph = new Paragraph();

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (currentParagraph.Inlines.Count > 0)
                    {
                        ChangelogContent.Blocks.Add(currentParagraph);
                        currentParagraph = new Paragraph();
                    }
                    continue;
                }

                // Detectar títulos (## Titulo)
                if (line.StartsWith("## "))
                {
                    if (currentParagraph.Inlines.Count > 0)
                    {
                        ChangelogContent.Blocks.Add(currentParagraph);
                        currentParagraph = new Paragraph();
                    }

                    var heading = new Paragraph();
                    var run = new Run { Text = line.Substring(3).Trim() };
                    run.FontSize = 18;
                    run.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                    run.Foreground = new SolidColorBrush(
                        SettingsService.Current.DarkMode ? Colors.White : Colors.Black
                    );
                    heading.Inlines.Add(run);
                    heading.Margin = new Thickness(0, 15, 0, 8);
                    ChangelogContent.Blocks.Add(heading);
                    continue;
                }

                // Detectar sub-títulos (### Subtitulo)
                if (line.StartsWith("### "))
                {
                    if (currentParagraph.Inlines.Count > 0)
                    {
                        ChangelogContent.Blocks.Add(currentParagraph);
                        currentParagraph = new Paragraph();
                    }

                    var subHeading = new Paragraph();
                    var run = new Run { Text = line.Substring(4).Trim() };
                    run.FontSize = 15;
                    run.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                    run.Foreground = new SolidColorBrush(
                        SettingsService.Current.DarkMode ? Colors.LightGray : Colors.DarkGray
                    );
                    subHeading.Inlines.Add(run);
                    subHeading.Margin = new Thickness(0, 10, 0, 5);
                    ChangelogContent.Blocks.Add(subHeading);
                    continue;
                }

                // Detectar listas (- Item o * Item)
                if (line.TrimStart().StartsWith("- ") || line.TrimStart().StartsWith("* "))
                {
                    var bulletParagraph = new Paragraph();
                    bulletParagraph.Margin = new Thickness(20, 4, 0, 4);

                    var bullet = new Run { Text = "• " };
                    bullet.Foreground = new SolidColorBrush(
                        Windows.UI.Color.FromArgb(255, 0, 229, 255) // AccentColor
                    );
                    bulletParagraph.Inlines.Add(bullet);

                    var text = line.TrimStart().Substring(2).Trim();

                    // Detectar texto en negrita (**texto**)
                    text = ProcessBoldText(text, bulletParagraph);

                    ChangelogContent.Blocks.Add(bulletParagraph);
                    continue;
                }

                // Texto normal
                var normalRun = new Run { Text = line };
                currentParagraph.Inlines.Add(normalRun);
                currentParagraph.Inlines.Add(new LineBreak());
            }

            if (currentParagraph.Inlines.Count > 0)
            {
                ChangelogContent.Blocks.Add(currentParagraph);
            }
        }

        private string ProcessBoldText(string text, Paragraph paragraph)
        {
            // Regex para detectar **texto en negrita**
            var regex = new Regex(@"\*\*(.*?)\*\*");
            int lastIndex = 0;

            foreach (Match match in regex.Matches(text))
            {
                // Texto antes del match
                if (match.Index > lastIndex)
                {
                    paragraph.Inlines.Add(new Run { Text = text.Substring(lastIndex, match.Index - lastIndex) });
                }

                // Texto en negrita
                var boldRun = new Run { Text = match.Groups[1].Value };
                boldRun.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
                paragraph.Inlines.Add(boldRun);

                lastIndex = match.Index + match.Length;
            }

            // Texto restante
            if (lastIndex < text.Length)
            {
                paragraph.Inlines.Add(new Run { Text = text.Substring(lastIndex) });
            }

            return string.Empty; // Ya procesado
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}