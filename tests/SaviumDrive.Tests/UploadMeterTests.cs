using SaviumDrive.Services;
using System;
using System.Globalization;
using Xunit;

namespace SaviumDrive.Tests
{
    public class UploadMeterTests
    {
        private DateTime _ahora = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
        private UploadMeter Medidor(long total) => new UploadMeter(total, () => _ahora);

        [Fact]
        public void Porcentaje_CuentaLoEnviadoDelArchivoEnCurso()
        {
            var m = Medidor(1000);
            m.ProgresoArchivo(250);
            Assert.Equal(25, m.Porcentaje);

            m.ArchivoTerminado(500);
            m.ProgresoArchivo(100);
            Assert.Equal(600, m.Enviados);
            Assert.Equal(60, m.Porcentaje);
        }

        [Fact]
        public void Velocidad_SaleDeLosBytesPorSegundo()
        {
            var m = Medidor(10_000_000);
            _ahora = _ahora.AddSeconds(1);
            m.ProgresoArchivo(2_000_000);
            Assert.Equal(2_000_000, m.BytesPorSegundo, 0);
            Assert.Equal(TimeSpan.FromSeconds(4), m.Restante);
        }

        [Fact]
        public void Velocidad_SeSuavizaYNoSeMideConMuestrasMuyCortas()
        {
            var m = Medidor(100_000_000);
            _ahora = _ahora.AddSeconds(1);
            m.ProgresoArchivo(1_000_000);           // 1 MB/s

            _ahora = _ahora.AddMilliseconds(100);
            m.ProgresoArchivo(1_900_000);           // muestra demasiado corta: no cambia
            Assert.Equal(1_000_000, m.BytesPorSegundo, 0);

            _ahora = _ahora.AddMilliseconds(900);
            m.ProgresoArchivo(4_000_000);           // 3 MB/s en esta muestra; suavizado: 0.7·1 + 0.3·3 = 1.6 MB/s
            Assert.Equal(1_600_000, m.BytesPorSegundo, 0);
        }

        [Fact]
        public void ArchivosSinCambios_AvanzanElProgresoPeroNoLaVelocidad()
        {
            var m = Medidor(1000);
            m.Saltar(900);
            _ahora = _ahora.AddSeconds(1);
            m.ProgresoArchivo(50);

            Assert.Equal(95, m.Porcentaje);
            Assert.Equal(50, m.BytesPorSegundo, 0); // no 950
        }

        [Fact]
        public void NuncaPasaDe100NiDelTotal()
        {
            var m = Medidor(100);
            m.ArchivoTerminado(150);
            Assert.Equal(100, m.Porcentaje);
            Assert.Equal(100, m.Enviados);
            Assert.Null(m.Restante);
            Assert.Equal(0, Medidor(0).Porcentaje);
        }

        [Fact]
        public void DebeAvisar_LimitaLosAvisos()
        {
            var m = Medidor(100);
            Assert.True(m.DebeAvisar());
            _ahora = _ahora.AddMilliseconds(50);
            Assert.False(m.DebeAvisar());
            Assert.True(m.DebeAvisar(forzar: true));
            _ahora = _ahora.AddMilliseconds(200);
            Assert.True(m.DebeAvisar());
        }

        [Theory]
        [InlineData(512, "512 B")]
        [InlineData(1536, "1.5 KB")]
        [InlineData(5 * 1024 * 1024, "5.0 MB")]
        public void Bytes_FormatoLegible(long bytes, string esperado)
        {
            var cultura = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            try { Assert.Equal(esperado, UploadMeter.Bytes(bytes)); }
            finally { CultureInfo.CurrentCulture = cultura; }
        }

        [Theory]
        [InlineData(0.4, "1 s")]
        [InlineData(45, "45 s")]
        [InlineData(200, "3 min 20 s")]
        [InlineData(180, "3 min")]
        [InlineData(3900, "1 h 5 min")]
        public void Tiempo_FormatoLegible(double segundos, string esperado)
        {
            Assert.Equal(esperado, UploadMeter.Tiempo(TimeSpan.FromSeconds(segundos)));
        }
    }
}
