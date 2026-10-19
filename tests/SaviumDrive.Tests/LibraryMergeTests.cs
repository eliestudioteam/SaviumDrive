using SaviumDrive.Models;
using SaviumDrive.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SaviumDrive.Tests
{
    public class LibraryMergeTests
    {
        private static readonly DateTime Antes = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime Despues = Antes.AddHours(1);

        private static GameConfig Juego(string id) => new GameConfig { Id = id, Name = id };

        private static List<string> Ids(LibraryMerge.Resultado r) => r.Juegos.Select(j => j.Id).OrderBy(x => x).ToList();

        [Fact]
        public void NubeMasReciente_NoResucitaJuegosBorradosEnOtroPC()
        {
            // Local: A y B. En otro PC se borró B, así que la nube (más reciente) solo tiene A.
            var locales = new List<GameConfig> { Juego("A"), Juego("B") };
            var nube = new List<GameConfig> { Juego("A") };

            var r = LibraryMerge.Fusionar(locales, Antes, nube, Despues);

            Assert.True(r.UsarNube);
            Assert.False(r.HayCambios);
            Assert.Equal(new List<string> { "A" }, Ids(r));
        }

        [Fact]
        public void NubeMasReciente_TraeJuegosNuevosDeOtroPC()
        {
            var locales = new List<GameConfig> { Juego("A") };
            var nube = new List<GameConfig> { Juego("A"), Juego("C") };

            var r = LibraryMerge.Fusionar(locales, Antes, nube, Despues);

            Assert.True(r.UsarNube);
            Assert.False(r.HayCambios); // la copia de la nube ya se guardó tal cual, no hay que resubir
            Assert.Equal(new List<string> { "A", "C" }, Ids(r));
        }

        [Fact]
        public void LocalMasReciente_AnadeJuegosQueSoloEstanEnLaNube()
        {
            var locales = new List<GameConfig> { Juego("A"), Juego("B") };
            var nube = new List<GameConfig> { Juego("A"), Juego("C") };

            var r = LibraryMerge.Fusionar(locales, Despues, nube, Antes);

            Assert.False(r.UsarNube);
            Assert.True(r.HayCambios);
            Assert.Equal(new List<string> { "A", "B", "C" }, Ids(r));
        }

        [Fact]
        public void LocalMasReciente_SinNovedades_NoHayCambios()
        {
            var locales = new List<GameConfig> { Juego("A"), Juego("B") };
            var nube = new List<GameConfig> { Juego("A") };

            var r = LibraryMerge.Fusionar(locales, Despues, nube, Antes);

            Assert.False(r.UsarNube);
            Assert.False(r.HayCambios);
            Assert.Equal(new List<string> { "A", "B" }, Ids(r));
        }

        [Fact]
        public void NubeSinJuegos_NoFalla()
        {
            var r = LibraryMerge.Fusionar(new List<GameConfig> { Juego("A") }, Despues, null, Antes);

            Assert.False(r.HayCambios);
            Assert.Equal(new List<string> { "A" }, Ids(r));
        }
    }
}
