using System;
using Newtonsoft.Json;

namespace SaviumDrive.Models
{
    public class GameConfig
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; }
        public string LocalPath { get; set; }
        public string DriveFolderId { get; set; }
        public string CoverUrl { get; set; }
        public DateTime LastBackup { get; set; }

        public bool NeedsRelink => string.IsNullOrEmpty(LocalPath) || !System.IO.Directory.Exists(LocalPath);

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