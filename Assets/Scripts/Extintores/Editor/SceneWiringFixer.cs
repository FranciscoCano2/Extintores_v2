using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using AmbientMode = UnityEngine.Rendering.AmbientMode;

namespace Extintores.EditorTools
{
    /// <summary>
    /// Diagnostica y repara el cableado de la escena de entrenamiento. Existe porque el cableado a
    /// mano dejó cuatro fallas silenciosas (ninguna da error de compilación, todas rompen el juego):
    /// <list type="bullet">
    ///   <item>dos <see cref="FireManager"/> en escena y ningún <see cref="TrainingManager"/>;</item>
    ///   <item>el <c>SwapSensor</c> del tanque sin <c>isTrigger</c>, en layer Default y de 1 cm;</item>
    ///   <item><c>ExtinguisherStation.prefab</c> sin <see cref="BayHighlighter"/>;</item>
    ///   <item>ningún <see cref="TrainingHud"/> instanciado.</item>
    /// </list>
    ///
    /// Dos menús: <b>Extintores ▸ Validate Setup</b> (solo reporta) y
    /// <b>Extintores ▸ Fix Scene Wiring</b> (repara). Ambos son idempotentes: correr el fixer dos
    /// veces no duplica nada, y validar después de reparar debe dar cero problemas.
    ///
    /// Busca todo por COMPONENTE, no por ruta de jerarquía, para que siga funcionando si movés los
    /// objetos de lugar. La escena queda marcada como sucia: hay que guardarla (Ctrl+S).
    /// </summary>
    public static class SceneWiringFixer
    {
        private const string StationPrefabPath = "Assets/Prefabs/ExtinguisherStation.prefab";
        private const string WarehousePrefabPath = "Assets/Prefabs/Warehouse_Blockout.prefab";
        private const string WrongClipPath = "Assets/Extintores/Audio/SFX_WrongAgent.wav";
        private const string SensorName = "SwapSensor";
        private const string HudName = "TrainingHud";

        // Tamaño del volumen-sensor del tanque, acotado para que no sea ni un punto ni una pared.
        private const float SensorMin = 0.10f;
        private const float SensorMax = 0.40f;

        [MenuItem("Extintores/Validate Setup", priority = 200)]
        public static void Validate() => Run(false);

        [MenuItem("Extintores/Fix Scene Wiring", priority = 201)]
        public static void FixScene() => Run(true);

        // ------------------------------------------------------------------
        private static readonly List<string> Log = new List<string>();
        private static int _problems;

        private static void Ok(string m) => Log.Add($"   ok   · {m}");
        private static void Fixed(string m) { Log.Add($"  FIX   · {m}"); }
        private static void Problem(string m) { Log.Add($"  ✗     · {m}"); _problems++; }

        private const string TrainingScenePath = "Assets/Scenes/Training.unity";

        private static void Run(bool apply)
        {
            Log.Clear();
            _problems = 0;

            // Unity a veces arranca con una escena "Untitled" vacía en vez de la última abierta. Reparar
            // ESA escena llena de objetos una escena que nadie usa, y la de verdad queda igual.
            var active = SceneManager.GetActiveScene();
            if (active.path != TrainingScenePath)
            {
                string which = string.IsNullOrEmpty(active.path) ? "una escena sin guardar (Untitled)" : $"'{active.path}'";
                if (apply)
                {
                    Debug.LogError($"[SceneWiringFixer] La escena abierta es {which}, no '{TrainingScenePath}'. " +
                                   "No se modificó nada: abrí Training.unity (Project ▸ Assets/Scenes) y volvé a correrlo.");
                    return;
                }
                Problem($"La escena abierta es {which}, no '{TrainingScenePath}': este diagnóstico no es el del juego.");
            }

            int group = 0;
            if (apply)
            {
                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName("Extintores: Fix Scene Wiring");
                group = Undo.GetCurrentGroup();
            }

            CheckLayers();
            FixTrackingOrigin(apply);

            var spray = Object.FindFirstObjectByType<ExtinguisherSpray>(FindObjectsInactive.Include);
            var swappable = Object.FindFirstObjectByType<ExtinguisherSwappable>(FindObjectsInactive.Include);
            var station = Object.FindFirstObjectByType<ExtinguisherStation>(FindObjectsInactive.Include);

            if (spray == null) Problem("No hay ningún ExtinguisherSpray en la escena (¿falta el NozzleRig?).");
            else Ok($"Chorro: '{Path(spray.transform)}'.");

            if (swappable == null)
                Problem("No hay ningún ExtinguisherSwappable en la escena: sin él no hay tanque que " +
                        "intercambiar y no puedo arreglar el sensor. Agregá el componente al ExtinguisherRig.");

            var fireManager = FixFireManagers(apply);
            var training = FixTrainingManager(apply, fireManager, spray);
            FixSwapSensor(apply, swappable);
            FixHose(apply, swappable, spray); // antes del tinte: la manguera es una pieza más de la boquilla
            FixSwappableWiring(apply, swappable, spray);
            FixLaser(apply, spray);
            FixStationPrefab(apply, station);
            FixBoardTranslucency(apply);
            FixBayLabels(apply);
            FixPads(apply);
            FixLobby(apply);
            FixPanelAnchors(apply);
            FixYard(apply);
            FixHud(apply, training);
            FixStationSelectors(apply);
            FixCustomMenu(apply);
            FixSettingsMenu(apply);
            CheckFires(spray);
            CheckTrainingSetup();

            if (apply)
            {
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                Undo.CollapseUndoOperations(group);
            }

            Print(apply);
        }

        private static void Print(bool apply)
        {
            var sb = new StringBuilder();
            sb.AppendLine(apply
                ? "[SceneWiringFixer] Reparación de la escena:"
                : "[SceneWiringFixer] Diagnóstico (no se modificó nada):");
            foreach (var l in Log) sb.AppendLine(l);

            if (_problems == 0)
            {
                sb.Append(apply
                    ? "\nTodo cableado. GUARDÁ LA ESCENA (Ctrl+S) y probá con Build and Run."
                    : "\nSin problemas pendientes.");
                Debug.Log(sb.ToString());
            }
            else
            {
                sb.Append($"\n{_problems} problema(s) sin resolver" +
                          (apply ? " — requieren intervención manual." : " — corré 'Extintores ▸ Fix Scene Wiring'."));
                Debug.LogWarning(sb.ToString());
            }
        }

        // ------------------------------------------------------------------ layers
        private static void CheckLayers()
        {
            foreach (var n in new[] { "Fire", "Interactor", SprayButton.LayerName })
            {
                if (LayerMask.NameToLayer(n) >= 0) Ok($"Layer '{n}' = {LayerMask.NameToLayer(n)}.");
                else Problem($"Falta la layer '{n}' (Project Settings ▸ Tags and Layers). " +
                             "Sin ella el chorro no colisiona / el swap no dispara.");
            }
        }

        // ------------------------------------------------------------------ tracking origin
        /// <summary>
        /// El rig tiene que estar en FloorLevel o el usuario aparece con los ojos al ras del piso.
        /// Se chequea acá —y no sólo en el builder— porque la escena ya está construida y guardada:
        /// nadie debería tener que rehacerla entera para arreglar un enum.
        /// </summary>
        private static void FixTrackingOrigin(bool apply)
        {
            switch (OvrTrackingOrigin.Ensure(apply, out string detail))
            {
                case OvrTrackingOrigin.Result.AlreadyFloor: Ok(detail); break;
                case OvrTrackingOrigin.Result.Fixed: Fixed(detail); break;
                default: Problem(detail); break;
            }
        }

        // ------------------------------------------------------------------ FireManager
        /// <summary>Deja UN solo FireManager y devuelve el que sobrevive.</summary>
        private static FireManager FixFireManagers(bool apply)
        {
            var all = Object.FindObjectsByType<FireManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (all.Length == 0)
            {
                Problem("No hay ningún FireManager en la escena: nadie cuenta los fuegos ni dispara " +
                        "la condición de éxito.");
                return null;
            }

            // El "bueno" es el del GameObject llamado FireManager; si no hay, el primero.
            FireManager keep = all[0];
            foreach (var m in all)
                if (m.gameObject.name.Trim() == "FireManager") { keep = m; break; }

            if (all.Length == 1)
            {
                Ok($"Un solo FireManager, en '{Path(keep.transform)}'.");
                return keep;
            }

            foreach (var m in all)
            {
                if (m == keep) continue;
                string where = Path(m.transform);
                if (apply)
                {
                    Undo.DestroyObjectImmediate(m);
                    Fixed($"FireManager duplicado quitado de '{where}' (dos managers con " +
                          "autoCollectOnStart registran los mismos fuegos y ambos disparan el éxito).");
                }
                else
                {
                    Problem($"FireManager duplicado en '{where}'. Se conserva el de '{Path(keep.transform)}'.");
                }
            }
            return keep;
        }

