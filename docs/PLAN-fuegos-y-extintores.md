# Plan — Tipos de fuego, tipos de extintor, selección y cambio

> Documento vivo. Aterriza el roadmap de la memoria en tareas concretas. Ver `CLAUDE.md`
> para el detalle técnico de XR/OVR y `docs/` para futuros anexos.
> Estado base: agosto 2026 (Unity 6000.5.4f1, URP 17.5, OpenXR multimodal, Quest).

## 1. Qué ya está resuelto (código)

| Pieza | Archivo | Estado |
|---|---|---|
| Chorro a dos manos, grip derecho, colisión de partículas | `ExtinguisherSpray.cs` | ✅ funciona |
| Fuego apagable por impacto de partículas + mengua + evento `Extinguished` | `FireSource.cs` | ✅ probado en editor |
| Conteo de fuegos y condición de éxito | `FireManager.cs` | ✅ probado en editor |
| **Clases de fuego A/B/C/K + agentes + matriz de efectividad** | `ExtinguishTypes.cs` | ✅ código listo |
| Agente por extintor (`ExtinguisherSpray.Agent`) y clase por fuego (`FireSource.Class`) | ambos | ✅ dropdowns en inspector |
| Aviso de agente equivocado (`WrongAgentUsed`, throttled) | `FireSource.cs` | ✅ código listo |

**Conclusión:** la *lógica* de distinguir tipos ya existe. Lo que falta es (a) **distinción
visual** de cada clase de fuego, (b) el **mecanismo de cambio de extintor**, y (c) los **assets**
de las causas de fuego. Este plan cubre esos tres.

## 2. Modelo conceptual (fijamos vocabulario)

- **Clase de fuego** (`FireClass`): A sólidos, B líquidos inflamables, C eléctrico, K cocina.
- **Agente** (`AgentType`): Water, Foam, CO2, DryChemABC, DryChemBC, WetChemical.
- **Efectividad** = `ExtinguishMatrix.Effectiveness(agent, clase)` → multiplicador:
  `1` apaga, `~0` inútil, `<0` **aviva** (extintor equivocado). Es el núcleo pedagógico.

Mapa "causa realista → clase → extintor correcto" que guía escena y assets:

| Causa (escenografía) | Clase | Extintor correcto | Extintor peligroso |
|---|---|---|---|
| Pila de leña / palés / cartón | A | Agua, Espuma, ABC | — |
| Charco de combustible / vehículo / pintura | B | Espuma, CO2, ABC/BC | Agua (esparce) |
| Tablero eléctrico / fusebox energizado | C | CO2, ABC/BC | **Agua/Espuma** (electrocución) |
| Hornalla / sartén con grasa | K | Químico húmedo | **Agua** (explosión de vapor) |

## 3. Cambio de extintor — "pared de recarga" (dock-swap)

### Restricción de diseño (decidida con el usuario, ago 2026)
El tanque va **atado al mando derecho** (`ExtinguisherMount.followController`); el usuario **no
agarra/suelta** el extintor ni interactúa con un menú sobre él. Por eso se descartan: rack de
agarrar (no hay grab), ciclar con botón y menú radial.

### Mecánica elegida
Una **estación en la pared** con varias **bahías rotuladas** (color + letra de clase, estilo
etiqueta real de extintor). El usuario **acerca/introduce** el tanque que lleva en la mano dentro
de una bahía; el extintor de su mano **cambia de agente** y su **apariencia** (color de cuerpo +
etiqueta) pasa a la de esa bahía. No se suelta nada: es un *swap por volumen-trigger*.

### Diseño técnico
Nuevos scripts en `Assets/Scripts/Extintores/`:

- `ExtinguisherStation.cs` — contenedor; opcional, agrupa bahías y expone la lista.
- `ExtinguisherBay.cs` — una bahía:
  - `AgentType agent;` `Color bodyColor;` `string label;` (o sprite de etiqueta).
  - Trigger `Collider` (kinematic `Rigidbody`) en su propia **layer nueva `Interactor`**
    (NO la layer `Fire`, para no interferir con la colisión del chorro).
  - `OnTriggerEnter/Stay(other)`: si `other` es el tanque (tag/`ExtinguisherRig` o componente
    `ExtinguisherSwappable`), y hay **dwell ≥ ~0.3 s** (evita swaps accidentales al pasar),
    llama `swappable.SetAgent(agent, bodyColor, label)`.
  - Feedback al confirmar: pulso háptico (`ExtinguisherHandle.Pulse`), sonido corto, breve
    "flash"/escala de la bahía.
