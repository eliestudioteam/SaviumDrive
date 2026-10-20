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
    /// <summary>
    /// Inicio de sesión con Google (OAuth 2.0 para apps de escritorio).
    /// El navegador abre la pantalla de consentimiento de Google y vuelve a un servidor local
    /// (<see cref="CustomCodeReceiver"/>) que recibe el código de autorización. La sesión (token) se guarda
    /// en %LOCALAPPDATA%\SaviumDrive\token.json y se reutiliza en los siguientes arranques.
    /// </summary>
    public static class GoogleAuth
    {
        // Solo los archivos que crea la app (ver OAuthScopes), no todo el Drive del usuario
        static string[] Scopes = OAuthScopes.Actuales;
        static string ApplicationName = "SaviumDrive";

        // Credenciales del cliente OAuth de tipo "app de escritorio" del proyecto en Google Cloud.
        // No están en el código: se inyectan al compilar (ver Core/AppSecrets.cs).
        private static string ClientId => AppSecrets.GoogleClientId;
        private static string ClientSecret => AppSecrets.GoogleClientSecret;

        // Límite de intentos: tras 5 intentos fallidos seguidos hay que esperar 5 minutos
        private static int intentosLogin = 0;
        private static DateTime ultimoIntento = DateTime.MinValue;
        private const int MaxIntentosPermitidos = 5;
        private const int MinutosBloqueo = 5;

        /// <summary>
        /// Carpeta de la sesión guardada. Va en %LOCALAPPDATA% porque la carpeta del .exe puede ser
        /// de solo lectura (por ejemplo, Program Files).
        /// </summary>
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

        /// <summary>
        /// Devuelve un cliente de Drive autenticado. Si hay una sesión guardada válida la reutiliza sin abrir
        /// el navegador; si no, abre la pantalla de consentimiento de Google y espera la respuesta.
        /// </summary>
        public static async Task<DriveService> Autenticar(CancellationToken cancellationToken)
        {
            if (intentosLogin >= MaxIntentosPermitidos &&
                (DateTime.Now - ultimoIntento).TotalMinutes < MinutosBloqueo)
            {
                int minutosRestantes = MinutosBloqueo - (int)(DateTime.Now - ultimoIntento).TotalMinutes;
                throw new Exception($"Demasiados intentos fallidos. Espera {minutosRestantes} minuto(s).");
            }

            string rutaToken = GetTokenPath();

            // Sesión de una versión anterior con acceso a todo el Drive: renovarla con el permiso reducido
            if (Directory.Exists(rutaToken) && !SesionGuardadaAlDia(rutaToken)) await RevocarAcceso();

            if (Directory.Exists(rutaToken))
            {
                bool integridad = await SecurityHelpers.ValidarIntegridadToken(rutaToken);
                if (!integridad) { try { Directory.Delete(rutaToken, true); } catch { } }
            }
            SecurityHelpers.LimpiarDatosAntiguos(rutaToken);

            if (!SecurityHelpers.ValidarClientId(ClientId))
                throw new Exception("Esta compilación no incluye las credenciales de Google. " +
                                    "Copia SaviumDrive/Secrets.props.example como Secrets.props y rellénalo.");

            intentosLogin++;
            ultimoIntento = DateTime.Now;

            try
            {
                UserCredential credential;
                var customReceiver = new CustomCodeReceiver();

                // ClientSecrets se crea en código en vez de leerse de un JSON: en Release, el recorte
                // del compilador rompía la deserialización del archivo de credenciales
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
                    Debug.WriteLine("Token obtenido está expirado");
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
                // Los fallos de red no cuentan como intento fallido
                if (ex.Message.Contains("network") || ex.Message.Contains("timeout")) intentosLogin--;
                throw new Exception($"Error al autenticar: {ex.Message}", ex);
            }
        }

        /// <summary>Cierra la sesión: revoca el permiso en Google y borra el token guardado.</summary>
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

        /// <summary>True si el token guardado existe y no ha caducado.</summary>
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

        /// <summary>Intentos fallidos acumulados y minutos que faltan para poder volver a intentarlo.</summary>
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

        /// <summary>
        /// True si hay una sesión guardada con la que entrar sin abrir el navegador.
        /// Se usa al arrancar para decidir entre entrar directamente o mostrar la pantalla de inicio de sesión.
        /// </summary>
        public static bool ExisteSesionGuardada()
        {
            string tokenPath = GetTokenPath();
            if (!Directory.Exists(tokenPath) || Directory.GetFiles(tokenPath).Length == 0) return false;

            // Con permisos antiguos no se entra solo: al arrancar se abriría el navegador sin que el usuario lo pida.
            // Se cierra esa sesión (revocándola en Google) y se muestra la pantalla de inicio de sesión.
            if (!SesionGuardadaAlDia(tokenPath))
            {
                _ = RevocarAcceso();
                return false;
            }
            return true;
        }

        /// <summary>Lee el permiso ("scope") con el que se concedió la sesión guardada y comprueba que sea el actual.</summary>
        private static bool SesionGuardadaAlDia(string carpetaToken)
        {
            try
            {
                foreach (var archivo in Directory.GetFiles(carpetaToken))
                {
                    using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(archivo));
                    if (json.RootElement.TryGetProperty("scope", out var scope))
                        return OAuthScopes.SesionAlDia(scope.GetString());
                }
            }
            catch { }
            return false;
        }
    }

    /// <summary>
    /// Recibe la respuesta de Google tras el consentimiento. Abre la pantalla de Google en el navegador
    /// predeterminado y escucha en http://127.0.0.1:{puerto libre}/authorize/, que es la dirección de vuelta
    /// (redirect URI) que Google permite a las apps de escritorio. Al recibir el código, muestra en el
    /// navegador la página de <see cref="LoginResponsePage"/>.
    /// </summary>
    internal class CustomCodeReceiver : ICodeReceiver
    {
        public string RedirectUri { get; private set; }

        public CustomCodeReceiver()
        {
            RedirectUri = $"http://127.0.0.1:{GetRandomUnusedPort()}/authorize/";
        }

        public async Task<AuthorizationCodeResponseUrl> ReceiveCodeAsync(AuthorizationCodeRequestUrl url, CancellationToken taskCancellationToken)
        {
            var authorizationUrl = url.Build().ToString();

            Process.Start(new ProcessStartInfo { FileName = authorizationUrl, UseShellExecute = true });

            var listener = new HttpListener();
            try
            {
                listener.Prefixes.Add(RedirectUri);
                listener.Start();

                // Al cancelar (botón Cancelar o tiempo de espera) se cierra el puerto en vez de dejarlo esperando
                HttpListenerContext context;
                using (taskCancellationToken.Register(() => { try { listener.Stop(); } catch { } }))
                {
                    try { context = await listener.GetContextAsync().ConfigureAwait(false); }
                    catch (Exception) when (taskCancellationToken.IsCancellationRequested) { throw new OperationCanceledException(taskCancellationToken); }
                }
                var coll = context.Request.QueryString;

                var buffer = Encoding.UTF8.GetBytes(PaginaDeRespuesta(coll["error"]));
                var respuesta = context.Response;
                respuesta.ContentType = "text/html; charset=utf-8";
                respuesta.ContentLength64 = buffer.Length;
                respuesta.KeepAlive = false;
                await respuesta.OutputStream.WriteAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                await respuesta.OutputStream.FlushAsync().ConfigureAwait(false);
                respuesta.Close();

                return new AuthorizationCodeResponseUrl
                {
                    Code = coll["code"],
                    State = coll["state"],
                    Error = coll["error"],
                    ErrorDescription = coll["error_description"],
                    ErrorUri = coll["error_uri"]
                };
            }
            finally
            {
                // Cerrar el puerto unos segundos después: si se cierra en el acto, .NET corta la conexión
                // antes de que el navegador reciba toda la página (con el logo pesa ~16 KB) y se ve en negro
                _ = Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ => { try { listener.Close(); } catch { } });
            }
        }

        /// <summary>Página del navegador con el idioma, el tema y el logo de la app.</summary>
        private static string PaginaDeRespuesta(string error)
        {
            string logo = null;
            try { logo = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "logo-about.png"))); }
            catch { }

            var ajustes = SaviumDrive.Services.SettingsService.Current;
            bool espanol = (ajustes?.Language ?? "es-ES").StartsWith("es", StringComparison.OrdinalIgnoreCase);
            return LoginResponsePage.Crear(error, espanol, ajustes?.DarkMode ?? true, logo, SaviumDrive.Services.ProtocolService.EnlaceAbrir);
        }

        /// <summary>Pide al sistema un puerto TCP libre en la interfaz local.</summary>
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