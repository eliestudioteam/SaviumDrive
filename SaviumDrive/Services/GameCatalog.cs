// Services/GameCatalog.cs
// Juegos instalados en este PC según Steam, Epic y GOG, y las carpetas de los propios lanzadores
// (sus procesos —como steamwebhelper— no son juegos).
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SaviumDrive.Services
{
    public sealed class GameCatalog
    {
        public List<JuegoInstalado> Juegos { get; } = new List<JuegoInstalado>();

        /// <summary>Carpetas de los lanzadores (Steam, Epic, GOG Galaxy...): nada de ahí dentro es un juego, salvo las bibliotecas.</summary>
        public List<string> CarpetasDeLanzadores { get; } = new List<string>();

        /// <summary>Bibliotecas de juegos dentro de las carpetas de lanzadores (ej. Steam\steamapps).</summary>
        public List<string> CarpetasDeBibliotecas { get; } = new List<string>();

        public DateTime CargadoEn { get; private set; }

        /// <summary>Lee lo que tenga cada tienda; si una no está instalada o falla, se sigue con las demás.</summary>
        public static GameCatalog Cargar()
        {
            var catalogo = new GameCatalog();
            Intentar(catalogo.CargarSteam);
            Intentar(catalogo.CargarEpic);
            Intentar(catalogo.CargarGog);
            Intentar(catalogo.AgregarOtrosLanzadores);
            catalogo.CargadoEn = DateTime.UtcNow;
            return catalogo;
        }

        private static void Intentar(Action accion)
        {
            try { accion(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"⚠️ Catálogo de juegos: {ex.Message}"); }
        }

        private void CargarSteam()
        {
            string raiz = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
                          ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
            if (string.IsNullOrEmpty(raiz)) return;
            raiz = Path.GetFullPath(raiz.Replace('/', '\\'));
            if (!Directory.Exists(raiz)) return;

            CarpetasDeLanzadores.Add(raiz);

            var bibliotecas = new List<string> { raiz };
            string vdf = Path.Combine(raiz, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf)) bibliotecas.AddRange(GameStores.BibliotecasSteam(File.ReadAllText(vdf)));

            foreach (var biblioteca in bibliotecas.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string steamapps = Path.Combine(biblioteca, "steamapps");
                if (!Directory.Exists(steamapps)) continue;
                CarpetasDeBibliotecas.Add(steamapps);

                foreach (var acf in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
                {
                    try
                    {
                        var juego = GameStores.LeerManifestSteam(File.ReadAllText(acf), steamapps);
                        if (juego != null) Juegos.Add(juego);
                    }
                    catch (IOException) { }
                }
            }
        }

        private void CargarEpic()
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string manifiestos = Path.Combine(programData, "Epic", "EpicGamesLauncher", "Data", "Manifests");
            if (Directory.Exists(manifiestos))
            {
                foreach (var item in Directory.EnumerateFiles(manifiestos, "*.item"))
                {
                    try
                    {
                        var juego = GameStores.LeerManifestEpic(File.ReadAllText(item));
                        if (juego != null) Juegos.Add(juego);
                    }
                    catch (IOException) { }
                }
            }

            string lanzador = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Epic Games", "Launcher");
            if (Directory.Exists(lanzador)) CarpetasDeLanzadores.Add(lanzador);
        }

        private void CargarGog()
        {
            using var juegos = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
            if (juegos != null)
            {
                foreach (var id in juegos.GetSubKeyNames())
                {
                    using var clave = juegos.OpenSubKey(id);
                    string nombre = clave?.GetValue("gameName") as string;
                    string carpeta = clave?.GetValue("path") as string;
                    if (!string.IsNullOrWhiteSpace(nombre) && !string.IsNullOrWhiteSpace(carpeta))
                        Juegos.Add(new JuegoInstalado { Nombre = nombre.Trim(), CarpetaInstalacion = carpeta, Tienda = "GOG" });
                }
            }

            string galaxy = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\GOG.com\GalaxyClient\paths", "client", null) as string;
            if (!string.IsNullOrEmpty(galaxy) && Directory.Exists(galaxy)) CarpetasDeLanzadores.Add(galaxy);
        }

        private void AgregarOtrosLanzadores()
        {
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            foreach (var ruta in new[]
            {
                Path.Combine(pf86, "Ubisoft", "Ubisoft Game Launcher"),
                Path.Combine(pf, "Electronic Arts", "EA Desktop"),
                Path.Combine(pf86, "Battle.net"),
                Path.Combine(pf86, "Origin"),
            })
            {
                if (Directory.Exists(ruta)) CarpetasDeLanzadores.Add(ruta);
            }
        }
    }
}
