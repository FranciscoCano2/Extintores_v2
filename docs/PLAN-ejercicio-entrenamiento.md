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

La **segunda** prueba en visor (septiembre) destapó otras cuatro, con la misma firma: código escrito,
cero errores, nada funcionando.

| Síntoma reportado | Causa real |
|---|---|
| "Estoy a la altura del suelo" | `_trackingOriginType` vive en **`OVRManager`**, no en `OVRCameraRig`. `TrainingSceneBuilder.FixTrackingOrigin` hacía `SerializedObject(ovrCameraRig).FindProperty("_trackingOriginType")` → `null` → `return` sin tocar nada **ni avisar**. La escena nunca dejó de estar en EyeLevel (el default del prefab del SDK). |
| "El cartel se gira conmigo, nunca lo puedo leer" | El HUD colgaba del `CenterEyeAnchor`: clavado a la cara, gira exacto con la cabeza. Además el `TextMesh` era gigante (`fontSize 90 × characterSize 0.05` ≈ **0,45 m por renglón**) y anclado arriba-izquierda, así que se derramaba hacia el rincón inferior derecho. |
| "El panel de matafuegos es muy grande" | Achicar el panel en el código de `WallBuilder` **no achica el asset ya generado**. `ExtinguisherStation.prefab` seguía midiendo 2,45 × 1,3 m; el plan lo daba por hecho desde agosto. |
| "El apretado de los botones tiene que ser más rápido" | `SprayButton` contaba partículas. La cantidad que llega depende de la distancia (el cono se abre: a 3 m entra ¼ de lo que entra a 1,5 m) y de cuánto se apriete el grip → imposible de calibrar, y en el visor lentísimo. |

La **tercera** prueba (también septiembre), ya con lo anterior aplicado:

| Síntoma reportado | Causa real |
|---|---|
| "El panel lo tengo al lado mirando hacia atrás" | El `PanelAnchor` se orientaba **igual que el usuario**. Para un cartel AL FRENTE eso está bien (su −Z le queda de cara), pero el panel va al COSTADO: ahí el mismo yaw apunta su −Z a la espalda del usuario. Faltaban 90°. |
| "Las partículas golpean varios carteles" | El cono del chorro se abre ~14°: a 1,5 m ya cubre 37 cm de radio, más que la separación entre botones. Apuntar con él es intrínsecamente ambiguo. |
| "El botón Continuar no responde a ningún estímulo" | No estaba muerto: estaba **a la espalda**. Se plantaba en un punto fijo del mundo derivado del `PlayerSpawn` del puesto, y el usuario se había girado (justamente, para usar el panel). Nunca lo veía. |

La **cuarta** ronda fue de ajuste, no de bugs — salvo una trampa que no se ve venir:

| Pedido | Qué implicó |
|---|---|
| Láser sólo sobre un botón | Un rayo permanente es ruido visual mientras se apaga fuego. Verlo aparecer ya es la señal de "acá hay algo". |
| Aparecer más lejos del auto | La causa de clase B es un auto de ~4,5 m y el spawn estaba a 2,2 m del anchor: el usuario salía encima del paragolpes. **Ojo al techo:** el chorro llega a `speed × lifetime` = 6 × 0,6 = **3,6 m**. Más lejos que eso y el puesto es imposible sin ningún error que lo explique. Se fue a 3,0 m. |
| Color real del cuerpo del extintor | La textura del pack es un tanque **rojo**, y URP/Lit multiplica `_BaseMap × _BaseColor`: rojo × azul = negro. Teñir directamente no da un extintor azul, da uno sucio. Hay que teñir sobre una copia **desaturada**. |
| Accionar disparando, no al instante | Apuntar y confirmar pasan a ser dos actos distintos. Posar el rayo ya no hace nada. |

La **quinta** ronda:

| Síntoma reportado | Causa real |
|---|---|
| "Personalizado se superpone con el cartel de nivel superado" | `TrainingHud._done` no se bajaba nunca: "NIVEL SUPERADO" quedaba colgado para siempre, y en el lobby vive a la misma altura que el menú. |
| "El tiempo permanece en 0" | `TrainingManager.Start()` hacía `TimerRunning = Total > 0`, pero `autoCollectOnStart` está apagado **a propósito**, así que al arrancar hay 0 fuegos y el reloj no se encendía nunca. Los fuegos los elige `TrainingSession` mucho después. |
| "El extintor K, ¿no es plateado?" | Sí. El químico húmedo se fabrica en acero inoxidable; estaba amarillo, que es el código británico BS EN 3. |

