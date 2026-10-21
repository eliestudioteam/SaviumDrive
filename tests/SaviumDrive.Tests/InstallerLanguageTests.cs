using System;
using System.IO;
using SaviumDrive.Services;
using Xunit;

namespace SaviumDrive.Tests
{
    public class InstallerLanguageTests
    {
        [Theory]
        [InlineData("es", "es-ES")]
        [InlineData("en", "en-US")]
        [InlineData("EN", "en-US")]
        [InlineData("fr", null)]
        [InlineData("", null)]
        [InlineData(null, null)]
        public void ToAppLanguage_SoloIdiomasDeLaApp(string instalador, string esperado)
        {
            Assert.Equal(esperado, InstallerLanguage.ToAppLanguage(instalador));
        }

        [Fact]
        public void FromRecordJson_LeeElIdiomaDeUninstallJson()
        {
            string json = "{ \"appId\": \"{0435AA71-C363-470A-AAFB-0DF8AF2038C6}\", \"name\": \"SaviumDrive\", \"language\": \"en\" }";

            Assert.Equal("en-US", InstallerLanguage.FromRecordJson(json));
        }

        [Theory]
        [InlineData("{ \"name\": \"SaviumDrive\" }")]   // instalado con una versión anterior del instalador
        [InlineData("{ \"language\": 3 }")]
        [InlineData("esto no es JSON")]
        public void FromRecordJson_SinIdiomaValido_DevuelveNull(string json)
        {
            Assert.Null(InstallerLanguage.FromRecordJson(json));
        }

        [Fact]
        public void Read_SinUninstallJson_DevuelveNull()
        {
            // Lo normal al ejecutar desde Visual Studio: no hay instalación
            string carpeta = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(carpeta);
            try
            {
                Assert.Null(InstallerLanguage.Read(carpeta));

                File.WriteAllText(Path.Combine(carpeta, InstallerLanguage.RecordFileName), "{ \"language\": \"es\" }");
                Assert.Equal("es-ES", InstallerLanguage.Read(carpeta));
            }
            finally
            {
                Directory.Delete(carpeta, true);
            }
        }
    }
}
