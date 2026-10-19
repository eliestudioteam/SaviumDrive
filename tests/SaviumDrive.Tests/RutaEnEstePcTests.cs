using Newtonsoft.Json;
using SaviumDrive.Models;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace SaviumDrive.Tests
{
    // GameConfig.EquipoActual es estático: estas pruebas no deben correr en paralelo con otras que lo cambien
    [Collection("EquipoActual")]
    public class RutaEnEstePcTests : IDisposable
    {
        private readonly string _equipoAnterior = GameConfig.EquipoActual;
        private readonly string _dir;

        public RutaEnEstePcTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "SaviumDriveTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            GameConfig.EquipoActual = _equipoAnterior;
            try { Directory.Delete(_dir, true); } catch { }
        }

        private static GameConfig Juego() => new GameConfig
        {
            Name = "MINECRAFT",
            LocalPath = @"C:\Users\PcDeCasa\AppData\Roaming\.minecraft\saves"
        };

        [Fact]
        public void RutaEnEstePc_SinRutaPropia_UsaLaOriginal()
        {
            GameConfig.EquipoActual = "portatil";
            Assert.Equal(@"C:\Users\PcDeCasa\AppData\Roaming\.minecraft\saves", Juego().RutaEnEstePc);
        }

        [Fact]
        public void AsignarRutaEnEstePc_NoCambiaLaDeOtrosEquipos()
        {
            var juego = Juego();

            GameConfig.EquipoActual = "portatil";
            juego.AsignarRutaEnEstePc(@"D:\Juegos\Minecraft\saves");
            Assert.Equal(@"D:\Juegos\Minecraft\saves", juego.RutaEnEstePc);

            // El PC de casa (sin ruta propia) sigue usando la original
            GameConfig.EquipoActual = "pc-de-casa";
            Assert.Equal(@"C:\Users\PcDeCasa\AppData\Roaming\.minecraft\saves", juego.RutaEnEstePc);
            Assert.Equal(@"C:\Users\PcDeCasa\AppData\Roaming\.minecraft\saves", juego.LocalPath);
        }

        [Fact]
        public void NombreCarpetaBackup_EsElDeLaCarpetaOriginalEnTodosLosEquipos()
        {
            var juego = Juego();
            GameConfig.EquipoActual = "portatil";
            juego.AsignarRutaEnEstePc(@"D:\OtraCarpeta\MisPartidas");

            // En Drive el backup se sigue buscando como "saves", aunque aquí la carpeta se llame distinto
            Assert.Equal("saves", juego.NombreCarpetaBackup);

            juego.LocalPath = @"C:\Saves\Mi Juego\";
            Assert.Equal("Mi Juego", juego.NombreCarpetaBackup);
        }

        [Fact]
        public void NeedsRelink_SegunLaCarpetaDeEstePc()
        {
            var juego = Juego();
            GameConfig.EquipoActual = "portatil";
            Assert.True(juego.NeedsRelink);   // la ruta del otro PC no existe aquí

            juego.AsignarRutaEnEstePc(_dir);
            Assert.False(juego.NeedsRelink);
        }

        [Fact]
        public void Json_GuardaLasRutasPorEquipoYNoLasCalculadas()
        {
            var juego = Juego();
            GameConfig.EquipoActual = "portatil";
            juego.AsignarRutaEnEstePc(@"D:\Juegos\saves");

            string json = JsonConvert.SerializeObject(juego);
            var copia = JsonConvert.DeserializeObject<GameConfig>(json);

            Assert.Equal(@"D:\Juegos\saves", copia.RutasPorEquipo["portatil"]);
            Assert.Equal(@"D:\Juegos\saves", copia.RutaEnEstePc);
            Assert.DoesNotContain("RutaEnEstePc", json);
            Assert.DoesNotContain("NeedsRelink", json);
            Assert.DoesNotContain("EquipoActual", json);

            // Juegos antiguos (sin RutasPorEquipo) siguen funcionando
            var antiguo = JsonConvert.DeserializeObject<GameConfig>("{\"Name\":\"X\",\"LocalPath\":\"C:\\\\Saves\"}");
            Assert.Equal(@"C:\Saves", antiguo.RutaEnEstePc);
        }
    }
}
