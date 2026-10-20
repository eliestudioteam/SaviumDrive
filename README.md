<p align="center">
  <img src="SaviumDrive/Assets/logo-about.png" alt="Savium Drive" width="220">
</p>

<p align="center">
  <b>Respaldos de tus partidas de juegos en Google Drive.</b><br>
  Guarda, versiona y restaura tus saves desde cualquier PC con Windows.
</p>

<p align="center">
  <a href="https://github.com/eliestudioteam/SaviumDrive/actions/workflows/ci.yml"><img src="https://github.com/eliestudioteam/SaviumDrive/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D6?logo=windows" alt="Windows 10 | 11">
  <img src="https://img.shields.io/badge/.NET-8-512BD4?logo=dotnet" alt=".NET 8">
  <img src="https://img.shields.io/badge/WinUI-3-0078D4" alt="WinUI 3">
  <a href="LICENSE"><img src="https://img.shields.io/badge/licencia-MIT-green" alt="Licencia MIT"></a>
</p>

---

**Savium Drive** es una app de escritorio para Windows que sube las carpetas de partidas de tus juegos a tu propio Google Drive, para que no pierdas tu progreso al formatear, cambiar de PC o cuando un juego no tiene guardado en la nube. Todo se guarda en la carpeta `SaviumBackups` de **tu** cuenta; no hay servidores intermedios.

## Índice

