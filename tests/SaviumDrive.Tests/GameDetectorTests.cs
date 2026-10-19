using SaviumDrive.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SaviumDrive.Tests
{
    public class GameStoresTests
    {
        // Formato real de Steam (steamapps/libraryfolders.vdf)
        private const string LibraryFolders = @"""libraryfolders""
{
	""0""
	{
		""path""		""C:\\Program Files (x86)\\Steam""
		""label""		""""
		""apps""
		{
			""108710""		""8624512019""
		}
	}
	""1""
	{
		""path""		""D:\\SteamLibrary""
	}
}";

        // appmanifest_108710.acf de Alan Wake (como el de este PC)
        private const string ManifestAlanWake = @"""AppState""
{
	""appid""		""108710""
	""Universe""		""1""
	""name""		""Alan Wake""
	""StateFlags""		""4""
	""installdir""		""Alan Wake""
	""UserConfig""
	{
		""language""		""spanish""
	}
}";

        [Fact]
        public void BibliotecasSteam_LeeTodasLasRutas()
        {
            Assert.Equal(new[] { @"C:\Program Files (x86)\Steam", @"D:\SteamLibrary" }, GameStores.BibliotecasSteam(LibraryFolders));
        }

        [Fact]
        public void LeerManifestSteam_NombreRealCarpetaYAppId()
        {
            var juego = GameStores.LeerManifestSteam(ManifestAlanWake, @"C:\Program Files (x86)\Steam\steamapps");

            Assert.Equal("Alan Wake", juego.Nombre);
            Assert.Equal(@"C:\Program Files (x86)\Steam\steamapps\common\Alan Wake", juego.CarpetaInstalacion);
            Assert.Equal("108710", juego.SteamAppId);
            Assert.Equal("Steam", juego.Tienda);
        }

        [Fact]
        public void LeerManifestSteam_Incompleto_DevuelveNull()
        {
            Assert.Null(GameStores.LeerManifestSteam(@"""AppState"" { ""appid"" ""1"" }", @"C:\steamapps"));
        }

        [Fact]
        public void LeerManifestEpic_NombreYCarpeta()
        {
            const string item = "{ \"FormatVersion\": 0, \"DisplayName\": \"Hogwarts Legacy\", \"InstallLocation\": \"C:\\\\Program Files\\\\Epic Games\\\\HogwartsLegacy\", \"AppName\": \"fa4240e5\" }";
            var juego = GameStores.LeerManifestEpic(item);

            Assert.Equal("Hogwarts Legacy", juego.Nombre);
            Assert.Equal(@"C:\Program Files\Epic Games\HogwartsLegacy", juego.CarpetaInstalacion);
            Assert.Equal("Epic", juego.Tienda);
            Assert.Null(GameStores.LeerManifestEpic("esto no es json"));
        }

        [Fact]
        public void BuscarPorEjecutable_EligeLaCarpetaMasConcretaYNoConfundePrefijos()
        {
            var catalogo = new List<JuegoInstalado>
            {
                new JuegoInstalado { Nombre = "Juego", CarpetaInstalacion = @"D:\Juegos\Juego" },
                new JuegoInstalado { Nombre = "Juego 2", CarpetaInstalacion = @"D:\Juegos\Juego 2" },
                new JuegoInstalado { Nombre = "DLC", CarpetaInstalacion = @"D:\Juegos\Juego\DLC" },
            };

            Assert.Equal("Juego 2", GameStores.BuscarPorEjecutable(catalogo, @"D:\Juegos\Juego 2\bin\juego2.exe").Nombre);
            Assert.Equal("Juego", GameStores.BuscarPorEjecutable(catalogo, @"D:\Juegos\Juego\juego.exe").Nombre);
            Assert.Equal("DLC", GameStores.BuscarPorEjecutable(catalogo, @"D:\Juegos\Juego\DLC\dlc.exe").Nombre);
            Assert.Null(GameStores.BuscarPorEjecutable(catalogo, @"D:\Otros\app.exe"));
        }
    }

    public class GameClassifierTests
    {
        private const string Steam = @"C:\Program Files (x86)\Steam";

        private static GameCatalog Catalogo()
        {
            var c = new GameCatalog();
            c.CarpetasDeLanzadores.Add(Steam);
            c.CarpetasDeBibliotecas.Add(Steam + @"\steamapps");
            c.Juegos.Add(new JuegoInstalado
            {
                Nombre = "Alan Wake", Tienda = "Steam", SteamAppId = "108710",
                CarpetaInstalacion = Steam + @"\steamapps\common\Alan Wake"
            });
            return c;
        }

        private static InfoProceso Proceso(string exe, string clase = "", bool pantallaCompleta = false) => new InfoProceso
        {
            RutaExe = exe,
            NombreProceso = System.IO.Path.GetFileNameWithoutExtension(exe),
            ClaseVentana = clase,
            PantallaCompleta = pantallaCompleta
        };

        private static Func<string, IEnumerable<string>> Dlls(params string[] dlls) => _ => dlls;

        // --- El caso que fallaba: Steam en primer plano al lanzar Alan Wake ---

        [Fact]
        public void SteamWebHelper_NoEsJuegoAunqueTengaDllsGraficasYPantallaCompleta()
        {
            var p = Proceso(Steam + @"\bin\cef\cef.win7x64\steamwebhelper.exe", pantallaCompleta: true);
            var r = GameClassifier.Clasificar(p, Catalogo(), Dlls("d3dcompiler_47.dll", "vulkan-1.dll", "libcef.dll"));
            Assert.False(r.EsJuego);
        }

        [Fact]
        public void OtroProcesoDeLaCarpetaDeSteam_NoEsJuego()
        {
            // Aunque no esté en la lista por nombre: está en Steam pero fuera de steamapps
            var p = Proceso(Steam + @"\bin\steamhelperx.exe", pantallaCompleta: true);
            Assert.False(GameClassifier.Clasificar(p, Catalogo(), Dlls()).EsJuego);
        }

        [Fact]
        public void AlanWake_EsJuegoConNombreRealDeSteamYAppId()
        {
            var p = Proceso(Steam + @"\steamapps\common\Alan Wake\AlanWake.exe");
            p.NombreProducto = "AlanWake";

            var r = GameClassifier.Clasificar(p, Catalogo(), Dlls());

            Assert.True(r.EsJuego);
            Assert.Equal("Alan Wake", r.Nombre);
            Assert.Equal("108710", r.Instalado.SteamAppId);
            Assert.Contains("steam", r.Motivo, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData(@"thirdparty\DirectX\DXSETUP.exe")]
        [InlineData(@"thirdparty\Studio_Redistributable\vcredist_x86.exe")]
        [InlineData(@"EasyAntiCheat\EasyAntiCheat_EOS_Setup.exe")]
        [InlineData(@"unins000.exe")]
        public void InstaladoresDentroDeLaCarpetaDelJuego_NoSonElJuego(string relativa)
        {
            var p = Proceso(Steam + @"\steamapps\common\Alan Wake\" + relativa);
            Assert.False(GameClassifier.Clasificar(p, Catalogo(), Dlls()).EsJuego);
        }

        [Fact]
        public void JuegoConAntitrampasEnElNombre_SigueSiendoJuego()
        {
            // RocketLeague_EAC.exe es el propio juego lanzado con EasyAntiCheat
            var p = Proceso(Steam + @"\steamapps\common\Alan Wake\AlanWake_EAC.exe");
            Assert.True(GameClassifier.Clasificar(p, Catalogo(), Dlls()).EsJuego);
        }

        [Fact]
        public void LanzadorYJuegoDeLaMismaInstalacion_TienenLaMismaClave()
        {
            var lanzador = GameClassifier.Clasificar(Proceso(Steam + @"\steamapps\common\Alan Wake\Launcher.exe"), Catalogo(), Dlls());
            var juego = GameClassifier.Clasificar(Proceso(Steam + @"\steamapps\common\Alan Wake\bin\AlanWake.exe"), Catalogo(), Dlls());

            Assert.Equal(lanzador.Clave, juego.Clave);
        }

        // --- Otras señales ---

        [Fact]
        public void DllsGenericas_NoBastan_DllsDeJuego_Si()
        {
            string exe = @"D:\Juegos\Indie\Indie.exe";
            Assert.False(GameClassifier.Clasificar(Proceso(exe), Catalogo(), Dlls("d3dcompiler_47.dll", "vulkan-1.dll", "opengl32.dll")).EsJuego);
            Assert.True(GameClassifier.Clasificar(Proceso(exe), Catalogo(), Dlls("UnityPlayer.dll")).EsJuego);
            Assert.True(GameClassifier.Clasificar(Proceso(exe), Catalogo(), Dlls("steam_api64.dll")).EsJuego);
        }

        [Fact]
        public void VentanaDeMotorYEmuladores_SonJuegos()
        {
            Assert.True(GameClassifier.Clasificar(Proceso(@"D:\X\juego.exe", "UnrealWindow"), Catalogo(), Dlls()).EsJuego);
            Assert.True(GameClassifier.Clasificar(Proceso(@"D:\Emus\RetroArch\retroarch.exe"), Catalogo(), Dlls()).EsJuego);
        }

        [Fact]
        public void AppsElectronYProgramasComunes_NoSonJuegos()
        {
            var electron = Proceso(@"C:\Users\x\AppData\Local\AnthropicClaude\app-1.0\algo.exe", pantallaCompleta: true);
            electron.EsAppElectron = true;
            Assert.False(GameClassifier.Clasificar(electron, Catalogo(), Dlls()).EsJuego);

            Assert.False(GameClassifier.Clasificar(Proceso(@"C:\Program Files\Google\Chrome\Application\chrome.exe", pantallaCompleta: true), Catalogo(), Dlls()).EsJuego);
            Assert.False(GameClassifier.Clasificar(Proceso(@"C:\Program Files (x86)\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe"), Catalogo(), Dlls()).EsJuego);
        }

        [Fact]
        public void SinSenales_NoEsJuego_PantallaCompleta_Si()
        {
            Assert.False(GameClassifier.Clasificar(Proceso(@"D:\Apps\visor.exe"), Catalogo(), Dlls()).EsJuego);
            Assert.True(GameClassifier.Clasificar(Proceso(@"D:\Apps\juegosuelto.exe", pantallaCompleta: true), Catalogo(), Dlls()).EsJuego);
        }

        // --- Nombre real ---

        [Fact]
        public void NombreReal_PropiedadesDelExeSinSimbolos()
        {
            var p = Proceso(@"D:\Juegos\ER\Game\eldenring.exe");
            p.NombreProducto = "ELDEN RING™";
            Assert.Equal("ELDEN RING", GameClassifier.NombreReal(p, null));
        }

        [Fact]
        public void NombreReal_ProductoGenerico_UsaLaCarpetaDelJuego()
        {
            // "Unity" no es el nombre del juego; las carpetas "Binaries\Win64" tampoco
            var p = Proceso(@"D:\Juegos\Hollow Knight\Binaries\Win64\hollow_knight.exe");
            p.NombreProducto = "Unity";
            Assert.Equal("Hollow Knight", GameClassifier.NombreReal(p, null));
        }

        [Fact]
        public void NombreReal_UltimoRecursoElProceso()
        {
            var p = new InfoProceso { NombreProceso = "MiJuego", RutaExe = null };
            Assert.Equal("MiJuego", GameClassifier.NombreReal(p, null));
        }

        [Theory]
        [InlineData("Alan Wake Launcher", "Alan Wake")]
        [InlineData("  Witcher®  3  ", "Witcher 3")]
        [InlineData("juego.exe", "juego")]
        public void Limpiar_QuitaSimbolosYSufijos(string entrada, string esperado)
        {
            Assert.Equal(esperado, GameClassifier.Limpiar(entrada));
        }
    }

    /// <summary>Con el catálogo real de este PC (solo si DETECTOR_REAL=1; en el CI se salta).</summary>
    public class GameCatalogRealTests
    {
        private readonly Xunit.Abstractions.ITestOutputHelper _salida;
        public GameCatalogRealTests(Xunit.Abstractions.ITestOutputHelper salida) { _salida = salida; }

        [Fact]
        public void CatalogoReal_ClasificaLosProcesosDeEstePc()
        {
            if (Environment.GetEnvironmentVariable("DETECTOR_REAL") != "1") return;

            var catalogo = GameCatalog.Cargar();
            _salida.WriteLine($"Juegos instalados: {catalogo.Juegos.Count}");
            foreach (var j in catalogo.Juegos) _salida.WriteLine($"  [{j.Tienda}] {j.Nombre} | {j.CarpetaInstalacion} | appid {j.SteamAppId}");
            _salida.WriteLine($"Lanzadores: {string.Join(" | ", catalogo.CarpetasDeLanzadores)}");

            IEnumerable<string> Dlls(string carpeta) =>
                System.IO.Directory.Exists(carpeta) ? System.IO.Directory.EnumerateFiles(carpeta, "*.dll").Select(System.IO.Path.GetFileName).ToList() : new List<string>();

            var exes = new List<string>();
            string steam = catalogo.CarpetasDeLanzadores.FirstOrDefault(c => c.Contains("Steam", StringComparison.OrdinalIgnoreCase));
            if (steam != null) exes.AddRange(System.IO.Directory.EnumerateFiles(steam, "steamwebhelper.exe", System.IO.SearchOption.AllDirectories).Take(1));
            foreach (var j in catalogo.Juegos.Where(j => System.IO.Directory.Exists(j.CarpetaInstalacion)))
                exes.AddRange(System.IO.Directory.EnumerateFiles(j.CarpetaInstalacion, "*.exe", System.IO.SearchOption.AllDirectories).Take(5));

            foreach (var exe in exes)
            {
                var info = new InfoProceso { RutaExe = exe, NombreProceso = System.IO.Path.GetFileNameWithoutExtension(exe) };
                try
                {
                    var v = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe);
                    info.NombreProducto = v.ProductName;
                    info.Descripcion = v.FileDescription;
                }
                catch { }
                var r = GameClassifier.Clasificar(info, catalogo, Dlls);
                _salida.WriteLine($"{(r.EsJuego ? "JUEGO   " : "no juego")} | nombre '{r.Nombre}' | {r.Motivo} | {exe}");

                string nombre = System.IO.Path.GetFileNameWithoutExtension(exe).ToLowerInvariant();
                bool esAyudante = new[] { "setup", "redist", "vcredist", "unins", "crash" }.Any(nombre.Contains);
                if (exe.EndsWith("steamwebhelper.exe", StringComparison.OrdinalIgnoreCase) || esAyudante) Assert.False(r.EsJuego);
                else Assert.True(r.EsJuego);
            }
        }
    }

    /// <summary>
    /// Prueba en vivo del detector (solo si DETECTOR_VIVO_EXE apunta a un "juego" de prueba; en el CI se salta):
    /// lanza el .exe, espera a que el detector lo siga, lo cierra y comprueba el aviso.
    /// </summary>
    public class GameDetectorLiveTests
    {
        private readonly Xunit.Abstractions.ITestOutputHelper _salida;
        public GameDetectorLiveTests(Xunit.Abstractions.ITestOutputHelper salida) { _salida = salida; }

        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        private uint ultimoFrente;
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

        private static void TraerAlFrente(IntPtr ventana)
        {
            // Pulsar y soltar ALT permite a SetForegroundWindow saltarse el bloqueo de primer plano de Windows
            keybd_event(0x12, 0, 0, UIntPtr.Zero);
            keybd_event(0x12, 0, 2, UIntPtr.Zero);
            SetForegroundWindow(ventana);
        }

        [Fact]
        public void DetectorEnVivo_AvisaAlCerrarElJuegoConSuNombre()
        {
            string exe = Environment.GetEnvironmentVariable("DETECTOR_VIVO_EXE");
            if (string.IsNullOrEmpty(exe) || !System.IO.File.Exists(exe)) return;

            var detector = new GameDetectorService(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));
            GameSession aviso = null;
            var avisado = new System.Threading.ManualResetEventSlim();
            detector.GameClosed += (s, sesion) => { aviso = sesion; avisado.Set(); };
            detector.Start();

            using var juego = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
            try
            {
                var limite = DateTime.UtcNow.AddSeconds(40);
                while (detector.JuegosAbiertos == 0 && DateTime.UtcNow < limite)
                {
                    // Un proceso lanzado desde segundo plano no recibe el foco solo: traerlo al frente
                    // (con un juego real, el usuario lo abre y ya queda en primer plano)
                    juego.Refresh();
                    if (juego.MainWindowHandle != IntPtr.Zero) TraerAlFrente(juego.MainWindowHandle);
                    System.Threading.Thread.Sleep(500);

                    GetWindowThreadProcessId(GetForegroundWindow(), out uint pidFrente);
                    if (pidFrente != ultimoFrente)
                    {
                        ultimoFrente = pidFrente;
                        _salida.WriteLine($"Primer plano: pid {pidFrente} (juego {juego.Id}, ventana {juego.MainWindowHandle})");
                    }
                }

                var info = new InfoProceso { RutaExe = exe, NombreProceso = System.IO.Path.GetFileNameWithoutExtension(exe) };
                var r = GameClassifier.Clasificar(info, GameCatalog.Cargar(),
                    c => System.IO.Directory.EnumerateFiles(c, "*.dll").Select(System.IO.Path.GetFileName).ToList());
                _salida.WriteLine($"Clasificación del .exe: {(r.EsJuego ? "juego" : "no juego")} ({r.Motivo}), nombre '{r.Nombre}'");
                _salida.WriteLine($"Juegos seguidos: {detector.JuegosAbiertos}");
                Assert.Equal(1, detector.JuegosAbiertos);

                System.Threading.Thread.Sleep(6000); // más que la duración mínima
                juego.Kill();
                Assert.True(avisado.Wait(TimeSpan.FromSeconds(15)), "El detector no avisó al cerrarse el juego");

                _salida.WriteLine($"Aviso: '{aviso.Name}' | {aviso.Duration} | {aviso.ExecutablePath}");
                Assert.False(string.IsNullOrWhiteSpace(aviso.Name));
                Assert.True(aviso.Duration >= TimeSpan.FromSeconds(5));
                Assert.Equal(0, detector.JuegosAbiertos);
            }
            finally
            {
                detector.Stop();
                try { if (!juego.HasExited) juego.Kill(); } catch { }
            }
        }
    }

    public class GameSessionTrackerTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 20, 20, 0, 0, DateTimeKind.Utc);

        private static GameSession Sesion(string nombre) => new GameSession { Name = nombre };

        [Fact]
        public void VariosProcesosDelMismoJuego_UnSoloAvisoAlCerrarElUltimo()
        {
            var tracker = new GameSessionTracker();
            var abiertos = new HashSet<int> { 10, 11 };
            tracker.Registrar("alan wake", 10, Sesion("Alan Wake"), T0);              // lanzador
            tracker.Registrar("alan wake", 11, Sesion("Alan Wake"), T0.AddSeconds(20)); // juego

            abiertos.Remove(10);
            Assert.Empty(tracker.Revisar(abiertos.Contains, T0.AddMinutes(30)));

            abiertos.Remove(11);
            var cerradas = tracker.Revisar(abiertos.Contains, T0.AddMinutes(45));
            var sesion = Assert.Single(cerradas);
            Assert.Equal("Alan Wake", sesion.Name);
            Assert.Equal(TimeSpan.FromMinutes(45), sesion.Duration);
        }

        [Fact]
        public void SesionMuyCorta_NoAvisa()
        {
            var tracker = new GameSessionTracker();
            tracker.Registrar("lanzador", 5, Sesion("Juego"), T0);
            Assert.Empty(tracker.Revisar(_ => false, T0.AddSeconds(30)));
            Assert.Equal(0, tracker.JuegosAbiertos);
        }

        [Fact]
        public void DosJuegosAbiertos_SeSiguenPorSeparado()
        {
            // Antes solo se vigilaba un proceso: el segundo juego se ignoraba
            var tracker = new GameSessionTracker();
            var abiertos = new HashSet<int> { 1, 2 };
            tracker.Registrar("juego a", 1, Sesion("A"), T0);
            tracker.Registrar("juego b", 2, Sesion("B"), T0);

            abiertos.Remove(2);
            Assert.Equal("B", Assert.Single(tracker.Revisar(abiertos.Contains, T0.AddMinutes(10))).Name);
            Assert.True(tracker.EstaSiguiendo(1));
            Assert.False(tracker.EstaSiguiendo(2));
        }
    }
}
