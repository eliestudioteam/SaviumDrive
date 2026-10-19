// Services/ZipBackup.cs
// Lógica pura (sin WinUI ni Drive) para backups comprimidos en .zip; se prueba desde SaviumDrive.Tests.
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SaviumDrive.Services
{
    public static class ZipBackup
    {
        /// <summary>A partir de cuántos archivos conviene subir un solo .zip en vez de miles de peticiones.</summary>
        public const int UmbralArchivos = 100;

        public static string NombreZip(string carpetaLocal) => new DirectoryInfo(carpetaLocal).Name + ".zip";

        public static int ContarArchivos(string carpeta) =>
            Directory.Exists(carpeta) ? Directory.EnumerateFiles(carpeta, "*", SearchOption.AllDirectories).Count() : 0;

        public static bool ConvieneComprimir(string carpeta) => ContarArchivos(carpeta) >= UmbralArchivos;

        /// <summary>
        /// Comprime el contenido de la carpeta (sin la carpeta raíz). Lee con FileShare.ReadWrite porque
        /// el juego puede tener los archivos abiertos, y conserva fechas y carpetas vacías.
        /// </summary>
        public static void Comprimir(string carpeta, string zipDestino)
        {
            if (File.Exists(zipDestino)) File.Delete(zipDestino);

            using var zip = ZipFile.Open(zipDestino, ZipArchiveMode.Create);

            foreach (var dir in Directory.EnumerateDirectories(carpeta, "*", SearchOption.AllDirectories))
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    zip.CreateEntry(RutaEntrada(carpeta, dir) + "/");
            }

            foreach (var archivo in Directory.EnumerateFiles(carpeta, "*", SearchOption.AllDirectories))
            {
                var entrada = zip.CreateEntry(RutaEntrada(carpeta, archivo), CompressionLevel.Optimal);
                entrada.LastWriteTime = AFechaZip(File.GetLastWriteTime(archivo));

                using var origen = new FileStream(archivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var destino = entrada.Open();
                origen.CopyTo(destino);
            }
        }

        /// <summary>Extrae el .zip en la carpeta (que debe estar vacía). .NET bloquea rutas fuera del destino.</summary>
        public static void Extraer(string zip, string carpetaDestino)
        {
            Directory.CreateDirectory(carpetaDestino);
            ZipFile.ExtractToDirectory(zip, carpetaDestino, overwriteFiles: false);
        }

        /// <summary>
        /// Huella del contenido (rutas, tamaños y MD5). Si no cambia, el .zip en Drive ya está al día
        /// y no hace falta volver a subirlo.
        /// </summary>
        public static string HuellaCarpeta(string carpeta)
        {
            var lineas = Directory.EnumerateFiles(carpeta, "*", SearchOption.AllDirectories)
                .Select(archivo => (ruta: RutaEntrada(carpeta, archivo).ToLowerInvariant(), archivo))
                .OrderBy(x => x.ruta, StringComparer.Ordinal)
                .Select(x => $"{x.ruta}|{new FileInfo(x.archivo).Length}|{Md5Hex(x.archivo)}");

            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", lineas)))).ToLowerInvariant();
        }

        private static string RutaEntrada(string carpeta, string ruta) =>
            Path.GetRelativePath(carpeta, ruta).Replace('\\', '/');

        private static string Md5Hex(string ruta)
        {
            using var md5 = MD5.Create();
            using var stream = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Convert.ToHexString(md5.ComputeHash(stream)).ToLowerInvariant();
        }

        // El formato zip solo admite fechas entre 1980 y 2107
        private static DateTimeOffset AFechaZip(DateTime fecha)
        {
            var minimo = new DateTime(1980, 1, 2);
            return fecha < minimo ? minimo : fecha;
        }
    }
}
