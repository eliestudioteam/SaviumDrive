using SaviumDrive.Services;
using Xunit;

namespace SaviumDrive.Tests
{
    public class UpdateVersioningTests
    {
        [Theory]
        [InlineData("1.0.1", "1.0.0", true)]
        [InlineData("1.1", "1.0.9", true)]
        [InlineData("2.0.0", "1.9.9", true)]
        [InlineData("1.0.0", "1.0.0", false)]
        [InlineData("1.0", "1.0.0", false)]
        [InlineData("0.9.9", "1.0.0", false)]
        [InlineData("basura", "1.0.0", false)]
        public void IsNewerVersion_ComparaPorComponentes(string nueva, string actual, bool esperado)
        {
            Assert.Equal(esperado, UpdateVersioning.IsNewerVersion(nueva, actual));
        }

        [Fact]
        public void PrimeraEjecucion_TrasActualizar_MuestraChangelog()
        {
            // Se ejecutó 1.0.0, el actualizador descargó 1.1.0 y ahora corre 1.1.0
            Assert.True(UpdateVersioning.IsFirstRunAfterUpdate("1.0.0", "1.1.0", "1.1.0"));
        }

        [Fact]
        public void PrimeraEjecucion_TrasInstalacionManual_MuestraChangelog()
        {
            Assert.True(UpdateVersioning.IsFirstRunAfterUpdate("1.0.0", null, "1.1.0"));
        }

        [Fact]
        public void MismaVersion_NoMuestraChangelog()
        {
            Assert.False(UpdateVersioning.IsFirstRunAfterUpdate("1.1.0", "1.1.0", "1.1.0"));
        }

        [Fact]
        public void InstalacionNueva_NoMuestraChangelog()
        {
            Assert.False(UpdateVersioning.IsFirstRunAfterUpdate(null, null, "1.0.0"));
        }

        [Fact]
        public void DescargadaPeroNoInstalada_NoMuestraChangelog()
        {
            // El bug original: se guardaba 1.1.0 al descargar y la app 1.0.0 lo mostraba sin haber actualizado
            Assert.False(UpdateVersioning.IsFirstRunAfterUpdate("1.0.0", "1.1.0", "1.0.0"));
        }

        [Fact]
        public void VersionAntiguaSinLastRun_ActualizadaConElActualizador_MuestraChangelog()
        {
            Assert.True(UpdateVersioning.IsFirstRunAfterUpdate(null, "1.1.0", "1.1.0"));
        }
    }
}
