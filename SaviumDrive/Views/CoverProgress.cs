// Views/CoverProgress.cs
// Portada de la tarjeta que muestra el progreso como Epic Games: en gris y coloreándose de izquierda a derecha.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace SaviumDrive.Views
{
    public sealed class CoverProgress : Grid
    {
        // Ancho al que se decodifica la versión gris (2x para pantallas con escala)
        private const int AnchoGris = 320;

        private readonly Image _gris = new Image { Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed };
        private readonly Image _color = new Image { Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Top };
        private readonly RectangleGeometry _recorte = new RectangleGeometry();
        private Uri _uriGris; // portada de la que ya se generó (o se está generando) la versión gris

        public static readonly DependencyProperty CoverUriProperty = DependencyProperty.Register(
            nameof(CoverUri), typeof(Uri), typeof(CoverProgress), new PropertyMetadata(null, (d, _) => ((CoverProgress)d).PortadaCambiada()));

        /// <summary>0 a 100; -1 = sin operación (portada normal).</summary>
        public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
            nameof(Progress), typeof(int), typeof(CoverProgress), new PropertyMetadata(-1, (d, _) => ((CoverProgress)d).ProgresoCambiado()));

        public Uri CoverUri { get => (Uri)GetValue(CoverUriProperty); set => SetValue(CoverUriProperty, value); }
        public int Progress { get => (int)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }

        public CoverProgress()
        {
            Children.Add(_gris);
            Children.Add(_color);
            SizeChanged += (_, __) => ActualizarRecorte();
        }

        private void PortadaCambiada()
        {
            // Decodificar a tamaño de tarjeta: la portada original es 600x900 (~2 MB en memoria)
            _color.Source = CoverUri == null ? null : new BitmapImage(CoverUri) { DecodePixelWidth = 160, DecodePixelType = DecodePixelType.Logical };
            _uriGris = null;
            _gris.Source = null;
            ProgresoCambiado();
        }

        private void ProgresoCambiado()
        {
            if (Progress < 0)
            {
                // Sin operación: portada normal y sin la copia gris en memoria
                _color.Clip = null;
                _gris.Visibility = Visibility.Collapsed;
                _gris.Source = null;
                _uriGris = null;
                return;
            }

            _gris.Visibility = Visibility.Visible;
            if (CoverUri != null && _uriGris != CoverUri) _ = CargarGris(CoverUri);
            ActualizarRecorte();
        }

        private void ActualizarRecorte()
        {
            if (Progress < 0) return;
            double ancho = Math.Max(0, ActualWidth) * Math.Clamp(Progress, 0, 100) / 100.0;
            _recorte.Rect = new Rect(0, 0, ancho, Math.Max(0, ActualHeight));
            _color.Clip = _recorte;
        }

        /// <summary>Genera la portada en gris (y algo más oscura) a partir de la original.</summary>
        private async System.Threading.Tasks.Task CargarGris(Uri uri)
        {
            _uriGris = uri;
            try
            {
                using IRandomAccessStreamWithContentType stream = await RandomAccessStreamReference.CreateFromUri(uri).OpenReadAsync();
                var decoder = await BitmapDecoder.CreateAsync(stream);

                uint ancho = Math.Min((uint)AnchoGris, decoder.PixelWidth);
                uint alto = Math.Max(1, (uint)Math.Round(decoder.PixelHeight * (double)ancho / Math.Max(1, decoder.PixelWidth)));
                var datos = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                    new BitmapTransform { ScaledWidth = ancho, ScaledHeight = alto, InterpolationMode = BitmapInterpolationMode.Fant },
                    ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
                byte[] px = datos.DetachPixelData();

                for (int i = 0; i + 3 < px.Length; i += 4)
                {
                    // Luminancia y un poco más oscuro, como la portada "pendiente" de Epic
                    byte gris = (byte)((px[i + 2] * 0.299 + px[i + 1] * 0.587 + px[i] * 0.114) * 0.7);
                    px[i] = px[i + 1] = px[i + 2] = gris;
                }

                var bitmap = new WriteableBitmap((int)ancho, (int)alto);
                using (var salida = bitmap.PixelBuffer.AsStream()) salida.Write(px, 0, px.Length);
                bitmap.Invalidate();

                if (_uriGris == uri && Progress >= 0) { _gris.Opacity = 1; _gris.Source = bitmap; }
            }
            catch
            {
                // No se pudo leer la imagen (sin red, ruta local...): la misma portada muy apagada
                if (_uriGris == uri && Progress >= 0)
                {
                    _gris.Source = new BitmapImage(uri) { DecodePixelWidth = 160, DecodePixelType = DecodePixelType.Logical };
                    _gris.Opacity = 0.25;
                }
            }
        }
    }
}