- `ExtinguisherSwappable.cs` (en `ExtinguisherRig`) — recibe el cambio:
  - Referencia al `ExtinguisherSpray` y al `Renderer`/material del cuerpo y a la etiqueta.
  - `SetAgent(AgentType, Color, string)`: setea el agente y actualiza color + etiqueta.
  - Necesita un **collider "sensor"** en el tanque (trigger, layer `Interactor`) para que las
    bahías lo detecten. Debe ser chico y estar en la punta/cuerpo del tanque.

Cambio mínimo requerido en código existente:
- `ExtinguisherSpray`: hoy `agent` es `[SerializeField] private`. Agregar
  `public void SetAgent(AgentType a) => agent = a;` para poder cambiarlo en runtime.

### Presentación de la pared (rótulos)
Cada bahía muestra el **color estándar** y la **letra**:
Agua/Espuma → clase A (verde/crema), ABC → polvo (rojo/blanco "ABC"), CO2 → B-C (negro), Químico
húmedo → K (canela). No hace falta realismo cromático perfecto; sí que cada bahía sea
inequívoca (color + letra grande + ícono de tipo de fuego permitido).

### Chequeo de "extintor correcto" (opcional, refuerza aprendizaje)
El HUD/estación puede resaltar en verde la bahía adecuada para el fuego más cercano y en rojo la
peligrosa, leyendo `FireSource.EffectivenessOf(agent)`.

### Identidad visual del extintor (ABC · hecho)
El extintor actual es el **universal ABC** (`ExtinguisherSpray.agent = DryChemABC`, default). Se le
da identidad con una **etiqueta/calco**: textura `Assets/Extintores/Labels/Label_ABC.png` (generada
por código, no asset externo) aplicada con el tool **Extintores ▸ Add ABC Label to Selected** (crea
un Quad hijo + material URP/Lit con alpha-clip). El mismo patrón de etiqueta (color + letras) se
reutiliza para rotular las bahías de la pared de recarga (Bloque 2) y los futuros tipos de extintor.

## 4. Distinción visual de los fuegos por clase

1. **Escenografía de la causa** (lo más importante): cada `FireSource` se coloca **sobre** un
   prop que comunica la clase (leña, bidón, tablero, hornalla). Ver §5 assets.
2. **Variante de VFX por clase** usando el pack ya instalado
   (`Assets/Vefects/Free Fire VFX URP/Particles/`):
   - A (sólidos): `VFX_Fire_01_Medium` / `_Big` (llama vertical volumétrica).
   - B (líquido derramado): `VFX_Fire_Floor_01/02` (fuego bajo y extendido).
   - C (eléctrico): `VFX_Fire_01_Small` + tinte azulado/blanco y chispas (partícula extra).
   - K (sartén): `VFX_Fire_01_Medium` naranja intenso + humo (`_Smoke`).
   - El tinte se ajusta en el `ParticleSystem` del prefab; no requiere código nuevo.
3. **Badge de clase opcional** (modo entrenamiento): una etiqueta flotante con la letra sobre
   cada fuego, toggle global para "modo examen" sin ayudas.

Prefab sugerido por clase: `Fire_A`, `Fire_B`, `Fire_C`, `Fire_K` = prop de causa + VFX +
`FireSource` (con `Class` seteada) + `SphereCollider` (auto). Se registran solos en `FireManager`.

## 5. Assets gratis — análisis Opción 1 (PBR realista) vs Opción 2 (estilizado)

Pediste que analice ambas. Hallazgos concretos de la búsqueda:

### Opción 1 — PBR realista (igualar al extintor actual)
El extintor actual es un modelo realista low-poly con textura PBR. Para las causas hay props
realistas, **pero casi todos de packs distintos y de autores distintos**:
- Leña: *Wood Package — PBR Logs, Firewood and Stumps* (Asset Store, **de pago**); alternativa
  gratis: modelos sueltos de CGTrader/Sketchfab (calidad y licencia dispares).
- Tablero eléctrico: *Fuse Box Model Pack* (SpeedTutor) y *Rusty Electrical Boxes* (**de pago**);
  gratis: fusebox sueltos en CGTrader (uno por uno, sin coherencia entre sí).