## 2. Decisiones tomadas (y por qué)

- **Los botones se apuntan con un LÁSER, no con el chorro.** El chorro se probó primero y falló por
  física, no por implementación: un cono de 14° cubre varios botones a la vez. `LaserPointer` sale
  de la misma punta de boquilla y en la misma dirección, así que apuntar a un botón y apuntar a un
  fuego siguen siendo el mismo gesto — pero el rayo pega en uno solo. Sigue sin entrar UGUI ni
  TextMeshPro.
- **Apuntar y confirmar son dos actos distintos.** El rayo señala; lo que acciona es **disparar el
  extintor** (el grip, el mismo gesto que apaga un fuego) sostenido ~⅓ de segundo sobre el botón.
  Posar el rayo por sí solo no hace nada: si bastara con posarlo, cruzar la mirada por el menú
  activaría cosas sin querer.
- **El rayo se ve sólo cuando toca un botón accionable.** Que aparezca ES la señal de que ahí hay
  algo que apretar, y no estorba mientras se apaga fuego.
- **El aspecto del cuerpo lo define `AgentColor`, en runtime y en un solo lugar.** Color, metallic y
  smoothness. Lo consumen la pared (para pintar cada bahía) y el tanque de la mano; ni la bahía
  serializa un color propio ni `SetAgent` lo recibe por parámetro. Cada copia era un lugar donde
  panel y extintor podían discrepar sin que nada lo delatara.
- **La paleta es didáctica, no normativa — con una excepción.** Ninguna norma pinta el cuerpo
  entero: IRAM y NFPA usan cuerpo rojo con etiqueta, BS EN 3 usa rojo con banda de color. Acá se
  pinta entero porque en VR, a 3 m, una etiqueta no se lee y el color sí — que es exactamente lo
  que el ejercicio enseña. La excepción es el de **clase K**: el químico húmedo se fabrica en acero
  inoxidable, así que ese va plateado y con `metallic` alto, que es su aspecto real.
- **El HUD se esconde fuera del ejercicio.** En el lobby no hay nada que informar y el cartel vive
  a la misma altura que el menú. Se sostiene 5 s tras ganar para poder leer el tiempo final.
- **Sin láser en escena, el chorro vuelve a accionar botones.** Un error de cableado degrada a la
  interacción vieja en vez de dejar la app sin ninguna forma de apretar nada.
- **"Continuar" se ancla a la MIRADA, no al puesto.** Se planta delante de la cabeza en el momento
  de mostrarse. Un botón en un punto fijo del mundo desaparece apenas el usuario se gira.
- **La carga de un botón se mide en SEGUNDOS, no en partículas.** Las partículas sólo dicen *si* el
  chorro está pegando; el reloj hace el resto (`fillSeconds`, hoy 0,35 s). Así el ritmo no depende
  de la distancia ni del grip, y deja de ser un parámetro que sólo se puede calibrar en el visor.
- **Elegir el extintor también se hace con el chorro.** Cada bahía tiene su botón `Selector`; meter
  el tanque en la bahía sigue andando como gesto realista. Un solo vocabulario de interacción para
  toda la app.
- **El HUD acompaña al usuario, pero no va pegado a la cabeza.** `BodyLockedPanel`: queda quieto en
  el mundo mientras mirás alrededor y se reacomoda —despacio— sólo cuando la mirada se va más de
  35°. Es el patrón de confort estándar en VR; clavarlo al ojo marea y no se puede leer.
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
de las bahías y el trigger de cada bahía miran hacia −Z.

De ahí salen **dos reglas, según dónde esté el cartel**, y confundirlas costó una sesión de visor:

| Dónde está | Yaw del anchor | Por qué |
|---|---|---|
| **Al frente** (menú del lobby) | igual que el usuario | Su −Z ya le queda de cara. |
| **Al costado** (panel de extintores) | usuario **+ 90°** | Con el mismo yaw que el usuario, el −Z apunta a su espalda: había que darse vuelta entero para leerlo. |

Lo resuelve `WarehouseBuilder.PanelRotation`.

