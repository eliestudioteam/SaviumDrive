using SaviumDrive.Services;
using System;
using System.IO;
using Xunit;

namespace SaviumDrive.Tests
{
    public class SaveTimestampsTests : IDisposable
    {
        private readonly string _dir;

        public SaveTimestampsTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "SaviumDriveTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private string Archivo(string relativa, DateTime fechaUtc)
        {
            string ruta = Path.Combine(_dir, relativa);
            Directory.CreateDirectory(Path.GetDirectoryName(ruta));
            File.WriteAllText(ruta, "x");
            File.SetLastWriteTimeUtc(ruta, fechaUtc);
            return ruta;
        }

        private static readonly DateTime Backup = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

        // --- Última modificación local ---

        [Fact]
        public void UltimaModificacion_DevuelveElArchivoMasRecienteIncluidoEnSubcarpetas()
        {
            Archivo("save1.dat", Backup.AddDays(-3));
            Archivo(@"perfil\slot2\save2.dat", Backup.AddHours(5));
            Archivo("config.ini", Backup.AddDays(-1));

            Assert.Equal(Backup.AddHours(5), SaveTimestamps.UltimaModificacion(_dir));
        }

        [Fact]
        public void UltimaModificacion_CarpetaVaciaOInexistente_DevuelveNull()
        {
            Assert.Null(SaveTimestamps.UltimaModificacion(_dir));
            Assert.Null(SaveTimestamps.UltimaModificacion(Path.Combine(_dir, "no-existe")));
            Assert.Null(SaveTimestamps.UltimaModificacion(null));
        }

        // --- ¿Avisar antes de restaurar? ---

        [Fact]
        public void LocalEsMasReciente_PartidaJugadaDespuesDelBackup_Avisa()
        {
            Assert.True(SaveTimestamps.LocalEsMasReciente(Backup.AddHours(2), Backup));
        }

        [Fact]
        public void LocalEsMasReciente_PartidaAnteriorAlBackup_NoAvisa()
        {
            Assert.False(SaveTimestamps.LocalEsMasReciente(Backup.AddHours(-2), Backup));
        }

        [Fact]
        public void LocalEsMasReciente_DiferenciaDentroDeLaTolerancia_NoAvisa()
        {
            // Guardar y modificar casi a la vez (relojes o redondeo de fechas) no debe avisar
            Assert.False(SaveTimestamps.LocalEsMasReciente(Backup.AddSeconds(90), Backup));
            Assert.True(SaveTimestamps.LocalEsMasReciente(Backup.AddMinutes(3), Backup));
        }

        [Fact]
        public void LocalEsMasReciente_SinDatos_NoAvisa()
        {
            Assert.False(SaveTimestamps.LocalEsMasReciente(null, Backup));
            Assert.False(SaveTimestamps.LocalEsMasReciente(Backup.AddDays(1), default));
        }

        [Fact]
        public void LocalEsMasReciente_ComparaBienFechasLocalesYUtc()
        {
            // La fecha local equivale a 1 hora después del backup en UTC
            DateTime local = Backup.AddHours(1).ToLocalTime();
            Assert.Equal(DateTimeKind.Local, local.Kind);

            Assert.True(SaveTimestamps.LocalEsMasReciente(local, Backup));
            Assert.False(SaveTimestamps.LocalEsMasReciente(Backup.AddHours(-1).ToLocalTime(), Backup));
        }
    }
}
