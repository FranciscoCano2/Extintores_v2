# Extintores_v2 — Proyecto RV (Meta Quest)

Proyecto de Realidad Virtual en Unity para práctica de uso de extintores en Meta Quest.
Usa el modo **Multimodal (Simultaneous Hands and Controllers)** del Meta XR SDK: manos y
controles rastreados a la vez, con transición automática según el control esté en la mano
o soltado.

## Documentación (leer ANTES de tocar nada)

Este archivo cubre el **entorno y el rig XR**. El diseño y el estado del juego están en `docs/`:

| Documento | Qué contiene |
|---|---|
| [`docs/PLAN-ejercicio-entrenamiento.md`](docs/PLAN-ejercicio-entrenamiento.md) | **Estado actual.** Menú de modos, puestos, patio industrial (antes galpón), botones por chorro. Arquitectura, qué está hecho y qué falta. |
| [`docs/PLAN-fuegos-y-extintores.md`](docs/PLAN-fuegos-y-extintores.md) | Diseño de fondo: clases de fuego A/B/C/K, agentes, matriz de efectividad, pared de recarga. |
| [`docs/CREDITS.md`](docs/CREDITS.md) | Licencias de los assets de terceros (varios CC-BY: hay que acreditar). |

**La escena del juego es `Assets/Scenes/Training.unity`** (primera en Build Settings).
`SampleScene.unity` quedó como sandbox y está deshabilitada en el build.

⚠️ **Ante "el código está pero no funciona", correr `Extintores ▸ Validate Setup` ANTES de tocar
código.** El cableado de escena falla en silencio: los bugs de agosto 2026 (manager duplicado,
componente equivocado, sensor sin `isTrigger`, prefab desactualizado) no daban ni error de
compilación ni excepción, simplemente no pasaba nada. Para cambios de escena usar un editor tool
(`SerializedObject` + `Undo`), nunca editar el YAML a mano — eso fue justamente la causa raíz.

## Entorno

| Ítem | Valor |
|---|---|
| Unity | **6000.5.4f1** (el proyecto se creó en 6000.5.3f1, que ya no es descargable; se migró a 6000.5.4f1) |
| Render pipeline | URP 17.5.0 |
| Meta XR Core SDK | `com.meta.xr.sdk.core` 203.0.0 |
| XR backend | **OpenXR** (`com.unity.xr.openxr` 1.17.1) — NO Oculus provider |
| Build target | **Android** / Meta Quest |
| Dispositivos target | Quest 2, Quest 3, Quest Pro, Quest 3S |

> El editor debe tener el módulo **Android Build Support + Android SDK & NDK Tools + OpenJDK**
> instalado vía Unity Hub. `6000.5.4f1` lo tiene; una instalación previa de `6000.5.3f1` hecha
> con el instalador directo (fuera del Hub) NO lo tenía y no ofrecía "Add modules".

## Configuración XR (Project Settings)

- `XR Plug-in Management > OpenXR` habilitado en **Android** y **Standalone**.
- **Meta XR Feature Groups** (Android): Meta XR Feature, Meta XR Foveation, Meta XR Subsampled Layout.
- **Interaction Profiles (Android)** incluyen los perfiles *Detached* — imprescindibles para
  que los anchors detached reciban pose:
  - Detached Oculus Touch Controller Profile
  - Detached Meta Quest Touch Plus Controller Profile
  - Detached Meta Quest Touch Pro Controller Profile

Archivo: `Assets/XR/Settings/OpenXRPackageSettings.asset`

## OVRManager (en el OVRCameraRig de la escena)

En `Quest Features`:
- `Hand Tracking Support` = **Controllers And Hands** (`handTrackingSupport: 1` en
  `Assets/Oculus/OculusProjectConfig.asset`) — obligatorio para habilitar los toggles siguientes.
- `Simultaneous Hands And Controllers Enabled` = **ON**.
- `Launch Simultaneous H&C On Startup` = **ON** ← este es el que realmente activa multimodal
  al iniciar (`launchSimultaneousHandsControllersOnStartup`, se pasa a
  `OVRPlugin.SetSimultaneousHandsAndControllersEnabled`). Debe estar habilitado en build time.

Los tres campos tienen dependencia en cadena: Hand Tracking Support → Simultaneous Enabled →
Launch On Startup. Si el primero no es "Controllers And Hands", los otros quedan grises.

**Incompatibilidades:** multimodal NO convive con Body Tracking, Full Body Synthesis,
Wide Motion Mode ni Fast Motion Mode (FMM tiene prioridad y lo desactiva). No agregar
componente `OVRBody` a la escena.

