using SaviumDrive.Services;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace SaviumDrive.Tests
{
    /// <summary>Juegos que no vienen de Steam, Epic ni GOG: Minecraft, Hytale, juegos descargados de una web...</summary>
    public class GameDetectorSinTiendaTests
    {
        private static InfoProceso Proceso(string exe, string clase = "", bool pantallaCompleta = false, string titulo = null) => new InfoProceso
        {
            RutaExe = exe,
            NombreProceso = Path.GetFileNameWithoutExtension(exe),
            ClaseVentana = clase,
            PantallaCompleta = pantallaCompleta,
            TituloVentana = titulo
        };

        private static Func<string, IEnumerable<string>> SinDlls => _ => Array.Empty<string>();

        private const string JavaDeMinecraft = @"C:\Users\x\AppData\Roaming\.minecraft\runtime\java-runtime-delta\windows-x64\java-runtime-delta\bin\javaw.exe";

        [Fact]
        public void MinecraftJava_SeReconocePorSuVentanaYTomaElNombreDelTitulo()
        {
            var r = GameClassifier.Clasificar(Proceso(JavaDeMinecraft, "GLFW30", titulo: "Minecraft* 1.21.4 - Singleplayer"), new GameCatalog(), SinDlls);

            Assert.True(r.EsJuego);
            Assert.Equal("Minecraft", r.Nombre); // no "javaw" ni "bin"
            Assert.Equal("titulo:minecraft", r.Clave);
        }

        [Fact]
        public void DosJuegosEnJava_NoSeMezclan()
        {
            var a = GameClassifier.Clasificar(Proceso(@"C:\Java\bin\javaw.exe", "GLFW30", titulo: "Minecraft 1.20.1"), new GameCatalog(), SinDlls);
            var b = GameClassifier.Clasificar(Proceso(@"C:\Java\bin\javaw.exe", "LWJGL", titulo: "Project Zomboid"), new GameCatalog(), SinDlls);
            Assert.NotEqual(a.Clave, b.Clave);
            Assert.Equal("Project Zomboid", b.Nombre);
        }

        [Fact]
        public void ProgramaEnJavaSinVentanaDeJuego_NoEsJuego()
        {
            var r = GameClassifier.Clasificar(Proceso(@"C:\Java\bin\javaw.exe", "SunAwtFrame", titulo: "Mi programa"), new GameCatalog(), SinDlls);
            Assert.False(r.EsJuego);
        }

        [Theory]
        [InlineData(@"C:\Program Files\Hytale\HytaleLauncher.exe")]
        [InlineData(@"C:\Games\MiJuego\MiJuego Launcher.exe")]
        [InlineData(@"C:\XboxGames\Minecraft Launcher\Content\MinecraftLauncher.exe")]
        public void LanzadoresPropios_NoSonJuegoAunqueTenganDllsDeJuego(string exe)
        {
            var r = GameClassifier.Clasificar(Proceso(exe, pantallaCompleta: false), new GameCatalog(), _ => new[] { "steam_api64.dll" });
            Assert.False(r.EsJuego);
        }

        [Fact]
        public void JuegoDeUnaWeb_ConMotorReconocible_UsaElNombreDeSuCarpeta()
        {
            var r = GameClassifier.Clasificar(Proceso(@"C:\Users\x\AppData\Roaming\Hytale\install\release\package\game\latest\Client\HytaleClient.exe", "SDL_app"),
                new GameCatalog(), SinDlls);
            Assert.True(r.EsJuego);
            Assert.Equal("ventana de motor", r.Motivo);
        }

        [Fact]
        public void JuegoAgregadoPorElUsuario_SeReconoceSinSenalesYConSuNombre()
        {
            string exe = @"D:\Juegos\MiJuegoIndie\bin\juego.exe";
            var catalogo = new GameCatalog();
            var antes = GameClassifier.Clasificar(Proceso(exe), catalogo, SinDlls);
            Assert.False(antes.EsJuego);

            catalogo.Aprendidos[GameClassifier.ClaveDe(Proceso(exe), null)] = "Mi Juego Indie";
            var despues = GameClassifier.Clasificar(Proceso(exe), catalogo, SinDlls);

            Assert.True(despues.EsJuego);
            Assert.Equal("Mi Juego Indie", despues.Nombre);
        }

        [Fact]
        public void NombreElegidoPorElUsuario_ReemplazaAlDetectado()
        {
            var catalogo = new GameCatalog();
            catalogo.Aprendidos["titulo:minecraft"] = "Minecraft Java";
            var r = GameClassifier.Clasificar(Proceso(JavaDeMinecraft, "GLFW30", titulo: "Minecraft 1.21"), catalogo, SinDlls);
            Assert.Equal("Minecraft Java", r.Nombre);
        }

        [Theory]
        [InlineData("Minecraft* 1.21.4 - Singleplayer", "Minecraft")]
        [InlineData("Minecraft 1.20.1", "Minecraft")]
        [InlineData("Mi Juego v0.3.1-beta", "Mi Juego")]
        [InlineData("Starbound | Cargando", "Starbound")]
        [InlineData("", null)]
        [InlineData("1.21", null)]
        public void NombreDesdeTitulo(string titulo, string esperado)
        {
            Assert.Equal(esperado, GameClassifier.NombreDesdeTitulo(titulo));
        }
    }

    public class LearnedGamesTests : IDisposable
    {
        private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "sd-pruebas", "aprendidos-" + Guid.NewGuid().ToString("N"));
        private readonly string _original = LearnedGames.Archivo;

        public LearnedGamesTests() { LearnedGames.Archivo = Path.Combine(_carpeta, "juegos-aprendidos.json"); }

        public void Dispose()
        {
            LearnedGames.Archivo = _original;
            try { Directory.Delete(_carpeta, true); } catch { }
        }

        [Fact]
        public void Aprender_SeGuardaYSeLeeSinDistinguirMayusculas()
        {
            int version = LearnedGames.Version;
            Assert.Empty(LearnedGames.Leer());

            LearnedGames.Aprender(@"d:\juegos\mijuego", "Mi Juego");
            LearnedGames.Aprender("titulo:minecraft", "Minecraft");

            var datos = LearnedGames.Leer();
            Assert.Equal("Mi Juego", datos[@"D:\Juegos\MiJuego"]);
            Assert.Equal("Minecraft", datos["titulo:minecraft"]);
            Assert.True(LearnedGames.Version > version);
        }

        [Fact]
        public void Aprender_SinClave_NoHaceNada()
        {
            LearnedGames.Aprender(null, "X");
            LearnedGames.Aprender("clave", " ");
            Assert.Empty(LearnedGames.Leer());
        }
    }
}
