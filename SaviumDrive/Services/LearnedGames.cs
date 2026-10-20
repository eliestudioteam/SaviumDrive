// Services/LearnedGames.cs
// Juegos que el usuario agregó desde el aviso del detector: la próxima vez se reconocen con el nombre que eligió.
// Sirve sobre todo para juegos sin tienda (Minecraft, Hytale, juegos descargados de una web...).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace SaviumDrive.Services
{
    public static class LearnedGames
    {
        private static readonly object _bloqueo = new object();
        private static int _version;

        /// <summary>Cambia cada vez que se aprende un juego (el detector recarga su catálogo).</summary>
        public static int Version => Volatile.Read(ref _version);

        /// <summary>Archivo donde se guardan; se puede cambiar en las pruebas.</summary>
        public static string Archivo { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SaviumDrive", "juegos-aprendidos.json");

        /// <summary>Juegos aprendidos: clave del detector (carpeta del juego o "titulo:...") y nombre elegido por el usuario.</summary>
        public static Dictionary<string, string> Leer()
        {
            lock (_bloqueo)
            {
                try
                {
                    if (File.Exists(Archivo))
                    {
                        var datos = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Archivo));
                        if (datos != null) return new Dictionary<string, string>(datos, StringComparer.OrdinalIgnoreCase);
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Juegos aprendidos: {ex.Message}"); }
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>Recuerda (o actualiza) el nombre de un juego. Lo llama la biblioteca al agregarlo desde el aviso.</summary>
        public static void Aprender(string clave, string nombre)
        {
            if (string.IsNullOrWhiteSpace(clave) || string.IsNullOrWhiteSpace(nombre)) return;
            lock (_bloqueo)
            {
                var datos = Leer();
                datos[clave] = nombre.Trim();
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Archivo));
                    File.WriteAllText(Archivo, JsonSerializer.Serialize(datos, new JsonSerializerOptions { WriteIndented = true }));
                    Interlocked.Increment(ref _version);
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Juegos aprendidos: {ex.Message}"); }
            }
        }
    }
}
