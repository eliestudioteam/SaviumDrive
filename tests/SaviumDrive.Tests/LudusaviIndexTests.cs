using SaviumDrive.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace SaviumDrive.Tests
{
    public class LudusaviIndexTests : IDisposable
    {
        private readonly ITestOutputHelper _salida;
        private readonly string _raiz;

        public LudusaviIndexTests(ITestOutputHelper salida)
        {
            _salida = salida;
            _raiz = Path.Combine(Path.GetTempPath(), "SaviumDriveTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_raiz);
        }

        public void Dispose()
        {
            try { Directory.Delete(_raiz, true); } catch { }
        }

        // Fragmentos reales de manifest.yaml (formato de Ludusavi)
        private const string Manifest = @"---
""!Anyway!"":
  cloud:
    steam: true
  files:
    ""<winLocalAppData>/anyway"":
      tags:
        - save
      when:
        - os: windows
  steam:
    id: 866510
""!4RC4N01D!"":
  steam:
    id: 777010
""'83"":
  files:
    ""<winLocalAppData>/XV83/Saved/Config/Windows/*.ini"":
      when:
        - os: windows
          store: steam
    ""<winLocalAppData>/XV83/Saved/SaveGames/**/*.sav"":
      when:
        - os: windows
          store: steam
  steam:
    id: 1059220
Elden Ring:
  cloud:
    steam: true
  files:
    ""<winAppData>/EldenRing/<storeUserId>"":
      tags:
        - save
      when:
        - os: windows
    ""<winAppData>/EldenRing/GraphicsConfig.xml"":
      tags:
        - config
      when:
        - os: windows
  installDir:
    ELDEN RING: {}
  launch:
    ""<base>/Game/start_protected_game.exe"":
      - when:
          - store: steam
        workingDir: ""<base>/Game""
Solo Linux:
  files:
    ""<home>/.local/share/sololinux"":
      tags:
        - save
      when:
        - os: linux
    ""<base>/saves"":
      tags:
        - save
""Juego \""con\"" comillas"":
  files:
    ""<winDocuments>/My Games/Comillas"": {}
";

        private List<JuegoLudusavi> Leer() => LudusaviIndex.LeerManifest(new StringReader(Manifest)).ToList();

        // --- Lectura del manifest ---

        [Fact]
        public void LeerManifest_SoloJuegosConRutasDeWindows()
        {
            var juegos = Leer();

            Assert.Equal(new[] { "!Anyway!", "'83", "Elden Ring", "Juego \"con\" comillas" }, juegos.Select(j => j.Nombre));
            // "!4RC4N01D!" no tiene rutas y "Solo Linux" solo tiene rutas de Linux o de la carpeta de instalación
        }

        [Fact]
        public void LeerManifest_DescartaRutasSoloDeConfiguracion()
        {
            var elden = Leer().Single(j => j.Nombre == "Elden Ring");
            Assert.Equal(new[] { "<winAppData>/EldenRing/<storeUserId>" }, elden.Rutas);
        }

        [Fact]
        public void LeerManifest_IncluyeRutasSinEtiquetasYConVariasCondiciones()
        {
            var juego = Leer().Single(j => j.Nombre == "'83");
            Assert.Equal(2, juego.Rutas.Count);
            Assert.Contains("<winLocalAppData>/XV83/Saved/SaveGames/**/*.sav", juego.Rutas);
        }

        // --- Índice y búsqueda ---

        private StringReader Indice()
        {
            var escritor = new StringWriter();
            LudusaviIndex.EscribirIndice(new StringReader(Manifest), escritor);
            return new StringReader(escritor.ToString());
        }

        [Theory]
        [InlineData("Elden Ring")]
        [InlineData("ELDEN RING")]
        [InlineData("eldenring")]      // nombre del proceso detectado
        [InlineData("Elden-Ring!")]
        public void Buscar_IgnoraMayusculasEspaciosYSimbolos(string nombre)
        {
            var juego = LudusaviIndex.Buscar(Indice(), nombre);
            Assert.Equal("Elden Ring", juego?.Nombre);
        }

        [Fact]
        public void Buscar_PorPrefijo_EligeElNombreMasCorto()
        {
            Assert.Equal("Elden Ring", LudusaviIndex.Buscar(Indice(), "elden")?.Nombre);
        }

        [Fact]
        public void BuscarCandidatos_ExactoPrimeroYLuegoPrefijosDelMasCorto()
        {
            var escritor = new StringWriter();
            escritor.WriteLine("minecraftjavaedition\tMinecraft: Java Edition\t<winAppData>/.minecraft/saves");
            escritor.WriteLine("minecraftdungeons\tMinecraft Dungeons\t<winLocalAppData>/Dungeons/<storeUserId>");
            escritor.WriteLine("minecraft\tMinecraft\t<winAppData>/MinecraftExacto");
            escritor.WriteLine("otrojuego\tOtro juego\t<winAppData>/Otro");

            var candidatos = LudusaviIndex.BuscarCandidatos(new StringReader(escritor.ToString()), "Minecraft");

            Assert.Equal(new[] { "Minecraft", "Minecraft Dungeons", "Minecraft: Java Edition" }, candidatos.Select(c => c.Nombre));
        }

        [Fact]
        public void BuscarCandidatos_RespetaElMaximo()
        {
            var escritor = new StringWriter();
            for (int i = 0; i < 100; i++) escritor.WriteLine($"juegoserie{i:D3}\tJuego Serie {i}\t<winAppData>/Serie{i}");

            var candidatos = LudusaviIndex.BuscarCandidatos(new StringReader(escritor.ToString()), "Juego Serie", 5);

            Assert.Equal(5, candidatos.Count);
        }

        [Fact]
        public void LeerManifest_GuardaElIdDeSteam()
        {
            Assert.Equal("866510", Leer().Single(j => j.Nombre == "!Anyway!").SteamId);
            Assert.Null(Leer().Single(j => j.Nombre == "Elden Ring").SteamId); // el fragmento de prueba no lo trae
        }

        [Fact]
        public void BuscarPorSteamId_EncuentraElJuegoSinDependerDelNombre()
        {
            var juego = LudusaviIndex.BuscarPorSteamId(Indice(), "866510");
            Assert.Equal("!Anyway!", juego?.Nombre);
            Assert.Equal("866510", juego.SteamId);
            Assert.Null(LudusaviIndex.BuscarPorSteamId(Indice(), "999999"));
        }

        [Fact]
        public void Indice_AntiguoSinIdDeSteam_SigueFuncionando()
        {
            var juego = LudusaviIndex.Buscar(new StringReader("minecraft\tMinecraft\t<winAppData>/.minecraft/saves\n"), "minecraft");
            Assert.Equal("Minecraft", juego.Nombre);
            Assert.Null(juego.SteamId);
        }

        [Fact]
        public void Buscar_JuegoDesconocido_DevuelveNull()
        {
            Assert.Null(LudusaviIndex.Buscar(Indice(), "No Existe Este Juego"));
            Assert.Null(LudusaviIndex.Buscar(Indice(), "e")); // demasiado corto
        }

        [Fact]
        public void Normalizar_QuitaAcentosYSimbolos()
        {
            Assert.Equal("pokemonleyendas", LudusaviIndex.Normalizar("Pokémon: Leyendas"));
        }

        // --- Rutas en este PC ---

        private Dictionary<string, string> Carpetas()
        {
            var appData = Path.Combine(_raiz, "AppData");
            var local = Path.Combine(_raiz, "Local");
            Directory.CreateDirectory(appData);
            Directory.CreateDirectory(local);
            return new Dictionary<string, string> { ["<winAppData>"] = appData, ["<winLocalAppData>"] = local };
        }

        [Fact]
        public void ResolverCarpeta_StoreUserIdConUnaSolaCarpeta_EntraEnElla()
        {
            var c = Carpetas();
            string steamId = Path.Combine(c["<winAppData>"], "EldenRing", "76561198000000000");
            Directory.CreateDirectory(steamId);

            Assert.Equal(steamId, LudusaviIndex.ResolverCarpeta("<winAppData>/EldenRing/<storeUserId>", c));
        }

        [Fact]
        public void ResolverCarpeta_StoreUserIdConVariasCarpetas_SeQuedaEnLaDelJuego()
        {
            var c = Carpetas();
            Directory.CreateDirectory(Path.Combine(c["<winAppData>"], "EldenRing", "111"));
            Directory.CreateDirectory(Path.Combine(c["<winAppData>"], "EldenRing", "222"));

            Assert.Equal(Path.Combine(c["<winAppData>"], "EldenRing"),
                LudusaviIndex.ResolverCarpeta("<winAppData>/EldenRing/<storeUserId>", c));
        }

        [Fact]
        public void ResolverCarpeta_PatronDeArchivos_UsaSuCarpeta()
        {
            var c = Carpetas();
            string saves = Path.Combine(c["<winLocalAppData>"], "XV83", "Saved", "SaveGames");
            Directory.CreateDirectory(saves);

            Assert.Equal(saves, LudusaviIndex.ResolverCarpeta("<winLocalAppData>/XV83/Saved/SaveGames/**/*.sav", c));
        }

        [Fact]
        public void ResolverCarpeta_JuegoNoInstaladoOPrefijoDesconocido_DevuelveNull()
        {
            var c = Carpetas();
            Assert.Null(LudusaviIndex.ResolverCarpeta("<winAppData>/NoInstalado/Saves", c));
            Assert.Null(LudusaviIndex.ResolverCarpeta("<base>/saves", c));
            Assert.Null(LudusaviIndex.ResolverCarpeta("<xdgData>/juego", c));
        }

        [Fact]
        public void ResolverCarpeta_NoSugiereUnaCarpetaEspecialEntera()
        {
            // "<winAppData>/*.ini" no debe sugerir todo AppData
            Assert.Null(LudusaviIndex.ResolverCarpeta("<winAppData>/*.ini", Carpetas()));
        }

        [Fact]
        public void PrimeraCarpetaExistente_SaltaLasQueNoExisten()
        {
            var c = Carpetas();
            string segunda = Path.Combine(c["<winLocalAppData>"], "Juego");
            Directory.CreateDirectory(segunda);
            var juego = new JuegoLudusavi { Nombre = "Juego" };
            juego.Rutas.Add("<winAppData>/Juego");
            juego.Rutas.Add("<winLocalAppData>/Juego");

            Assert.Equal(segunda, LudusaviIndex.PrimeraCarpetaExistente(juego, c));
        }

        // --- Manifest real (solo si se indica dónde está: no se descarga en el CI) ---

        [Fact]
        public void ManifestReal_SeIndexaEnStreamingYEncuentraJuegosConocidos()
        {
            string ruta = Environment.GetEnvironmentVariable("LUDUSAVI_MANIFEST");
            if (string.IsNullOrEmpty(ruta) || !File.Exists(ruta)) return;

            long memoriaAntes = GC.GetTotalMemory(true);
            var reloj = Stopwatch.StartNew();
            string indice = Path.Combine(_raiz, "ludusavi.tsv");
            int juegos;
            using (var lector = new StreamReader(ruta))
            using (var escritor = new StreamWriter(indice))
                juegos = LudusaviIndex.EscribirIndice(lector, escritor);
            reloj.Stop();
            long memoriaDespues = GC.GetTotalMemory(true);

            _salida.WriteLine($"Juegos con rutas de Windows: {juegos}");
            _salida.WriteLine($"Índice: {new FileInfo(indice).Length / 1024} KB (manifest: {new FileInfo(ruta).Length / 1024} KB)");
            _salida.WriteLine($"Tiempo: {reloj.ElapsedMilliseconds} ms; memoria retenida: {(memoriaDespues - memoriaAntes) / 1024} KB");

            Assert.True(juegos > 5000);
            using var buscar = new StreamReader(indice);
            var elden = LudusaviIndex.Buscar(buscar, "ELDEN RING");
            _salida.WriteLine($"Elden Ring: {string.Join(" | ", elden?.Rutas ?? new List<string>())}");
            Assert.Contains("<winAppData>/EldenRing/<storeUserId>", elden.Rutas);
        }
    }
}
