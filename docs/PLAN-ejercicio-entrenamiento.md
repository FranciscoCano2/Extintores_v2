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

La **sexta** ronda, ya en el patio industrial, fue de diseño:

| Pedido | Qué implicó |
|---|---|
| Sacar los pisos de color debajo de los fuegos | Los `Pad` del blockout se borran del asset (`FixPads`); nadie los referencia. |
| Panel de extintores translúcido y más adelante | Adelante-derecha (1,0 m al frente, 0,75 m al costado) en vez de al costado puro. Al quedar entre el usuario y el fuego, el fondo tiene que ser **translúcido**: si no, lo tapa. La regla vieja de "+90°" pasó a `WarehouseBuilder.PanelPose`, y el chequeo viejo del fixer (que "corregía" todo lo que no estuviera a 90°) se reemplazó. |
| HUD chico en una esquina, grande al equivocarse | `BodyLockedPanel.Corner`: arriba-izquierda, siguiendo la mirada con retraso. El aviso de extintor equivocado (ahora 3 s) y el de nivel superado lo traen grande al frente, como antes. |
| En Aleatorio no tiene que estar la respuesta | Había **dos** respuestas: la letra sobre el fuego y el verde/rojo del panel. Se esconden las dos (`FireClassBadge.SuppressedByMode`, `BayHighlighter.HideAnswers`), aparte del toggle del mando para que no se pueda revelar. |
| Arrancar en el centro y elegir el elemento desde ahí | Lobby a (0, 0, 0). Los elementos tienen que verse en reposo → `FireStation.Deactivate` apaga sólo la llama. Cada puesto tiene un cartel apuntable (`FireStation.Selector`) y el láser pasó de 8 a 12 m: los elementos están a ~8,7 m. |

La **séptima** ronda, ya probando la sexta en el visor:

| Síntoma reportado | Causa real |
|---|---|
| "En Personalizado siempre se apaga el fuego, use el extintor que use" | La matriz devuelve multiplicadores, y **cualquier valor positivo apagaba**, sólo que más lento (ABC sobre K = 0,3; Espuma sobre K = 0,2). Con el caudal del chorro la diferencia no se notaba. Ahora sólo apaga lo que supera `ExtinguishMatrix.EffectiveThreshold`; lo del medio no hace nada y avisa "extintor equivocado". Ojo: ABC sobre A, B o C **sí** apaga, vale 1,0 — es el multipropósito. |
| "Si no soltás el gatillo a tiempo, al elegir PERSONALIZADO se acciona VOLVER" | `rearmSeconds` bloquea **el mismo** botón, no el que aparece después bajo el rayo. VOLVER queda a 10 cm de PERSONALIZADO y se llenaba en 0,35 s con el gatillo todavía apretado. Ahora hay que soltar (`SprayButton.AnyPressed` → `LaserPointer._waitRelease`). |
| "En el panel unos botones se ven grises y otros blancos, pero se pueden elegir todos" | No era el estado del botón: era **orden de dibujo**. El fondo translúcido nuevo y las placas/textos (`Sprites/Default`) estaban todos en la cola 3000, que URP ordena por distancia; con el panel girado, los botones del lado lejano se dibujaban antes que el fondo y quedaban velados. El fondo pasó a la cola 2990. |
| "El panel aparece en el menú de inicio" + "ponelo más lejos, más arriba y más a la derecha" | En el lobby no hay fuego: el panel se **desactiva** (antes sólo se movía a un anchor del lobby). En el puesto va a 1,4 m al frente / 1,0 m a la derecha / base a 1,05 m. |
| "El puntero tiene que estar permanente" | Se ve siempre en los menús (lobby y "Continuar"); mientras hay fuego sigue apareciendo sólo sobre un botón, para no estorbar y para poder elegir extintor en el panel. |

La **octava** ronda, sobre lo mismo:

