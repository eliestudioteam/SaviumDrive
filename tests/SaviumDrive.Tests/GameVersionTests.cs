using Newtonsoft.Json;
using SaviumDrive.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SaviumDrive.Tests
{
    public class GameVersionTests
    {
        private static GameConfig JuegoAntiguo() => new GameConfig
        {
            Id = "juego-1",
            Name = "MINECRAFT",
            LocalPath = @"C:\Saves\Minecraft",
            DriveFolderId = "raiz-drive",
            LastBackup = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc)
        };

        private static GameVersion Version(int numero, string id = null) => new GameVersion
        {
            Id = id ?? $"v{numero}",
            Number = numero,
            DriveFolderId = $"carpeta-v{numero}"
        };

        // --- Migración de backups anteriores a las versiones ---

        [Fact]
        public void EnsureVersions_JuegoAntiguo_CreaV1EnLaCarpetaRaiz()
        {
            var juego = JuegoAntiguo();

            bool cambio = juego.EnsureVersions();

            Assert.True(cambio);
            var v1 = Assert.Single(juego.Versions);
            Assert.Equal(1, v1.Number);
            Assert.Equal("raiz-drive", v1.DriveFolderId);
            Assert.Equal(juego.LastBackup, v1.LastBackup);
            Assert.Equal(v1.Id, juego.ActiveVersionId);
            Assert.Equal(1, juego.LastVersionNumber);
        }

        [Fact]
        public void EnsureVersions_IdDeV1EsIgualEnTodosLosPCs()
        {
            // Dos PCs migran el mismo juego por separado: deben generar la misma V1
            var pcA = JuegoAntiguo();
            var pcB = JuegoAntiguo();

            pcA.EnsureVersions();
            pcB.EnsureVersions();

            Assert.Equal(pcA.Versions[0].Id, pcB.Versions[0].Id);
            Assert.Equal(pcA.ActiveVersionId, pcB.ActiveVersionId);
        }

        [Fact]
        public void EnsureVersions_YaMigrado_NoCambiaNada()
        {
            var juego = JuegoAntiguo();
            juego.EnsureVersions();

            Assert.False(juego.EnsureVersions());
            Assert.Single(juego.Versions);
        }

        [Fact]
        public void EnsureVersions_VersionActivaInexistente_UsaLaPrimera()
        {
            var juego = JuegoAntiguo();
            juego.Versions = new List<GameVersion> { Version(2), Version(1) };
            juego.ActiveVersionId = "borrada";

            juego.EnsureVersions();

            Assert.Equal("v1", juego.ActiveVersionId);
        }

        [Fact]
        public void JsonAntiguo_SinVersiones_SeMigraAlCargar()
        {
            // Formato de games.json antes de existir las versiones
            string json = "{\"Id\":\"juego-1\",\"Name\":\"MINECRAFT\",\"LocalPath\":\"C:\\\\Saves\",\"DriveFolderId\":\"raiz-drive\",\"CoverUrl\":null,\"LastBackup\":\"2026-05-01T12:00:00Z\"}";

            var juego = JsonConvert.DeserializeObject<GameConfig>(json);
            juego.EnsureVersions();

            Assert.Equal("MINECRAFT V1", juego.DisplayName);
            Assert.Equal("raiz-drive", juego.ActiveVersion.DriveFolderId);
        }

        // --- Numeración ---

        [Fact]
        public void NextVersionNumber_NoReutilizaNumerosBorrados()
        {
            // Había V1, V2 y V3; se borró V3 (la más alta)
            var juego = JuegoAntiguo();
            juego.Versions = new List<GameVersion> { Version(1), Version(2) };
            juego.LastVersionNumber = 3;

            Assert.Equal(4, juego.NextVersionNumber);
        }

        [Fact]
        public void NextVersionNumber_BorrarUnaIntermedia_MantieneLosNumeros()
        {
            var juego = JuegoAntiguo();
            juego.Versions = new List<GameVersion> { Version(1), Version(3) };
            juego.EnsureVersions();

            Assert.Equal(new[] { "V1", "V3" }, juego.OrderedVersions.Select(v => v.Label));
            Assert.Equal(4, juego.NextVersionNumber);
        }

        // --- Versión activa y nombre ---

        [Fact]
        public void DisplayName_MuestraLaVersionActiva()
        {
            var juego = JuegoAntiguo();
            juego.Versions = new List<GameVersion> { Version(1), Version(2) };
            juego.ActiveVersionId = "v2";

            Assert.Equal("MINECRAFT V2", juego.DisplayName);
            Assert.True(juego.HasMultipleVersions);
        }

        [Fact]
        public void ActiveVersion_SinIdValido_UsaLaDeMenorNumero()
        {
            var juego = JuegoAntiguo();
            juego.Versions = new List<GameVersion> { Version(3), Version(1) };
            juego.ActiveVersionId = null;

            Assert.Equal(1, juego.ActiveVersion.Number);
            Assert.False(new GameConfig { Versions = new List<GameVersion> { Version(1) } }.HasMultipleVersions);
        }

        [Fact]
        public void OrderedVersions_OrdenaPorNumero()
        {
            var juego = JuegoAntiguo();
            juego.Versions = new List<GameVersion> { Version(5), Version(1), Version(3) };

            Assert.Equal(new[] { 1, 3, 5 }, juego.OrderedVersions.Select(v => v.Number));
        }

        // --- Formato del .json ---

        [Fact]
        public void Json_GuardaVersionesYNoLasPropiedadesCalculadas()
        {
            var juego = JuegoAntiguo();
            juego.Versions = new List<GameVersion> { Version(1), Version(2) };
            juego.Versions[1].Description = "Partida en hardcore";
            juego.ActiveVersionId = "v2";
            juego.LastVersionNumber = 2;

            string json = JsonConvert.SerializeObject(juego);
            var copia = JsonConvert.DeserializeObject<GameConfig>(json);

            Assert.DoesNotContain("DisplayName", json);
            Assert.DoesNotContain("\"Label\"", json);
            Assert.DoesNotContain("OrderedVersions", json);
            Assert.Equal(2, copia.Versions.Count);
            Assert.Equal("v2", copia.ActiveVersionId);
            Assert.Equal("Partida en hardcore", copia.FindVersion("v2").Description);
            Assert.Equal(3, copia.NextVersionNumber);
        }
    }
}
