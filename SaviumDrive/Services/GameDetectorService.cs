using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Timers;

namespace SaviumDrive.Services
{
    public class GameSession
    {
        public string Name { get; set; }
        public string ExecutablePath { get; set; }
        public DateTime StartTime { get; set; }
    }

    public class GameDetectorService
    {
        // --- P/INVOKE ---
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;

        private Timer _timer;
        private int _activePid = 0;
        private string _activeProcessName = "";
        private string _activeProcessPath = "";

        public event EventHandler<GameSession> GameClosed;

        // Caché de resultados (Qué es juego y qué no)
        private Dictionary<string, bool> _processCache = new Dictionary<string, bool>();

        // 🔥 NUEVO: Caché de procesos fallidos para no reintentar y evitar lag
        private HashSet<int> _failedPids = new HashSet<int>();

        // Lista Negra (Apps comunes que no son juegos)
        private readonly HashSet<string> _blacklist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "explorer", "searchhost", "taskmgr", "saviumdrive", "chrome", "firefox", "msedge", "opera", "brave",
            "discord", "spotify", "code", "devenv", "notepad", "calculator", "lockapp", "teams", "slack",
            "shellexperiencehost", "applicationframehost", "systemsettings", "whatsapp", "telegram",
            "vlc", "mpc-hc64", "potplayer", "photos", "movies", "netflix", "hulu", "primevideo", "plex",
            "obs64", "steam", "epicgameslauncher", "origin", "battlenet", "gog galaxy", "uplay", "xbox",
            "winword", "excel", "powerpoint", "outlook", "photoshop", "illustrator", "premiere",
            "startmenuexperiencehost", "textinputhost", "ctfmon", "widgets"
        };

        // Lista Blanca (Emuladores y exes conocidos)
        private readonly HashSet<string> _whitelistExes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "rpcs3", "yuzu", "cemu", "dolphin", "pcsx2", "xenia", "retroarch", "snes9x", "project64",
            "citra", "ryujinx", "dosbox", "scummvm", "ppsspp", "mame"
        };

        public GameDetectorService()
        {
            _timer = new Timer(4000);
            _timer.Elapsed += CheckForegroundWindow;
        }

        public void Start() { if (!_timer.Enabled) _timer.Start(); }
        public void Stop() { _timer.Stop(); ResetTracking(); }

        private void ResetTracking()
        {
            _activePid = 0;
            _activeProcessName = "";
            _activeProcessPath = "";
        }

        private void CheckForegroundWindow(object sender, ElapsedEventArgs e)
        {
            // 1. MODO VIGILANCIA (Si ya hay juego activo)
            if (_activePid != 0)
            {
                if (!IsProcessRunning(_activePid))
                {
                    FireGameClosed();
                    ResetTracking();
                }
                return;
            }

            // 2. MODO DETECCIÓN
            try
            {
                IntPtr handle = GetForegroundWindow();
                if (handle == IntPtr.Zero) return;

                GetWindowThreadProcessId(handle, out uint pid);
                if (pid == 0 || pid == (uint)_activePid) return;

                // 🔥 OPTIMIZACIÓN: Si este proceso ya falló antes, ignorarlo para no generar excepciones
                if (_failedPids.Contains((int)pid)) return;

                Process process = null;
                try
                {
                    process = Process.GetProcessById((int)pid);
                }
                catch
                {
                    _failedPids.Add((int)pid); // Marcar como fallido
                    return;
                }

                string processName = process.ProcessName;
                if (string.IsNullOrEmpty(processName)) return;

                if (_blacklist.Contains(processName)) return;

                if (_processCache.ContainsKey(processName))
                {
                    if (_processCache[processName]) StartTracking(process);
                    return;
                }

                // Intentar analizar (Capturando errores para no saturar el log)
                bool isGame = false;
                try
                {
                    isGame = AnalyzeHeuristic(process, handle);
                }
                catch
                {
                    // Si falla el análisis, agregar a lista de fallidos para no insistir
                    _failedPids.Add((int)pid);
                    return;
                }

                if (!_processCache.ContainsKey(processName))
                    _processCache.Add(processName, isGame);

                if (isGame) StartTracking(process);
            }
            catch { /* Silencio total */ }
        }

        private bool AnalyzeHeuristic(Process p, IntPtr windowHandle)
        {
            // 1. WHITELIST (Rápido y seguro)
            if (_whitelistExes.Contains(p.ProcessName)) return true;

            // 2. VENTANA (Motores)
            StringBuilder className = new StringBuilder(256);
            GetClassName(windowHandle, className, className.Capacity);
            string wClass = className.ToString();

            string[] gameClasses = {
                "UnrealWindow", "UnityWndClass", "LaunchUnrealUWindowsClient", "GODOT_WINDOW",
                "Gamemaker", "SDL_app", "SFML_Window", "Panda3D"
            };
            if (gameClasses.Any(c => wClass.Contains(c, StringComparison.OrdinalIgnoreCase))) return true;

            // 3. PANTALLA COMPLETA
            if (IsFullscreen(windowHandle)) return true;

            // 4. ARCHIVOS (ADN) - Aquí es donde solían ocurrir las excepciones
            try
            {
                string path = p.MainModule?.FileName; // Esto puede lanzar Win32Exception
                if (!string.IsNullOrEmpty(path))
                {
                    string folder = Path.GetDirectoryName(path);
                    if (Directory.Exists(folder))
                    {
                        string lowerPath = path.ToLower();
                        if (lowerPath.Contains("\\games\\") || lowerPath.Contains("\\juegos\\") || lowerPath.Contains("repack"))
                            return true;

                        string[] gameDlls = {
                            "steam_api", "unityplayer", "galaxy64", "eossdk", "fmod", "wwise",
                            "d3dcompiler", "opengl32", "vulkan", "physx", "havok"
                        };

                        var files = Directory.EnumerateFiles(folder, "*.dll", SearchOption.TopDirectoryOnly);
                        foreach (var f in files)
                        {
                            if (gameDlls.Any(dll => f.ToLower().Contains(dll))) return true;
                        }
                    }
                }
            }
            catch
            {
                // Si no tenemos acceso al módulo, no podemos saber si es juego por archivos.
                // Retornamos falso pero NO marcamos como fallido globalmente, 
                // tal vez en otro momento sí tengamos acceso o se detecte por pantalla completa.
                return false;
            }

            return false;
        }

        private bool IsFullscreen(IntPtr hWnd)
        {
            try
            {
                GetWindowRect(hWnd, out RECT rect);
                int screenWidth = GetSystemMetrics(SM_CXSCREEN);
                int screenHeight = GetSystemMetrics(SM_CYSCREEN);
                return (rect.Right - rect.Left) >= screenWidth && (rect.Bottom - rect.Top) >= screenHeight;
            }
            catch { return false; }
        }

        private bool IsProcessRunning(int pid)
        {
            try
            {
                Process.GetProcessById(pid);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void StartTracking(Process p)
        {
            try
            {
                _activePid = p.Id;
                _activeProcessName = p.ProcessName;
                try { _activeProcessPath = p.MainModule?.FileName; } catch { }
                System.Diagnostics.Debug.WriteLine($"🎮 JUEGO DETECTADO E INICIADO: {_activeProcessName}");
            }
            catch { ResetTracking(); }
        }

        private void FireGameClosed()
        {
            System.Diagnostics.Debug.WriteLine($"🏁 JUEGO CERRADO: {_activeProcessName}");
            GameClosed?.Invoke(this, new GameSession
            {
                Name = _activeProcessName,
                ExecutablePath = _activeProcessPath
            });
        }
    }
}