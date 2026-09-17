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

## API útil para la lógica del extintor

```csharp
// Estado en-mano / soltado por mano
OVRInput.ControllerInHandState state =
    OVRInput.GetControllerIsInHandState(OVRInput.Hand.HandLeft); // o HandRight
// -> NoHand | ControllerInHand | ControllerNotInHand
```

## Build

1. Conectar el Quest por USB, aceptar "Allow USB debugging" dentro del visor.
2. `File > Build Profiles` → **Meta Quest** → seleccionar el visor en Run Device.
3. **Build and Run** (la primera build compila shaders + Gradle, tarda).