        // ------------------------------------------------------------------ TrainingManager
        private static TrainingManager FixTrainingManager(bool apply, FireManager fm, ExtinguisherSpray spray)
        {
            var tm = Object.FindFirstObjectByType<TrainingManager>(FindObjectsInactive.Include);

            if (tm == null)
            {
                // Reusar el GameObject que ya se llama "TrainingManager" (ojo: tiene un espacio final).
                GameObject host = null;
                foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                    if (root.name.Trim() == "TrainingManager") { host = root; break; }

                if (!apply)
                {
                    Problem(host != null
                        ? $"'{host.name}' existe pero NO tiene el componente TrainingManager: sin " +
                          "temporizador, sin aviso de agente equivocado y sin modo examen."
                        : "No hay TrainingManager en la escena.");
                    return null;
                }

                if (host == null)
                {
                    host = new GameObject("TrainingManager");
                    Undo.RegisterCreatedObjectUndo(host, "Crear TrainingManager");
                    Fixed("GameObject 'TrainingManager' creado.");
                }
                tm = Undo.AddComponent<TrainingManager>(host);
                Fixed($"Componente TrainingManager agregado a '{host.name}'.");
            }

            var go = tm.gameObject;

            // El nombre traía un espacio al final; molesta al buscarlo por nombre.
            if (go.name != go.name.Trim())
            {
                if (apply)
                {
                    Undo.RecordObject(go, "Renombrar TrainingManager");
                    go.name = go.name.Trim();
                    Fixed("Nombre del GameObject normalizado (tenía un espacio al final).");
                }
                else Problem($"El GameObject '{go.name}' tiene un espacio al final en el nombre.");
            }

            // AudioSource para el aviso de agente equivocado.
            var audio = go.GetComponent<AudioSource>();
            if (audio == null)
            {
                if (apply) { audio = Undo.AddComponent<AudioSource>(go); Fixed("AudioSource agregado al TrainingManager."); }
                else Problem("El TrainingManager no tiene AudioSource (no sonaría el aviso).");
            }

            if (!apply) { ReportTrainingWiring(tm); return tm; }

            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(WrongClipPath);
            if (clip == null) Problem($"No se encontró el clip '{WrongClipPath}'.");

            var so = new SerializedObject(tm);
            int n = 0;
            n += SetRef(so, "fireManager", fm);
            n += SetRef(so, "spray", spray);
            n += SetRef(so, "audioSource", audio);
            n += SetRef(so, "wrongClip", clip);
            so.ApplyModifiedProperties();

            if (n > 0)
                Fixed($"TrainingManager cableado ({n} referencia(s)): fireManager, spray, audioSource, wrongClip. " +
                      "Sin esto Awake() caería en FindFirstObjectByType y elegiría un manager arbitrario.");
            else
                Ok("TrainingManager ya estaba cableado.");

            return tm;
        }

        private static void ReportTrainingWiring(TrainingManager tm)
        {
            var so = new SerializedObject(tm);
            foreach (var f in new[] { "fireManager", "spray", "audioSource", "wrongClip" })
            {
                var p = so.FindProperty(f);
                if (p != null && p.objectReferenceValue == null)
                    Problem($"TrainingManager.{f} sin asignar.");
            }
        }

