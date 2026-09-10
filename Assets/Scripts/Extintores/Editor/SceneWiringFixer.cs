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
            FixSwappableWiring(apply, swappable, spray);
            FixLaser(apply, spray);
            FixStationPrefab(apply, station);
            FixPanelAnchors(apply);
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
            if (laser != null) { Ok($"Puntero láser en '{Path(laser.transform)}'."); return; }

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

        // ------------------------------------------------------------------ anchors del panel
        /// <summary>
        /// Repara los anchors de cada zona del galpón, sin rehacerlo:
        /// <list type="bullet">
        ///   <item><b>Giro del panel</b>: 90° respecto del <c>PlayerSpawn</c> hermano. El panel va al
        ///         costado y su cara legible es el −Z; orientado igual que el usuario, ese −Z apunta
        ///         a su espalda y hay que darse vuelta entero para leerlo.</item>
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
                if (NeedsTurn(zone, out _)) turns++;
                if (NeedsMove(zone, out _, out _)) moves++;
            }

            if (turns == 0 && moves == 0)
            {
                Ok("Los anchors del galpón están bien: panel de cara al usuario y distancias correctas.");
                return;
            }

            if (!apply)
            {
                if (turns > 0)
                    Problem($"{turns} zona(s) con el PanelAnchor orientado igual que el usuario: el " +
                            "panel queda de costado mirando hacia atrás y hay que darse vuelta para usarlo.");
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
                    // Primero mover y después girar: el giro se calcula contra el spawn, que la
                    // mudanza no rota, así que el orden no cambia el resultado — pero sí importa
                    // que el panel se mueva junto con el spawn (ver MoveZone).
                    if (NeedsMove(zone, out float target, out Vector3 delta)) { MoveZone(zone, delta); m++; }
                    if (NeedsTurn(zone, out float spawnYaw))
                    {
                        zone.Panel.localRotation = Quaternion.Euler(0f, spawnYaw + 90f, 0f);
                        t++;
                    }
                }

                if (t > 0 || m > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(contents, WarehousePrefabPath);
                    if (t > 0) Fixed($"{t} PanelAnchor girados 90° en '{WarehousePrefabPath}'.");
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
        /// Un PanelAnchor está mal si mira casi igual que el PlayerSpawn. Se compara contra el spawn
        /// —y no contra un yaw fijo— para que sirva en las 4 zonas, que se enfrentan en direcciones
        /// distintas. Idempotente: a 90° ya no toca nada.
        /// </summary>
        private static bool NeedsTurn(ZoneAnchors zone, out float spawnYaw)
        {
            spawnYaw = zone.Spawn.localRotation.eulerAngles.y;
            float delta = Mathf.DeltaAngle(zone.Panel.localRotation.eulerAngles.y, spawnYaw);
            return Mathf.Abs(delta) < 45f;
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
