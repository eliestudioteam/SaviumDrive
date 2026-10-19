using SaviumDrive.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;

namespace SaviumDrive.Tests
{
    public class BackupDiffTests : IDisposable
    {
        private readonly string _dir;

        public BackupDiffTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "SaviumDriveTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private static Dictionary<string, HuellaArchivo> Huellas(params (string ruta, long tamano, string md5)[] archivos)
        {
            var d = new Dictionary<string, HuellaArchivo>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in archivos) d[a.ruta] = new HuellaArchivo(a.tamano, a.md5);
            return d;
        }

        // --- Comparación ---

        [Fact]
        public void Calcular_TodoIgual_YaAlDia()
        {
            var local = Huellas(("save.dat", 10, "aaa"), ("perfil/slot1.sav", 20, "bbb"));
            var remoto = Huellas(("save.dat", 10, "aaa"), ("perfil/slot1.sav", 20, "bbb"));

            var r = BackupDiff.Calcular(local, remoto);

            Assert.True(r.YaAlDia);
            Assert.Equal(2, r.SinCambios.Count);
        }

        [Fact]
        public void Calcular_DetectaModificadosNuevosYEliminados()
        {
            var local = Huellas(("igual.dat", 10, "aaa"), ("cambiado.dat", 10, "nuevo-md5"), ("nuevo.dat", 5, "ccc"));
            var remoto = Huellas(("igual.dat", 10, "aaa"), ("cambiado.dat", 10, "viejo-md5"), ("borrado.dat", 7, "ddd"));

            var r = BackupDiff.Calcular(local, remoto);

            Assert.False(r.YaAlDia);
            Assert.Equal(new[] { "igual.dat" }, r.SinCambios);
            Assert.Equal(new[] { "cambiado.dat" }, r.Modificados);
            Assert.Equal(new[] { "nuevo.dat" }, r.Nuevos);
            Assert.Equal(new[] { "borrado.dat" }, r.Eliminados);
        }

        [Fact]
        public void Calcular_MismoMd5PeroDistintoTamano_EsModificado()
        {
            var r = BackupDiff.Calcular(Huellas(("a.dat", 11, "aaa")), Huellas(("a.dat", 10, "aaa")));
            Assert.Equal(new[] { "a.dat" }, r.Modificados);
        }

        [Fact]
        public void Calcular_SinMd5EnDrive_SeVuelveASubir()
        {
            // Sin MD5 no se puede saber si cambió: más seguro subirlo de nuevo
            var r = BackupDiff.Calcular(Huellas(("a.dat", 10, "aaa")), Huellas(("a.dat", 10, null)));
            Assert.Equal(new[] { "a.dat" }, r.Modificados);
        }

        [Fact]
        public void Calcular_IgnoraMayusculasYSeparadores()
        {
            // Windows no distingue mayúsculas; Drive usa '/' y el PC '\'
            var local = Huellas((@"Perfil\Slot1.SAV", 20, "BBB"));
            var remoto = Huellas(("perfil/slot1.sav", 20, "bbb"));

            Assert.True(BackupDiff.Calcular(local, remoto).YaAlDia);
        }

        [Fact]
        public void Calcular_BackupVacio_TodoEsNuevo()
        {
            var r = BackupDiff.Calcular(Huellas(("a.dat", 1, "x"), ("b.dat", 2, "y")), new Dictionary<string, HuellaArchivo>());
            Assert.Equal(2, r.Nuevos.Count);
            Assert.False(r.YaAlDia);
        }

        // --- MD5 y lectura local ---

        [Fact]
        public void Md5Hex_IgualQueElFormatoDeDrive()
        {
            string ruta = Path.Combine(_dir, "abc.txt");
            File.WriteAllBytes(ruta, Encoding.ASCII.GetBytes("abc"));

            // MD5 conocido de "abc", en minúsculas como md5Checksum de Drive
            Assert.Equal("900150983cd24fb0d6963f7d28e17f72", BackupDiff.Md5Hex(ruta));
        }

        [Fact]
        public void LeerCarpetaLocal_IncluyeSubcarpetasConRutasNormalizadas()
        {
            Directory.CreateDirectory(Path.Combine(_dir, "perfil", "slot1"));
            File.WriteAllText(Path.Combine(_dir, "config.ini"), "abc");
            File.WriteAllText(Path.Combine(_dir, "perfil", "slot1", "save.dat"), "12345");

            var huellas = BackupDiff.LeerCarpetaLocal(_dir);

            Assert.Equal(2, huellas.Count);
            Assert.Equal(5, huellas["perfil/slot1/save.dat"].Tamano);
            Assert.Equal("900150983cd24fb0d6963f7d28e17f72", huellas["config.ini"].Md5);
        }

        [Fact]
        public void LeerCarpetaLocal_ArchivoAbiertoPorElJuego_SePuedeLeer()
        {
            string ruta = Path.Combine(_dir, "save.dat");
            File.WriteAllText(ruta, "abc");

            using (new FileStream(ruta, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
            {
                Assert.Single(BackupDiff.LeerCarpetaLocal(_dir));
            }
        }
    }
}