        // ------------------------------------------------------------------ SwapSensor
        private static void FixSwapSensor(bool apply, ExtinguisherSwappable swappable)
        {
            if (swappable == null) return;

            var tank = swappable.gameObject;
            var sensor = FindChild(tank.transform, SensorName);

            if (sensor == null)
            {
                if (!apply) { Problem($"El tanque '{tank.name}' no tiene un hijo '{SensorName}': las bahías no pueden detectarlo."); return; }
                var go = new GameObject(SensorName);
                Undo.RegisterCreatedObjectUndo(go, "Crear SwapSensor");
                go.transform.SetParent(tank.transform, false);
                sensor = go.transform;
                Fixed($"'{SensorName}' creado bajo '{tank.name}'.");
            }

            int interactor = LayerMask.NameToLayer("Interactor");
            var box = sensor.GetComponent<BoxCollider>();
            var bounds = TankBounds(tank, sensor);

            // Qué está mal hoy: no es trigger, está en Default y mide 0.01 m.
            bool badLayer = interactor >= 0 && sensor.gameObject.layer != interactor;
            bool badTrigger = box != null && !box.isTrigger;
            bool badSize = box != null && box.size.magnitude < SensorMin;
            bool noRb = sensor.GetComponent<Rigidbody>() == null;

            if (!apply)
            {
                if (box == null) Problem($"'{SensorName}' no tiene BoxCollider.");
                if (badTrigger) Problem($"'{SensorName}' no es trigger (isTrigger = false): OnTriggerEnter de la bahía nunca se llama.");
                if (badLayer) Problem($"'{SensorName}' está en la layer '{LayerMask.LayerToName(sensor.gameObject.layer)}', debería ser 'Interactor'.");
                if (badSize) Problem($"'{SensorName}' mide {box.size.x:0.###} m: demasiado chico para entrar en una bahía.");
                if (noRb) Problem($"'{SensorName}' no tiene Rigidbody kinemático (el rig se mueve por transform: los triggers son poco fiables sin él).");
                if (box != null && !badTrigger && !badLayer && !badSize && !noRb) Ok($"'{SensorName}' configurado correctamente.");
                return;
            }

            if (box == null) { box = Undo.AddComponent<BoxCollider>(sensor.gameObject); Fixed($"BoxCollider agregado a '{SensorName}'."); }

            Undo.RecordObject(box, "Configurar SwapSensor");
            box.isTrigger = true;
            box.center = Vector3.zero;

            if (bounds.HasValue)
            {
                var b = bounds.Value;
                Undo.RecordObject(sensor, "Posicionar SwapSensor");
                sensor.position = b.center; // centrado en el cuerpo del tanque, no en su pivote
                var s = sensor.lossyScale;
                box.size = new Vector3(
                    Mathf.Clamp(b.size.x, SensorMin, SensorMax) / Mathf.Max(0.0001f, Mathf.Abs(s.x)),
                    Mathf.Clamp(b.size.y, SensorMin, SensorMax) / Mathf.Max(0.0001f, Mathf.Abs(s.y)),
                    Mathf.Clamp(b.size.z, SensorMin, SensorMax) / Mathf.Max(0.0001f, Mathf.Abs(s.z)));
                Fixed($"'{SensorName}' dimensionado desde los bounds del tanque " +
                      $"({b.size.x:0.##}×{b.size.y:0.##}×{b.size.z:0.##} m) y centrado en el cuerpo.");
            }
            else
            {
                box.size = Vector3.one * 0.18f;
                Fixed($"'{SensorName}' sin renderers para medir: tamaño fijo 0.18 m.");
            }

            if (badTrigger) Fixed($"'{SensorName}' ahora es trigger.");

            if (interactor >= 0 && sensor.gameObject.layer != interactor)
            {
                Undo.RecordObject(sensor.gameObject, "Layer SwapSensor");
                sensor.gameObject.layer = interactor;
                Fixed($"'{SensorName}' movido a la layer 'Interactor' (no 'Fire': interferiría con el chorro).");
            }

            var rb = sensor.GetComponent<Rigidbody>();
            if (rb == null) { rb = Undo.AddComponent<Rigidbody>(sensor.gameObject); Fixed($"Rigidbody kinemático agregado a '{SensorName}'."); }
            Undo.RecordObject(rb, "Configurar Rigidbody");
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        /// <summary>Bounds en mundo de los renderers del tanque, salteando el sensor y las partículas.</summary>
        private static Bounds? TankBounds(GameObject tank, Transform exclude)
        {
            Bounds b = default;
            bool any = false;
            foreach (var r in tank.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                if (exclude != null && r.transform.IsChildOf(exclude)) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            return any ? b : (Bounds?)null;
        }

        // ------------------------------------------------------------------ manguera
        /// <summary>
        /// Manguera continua tanque ↔ boquilla. Vive como hijo del NozzleRig (así <see cref="FixNozzleTint"/>
        /// la tiñe con el extintor) y une dos conectores de rigs distintos: <c>HoseOutlet</c> en el
        /// tanque y <c>HoseInlet</c> en la boquilla, que crea <c>Extintores ▸ Swap Extinguisher Model</c>.
        /// No puede tener collider ni estar en las layers del juego: frenaría el chorro o el láser
        /// apuntaría a la manguera en vez de al botón, y ninguna de las dos cosas da error.
        /// </summary>
        private static void FixHose(bool apply, ExtinguisherSwappable sw, ExtinguisherSpray spray)
        {
            if (sw == null || spray == null) return;

            var mount = spray.GetComponentInParent<ExtinguisherMount>();
            var nozzleRoot = mount != null ? mount.transform : spray.transform;

            var hoses = Object.FindObjectsByType<ExtinguisherHose>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (hoses.Length > 1)
            {
                Problem($"Hay {hoses.Length} ExtinguisherHose en la escena: debe haber una sola (se dibujarían superpuestas).");
                return;
            }

            ExtinguisherHose hose = hoses.Length == 1 ? hoses[0] : null;
            if (hose == null)
            {
                if (!apply) { Problem("No hay manguera (ExtinguisherHose) entre el tanque y la boquilla."); return; }
                var go = new GameObject("HoseTube");
                Undo.RegisterCreatedObjectUndo(go, "Crear manguera");
                go.transform.SetParent(nozzleRoot, false);
                hose = Undo.AddComponent<ExtinguisherHose>(go);
                Fixed($"Manguera 'HoseTube' creada bajo '{nozzleRoot.name}'.");
            }
            else if (!hose.transform.IsChildOf(nozzleRoot))
            {
                Problem($"La manguera '{Path(hose.transform)}' no cuelga del rig de la boquilla: no se tiñe con el extintor.");
            }

            var outlet = FindChild(sw.transform, ExtinguisherHose.OutletName);
            var inlet = FindChild(nozzleRoot, ExtinguisherHose.InletName);
            if (outlet == null)
                Problem($"El tanque no tiene el conector '{ExtinguisherHose.OutletName}': corré 'Extintores ▸ Swap Extinguisher Model'.");
            if (inlet == null)
                Problem($"La boquilla no tiene el conector '{ExtinguisherHose.InletName}': corré 'Extintores ▸ Swap Extinguisher Model'.");

            var so = new SerializedObject(hose);
            var pOut = so.FindProperty("outlet");
            var pIn = so.FindProperty("inlet");
            if (pOut == null || pIn == null) { Problem("ExtinguisherHose sin los campos 'outlet'/'inlet' (¿se renombraron?)."); return; }

            bool wired = outlet != null && inlet != null &&
                         pOut.objectReferenceValue == outlet && pIn.objectReferenceValue == inlet;
            if (outlet != null && inlet != null && !wired)
            {
                if (apply)
                {
                    SetRef(so, "outlet", outlet);
                    SetRef(so, "inlet", inlet);
                    so.ApplyModifiedProperties();
                    Fixed("Manguera conectada: salida de la válvula ↔ entrada de la boquilla.");
                }
                else Problem("La manguera no está conectada a HoseOutlet/HoseInlet.");
            }
            else if (wired) Ok("Manguera conectada a la válvula y a la boquilla.");

            var pLen = so.FindProperty("length");
            if (pLen != null && (pLen.floatValue < 0.3f || pLen.floatValue > 1.2f))
                Problem($"La manguera mide {pLen.floatValue:0.00} m: fuera de lo realista (0,3–1,2 m).");

            foreach (var c in hose.GetComponentsInChildren<Collider>(true))
            {
                if (!apply) { Problem($"La manguera tiene un collider ('{c.name}'): frenaría el chorro o el láser."); continue; }
                Undo.DestroyObjectImmediate(c);
                Fixed("Collider quitado de la manguera.");
            }

            int layer = hose.gameObject.layer;
            if (layer == LayerMask.NameToLayer("Fire") || layer == LayerMask.NameToLayer("Interactor") ||
                layer == LayerMask.NameToLayer(SprayButton.LayerName))
            {
                if (!apply) Problem($"La manguera está en la layer '{LayerMask.LayerToName(layer)}': debe ir en Default.");
                else
                {
                    Undo.RecordObject(hose.gameObject, "Layer de la manguera");
                    hose.gameObject.layer = 0;
                    Fixed("Manguera pasada a la layer Default.");
                }
            }

            var mr = hose.GetComponent<MeshRenderer>();
            if (mr != null && mr.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off)
            {
                if (!apply) Problem("La manguera proyecta sombras: costo extra en el Quest por nada.");
                else
                {
                    Undo.RecordObject(mr, "Sombras de la manguera");
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    Fixed("Sombras de la manguera apagadas.");
                }
            }
        }

        // ------------------------------------------------------------------ Swappable
        private static void FixSwappableWiring(bool apply, ExtinguisherSwappable sw, ExtinguisherSpray spray)
        {
            if (sw == null) return;

            var so = new SerializedObject(sw);
            var pSpray = so.FindProperty("spray");
            var pLabel = so.FindProperty("labelRenderer");

            // La etiqueta del tanque: quad 'Label_*' colgado del rig (lo pone el ExtinguisherLabelTool).
            Renderer label = null;
            foreach (var r in sw.GetComponentsInChildren<Renderer>(true))
                if (r.gameObject.name.StartsWith("Label")) { label = r; break; }

            if (!apply)
            {
                if (pSpray != null && pSpray.objectReferenceValue == null)
                    Problem("ExtinguisherSwappable.spray sin asignar (depende de FindFirstObjectByType en Awake).");
                if (pLabel != null && pLabel.objectReferenceValue == null && label != null)
                    Problem("ExtinguisherSwappable.labelRenderer sin asignar: la etiqueta no cambiaría al hacer swap.");

                var pBody = so.FindProperty("bodyRenderer");
                if (pBody != null && pBody.objectReferenceValue == null)
                    Problem("ExtinguisherSwappable.bodyRenderer sin asignar: el tanque no cambiaría " +
                            "de color al cambiar de tipo de extintor.");
                var pTint = so.FindProperty("tintBody");
                if (pTint != null && !pTint.boolValue)
                    Problem("ExtinguisherSwappable.tintBody está apagado: sólo cambiaría la etiqueta.");

                FixBodyMaterial(false, sw);
                FixNozzleTint(false, sw, spray);
                return;
            }

            int n = SetRef(so, "spray", spray) + SetRef(so, "labelRenderer", label);
            n += SetRef(so, "bodyRenderer", FindBodyRenderer(sw));

            var tint = so.FindProperty("tintBody");
            if (tint != null && !tint.boolValue) { tint.boolValue = true; n++; }

            so.ApplyModifiedProperties();

            if (n > 0) Fixed($"ExtinguisherSwappable cableado ({n} referencia(s)), tinte del cuerpo activo.");
            else Ok("ExtinguisherSwappable ya estaba cableado.");

            FixBodyMaterial(true, sw);
            FixNozzleTint(true, sw, spray);
        }

        /// <summary>
        /// La boquilla es parte del mismo extintor, así que se pinta del color del agente igual que
        /// el tanque. Vive en OTRO rig (la mano izquierda), por eso hay que cablearla a mano en
        /// <c>extraBodyRenderers</c>; y necesita el mismo material tintable (con la textura
        /// desaturada), porque sobre la textura roja del pack cualquier tinte da negro.
        /// </summary>
        private static void FixNozzleTint(bool apply, ExtinguisherSwappable sw, ExtinguisherSpray spray)
        {
            if (sw == null || spray == null) return;

            // El rig de la boquilla: el ExtinguisherMount del que cuelga el chorro (NozzleRig). NO se
            // usa transform.root: si mañana la boquilla cuelga del OVRCameraRig, barrería el rig
            // entero (mandos, manos y el propio tanque).
            var mount = spray.GetComponentInParent<ExtinguisherMount>();
            var nozzleRoot = mount != null ? mount.transform : spray.transform;
            var pieces = new List<Renderer>();
            foreach (var r in nozzleRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is LineRenderer) continue;
                if (r.GetComponentInParent<ExtinguisherSwappable>() != null) continue; // el tanque ya se tiñe
                pieces.Add(r);
            }

            if (pieces.Count == 0)
            {
                Ok("(aviso) La boquilla no tiene piezas visibles que teñir.");
                return;
            }

            var mat = AssetDatabase.LoadAssetAtPath<Material>(ExtinguisherBodyTool.BodyMaterial);
            var so = new SerializedObject(sw);
            var arr = so.FindProperty("extraBodyRenderers");
            bool wired = arr != null && arr.arraySize == pieces.Count;
            if (wired)
                for (int i = 0; i < pieces.Count && wired; i++)
                    wired = arr.GetArrayElementAtIndex(i).objectReferenceValue == pieces[i];

            bool material = mat != null;
            foreach (var r in pieces) if (r.sharedMaterial != mat) material = false;

            if (wired && material) { Ok($"La boquilla ({pieces.Count} pieza(s)) se tiñe con el tanque."); return; }

            if (!apply)
            {
                if (!wired)
                    Problem("La boquilla no está cableada en ExtinguisherSwappable.extraBodyRenderers: " +
                            "no cambia de color con el extintor.");
                if (mat == null)
                    Problem($"Falta '{ExtinguisherBodyTool.BodyMaterial}' para teñir la boquilla.");
                else if (!material)
                    Problem("La boquilla no usa el material tintable: con la textura roja del pack, " +
                            "cualquier tinte sale negro.");
                return;
            }

            if (mat != null)
                foreach (var r in pieces)
                {
                    if (r.sharedMaterial == mat) continue;
                    Undo.RecordObject(r, "Material de la boquilla");
                    r.sharedMaterial = mat;
                    EditorUtility.SetDirty(r);
                }

            if (arr != null)
            {
                arr.arraySize = pieces.Count;
                for (int i = 0; i < pieces.Count; i++)
                    arr.GetArrayElementAtIndex(i).objectReferenceValue = pieces[i];
                so.ApplyModifiedProperties();
            }
            Fixed($"Boquilla ({pieces.Count} pieza(s)) cableada al tinte del extintor.");
        }

        /// <summary>
        /// El cuerpo del tanque: el renderer más grande, salteando el visor de vidrio, la etiqueta y
        /// las partículas. Se elige por tamaño y no por nombre para que siga andando si mañana se
        /// cambia el modelo del extintor por otro.
        /// </summary>
        private static Renderer FindBodyRenderer(ExtinguisherSwappable sw)
        {
            Renderer best = null;
            float bestSize = 0f;

            foreach (var r in sw.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is LineRenderer) continue;

                string n = r.gameObject.name;
                if (n.StartsWith("Label") || n.Contains("Glass")) continue;

                float size = r.bounds.size.sqrMagnitude;
                if (size > bestSize) { bestSize = size; best = r; }
            }
            return best;
        }

