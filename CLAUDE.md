# Extintores_v2 — Proyecto RV (Meta Quest)

Proyecto de Realidad Virtual en Unity para práctica de uso de extintores en Meta Quest.
Usa el modo **Multimodal (Simultaneous Hands and Controllers)** del Meta XR SDK: manos y
controles rastreados a la vez, con transición automática según el control esté en la mano
o soltado.

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