- Cocina/hornalla: *Kitchen Stove 3D (URP/HDRP)*, *Kitchen Appliance Low Poly* (**de pago**).

**Problema central:** juntar un set realista **gratis** obliga a mezclar autores → densidad de
téxel, escala, iluminación y estilo **no coinciden entre sí ni con el extintor**. Además suelen
traer texturas 4K → más pesado para Quest. Es la ruta que **peor** cumple tu requisito de
"calidad y animación similar entre sí".

### Opción 2 — estilizado uniforme (un solo estilo, CC0/gratis)
Librerías de **un mismo autor** → coherencia garantizada y peso ideal para Quest:
- **Kenney** (CC0, sin atribución, 100% gratis): *Survival Kit* (leña, bidones/jerrycans para B),
  *City Kit: Industrial* (cajas/tableros eléctricos para C), *Prototype/props* variados. Un
  único estilo limpio en todo.
- **Quaternius** (CC0): *LowPoly House Interior* (hornalla/horno para K), *Nature* (troncos para
  A). Misma familia flat-shaded.
- **Synty POLYGON Starter Pack** (**gratis** desde la Synty Store, con su EULA): sampler del
  estilo POLYGON; los packs *Office* (props de cocina) y *Construction* (cables, herramientas,
  pilas de chatarra, eléctrico) son el superset coherente **si más adelante hay presupuesto**.
  ⚠️ Usar solo Synty Store / Unity Asset Store; **evitar** los mirrors "free download"
  (unityassetcollection, unityassets4free, etc.) que son piratería y riesgo legal.

**Para igualar el extintor:** como el actual es realista, en esta opción conviene **cambiar el
mesh del extintor por uno estilizado** (Kenney y Synty incluyen extintor). El *rig* no cambia:
se reemplaza solo el hijo visual de `ExtinguisherRig`, conservando `ExtinguisherMount`/calibración.

### Recomendación (análisis) vs. Decisión tomada
El análisis favorecía la Opción 2 por coherencia garantizada. **Decisión del usuario (ago 2026):
Opción 1 — PBR realista.** Ventaja concreta: **se mantiene el extintor realista actual** (no hay
que cambiar el mesh) y el look es más cercano a un entrenamiento real. Contrapartida a mitigar:
como los props vienen de autores distintos, hay que **elegir modelos de calidad/escala similar y
unificar la iluminación** (mismos URP settings, mismo ambiente) para que no canten entre sí, y
vigilar el peso en Quest (evitar 4K, atlasar/optimizar). Lista concreta en el **Anexo A**.

## 6. Plan de trabajo por bloques

**Bloque 0 — Layers y decisión de estilo** *(rápido, editor)*
- [x] Crear layer `Interactor` en TagManager (separada de `Fire`) — índice 7. *(hecho)*
- [x] Decisión: **Opción 1 — PBR realista** (revierte la recomendación; mantiene el extintor actual). *(hecho)*
- [x] Props PBR descargados y ubicados en `Assets/ThirdParty/FireCauses/` (A cartón, B autos,
      C tablero/fusebox, K hornalla). Créditos en `docs/CREDITS.md`. *(hecho)*
- [x] Materiales ya en `URP/Lit` y texturas capadas a 2048 — verificado, **ninguno sale rosa**
      (`Extintores ▸ Fix Sketchfab Textures` ya corrió). *(hecho)*
- [x] Elegida UNA causa por clase: A `Cardboard_Box`, B `Sports_Car`, C `Electrical_Breaker_Panel`,
      K `Gas_Stove`. `Audi_Novulari` (37 MB, redundante) **eliminado**; `VR_Fuse_Box` (537 KB) se
      conserva como recambio de C. *(hecho)*
- [x] Peso de malla medido con **Extintores ▸ Optimize FireCause Models** (sept 2026):
      | Modelo | Triángulos |
      |---|---|
      | Cardboard_Box (A) | **1.982.529** ⚠️ |
      | sportcar.017 (B) | 5.042 |
      | Tz-ExteriorElectricBox2 (C) | 4.698 |
      | gas_stove_main (K) | 7.942 |
      | FuseBox (alt. C) | 1.172 |
      El cartón (escaneo Tripo) tenía **100× más triángulos que los otros tres juntos** y ~6×
      el presupuesto completo de un frame en Quest. **Decimado a 8.000 tri** con Blender
      headless (60,9 MB → 396 KB); el original queda recuperable en la historia de git/LFS.
      Receta en §7. *(hecho)*