        /// <summary>
        /// El tanque tiene que usar el material de cuerpo con la textura DESATURADA para que
        /// teñirlo dé el color del agente. Con la textura roja original, azul × rojo = negro: el
        /// extintor de agua saldría sucio en vez de azul, y nada lo explicaría.
        /// </summary>
        private static void FixBodyMaterial(bool apply, ExtinguisherSwappable sw)
        {
            var body = FindBodyRenderer(sw);
            if (body == null) { Problem("No encontré el renderer del cuerpo del tanque."); return; }

            var mat = AssetDatabase.LoadAssetAtPath<Material>(ExtinguisherBodyTool.BodyMaterial);
            if (mat == null)
            {
                Problem($"Falta '{ExtinguisherBodyTool.BodyMaterial}': el tanque no puede pintarse " +
                        "del color de su tipo. Corré 'Extintores ▸ Build Extinguisher Body Material'.");
                return;
            }

            if (body.sharedMaterial == mat) { Ok("El tanque usa el material tintable."); return; }

            if (!apply)
            {
                Problem($"El cuerpo del tanque usa '{(body.sharedMaterial != null ? body.sharedMaterial.name : "ninguno")}' " +
                        "en vez del material tintable: el color por tipo de extintor no se vería.");
                return;
            }

            Undo.RecordObject(body, "Material del tanque");
            body.sharedMaterial = mat;
            EditorUtility.SetDirty(body);
            Fixed($"Cuerpo del tanque ('{body.gameObject.name}') pasado al material tintable.");
        }

        // ------------------------------------------------------------------ láser
        /// <summary>
        /// Puntero láser en la punta de la boquilla, mismo objeto que el chorro: así apuntar a un
        /// botón y apuntar a un fuego son el mismo gesto, con el mismo origen y dirección.
        /// </summary>
        private static void FixLaser(bool apply, ExtinguisherSpray spray)
        {
            var laser = Object.FindFirstObjectByType<LaserPointer>(FindObjectsInactive.Include);
            if (laser != null)
            {
                Ok($"Puntero láser en '{Path(laser.transform)}'.");
                FixLaserReach(apply, laser);
                return;
            }

            if (spray == null)
            {
                Problem("No hay LaserPointer y tampoco ExtinguisherSpray donde ponerlo.");
                return;
            }

            if (!apply)
            {
                Problem("No hay ningún LaserPointer: los botones se accionan con el chorro, cuyo " +
                        "cono pega en varios carteles a la vez y hace imposible saber cuál se elige.");
                return;
            }

            Undo.AddComponent<LaserPointer>(spray.gameObject);
            Fixed($"LaserPointer agregado a '{Path(spray.transform)}' (la punta de la boquilla).");
        }

        /// <summary>
        /// El rayo tiene que llegar desde el centro del patio a los elementos de Personalizado
        /// (~8,7 m). El valor viejo (8 m) quedó serializado en la escena, así que cambiar el default
        /// del código no alcanza.
        /// </summary>
        private static void FixLaserReach(bool apply, LaserPointer laser)
        {
            var so = new SerializedObject(laser);
            var p = so.FindProperty("maxDistance");
            if (p == null) { Problem("LaserPointer no tiene 'maxDistance': no puedo revisar su alcance."); return; }
            if (p.floatValue >= LaserPointer.MinReach) { Ok($"El láser llega a {p.floatValue:0.#} m."); return; }

            if (!apply)
            {
                Problem($"El láser llega sólo a {p.floatValue:0.#} m: desde el centro del patio no alcanza " +
                        "los elementos de Personalizado (~8,7 m).");
                return;
            }
            p.floatValue = LaserPointer.MinReach;
            so.ApplyModifiedProperties();
            Fixed($"Alcance del láser subido a {LaserPointer.MinReach} m.");
        }

        // ------------------------------------------------------------------ anchors del panel
        /// <summary>
        /// Repara los anchors de cada zona del galpón, sin rehacerlo:
        /// <list type="bullet">
        ///   <item><b>Pose del panel</b>: adelante-derecha del <c>PlayerSpawn</c> hermano y girado
        ///         hacia él (<see cref="WarehouseBuilder.PanelPose"/>). Reemplaza la regla vieja de
        ///         90° al costado puro, que obligaba a apuntarle de canto.</item>
        ///   <item><b>Distancia de aproximación</b>: cuánto se separa el <c>PlayerSpawn</c> del
        ///         fuego. En clase B la causa es un auto de ~4,5 m y el usuario aparecía encima del
        ///         paragolpes.</item>
        /// </list>
        ///
        /// Se edita el ASSET con <c>LoadPrefabContents</c> → <c>SaveAsPrefabAsset</c>, no
        /// re-ejecutando <c>WarehouseBuilder</c>: rehacer el prefab desde una raíz nueva le cambia
        /// los fileID a los anchors y deja los <see cref="FireStation"/> de la escena apuntando a
        /// la nada.
        /// </summary>
        private static void FixPanelAnchors(bool apply)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(WarehousePrefabPath);
            if (asset == null)
            {
                Ok($"(aviso) No existe '{WarehousePrefabPath}': nada que revisar.");
                return;
            }

            int turns = 0, moves = 0;
            foreach (var zone in Zones(asset.transform))
            {
                if (NeedsPanelPose(zone, out _, out _)) turns++;
                if (NeedsMove(zone, out _, out _)) moves++;
            }

            if (turns == 0 && moves == 0)
            {
                Ok("Los anchors del galpón están bien: panel adelante-derecha y distancias correctas.");
                return;
            }

            if (!apply)
            {
                if (turns > 0)
                    Problem($"{turns} zona(s) con el PanelAnchor fuera de lugar: el panel tiene que ir " +
                            "adelante a la derecha del usuario y girado hacia él.");
                if (moves > 0)
                    Problem($"{moves} zona(s) con el PlayerSpawn a una distancia del fuego distinta " +
                            "de la que define WarehouseBuilder (clase B aparecía encima del auto).");
                return;
            }

