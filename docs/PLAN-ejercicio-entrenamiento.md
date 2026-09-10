# Plan — Ejercicio de entrenamiento: galpón, menú de modos y validación de tipos

> Documento vivo. Cubre la fase que convierte el sistema de fuegos en un **ejercicio** con menú,
> modos y puestos. Para el diseño previo (clases de fuego, agentes, matriz, pared de recarga) ver
> `PLAN-fuegos-y-extintores.md`; para el detalle técnico XR/OVR ver `../CLAUDE.md`.
> Estado: **septiembre 2026** · Unity 6000.5.4f1 · URP 17.5 · OpenXR multimodal · Quest.

## 1. Por qué existe esta fase

El sistema de fuegos ya funcionaba (se apaga fuego, la matriz agente×clase estaba implementada y
probada), pero **faltaba el ejercicio en sí**: los 4 fuegos flotaban en el vacío a distancias fijas,
sin forma de recorrerlos y sin nada que validara que el usuario elige el extintor correcto.

La primera prueba en visor destapó además dos bugs con causa exacta:

| Síntoma reportado | Causa real |
|---|---|
| "Aparece una B gigante" | En `Fire_B.prefab` la llama cuelga de `Cause` (el auto, escala ×100) en vez de la raíz. `FireClassBadge` parenteaba el cartel al padre con `localScale = 0.25` **local** → 0,25 × 100 = **~25 m de lado**. A/C/K no lo sufrían: ahí `Flame` es hijo de la raíz. |
| "Estoy lejos de la plataforma y no puedo desplazarme" | **No era distancia**: la pared estaba a 1,32 m. El `OVRCameraRig` tenía `_trackingOriginType: 0` (**EyeLevel**) con el rig en y=0 → los ojos arrancaban al ras del piso y todo quedaba por encima de la mirada. Sumado a que **no había piso ni paredes** (la escena tenía un solo MeshRenderer) y a que no existe locomoción en el proyecto. |

## 2. Decisiones tomadas (y por qué)

- **Los botones se accionan apuntándoles el chorro del extintor.** Mismo mecanismo que apagar un
  fuego (`OnParticleCollision`). Cero dependencias nuevas —no entra UGUI, ni TextMeshPro, ni un
  puntero láser—, funciona **a distancia** (que era la queja concreta del visor) y el extintor ya
  está siempre en la mano.
- **Sin locomoción libre.** El sistema reposiciona al usuario en cada puesto. Menos mareo, menos
  código, y evita que se aleje y rompa el ejercicio.
- **El tutorial habilita TODOS los agentes efectivos** (efectividad ≥ 0.9), no uno solo: para clase
  A sirven Agua, Espuma y ABC, y mostrar una única opción enseñaría algo falso.
- **Una sola escena.** Menú, galpón y los 4 puestos conviven; no hay carga de escenas. El lobby es
  un punto más del galpón al que se vuelve al terminar.
- **Blockout por código primero, assets después.** No hay un solo asset de arquitectura en el
  proyecto. El blockout fija medidas y flujo sin gastar ni sumar MB; como los puestos son
  Transforms, la geometría se reemplaza sin tocar lógica.

## 3. Convención de orientación ⚠️

**La cara legible de un Quad y de un TextMesh de Unity es su −Z.** Por eso los botones, los rótulos
de las bahías y el trigger de cada bahía miran hacia −Z, y los anchors del galpón orientan panel y
menú **en la misma dirección que el usuario**, no enfrentados a él.

Si algo se ve espejado, o el trigger de una bahía no dispara, o un rótulo queda tapado: **revisá
este eje antes que ninguna otra cosa**. El `WallBuilder` original ponía los rótulos en +Z y hubo que
corregirlos a mano en el prefab; quedó arreglado en origen.

## 4. Arquitectura

```
TrainingSession            ← orquesta: modo, cola de puestos, reposicionamiento
 ├── FireStation[]         ← un puesto = FireSource + PlayerSpawn + PanelAnchor
 ├── FireManager           ← cuenta SOLO los fuegos de la etapa (BeginSession)
 ├── ExtinguisherStation   ← el panel, se mueve al costado del usuario en cada puesto
 └── SprayButton           ← "Continuar", y los 8 del menú
MainMenu                   ← 3 modos + submenú de 4 clases
Warehouse_Blockout         ← geometría + anchors (Zone_A..K, Lobby)
```

