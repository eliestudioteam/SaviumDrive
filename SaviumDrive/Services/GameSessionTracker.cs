// Services/GameSessionTracker.cs
// Lógica pura (sin WinUI ni procesos reales) para seguir varios juegos a la vez; se prueba desde SaviumDrive.Tests.
using System;
using System.Collections.Generic;
using System.Linq;

namespace SaviumDrive.Services
{
    public class GameSession
    {
        /// <summary>Nombre real del juego (de la tienda, del .exe o de su carpeta).</summary>
        public string Name { get; set; }
        public string ExecutablePath { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        /// <summary>"Steam", "Epic", "GOG" o null si no viene de una tienda.</summary>
        public string Store { get; set; }
        public string SteamAppId { get; set; }
        public string InstallDir { get; set; }
        /// <summary>Clave del detector: con ella se recuerda el juego cuando el usuario lo agrega.</summary>
        public string Clave { get; set; }
        public TimeSpan Duration => EndTime - StartTime;
    }

    /// <summary>Desde el recordatorio: guardar ya el backup de un juego que está en la biblioteca.</summary>
    public sealed class SolicitudGuardarBackup
    {
        public string JuegoId { get; set; }
    }

    /// <summary>
    /// Sigue los juegos abiertos. Un juego puede tener varios procesos (lanzador + juego): se agrupan por clave
    /// y se avisa una sola vez, cuando termina el último. Las sesiones muy cortas (lanzadores, cargas) no avisan.
    /// </summary>
    public sealed class GameSessionTracker
    {
        public static readonly TimeSpan DuracionMinimaPorDefecto = TimeSpan.FromMinutes(1);

        private sealed class Seguimiento
        {
            public GameSession Sesion;
            public readonly HashSet<int> Pids = new HashSet<int>();
        }

        private readonly Dictionary<string, Seguimiento> _juegos = new Dictionary<string, Seguimiento>(StringComparer.OrdinalIgnoreCase);
        private readonly TimeSpan _duracionMinima;

        public GameSessionTracker(TimeSpan? duracionMinima = null)
        {
            _duracionMinima = duracionMinima ?? DuracionMinimaPorDefecto;
        }

        public int JuegosAbiertos => _juegos.Count;

        public bool EstaSiguiendo(int pid) => _juegos.Values.Any(s => s.Pids.Contains(pid));

        /// <summary>Anota un proceso de un juego (si el juego ya estaba abierto, se suma a su sesión).</summary>
        public void Registrar(string clave, int pid, GameSession datos, DateTime ahora)
        {
            if (string.IsNullOrEmpty(clave)) return;
            if (!_juegos.TryGetValue(clave, out var seguimiento))
            {
                datos.StartTime = ahora;
                seguimiento = new Seguimiento { Sesion = datos };
                _juegos[clave] = seguimiento;
            }
            seguimiento.Pids.Add(pid);
        }

        /// <summary>
        /// Quita los procesos que ya terminaron y devuelve las sesiones cerradas que duraron lo suficiente.
        /// </summary>
        public List<GameSession> Revisar(Func<int, bool> sigueAbierto, DateTime ahora)
        {
            var cerradas = new List<GameSession>();
            foreach (var clave in _juegos.Keys.ToList())
            {
                var seguimiento = _juegos[clave];
                seguimiento.Pids.RemoveWhere(pid => !sigueAbierto(pid));
                if (seguimiento.Pids.Count > 0) continue;

                _juegos.Remove(clave);
                seguimiento.Sesion.EndTime = ahora;
                if (seguimiento.Sesion.Duration >= _duracionMinima) cerradas.Add(seguimiento.Sesion);
            }
            return cerradas;
        }

        public void Limpiar() => _juegos.Clear();
    }
}
