// Services/UploadMeter.cs
// Progreso real de una subida (bytes enviados, velocidad y tiempo restante), sin WinUI ni Drive;
// se prueba desde SaviumDrive.Tests.
using System;
using System.Globalization;

namespace SaviumDrive.Services
{
    public sealed class UploadMeter
    {
        private static readonly TimeSpan IntervaloVelocidad = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan IntervaloAviso = TimeSpan.FromMilliseconds(150);

        private readonly Func<DateTime> _reloj;
        private long _terminados;      // bytes de archivos ya terminados (subidos o copiados dentro de Drive)
        private long _actual;          // bytes enviados del archivo en curso
        private long _enviadosReales;  // solo lo que pasó por la red: de aquí sale la velocidad
        private long _enviadosEnMuestra;
        private DateTime _inicioMuestra;
        private DateTime _ultimoAviso = DateTime.MinValue;

        /// <param name="total">Bytes de toda la subida.</param>
        /// <param name="reloj">Hora actual; las pruebas pasan una hora simulada.</param>
        public UploadMeter(long total, Func<DateTime> reloj = null)
        {
            Total = Math.Max(0, total);
            _reloj = reloj ?? (() => DateTime.UtcNow);
            _inicioMuestra = _reloj();
        }

        public long Total { get; }
        public long Enviados => Math.Min(Total, _terminados + _actual);

        /// <summary>0 a 100.</summary>
        public int Porcentaje => Total > 0 ? (int)Math.Min(100, Enviados * 100 / Total) : 0;

        /// <summary>Velocidad suavizada en bytes por segundo (0 hasta tener la primera muestra).</summary>
        public double BytesPorSegundo { get; private set; }

        public TimeSpan? Restante =>
            BytesPorSegundo > 0 && Total > Enviados ? TimeSpan.FromSeconds((Total - Enviados) / BytesPorSegundo) : (TimeSpan?)null;

        /// <summary>Bytes enviados del archivo que se está subiendo (lo informa la subida de Google).</summary>
        public void ProgresoArchivo(long bytesEnviados)
        {
            bytesEnviados = Math.Max(0, bytesEnviados);
            if (bytesEnviados > _actual) _enviadosReales += bytesEnviados - _actual;
            _actual = bytesEnviados;
            MedirVelocidad();
        }

        /// <summary>El archivo en curso terminó de subirse.</summary>
        public void ArchivoTerminado(long peso)
        {
            if (peso > _actual) _enviadosReales += peso - _actual;
            _terminados += Math.Max(0, peso);
            _actual = 0;
            MedirVelocidad();
        }

        /// <summary>Archivo que no hizo falta subir (sin cambios, copiado dentro de Drive): avanza el progreso, no la velocidad.</summary>
        public void Saltar(long peso)
        {
            _terminados += Math.Max(0, peso);
            _actual = 0;
        }

        private void MedirVelocidad()
        {
            var ahora = _reloj();
            double segundos = (ahora - _inicioMuestra).TotalSeconds;
            if (segundos < IntervaloVelocidad.TotalSeconds) return;

            double instantanea = (_enviadosReales - _enviadosEnMuestra) / segundos;
            // Suavizado: la velocidad no salta con cada fragmento
            BytesPorSegundo = BytesPorSegundo <= 0 ? instantanea : BytesPorSegundo * 0.7 + instantanea * 0.3;
            _enviadosEnMuestra = _enviadosReales;
            _inicioMuestra = ahora;
        }

        /// <summary>True como mucho unas 7 veces por segundo (para no inundar la interfaz de avisos).</summary>
        public bool DebeAvisar(bool forzar = false)
        {
            var ahora = _reloj();
            if (!forzar && ahora - _ultimoAviso < IntervaloAviso) return false;
            _ultimoAviso = ahora;
            return true;
        }

        // ===================== TEXTOS =====================

        /// <summary>Tamaño legible: "512 B", "1,5 KB", "12,3 MB" (con el separador decimal del sistema).</summary>
        public static string Bytes(long bytes)
        {
            string[] sufijos = { "B", "KB", "MB", "GB", "TB" };
            double n = Math.Max(0, bytes);
            int i = 0;
            while (n >= 1024 && i < sufijos.Length - 1) { n /= 1024; i++; }
            return i == 0 ? $"{n:0} {sufijos[i]}" : string.Format(CultureInfo.CurrentCulture, "{0:0.0} {1}", n, sufijos[i]);
        }

        /// <summary>"45 s", "3 min 20 s", "1 h 5 min".</summary>
        public static string Tiempo(TimeSpan t)
        {
            if (t.TotalSeconds < 60) return $"{Math.Max(1, (int)Math.Ceiling(t.TotalSeconds))} s";
            if (t.TotalMinutes < 60) return t.Seconds > 0 ? $"{(int)t.TotalMinutes} min {t.Seconds} s" : $"{(int)t.TotalMinutes} min";
            return $"{(int)t.TotalHours} h {t.Minutes} min";
        }
    }
}