**Piezas clave y por qué son así:**

- `TrainingSession.PlacePlayer` compensa dónde está parada la persona dentro del guardian y rota el
  rig **alrededor de la cabeza**. Mover el rig al punto a secas dejaría al usuario corrido esa misma
  distancia.
- `FireStation` apaga la **raíz** del prefab del fuego, no solo el `FireSource`: el badge de clase
  cuelga del padre y quedaría la letra flotando sobre un puesto vacío.
- `FireSource.ResetFire()` cancela el `Invoke(Deactivate)` pendiente. Sin eso, un fuego reseteado
  dentro de la ventana de `fadeOutSeconds` se apagaría solo unos segundos después.
- `FireManager.BeginSession()` existe porque `autoCollectOnStart` agarraba todos los fuegos y
  `_completed` quedaba latcheado para siempre tras la primera etapa. **Debe quedar apagado** en la
  escena del ejercicio.
- `ExtinguisherSpray` incluye la layer `Interactable` **siempre por código**, no según el LayerMask
  serializado: si dependiera del valor guardado en el prefab, el chorro atravesaría el menú sin
  accionarlo y no habría ningún error que lo delate.
- `SprayButton` acumula carga en vez de dispararse al primer impacto. Cumple el mismo rol que el
  `dwellSeconds` de `ExtinguisherBay`: evita que un barrido del chorro active algo sin querer.

**Layers:** `Fire` = 6 (chorro ↔ fuegos) · `Interactor` = 7 (swap de extintor) · `Interactable` = 8
(botones). Están separadas a propósito: mezclarlas rompe el `collidesWith` del chorro.

## 5. Estado

### Hecho ✅

- [x] `FireClassBadge` compensa la escala del padre (bug de "la B gigante").
- [x] `OVRCameraRig` pasa a **FloorLevel** (lo hace `TrainingSceneBuilder`).
- [x] `SprayButton`: botones accionados por el chorro, con barra de carga y rótulo `TextMesh`.
- [x] Layer `Interactable` (8) + el chorro la incluye siempre.
- [x] `FireSource.ResetFire()` y `FireManager.BeginSession()` → se puede repetir un puesto.
- [x] `FireStation`: puesto desacoplado de la escenografía.
- [x] `TrainingSession`: modos tutorial / aleatorio / personalizado, "Continuar" sin teletransporte
      automático.
- [x] `MainMenu`: 3 opciones + submenú de 4 clases.
- [x] Gateo del tutorial (`ExtinguisherBay.Interactable` + gris en `BayHighlighter`).
- [x] Panel de extintores achicado de 2,45 × 1,3 m a **~1 × 0,55 m**.
- [x] `WarehouseBuilder` (blockout 24×16 m con zonas y anchors) y `TrainingSceneBuilder`.
- [x] `SceneWiringFixer` valida también el ejercicio.
- [x] **Escena `Assets/Scenes/Training.unity` generada y validada** — `Validate Setup` da cero
      problemas: 3 layers, 4 puestos, 9 botones, 1 `FireManager`, HUD cableado.
- [x] `Training.unity` primera en Build Settings (`SampleScene` queda como sandbox, deshabilitada).

### Pendiente ⬜

- [ ] **Build & Run en el Quest** — imprescindible: por Link no llegan ni la colisión de partículas
      ni la pose de los mandos. Checklist en §6.
- [ ] Calibrar en el visor el ritmo de carga de los botones (`chargeNeeded`, hoy 45 partículas): el
      rate real del chorro solo se mide en el dispositivo.
- [ ] Confirmar que el texto de los botones **no sale espejado** (ver §3).
- [ ] Importar el pack gratis *RPG/FPS Game Assets — Industrial Set v2.0* (Asset Store, gratis, URP,
      low-poly, ~204 MB) y reemplazar el blockout zona por zona. Convertir materiales con el Render
      Pipeline Converter y bajar texturas a 1024.
