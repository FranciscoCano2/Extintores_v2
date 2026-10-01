# Build e instalación en el Meta Quest 3

Cómo generar el paquete (`.apk`) y dejarlo corriendo en el visor, **sin cable durante el uso**:
una vez instalado, el juego queda como una app más del Quest y se abre desde la biblioteca.

## Requisitos (una sola vez)

1. **Modo desarrollador** en la cuenta Meta: app Meta Horizon en el celular ▸ *Dispositivos* ▸
   el visor ▸ *Configuración del desarrollador* ▸ activar. Sin esto `adb` no ve el Quest.
2. Cable USB-C y, al conectar, aceptar **"Permitir depuración por USB"** *dentro del visor*
   (el cartel aparece puesto en la cabeza, no en la PC).
3. Unity **6000.5.4f1** con el módulo *Android Build Support* (SDK + NDK + OpenJDK).

## Camino rápido (recomendado)

Con el **editor de Unity cerrado** (bloquea el proyecto), desde la raíz del repo:

```bash
powershell -ExecutionPolicy Bypass -File tools\build-quest.ps1 -Run
```

Compila, instala en el visor conectado y lanza la app. Variantes:

| Comando | Qué hace |
|---|---|
| `tools\build-quest.ps1` | Sólo compila → `Build/Android/Extintores_v2.apk` |
| `tools\build-quest.ps1 -Install` | Compila e instala, sin lanzar |
| `tools\build-quest.ps1 -Run` | Compila, instala y abre la app en el visor |
| `tools\build-quest.ps1 -Development` | Development build (profiler y debugging remoto; anda más lento) |

La **primera** build compila shaders + IL2CPP y tarda entre 15 y 30 minutos. Las siguientes
reutilizan la caché y bajan a unos pocos minutos. El log completo queda en `Logs/build-quest.log`.

## Desde el editor abierto

Menú **Extintores ▸ Build APK (Quest)**. Genera el mismo APK y abre la carpeta al terminar; la
instalación queda a cargo tuyo (`adb install -r` o SideQuest).

## Qué configura el build por su cuenta

`Assets/Scripts/Extintores/Editor/QuestBuild.cs` fuerza, antes de compilar, los ajustes que mal
puestos dan un build que "termina bien" pero no arranca en el visor:

| Ajuste | Valor | Por qué |
|---|---|---|
| Build target | Android | — |
| `buildAppBundle` | **false** (APK) | un `.aab` es para la Store, `adb` no lo instala |
| Arquitectura | **ARM64** | un build ARMv7 instala pero no abre |
| Scripting backend | **IL2CPP** | obligatorio para ARM64 |
| `applicationIdentifier` | `com.rv.extintores` | el proyecto venía con el del template URP |

Escenas: las **habilitadas** en Build Settings, hoy sólo `Assets/Scenes/Training.unity`
(`SampleScene` está deshabilitada a propósito).

**Firma:** no hay keystore propio, así que Unity firma con el *debug keystore*. Alcanza para
sideload. Para publicar en la Store haría falta uno real — y cambiar de keystore obliga a
desinstalar primero la versión vieja.

## Instalación manual (otra PC, SideQuest, entrega del .apk)

El APK es autocontenido: una vez instalado **corre solo en el Quest, sin PC ni cable** (no es
Link). Se puede copiar y pasar a cualquiera con el visor en modo desarrollador.

**Con el script (recomendado), sin recompilar ni tener Unity:**

```bash
powershell -ExecutionPolicy Bypass -File tools\install-quest.ps1 -Run
```

Toma `Build/Android/Extintores_v2.apk` (o `-Apk <ruta>`), instala con `adb install -r`, muestra el
`versionCode` que quedó instalado y, con `-Run`, abre la app. Para llevarlo a **otra PC** sin el
repo: copiar `install-quest.ps1` + el `.apk` a una carpeta; si esa PC no tiene `adb`, poner al lado
la carpeta `platform-tools` (Android SDK Platform Tools) y el script la encuentra.

**A mano:**

```bash
adb install -r Build\Android\Extintores_v2.apk
```

Si no hay `adb` en el PATH, está en
`C:\Program Files\Unity\Hub\Editor\6000.5.4f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe`.
Sin línea de comandos: **Meta Quest Developer Hub** o **SideQuest** (arrastrar el `.apk`).

**Qué versión está instalada:** cada build lleva `versionCode` = fecha y hora de compilación
(`yyMMddHHm`, p. ej. `261001153` = 01/10/26 15:3x) y `versionName` = `bundleVersion` de Player
Settings. `adb shell dumpsys package com.rv.extintores | findstr version`.

**La configuración del usuario** (volumen, vibración, calibración del mando/boquilla, posición del
panel) vive en los PlayerPrefs de la app y **sobrevive a `adb install -r`**, que es lo que usa
`build-quest.ps1`. Se pierde si se **desinstala** (`adb uninstall`, o desde el visor) o si cambia el
`applicationIdentifier`. Para comprobar qué valores cargó al arrancar:
`adb logcat -s Unity | findstr GameSettings`.

En el visor queda en **Biblioteca ▸ (filtro) Orígenes desconocidos ▸ Extintores_v2**. No aparece
entre las apps normales: si lo buscás ahí, no está.

## Si algo falla

| Síntoma | Causa / arreglo |
|---|---|
| `El proyecto está abierto en el editor de Unity` | Cerrá Unity: el batchmode no puede tomar el lock del proyecto. |
| `No hay ningún Quest conectado` | Poné el visor, aceptá el cartel de depuración USB y reintentá. Sirve `adb devices` para verificar. |
| `INSTALL_FAILED_UPDATE_INCOMPATIBLE` | Hay una versión firmada con otra clave: `adb uninstall com.rv.extintores` y reinstalá. ⚠ Desinstalar borra la configuración del usuario: anotá la calibración antes. |
| Instala pero arranca en negro | Casi siempre XR: revisá que OpenXR esté habilitado en Android y que estén los *Interaction Profiles* detached (ver `CLAUDE.md`). |
| Los mandos no se ven / las manos no aparecen | Multimodal: `Launch Simultaneous H&C On Startup` tiene que estar **ON** en el OVRManager de la escena al momento de compilar. |
| Aparecés hundido en el piso | Lo corrige solo `FloorGuard` (ver `CLAUDE.md`). Si persiste: `adb logcat -s Unity \| findstr FloorGuard` dice a qué altura quedaron los ojos y si el origen de piso falló. Si queda corrido unos centímetros (no hundido del todo), es el piso del visor: rehacer el límite (Guardian) ▸ *Confirmar altura del piso*. |
| El build falla sin mensaje claro | `Logs/build-quest.log`, buscar `error CS` (compilación) o `BuildFailedException`. |

## Antes de entregar

Correr `Extintores ▸ Validate Setup` y guardar la escena: el cableado de escena falla en silencio y
el build se lleva lo que esté guardado, no lo que arregla el código al vuelo.
