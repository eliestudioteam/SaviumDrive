# Política de privacidad de Savium Drive

*Última actualización: 21 de octubre de 2026* · [English version below](#privacy-policy-for-savium-drive)

Savium Drive es una aplicación de escritorio para Windows, de código abierto, creada por Eliestudio. Sirve
para hacer copias de seguridad de las partidas guardadas de tus juegos en **tu propia cuenta de Google
Drive**. Esta política explica qué datos usa la aplicación y para qué.

## En resumen

- Tus partidas se guardan **solo en tu Google Drive**. Savium Drive no tiene servidores propios y nadie más
  (tampoco Eliestudio) puede ver tus archivos.
- La aplicación **solo puede ver los archivos que ella misma crea** en tu Drive; no el resto.
- **No hay telemetría, publicidad ni análisis de uso**, y tus datos no se venden ni se comparten.

## Datos de tu cuenta de Google

Para conectarse a Google Drive, Savium Drive te pide permiso con el inicio de sesión de Google (OAuth).
Solo solicita el permiso **`drive.file`**: acceso a los archivos y carpetas creados por la propia
aplicación (la carpeta `SaviumBackups` y el archivo `savium_config.json`). **No puede ver, abrir ni modificar
ningún otro archivo de tu Drive.**

Con ese permiso, la aplicación:

- crea, sube, descarga, copia, renombra y envía a la papelera sus propias copias de seguridad;
- lee el nombre de tu cuenta y el espacio disponible en tu Drive, para mostrarlos en la aplicación.

El uso que hace Savium Drive de la información recibida de las API de Google cumple la
[Política de datos de usuario de los servicios de API de Google](https://developers.google.com/terms/api-services-user-data-policy),
incluidos los requisitos de uso limitado.

La sesión se guarda **solo en tu equipo**, en `%LOCALAPPDATA%\SaviumDrive\token.json`. Al cerrar sesión
desde la aplicación, el permiso se revoca en Google y ese archivo se borra. También puedes quitar el
acceso en cualquier momento desde [la configuración de tu cuenta de Google](https://myaccount.google.com/permissions).

## Datos de tu equipo

Para encontrar tus juegos y sus partidas, Savium Drive revisa **en tu equipo** las carpetas habituales de
juegos y lanzadores (por ejemplo, Steam o Epic Games) y la carpeta de partidas guardadas. Esa información se
usa solo dentro de la aplicación: no se envía a ningún sitio.

Tus preferencias (idioma, tema, juegos añadidos…) se guardan en tu equipo, en `%APPDATA%\SaviumDrive`.

## Servicios de terceros

Además de Google Drive, la aplicación se conecta a estos servicios. Ninguno recibe datos de tu cuenta de
Google ni tus partidas:

| Servicio | Para qué | Qué se envía |
|---|---|---|
| [SteamGridDB](https://www.steamgriddb.com) | Mostrar la portada de cada juego | El **nombre del juego** que se busca |
| GitHub (`api.github.com`) | Comprobar y descargar actualizaciones de Savium Drive | Nada personal: solo se consulta la última versión publicada |
| GitHub (`raw.githubusercontent.com`) | Descargar la lista pública de juegos de [Ludusavi](https://github.com/mtkennerly/ludusavi-manifest), que indica dónde guarda cada juego sus partidas | Nada personal |

Como en cualquier conexión a internet, estos servicios pueden registrar tu dirección IP según sus propias
políticas de privacidad.

## Niños

Savium Drive no está dirigida a menores de 13 años ni recoge datos de ellos a sabiendas.

## Cambios en esta política

Si esta política cambia, se publicará aquí con su nueva fecha. Los cambios importantes se indicarán también
en las notas de la versión correspondiente.

## Contacto

Para cualquier pregunta sobre esta política o sobre tus datos, abre una consulta en
[GitHub](https://github.com/eliestudioteam/SaviumDrive/issues).

---

# Privacy Policy for Savium Drive

*Last updated: October 21, 2026* · [Versión en español arriba](#política-de-privacidad-de-savium-drive)

Savium Drive is an open-source Windows desktop app made by Eliestudio. It backs up your games' save files
to **your own Google Drive account**. This policy explains what data the app uses and why.

## Summary

- Your saves are stored **only in your Google Drive**. Savium Drive has no servers of its own, and nobody
  else (including Eliestudio) can see your files.
- The app **can only see the files it creates** in your Drive, nothing else.
- **There is no telemetry, advertising or usage analytics**, and your data is never sold or shared.

## Your Google account data

To connect to Google Drive, Savium Drive asks for your permission through Google sign-in (OAuth). It only
requests the **`drive.file`** scope: access to files and folders created by the app itself (the
`SaviumBackups` folder and the `savium_config.json` file). **It cannot see, open or change any other file in
your Drive.**

With that permission, the app:

- creates, uploads, downloads, copies, renames and moves to the trash its own backups;
- reads your account name and your available Drive storage, to show them in the app.

Savium Drive's use of information received from Google APIs adheres to the
[Google API Services User Data Policy](https://developers.google.com/terms/api-services-user-data-policy),
including the Limited Use requirements.

Your session is stored **only on your computer**, in `%LOCALAPPDATA%\SaviumDrive\token.json`. Signing out
from the app revokes the permission with Google and deletes that file. You can also remove access at any
time from [your Google Account settings](https://myaccount.google.com/permissions).

## Data on your computer

To find your games and their saves, Savium Drive looks **on your computer** at the usual game and launcher
folders (for example Steam or Epic Games) and at save-file locations. That information is only used inside
the app and is never sent anywhere.

Your preferences (language, theme, added games…) are stored on your computer, in `%APPDATA%\SaviumDrive`.

## Third-party services

Besides Google Drive, the app connects to these services. None of them receives your Google account data or
your saves:

| Service | Purpose | What is sent |
|---|---|---|
| [SteamGridDB](https://www.steamgriddb.com) | Showing each game's cover art | The **name of the game** being searched |
| GitHub (`api.github.com`) | Checking for and downloading Savium Drive updates | Nothing personal: it only asks for the latest release |
| GitHub (`raw.githubusercontent.com`) | Downloading the public [Ludusavi](https://github.com/mtkennerly/ludusavi-manifest) game list, which says where each game stores its saves | Nothing personal |

As with any internet connection, these services may log your IP address under their own privacy policies.

## Children

Savium Drive is not directed at children under 13 and does not knowingly collect data from them.

## Changes to this policy

If this policy changes, the new version will be published here with its new date. Significant changes will
also be mentioned in the notes of the corresponding release.

## Contact

For any question about this policy or your data, open an issue on
[GitHub](https://github.com/eliestudioteam/SaviumDrive/issues).
