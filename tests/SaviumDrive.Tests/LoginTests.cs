using SaviumDrive.Core;
using System;
using System.IO;
using Xunit;

namespace SaviumDrive.Tests
{
    public class OAuthScopesTests
    {
        [Fact]
        public void PideSoloLosArchivosDeLaApp()
        {
            Assert.Equal(new[] { "https://www.googleapis.com/auth/drive.file" }, OAuthScopes.Actuales);
        }

        [Theory]
        [InlineData("https://www.googleapis.com/auth/drive.file", true)]
        [InlineData("https://www.googleapis.com/auth/drive", false)]                                              // versión anterior
        [InlineData("https://www.googleapis.com/auth/drive https://www.googleapis.com/auth/drive.file", false)]   // aún tiene todo el Drive
        [InlineData("", false)]
        [InlineData(null, false)]
        public void SesionAlDia_SoloConElPermisoReducido(string scope, bool esperado)
        {
            Assert.Equal(esperado, OAuthScopes.SesionAlDia(scope));
        }
    }

    public class LoginResponsePageTests
    {
        [Fact]
        public void Exito_EnEspanolYOscuro_ConLogo()
        {
            string html = LoginResponsePage.Crear(null, espanol: true, oscuro: true, logoBase64: "QUJD");
            Assert.Contains("¡Conectado!", html);
            Assert.Contains("--fondo:#151515", html);
            Assert.Contains("data:image/png;base64,QUJD", html);
            Assert.Contains("#008EFF", html);
            Assert.DoesNotContain("window.close", html); // los navegadores no dejan cerrar la pestaña: no prometerlo
        }

        [Fact]
        public void Cancelado_EnInglesYClaro()
        {
            string html = LoginResponsePage.Crear("access_denied", espanol: false, oscuro: false, logoBase64: null);
            Assert.Contains("Sign-in cancelled", html);
            Assert.Contains("--fondo:#F3F3F3", html);
            Assert.DoesNotContain("<img", html);
        }

        [Fact]
        public void Error_SeEscapaElTextoDeGoogle()
        {
            string html = LoginResponsePage.Crear("<script>x</script>", espanol: true, oscuro: true, logoBase64: null);
            Assert.Contains("No se pudo iniciar sesión", html);
            Assert.DoesNotContain("<script>x", html);
            Assert.Contains("&lt;script&gt;", html);
        }

        [Fact]
        public void ConEnlace_OfreceAbrirLaAppYLaAbreSolaSoloSiSalioBien()
        {
            string exito = LoginResponsePage.Crear(null, true, true, null, "saviumdrive://abrir");
            Assert.Contains("href='saviumdrive://abrir'", exito);
            Assert.Contains("Abrir Savium Drive", exito);
            Assert.Contains("window.location.href = 'saviumdrive://abrir'", exito);

            string cancelado = LoginResponsePage.Crear("access_denied", true, true, null, "saviumdrive://abrir");
            Assert.Contains("href='saviumdrive://abrir'", cancelado);        // botón para volver
            Assert.DoesNotContain("window.location.href", cancelado);         // pero no se abre sola

            Assert.DoesNotContain("saviumdrive://", LoginResponsePage.Crear(null, true, true, null));
        }

        /// <summary>Con LOGIN_PAGE_OUT=carpeta guarda las variantes para verlas en el navegador.</summary>
        [Fact]
        public void GuardarVariantesParaRevisar()
        {
            string carpeta = Environment.GetEnvironmentVariable("LOGIN_PAGE_OUT");
            string logo = Environment.GetEnvironmentVariable("LOGIN_PAGE_LOGO");
            if (string.IsNullOrEmpty(carpeta)) return;
            Directory.CreateDirectory(carpeta);
            string b64 = File.Exists(logo) ? Convert.ToBase64String(File.ReadAllBytes(logo)) : null;
            File.WriteAllText(Path.Combine(carpeta, "exito-oscuro.html"), LoginResponsePage.Crear(null, true, true, b64, "saviumdrive://abrir"));
            File.WriteAllText(Path.Combine(carpeta, "exito-claro.html"), LoginResponsePage.Crear(null, true, false, b64, "saviumdrive://abrir"));
            File.WriteAllText(Path.Combine(carpeta, "cancelado.html"), LoginResponsePage.Crear("access_denied", true, true, b64, "saviumdrive://abrir"));
        }
    }
}