| Síntoma reportado | Causa real |
|---|---|
| "Los botones del panel siguen mezclados en grises y negros, y en el tutorial los deshabilitados también" | El arreglo anterior (bajarle la `renderQueue` al fondo translúcido) **no sobrevive**: URP reescribe la cola a 3000 cada vez que valida el material, y el `.mat` volvía solo. Ahora el orden lo fija `Renderer.sortingOrder` en las piezas del botón, que vive en el renderer y no lo pisa nadie. |
| "El láser tiene que verse apuntando a cualquier parte del tablero" | El rayo sólo se dibujaba sobre un `SprayButton`. El `Backboard` pasó a tener un collider **trigger** en la layer `Interactable`: el láser lo ve (raycast con `QueryTriggerInteraction.Collide`) y el chorro lo atraviesa, porque la colisión de partículas ignora los triggers y tiene que seguir llegando al fuego. |
| "La boquilla tiene que cambiar de color con el extintor" | La boquilla vive en OTRO rig (mano izquierda), así que `ExtinguisherSwappable` no la alcanzaba; además usaba la textura ROJA del pack, sobre la que cualquier tinte da negro. Ahora usa el material tintable y se cablea en `extraBodyRenderers`. |
| "El panel, 20 cm más atrás y 30 cm a la derecha" | `PanelForward` 1,6 m y `PanelSide` 1,3 m (a ~2,1 m del usuario, ~39° a la derecha). |

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
- **El extintor no efectivo NO apaga, ni un poco.** La matriz sigue teniendo valores intermedios
  (CO2 sobre A = 0,3), pero por debajo de `ExtinguishMatrix.EffectiveThreshold` (0,9) el fuego no
  baja y se avisa "extintor equivocado". Un apagado lento es indistinguible de uno normal cuando el
  chorro tira 3 segundos, y el ejercicio entero dejaba de enseñar nada. El mismo umbral decide qué
  bahías habilita el tutorial y qué marco pinta verde el panel.
- **Después de accionar un botón hay que soltar el gatillo.** Al cambiar de menú aparece otro botón
  bajo el rayo y se accionaba solo. El `rearmSeconds` no alcanzaba: bloquea un botón, no el gesto.
- **El orden de dibujo de los carteles se fija con `sortingOrder`, no con `renderQueue`.** URP
  revalida los materiales y devuelve la cola a la del Surface Type; el `sortingOrder` es del
  renderer y sobrevive.
- **La boquilla es parte del extintor**: se pinta del mismo color y cambia con él.
- **El láser se ve apuntando al tablero entero**, no sólo a un botón: el tablero es el objeto con
  el que se interactúa, y el rayo apareciendo sólo sobre cada botón se sentía intermitente.
- **El panel de extintores sólo existe donde hay fuego.** En el lobby se desactiva: no hay qué
  elegir y compite con el menú.
- **El rayo se ve siempre en los menús, y sólo sobre un botón mientras hay fuego.** En un menú el
  rayo ES el cursor y esconderlo obliga a barrer a ciegas; apagando fuego es ruido visual.
- **Personalizado se elige apuntando al ELEMENTO, no a una letra.** Desde el centro del patio se
  ven los 4 (auto, tablero…); el cartel dice qué es, no de qué clase es, porque la clase es lo que
  el ejercicio enseña.
- **En Aleatorio no hay ayudas.** Ni letra de clase ni verde/rojo en el panel: son la respuesta.
- **El HUD normal va chico en un rincón; los avisos, grandes al frente.** Tiempo y nivel no hace
  falta leerlos de frente; "extintor equivocado" sí.
- **El HUD acompaña al usuario, pero no va pegado a la cabeza.** `BodyLockedPanel`: queda quieto en
  el mundo mientras mirás alrededor y se reacomoda —despacio— sólo cuando la mirada se va más de
  35°. Es el patrón de confort estándar en VR; clavarlo al ojo marea y no se puede leer.
- **Sin locomoción libre.** El sistema reposiciona al usuario en cada puesto. Menos mareo, menos
  código, y evita que se aleje y rompa el ejercicio.
- **El tutorial habilita TODOS los agentes efectivos** (efectividad ≥ 0.9), no uno solo: para clase
  A sirven Agua, Espuma y ABC, y mostrar una única opción enseñaría algo falso.