## Escena: `Assets/Scenes/SampleScene.unity`

Rig: prefab `OVRCameraRig` (de `Packages/Meta XR Core SDK/Prefabs/`). Se borró la Main Camera
por defecto. Los anchors `LeftHandAnchorDetached` / `RightHandAnchorDetached` los crea el propio
`OVRCameraRig` en modo edición (`[ExecuteInEditMode]` → `EnsureGameObjectIntegrity`), no hace
falta crearlos a mano.

Cada anchor tiene prefabs con su `Show State` (`OVRInput.InputDeviceShowState`):

| Anchor | Prefab | Controller / HandType | Show State |
|---|---|---|---|
| LeftHandAnchor | OVRControllerPrefab | L Touch | Controller In Hand Or No Hand |
| LeftHandAnchor | OVRHandPrefab | Hand Left | Controller Not In Hand |
| RightHandAnchor | OVRControllerPrefab | R Touch | Controller In Hand Or No Hand |
| RightHandAnchor | OVRHandPrefab | Hand Right | Controller Not In Hand |
| LeftHandAnchorDetached | OVRControllerPrefab | L Touch | Controller Not In Hand |
| RightHandAnchorDetached | OVRControllerPrefab | R Touch | Controller Not In Hand |

Comportamiento resultante (sin superposiciones):

| Estado | Control en hand anchor | Mano | Control detached |
|---|---|---|---|
| Control agarrado | visible | oculta | oculto |
| Control soltado | oculto | visible | visible (en la pose real del control) |
| Sin hand tracking | visible | oculta | oculto |

## Gotchas / notas aprendidas

- **El tracking origin (`_trackingOriginType`) está en `OVRManager`, NO en `OVRCameraRig`.** Los dos
  componentes viven en el mismo GameObject del prefab, así que es fácil apuntarle al equivocado —
  y `SerializedObject.FindProperty` devuelve `null` sin avisar. El default del prefab del SDK es
  **EyeLevel**, que deja los ojos al ras del piso; el juego lo quiere en **FloorLevel**. Lo corrige
  `Extintores ▸ Fix Scene Wiring` (helper `Editor/OvrTrackingOrigin.cs`).
- **Aun con FloorLevel en la escena, el usuario podía aparecer HUNDIDO en el piso (a veces).** Con
  OpenXR, `OVRManager` pide el origen de piso (`XRInputSubsystem.TrySetTrackingOriginMode(Floor)`)
  **una sola vez** al iniciar; si el input subsystem todavía no arrancó loguea un error y sigue en
  `Device` (eye level) → la cabeza nace en y≈0. Un recenter también puede mover el piso. Lo cubre
  `FloorGuard` (lo maneja `TrainingSession`, sin cableado): re-pide el origen al arrancar, al
  ponerse el visor, en recenter y en cambios de origen, y si los ojos quedan a < 0,5 m del piso
  compensa la Y del rig (ojos a 1,6 m). Se evalúa sólo en esos momentos, NUNCA por frame: el
  usuario se agacha frente al fuego y no hay que "levantarlo". `PlacePlayer` fija la Y del rig a la
  del spawn. Diagnóstico en el visor: `adb logcat -s Unity | findstr FloorGuard`.
- **No colgar carteles del `CenterEyeAnchor`**: quedan clavados a la cara, giran exacto con la cabeza
  y no se pueden leer. Usar `BodyLockedPanel` (seguimiento con zona muerta).
- **`OVRControllerPrefab` trae `m_controller` con un valor viejo inválido** (`33554432`) que no
  corresponde a ningún valor del enum actual (`LTouch=1`, `RTouch=2`). Hay que setear el
  `Controller` explícitamente en cada instancia; si no, el control no se dibuja y no hay error.
- Los detached controllers deben quedar en `Controller Not In Hand`; con el default
  `Controller In Hand Or No Hand` se ven DOS controles superpuestos al agarrar.
- `OVRHand` no desactiva el GameObject cuando la mano no debe mostrarse: pone `IsDataValid = false`.
  Si se lee la mano por código, chequear `IsDataValid`.
- **Por Oculus/PC Link NO se obtiene la pose de los controles inactivos** → los anchors detached
  quedan quietos y parece que está roto. Probar SIEMPRE con Build and Run en el dispositivo.
- Ante pérdida de tracking, el sistema puede reportar falsamente `ControllerNotInHand`. No
  disparar lógica irreversible (ej: "se cayó el extintor") solo con esa señal.
