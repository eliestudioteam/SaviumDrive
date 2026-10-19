using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace SaviumDrive.Models
{
    /// <summary>
    /// Una versión (backup independiente) de un juego.
    /// En Drive, <see cref="DriveFolderId"/> es la carpeta que contiene la carpeta de partidas:
    /// V1 usa la carpeta raíz del juego (compatible con backups antiguos) y las demás "Version_N".
    /// </summary>
    public class GameVersion
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public int Number { get; set; }
        public string Description { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime LastBackup { get; set; }
        public string DriveFolderId { get; set; }

        /// <summary>El backup está en un solo .zip (carpetas con muchos archivos) en vez de archivo por archivo.</summary>
        public bool Comprimido { get; set; }

        /// <summary>Huella del contenido subido (solo en versiones comprimidas): si no cambia, no se vuelve a subir.</summary>
        public string HuellaContenido { get; set; }

        [JsonIgnore]
        public string Label => $"V{Number}";

        [JsonIgnore]
        public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    }

    public class GameConfig
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; }

        /// <summary>
        /// Carpeta de partidas original (la del PC donde se agregó el juego). Su nombre es el que
        /// se usa en Drive en todos los equipos; para trabajar en disco usar <see cref="RutaEnEstePc"/>.
        /// </summary>
        public string LocalPath { get; set; }

        /// <summary>Carpeta de partidas elegida en cada equipo (clave: id del equipo).</summary>
        public Dictionary<string, string> RutasPorEquipo { get; set; } = new Dictionary<string, string>();

        /// <summary>Id de este equipo (lo asigna la app al iniciar). Así cada PC usa su propia carpeta.</summary>
        public static string EquipoActual { get; set; }

        /// <summary>Carpeta de partidas en este PC: la elegida aquí o, si no hay, la original.</summary>
        [JsonIgnore]
        public string RutaEnEstePc =>
            EquipoActual != null && RutasPorEquipo != null &&
            RutasPorEquipo.TryGetValue(EquipoActual, out var ruta) && !string.IsNullOrEmpty(ruta)
                ? ruta
                : LocalPath;

        /// <summary>Nombre de la carpeta del backup en Drive (igual en todos los equipos).</summary>
        [JsonIgnore]
        public string NombreCarpetaBackup =>
            string.IsNullOrEmpty(LocalPath) ? "" : System.IO.Path.GetFileName(LocalPath.TrimEnd('\\', '/'));

        /// <summary>Elige la carpeta de partidas para este PC sin tocar la de los demás equipos.</summary>
        public void AsignarRutaEnEstePc(string ruta)
        {
            RutasPorEquipo ??= new Dictionary<string, string>();
            RutasPorEquipo[EquipoActual ?? ""] = ruta;
        }

        /// <summary>Carpeta raíz del juego en Drive (contiene todas sus versiones).</summary>
        public string DriveFolderId { get; set; }
        public string CoverUrl { get; set; }

        /// <summary>Último backup de cualquier versión (para "actividad reciente").</summary>
        public DateTime LastBackup { get; set; }

        public List<GameVersion> Versions { get; set; } = new List<GameVersion>();
        public string ActiveVersionId { get; set; }

        /// <summary>Número más alto usado alguna vez: los números no se reutilizan al borrar.</summary>
        public int LastVersionNumber { get; set; }

        /// <summary>La carpeta de partidas no existe en este PC (hay que elegirla).</summary>
        [JsonIgnore]
        public bool NeedsRelink => string.IsNullOrEmpty(RutaEnEstePc) || !System.IO.Directory.Exists(RutaEnEstePc);

        [JsonIgnore]
        public GameVersion ActiveVersion =>
            Versions?.FirstOrDefault(v => v.Id == ActiveVersionId)
            ?? Versions?.OrderBy(v => v.Number).FirstOrDefault();

        /// <summary>Nombre con la versión activa, ej. "MINECRAFT V1".</summary>
        [JsonIgnore]
        public string DisplayName => ActiveVersion != null ? $"{Name} {ActiveVersion.Label}" : Name;

        [JsonIgnore]
        public bool HasMultipleVersions => Versions != null && Versions.Count > 1;

        [JsonIgnore]
        public List<GameVersion> OrderedVersions => (Versions ?? new List<GameVersion>()).OrderBy(v => v.Number).ToList();

        [JsonIgnore]
        public int NextVersionNumber
        {
            get
            {
                int maxActual = Versions != null && Versions.Count > 0 ? Versions.Max(v => v.Number) : 0;
                return Math.Max(LastVersionNumber, maxActual) + 1;
            }
        }

        /// <summary>
        /// Convierte un juego guardado antes de existir las versiones: su backup pasa a ser V1.
        /// El Id de V1 es determinista para que todos los PCs migren igual. Devuelve true si cambió algo.
        /// </summary>
        public bool EnsureVersions()
        {
            bool cambios = false;
            Versions ??= new List<GameVersion>();

            if (Versions.Count == 0)
            {
                Versions.Add(new GameVersion
                {
                    Id = $"{Id}-v1",
                    Number = 1,
                    CreatedAt = LastBackup,
                    LastBackup = LastBackup,
                    DriveFolderId = DriveFolderId
                });
                cambios = true;
            }

            int maxActual = Versions.Max(v => v.Number);
            if (LastVersionNumber < maxActual)
            {
                LastVersionNumber = maxActual;
                cambios = true;
            }

            if (string.IsNullOrEmpty(ActiveVersionId) || !Versions.Any(v => v.Id == ActiveVersionId))
            {
                ActiveVersionId = Versions.OrderBy(v => v.Number).First().Id;
                cambios = true;
            }

            return cambios;
        }

        public GameVersion FindVersion(string versionId) => Versions?.FirstOrDefault(v => v.Id == versionId);

        // --- ESTA ES LA PROPIEDAD QUE TE FALTA Y EVITA EL CRASH ---
        [JsonIgnore]
        public string SafeCoverUrl
        {
            get
            {
                // Si la URL está vacía, devolvemos el logo local para que no falle
                if (string.IsNullOrEmpty(CoverUrl))
                {
                    return "ms-appx:///Assets/StoreLogo.png";
                }
                return CoverUrl;
            }
        }
    }
}