- **Una sola escena.** Menú, patio y los 4 puestos conviven; no hay carga de escenas. El lobby es
  un punto más del patio al que se vuelve al terminar.
- **Blockout por código primero, assets después.** El blockout fijó medidas y flujo sin sumar MB.
  Como los puestos son Transforms, la geometría se reemplazó sin tocar lógica: los anchors siguen
  en `Warehouse_Blockout` y el escenario lo pone `Yard_Industrial`.
- **Patio a cielo abierto, no galpón cerrado.** El pack industrial es de exterior y muy low-poly:
  sus hangares son cáscaras de 74 a 1.500 triángulos, sin interior, así que parado adentro no se
  ve nada. Los puestos pasan a un patio de 24 × 16 m con muro de hormigón y portón, y los hangares
  y tanques quedan afuera del muro, como horizonte. Mismas medidas y mismos anchors que antes.

## 3. Convención de orientación ⚠️

**La cara legible de un Quad y de un TextMesh de Unity es su −Z.** Por eso los botones, los rótulos
de las bahías y el trigger de cada bahía miran hacia −Z.

De ahí salen **dos reglas, según dónde esté el cartel**, y confundirlas costó una sesión de visor:

| Dónde está | Yaw del anchor | Por qué |
|---|---|---|
| **Al frente** (menú del lobby) | igual que el usuario | Su −Z ya le queda de cara. |
| **Adelante-derecha** (panel de extintores) | usuario **+ ángulo hacia el panel** (~37°) | El +Z tiene que apuntar DESDE el usuario HACIA el panel. Con el mismo yaw quedaría de canto; con +90° (la regla de cuando iba al costado puro) quedaría de espaldas. |
| **Sobre un elemento** (selector de Personalizado) | mirando desde el lobby hacia el elemento | El −Z queda de cara al centro del patio. |

Lo resuelve `WarehouseBuilder.PanelPose` (y `StationSelectorBuilder` para los selectores).

Si algo se ve espejado, o el trigger de una bahía no dispara, o un rótulo queda tapado: **revisá
este eje antes que ninguna otra cosa**. El `WallBuilder` original ponía los rótulos en +Z y hubo que
corregirlos a mano en el prefab; quedó arreglado en origen.

## 4. Arquitectura

