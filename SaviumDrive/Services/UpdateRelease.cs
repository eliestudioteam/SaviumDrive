// Services/UpdateRelease.cs
// Lógica pura sobre una release de GitHub (sin WinUI ni red) para poder probarla desde SaviumDrive.Tests.

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

namespace SaviumDrive.Services
{
    /// <summary>Un archivo adjunto a una release.</summary>
    public class ReleaseAsset
    {
        public string Name { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public long Size { get; set; }
    }

    public static class UpdateRelease
    {
        /// <summary>Lista de hashes que publica el flujo de releases junto al instalador.</summary>
        public const string ChecksumsFileName = "SHA256SUMS.txt";

        /// <summary>Argumento del instalador: espera a que la app se cierre, actualiza sin preguntar y la vuelve a abrir.</summary>
        public const string InstallerUpdateArgument = "/UPDATE";

        /// <summary>
        /// El instalador de la release: el .exe llamado "*-Setup-*.exe" (así lo nombra build.ps1 del instalador).
        /// Si no hay ninguno con ese nombre, el primer .exe, como en las releases antiguas. null si no hay .exe.
        /// </summary>
        public static ReleaseAsset? PickInstaller(IEnumerable<ReleaseAsset> assets)
        {
            var exes = assets
                .Where(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                .ToList();
            return exes.FirstOrDefault(a => a.Name.IndexOf("-Setup-", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? exes.FirstOrDefault();
        }

        public static ReleaseAsset? FindChecksums(IEnumerable<ReleaseAsset> assets) =>
            assets.FirstOrDefault(a => string.Equals(a.Name, ChecksumsFileName, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Busca el hash de un archivo en un SHA256SUMS.txt (formato de sha256sum: "hash  nombre" o "hash *nombre").
        /// Devuelve el hash en minúsculas, o null si el archivo no aparece.
        /// </summary>
        public static string? FindSha256(string? checksumsText, string fileName)
        {
            foreach (string rawLine in (checksumsText ?? "").Split('\n'))
            {
                string line = rawLine.Trim();
                int space = line.IndexOf(' ');
                if (space != 64)
                    continue;

                string name = line.Substring(space).Trim().TrimStart('*');
                if (string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase))
                    return line.Substring(0, 64).ToLowerInvariant();
            }
            return null;
        }

        public static string ComputeSha256(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        }

        /// <summary>
        /// Versión de esta compilación ("1.2.3"), tomada de &lt;Version&gt; en SaviumDrive.csproj.
        /// Es la que se compara con la etiqueta de la release (1.2.3).
        /// </summary>
        public static string NumericVersion(Assembly assembly)
        {
            var version = assembly.GetName().Version ?? new Version(0, 0, 0);
            return $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
        }

        /// <summary>
        /// Versión para mostrar: "1.0.0-beta" en el .csproj se muestra como "1.0.0 Beta".
        /// </summary>
        public static string DisplayVersion(string? informationalVersion)
        {
            string version = (informationalVersion ?? "").Split('+')[0];
            int dash = version.IndexOf('-');
            if (dash < 0 || dash == version.Length - 1)
                return version;

            string suffix = version.Substring(dash + 1);
            return version.Substring(0, dash) + " " + char.ToUpperInvariant(suffix[0]) + suffix.Substring(1);
        }
    }
}
