using Newtonsoft.Json;
using SaviumDrive.Models;
using SaviumDrive.Services;
using System;
using System.IO;
using System.IO.Compression;
using Xunit;

namespace SaviumDrive.Tests
{
    public class ZipBackupTests : IDisposable
    {
        private readonly string _raiz;
        private readonly string _origen;

        public ZipBackupTests()
        {
            _raiz = Path.Combine(Path.GetTempPath(), "SaviumDriveTests_" + Guid.NewGuid().ToString("N"));
            _origen = Path.Combine(_raiz, "Saves");
            Directory.CreateDirectory(_origen);
        }

        public void Dispose()
        {
            try { Directory.Delete(_raiz, true); } catch { }
        }

        private string Archivo(string relativa, string contenido, DateTime? fecha = null)
        {
            string ruta = Path.Combine(_origen, relativa);
            Directory.CreateDirectory(Path.GetDirectoryName(ruta));
            File.WriteAllText(ruta, contenido);
            if (fecha.HasValue) File.SetLastWriteTime(ruta, fecha.Value);
            return ruta;
        }

        // --- Cuándo comprimir ---

        [Fact]
        public void ConvieneComprimir_SoloConMuchosArchivos()
        {
            for (int i = 0; i < ZipBackup.UmbralArchivos - 1; i++) Archivo($"slot{i}.sav", "x");
            Assert.False(ZipBackup.ConvieneComprimir(_origen));

            Archivo(@"sub\ultimo.sav", "x");
            Assert.True(ZipBackup.ConvieneComprimir(_origen));
        }

        [Fact]
        public void NombreZip_UsaElNombreDeLaCarpeta()
        {
            Assert.Equal("Saves.zip", ZipBackup.NombreZip(_origen));
        }

        // --- Comprimir y extraer ---

        [Fact]
        public void ComprimirYExtraer_ConservaContenidoSubcarpetasYFechas()
        {
            var fecha = new DateTime(2026, 3, 14, 9, 30, 0);
            Archivo("config.ini", "volumen=5", fecha);
            Archivo(@"perfil\slot1\save.dat", "partida guardada", fecha.AddDays(1));
            Directory.CreateDirectory(Path.Combine(_origen, "capturas"));   // carpeta vacía

            string zip = Path.Combine(_raiz, "backup.zip");
            string destino = Path.Combine(_raiz, "restaurado");
            ZipBackup.Comprimir(_origen, zip);
            ZipBackup.Extraer(zip, destino);

            Assert.Equal("volumen=5", File.ReadAllText(Path.Combine(destino, "config.ini")));
            Assert.Equal("partida guardada", File.ReadAllText(Path.Combine(destino, "perfil", "slot1", "save.dat")));
            Assert.True(Directory.Exists(Path.Combine(destino, "capturas")));
            // El formato zip guarda la hora con precisión de 2 segundos
            Assert.True(Math.Abs((File.GetLastWriteTime(Path.Combine(destino, "config.ini")) - fecha).TotalSeconds) <= 2);
        }

        [Fact]
        public void Comprimir_NoIncluyeLaCarpetaRaiz()
        {
            Archivo("save.dat", "x");
            string zip = Path.Combine(_raiz, "backup.zip");

            ZipBackup.Comprimir(_origen, zip);

            using var archivo = ZipFile.OpenRead(zip);
            Assert.Equal("save.dat", Assert.Single(archivo.Entries).FullName);
        }

        [Fact]
        public void Comprimir_ArchivoAbiertoPorElJuego_SePuedeLeer()
        {
            string ruta = Archivo("save.dat", "x");
            string zip = Path.Combine(_raiz, "backup.zip");

            using (new FileStream(ruta, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
            {
                ZipBackup.Comprimir(_origen, zip);
            }

            Assert.True(File.Exists(zip));
        }

        [Fact]
        public void Extraer_RechazaRutasFueraDelDestino()
        {
            // Un .zip manipulado no debe poder escribir fuera de la carpeta de partidas
            string zip = Path.Combine(_raiz, "malo.zip");
            using (var archivo = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                using var w = new StreamWriter(archivo.CreateEntry("../fuera.txt").Open());
                w.Write("x");
            }

            Assert.Throws<IOException>(() => ZipBackup.Extraer(zip, Path.Combine(_raiz, "destino")));
            Assert.False(File.Exists(Path.Combine(_raiz, "fuera.txt")));
        }

        // --- Huella del contenido ---

        [Fact]
        public void HuellaCarpeta_IgualSiNoCambiaNada_DistintaSiCambia()
        {
            Archivo("save.dat", "partida 1");
            Archivo(@"perfil\slot1.sav", "abc");
            string h1 = ZipBackup.HuellaCarpeta(_origen);

            Assert.Equal(h1, ZipBackup.HuellaCarpeta(_origen));

            Archivo("save.dat", "partida 2");
            Assert.NotEqual(h1, ZipBackup.HuellaCarpeta(_origen));
        }

        [Fact]
        public void HuellaCarpeta_NoDependeDeLaFecha()
        {
            // Copiar o restaurar archivos cambia su fecha pero no su contenido
            string ruta = Archivo("save.dat", "partida");
            string h1 = ZipBackup.HuellaCarpeta(_origen);
            File.SetLastWriteTime(ruta, DateTime.Now.AddDays(-10));

            Assert.Equal(h1, ZipBackup.HuellaCarpeta(_origen));
        }

        [Fact]
        public void HuellaCarpeta_CambiaSiSeAgregaOBorraUnArchivo()
        {
            Archivo("save.dat", "x");
            string h1 = ZipBackup.HuellaCarpeta(_origen);

            string nuevo = Archivo("nuevo.sav", "y");
            string h2 = ZipBackup.HuellaCarpeta(_origen);
            File.Delete(nuevo);

            Assert.NotEqual(h1, h2);
            Assert.Equal(h1, ZipBackup.HuellaCarpeta(_origen));
        }

        // --- Formato del .json ---

        [Fact]
        public void GameVersion_GuardaSiEstaComprimidaYSuHuella()
        {
            var v = new GameVersion { Number = 2, Comprimido = true, HuellaContenido = "abc123" };
            var copia = JsonConvert.DeserializeObject<GameVersion>(JsonConvert.SerializeObject(v));

            Assert.True(copia.Comprimido);
            Assert.Equal("abc123", copia.HuellaContenido);

            // Versiones antiguas (sin estos campos) siguen siendo backups normales
            var antigua = JsonConvert.DeserializeObject<GameVersion>("{\"Number\":1,\"DriveFolderId\":\"raiz\"}");
            Assert.False(antigua.Comprimido);
            Assert.Null(antigua.HuellaContenido);
        }
    }
}