- **Los materiales del pack usan shaders Built-in → salen ROSA/magenta en URP.**
  `fire_extinguisher_tank.mat` y `Glass_part.mat` venían con shader legacy
  (`m_Shader: {fileID: 3/30, guid: 0000000000000000f000000000000000}`). Se convirtieron a
  `Universal Render Pipeline/Lit` (`guid 933532a4fcc9baf4fa0491de14d08ed7`), mapeando `_MainTex`
  → `_BaseMap`. Ojo: otros packs importados (p. ej. props sueltos) pueden tener el mismo
  problema; convertir con el Render Pipeline Converter o cambiando el shader a URP/Lit.
- **El modelo NO tiene boquilla/manguera: hay que crearla o importar otro modelo.** El FBX
  `Assets/Fire_Extinguisher/fire extinguisher.fbx` (de un pack "Police") contiene SOLO dos
  Models: `FireExtinguisher` (tanque) y `Glass_Part` (visor del manómetro). No hay malla de
  boquilla ni de palanca. ⚠️ El `.meta` del FBX lista `Object003`/`Object004` en
  `fileIDToRecycleName`, pero son nombres OBSOLETOS de una versión anterior del FBX que fue
  sobreescrita; el binario actual no los tiene (verificado con `strings`). Reinstanciar el FBX
  NO agrega la boquilla. Para tenerla: modelar una simple con primitivas (cono/cilindro en
  `NozzleTip`) o importar un modelo de extintor más completo.
- **`RightHandAnchor`/`LeftHandAnchor` NO siguen el mando físico en multimodal.** El
  `OVRCameraRig` los mueve a `GetLocalControllerPosition(GetActiveControllerForHand(...))`, y
  `GetActiveControllerForHand` **da prioridad a la mano rastreada**. Un objeto acoplado a ese
  anchor "se pega a la mano", no al mando. Para un prop con el mando atado (extintor), seguir la
  pose del mando físico directamente: `OVRInput.GetLocalControllerPosition(RTouch/LTouch)` en el
  espacio de `TrackingSpace`. Eso hace `ExtinguisherMount.followController` (activo por defecto).

## Chorro / boquilla — esquema a DOS MANOS

Uso realista: el **tanque va en la mano derecha** (`ExtinguisherRig`, `MountHand.Right`) y la
**boquilla/manguera en la izquierda** (`Assets/Prefabs/NozzleRig.prefab`, `MountHand.Left`,
`followController`). El disparo lo hace el **grip del mando DERECHO** (simula apretar la palanca)
y la espuma sale de la boquilla izquierda.

- `NozzleRig` = mount izquierdo + `Visual` (Hose/Horn con cilindros primitivos, material del
  tanque) + `NozzleTip` con `ExtinguisherSpray`. Se arrastra a la escena y sigue el mando
  izquierdo solo (el mount, si no tiene anchor/trackingSpace, busca `OVRCameraRig.trackingSpace`
  con `FindFirstObjectByType`).
- `ExtinguisherSpray` (en `NozzleTip`): crea su propio `ParticleSystem` por código (textura de
  punto suave generada en runtime, material `Sprites/Default` → sin depender de assets del pack).
  El chorro sale por el **+Z** del `NozzleTip`. Lee `OVRInput.Get(Axis1D.PrimaryHandTrigger,
  RTouch)` (grip). Expone `IsSpraying` para la futura lógica de apagado de fuegos.
- Calibración: offset de la boquilla en `NozzleRig > ExtinguisherMount` (Position/Rotation
  Offset); parámetros del chorro (rate, speed, size, cono) en `NozzleTip > ExtinguisherSpray`.
- **Densidad de la nube**: la manda `maxRate` (partículas/s). Los defaults del `.cs` NO alcanzan —
  los campos están serializados en `NozzleRig.prefab` y ganan sobre ellos; cambiarlos sin tocar el
  prefab no hace nada y no avisa. Para eso está `Extintores ▸ Tune Spray Density`, que reescribe el
  prefab con las constantes `ExtinguisherSpray.Default*`. Al subir la densidad hay que **bajar el
  alpha del tint**: cada partícula es un billboard traslúcido y lo que se paga en el Quest es el
  overdraw, no la cantidad. El techo de partículas vivas se deriva de `maxRate × lifetime`, así que
  no hay que acordarse de subir `maxParticles`. Valores actuales (01/10/2026): 1000 p/s, vida
  0,7 s, tamaño final 0,32 m, alpha 0,22.