- [ ] Unificar iluminación entre props (mismo ambiente/URP settings) y revisar escala de cada FBX.

**Bloque 1 — Prefabs de fuego por clase**
- [x] `FirePrefabBuilder` (menú **Extintores ▸ Build Fire Prefabs (A B C K)**) arma cada `Fire_X` =
      raíz + hijo `Flame` (VFX + `FireSource[clase]`, solo esto mengua) + hijo `Cause` (FBX de la
      causa anidado: A cartón, B auto, C tablero, K hornalla). *(hecho)*
- [x] Menú ejecutado → `Assets/Prefabs/Fires/Fire_A|B|C|K.prefab` generados. *(hecho)*
- [x] Los 4 colocados en `SampleScene`; `FireManager` los recoge solo. *(hecho)*
- [ ] En cada prefab: posicionar `Flame` sobre `Cause` (capó, hornallas, etc.) y ajustar escala FBX.

**Bloque 2 — Pared de recarga (cambio de extintor)**
- [x] `ExtinguisherSpray.SetAgent(...)` público. *(hecho)*
- [x] `ExtinguisherSwappable`, `ExtinguisherBay` (trigger + dwell + swap + háptica), `ExtinguisherStation`. *(hecho)*
- [x] Etiquetas por agente (Water/Foam/CO2/ABC/WetChemical) generadas en `Assets/Extintores/Labels`. *(hecho)*
- [x] `WallBuilder` (menú **Extintores ▸ Build Extinguisher Wall**) → prefab `ExtinguisherStation`
      con 5 bahías (panel + marco de color + etiqueta + trigger). *(hecho)*
- [x] Menú ejecutado, `ExtinguisherStation` colocada en la escena. *(hecho)*
- [x] `ExtinguisherSwappable` en el `ExtinguisherRig`. *(hecho)*
- [ ] ⚠️ El `SwapSensor` quedó **inservible** (`isTrigger` off, layer Default, 1 cm) y el prefab de
      la pared **sin `BayHighlighter`**. Lo repara `Extintores ▸ Fix Scene Wiring` (Bloque 6).
- [ ] Probar el swap en el visor (dwell ~0.3 s) y que el agente elegido apaga/aviva según la matriz.

**Bloque 3 — Feedback pedagógico**
- [x] `TrainingManager` consume `WrongAgentUsed` → háptica + audio (AudioClip cableable) + UnityEvent
      `onWrongAgent`; y `AllFiresExtinguished` → `onAllExtinguished`. *(hecho)*
- [x] `FireClassBadge` (billboard A/B/C/K sobre cada fuego, textura por clase) + toggle "modo examen"
      con botón del mando (`FireClassBadge.GloballyVisible`). El `FirePrefabBuilder` lo agrega solo. *(hecho)*
- [ ] ⚠️ En escena el GO `'TrainingManager '` lleva un **`FireManager`** duplicado, no el
      `TrainingManager`: sin temporizador, sin aviso de agente equivocado y sin modo examen.
      Lo repara `Extintores ▸ Fix Scene Wiring` (Bloque 6).
- [x] `BayHighlighter` resalta la bahía correcta (verde) / peligrosa (roja) según el fuego más
      cercano (`EffectivenessOf` + MaterialPropertyBlock). El WallBuilder lo agrega solo. *(hecho)*

**Bloque 4 — Técnica PASS + tiempo + HUD** *(código listo)*
- [x] `FireSource`: técnica **PASS** — pondera cada impacto según pegue en la BASE (peso 1) o más
      arriba (peso `offBaseFactor`); expone `LastAimQuality01`/`LastHitTime`. *(hecho)*
- [x] `TrainingManager`: **temporizador** (arranca con fuegos, para en éxito) + datos de HUD
      (`Remaining`/`Total`/`CurrentAgent`/`TimeText`) + eventos C# `WrongAgent`/`Success`. *(hecho)*
- [x] `TrainingHud`: HUD world-space con `TextMesh` (sin depender de UGUI/TMP): fuegos, tiempo,
      agente, aviso "extintor equivocado" y cartel de éxito con tiempo final. *(hecho)*