- [ ] Re-parentar `Flame` a la raíz en `Fire_B.prefab` para que los 4 prefabs tengan la misma forma
      (cosmético: el bug visible ya está resuelto en `FireClassBadge`).
- [ ] `AgentType.DryChemBC` no tiene bahía (la pared expone 5 de 6 agentes). Intencional, pero ese
      agente es inalcanzable en juego.

## 6. Cómo correrlo

**Regenerar desde cero** (solo si hace falta rearmar):

1. Abrir la escena con los rigs calibrados y correr `Extintores ▸ Build Warehouse Blockout`.
2. `Extintores ▸ Build Training Scene` — trabaja **sobre la escena abierta** a propósito, para
   conservar los offsets de `ExtinguisherMount` que se calibraron a mano en el visor.
3. `File ▸ Save As…` → `Assets/Scenes/Training.unity`, y ponerla primera en Build Settings.
4. `Extintores ▸ Validate Setup` → debe dar **cero problemas**.

**Checklist de Build & Run:**

- [ ] Arrancás **parado sobre el piso**, no hundido (valida el FloorLevel).
- [ ] El badge de `Fire_B` se ve del mismo tamaño que los de A/C/K.
- [ ] Apuntar el chorro a un botón llena la barra; un barrido rápido **no** lo activa.
- [ ] El texto de los botones se lee bien, no espejado.
- [ ] **Tutorial**: los 4 fuegos en orden; solo las bahías efectivas están activas, el resto grises.
- [ ] **Aleatorio**: un solo fuego, distinto entre partidas.
- [ ] **Personalizado**: aparece el fuego elegido.
- [ ] Al apagar un fuego **no** te teletransporta: aparece "Continuar".
- [ ] El panel se alcanza con el brazo sin caminar.
- [ ] Con el agente equivocado el fuego se aviva y suena el aviso.
- [ ] Framerate estable con el galpón + VFX.

## 7. Gotchas de esta fase

- **El cableado de escena falla en silencio.** Ninguno de los bugs de agosto daba error de
  compilación ni excepción. Ante "el código está pero no funciona", correr
  **`Extintores ▸ Validate Setup`** ANTES de tocar código.
- **No editar `.unity` / `.prefab` a mano.** Usar un editor tool (`SerializedObject` + `Undo`). Los
  cuatro bugs de agosto salieron de escribir YAML a mano.
- **Reparar prefabs por el asset**, con `PrefabUtility.LoadPrefabContents` → `SaveAsPrefabAsset`.
  Re-ejecutar un builder que hace `SaveAsPrefabAsset` sobre una raíz nueva sobreescribe el asset y
  le rompe los overrides a las instancias ya colocadas.
- **Medir siempre el tri-count de un asset nuevo** con `Extintores ▸ Optimize FireCause Models`. El
  cartón de clase A traía 1,98 M de triángulos —100× los otros props juntos— y nada lo delataba
  salvo el tamaño del archivo.
- **Los builders son idempotentes**: `Build Training Scene` borra y rehace su propio
  `TrainingSetup`, así que se puede re-ejecutar sin duplicar nada.

## 8. Menús disponibles

| Menú | Qué hace |
|---|---|
| `Extintores ▸ Validate Setup` | Diagnostica la escena. No modifica nada. **Empezá por acá.** |
| `Extintores ▸ Fix Scene Wiring` | Repara el cableado (managers, sensor, HUD, prefab de la pared). |
| `Extintores ▸ Build Warehouse Blockout` | Genera `Prefabs/Warehouse_Blockout.prefab`. |
| `Extintores ▸ Build Training Scene` | Arma el ejercicio sobre la escena abierta. |
| `Extintores ▸ Build Fire Prefabs (A B C K)` | Regenera `Prefabs/Fires/Fire_X.prefab`. |
| `Extintores ▸ Build Extinguisher Wall` | Regenera el panel de extintores. |
| `Extintores ▸ Optimize FireCause Models` | Comprime mallas y reporta tri-count. |
| `Extintores ▸ Fix Sketchfab Textures` | Corrige importadores de textura de los props. |
| `Extintores ▸ Add ABC Label to Selected` | Pega la calcomanía ABC en el tanque. |
