// Services/GameClassifier.cs
// Decide (sin WinUI) si un proceso en primer plano es un juego y cuál es su nombre real; se prueba desde SaviumDrive.Tests.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SaviumDrive.Services
{
    /// <summary>Lo que se sabe de un proceso en primer plano.</summary>
    public sealed class InfoProceso
    {
        public string NombreProceso { get; set; }
        public string RutaExe { get; set; }
        public string ClaseVentana { get; set; }
        public bool PantallaCompleta { get; set; }
        /// <summary>Nombre de producto y descripción del .exe (propiedades del archivo).</summary>
        public string NombreProducto { get; set; }
        public string Descripcion { get; set; }
        /// <summary>App Electron (Claude, Discord, VS Code...): traen DLL gráficas y parecían juegos.</summary>
        public bool EsAppElectron { get; set; }
    }

    public sealed class Clasificacion
    {
        public bool EsJuego { get; set; }
        /// <summary>Juego de la tienda (Steam/Epic/GOG) al que pertenece el .exe, si se encontró.</summary>
        public JuegoInstalado Instalado { get; set; }
        public string Nombre { get; set; }
        /// <summary>Identifica el juego aunque tenga varios procesos (lanzador + juego): carpeta de instalación o del .exe.</summary>
        public string Clave { get; set; }
        public string Motivo { get; set; }
    }

    public static class GameClassifier
    {
        // Procesos que nunca son juegos (lanzadores, sus ayudantes, overlays, navegadores, apps comunes...)
        private static readonly HashSet<string> NoSonJuegos = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "explorer", "searchhost", "taskmgr", "saviumdrive", "chrome", "firefox", "msedge", "opera", "brave", "vivaldi",
            "discord", "spotify", "code", "devenv", "notepad", "calculatorapp", "calculator", "lockapp", "teams", "ms-teams", "slack",
            "shellexperiencehost", "applicationframehost", "systemsettings", "whatsapp", "telegram",
            "vlc", "mpc-hc64", "potplayer", "photos", "movies", "netflix", "hulu", "primevideo", "plex",
            "obs64", "winword", "excel", "powerpnt", "powerpoint", "outlook", "photoshop", "illustrator", "premiere",
            "startmenuexperiencehost", "textinputhost", "ctfmon", "widgets", "claude", "windowsterminal", "powershell", "cmd",
            // Lanzadores y sus procesos auxiliares
            "steam", "steamwebhelper", "steamservice", "steamerrorreporter", "gameoverlayui", "gameoverlayui64",
            "epicgameslauncher", "epicwebhelper", "epiconlineservicesuserhelper", "epiconlineserviceshost",
            "galaxyclient", "galaxyclient helper", "galaxycommunication", "gog galaxy",
            "eadesktop", "eabackgroundservice", "ealauncher", "origin", "originwebhelperservice",
            "upc", "ubisoftconnect", "uplaywebcore", "battle.net", "agent", "riotclientservices", "riotclientux",
            "xbox", "xboxpcapp", "gamebar", "gamebarftserver", "nvidia overlay", "nvcontainer", "radeonsoftware",
            // Informes de errores y ayudantes de motores
            "unitycrashhandler64", "unitycrashhandler32", "crashreportclient", "crashpad_handler", "werfault"
        };

        // Emuladores y ejecutables de juegos conocidos
        private static readonly HashSet<string> Emuladores = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "rpcs3", "yuzu", "cemu", "dolphin", "pcsx2", "pcsx2-qt", "xenia", "xenia_canary", "retroarch", "snes9x", "project64",
            "citra", "citra-qt", "ryujinx", "dosbox", "scummvm", "ppsspp", "ppssppwindows64", "mame", "duckstation-qt"
        };

        // Clases de ventana de motores de juego
        private static readonly string[] ClasesDeMotor =
        {
            "UnrealWindow", "UnityWndClass", "LaunchUnrealUWindowsClient", "GODOT_WINDOW", "Gamemaker", "SDL_app", "SFML_Window", "Panda3D"
        };

        // DLL propias de juegos. Las genéricas (d3dcompiler, vulkan, opengl32) también las usan navegadores
        // y lanzadores como steamwebhelper, así que ya no cuentan.
        private static readonly string[] DllsDeJuego =
        {
            "steam_api", "unityplayer", "galaxy64", "galaxy.dll", "eossdk", "fmod", "wwise", "physx", "havok", "bink2w64", "binkw32", "gameassembly"
        };

        /// <summary>
        /// Clasifica un proceso. <paramref name="dllsDeCarpeta"/> devuelve los nombres de las .dll junto al .exe
        /// (solo se consulta si hace falta).
        /// </summary>
        public static Clasificacion Clasificar(InfoProceso p, GameCatalog catalogo, Func<string, IEnumerable<string>> dllsDeCarpeta = null)
        {
            var noEs = new Clasificacion { EsJuego = false };
            if (p == null || string.IsNullOrEmpty(p.NombreProceso)) { noEs.Motivo = "sin proceso"; return noEs; }

            // 1. Procesos que nunca son juegos
            if (NoSonJuegos.Contains(p.NombreProceso)) { noEs.Motivo = "lista de no juegos"; return noEs; }

            // Instaladores, redistribuibles y antitrampas que vienen dentro de la carpeta del juego
            if (EsInstaladorOAyudante(p.NombreProceso)) { noEs.Motivo = "instalador o ayudante"; return noEs; }

            string exe = p.RutaExe;
            var juegos = catalogo?.Juegos ?? new List<JuegoInstalado>();

            // 2. El .exe está en la carpeta de un juego instalado (Steam/Epic/GOG): es ese juego, con su nombre real
            var instalado = GameStores.BuscarPorEjecutable(juegos, exe);
            if (instalado != null)
                return Juego(p, instalado, $"juego de {instalado.Tienda}");

            if (!string.IsNullOrEmpty(exe))
            {
                // 3. Dentro de la carpeta de un lanzador pero fuera de sus bibliotecas (ej. Steam\bin\cef\steamwebhelper.exe)
                bool enLanzador = (catalogo?.CarpetasDeLanzadores ?? new List<string>()).Any(c => GameStores.EstaDentroDe(exe, c));
                bool enBiblioteca = (catalogo?.CarpetasDeBibliotecas ?? new List<string>()).Any(c => GameStores.EstaDentroDe(exe, c));
                if (enLanzador && !enBiblioteca) { noEs.Motivo = "proceso de un lanzador"; return noEs; }

                // 4. Programas de Windows
                string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                if (!string.IsNullOrEmpty(windows) && GameStores.EstaDentroDe(exe, windows)) { noEs.Motivo = "programa de Windows"; return noEs; }
            }

            // 5. Apps Electron
            if (p.EsAppElectron) { noEs.Motivo = "app Electron"; return noEs; }

            // 6. Emuladores conocidos
            if (Emuladores.Contains(p.NombreProceso)) return Juego(p, null, "emulador");

            // 7. Ventana de un motor de juego
            string clase = p.ClaseVentana ?? "";
            if (ClasesDeMotor.Any(c => clase.Contains(c, StringComparison.OrdinalIgnoreCase))) return Juego(p, null, "ventana de motor");

            // 8. DLL propias de juegos junto al .exe
            if (!string.IsNullOrEmpty(exe) && dllsDeCarpeta != null)
            {
                var dlls = dllsDeCarpeta(Path.GetDirectoryName(exe)) ?? Enumerable.Empty<string>();
                if (dlls.Any(d => DllsDeJuego.Any(g => d.Contains(g, StringComparison.OrdinalIgnoreCase))))
                    return Juego(p, null, "DLL de juego");
            }

            // 9. Pantalla completa (juegos sueltos sin tienda ni motor reconocible)
            if (p.PantallaCompleta) return Juego(p, null, "pantalla completa");

            noEs.Motivo = "sin señales de juego";
            return noEs;
        }

        private static readonly string[] PartesDeInstaladores =
        {
            "setup", "install", "redist", "vcredist", "dxsetup", "directx", "dotnet", "easyanticheat", "battleye",
            "crashreport", "crashhandler", "unins", "updater", "touchup", "cleanup"
        };

        private static bool EsInstaladorOAyudante(string nombreProceso) =>
            PartesDeInstaladores.Any(parte => nombreProceso.Contains(parte, StringComparison.OrdinalIgnoreCase))
            && !nombreProceso.EndsWith("_EAC", StringComparison.OrdinalIgnoreCase); // RocketLeague_EAC.exe es el juego con su antitrampas

        private static Clasificacion Juego(InfoProceso p, JuegoInstalado instalado, string motivo)
        {
            return new Clasificacion
            {
                EsJuego = true,
                Instalado = instalado,
                Nombre = NombreReal(p, instalado),
                Clave = (instalado?.CarpetaInstalacion ?? CarpetaDelJuego(p.RutaExe) ?? p.RutaExe ?? p.NombreProceso).ToLowerInvariant(),
                Motivo = motivo
            };
        }

        // ===================== NOMBRE REAL =====================

        // Carpetas que no dicen el nombre del juego (ej. "Alan Wake\Binaries\Win64\AlanWake.exe")
        private static readonly HashSet<string> CarpetasGenericas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bin", "bin32", "bin64", "binaries", "win64", "win32", "x64", "x86", "game", "retail", "release", "shipping",
            "launcher", "common", "data", "app", "client", "windows", "wingdk", "dx11", "dx12", "engine", "program files",
            "program files (x86)", "games", "juegos", "steamapps", "epic games", "gog games"
        };

        // Nombres de producto que no son el del juego
        private static readonly string[] ProductosGenericos =
        {
            "unity", "unreal", "launcher", "microsoft", "windows", "game", "bootstrap", "setup", "installer", "crash"
        };

        /// <summary>
        /// Nombre real del juego, por orden de fiabilidad: tienda, propiedades del .exe, carpeta del juego y, al final, el proceso.
        /// </summary>
        public static string NombreReal(InfoProceso p, JuegoInstalado instalado)
        {
            if (!string.IsNullOrWhiteSpace(instalado?.Nombre)) return Limpiar(instalado.Nombre);

            foreach (var candidato in new[] { p?.NombreProducto, p?.Descripcion })
            {
                string limpio = Limpiar(candidato);
                if (EsNombreUtil(limpio)) return limpio;
            }

            string carpeta = CarpetaDelJuego(p?.RutaExe);
            if (carpeta != null) return Limpiar(Path.GetFileName(carpeta));

            return Limpiar(p?.NombreProceso ?? "");
        }

        /// <summary>Primera carpeta hacia arriba desde el .exe que no sea genérica ("Binaries", "Win64"...).</summary>
        public static string CarpetaDelJuego(string rutaExe)
        {
            if (string.IsNullOrEmpty(rutaExe)) return null;
            string dir = Path.GetDirectoryName(rutaExe);
            while (!string.IsNullOrEmpty(dir))
            {
                string nombre = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(nombre)) return null; // raíz de la unidad
                if (!CarpetasGenericas.Contains(nombre)) return dir;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        private static bool EsNombreUtil(string nombre)
        {
            if (string.IsNullOrWhiteSpace(nombre) || nombre.Length < 2) return false;
            string bajo = nombre.ToLowerInvariant();
            if (ProductosGenericos.Any(g => bajo == g || bajo.StartsWith(g + " ") || bajo.EndsWith(" " + g))) return false;
            return bajo.Any(char.IsLetter);
        }

        /// <summary>Quita ™ ® ©, ".exe", "Launcher" del final y espacios sobrantes.</summary>
        public static string Limpiar(string nombre)
        {
            if (string.IsNullOrWhiteSpace(nombre)) return "";
            string s = nombre.Replace("™", "").Replace("®", "").Replace("©", "");
            s = Regex.Replace(s, @"\.exe$", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"[\s_-]+launcher$", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s;
        }
    }
}