            var contents = PrefabUtility.LoadPrefabContents(WarehousePrefabPath);
            try
            {
                int t = 0, m = 0;
                foreach (var zone in Zones(contents.transform))
                {
                    // Primero mover el spawn y después ubicar el panel: la pose del panel se calcula
                    // contra el spawn ya reubicado.
                    if (NeedsMove(zone, out float target, out Vector3 delta)) { MoveZone(zone, delta); m++; }
                    if (NeedsPanelPose(zone, out var panelPos, out var panelRot))
                    {
                        zone.Panel.SetLocalPositionAndRotation(panelPos, panelRot);
                        t++;
                    }
                }

                if (t > 0 || m > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(contents, WarehousePrefabPath);
                    if (t > 0) Fixed($"{t} PanelAnchor llevados adelante-derecha en '{WarehousePrefabPath}'.");
                    if (m > 0) Fixed($"{m} PlayerSpawn (y su panel) reubicados a la distancia correcta del fuego.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// <summary>Anchors de un puesto. <c>Fire</c> es null en el Lobby, que no tiene fuego.</summary>
        private struct ZoneAnchors
        {
            public string Name;
            public Transform Fire, Spawn, Panel;
        }

        /// <summary>
        /// Todo grupo que tenga PlayerSpawn + PanelAnchor: las 4 zonas <b>y el Lobby</b>. El lobby
        /// también tiene el panel al costado, así que también necesita el giro de 90°.
        /// </summary>
        private static System.Collections.Generic.IEnumerable<ZoneAnchors> Zones(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var spawn = t.Find("PlayerSpawn");
                var panel = t.Find("PanelAnchor");
                if (spawn == null || panel == null) continue;

                yield return new ZoneAnchors
                {
                    Name = t.name,
                    Fire = t.Find("FireAnchor"),
                    Spawn = spawn,
                    Panel = panel,
                };
            }
        }

        /// <summary>
        /// Aleja o acerca el spawn del fuego. El panel se mueve <b>el mismo vector</b>: está definido
        /// como un offset lateral del usuario, así que si sólo se moviera el spawn quedaría lejos y
        /// fuera de alcance.
        /// </summary>
        private static void MoveZone(ZoneAnchors zone, Vector3 delta)
        {
            zone.Spawn.localPosition += delta;
            zone.Panel.localPosition += delta;
        }

        private static bool NeedsMove(ZoneAnchors zone, out float target, out Vector3 delta)
        {
            delta = Vector3.zero;
            target = 0f;
            if (zone.Fire == null) return false; // el Lobby no tiene fuego del que alejarse
            if (!WarehouseBuilder.TryGetApproach(zone.Name, out target)) return false;

            Vector3 fire = zone.Fire.localPosition;
            Vector3 spawn = zone.Spawn.localPosition;
            Vector3 flat = new Vector3(spawn.x - fire.x, 0f, spawn.z - fire.z);
            float current = flat.magnitude;
            if (current < 0.01f) return false; // spawn encima del fuego: no sé hacia dónde alejarlo
            if (Mathf.Abs(current - target) < 0.05f) return false;

            Vector3 wanted = new Vector3(fire.x, spawn.y, fire.z) + flat.normalized * target;
            delta = wanted - spawn;
            return true;
        }

        /// <summary>
        /// Un PanelAnchor está mal si no coincide con <see cref="WarehouseBuilder.PanelPose"/> para su
        /// spawn hermano. Se calcula contra el spawn —y no con valores fijos— para que sirva en las 4
        /// zonas y en el lobby. Todo en espacio local del grupo. Idempotente.
        /// </summary>
        private static bool NeedsPanelPose(ZoneAnchors zone, out Vector3 position, out Quaternion rotation)
        {
            float spawnYaw = zone.Spawn.localRotation.eulerAngles.y;
            WarehouseBuilder.PanelPose(zone.Spawn.localPosition, spawnYaw, out position, out rotation);
            bool moved = (zone.Panel.localPosition - position).sqrMagnitude > 0.02f * 0.02f;
            bool turned = Quaternion.Angle(zone.Panel.localRotation, rotation) > 2f;
            return moved || turned;
        }

        // ------------------------------------------------------------------ pads y lobby
        private const string PadName = "Pad";

        /// <summary>
        /// Saca los Pad de color debajo de cada puesto: el piso es el del patio. Nada los referencia,
        /// así que se borran del asset sin riesgo para los fileID de los anchors.
        /// </summary>
        private static void FixPads(bool apply)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(WarehousePrefabPath);
            if (asset == null) return; // FixPanelAnchors ya lo avisa

            int count = 0;
            foreach (var t in asset.GetComponentsInChildren<Transform>(true))
                if (t.name == PadName) count++;

            if (count == 0) { Ok("Sin Pad de color en los puestos: queda el piso del patio."); return; }
            if (!apply)
            {
                Problem($"{count} Pad de color todavía debajo de los puestos/lobby en '{WarehousePrefabPath}'.");
                return;
            }

            var contents = PrefabUtility.LoadPrefabContents(WarehousePrefabPath);
            try
            {
                var pads = new List<GameObject>();
                foreach (var t in contents.GetComponentsInChildren<Transform>(true))
                    if (t.name == PadName) pads.Add(t.gameObject);
                foreach (var go in pads) Object.DestroyImmediate(go);
                PrefabUtility.SaveAsPrefabAsset(contents, WarehousePrefabPath);
                Fixed($"{pads.Count} Pad borrados de '{WarehousePrefabPath}'.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// <summary>
        /// Lleva el lobby al centro del patio (<see cref="WarehouseBuilder.LobbySpawn"/>): mueve
        /// PlayerSpawn, MenuAnchor y PanelAnchor del asset con el mismo vector, y después el menú de
        /// la escena, que se colocó por posición al construirla y no cuelga del anchor.
        /// </summary>
        private static void FixLobby(bool apply)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(WarehousePrefabPath);
            if (asset == null) return;

            var spawn = asset.transform.Find("Lobby/PlayerSpawn");
            if (spawn == null) { Problem("El galpón no tiene 'Lobby/PlayerSpawn'."); return; }

            Vector3 delta = WarehouseBuilder.LobbySpawn - spawn.localPosition;
            delta.y = 0f;
            if (delta.sqrMagnitude < 0.01f * 0.01f)
            {
                Ok("El lobby está en el centro del patio.");
            }
            else if (!apply)
            {
                Problem($"El lobby está en {spawn.localPosition}, no en el centro del patio: desde ahí no " +
                        "se eligen cómodos los elementos de Personalizado.");
            }
            else
            {
                var contents = PrefabUtility.LoadPrefabContents(WarehousePrefabPath);
                try
                {
                    var lobby = contents.transform.Find("Lobby");
                    foreach (Transform child in lobby) child.localPosition += delta;
                    PrefabUtility.SaveAsPrefabAsset(contents, WarehousePrefabPath);
                    Fixed($"Lobby corrido {delta} al centro del patio (spawn, menú y panel).");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
                asset = AssetDatabase.LoadAssetAtPath<GameObject>(WarehousePrefabPath);
            }

            FixMenuPlacement(apply, asset);
        }

        /// <summary>
        /// El menú de la escena tiene que estar en el MenuAnchor del lobby. Se calcula desde el ASSET
        /// (recién guardado) y la pose de la instancia del galpón, sin esperar a que la instancia se
        /// refresque.
        /// </summary>
        private static void FixMenuPlacement(bool apply, GameObject warehouseAsset)
        {
            var menu = Object.FindFirstObjectByType<MainMenu>(FindObjectsInactive.Include);
            var warehouse = WarehouseInstance();
            var anchor = warehouseAsset.transform.Find("Lobby/MenuAnchor");
            if (menu == null || warehouse == null || anchor == null) return;

            var assetRoot = warehouseAsset.transform;
            Vector3 pos = warehouse.transform.TransformPoint(assetRoot.InverseTransformPoint(anchor.position));
            Quaternion rot = warehouse.transform.rotation * (Quaternion.Inverse(assetRoot.rotation) * anchor.rotation);

            bool ok = (menu.transform.position - pos).sqrMagnitude < 0.02f * 0.02f &&
                      Quaternion.Angle(menu.transform.rotation, rot) < 2f;
            if (ok) { Ok("El menú está en el MenuAnchor del lobby."); return; }

            if (!apply) { Problem($"El menú '{Path(menu.transform)}' no está en el MenuAnchor del lobby."); return; }

            Undo.RecordObject(menu.transform, "Mover menú al lobby");
            menu.transform.SetPositionAndRotation(pos, rot);
            Fixed($"Menú movido al MenuAnchor del lobby ({pos}).");
        }

        /// <summary>La instancia del galpón en la escena (la que contiene los anchors de los puestos).</summary>
        private static GameObject WarehouseInstance()
        {
            var station = Object.FindFirstObjectByType<FireStation>(FindObjectsInactive.Include);
            return station != null && station.PanelAnchor != null
                ? PrefabUtility.GetOutermostPrefabInstanceRoot(station.PanelAnchor)
                : null;
        }

        // ------------------------------------------------------------------ Personalizado
        /// <summary>
        /// Cada puesto necesita su cartel apuntable para elegirlo desde el centro del patio
        /// (<see cref="StationSelectorBuilder"/>).
        /// </summary>
        private static void FixStationSelectors(bool apply)
        {
            var stations = Object.FindObjectsByType<FireStation>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (stations.Length == 0) return; // CheckTrainingSetup lo reporta

            var missing = new List<FireStation>();
            foreach (var st in stations) if (st.Selector == null) missing.Add(st);
            if (missing.Count == 0) { Ok($"Los {stations.Length} puestos tienen su cartel apuntable."); return; }

            if (!apply)
            {
                Problem($"{missing.Count} puesto(s) sin cartel apuntable: en Personalizado no se pueden " +
                        "elegir desde el centro del patio.");
                return;
            }

            var warehouse = WarehouseInstance();
            Vector3 lobby = warehouse != null
                ? warehouse.transform.TransformPoint(WarehouseBuilder.LobbySpawn)
                : WarehouseBuilder.LobbySpawn;
            foreach (var st in missing) StationSelectorBuilder.Ensure(st, lobby, undo: true);
            Fixed($"Cartel apuntable creado en {missing.Count} puesto(s).");
        }

        /// <summary>
        /// Botón CONFIGURACIÓN en el menú principal + submenú <see cref="SettingsMenu"/> (volumen,
        /// vibración, altura del mando sobre el tanque y corrimiento de la boquilla).
        /// </summary>
        private static void FixSettingsMenu(bool apply)
        {
            var menu = Object.FindFirstObjectByType<MainMenu>(FindObjectsInactive.Include);
            if (menu == null) return; // CheckTrainingSetup lo reporta

            if (SettingsMenuBuilder.IsComplete(menu, out string missing))
            {
                Ok("Configuración: botón en el menú + submenú cableado.");
                return;
            }

            if (!apply)
            {
                Problem($"Menú de configuración incompleto ({missing}).");
                return;
            }

            int n = SettingsMenuBuilder.Ensure(menu, undo: true);
            if (SettingsMenuBuilder.IsComplete(menu, out missing))
                Fixed($"Menú de configuración creado y cableado ({n} pieza(s)).");
            else
                Problem($"Menú de configuración sigue incompleto ({missing}): revisá '{SettingsMenuBuilder.PanelName}' a mano.");
        }

        /// <summary>
        /// Aplica <see cref="FixScene"/> sobre <c>Training.unity</c> y la guarda. Para batchmode, con el
        /// editor cerrado:
        /// <c>Unity.exe -batchmode -quit -projectPath … -executeMethod Extintores.EditorTools.SceneWiringFixer.FixTrainingSceneBatch</c>
        /// </summary>
        public static void FixTrainingSceneBatch()
        {
            var scene = EditorSceneManager.OpenScene(TrainingScenePath, OpenSceneMode.Single);
            Run(true);
            if (!EditorSceneManager.SaveScene(scene))
                Debug.LogError($"[SceneWiringFixer] No se pudo guardar {TrainingScenePath}.");
            Run(false); // el diagnóstico posterior queda en el log
        }

        /// <summary>
        /// Sólo diagnostica <c>Training.unity</c> (no modifica ni guarda nada). Para batchmode:
        /// <c>-executeMethod Extintores.EditorTools.SceneWiringFixer.ValidateTrainingSceneBatch</c>
        /// </summary>
        public static void ValidateTrainingSceneBatch()
        {
            EditorSceneManager.OpenScene(TrainingScenePath, OpenSceneMode.Single);
            Run(false);
        }

        /// <summary>
        /// El submenú de Personalizado ya no tiene botones por clase: se borran y se agrega la
        /// indicación de apuntar al elemento.
        /// </summary>
        private static void FixCustomMenu(bool apply)
        {
            var menu = Object.FindFirstObjectByType<MainMenu>(FindObjectsInactive.Include);
            if (menu == null) return; // CheckTrainingSetup lo reporta

            var panel = new SerializedObject(menu).FindProperty("customPanel")?.objectReferenceValue as GameObject;
            if (panel == null) { Problem("MainMenu.customPanel sin asignar."); return; }

            var old = new List<GameObject>();
            foreach (var t in panel.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Button_Class_")) old.Add(t.gameObject);
            bool hasHint = panel.transform.Find(TrainingSceneBuilder.CustomHintName) != null;

            if (old.Count == 0 && hasHint) { Ok("Personalizado: indicación + Volver, sin botones por clase."); return; }

            if (!apply)
            {
                if (old.Count > 0) Problem($"El submenú Personalizado todavía tiene {old.Count} botones por clase.");
                if (!hasHint) Problem("El submenú Personalizado no tiene la indicación de apuntar al elemento.");
                return;
            }

            foreach (var go in old) Undo.DestroyObjectImmediate(go);
            if (old.Count > 0) Fixed($"{old.Count} botones por clase quitados del submenú Personalizado.");
            if (!hasHint)
            {
                var hint = TrainingSceneBuilder.CreateCustomHint(panel.transform);
                Undo.RegisterCreatedObjectUndo(hint, "Indicación de Personalizado");
                Fixed("Indicación 'Apuntá al elemento…' agregada al submenú Personalizado.");
            }
        }

        // ------------------------------------------------------------------ Station
        private static void FixStationPrefab(bool apply, ExtinguisherStation sceneStation)
        {
            if (sceneStation == null)
                Problem("No hay ninguna ExtinguisherStation en la escena (arrastrá el prefab a una pared).");

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(StationPrefabPath);
            if (asset == null) { Problem($"No se encontró '{StationPrefabPath}'."); return; }

            CheckStationIsCurrent(asset);

            if (asset.GetComponent<BayHighlighter>() != null) { Ok("El prefab de la pared ya tiene BayHighlighter."); return; }

            if (!apply)
            {
                Problem("'ExtinguisherStation.prefab' no tiene BayHighlighter (se generó antes de que " +
                        "WallBuilder lo agregara): las bahías no se resaltan en verde/rojo.");
                return;
            }

            // Se edita el ASSET, no la instancia: así la pared ya colocada conserva su posición y su
            // vínculo al prefab. Re-ejecutar WallBuilder.Build() sobreescribiría el asset entero.
            var contents = PrefabUtility.LoadPrefabContents(StationPrefabPath);
            try
            {
                if (contents.GetComponent<BayHighlighter>() == null)
                {
                    contents.AddComponent<BayHighlighter>();
                    PrefabUtility.SaveAsPrefabAsset(contents, StationPrefabPath);
                    Fixed("BayHighlighter agregado a 'ExtinguisherStation.prefab' (sin tocar la instancia de la escena).");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// <summary>
        /// El fondo del panel: translúcido (uno opaco taparía el fuego que está detrás) y con un
        /// collider-trigger en la layer de los botones, para que el láser se vea apuntando a
        /// cualquier parte del tablero. Se edita el asset, no se rehace la pared.
        /// </summary>
        private static void FixBoardTranslucency(bool apply)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(StationPrefabPath);
            if (asset == null) return; // FixStationPrefab ya lo reporta

            var board = FindChild(asset.transform, "Backboard");
            var r = board != null ? board.GetComponent<MeshRenderer>() : null;
            if (r == null) { Problem("El panel de extintores no tiene 'Backboard'."); return; }

            bool translucent = WallBuilder.IsTranslucent(r.sharedMaterial);
            bool aimable = BoardIsAimable(board.gameObject);

            if (translucent && aimable)
            {
                Ok("El fondo del panel es translúcido y se puede apuntar con el láser.");
                return;
            }

            if (!apply)
            {
                if (!translucent)
                    Problem("El fondo del panel de extintores es opaco: tapa el fuego que está detrás.");
                if (!aimable)
                    Problem("El fondo del panel no tiene collider apuntable: el láser sólo se ve " +
                            "encima de un botón, no sobre el tablero.");
                return;
            }

            var mat = WallBuilder.GetBoardMat();
            var contents = PrefabUtility.LoadPrefabContents(StationPrefabPath);
            try
            {
                var cb = FindChild(contents.transform, "Backboard");
                var rend = cb.GetComponent<MeshRenderer>();
                rend.sharedMaterial = mat;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                WallBuilder.AddBoardCollider(cb.gameObject);
                PrefabUtility.SaveAsPrefabAsset(contents, StationPrefabPath);
                if (!translucent) Fixed("Fondo del panel de extintores pasado a translúcido.");
                if (!aimable) Fixed("Fondo del panel apuntable con el láser (collider trigger).");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// <summary>Trigger en la layer de los botones: lo ve el láser y lo atraviesa el chorro.</summary>
        private static bool BoardIsAimable(GameObject board)
        {
            var col = board.GetComponent<Collider>();
            int layer = LayerMask.NameToLayer(SprayButton.LayerName);
            return col != null && col.isTrigger && layer >= 0 && board.layer == layer;
        }

        /// <summary>
        /// Asigna las etiquetas que aparecieron DESPUÉS de generar la pared (así llegó
        /// <c>Label_BC.png</c>). Sin esto la bahía queda con el quad liso y el tanque sin etiqueta al
        /// elegirla, y la única salida era rehacer el panel con WallBuilder. Edita el asset como
        /// <see cref="FixStationPrefab"/>, para no cambiarle los fileID a la instancia de la escena.
        /// </summary>
        private static void FixBayLabels(bool apply)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(StationPrefabPath);
            if (asset == null) return; // FixStationPrefab ya lo reporta

            var missing = new List<string>();
            foreach (var bay in asset.GetComponentsInChildren<ExtinguisherBay>(true))
                if (LabelNeedsWiring(bay, out _, out _)) missing.Add(bay.Agent.ToString());

            if (missing.Count == 0) { Ok("Las bahías tienen su etiqueta (las que tienen textura)."); return; }

            if (!apply)
            {
                Problem($"Bahías sin etiqueta aunque la textura existe: {string.Join(", ", missing)}. " +
                        "El rótulo queda liso y el tanque no la recibe al elegirla.");
                return;
            }

            var contents = PrefabUtility.LoadPrefabContents(StationPrefabPath);
            try
            {
                foreach (var bay in contents.GetComponentsInChildren<ExtinguisherBay>(true))
                {
                    if (!LabelNeedsWiring(bay, out var id, out var tex)) continue;

                    var so = new SerializedObject(bay);
                    SetRef(so, "label", tex);
                    so.ApplyModifiedPropertiesWithoutUndo();

                    var quad = bay.transform.Find("Label");
                    var r = quad != null ? quad.GetComponent<MeshRenderer>() : null;
                    if (r != null) r.sharedMaterial = WallBuilder.GetLabelMat(id, tex);
                }
                PrefabUtility.SaveAsPrefabAsset(contents, StationPrefabPath);
                Fixed($"Etiqueta asignada en 'ExtinguisherStation.prefab': {string.Join(", ", missing)}.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static bool LabelNeedsWiring(ExtinguisherBay bay, out string id, out Texture2D tex)
        {
            tex = null;
            id = WallBuilder.LabelIdOf(bay.Agent);
            if (id == null) return false;

            tex = AssetDatabase.LoadAssetAtPath<Texture2D>(WallBuilder.LabelTexturePath(id));
            if (tex == null) return false; // sin textura no hay nada que asignar: el botón ya lleva el nombre

            if (bay.Label != tex) return true;

            var quad = bay.transform.Find("Label");
            var mat = quad != null && quad.TryGetComponent<MeshRenderer>(out var r) ? r.sharedMaterial : null;
            return mat == null || !mat.HasProperty("_BaseMap") || mat.GetTexture("_BaseMap") != tex;
        }

        // ------------------------------------------------------------------ patio
        private const string SkyboxPath =
            "Assets/RPG_FPS_game_assets_industrial/Textures/Skyboxes/Skybox_v9.mat";

        /// <summary>Lo que el blockout ya no debe mostrar: con techo y paredes el patio queda encerrado.</summary>
        private static readonly string[] BlockoutGeometry = { "Shell", "Dressing" };

        // Despejes que ningún prop del patio puede invadir (ver CheckYardClearance).
        private const float FireClearance = 1.8f;
        private const float CorridorRadius = 0.6f;
        private static readonly Vector3 PanelClearance = new Vector3(1.4f, 1.0f, 1.4f);

        /// <summary>
        /// Pasa la escena del blockout al patio industrial (<see cref="YardBuilder"/>): apaga la
        /// cáscara del blockout, instancia el patio y pone el cielo del pack.
        ///
        /// La cáscara se APAGA en el asset y no se borra ni se rehace el prefab: los FireStation de la
        /// escena apuntan a sus anchors por fileID, y rehacerlo los dejaría apuntando a la nada.
        /// </summary>
        private static void FixYard(bool apply)
        {
            var yardAsset = AssetDatabase.LoadAssetAtPath<GameObject>(YardBuilder.OutPrefab);
            if (yardAsset == null)
            {
                Ok("(aviso) Todavía no hay patio industrial: la escena sigue con el blockout. " +
                   "Corré 'Extintores ▸ Build Industrial Yard'.");
                return;
            }

            HideBlockoutGeometry(apply);
            var yard = EnsureYardInstance(apply, yardAsset);
            FixSkybox(apply);
            if (yard != null) CheckYardClearance(yard);
        }

        private static void HideBlockoutGeometry(bool apply)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(WarehousePrefabPath);
            if (asset == null) return; // FixPanelAnchors ya lo avisa

            var visible = new List<string>();
            foreach (var n in BlockoutGeometry)
            {
                var t = asset.transform.Find(n);
                if (t != null && t.gameObject.activeSelf) visible.Add(n);
            }

            if (visible.Count == 0) { Ok("El blockout ya no muestra cáscara: el escenario es el patio."); return; }

            if (!apply)
            {
                Problem($"El blockout todavía muestra {string.Join(" y ", visible)}: techo y paredes " +
                        "encierran el patio industrial.");
                return;
            }

            var contents = PrefabUtility.LoadPrefabContents(WarehousePrefabPath);
            try
            {
                foreach (var n in visible) contents.transform.Find(n).gameObject.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(contents, WarehousePrefabPath);
                Fixed($"{string.Join(" y ", visible)} apagados en '{WarehousePrefabPath}' (anchors intactos).");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static GameObject EnsureYardInstance(bool apply, GameObject yardAsset)
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject) &&
                        PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject) == yardAsset)
                    {
                        Ok($"Patio industrial en escena: '{Path(t)}'.");
                        return t.gameObject;
                    }

            if (!apply) { Problem("El patio industrial no está en la escena."); return null; }

            // Hermano del galpón y con su misma pose: el patio está medido en el espacio del blockout.
            var station = Object.FindFirstObjectByType<FireStation>(FindObjectsInactive.Include);
            var warehouse = station != null && station.PanelAnchor != null
                ? PrefabUtility.GetOutermostPrefabInstanceRoot(station.PanelAnchor)
                : null;

            var yard = (GameObject)PrefabUtility.InstantiatePrefab(yardAsset, SceneManager.GetActiveScene());
            Undo.RegisterCreatedObjectUndo(yard, "Instanciar patio industrial");
            if (warehouse != null)
            {
                yard.transform.SetParent(warehouse.transform.parent, false);
                yard.transform.SetPositionAndRotation(warehouse.transform.position, warehouse.transform.rotation);
            }
            Fixed($"Patio industrial instanciado{(warehouse != null ? $" junto a '{warehouse.name}'" : "")}.");
            return yard;
        }

        private static void FixSkybox(bool apply)
        {
            var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyboxPath);
            if (sky == null) { Problem($"No se encontró el cielo del pack '{SkyboxPath}'."); return; }

            bool ok = RenderSettings.skybox == sky && RenderSettings.ambientMode == AmbientMode.Skybox;
            if (ok) { Ok("Cielo del pack y luz ambiente desde el skybox."); return; }

            if (!apply) { Problem("El patio está a cielo abierto pero la escena usa el skybox por defecto."); return; }

            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            if (RenderSettings.sun == null)
            {
                foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional) { RenderSettings.sun = l; break; }
            }
            DynamicGI.UpdateEnvironment();
            Fixed("Skybox_v9 puesto como cielo, con la luz ambiente tomada de él.");
        }

        /// <summary>
        /// Ningún prop del patio puede tapar un puesto. Reporta, no repara: la corrección es mover el
        /// prop en la tabla de <see cref="YardBuilder"/> y regenerar.
        ///
        /// Existe porque "no veo el fuego" o "no encuentro el panel" ya costaron rondas enteras de
        /// visor, y siempre era algo fuera de lugar que ningún error delataba.
        /// </summary>
        private static void CheckYardClearance(GameObject yard)
        {
            var warehouse = WarehouseInstance();
            if (warehouse == null) { Ok("(aviso) Sin puestos en escena: no hay despejes que revisar."); return; }

            var props = new List<Renderer>();
            foreach (var r in yard.GetComponentsInChildren<Renderer>(true))
                if (r.gameObject.name != YardBuilder.GroundName && !(r is ParticleSystemRenderer)) props.Add(r);

            int before = _problems;
            var zones = new List<ZoneAnchors>(Zones(warehouse.transform));
            foreach (var z in zones)
            {
                foreach (var r in props)
                {
                    var b = r.bounds;
                    string who = PropName(r, yard);

                    if (z.Fire != null && DistanceXZ(b, z.Fire.position) < FireClearance)
                        Problem($"'{who}' está a menos de {FireClearance} m del fuego de {z.Name}.");
                    else if (z.Fire != null && b.min.y < 2f && SegmentHits(b, z.Spawn.position, z.Fire.position, CorridorRadius))
                        Problem($"'{who}' se cruza entre el usuario y el fuego de {z.Name}.");

                    if (b.Intersects(new Bounds(z.Panel.position, PanelClearance)))
                        Problem($"'{who}' tapa el panel de extintores de {z.Name}.");
                }

                // Lobby: la línea de la mirada al menú.
                var menu = z.Spawn.parent.Find("MenuAnchor");
                if (menu == null) continue;
                var eye = z.Spawn.position + Vector3.up * 1.5f;
                foreach (var r in props)
                    if (SegmentHits(r.bounds, eye, menu.position, 0.2f, flat: false))
                        Problem($"'{PropName(r, yard)}' tapa el menú del {z.Name}.");

                // Y la de cada elemento: en Personalizado se eligen apuntándoles desde acá.
                foreach (var other in zones)
                {
                    if (other.Fire == null) continue;
                    var target = other.Fire.position + Vector3.up * 1f;
                    foreach (var r in props)
                        if (SegmentHits(r.bounds, eye, target, 0.2f, flat: false))
                            Problem($"'{PropName(r, yard)}' tapa, desde el {z.Name}, el elemento de {other.Name}.");
                }
            }

            if (_problems == before)
                Ok($"Ningún prop del patio ({props.Count} renderers) tapa fuegos, corredores, paneles ni el menú.");
        }

        private static string PropName(Renderer r, GameObject yard)
        {
            var root = PrefabUtility.GetNearestPrefabInstanceRoot(r.gameObject);
            return root != null && root != yard ? root.name : r.name;
        }

        private static float DistanceXZ(Bounds b, Vector3 p)
        {
            var q = b.ClosestPoint(new Vector3(p.x, b.center.y, p.z));
            return new Vector2(q.x - p.x, q.z - p.z).magnitude;
        }

        /// <summary>
        /// ¿El segmento pasa a menos de <paramref name="radius"/> del bounds? Muestreado cada 10 cm, que
        /// sobra para props de medio metro para arriba. <paramref name="flat"/> ignora la altura (corredor
        /// a nivel del piso); sin él se mide en 3D (la línea de la mirada).
        /// </summary>
        private static bool SegmentHits(Bounds b, Vector3 from, Vector3 to, float radius, bool flat = true)
        {
            int steps = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(from, to) / 0.1f));
            for (int i = 0; i <= steps; i++)
            {
                var p = Vector3.Lerp(from, to, i / (float)steps);
                float d = flat ? DistanceXZ(b, p) : Vector3.Distance(b.ClosestPoint(p), p);
                if (d < radius) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ HUD
        private static void FixHud(bool apply, TrainingManager tm)
        {
            var hud = Object.FindFirstObjectByType<TrainingHud>(FindObjectsInactive.Include);

            if (hud == null)
            {
                if (!apply) { Problem("No hay ningún TrainingHud en la escena: no se ve fuegos restantes ni tiempo."); return; }

                var go = new GameObject(HudName);
                Undo.RegisterCreatedObjectUndo(go, "Crear TrainingHud");
                go.transform.position = new Vector3(0f, 1.3f, 1.5f);
                hud = Undo.AddComponent<TrainingHud>(go);
                Fixed($"'{HudName}' creado.");
            }

            FixHudPlacement(apply, hud);

            if (!apply)
            {
                var check = new SerializedObject(hud).FindProperty("training");
                if (check != null && check.objectReferenceValue == null)
                    Problem("TrainingHud.training sin asignar.");
                else Ok("TrainingHud presente y cableado.");
                return;
            }

            var so = new SerializedObject(hud);
            if (SetRef(so, "training", tm) > 0)
            {
                so.ApplyModifiedProperties();
                Fixed("TrainingHud cableado al TrainingManager.");
            }
            else Ok("TrainingHud ya estaba cableado.");
        }

        /// <summary>
        /// Saca el HUD de la cabeza y le pone <see cref="BodyLockedPanel"/>.
        ///
        /// Colgado del <c>CenterEyeAnchor</c> el cartel queda clavado a la cara: gira exacto con la
        /// cabeza, siempre en el mismo rincón del ojo, y no hay forma de mirarlo de frente. Suelto en
        /// el mundo + seguimiento con zona muerta, te espera quieto donde lo dejaste y sólo se
        /// reacomoda cuando de verdad te diste vuelta.
        /// </summary>
        private static void FixHudPlacement(bool apply, TrainingHud hud)
        {
            var eye = FindDeep("CenterEyeAnchor");
            var t = hud.transform;
            bool headParented = eye != null && t.IsChildOf(eye);
            var follower = hud.GetComponent<BodyLockedPanel>();

            if (!apply)
            {
                if (headParented)
                    Problem($"'{hud.name}' cuelga del CenterEyeAnchor: el cartel queda pegado a la " +
                            "cara, gira con la cabeza y no se puede leer.");
                if (follower == null)
                    Problem($"'{hud.name}' no tiene BodyLockedPanel: o queda fijo en el galpón, o " +
                            "clavado a la cabeza. Debería seguirte con zona muerta.");
                if (!headParented && follower != null) Ok("TrainingHud suelto y con seguimiento body-locked.");
                return;
            }

            if (headParented)
            {
                Undo.SetTransformParent(t, null, "Sacar el HUD de la cabeza");
                Fixed($"'{hud.name}' desacoplado del CenterEyeAnchor.");
            }

            if (follower == null)
            {
                follower = Undo.AddComponent<BodyLockedPanel>(hud.gameObject);
                Fixed($"BodyLockedPanel agregado a '{hud.name}' (te sigue con zona muerta, no pegado a la cara).");
            }

            var so = new SerializedObject(follower);
            if (SetRef(so, "head", eye) > 0)
            {
                so.ApplyModifiedProperties();
                Fixed("BodyLockedPanel.head cableado al CenterEyeAnchor.");
            }
        }

        /// <summary>
        /// Detecta un <c>ExtinguisherStation.prefab</c> viejo. No lo repara: rehacerlo es correr
        /// <b>Extintores ▸ Build Extinguisher Wall</b>, que sobre-escribe el asset entero, y eso es
        /// decisión del usuario, no un efecto colateral de pedir un diagnóstico.
        ///
        /// Existe porque achicar el panel en el código de <c>WallBuilder</c> NO achica el asset ya
        /// generado: el prefab siguió midiendo 2,45 × 1,3 m durante toda una prueba en visor mientras
        /// el plan lo daba por hecho. Nada lo delataba salvo abrir el YAML.
        /// </summary>
        private static void CheckStationIsCurrent(GameObject asset)
        {
            var board = FindChild(asset.transform, "Backboard");
            if (board != null && board.localScale.x > 1.5f)
                Problem($"El panel de extintores mide {board.localScale.x:0.##} × {board.localScale.y:0.##} m: " +
                        "es el asset viejo, el código de WallBuilder ya genera uno de ~1,1 × 0,62 m. " +
                        "Corré 'Extintores ▸ Build Extinguisher Wall' para regenerarlo.");

            var bays = asset.GetComponentsInChildren<ExtinguisherBay>(true);
            int withSelector = 0;
            foreach (var b in bays)
                if (b.GetComponentInChildren<SprayButton>(true) != null) withSelector++;

            if (bays.Length > 0 && withSelector < bays.Length)
                Problem($"{bays.Length - withSelector} de {bays.Length} bahías no tienen botón " +
                        "'Selector': sólo se puede cambiar de extintor metiendo el tanque en la bahía, " +
                        "no apuntándole el chorro. Corré 'Extintores ▸ Build Extinguisher Wall'.");
            else if (bays.Length > 0)
                Ok($"Las {bays.Length} bahías tienen botón de chorro.");
        }

        // ------------------------------------------------------------------ fuegos
        private static void CheckFires(ExtinguisherSpray spray)
        {
            var fires = Object.FindObjectsByType<FireSource>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (fires.Length == 0) { Problem("No hay ningún FireSource en la escena."); return; }

            var byClass = new Dictionary<FireClass, int>();
            foreach (var f in fires)
            {
                byClass.TryGetValue(f.Class, out int c);
                byClass[f.Class] = c + 1;
            }
            var parts = new List<string>();
            foreach (var kv in byClass) parts.Add($"{kv.Key}×{kv.Value}");
            Ok($"{fires.Length} fuego(s): {string.Join(", ", parts)}.");

            // Los dos defectos que repara 'Fix Fire Prefabs'. No se arreglan acá: viven en el
            // prefab, no en la escena.
            int mute = 0, nested = 0;
            foreach (var f in fires)
            {
                if (new SerializedObject(f).FindProperty("loopClip")?.objectReferenceValue == null) mute++;

                // La llama tiene que colgar de la raíz del prefab del fuego ('Fire_A'…), no de la
                // causa. Se compara por nombre y no por profundidad: el prefab está anidado dentro
                // de Station_X > TrainingSetup, así que contar padres no distingue nada.
                var parent = f.transform.parent;
                if (parent != null && parent.name != $"Fire_{f.Class}") nested++;
            }
            if (mute > 0)
                Problem($"{mute} fuego(s) sin crepitar asignado. Corré 'Extintores ▸ Fix Fire Prefabs'.");
            if (nested > 0)
                Problem($"{nested} fuego(s) con la llama colgando de la causa en vez de la raíz " +
                        "(hereda su escala: es el bug de 'la B gigante'). " +
                        "Corré 'Extintores ▸ Fix Fire Prefabs'.");

            foreach (FireClass fc in System.Enum.GetValues(typeof(FireClass)))
                if (!byClass.ContainsKey(fc))
                    Ok($"(aviso) No hay ningún fuego de clase {fc} en la escena.");

            int fire = LayerMask.NameToLayer("Fire");
            if (spray != null && fire >= 0)
            {
                var mask = new SerializedObject(spray).FindProperty("collidesWith");
                if (mask != null && mask.intValue != 0 && (mask.intValue & (1 << fire)) == 0)
                    Problem("ExtinguisherSpray.collidesWith no incluye la layer 'Fire': el chorro no apaga nada.");
                else Ok("El chorro colisiona contra la layer 'Fire'.");
            }
        }

        // ------------------------------------------------------------------ ejercicio
        /// <summary>
        /// Chequeos del ejercicio (menú, puestos, reposicionamiento). Solo reporta: lo arma
        /// <c>Extintores ▸ Build Training Scene</c>, no este fixer.
        /// </summary>
        private static void CheckTrainingSetup()
        {
            var session = Object.FindFirstObjectByType<TrainingSession>(FindObjectsInactive.Include);
            if (session == null)
            {
                Ok("(aviso) No hay TrainingSession: la escena todavía es la vieja, sin modos ni menú. " +
                   "Corré 'Extintores ▸ Build Training Scene'.");
                return;
            }

            var stations = Object.FindObjectsByType<FireStation>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (stations.Length == 0) Problem("Hay TrainingSession pero ningún FireStation.");
            else Ok($"{stations.Length} puesto(s) de entrenamiento.");

            foreach (var s in stations)
            {
                if (s.Fire == null) Problem($"El puesto '{s.name}' no tiene FireSource.");
                if (s.PanelAnchor == null) Problem($"El puesto '{s.name}' no tiene PanelAnchor: el panel no se movería.");
            }

            CheckSpawnHeights(session, stations);

            var buttons = Object.FindObjectsByType<SprayButton>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (buttons.Length == 0) Problem("No hay ningún SprayButton: no se puede navegar el menú.");
            else Ok($"{buttons.Length} botón(es) accionables con el chorro.");

            int buttonLayer = LayerMask.NameToLayer(SprayButton.LayerName);
            foreach (var b in buttons)
                if (buttonLayer >= 0 && b.gameObject.layer != buttonLayer)
                    Problem($"El botón '{b.name}' está en la layer " +
                            $"'{LayerMask.LayerToName(b.gameObject.layer)}': el chorro no lo va a tocar.");

            if (Object.FindFirstObjectByType<MainMenu>(FindObjectsInactive.Include) == null)
                Problem("No hay MainMenu: no habría forma de elegir el modo.");

            var fm = Object.FindFirstObjectByType<FireManager>(FindObjectsInactive.Include);
            if (fm != null)
            {
                var auto = new SerializedObject(fm).FindProperty("autoCollectOnStart");
                if (auto != null && auto.boolValue)
                    Problem("FireManager.autoCollectOnStart está encendido: registraría los 4 fuegos " +
                            "y pisaría la etapa que arma TrainingSession. Debe estar apagado.");
            }
        }

        /// <summary>
        /// <see cref="TrainingSession"/> apoya el rig en la Y de cada spawn (con origen de piso, el
        /// origen del rig ES el piso). Un spawn más bajo que el <c>Ground</c> del patio deja al usuario
        /// hundido; uno más alto, flotando. Sólo diagnostica: los spawns los arma el builder del patio.
        /// </summary>
        private static void CheckSpawnHeights(TrainingSession session, FireStation[] stations)
        {
            var ground = FindDeep(YardBuilder.GroundName);
            if (ground == null)
            {
                Ok($"(aviso) No encontré '{YardBuilder.GroundName}': no puedo comparar la altura de los spawns.");
                return;
            }

            const float tolerance = 0.05f; // m
            float floorY = ground.position.y;
            var spawns = new List<Transform>();
            var lobby = new SerializedObject(session).FindProperty("lobbySpawn");
            if (lobby != null && lobby.objectReferenceValue is Transform l) spawns.Add(l);
            foreach (var s in stations) if (s != null) spawns.Add(s.PlayerSpawn);

            int bad = 0;
            foreach (var t in spawns)
            {
                float dy = t.position.y - floorY;
                if (Mathf.Abs(dy) <= tolerance) continue;
                bad++;
                Problem($"'{Path(t)}' está {dy * 100f:+0;-0} cm respecto del piso del patio (y={floorY:0.00}): " +
                        (dy < 0 ? "el usuario aparecería hundido." : "el usuario aparecería flotando."));
            }
            if (bad == 0) Ok($"{spawns.Count} spawn(s) a la altura del piso (y={floorY:0.00}).");
        }

        // ------------------------------------------------------------------ helpers
        /// <summary>Asigna una referencia si difiere. Devuelve 1 si cambió, 0 si ya estaba.</summary>
        private static int SetRef(SerializedObject so, string field, Object value)
        {
            var p = so.FindProperty(field);
            if (p == null || value == null || p.objectReferenceValue == value) return 0;
            p.objectReferenceValue = value;
            return 1;
        }

        private static Transform FindChild(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        private static Transform FindDeep(string name)
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                var t = FindChild(root.transform, name);
                if (t != null) return t;
            }
            return null;
        }

        private static string Path(Transform t)
        {
            var sb = new StringBuilder(t.name);
            for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
            return sb.ToString();
        }
    }
}