- **Subir la densidad NO debe apagar los fuegos más rápido.** `FireSource` cobra el daño *por
  partícula*, así que triplicar el caudal triplicaba la velocidad de apagado — un desbalanceo del
  ejercicio que ningún error delata. Por eso `FireSource` divide por `ExtinguisherSpray.ReferenceRate`
  (220 p/s, el caudal con el que se calibró `healthPerParticle`): la densidad es una decisión
  visual y el ritmo de apagado lo sigue fijando `healthPerParticle`.

## Configuración del usuario (menú del lobby)

`GameSettings` (estático, PlayerPrefs) guarda volumen, vibración y la calibración del prop; lo
edita `SettingsMenu` (botón CONFIGURACIÓN del menú). Reglas:

- **`OVRInput.SetControllerVibration` NO VIBRA en este proyecto.** Con el backend OpenXR de Unity,
  el action set de háptica de OVRPlugin no se engancha a la sesión: cada llamada deja
  `XR_ERROR_ACTIONSET_NOT_ATTACHED` en el log y nada más. Tampoco sirvió buscar el mando con
  `UnityEngine.XR.InputDevices` filtrando por `Controller|HeldInHand`: no devolvió ninguno. Usar
  siempre `Haptics.Set/Stop/Pulse`: va por el **Input System** (acción ligada a
  `<XRController>{RightHand}/{Haptic}` → `OpenXRInput.SendHapticImpulse`) y respeta el switch.
  Si no vibra, el log `[Haptics]` lista los dispositivos XR que veía el juego.
- **No mandar `Haptics.Stop` en cada frame**: corta los pulsos de todos los demás en ese mando. El
  chorro lo hacía con el gatillo suelto y ningún pulso llegaba a sentirse.
- La calibración NO toca `positionOffset`/`rotationOffset` (esos son la calibración de fábrica de la
  abrazadera): `ExtinguisherMount` suma encima la rotación del usuario (X/Y/Z, pivotando en el
  `GripPoint`) y un deslizamiento sobre el eje del prop (`userSlide`/`slideAxis`; tanque = +Y del
  `GripPoint`, boquilla = +Z).
- **Los defaults de la calibración del usuario NO son cero**: son la calibración medida con el prop
  real (`GameSettings.Default*`: altura −22 cm, rotación Y 65°, desplazamiento X/Z 2 cm). Si se
  cambian, subir `GameSettings.SettingsVersion`: un visor con prefs guardadas no ve los defaults
  nuevos si no se lo migra. Las prefs de las pruebas por Link (editor) viven en el registro:
  `HKCU\Software\Unity\UnityEditor\DefaultCompany\Extintores_v2` (floats como QWORD de un double).
- El panel de extintores tampoco se mueve tocando los `PanelAnchor`: `TrainingSession.ApplyPanelPose`
  suma `GameSettings.PanelOffset` en el espacio del anchor. El `SettingsPanel` es 100 % generado;
  si se le agrega un campo a `SettingsMenu`, sumarlo a `RequiredRefs` y `Fix Scene Wiring` lo rehace.

## API útil para la lógica del extintor

```csharp
// Estado en-mano / soltado por mano
OVRInput.ControllerInHandState state =
    OVRInput.GetControllerIsInHandState(OVRInput.Hand.HandLeft); // o HandRight
// -> NoHand | ControllerInHand | ControllerNotInHand
```

## Build

Detalle completo (requisitos, instalación, troubleshooting): [`docs/BUILD-quest.md`](docs/BUILD-quest.md).

Con el **editor cerrado**, desde la raíz del repo:

```bash
powershell -ExecutionPolicy Bypass -File tools\build-quest.ps1 -Run
```

Compila el APK (`Build/Android/Extintores_v2.apk`), lo instala por `adb` y lo lanza en el visor.
Sin flags sólo compila; `-Install` instala sin lanzar. La primera build tarda 15-30 min (shaders +
IL2CPP); el log queda en `Logs/build-quest.log`.

Desde el editor abierto: menú **Extintores ▸ Build APK (Quest)** (mismo APK, sin instalar).

`Editor/QuestBuild.cs` fuerza antes de compilar ARM64 + IL2CPP + APK (no AAB) + un
`applicationIdentifier` propio (`com.rv.extintores`, el proyecto traía el del template URP). Sin
eso el build "termina bien" pero no instala o arranca en negro. Firma con el debug keystore de
Unity, que alcanza para sideload.

A mano, como antes: `File > Build Profiles` → **Meta Quest** → visor en Run Device → **Build and Run**.
