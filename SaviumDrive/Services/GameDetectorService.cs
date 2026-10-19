using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace SaviumDrive.Services
{
    /// <summary>
    /// Detecta juegos en primer plano y avisa (GameClosed) cuando se cierran.
    /// - Reconoce los juegos instalados de Steam, Epic y GOG y usa su nombre real.
    /// - Ignora los procesos de los lanzadores (steamwebhelper, Epic, Galaxy...).
    /// - Sigue varios juegos a la vez y agrupa los procesos de cada uno (lanzador + juego).
    /// - No avisa de sesiones de menos de un minuto.
    /// </summary>
    public class GameDetectorService
    {
        // --- P/INVOKE ---
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, StringBuilder exeName, ref int size);
        [DllImport("kernel32.dll")]
        private static extern bool GetExitCodeProcess(IntPtr hProcess, out uint exitCode);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const uint STILL_ACTIVE = 259;
        private const uint MONITOR_DEFAULTTONEAREST = 2;

        private readonly TimeSpan IntervaloRevision;
        private static readonly TimeSpan CaducidadCatalogo = TimeSpan.FromMinutes(10);
        private const int MaximoEnCache = 300;

        private readonly System.Threading.Timer _timer;
        private readonly object _bloqueo = new object();
        private readonly GameSessionTracker _tracker;
        private bool _activo;

        // Clasificación por ejecutable (para no volver a analizar cada 4 segundos)
        private readonly Dictionary<string, Clasificacion> _cache = new Dictionary<string, Clasificacion>(StringComparer.OrdinalIgnoreCase);
        private GameCatalog _catalogo;

        public event EventHandler<GameSession> GameClosed;

        public GameDetectorService() : this(TimeSpan.FromSeconds(4), GameSessionTracker.DuracionMinimaPorDefecto) { }

        /// <summary>Con intervalo y duración mínima propios (para pruebas).</summary>
        public GameDetectorService(TimeSpan intervalo, TimeSpan duracionMinima)
        {
            IntervaloRevision = intervalo;
            _tracker = new GameSessionTracker(duracionMinima);
            _timer = new System.Threading.Timer(_ => Revisar(), null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>Juegos abiertos que se están siguiendo ahora mismo.</summary>
        public int JuegosAbiertos { get { lock (_bloqueo) return _tracker.JuegosAbiertos; } }

        public void Start()
        {
            lock (_bloqueo)
            {
                if (_activo) return;
                _activo = true;
                _timer.Change(IntervaloRevision, Timeout.InfiniteTimeSpan);
            }
        }

        public void Stop()
        {
            lock (_bloqueo)
            {
                _activo = false;
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
                _tracker.Limpiar();
            }
        }

        private void Revisar()
        {
            List<GameSession> cerradas;
            lock (_bloqueo)
            {
                if (!_activo) return;
                try
                {
                    // 1. ¿Terminó algún juego? (todos sus procesos cerrados)
                    cerradas = _tracker.Revisar(ProcesoSigueAbierto, DateTime.UtcNow);

                    // 2. ¿Hay un juego en primer plano?
                    DetectarPrimerPlano();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"⚠️ Detector de juegos: {ex.Message}");
                    cerradas = new List<GameSession>();
                }
                finally
                {
                    // Sin solapamientos: la siguiente revisión empieza cuando termina esta
                    if (_activo) _timer.Change(IntervaloRevision, Timeout.InfiniteTimeSpan);
                }
            }

            foreach (var sesion in cerradas)
            {
                Debug.WriteLine($"🏁 JUEGO CERRADO: {sesion.Name} ({sesion.Duration:hh\\:mm\\:ss})");
                GameClosed?.Invoke(this, sesion);
            }
        }

        private void DetectarPrimerPlano()
        {
            IntPtr ventana = GetForegroundWindow();
            if (ventana == IntPtr.Zero) return;
            GetWindowThreadProcessId(ventana, out uint pidU);
            int pid = (int)pidU;
            if (pid == 0 || pid == Environment.ProcessId || _tracker.EstaSiguiendo(pid)) return;

            string exe = RutaDelProceso(pid);
            string nombreProceso = string.IsNullOrEmpty(exe) ? NombreDelProceso(pid) : Path.GetFileNameWithoutExtension(exe);
            if (string.IsNullOrEmpty(nombreProceso)) return;

            // Con un intérprete (javaw...) cada juego se distingue por el título de su ventana
            string titulo = GameClassifier.EsInterprete(nombreProceso) ? TituloDeVentana(ventana) : null;
            if (_catalogo != null && _catalogo.VersionAprendidos != LearnedGames.Version) _cache.Clear();
            string claveCache = (exe ?? nombreProceso) + (titulo != null ? "|" + GameClassifier.NombreDesdeTitulo(titulo) : "");
            if (!_cache.TryGetValue(claveCache, out var clasificacion))
            {
                clasificacion = Clasificar(ventana, exe, nombreProceso, titulo);
                if (_cache.Count >= MaximoEnCache) _cache.Clear();
                _cache[claveCache] = clasificacion;
                Debug.WriteLine($"🔎 {nombreProceso}: {(clasificacion.EsJuego ? "JUEGO" : "no es juego")} ({clasificacion.Motivo})");
            }

            if (!clasificacion.EsJuego) return;

            _tracker.Registrar(clasificacion.Clave, pid, new GameSession
            {
                Name = clasificacion.Nombre,
                ExecutablePath = exe,
                Store = clasificacion.Instalado?.Tienda,
                SteamAppId = clasificacion.Instalado?.SteamAppId,
                InstallDir = clasificacion.Instalado?.CarpetaInstalacion
                             ?? (GameClassifier.EsInterprete(nombreProceso) ? null : GameClassifier.CarpetaDelJuego(exe)),
                Clave = clasificacion.Clave
            }, DateTime.UtcNow);
        }

        private Clasificacion Clasificar(IntPtr ventana, string exe, string nombreProceso, string titulo)
        {
            // Recargar el catálogo de vez en cuando (juegos instalados o desinstalados) o si el usuario agregó un juego
            if (_catalogo == null || DateTime.UtcNow - _catalogo.CargadoEn > CaducidadCatalogo
                || _catalogo.VersionAprendidos != LearnedGames.Version)
            {
                _catalogo = GameCatalog.Cargar();
                _cache.Clear(); // un juego recién instalado ya no debe seguir como "no es juego"
            }

            var info = new InfoProceso
            {
                NombreProceso = nombreProceso,
                RutaExe = exe,
                ClaseVentana = ClaseDeVentana(ventana),
                PantallaCompleta = EsPantallaCompleta(ventana),
                EsAppElectron = EsElectron(exe),
                TituloVentana = titulo
            };

            if (!string.IsNullOrEmpty(exe))
            {
                try
                {
                    var version = FileVersionInfo.GetVersionInfo(exe);
                    info.NombreProducto = version.ProductName;
                    info.Descripcion = version.FileDescription;
                }
                catch { }
            }

            return GameClassifier.Clasificar(info, _catalogo, DllsDeCarpeta);
        }

        // ===================== AYUDANTES DE WINDOWS =====================

        /// <summary>Ruta del .exe con permisos mínimos (funciona también con procesos que no se pueden abrir del todo).</summary>
        private static string RutaDelProceso(int pid)
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int tam = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref tam) ? sb.ToString(0, tam) : null;
            }
            finally { CloseHandle(h); }
        }

        private static string NombreDelProceso(int pid)
        {
            try { using var p = Process.GetProcessById(pid); return p.ProcessName; }
            catch { return null; }
        }

        /// <summary>True si el proceso sigue abierto (sin crear objetos Process cada vez).</summary>
        private static bool ProcesoSigueAbierto(int pid)
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return false;
            try { return GetExitCodeProcess(h, out uint codigo) && codigo == STILL_ACTIVE; }
            finally { CloseHandle(h); }
        }

        private static string TituloDeVentana(IntPtr ventana)
        {
            var sb = new StringBuilder(256);
            return GetWindowText(ventana, sb, sb.Capacity) > 0 ? sb.ToString() : null;
        }

        private static string ClaseDeVentana(IntPtr ventana)
        {
            var sb = new StringBuilder(256);
            return GetClassName(ventana, sb, sb.Capacity) > 0 ? sb.ToString() : "";
        }

        /// <summary>La ventana cubre todo su monitor (vale también con varios monitores).</summary>
        private static bool EsPantallaCompleta(IntPtr ventana)
        {
            if (!GetWindowRect(ventana, out RECT r)) return false;
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(MonitorFromWindow(ventana, MONITOR_DEFAULTTONEAREST), ref info)) return false;
            var m = info.rcMonitor;
            return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
        }

        private static bool EsElectron(string exe)
        {
            try
            {
                string carpeta = Path.GetDirectoryName(exe ?? "");
                return !string.IsNullOrEmpty(carpeta) && File.Exists(Path.Combine(carpeta, "resources", "app.asar"));
            }
            catch { return false; }
        }

        private static IEnumerable<string> DllsDeCarpeta(string carpeta)
        {
            try { return Directory.EnumerateFiles(carpeta, "*.dll", SearchOption.TopDirectoryOnly).Select(Path.GetFileName).ToList(); }
            catch { return Enumerable.Empty<string>(); }
        }
    }
}
