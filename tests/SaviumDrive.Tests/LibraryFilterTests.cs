using SaviumDrive.Models;
using SaviumDrive.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SaviumDrive.Tests
{
    public class LibraryFilterTests
    {
        private static readonly DateTime Hoy = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

        private static List<GameConfig> Biblioteca() => new List<GameConfig>
        {
            new GameConfig { Name = "Minecraft", LastBackup = Hoy.AddDays(-3) },
            new GameConfig { Name = "Elden Ring", LastBackup = Hoy },
            new GameConfig { Name = "Pokémon Leyendas", LastBackup = Hoy.AddDays(-1) },
            new GameConfig { Name = "age of empires", LastBackup = Hoy.AddDays(-10) },
        };

        private static string[] Nombres(IEnumerable<GameConfig> juegos) => juegos.Select(j => j.Name).ToArray();

        [Fact]
        public void SinBusqueda_OrdenaPorNombreSinDistinguirMayusculas()
        {
            Assert.Equal(new[] { "age of empires", "Elden Ring", "Minecraft", "Pokémon Leyendas" },
                Nombres(LibraryFilter.Aplicar(Biblioteca(), "", OrdenBiblioteca.NombreAZ)));
        }

        [Fact]
        public void OrdenNombreZA()
        {
            Assert.Equal(new[] { "Pokémon Leyendas", "Minecraft", "Elden Ring", "age of empires" },
                Nombres(LibraryFilter.Aplicar(Biblioteca(), null, OrdenBiblioteca.NombreZA)));
        }

        [Fact]
        public void OrdenUltimoBackup_ElMasRecientePrimero()
        {
            Assert.Equal(new[] { "Elden Ring", "Pokémon Leyendas", "Minecraft", "age of empires" },
                Nombres(LibraryFilter.Aplicar(Biblioteca(), "", OrdenBiblioteca.UltimoBackup)));
        }

        [Theory]
        [InlineData("ring", "Elden Ring")]
        [InlineData("ELDEN", "Elden Ring")]
        [InlineData("pokemon", "Pokémon Leyendas")]   // sin acento
        [InlineData("Pokémon", "Pokémon Leyendas")]
        [InlineData("  craft ", "Minecraft")]          // espacios alrededor
        [InlineData("of emp", "age of empires")]       // varias palabras
        public void Busqueda_SinDistinguirMayusculasNiAcentos(string texto, string esperado)
        {
            Assert.Equal(new[] { esperado }, Nombres(LibraryFilter.Aplicar(Biblioteca(), texto, OrdenBiblioteca.NombreAZ)));
        }

        [Fact]
        public void Busqueda_SinCoincidencias_ListaVacia()
        {
            Assert.Empty(LibraryFilter.Aplicar(Biblioteca(), "zelda", OrdenBiblioteca.NombreAZ));
        }

        [Fact]
        public void Busqueda_ToleraNulosYNombresVacios()
        {
            var juegos = new List<GameConfig> { null, new GameConfig { Name = null }, new GameConfig { Name = "Minecraft" } };

            Assert.Equal(2, LibraryFilter.Aplicar(juegos, "", OrdenBiblioteca.NombreAZ).Count);
            Assert.Single(LibraryFilter.Aplicar(juegos, "mine", OrdenBiblioteca.NombreAZ));
            Assert.Empty(LibraryFilter.Aplicar(null, "x", OrdenBiblioteca.NombreAZ));
        }

        [Theory]
        [InlineData("UltimoBackup", OrdenBiblioteca.UltimoBackup)]
        [InlineData("NombreZA", OrdenBiblioteca.NombreZA)]
        [InlineData("desconocido", OrdenBiblioteca.NombreAZ)]
        [InlineData(null, OrdenBiblioteca.NombreAZ)]
        public void DesdeTexto_ValorGuardadoEnLaConfiguracion(string valor, OrdenBiblioteca esperado)
        {
            Assert.Equal(esperado, LibraryFilter.DesdeTexto(valor));
        }
    }
}