```
TrainingSession            ← orquesta: modo, cola de puestos, reposicionamiento
 ├── FireStation[]         ← un puesto = FireSource + PlayerSpawn + PanelAnchor + Selector
 │                            (en reposo: elemento visible, llama apagada)
 ├── FireManager           ← cuenta SOLO los fuegos de la etapa (BeginSession)
 ├── ExtinguisherStation   ← el panel (fondo translúcido), adelante-derecha del usuario
 │    └── ExtinguisherBay[]  ← + SprayButton "Selector" por bahía
 └── SprayButton           ← "Continuar", y los botones del menú
MainMenu                   ← 3 modos; Personalizado = indicación + Volver + selectores de puesto
LaserPointer               ← en el NozzleTip: apunta y acciona TODOS los SprayButton (12 m)
TrainingHud + BodyLockedPanel ← chico en la esquina; grande con zona muerta al avisar (NO cuelga del ojo)
Warehouse_Blockout         ← SÓLO anchors (Zone_A..K, Lobby en el centro); Shell/Dressing apagados
Yard_Industrial            ← escenografía del pack: piso, muro, props por zona, horizonte
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

- [x] **Correcciones aplicadas en la escena** (`4574f9ad`, incluye `Training.unity`).
- [x] **Pack industrial importado y commiteado** (`6cb6ec59`, 278 MB): los 36 materiales de modelos
      ya pasaron por el Render Pipeline Converter (URP/Lit). Los `.fbx` van por LFS; el resto no,
      porque ya hay `.tga` commiteados sin LFS (ver `.gitattributes`).
- [x] Texturas de `Assets/ThirdParty/FireCauses` y del pack industrial topeadas a 1024 px.
      ⚠️ `Fix Sketchfab Textures` sin carpeta seleccionada cae a `FireCauses`: para otro pack,
      seleccionarlo primero en el Project.
- [x] **Etiqueta de la bahía BC** (`Label_BC.png`: letras "BC" y sólo los chips B y C). La asigna
      `Fix Scene Wiring` (`FixBayLabels`) editando el asset, sin rehacer la pared.

- [x] **Sexta ronda en código** (compila): sin Pads, panel translúcido adelante-derecha, HUD en
      la esquina, Aleatorio sin respuestas, lobby al centro y Personalizado apuntando al elemento.

### Pendiente ⬜

- [x] **Séptima ronda en código** (compila): el extintor no efectivo no apaga, soltar el gatillo
      entre selecciones, fondo del panel en cola 2990, panel oculto en el lobby y más lejos/arriba,
      láser permanente en menús.

- [x] **Octava ronda en código** (compila): botones con `sortingOrder` propio, láser sobre todo el
      tablero (fondo con collider trigger), panel a 1,6/1,3 m y boquilla teñida con el extintor.

- [x] **Menú Configuración en código** (`SettingsMenu` + `GameSettings`, persiste en PlayerPrefs):
      volumen (`AudioListener.volume`, pasos de 10 %), vibración sí/no (toda vibración pasa por
      `Haptics`, que la respeta) y la calibración del prop: **altura del mando sobre el tanque** y
      **corrimiento de la boquilla**, de a 1 cm. La orientación mando↔prop es fija (la abrazadera,
      en el `rotationOffset`); el usuario sólo desliza sobre el eje del prop (+Y del `GripPoint`
      en el tanque, +Z en la boquilla), así el apuntado rota junto con el extintor. Se ve al instante.
- [x] Menú Configuración aplicado en `Training.unity` (19/09).
- [x] **Posición del panel de extintores en Configuración** (segunda página): lateral / altura /
      distancia de a 5 cm, en el espacio del `PanelAnchor` (así vale igual en los 4 puestos). Si se
      corre de costado o en profundidad, `TrainingSession.ApplyPanelPose` lo re-gira en yaw para que
      siga de cara al spawn (§3). Con la página abierta el panel se muestra en el lobby
      (`PreviewPanel`, en `Lobby/PanelAnchor`) para ajustarlo viéndolo.
- [x] Página del panel aplicada en `Training.unity` (19/09).
- [x] **Rotación del extintor en Configuración** (X/Y/Z de a 5°, ±90°, pivotando en la abrazadera).
      Configuración pasa a tres páginas: General · Ajustar extintor · Posición del panel.
- [x] **Vibración arreglada** (no vibraba nunca): `OVRInput` no puede vibrar con el backend OpenXR
      de Unity (`XR_ERROR_ACTIONSET_NOT_ATTACHED`), y además el chorro mandaba "parar" en cada frame.
      Ahora todo va por `Haptics` → `OpenXRInput`; el chorro vibra en los dos mandos.
- [x] Tres páginas aplicadas en la escena (19/09). La vibración **siguió sin andar**: la búsqueda
      por `UnityEngine.XR.InputDevices` no encontró ningún mando. Pasó al Input System (usage
      `Haptic`, común a todos los perfiles OpenXR del Quest, incluidos los *detached*).
- [x] **Desplazamiento X/Z del tanque** (±15 cm, de a 1 cm), perpendicular a su eje; el eje largo
      sigue siendo la altura del mando.
- [ ] Aplicar en la escena: `Fix Scene Wiring` rehace el `SettingsPanel` → Ctrl+S.
- [ ] Confirmar en el visor que vibra; si no, pasar las líneas `[Haptics]` del log.
- [ ] Probar en el visor que la configuración **persiste**: cambiar algo, cerrar la app del todo,
      abrirla y ver que sigue igual (también tras reinstalar con `build-quest.ps1`). El log
      `[GameSettings] Cargado: …` muestra lo leído al arrancar.

- [ ] **Aplicar la séptima y la octava ronda en la escena**: `Validate Setup` (tiene que marcar los
      PanelAnchor, el collider del fondo del panel y la boquilla sin teñir) → `Fix Scene Wiring` →
      Ctrl+S → `Validate Setup` en cero.
- [ ] **Build & Run en el Quest** — imprescindible: por Link no llegan ni la colisión de partículas
      ni la pose de los mandos. Checklist en §6.
- [ ] Ajustar en el visor: comodidad del HUD (`BodyLockedPanel`: `distance` 1,5 m, `heightOffset`
      −0,45 m, `deadZoneDegrees` 35°), grosor/alcance del rayo (`LaserPointer`) y el dwell de los
      botones (`fillSeconds` 0,35 s) si molesta accionar sin querer al barrer.
- [ ] Ajustar en el visor la pose del panel (`WarehouseBuilder.PanelForward` 1,4 m / `PanelSide`
      1,0 m / `PanelHeight` 1,05 m) y su transparencia (`WallBuilder.BoardColor`, alfa 0,28). Ya no está al alcance del
      brazo: se elige con el láser.
- [ ] **Patio industrial aplicado en la escena** (54 renderers, 5.880 triángulos; anchors del
      blockout con los mismos fileID). Falta confirmar que `Validate Setup` no marca despejes y
      ajustar las posiciones con el visor puesto, en la tabla `Props` de `YardBuilder.cs`.
- [ ] Opcional: 3 materiales del pack siguen en shaders Built-in porque el Material Upgrade no
      convierte partículas ni skybox: `Particles/Dust/Dust_v1`, `Particles/Smoke/.../New Material`
      y `Textures/Skyboxes/Skybox_v9`. El skybox anda en URP; las partículas salen magenta si se usan.
- [ ] Opcional: ~250 de los 278 MB del pack son arte fuente que el juego no usa (carpetas
      `Source/`, `.psd` de 58 MB, lightmaps `.exr` de la escena demo `Map_v1`). Ya está pusheado:
      borrarlo no achica el `.git` sin reescribir historia.

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
- [ ] Con el agente equivocado el fuego se aviva y suena el aviso.
- [ ] Arrancás **en el centro del patio**, con el menú al frente y los 4 elementos a la vista (sin
      llamas y sin pisos de color).
- [ ] **Personalizado**: aparecen los carteles sobre los elementos; el láser los alcanza desde el
      centro y disparar sobre uno te lleva a ese puesto.
- [ ] El panel de extintores es **translúcido** y deja ver el fuego detrás; se apunta cómodo.
- [ ] Los botones del panel se ven **todos iguales** (y los grises del tutorial, todos del mismo
      gris) desde cualquier ángulo.
- [ ] El rayo se ve apuntando a **cualquier parte del tablero**, y el chorro lo atraviesa: apuntando
      al fuego a través del panel, el fuego se apaga igual.
- [ ] La **boquilla** es del mismo color que el tanque y cambia con él (azul agua, negro CO2, acero K).
- [ ] En el lobby **no hay panel** de extintores; en el puesto aparece adelante-derecha, a la
      altura de la vista, y **todos sus botones se ven igual** desde cualquier ángulo.
- [ ] Con el gatillo sostenido, elegir PERSONALIZADO **no** acciona VOLVER (hay que soltar).
- [ ] En menús el rayo **se ve siempre**; con fuego sólo sobre un botón; vuelve al aparecer CONTINUAR.
- [ ] Con un extintor no efectivo (p. ej. ABC sobre la cocina) el fuego **no baja** y aparece
      "EXTINTOR EQUIVOCADO"; con el correcto se apaga.
- [ ] Tiempo/nivel **chico arriba a la izquierda** sin marear; con el extintor equivocado aparece
      grande al frente ~3 s.
- [ ] **Aleatorio**: ni letra sobre el fuego ni verde/rojo en el panel (tampoco con el botón B).
- [ ] Framerate estable con el patio + VFX.
- [ ] El chorro se ve como una **nube compacta** (no puntos sueltos contables) y el framerate
      aguanta con el grip a fondo apuntando de cerca — es el peor caso de overdraw.
- [ ] El menú, el HUD y los rótulos de los botones **se leen contra el cielo** (el blockout tenía
      paredes grises atrás; ahora puede haber cielo brillante).
- [ ] Ningún prop del patio tapa un fuego, el panel o el menú, y no se ven bordes del piso ni
      hangares "huecos" desde ningún puesto.

## 7. Gotchas de esta fase

- **La densidad del chorro es también un parámetro de dificultad.** El daño se cobra *por partícula*
  (`FireSource.healthPerParticle`), así que pasar el caudal de 220 a 650 p/s apagaba los fuegos casi
  3× más rápido, cambiando los tiempos del ejercicio sin un solo error de por medio. `FireSource`
  normaliza ahora por `ExtinguisherSpray.ReferenceRate`; si algún día se cambia la calibración del
  apagado, es `healthPerParticle` lo que hay que tocar, no el caudal.
- **Dos materiales transparentes en la misma cola se ordenan por distancia, no por jerarquía.** El
  fondo del panel (translúcido) y las placas de los botones (`Sprites/Default`) estaban los dos en
  3000: con el panel girado, media fila de botones se dibujaba antes que el fondo y se veía velada.
  Si un cartel se ve "sucio" de un lado y limpio del otro, mirá el orden de dibujo antes que el color.
- **`material.renderQueue` no se puede fijar a mano en URP.** Al validar el material (import,
  cambio de propiedades) URP la reescribe según el Surface Type: se puso 2990 y el `.mat` volvió a
  3000 solo. Para ordenar transparentes, `Renderer.sortingOrder`.
- **Un collider en la layer de los botones también frena el chorro.** Salvo que sea trigger: la
  colisión de partículas los ignora. Por eso el fondo del panel es trigger — si no, las partículas
  morían contra el tablero en vez de llegar al fuego.
- **Un multiplicador positivo chico igual apaga.** La matriz tenía 0,2–0,4 pensados como "casi
  inútil", pero multiplicados por el caudal del chorro apagaban igual. "Casi inútil" hay que
  escribirlo como cero.
- **`rearmSeconds` bloquea un botón, no el gesto.** Con el gatillo sostenido, el botón que aparece
  después bajo el rayo se acciona solo. Al accionar algo que cambia de pantalla, exigir soltar.

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
| `Extintores ▸ Build Warehouse Blockout` | Genera `Prefabs/Warehouse_Blockout.prefab` (sólo anchors y pads). **Les cambia los fileID a los anchors**: rompe los `FireStation` de la escena. |
| `Extintores ▸ Build Industrial Yard` | Genera `Prefabs/Yard_Industrial.prefab` con el pack. Seguro de re-correr. Después, `Fix Scene Wiring`. |
| `Extintores ▸ Build Training Scene` | Arma el ejercicio sobre la escena abierta. |
| `Extintores ▸ Build Fire Prefabs (A B C K)` | Regenera `Prefabs/Fires/Fire_X.prefab`. **Les cambia los fileID**: rompe las referencias de la escena. |
| `Extintores ▸ Fix Fire Prefabs` | Repara los prefabs de fuego en el asset (llama a la raíz + crepitar) **sin** romper la escena. |
| `Extintores ▸ Build Extinguisher Wall` | Regenera el panel de extintores. |
| `Extintores ▸ Build Extinguisher Body Material` | Textura desaturada + material tintable del tanque. |
| `Extintores ▸ Optimize FireCause Models` | Comprime mallas y reporta tri-count. |
| `Extintores ▸ Fix Sketchfab Textures` | Corrige importadores de textura de los props. |
| `Extintores ▸ Add ABC Label to Selected` | Pega la calcomanía ABC en el tanque. |
| `Extintores ▸ Tune Spray Density` | Reescribe los parámetros del chorro en `NozzleRig.prefab` con las constantes del script. Sin esto, cambiar los defaults del `.cs` no mueve la densidad. |
| `Extintores ▸ Build APK (Quest)` | Compila el APK de sideload (ARM64 + IL2CPP). Ver [`BUILD-quest.md`](BUILD-quest.md). |
