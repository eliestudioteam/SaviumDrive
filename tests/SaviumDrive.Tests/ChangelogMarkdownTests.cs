using SaviumDrive.Services;
using System.Linq;
using Xunit;

namespace SaviumDrive.Tests
{
    public class ChangelogMarkdownTests
    {
        // Notas de versión con el formato que genera GitHub
        private const string Notas = @"## Novedades
<!-- comentario que no se muestra -->
- **Progreso real** al subir, con velocidad
- Corrección del botón `Cancelar`
  - Subpunto con *cursiva*
* [x] Tarea hecha

### Pasos
1. Abre la app
2) Agrega un juego

Texto normal
con salto de línea.

---
**Full Changelog**: https://github.com/eliestudioteam/SaviumDrive/compare/v1.0.0...v1.1.0";

        [Fact]
        public void Analizar_ReconoceTitulosListasParrafosYSeparadores()
        {
            var b = ChangelogMarkdown.Analizar(Notas);

            Assert.Equal(new[]
            {
                TipoBloqueMd.Titulo, TipoBloqueMd.Vineta, TipoBloqueMd.Vineta, TipoBloqueMd.Vineta, TipoBloqueMd.Vineta,
                TipoBloqueMd.Titulo, TipoBloqueMd.Numerado, TipoBloqueMd.Numerado,
                TipoBloqueMd.Parrafo, TipoBloqueMd.Separador, TipoBloqueMd.Parrafo
            }, b.Select(x => x.Tipo));

            Assert.Equal(2, b[0].Nivel);
            Assert.Equal("Novedades", b[0].TextoPlano);
            Assert.Equal(3, b[5].Nivel);
            Assert.Equal(1, b[3].Nivel);                    // subpunto
            Assert.Equal("Tarea hecha", b[4].TextoPlano);   // sin "[x]"
            Assert.Equal(new[] { "1.", "2." }, b.Where(x => x.Tipo == TipoBloqueMd.Numerado).Select(x => x.Numero));
            Assert.Equal("Texto normal\ncon salto de línea.", b[8].TextoPlano);
            Assert.DoesNotContain(b, x => x.TextoPlano.Contains("comentario"));
        }

        [Fact]
        public void Tramos_NegritaCodigoCursivaYEnlaces()
        {
            var t = ChangelogMarkdown.Tramos("Usa **Guardar**, el botón `Cancelar` y *mira* [la guía](https://example.com/guia).");

            Assert.Equal(new[]
            {
                "Texto:Usa ", "Negrita:Guardar", "Texto:, el botón ", "Codigo:Cancelar", "Texto: y ",
                "Cursiva:mira", "Texto: ", "Enlace:la guía", "Texto:."
            }, t.Select(x => x.ToString()));
            Assert.Equal("https://example.com/guia", t[7].Url);
        }

        [Fact]
        public void Tramos_EnlaceSuelto_SinLaPuntuacionFinal()
        {
            var t = ChangelogMarkdown.Tramos("Más en https://github.com/eliestudioteam/SaviumDrive.");
            var enlace = t.Single(x => x.Tipo == TipoTramoMd.Enlace);
            Assert.Equal("https://github.com/eliestudioteam/SaviumDrive", enlace.Url);
            Assert.Equal(".", t.Last().Texto);
        }

        [Theory]
        [InlineData("archivo_de_partida_1.sav")]   // guiones bajos dentro de palabras
        [InlineData("2 * 3 = 6")]                  // asteriscos sueltos
        public void Tramos_NoConfundeSimbolosNormalesConFormato(string texto)
        {
            var t = ChangelogMarkdown.Tramos(texto);
            Assert.Equal(texto, string.Concat(t.Select(x => x.Texto)));
            Assert.All(t, x => Assert.Equal(TipoTramoMd.Texto, x.Tipo));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   \n  ")]
        public void Analizar_SinNotas_NoDevuelveBloques(string notas)
        {
            Assert.Empty(ChangelogMarkdown.Analizar(notas));
        }
    }
}
