// Core/LoginResponsePage.cs
// Página que ve el usuario en el navegador al terminar el inicio de sesión con Google.
// Sin dependencias de WinUI: se prueba desde SaviumDrive.Tests.
using System.Net;

namespace SaviumDrive.Core
{
    public static class LoginResponsePage
    {
        /// <param name="error">Código de error de Google ("access_denied"...) o null si salió bien.</param>
        /// <param name="espanol">Idioma de la app (no el del navegador).</param>
        /// <param name="oscuro">Tema de la app.</param>
        /// <param name="logoBase64">PNG del logo en base64, o null para no mostrarlo.</param>
        /// <param name="enlaceApp">Enlace saviumdrive:// para volver a la app; null para no ofrecerlo.</param>
        public static string Crear(string error, bool espanol, bool oscuro, string logoBase64, string enlaceApp = null)
        {
            bool exito = string.IsNullOrEmpty(error);
            bool cancelado = error == "access_denied";

            string titulo, mensaje, pie;
            if (exito)
            {
                titulo = espanol ? "¡Conectado!" : "Connected!";
                mensaje = espanol ? "Has iniciado sesión correctamente. Savium Drive ya puede guardar tus partidas en tu Google Drive."
                                  : "You're signed in. Savium Drive can now back up your saves to your Google Drive.";
                pie = espanol ? "Ya puedes cerrar esta pestaña." : "You can close this tab now.";
            }
            else if (cancelado)
            {
                titulo = espanol ? "Inicio de sesión cancelado" : "Sign-in cancelled";
                mensaje = espanol ? "No se concedió el permiso. Vuelve a Savium Drive y pulsa «Iniciar sesión» para intentarlo de nuevo."
                                  : "Permission wasn't granted. Go back to Savium Drive and click “Sign in” to try again.";
                pie = espanol ? "Puedes cerrar esta pestaña." : "You can close this tab.";
            }
            else
            {
                titulo = espanol ? "No se pudo iniciar sesión" : "Couldn't sign in";
                mensaje = (espanol ? "Google devolvió un error: " : "Google returned an error: ") + WebUtility.HtmlEncode(error);
                pie = espanol ? "Vuelve a Savium Drive e inténtalo de nuevo." : "Go back to Savium Drive and try again.";
            }

            string icono = exito
                ? "<svg viewBox='0 0 52 52' aria-hidden='true'><circle class='aro' cx='26' cy='26' r='24'/><path class='trazo' d='M15 27l7 7 15-16'/></svg>"
                : "<svg viewBox='0 0 52 52' aria-hidden='true'><circle class='aro' cx='26' cy='26' r='24'/><path class='trazo' d='M18 18l16 16M34 18L18 34'/></svg>";

            // Botón que abre la app: el navegador pregunta "¿Abrir Savium Drive?". Al terminar bien se intenta solo.
            string textoBoton = espanol ? "Abrir Savium Drive" : "Open Savium Drive";
            string boton = string.IsNullOrEmpty(enlaceApp) ? ""
                : $"<a class='boton' href='{WebUtility.HtmlEncode(enlaceApp)}'>{textoBoton}</a>";
            string abrirSolo = exito && !string.IsNullOrEmpty(enlaceApp)
                ? $"<script>setTimeout(function () {{ window.location.href = '{enlaceApp}'; }}, 700);</script>"
                : "";

            string logo = string.IsNullOrEmpty(logoBase64) ? "" : $"<img class='logo' src='data:image/png;base64,{logoBase64}' alt='Savium Drive'>";

            // Colores de la app (Themes/ThemeResources.xaml): azul de acento #008EFF
            string tema = oscuro
                ? "--fondo:#151515;--tarjeta:#202020;--borde:#303030;--texto:#FFFFFF;--texto2:#D0D0D0;--texto3:#909090;--brillo:rgba(0,142,255,.18)"
                : "--fondo:#F3F3F3;--tarjeta:#FFFFFF;--borde:#E0E0E0;--texto:#1F1F1F;--texto2:#505050;--texto3:#808080;--brillo:rgba(0,142,255,.12)";
            string estado = exito ? "#008EFF" : (cancelado ? "#F5A623" : "#E5484D");

            return $@"<!doctype html>
<html lang='{(espanol ? "es" : "en")}'>
<head>
<meta charset='utf-8'>
<meta name='viewport' content='width=device-width, initial-scale=1'>
<title>Savium Drive</title>
<style>
  :root {{ {tema}; --acento:#008EFF; --estado:{estado}; }}
  * {{ box-sizing: border-box; }}
  html, body {{ height: 100%; margin: 0; }}
  body {{
    display: flex; align-items: center; justify-content: center; padding: 24px;
    background: radial-gradient(circle at 50% 0%, var(--brillo), transparent 60%), var(--fondo);
    color: var(--texto); font-family: 'Segoe UI Variable Text', 'Segoe UI', system-ui, sans-serif;
  }}
  .tarjeta {{
    width: 100%; max-width: 440px; padding: 40px 36px 32px; text-align: center;
    background: var(--tarjeta); border: 1px solid var(--borde); border-radius: 16px;
    box-shadow: 0 20px 60px rgba(0,0,0,.35);
    animation: entrar .45s cubic-bezier(.2,.8,.2,1) both;
  }}
  .logo {{ display: block; width: 180px; height: auto; margin: -28px auto -36px; }}
  svg {{ width: 64px; height: 64px; margin: 8px auto 18px; display: block; }}
  .aro {{ fill: none; stroke: var(--estado); stroke-width: 3; opacity: .25; }}
  .trazo {{ fill: none; stroke: var(--estado); stroke-width: 4; stroke-linecap: round; stroke-linejoin: round;
           stroke-dasharray: 48; stroke-dashoffset: 48; animation: dibujar .5s .3s ease-out forwards; }}
  h1 {{ margin: 0 0 10px; font-size: 24px; font-weight: 600; }}
  p {{ margin: 0; color: var(--texto2); font-size: 15px; line-height: 1.55; }}
  .boton {{
    display: inline-block; margin-top: 24px; padding: 11px 26px; border-radius: 8px;
    background: var(--acento); color: #FFFFFF; font-size: 15px; font-weight: 600; text-decoration: none;
    box-shadow: 0 6px 18px rgba(0,142,255,.35); transition: transform .15s, box-shadow .15s;
  }}
  .boton:hover {{ transform: translateY(-1px); box-shadow: 0 8px 22px rgba(0,142,255,.45); }}
  .boton:focus-visible {{ outline: 2px solid var(--texto); outline-offset: 3px; }}
  .pie {{ margin-top: 28px; padding-top: 18px; border-top: 1px solid var(--borde); color: var(--texto3); font-size: 13px; }}
  @keyframes entrar {{ from {{ opacity: 0; transform: translateY(12px) scale(.98); }} }}
  @keyframes dibujar {{ to {{ stroke-dashoffset: 0; }} }}
  @media (prefers-reduced-motion: reduce) {{ .tarjeta, .trazo {{ animation: none; stroke-dashoffset: 0; }} }}
</style>
</head>
<body>
  <main class='tarjeta'>
    {logo}
    {icono}
    <h1>{titulo}</h1>
    <p>{mensaje}</p>
    {boton}
    <p class='pie'>{pie}</p>
  </main>
  {abrirSolo}
</body>
</html>";
        }
    }
}
