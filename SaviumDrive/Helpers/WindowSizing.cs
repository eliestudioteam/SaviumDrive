// Helpers/WindowSizing.cs
// Tamaño y posición de las ventanas secundarias (aviso del detector, novedades).
using Microsoft.UI.Windowing;
using System;
using System.Runtime.InteropServices;

namespace SaviumDrive.Helpers
{
    public static class WindowSizing
    {
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        /// <summary>Escala de la pantalla de la ventana (1 = 100 %, 1.5 = 150 %...).</summary>
        public static double Escala(IntPtr hwnd)
        {
            uint dpi = hwnd != IntPtr.Zero ? GetDpiForWindow(hwnd) : 0;
            return dpi > 0 ? dpi / 96.0 : 1.0;
        }

        /// <summary>
        /// Da a la ventana un tamaño en píxeles lógicos (se ve igual con cualquier escala de Windows),
        /// sin pasar del área de trabajo, y la centra en la pantalla donde está (no siempre en la principal).
        /// Devuelve el tamaño real en píxeles.
        /// </summary>
        public static Windows.Graphics.SizeInt32 AjustarYCentrar(AppWindow ventana, IntPtr hwnd, int anchoLogico, int altoLogico)
        {
            double escala = Escala(hwnd);
            var area = DisplayArea.GetFromWindowId(ventana.Id, DisplayAreaFallback.Primary).WorkArea;

            int ancho = Math.Min((int)Math.Round(anchoLogico * escala), (int)(area.Width * 0.95));
            int alto = Math.Min((int)Math.Round(altoLogico * escala), (int)(area.Height * 0.92));
            var tamano = new Windows.Graphics.SizeInt32(ancho, alto);

            ventana.Resize(tamano);
            ventana.Move(new Windows.Graphics.PointInt32(area.X + (area.Width - ancho) / 2, area.Y + (area.Height - alto) / 2));
            return tamano;
        }
    }
}
