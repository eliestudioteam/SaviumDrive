using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SaviumDrive.Core
{
    public static class GoogleAuth
    {
        static string[] Scopes = { DriveService.Scope.Drive };
        static string ApplicationName = "SaviumDrive";

        // Client
        private const string ClientId = "754275432342-u4ls7i7onuscofi851lgivcdrnic0k9r.apps.googleusercontent.com";

        // CS
        private static string ClientSecret =>
            System.Text.Encoding.UTF8.GetString(
                Convert.FromBase64String("R09DU1BYLUFpekN2Wk1CcndJUEFYalRFNkwtYVlGZDJ6bnY=")
            );

        // 🔥 HTML PERSONALIZADO
        private const string ClosePageResponseHtml = @"
            <html>
              <head>
                <meta charset='utf-8'>
                <title>Savium Drive</title>
                <style>
                  body { background-color: #151515; color: #FFFFFF; font-family: 'Segoe UI Variable', 'Segoe UI', sans-serif; display: flex; flex-direction: column; align-items: center; justify-content: center; height: 100vh; margin: 0; user-select: none; }
                  .card { background-color: #2D2D2D; padding: 48px; border-radius: 8px; box-shadow: 0 8px 32px rgba(0,0,0,0.5); text-align: center; width: 100%; max-width: 420px; border: 1px solid #3C3C3C; }
                  h1 { font-weight: 600; margin-bottom: 16px; font-size: 24px; }
                  p { color: #D0D0D0; margin: 0; font-size: 15px; line-height: 1.5; }
                  .footer-text { color: #909090; font-size: 13px; margin-top: 32px; }
                  .icon-container { color: #6CCB5F; margin-bottom: 24px; display: inline-block; }
                </style>
                <script>
                  const i18n = {
                    es: { title: '¡Conectado!', msg: 'Has iniciado sesión correctamente en Savium Drive.', footer: 'Esta ventana ya se puede cerrar.' }
                  };
                  window.onload = function() {
                    try {
                        const userLang = navigator.language || navigator.userLanguage || 'en';
                        if (userLang.startsWith('es')) {
                            document.getElementById('txt-title').innerText = i18n.es.title;
                            document.getElementById('txt-msg').innerText = i18n.es.msg;
                            document.getElementById('txt-footer').innerText = i18n.es.footer;
                        }
                        setTimeout(function() { window.close(); }, 3000);
                    } catch (e) { console.error(e); }
                  };
                </script>
              </head>
              <body>
                <div class='card'>
                    <div class='icon-container'>
                        <svg width='64' height='64' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2.5' stroke-linecap='round' stroke-linejoin='round'><path d='M22 11.08V12a10 10 0 1 1-5.93-9.14'></path><polyline points='22 4 12 14.01 9 11.01'></polyline></svg>
                    </div>
                    <h1 id='txt-title'>Connected!</h1>
                    <p id='txt-msg'>You have successfully logged in to Savium Drive.</p>
                    <p class='footer-text' id='txt-footer'>You can close this tab.</p>
                </div>
              </body>
            </html>";

        // 🛡️ RATE LIMITING
        private static int intentosLogin = 0;
        private static DateTime ultimoIntento = DateTime.MinValue;
        private const int MaxIntentosPermitidos = 5;
        private const int MinutosBloqueo = 5;

        // 🔥 El token va en %LOCALAPPDATA%: la carpeta del .exe puede ser de solo lectura (ej. Program Files)
        public static string GetTokenPath()
        {
            string carpeta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SaviumDrive");
            string rutaToken = Path.Combine(carpeta, "token.json");

            // Migrar sesión de versiones anteriores (token junto al .exe)
            string rutaAntigua = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "token.json");
            if (!Directory.Exists(rutaToken) && Directory.Exists(rutaAntigua))
            {
                try
                {
                    Directory.CreateDirectory(rutaToken);
                    foreach (var archivo in Directory.GetFiles(rutaAntigua))
                        File.Copy(archivo, Path.Combine(rutaToken, Path.GetFileName(archivo)), true);
                }
                catch { }
            }

            return rutaToken;
        }

        public static async Task<DriveService> Autenticar(CancellationToken cancellationToken)
        {
            if (intentosLogin >= MaxIntentosPermitidos &&
                (DateTime.Now - ultimoIntento).TotalMinutes < MinutosBloqueo)
            {
                int minutosRestantes = MinutosBloqueo - (int)(DateTime.Now - ultimoIntento).TotalMinutes;
                throw new Exception($"Demasiados intentos fallidos. Espera {minutosRestantes} minuto(s).");
            }

            string rutaToken = GetTokenPath();
            if (Directory.Exists(rutaToken))
            {
                bool integridad = await SecurityHelpers.ValidarIntegridadToken(rutaToken);
                if (!integridad) { try { Directory.Delete(rutaToken, true); } catch { } }
            }
            SecurityHelpers.LimpiarDatosAntiguos(rutaToken);

            intentosLogin++;
            ultimoIntento = DateTime.Now;

            try
            {
                UserCredential credential;
                var customReceiver = new CustomCodeReceiver(ClosePageResponseHtml);

                // 🔥 SOLUCIÓN RELEASE: Instanciar ClientSecrets directamente
                // Esto evita la deserialización JSON que falla por optimizaciones del compilador
                var secretos = new ClientSecrets
                {
                    ClientId = ClientId,
                    ClientSecret = ClientSecret
                };

                credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    secretos,
                    Scopes,
                    "user",
                    cancellationToken,
                    new FileDataStore(rutaToken, true),
                    customReceiver
                );

                intentosLogin = 0;

                var dataStore = new FileDataStore(rutaToken, true);
                var tokenResponse = await dataStore.GetAsync<TokenResponse>("user");
                if (tokenResponse != null && !SecurityHelpers.TokenEsValido(tokenResponse))
                {
                    Debug.WriteLine("⚠️ Token obtenido está expirado");
                }

                return new DriveService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = ApplicationName,
                });
            }
            catch (OperationCanceledException)
            {
                intentosLogin--;
                throw new Exception("La operación fue cancelada por el usuario o por tiempo de espera.");
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("network") || ex.Message.Contains("timeout")) intentosLogin--;
                throw new Exception($"Error al autenticar: {ex.Message}", ex);
            }
        }

        public static async Task RevocarAcceso()
        {
            try
            {
                string rutaToken = GetTokenPath();
                var dataStore = new FileDataStore(rutaToken, true);
                var tokenResponse = await dataStore.GetAsync<TokenResponse>("user");

                if (tokenResponse?.AccessToken != null)
                {
                    using (var httpClient = new HttpClient())
                    {
                        var revokeUrl = $"https://oauth2.googleapis.com/revoke?token={tokenResponse.AccessToken}";
                        await httpClient.PostAsync(revokeUrl, null);
                    }
                }
                if (Directory.Exists(rutaToken)) Directory.Delete(rutaToken, true);
                intentosLogin = 0;
            }
            catch { }
        }

        public static async Task<bool> ValidarToken()
        {
            try
            {
                string rutaToken = GetTokenPath();
                var dataStore = new FileDataStore(rutaToken, true);
                var tokenResponse = await dataStore.GetAsync<TokenResponse>("user");
                return SecurityHelpers.TokenEsValido(tokenResponse);
            }
            catch { return false; }
        }

        public static (int intentos, int minutosRestantes) ObtenerEstadoRateLimit()
        {
            int minutosRestantes = 0;
            if (intentosLogin >= MaxIntentosPermitidos)
            {
                double mins = (DateTime.Now - ultimoIntento).TotalMinutes;
                if (mins < MinutosBloqueo) minutosRestantes = MinutosBloqueo - (int)mins;
            }
            return (intentosLogin, minutosRestantes);
        }

        public static bool ExisteSesionGuardada()
        {
            string tokenPath = GetTokenPath();
            return Directory.Exists(tokenPath) && Directory.GetFiles(tokenPath).Length > 0;
        }
    }

    // CLASE CUSTOM RECEIVER IGUAL QUE ANTES
    internal class CustomCodeReceiver : ICodeReceiver
    {
        private readonly string _responseHtml;
        public string RedirectUri { get; private set; }

        public CustomCodeReceiver(string responseHtml)
        {
            _responseHtml = responseHtml;
            RedirectUri = $"http://127.0.0.1:{GetRandomUnusedPort()}/authorize/";
        }

        public async Task<AuthorizationCodeResponseUrl> ReceiveCodeAsync(AuthorizationCodeRequestUrl url, CancellationToken taskCancellationToken)
        {
            var authorizationUrl = url.Build().ToString();

            Process.Start(new ProcessStartInfo { FileName = authorizationUrl, UseShellExecute = true });

            using (var listener = new HttpListener())
            {
                listener.Prefixes.Add(RedirectUri);
                listener.Start();

                var context = await listener.GetContextAsync().ConfigureAwait(false);
                var coll = context.Request.QueryString;

                var buffer = Encoding.UTF8.GetBytes(_responseHtml);
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.ContentLength64 = buffer.Length;
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                context.Response.OutputStream.Close();

                return new AuthorizationCodeResponseUrl
                {
                    Code = coll["code"],
                    State = coll["state"],
                    Error = coll["error"],
                    ErrorDescription = coll["error_description"],
                    ErrorUri = coll["error_uri"]
                };
            }
        }

        private int GetRandomUnusedPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}