Si algo se ve espejado, o el trigger de una bahía no dispara, o un rótulo queda tapado: **revisá
este eje antes que ninguna otra cosa**. El `WallBuilder` original ponía los rótulos en +Z y hubo que
corregirlos a mano en el prefab; quedó arreglado en origen.

## 4. Arquitectura

```
TrainingSession            ← orquesta: modo, cola de puestos, reposicionamiento
 ├── FireStation[]         ← un puesto = FireSource + PlayerSpawn + PanelAnchor
 ├── FireManager           ← cuenta SOLO los fuegos de la etapa (BeginSession)
 ├── ExtinguisherStation   ← el panel, se mueve al costado del usuario en cada puesto
 │    └── ExtinguisherBay[]  ← + SprayButton "Selector" por bahía
 └── SprayButton           ← "Continuar", y los 8 del menú
MainMenu                   ← 3 modos + submenú de 4 clases
LaserPointer               ← en el NozzleTip: apunta y acciona TODOS los SprayButton
TrainingHud + BodyLockedPanel ← cartel que te sigue con zona muerta (NO cuelga del ojo)
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
  `dwellSeconds` de `ExtinguisherBay`: evita que un barrido del chorro active algo sin querer. La
  carga se mide **en segundos de chorro sostenido** (`fillSeconds`), no en partículas — ver §1.
- `ExtinguisherBay.Interactable` apaga **también** el botón `Selector`. Si sólo bloqueara el
  trigger, el tutorial se saltearía entero apuntándole el chorro a la bahía equivocada.
- `BodyLockedPanel` usa histéresis: se engancha al salir de la zona muerta y no se suelta hasta
  alcanzar la cabeza. Sin eso el cartel arranca y frena en el borde de la zona, temblando.

**Layers:** `Fire` = 6 (chorro ↔ fuegos) · `Interactor` = 7 (swap de extintor) · `Interactable` = 8
(botones). Están separadas a propósito: mezclarlas rompe el `collidesWith` del chorro.

## 5. Estado

### Hecho ✅

- [x] `FireClassBadge` compensa la escala del padre (bug de "la B gigante").
- [x] **FloorLevel de verdad**: `OvrTrackingOrigin` toca `OVRManager` (no `OVRCameraRig`) y ahora
      `Validate Setup` lo reporta como problema si está en EyeLevel. Lo aplica `Fix Scene Wiring`,
      sin tener que rehacer la escena.
- [x] HUD `body-locked` (`BodyLockedPanel`) y texto a tamaño legible (~7 cm por renglón).
      `Fix Scene Wiring` lo desacopla del `CenterEyeAnchor` si lo encuentra colgado ahí.
- [x] Botones por tiempo (`fillSeconds` 0,35 s) en vez de por conteo de partículas.
- [x] Bahías seleccionables con el chorro (`SprayButton` "Selector" por bahía), gateadas por el
      tutorial igual que el trigger.
- [x] `WallBuilder` genera el panel a **1,1 × 0,62 m**, y `Validate Setup` detecta el asset viejo.
- [x] `LaserPointer` en la boquilla: rayo + punto, gatillo para accionar al toque y dwell como
      respaldo. Con láser presente el chorro deja de accionar botones.
- [x] `PanelAnchor` a 90° (`WarehouseBuilder.PanelRotation`), y `Fix Scene Wiring` gira los del
      prefab ya generado **sin** rehacer el galpón (conserva los fileID, así los `FireStation` de la
      escena siguen apuntando a sus anchors).
- [x] "Continuar" anclado a la mirada. Loguea su posición al aparecer, para que la próxima vez que
      no se vea quede dicho dónde estaba.
- [x] Láser visible sólo sobre un botón, y accionado con el disparo del extintor (no al posarlo).
- [x] Clase B a 3,0 m del fuego (`WarehouseBuilder.Zone.Approach`), fuera del auto y dentro del
      alcance del chorro. `Fix Scene Wiring` reubica el spawn y su panel en el prefab ya generado.
- [x] **El tanque se pinta del color de su tipo**: `AgentColor` + textura desaturada
      (`Extintores ▸ Build Extinguisher Body Material`) para que el tinte no salga negro. El de
      clase K va plateado con `metallic` alto (acero inoxidable).
- [x] Cronómetro arrancado por `FireManager.SessionBegan` (antes quedaba en 0 para siempre).
- [x] HUD oculto fuera del ejercicio y `_done` bajado al empezar una etapa: se acabó el
      "NIVEL SUPERADO" encima del menú.
- [x] El botón apuntado por el láser se resalta (placa agrandada + teñida).
- [x] **Sexta bahía, `DryChemBC`**: ya no hay agentes de la matriz inalcanzables en juego. Enseña
      algo que ABC no: el polvo BC **no** sirve para clase A.
- [x] **Los fuegos suenan.** `FireSource` crea su `AudioSource` 3D en loop; el crepitar baja junto
      con las llamas (mismo piso que la escala) y se desvanece con el fade de las partículas. Clips
      del propio pack de VFX: `SFX_FireBig_L` para el auto, `Small` para el tablero eléctrico.
- [x] `Flame` re-parenteado a la raíz en `Fire_B` (`Extintores ▸ Fix Fire Prefabs`), editando el
      asset para no romperle los fileID a los `FireStation` de la escena. `Validate Setup` detecta
      tanto la llama anidada como los fuegos sin sonido.
- [x] `SprayButton`: botones accionados por el chorro, con barra de carga y rótulo `TextMesh`.
- [x] Layer `Interactable` (8) + el chorro la incluye siempre.
- [x] `FireSource.ResetFire()` y `FireManager.BeginSession()` → se puede repetir un puesto.
- [x] `FireStation`: puesto desacoplado de la escenografía.
- [x] `TrainingSession`: modos tutorial / aleatorio / personalizado, "Continuar" sin teletransporte
      automático.
- [x] `MainMenu`: 3 opciones + submenú de 4 clases.
- [x] Gateo del tutorial (`ExtinguisherBay.Interactable` + gris en `BayHighlighter`).
- [x] `WarehouseBuilder` (blockout 24×16 m con zonas y anchors) y `TrainingSceneBuilder`.
- [x] `SceneWiringFixer` valida también el ejercicio.
- [x] **Escena `Assets/Scenes/Training.unity` generada y validada** — `Validate Setup` da cero
      problemas: 3 layers, 4 puestos, 9 botones, 1 `FireManager`, HUD cableado.
- [x] `Training.unity` primera en Build Settings (`SampleScene` queda como sandbox, deshabilitada).
- [x] **La convención −Z de §3 quedó confirmada en el visor**: el texto de los botones se lee bien,
      no espejado. No hay que dar vuelta ningún eje.

### Pendiente ⬜

- [ ] **Aplicar las correcciones en la escena** — el código está, la escena guardada no.
      `Extintores ▸ Fix Scene Wiring` → **Ctrl+S** → `Validate Setup`. Ver §6.
- [ ] **Build & Run en el Quest** — imprescindible: por Link no llegan ni la colisión de partículas
      ni la pose de los mandos. Checklist en §6.
- [ ] Ajustar en el visor: comodidad del HUD (`BodyLockedPanel`: `distance` 1,5 m, `heightOffset`
      −0,45 m, `deadZoneDegrees` 35°), grosor/alcance del rayo (`LaserPointer`) y el dwell de los
      botones (`fillSeconds` 0,35 s) si molesta accionar sin querer al barrer.
- [ ] El panel queda a 0,75 m del costado (`WarehouseBuilder.PanelSide`), medida pensada para
      alcanzarlo con el brazo. Con el láser ya no hace falta que esté al alcance: se podría alejar
      y agrandar. Decidirlo con el visor puesto.
- [ ] **Pack industrial: importado (278 MB en `Assets/RPG_FPS_game_assets_industrial`), sin
      preparar.** Verificado en los `.mat`: **los 36 materiales usan shaders Built-in**
      (`guid 0000…f000…`), así que todo el pack sale MAGENTA en URP. Antes de tocar el blockout:
  1. `Window ▸ Rendering ▸ Render Pipeline Converter` → *Built-in to URP* → **Material Upgrade**.
     Es mejor que cualquier conversor propio: mapea metallic/smoothness/normales bien.
  2. Seleccionar la carpeta del pack en el Project y correr `Extintores ▸ Fix Sketchfab Textures`
     — ahora también baja el tope de resolución de 2048 a **1024** (sólo toca el importador).
  3. Recién ahí, reemplazar el blockout zona por zona.
- [ ] Falta `Assets/Extintores/Labels/Label_BC.png` para la bahía nueva. El builder tolera la
      etiqueta faltante (el botón dice "BC"), pero el quad del rótulo queda liso.

## 6. Cómo correrlo

**Regenerar desde cero** (solo si hace falta rearmar):

1. Abrir la escena con los rigs calibrados y correr `Extintores ▸ Build Warehouse Blockout`.
2. `Extintores ▸ Build Training Scene` — trabaja **sobre la escena abierta** a propósito, para
   conservar los offsets de `ExtinguisherMount` que se calibraron a mano en el visor.
3. `File ▸ Save As…` → `Assets/Scenes/Training.unity`, y ponerla primera en Build Settings.
4. `Extintores ▸ Validate Setup` → debe dar **cero problemas**.

**Estado verificado el 10/09/2026** (leído del YAML, no del inspector): tracking origin en
FloorLevel ✓ · HUD suelto con `BodyLockedPanel` ✓ · `LaserPointer` en escena ✓ · los 5 anchors
(4 zonas + lobby) a 90° del spawn ✓ · Zone_B a 3,00 m del fuego y el resto a 2,20 ✓ · panel de
1,30 × 0,62 m con 6 bahías y 6 selectores ✓ · tanque con `M_Extinguisher_Body` y `tintBody` ✓.

**Aplicar las correcciones** (la escena guardada va por detrás del código):

1. `Extintores ▸ Fix Fire Prefabs` — llama a la raíz en `Fire_B` + crepitar en los 4 fuegos.
2. `Extintores ▸ Fix Scene Wiring` — cablea lo que falte en la escena.
3. **Ctrl+S**.
4. `Extintores ▸ Validate Setup` → debe dar **cero problemas**.

**Checklist de Build & Run:**

- [ ] Arrancás **parado sobre el piso**, no hundido (valida el FloorLevel).
- [ ] El cartel de tiempo/nivel se lee de frente, te espera quieto y sólo te sigue si te das vuelta.
- [ ] El badge de `Fire_B` se ve del mismo tamaño que los de A/C/K.
- [ ] El rayo **aparece sólo** al apuntar un botón, y no se ve mientras apagás fuego.
- [ ] El botón apuntado **se resalta** (crece y se tiñe), y se apaga al mover el rayo a otro.
- [ ] Posar el rayo no acciona nada; disparar el extintor sobre él lo acciona en ~⅓ de segundo,
      y **sólo al botón apuntado**.
- [ ] **El cronómetro corre** y se congela al apagar el último fuego.
- [ ] "NIVEL SUPERADO" se va solo a los ~5 s y **no tapa el menú** al volver al lobby.
- [ ] El extintor de clase K se ve de **acero inoxidable**, no gris mate.
- [ ] El panel de extintores se lee **girando la cabeza al costado**, sin darse vuelta entero.
- [ ] Al elegir una bahía, **el tanque entero cambia de color** (azul agua, negro CO2, amarillo K),
      no sólo la etiqueta. Arranca rojo (ABC).
- [ ] En clase B aparecés **fuera del auto**, y el chorro sigue llegando al fuego.
- [ ] **Los fuegos suenan**, más fuerte cuanto más cerca, y el crepitar se apaga con las llamas.
- [ ] El badge de `Fire_B` sigue del tamaño correcto después de re-parentar la llama.
- [ ] Al apagar el fuego, "CONTINUAR" aparece **delante tuyo mires donde mires**, y responde.
- [ ] **Tutorial**: los 4 fuegos en orden; solo las bahías efectivas están activas, el resto grises
      **y con el botón gris** (el láser no las acciona).
- [ ] **Aleatorio**: un solo fuego, distinto entre partidas.
- [ ] **Personalizado**: aparece el fuego elegido.
- [ ] Al apagar un fuego **no** te teletransporta: aparece "Continuar".
- [ ] El panel se alcanza con el brazo sin caminar.
- [ ] Con el agente equivocado el fuego se aviva y suena el aviso.
- [ ] Framerate estable con el galpón + VFX.

## 7. Gotchas de esta fase

- **El cableado de escena falla en silencio.** Ninguno de los bugs de agosto ni de septiembre daba
  error de compilación ni excepción. Ante "el código está pero no funciona", correr
  **`Extintores ▸ Validate Setup`** ANTES de tocar código.
- **Cambiar el código de un builder NO cambia el asset ya generado.** El panel se achicó en
  `WallBuilder.cs` en agosto y el prefab siguió midiendo 2,45 × 1,3 m hasta septiembre, con el plan
  marcándolo como hecho. Si tocás un builder, **corré el menú** y verificá el asset.
- **`SerializedObject.FindProperty` devuelve `null` sin quejarse.** El arreglo del FloorLevel buscaba
  `_trackingOriginType` en `OVRCameraRig`, pero el campo está en `OVRManager` (mismo GameObject,
  otro componente). El `if (p != null)` se saltaba el bloque entero y no había ni un warning. Si
  escribís código que setea una propiedad serializada, **logueá también el caso `null`**.
- **No colgar carteles del `CenterEyeAnchor`.** Queda clavado a la cara: no se puede mirar de frente
  y marea. Usar `BodyLockedPanel`.
- **Un botón que aparece a mitad de partida va anclado a la MIRADA, no al mundo.** El usuario se
  gira y camina dentro del guardian; cualquier punto fijo derivado de un anchor puede quedarle a la
  espalda. Un botón que no se ve es indistinguible de un botón roto — se perdió una sesión entera
  buscando el bug de cableado de "Continuar", que estaba perfectamente cableado.
- **El chorro no sirve para elegir.** Cono de 14° = 37 cm de radio a 1,5 m, más que la separación
  entre botones. Para apuntar, `LaserPointer`.
- **El alcance del chorro es el techo de la distancia de aproximación.** `speed × lifetime` = 3,6 m.
  Alejar un puesto más allá de eso lo vuelve imposible y no da ningún error: las partículas
  simplemente mueren en el aire.
- **Un latch de estado necesita alguien que lo baje.** `TrainingHud._done` se prendía al ganar y no
  se apagaba nunca: el cartel de victoria quedó colgado sobre el menú del lobby. Lo mismo con
  `FireManager._completed`, que ya había mordido en agosto.
- **`autoCollectOnStart` apagado significa que en `Start()` NO hay datos todavía.** El cronómetro se
  inicializaba ahí leyendo `Total`, que valía 0, y se quedaba en 0 para siempre. Todo lo que dependa
  de los fuegos de la etapa tiene que colgarse de `FireManager.SessionBegan`, no de `Start()`.
- **URP/Lit multiplica `_BaseMap × _BaseColor`.** Para teñir un modelo por código, su textura tiene
  que ser neutra: sobre la roja del pack, cualquier tinte da negro. Y al desaturar conviene
  `max(R,G,B)` y no la luminancia perceptual — la luminancia de un rojo saturado es ~0,16 y deja el
  tanque casi negro.
- **Un `TextMesh` mide ≈ `fontSize × characterSize × 0,1` metros por renglón.** `90 × 0,05` son
  45 cm por renglón, no los "9 puntitos" que sugiere el inspector.
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
| `Extintores ▸ Fix Scene Wiring` | Repara el cableado (managers, sensor, HUD, FloorLevel, prefab de la pared). |
| `Extintores ▸ Build Warehouse Blockout` | Genera `Prefabs/Warehouse_Blockout.prefab`. |
| `Extintores ▸ Build Training Scene` | Arma el ejercicio sobre la escena abierta. |
| `Extintores ▸ Build Fire Prefabs (A B C K)` | Regenera `Prefabs/Fires/Fire_X.prefab`. **Les cambia los fileID**: rompe las referencias de la escena. |
| `Extintores ▸ Fix Fire Prefabs` | Repara los prefabs de fuego en el asset (llama a la raíz + crepitar) **sin** romper la escena. |
| `Extintores ▸ Build Extinguisher Wall` | Regenera el panel de extintores. |
| `Extintores ▸ Build Extinguisher Body Material` | Textura desaturada + material tintable del tanque. |
| `Extintores ▸ Optimize FireCause Models` | Comprime mallas y reporta tri-count. |
| `Extintores ▸ Fix Sketchfab Textures` | Corrige importadores de textura de los props. |
| `Extintores ▸ Add ABC Label to Selected` | Pega la calcomanía ABC en el tanque. |