- [Funciones](#funciones)
  - [Panel de inicio](#1-panel-de-inicio)
  - [Agregar un juego](#2-agregar-un-juego)
  - [Subida con progreso en tiempo real](#3-subida-con-progreso-en-tiempo-real)
  - [Biblioteca](#4-biblioteca)
  - [Versiones de cada juego](#5-versiones-de-cada-juego)
  - [Restaurar partidas](#6-restaurar-partidas)
  - [Detector de juegos (recordatorio de backup)](#7-detector-de-juegos-recordatorio-de-backup)
  - [Varios PCs, una sola biblioteca](#8-varios-pcs-una-sola-biblioteca)
  - [Configuración](#9-configuración)
- [Instalación y compilación](#instalación-y-compilación)
- [Privacidad](#privacidad)
- [Tecnologías](#tecnologías)
- [Licencia](#licencia)

## Funciones

### 1. Panel de inicio

<p align="center"><img src="docs/capturas/1-inicio.png" alt="Panel de inicio" width="900"></p>

Un vistazo rápido al estado de tus respaldos:

- **Estado de la nube:** si estás conectado a Google Drive y si la sincronización terminó.
- **Juegos guardados:** cuántos juegos tienes en la biblioteca.
- **Almacenamiento de Drive:** cuánto ocupan tus backups y cuánto espacio te queda en la cuenta.
- **Respaldos recientes:** los últimos juegos que guardaste.
- Botón **Crear nuevo respaldo** para agregar un juego en un clic.

### 2. Agregar un juego

<p align="center"><img src="docs/capturas/2-agregar-juego.png" alt="Agregar un juego" width="900"></p>

Escribe el nombre del juego y Savium Drive hace el resto:

- **Portada automática:** busca la carátula en [SteamGridDB](https://www.steamgriddb.com/) y te deja elegir entre varias con las flechas.
- **Carpeta de partidas automática:** consulta la base de datos de [Ludusavi](https://github.com/mtkennerly/ludusavi-manifest), con miles de juegos, para saber dónde guarda cada juego sus partidas, y rellena la ruta si existe en tu PC. Si el juego no está ahí, prueba las carpetas habituales (`Documentos\My Games`, `AppData`, `Saved Games`…).
- Siempre puedes elegir otra carpeta con **Seleccionar carpeta…**.

### 3. Subida con progreso en tiempo real

<p align="center"><img src="docs/capturas/3-subida.png" alt="Subida con progreso" width="900"></p>

- El juego aparece en la biblioteca **desde el primer segundo**, con la portada **en gris que se va coloreando de izquierda a derecha** según avanza la subida, al estilo de Epic Games.
- Barra con el **porcentaje real**, los MB enviados, la **velocidad de subida** y el **tiempo restante**, por ejemplo `saves.zip · 149,0 MB / 263,5 MB · 1,5 MB/s · quedan 1 min 18 s`.
- Las carpetas con muchos archivos se **comprimen en un `.zip`**: una sola subida en vez de miles de peticiones.
- Las actualizaciones son **incrementales**: solo se sube lo que cambió (se compara tamaño y MD5) y el resto se copia dentro de Drive.
- Un indicador flotante muestra el progreso en cualquier sección de la app.
- **Cancelar** detiene la subida de verdad y borra de Drive lo que se había subido a medias, aunque hayas cambiado de sección.

### 4. Biblioteca

<p align="center"><img src="docs/capturas/4-biblioteca.png" alt="Biblioteca" width="900"></p>

Todos tus juegos con su portada. Desde cada tarjeta puedes:

| Icono | Acción |
|:---:|---|
| 💾 | **Guardar** el backup de la versión activa (solo sube lo que cambió) |
| ⬇️ | **Restaurar** la partida en este PC |
| 🗑️ | **Eliminar** el juego (va a la papelera de Drive, no se borra para siempre) |
| 📄 | **Crear una versión nueva** |
| 🕘 | **Ver el historial de versiones** (si el juego tiene más de una) |

Además:

- **Buscar** por nombre y **ordenar** de la A a la Z, de la Z a la A o por el último backup.
- El **selector de versión** (`V3 ▾`) de la tarjeta cambia la versión activa sin entrar al historial.
- Si la carpeta de partidas no existe en este PC, la tarjeta te pide elegirla.

### 5. Versiones de cada juego

<p align="center"><img src="docs/capturas/5-historial-versiones.png" alt="Historial de versiones" width="900"></p>

Cada juego puede tener **varias partidas independientes** (V1, V2, V3…): una por cada partida, personaje o forma de jugar. Por ejemplo, en *Los Sims 4*, una dinastía de 10 generaciones, un Sim obsesionado con hacerse rico y una casa de criaturas sobrenaturales.

- Cada versión tiene su **descripción**, que puedes ver y editar.
- El **historial** muestra la fecha de creación, el último guardado y el **tamaño en Drive** de cada versión, y el total del juego.
- **Usar esta versión** la activa: guardar y restaurar trabajan siempre sobre la versión activa.
- Borrar una versión la envía a la papelera de Drive. Los números no se reutilizan, así que siempre sabes cuál es cuál.

### 6. Restaurar partidas

- Descarga la versión activa y la deja en la carpeta de partidas del juego, con las **fechas originales** de cada archivo.
- **Te avisa antes de sobrescribir** si las partidas de tu PC son más recientes que las del backup.
- Restaura primero en una carpeta temporal y solo reemplaza la tuya cuando todo se descargó bien. Si algo falla, tu partida no se toca.

### 7. Detector de juegos (recordatorio de backup)

¿Se te olvida hacer el backup después de jugar? Activa **Detectar juegos** en la configuración y Savium Drive te lo recordará:

1. Mientras juegas, detecta el juego que tienes en primer plano. Funciona en segundo plano y con muy poca memoria.
2. Cuando lo cierras, si jugaste más de un minuto, aparece un aviso.
3. Según el caso:
   - **Si el juego ya está en tu biblioteca**, el aviso te ofrece **Guardar ahora** y hace el backup en un clic.
   - **Si todavía no lo está**, te ofrece **agregarlo** con el nombre, la portada y la carpeta de partidas ya rellenados.

Cómo reconoce los juegos:

- **Steam, Epic Games y GOG:** lee los juegos instalados de cada tienda, así que sabe el **nombre real** (*Alan Wake*, no `AlanWake.exe`) y, en Steam, su ID, para encontrar la carpeta de partidas exacta.
- **Juegos sin tienda** (Minecraft, Hytale, juegos descargados de una web): ignora los lanzadores y detecta el juego que abren por su motor (Unity, Unreal, GLFW/Java…), sus librerías o la pantalla completa. En Minecraft Java, el nombre sale del título de la ventana.
- **Aprende de ti:** cuando agregas un juego desde el aviso, lo recuerda con el nombre que elegiste.
- **No se confunde** con los procesos de Steam (`steamwebhelper`), los lanzadores, los instaladores, los navegadores ni las apps como Discord. Si tienes varios juegos abiertos a la vez, sigue cada uno por separado.

### 8. Varios PCs, una sola biblioteca

- La biblioteca se **sincroniza con Drive**: inicia sesión en otro PC y aparecen tus juegos.
- **Cada PC recuerda su propia carpeta de partidas** para el mismo juego (por ejemplo, otro usuario de Windows u otro disco) sin cambiar la de los demás.

### 9. Configuración

- **Iniciar con Windows** e **iniciar minimizado**.
- **Minimizar a la bandeja del sistema** al cerrar, con menú en el icono de la bandeja.
- **Modo oscuro / claro.**
- **Idioma:** español e inglés.
- **Detectar juegos** (ver arriba) y aviso al cambiar de versión.
- **Cerrar sesión** de Google.

## Instalación y compilación

> Por ahora no hay instalador publicado: la app se compila desde el código. Es una app de escritorio **sin empaquetar** (no usa MSIX), así que no necesita certificado.

**Requisitos**

- Windows 10 (1809) o superior.
- [Visual Studio 2022 o 2026](https://visualstudio.microsoft.com/) con la carga de trabajo **Desarrollo de aplicaciones WinUI** (o **Desarrollo de escritorio de .NET**).
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

**Pasos**

```bash
git clone https://github.com/eliestudioteam/SaviumDrive.git
cd SaviumDrive
dotnet build SaviumDrive/SaviumDrive.csproj -c Release -p:Platform=x64
```

O abre `SaviumDrive.sln` en Visual Studio, elige la plataforma **x64** (o x86) y pulsa ▶️.

**Pruebas**

```bash
dotnet test tests/SaviumDrive.Tests
```

Cada cambio se compila (Debug y Release, x64 y x86) y se prueba automáticamente con GitHub Actions.

## Privacidad

- Tus partidas se guardan **en tu Google Drive**, en la carpeta `SaviumBackups`. Savium Drive no tiene servidores propios ni recoge datos.
- El inicio de sesión usa el flujo oficial de Google (OAuth) y la sesión se guarda solo en tu PC (en `%LOCALAPPDATA%\SaviumDrive`).
- Solo se consultan servicios públicos para la portada (SteamGridDB) y la ubicación de las partidas (base de datos de Ludusavi).

## Tecnologías

- **C# / .NET 8** y **WinUI 3** (Windows App SDK).
- **Google Drive API v3.**
- [H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon) para la bandeja del sistema.
- [SteamGridDB](https://www.steamgriddb.com/) para las portadas.
- [Ludusavi Manifest](https://github.com/mtkennerly/ludusavi-manifest) para las rutas de partidas.
- **xUnit** para las pruebas.

## Licencia

Distribuido bajo la licencia **MIT**. Consulta [LICENSE](LICENSE).

<p align="center">Hecho con ❤️ por <b>Elie Studio</b></p>