- [ ] En escena no hay ningún `TrainingHud`. Lo crea `Extintores ▸ Fix Scene Wiring` (Bloque 6),
      colgado del `CenterEyeAnchor`; después ajustar posición/tamaño a gusto.

**Bloque 6 — Reparación del cableado de escena** *(agosto 2026)*
- [x] `SceneWiringFixer` (`Assets/Scripts/Extintores/Editor/`) con dos menús: **Extintores ▸
      Validate Setup** (solo diagnostica) y **Extintores ▸ Fix Scene Wiring** (repara). Idempotente,
      con Undo, busca por componente y no por ruta de jerarquía. *(hecho)*
      Repara: FireManager duplicado · TrainingManager faltante y su cableado · `SwapSensor`
      (trigger + layer `Interactor` + tamaño desde los bounds del tanque + Rigidbody kinemático) ·
      `BayHighlighter` en el **asset** del prefab de la pared · `TrainingHud`.
- [x] `ModelImportOptimizer` (**Extintores ▸ Optimize FireCause Models**): `meshCompression=High`,
      `optimizeMesh`, `isReadable=off`, y reporte de vértices/triángulos por modelo. *(hecho)*
- [ ] Correr `Validate Setup` → `Fix Scene Wiring` → `Validate Setup` (debe dar cero) y **guardar
      la escena (Ctrl+S)**.

**Bloque 5 — Prueba en visor** *(imprescindible)*
- [ ] Build & Run en Quest: colisión de partículas y pose de mandos **no** llegan bien por Link.
- [ ] Verificar: agente equivocado no apaga/aviva; swap en la pared; victoria por `FireManager`.

## 7. Riesgos / gotchas (además de los de `CLAUDE.md`)
- **No usar la layer `Fire` para el sensor del tanque/bahías** → interferiría con `collidesWith`
  del chorro. Usar layer `Interactor` dedicada.
- **Swap accidental:** exigir dwell + confirmación háptica; no cambiar de agente con solo rozar.
- **Materiales de assets nuevos en URP:** los packs pueden venir en shader Built-in → salen
  rosa/magenta. Convertir con Render Pipeline Converter o a `URP/Lit` (ver `CLAUDE.md`).
- **Coherencia visual:** con la Opción 1 (realista, elegida) los props vienen de autores distintos;
  unificar iluminación (mismo ambiente/URP settings) o el contraste canta.
- **Peso en Quest:** limitar cantidad de VFX simultáneos y texturas 4K. **Ojo con los escaneos
  (Tripo/photogrammetry):** el cartón de clase A traía 1,98 M de triángulos — 100× los otros tres
  props juntos — sin que nada lo delatara salvo el tamaño del archivo. Medir SIEMPRE con
  `Extintores ▸ Optimize FireCause Models` antes de dar un prop por bueno; `meshCompression`
  achica el archivo pero **no** el costo de dibujarlo.
- **Decimar un FBX sin abrir Blender a mano** (receta usada con el cartón):
  ```
  blender --background --python decimate_fbx.py -- <in.fbx> <out.fbx> <tris_objetivo>
  ```
  El script aplica un modifier Decimate/Collapse y re-exporta. Clave: **conservar los nombres de
  objeto, malla y material** — Unity referencia los sub-assets del FBX por nombre, así que si
  cambian, los prefabs que los usan quedan apuntando a nada y el modelo desaparece sin error.
  Con el normal map a resolución completa la pérdida de detalle casi no se nota.
- **El cableado de escena falla en silencio.** Ninguno de los cuatro bugs de agosto 2026 (manager
  duplicado, componente equivocado, sensor sin `isTrigger`, prefab desactualizado) daba error de
  compilación ni excepción en runtime: simplemente no pasaba nada. Ante "el código está pero no
  funciona", correr **Extintores ▸ Validate Setup** ANTES de tocar código.
- **Editar el `.unity`/`.prefab` a mano es la causa raíz de esos bugs.** Para cambios de escena
  usar un editor tool (`SerializedObject` + `Undo`), no YAML escrito a mano.
- **Reparar prefabs por el asset, no por la instancia:** `PrefabUtility.LoadPrefabContents` →
  `SaveAsPrefabAsset`. Re-ejecutar un builder que hace `SaveAsPrefabAsset` sobre una raíz nueva
  sobreescribe el asset y le rompe los overrides a la instancia ya colocada.

