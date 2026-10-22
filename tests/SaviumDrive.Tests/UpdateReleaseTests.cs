using System.IO;
using System.Text;
using SaviumDrive.Services;
using Xunit;

namespace SaviumDrive.Tests
{
    public class UpdateReleaseTests
    {
        private static ReleaseAsset Asset(string name) =>
            new ReleaseAsset { Name = name, DownloadUrl = "https://example.com/" + name, Size = 1 };

        [Fact]
        public void PickInstaller_PrefiereElInstaladorPorNombre()
        {
            var assets = new[] { Asset("herramienta.exe"), Asset("SHA256SUMS.txt"), Asset("SaviumDrive-Setup-1.1.0.exe") };

            Assert.Equal("SaviumDrive-Setup-1.1.0.exe", UpdateRelease.PickInstaller(assets).Name);
        }

        [Fact]
        public void PickInstaller_SinNombreDeInstalador_UsaElPrimerExe()
        {
            var assets = new[] { Asset("notas.txt"), Asset("SaviumDrive.exe") };

            Assert.Equal("SaviumDrive.exe", UpdateRelease.PickInstaller(assets).Name);
        }

        [Fact]
        public void PickInstaller_SinExe_DevuelveNull()
        {
            Assert.Null(UpdateRelease.PickInstaller(new[] { Asset("SaviumDrive.zip") }));
        }

        [Fact]
        public void FindChecksums_EncuentraElArchivoDeHashes()
        {
            var assets = new[] { Asset("SaviumDrive-Setup-1.1.0.exe"), Asset("sha256sums.TXT") };

            Assert.Equal("sha256sums.TXT", UpdateRelease.FindChecksums(assets).Name);
        }

        [Theory]
        [InlineData("  ")] // formato de sha256sum en modo texto
        [InlineData(" *")] // modo binario
        public void FindSha256_LeeElFormatoDeSha256sum(string separador)
        {
            string hash = new string('A', 64);
            string texto = $"{new string('b', 64)}  otro.exe\r\n{hash}{separador}SaviumDrive-Setup-1.1.0.exe\n";

            Assert.Equal(hash.ToLowerInvariant(), UpdateRelease.FindSha256(texto, "SaviumDrive-Setup-1.1.0.exe"));
        }

        [Fact]
        public void FindSha256_ConNombreRenombradoPorGitHub()
        {
            // GitHub publica "Savium Drive-Setup-1.0.1.exe" como "Savium.Drive-Setup-1.0.1.exe"
            string hash = new string('c', 64);
            Assert.Equal(hash, UpdateRelease.FindSha256($"{hash}  Savium Drive-Setup-1.0.1.exe", "Savium.Drive-Setup-1.0.1.exe"));
        }

        [Fact]
        public void FindSha256_ArchivoAusente_DevuelveNull()
        {
            Assert.Null(UpdateRelease.FindSha256($"{new string('a', 64)}  otro.exe", "SaviumDrive-Setup-1.1.0.exe"));
            Assert.Null(UpdateRelease.FindSha256("basura", "SaviumDrive-Setup-1.1.0.exe"));
        }

        [Fact]
        public void ComputeSha256_CoincideConElValorConocido()
        {
            string ruta = Path.GetTempFileName();
            try
            {
                File.WriteAllText(ruta, "abc", new UTF8Encoding(false));
                Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", UpdateRelease.ComputeSha256(ruta));
            }
            finally
            {
                File.Delete(ruta);
            }
        }

        [Theory]
        [InlineData("1.0.0-beta", "1.0.0 Beta")]
        [InlineData("1.2.0", "1.2.0")]
        [InlineData("1.0.0-rc1+abc123", "1.0.0 Rc1")]
        [InlineData("2.0.0+abc123", "2.0.0")]
        public void DisplayVersion_MuestraElSufijoComoTexto(string informativa, string esperada)
        {
            Assert.Equal(esperada, UpdateRelease.DisplayVersion(informativa));
        }

        [Fact]
        public void NumericVersion_UsaTresComponentes()
        {
            // La versión del ensamblado de pruebas es 1.0.0.0
            Assert.Equal("1.0.0", UpdateRelease.NumericVersion(typeof(UpdateReleaseTests).Assembly));
        }
    }
}
