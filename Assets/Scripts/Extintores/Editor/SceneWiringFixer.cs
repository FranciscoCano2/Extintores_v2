using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

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

        private static void Run(bool apply)
        {
            Log.Clear();
            _problems = 0;

            int group = 0;
            if (apply)
            {
                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName("Extintores: Fix Scene Wiring");
                group = Undo.GetCurrentGroup();
            }

            CheckLayers();

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
            FixSwappableWiring(apply, swappable, spray);
            FixStationPrefab(apply, station);
            FixHud(apply, training);
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
                return;
            }

            int n = SetRef(so, "spray", spray) + SetRef(so, "labelRenderer", label);
            so.ApplyModifiedProperties();

            if (n > 0) Fixed($"ExtinguisherSwappable cableado ({n} referencia(s)).");
            else Ok("ExtinguisherSwappable ya estaba cableado.");
        }

        // ------------------------------------------------------------------ Station
        private static void FixStationPrefab(bool apply, ExtinguisherStation sceneStation)
        {
            if (sceneStation == null)
                Problem("No hay ninguna ExtinguisherStation en la escena (arrastrá el prefab a una pared).");

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(StationPrefabPath);
            if (asset == null) { Problem($"No se encontró '{StationPrefabPath}'."); return; }

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

        // ------------------------------------------------------------------ HUD
        private static void FixHud(bool apply, TrainingManager tm)
        {
            var hud = Object.FindFirstObjectByType<TrainingHud>(FindObjectsInactive.Include);

            if (hud == null)
            {
                if (!apply) { Problem("No hay ningún TrainingHud en la escena: no se ve fuegos restantes ni tiempo."); return; }

                var go = new GameObject(HudName);
                Undo.RegisterCreatedObjectUndo(go, "Crear TrainingHud");

                // Pegado al ojo para que el cartel acompañe la mirada; si no hay rig queda suelto.
                var anchor = FindDeep("CenterEyeAnchor");
                if (anchor != null)
                {
                    go.transform.SetParent(anchor, false);
                    go.transform.localPosition = new Vector3(-0.35f, -0.20f, 1.2f);
                    go.transform.localRotation = Quaternion.identity;
                    Fixed($"'{HudName}' creado bajo 'CenterEyeAnchor' (1.2 m al frente).");
                }
                else
                {
                    go.transform.position = new Vector3(0f, 1.6f, 2f);
                    Fixed($"'{HudName}' creado suelto en la escena (no encontré CenterEyeAnchor): posicionalo a mano.");
                }
                hud = Undo.AddComponent<TrainingHud>(go);
            }

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