## Anexo A — Assets concretos a descargar (Opción 1 · PBR realista)

Prioridad: props realistas de **calidad y escala similar** para las 4 causas. Se prefieren los
**gratis**; los de pago quedan como upgrade opcional. El extintor **se mantiene** (el realista
actual); si se quiere mejorar, *Fire Extinguisher PBR*.

**Clase A — Leña / troncos**
- Gratis: Awesome Free Scans – Wood Logs (Freya Gameworks) —
  <https://assetstore.unity.com/packages/3d/props/exterior/awesome-free-scans-wood-logs-216756>
- Pago: Wood Package – PBR Logs, Firewood and Stumps —
  <https://assetstore.unity.com/packages/3d/props/wood-package-pbr-logs-firewood-and-stumps-88141>
- Pago: Logs Sticks Split Lumber PBR —
  <https://assetstore.unity.com/packages/3d/vegetation/trees/logs-sticks-split-lumber-pbr-106939>

**Clase B — Líquido inflamable: CHARCO o VEHÍCULO** (no bidón)
- Charco (gratis): Oil Spill (L3v3n, Sketchfab) —
  <https://sketchfab.com/3d-models/oil-spill-20c9c6bf32ee4f9fbcef4478972ccc15>
- Charco (gratis): Water Puddle Plane Low Poly (16 charcos, Sketchfab; re-teñir a combustible) —
  <https://sketchfab.com/3d-models/water-puddle-plane-low-poly-96b655839d804dbcae4cd2101c27f577>
- Vehículo (gratis): Low-Poly Sports Car Free (donnichols, Sketchfab) —
  <https://sketchfab.com/3d-models/cartoonish-low-poly-sports-car-pbr-free-2b23201839fb43049454f431752073bb>
- Vehículo (gratis): Game-Ready Sports Car LP PBR (Sketchfab, 4K → optimizar) —
  <https://sketchfab.com/3d-models/game-ready-sports-car-low-poly-pbr-textured-f95ed604c61541b5b09caebc2e494d5b>
- (El charco combina de una con `VFX_Fire_Floor_01`; el vehículo lleva el fuego sobre el capó/motor.)

**Clase C — Tablero eléctrico / caja de fusibles**
- Gratis: Electrical Breaker Panel Box – LP (Sketchfab) —
  <https://sketchfab.com/3d-models/electrical-breaker-panel-box-lp-model-4b12b4e00fc14f95beb55d7016eaf28d>
- Gratis: VR-Ready Fuse Box (Sketchfab) —
  <https://sketchfab.com/3d-models/vr-ready-fuse-box-e7ffc35358af4ae0b981f4bd2eadb38a>
- Pago: Fuse Box Model Pack (SpeedTutor) —
  <https://assetstore.unity.com/packages/3d/props/fuse-box-model-pack-268040>

**Clase K — Hornalla / cocina**
- Gratis: Gas Stove Kitchen (Yousof Hajizeinali, Sketchfab) —
  <https://sketchfab.com/3d-models/gas-stove-kitchen-f82172b787854191a55b3a5dfd5ede0b>
- Gratis: Gas Stove – Zline, game-ready PBR (Serious Black 19, Sketchfab) —
  <https://sketchfab.com/3d-models/gas-stove-dffe81231c954260af941400810dcafd>
- Pago: Kitchen Stove 3D (URP/HDRP) – BHS —
  <https://assetstore.unity.com/packages/3d/props/interior/kitchen-stove-3d-asset-urp-hdrp-built-in-supported-bhs-290767>

**Extintor (opcional, mejora del actual)**
- Fire Extinguisher PBR —
  <https://assetstore.unity.com/packages/3d/props/industrial/fire-extinguisher-pbr-101382>

> **Licencias Sketchfab:** revisar CADA modelo — muchos son **CC-BY** (acreditar al autor en los
> créditos del TP) y algunos son "Buy" (pago). Los de Unity Asset Store "gratis" van bajo su EULA.
> **URP:** al importar, convertir materiales que salgan rosa (Render Pipeline Converter → URP/Lit).
> **Quest:** preferir modelos low/mid-poly y bajar texturas 4K a 1–2K; unificar iluminación para
> que los props de distintos autores no canten entre sí.
