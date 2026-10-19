// Services/LibraryMerge.cs
// Lógica pura de fusión local/nube (sin WinUI ni Drive) para poder probarla desde SaviumDrive.Tests.
using SaviumDrive.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SaviumDrive.Services
{
    public static class LibraryMerge
    {
        public class Resultado
        {
            /// <summary>La copia de la nube es más reciente y debe sobrescribir la local.</summary>
            public bool UsarNube { get; set; }

            /// <summary>Tras fusionar hay juegos nuevos que hay que guardar (y subir).</summary>
            public bool HayCambios { get; set; }

            /// <summary>Lista final de juegos.</summary>
            public List<GameConfig> Juegos { get; set; }
        }

        public static Resultado Fusionar(List<GameConfig> locales, DateTime fechaLocal,
                                         List<GameConfig> nube, DateTime fechaNube)
        {
            nube ??= new List<GameConfig>();
            bool usarNube = fechaNube > fechaLocal;

            // Si la nube gana, la base es la copia de la nube (la que se acaba de guardar).
            // Fusionar contra la lista local antigua resucitaría juegos borrados en otro PC.
            var juegos = usarNube ? new List<GameConfig>(nube) : new List<GameConfig>(locales ?? new List<GameConfig>());

            bool cambios = false;
            foreach (var jNube in nube)
            {
                if (!juegos.Any(j => j.Id == jNube.Id))
                {
                    juegos.Add(jNube);
                    cambios = true;
                }
            }

            return new Resultado { UsarNube = usarNube, HayCambios = cambios, Juegos = juegos };
        }
    }
}
