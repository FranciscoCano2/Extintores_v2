using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Extintores.EditorTools
{
    /// <summary>
    /// Reemplaza el tanque del pack por el modelo propio del extintor real de 1 kg
    /// (<c>tools/blender/extintor_1kg.py</c>) y la boquilla de cilindros por la modelada, y deja los
    /// conectores de la manguera (<c>HoseOutlet</c> / <c>HoseInlet</c>) y la propia manguera.
    ///
    /// <b>La calibración no se toca.</b> El <c>GripPoint</c>, los offsets del <see cref="ExtinguisherMount"/>
    /// y los defaults de <see cref="GameSettings"/> se midieron con el prop real usando el modelo
    /// viejo, y lo que coincidía era la VÁLVULA/agarre. Por eso el modelo nuevo se coloca DEBAJO del
    /// GripPoint de modo que su eje sea el del tanque viejo, el centro de su cabeza (válvula + palanca)
    /// caiga donde estaba el del viejo y la salida de la válvula apunte para el mismo lado. El fondo
    /// queda más arriba y el diámetro baja a la mitad: el viejo medía 48 × 15 cm y el real 34,8 × 7,65.
    ///
    /// Menús: <b>Extintores ▸ Preview Extinguisher Swap</b> (renders viejo vs nuevo, no modifica nada)
    /// y <b>Extintores ▸ Swap Extinguisher Model (1 kg real)</b>. Para batchmode: <see cref="PreviewBatch"/>
    /// (SIN <c>-nographics</c>: renderiza) y <see cref="SwapBatch"/>.
    /// </summary>
    public static class ExtinguisherModelTool
    {
        public const string Folder = "Assets/Extintores/Extinguisher/Real";
        public const string ModelPath = Folder + "/Extintor_1kg.fbx";
        public const string NozzlePath = Folder + "/Boquilla.fbx";
        public const string AtlasPath = Folder + "/T_Extinguisher_Details.png";
        public const string PaintMaterial = Folder + "/M_Extinguisher_Paint.mat";
        public const string DetailsMaterial = Folder + "/M_Extinguisher_Details.mat";
        private const string LabelMaterial = "Assets/Extintores/Labels/M_Label_ABC.mat";

        private const string RigPrefab = "Assets/Prefabs/ExtinguisherRig.prefab";
        private const string NozzlePrefab = "Assets/Prefabs/NozzleRig.prefab";
        private const string OldModel = "Assets/Fire_Extinguisher/fire extinguisher.fbx";
        private const string TrainingScene = "Assets/Scenes/Training.unity";
        private const string PreviewDir = "Logs/ExtinguisherPreview";

        // --- Referencias del modelo VIEJO, medidas sobre sus vértices en el espacio de la raíz del
        //     ExtinguisherRig (01/10/2026, MeasureOld). El modelo viejo desaparece con el swap, así que
        //     no tiene sentido un detector genérico: se dejan los números y se re-miden en el preview.
        /// <summary>Eje del tanque viejo: centro de los anillos completos del cilindro (x, z).</summary>
        private static readonly Vector2 OldAxisXZ = new Vector2(-0.0462f, -0.0126f);
        /// <summary>Unión domo ↔ válvula del viejo (y) y punta de la palanca (y).</summary>
        private const float OldNeckY = 0.386f, OldTopY = 0.466f;
        /// <summary>Hacia dónde salía la manguera del viejo (la palanca va del lado opuesto, +X).</summary>
        private static readonly Vector3 OldOutletDir = Vector3.left;

        // --- Modelo nuevo (mantener igual que P en extintor_1kg.py).
        private const float NewNeckY = 0.290f, NewTopY = 0.348f;

        /// <summary>Centro de la cabeza: lo que se alinea entre viejo y nuevo (la válvula/agarre).</summary>
        private static float OldHeadY => 0.5f * (OldNeckY + OldTopY);
        private static float NewHeadY => 0.5f * (NewNeckY + NewTopY);

        // ------------------------------------------------------------------ menús
        [MenuItem("Extintores/Preview Extinguisher Swap", priority = 36)]
        public static void Preview()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            PreviewBatch();
        }

        [MenuItem("Extintores/Swap Extinguisher Model (1 kg real)", priority = 37)]
        public static void Swap()
        {
            if (!EditorUtility.DisplayDialog("Swap Extinguisher Model",
                    "Reemplaza el tanque y la boquilla por el modelo de 1 kg en los prefabs y en Training.unity " +
                    "(la guarda). La calibración no se toca. ¿Seguir?", "Sí", "Cancelar")) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            SwapBatch();
        }

        // ------------------------------------------------------------------ importación y materiales
        private static bool PrepareAssets()
        {
            foreach (var path in new[] { ModelPath, NozzlePath })
            {
                if (!(AssetImporter.GetAtPath(path) is ModelImporter mi))
                {
                    Debug.LogError($"[ExtinguisherModelTool] Falta '{path}'. Generalo con tools/blender/build-extintor.ps1.");
                    return false;
                }
                bool dirty = mi.globalScale != 1f || !mi.useFileScale || !mi.bakeAxisConversion ||
                             mi.materialImportMode != ModelImporterMaterialImportMode.None ||
                             mi.importAnimation || mi.isReadable || mi.importCameras || mi.importLights;
                if (!dirty) continue;
                mi.globalScale = 1f;
                mi.useFileScale = true;
                mi.bakeAxisConversion = true;
                mi.materialImportMode = ModelImporterMaterialImportMode.None;
                mi.importAnimation = false;
                mi.animationType = ModelImporterAnimationType.None;
                mi.importCameras = false;
                mi.importLights = false;
                mi.isReadable = false;
                mi.importNormals = ModelImporterNormals.Import;
                mi.SaveAndReimport();
            }

            if (AssetImporter.GetAtPath(AtlasPath) is TextureImporter ti && ti.maxTextureSize != 512)
            {
                ti.maxTextureSize = 512;
                ti.SaveAndReimport();
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var paint = LoadOrCreate(PaintMaterial, shader);
            // Arranca en el rojo del ABC (el agente por defecto); en runtime lo pisa ExtinguisherSwappable.
            paint.SetColor("_BaseColor", AgentColor.Of(AgentType.DryChemABC));
            paint.SetFloat("_Metallic", AgentColor.MetallicOf(AgentType.DryChemABC));
            paint.SetFloat("_Smoothness", AgentColor.SmoothnessOf(AgentType.DryChemABC));
            EditorUtility.SetDirty(paint);

            var details = LoadOrCreate(DetailsMaterial, shader);
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
            details.SetTexture("_BaseMap", atlas);
            details.SetColor("_BaseColor", Color.white);
            details.SetFloat("_Metallic", 0.55f);
            details.SetFloat("_Smoothness", 0.6f);
            EditorUtility.SetDirty(details);

            AssetDatabase.SaveAssets();
            return true;
        }

        private static Material LoadOrCreate(string path, Shader shader)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // ------------------------------------------------------------------ colocación
        /// <summary>
        /// Pose del modelo nuevo en el espacio de la raíz del ExtinguisherRig: eje vertical del rig,
        /// la salida hacia <see cref="OldOutletDir"/>, el centro de la cabeza a la altura del viejo.
        /// </summary>
        private static void Placement(GameObject modelAsset, out Vector3 pos, out Quaternion rot)
        {
            var outlet = FindChild(modelAsset.transform, ExtinguisherHose.OutletName);
            Vector3 o = outlet != null ? outlet.localPosition : Vector3.left;
            Vector3 outletDir = new Vector3(o.x, 0f, o.z).normalized;
            rot = Quaternion.FromToRotation(outletDir, OldOutletDir);
            float baseY = OldHeadY - NewHeadY;
            pos = new Vector3(OldAxisXZ.x, baseY, OldAxisXZ.y);
        }

        private static bool CheckModelAxes(GameObject modelAsset)
        {
            var b = LocalBounds(modelAsset.transform, modelAsset.transform);
            bool upright = b.size.y > b.size.x && b.size.y > b.size.z && Mathf.Abs(b.min.y) < 0.002f;
            Debug.Log($"[ExtinguisherModelTool] Modelo nuevo importado: bounds {b.size.x:0.000}×{b.size.y:0.000}×{b.size.z:0.000} m, " +
                      $"fondo en y={b.min.y:0.0000}.");
            if (!upright)
                Debug.LogError("[ExtinguisherModelTool] El FBX no quedó parado sobre +Y con el pivote en el fondo: " +
                               "revisar los ejes de exportación de extintor_1kg.py.");
            return upright;
        }

        // ------------------------------------------------------------------ preview
        /// <summary>
        /// Escena temporal con el rig actual (modelo viejo), el modelo nuevo donde lo dejaría el swap y
        /// una caja en la pose del mando con la calibración de fábrica. Renderiza viejo y nuevo por
        /// separado y los compone: viejo en celeste translúcido, nuevo encima. No modifica nada.
        /// </summary>
        public static void PreviewBatch()
        {
            if (!PrepareAssets()) return;
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (!CheckModelAxes(modelAsset)) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var rigAsset = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefab);
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(rigAsset, scene);
            foreach (var mount in rig.GetComponentsInChildren<ExtinguisherMount>()) mount.enabled = false;
            var grip = FindChild(rig.transform, "GripPoint");

            Placement(modelAsset, out Vector3 pos, out Quaternion rot);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, scene);
            model.transform.SetPositionAndRotation(pos, rot);
            AssignModelMaterials(model);

            // Mando: se invierte la cuenta de ExtinguisherMount con los defaults de GameSettings.
            Quaternion gripRot = grip.rotation;
            Quaternion anchorRot = gripRot * Quaternion.Inverse(Quaternion.Euler(GameSettings.DefaultTankRotation));
            Vector3 anchorPos = grip.position - gripRot * (Vector3.up * -GameSettings.DefaultTankHeight + GameSettings.DefaultTankShift);
            var controller = GameObject.CreatePrimitive(PrimitiveType.Cube);
            controller.name = "Mando (calibración de fábrica)";
            controller.transform.SetPositionAndRotation(anchorPos, anchorRot);
            controller.transform.localScale = new Vector3(0.05f, 0.05f, 0.10f);
            var gripMark = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            gripMark.transform.position = grip.position;
            gripMark.transform.localScale = Vector3.one * 0.015f;

            var oldRenderers = new List<Renderer>();
            foreach (var r in rig.GetComponentsInChildren<Renderer>())
                if (!(r is ParticleSystemRenderer)) oldRenderers.Add(r);

            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var ghost = new Material(unlit); ghost.SetColor("_BaseColor", new Color(0.2f, 0.45f, 0.9f));
            var yellow = new Material(unlit); yellow.SetColor("_BaseColor", new Color(1f, 0.85f, 0.1f));
            var green = new Material(unlit); green.SetColor("_BaseColor", new Color(0.1f, 0.8f, 0.2f));
            foreach (var r in oldRenderers) r.sharedMaterials = Fill(ghost, r.sharedMaterials.Length);
            controller.GetComponent<Renderer>().sharedMaterial = yellow;
            gripMark.GetComponent<Renderer>().sharedMaterial = green;

            var lightGo = new GameObject("Luz");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightGo.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.55f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;

            Directory.CreateDirectory(PreviewDir);
            var center = new Vector3(OldAxisXZ.x, 0.23f, OldAxisXZ.y);
            var views = new (string name, Vector3 dir)[]
            {
                ("frente_-Z", Vector3.back), ("costado_+X", Vector3.right), ("costado_-X", Vector3.left),
                ("arriba", Vector3.up + Vector3.back * 0.001f), ("tres_cuartos", new Vector3(0.8f, 0.45f, -1f)),
            };

            var newOnly = new List<Renderer>(model.GetComponentsInChildren<Renderer>());
            newOnly.Add(controller.GetComponent<Renderer>());
            newOnly.Add(gripMark.GetComponent<Renderer>());

            foreach (var (name, dir) in views)
            {
                SetVisible(oldRenderers, true); SetVisible(newOnly, false);
                var a = Shot(center, dir);
                SetVisible(oldRenderers, false); SetVisible(newOnly, true);
                var b = Shot(center, dir);
                var outTex = Composite(a, b);
                File.WriteAllBytes(Path.Combine(PreviewDir, $"swap_{name}.png"), outTex.EncodeToPNG());
                Object.DestroyImmediate(a); Object.DestroyImmediate(b); Object.DestroyImmediate(outTex);
            }

            // Números para el checkpoint.
            var mb = WorldBounds(model);
            Vector3 outletPos = FindChild(model.transform, ExtinguisherHose.OutletName).position;
            var sb = new StringBuilder("[ExtinguisherModelTool] PREVIEW (espacio de la raíz del ExtinguisherRig, metros):\n");
            sb.AppendLine($"  GripPoint                 {grip.position:F4} (no cambia)");
            sb.AppendLine($"  Mando con defaults        {anchorPos:F4}");
            sb.AppendLine($"  Eje del tanque (x, z)     ({OldAxisXZ.x:F4}, {OldAxisXZ.y:F4}) (igual que el viejo)");
            sb.AppendLine($"  Cabeza viejo / nuevo      y={OldHeadY:F4} / y={pos.y + NewHeadY:F4}");
            sb.AppendLine($"  Fondo viejo / nuevo       y={-0.014f:F4} / y={mb.min.y:F4}");
            sb.AppendLine($"  Punta viejo / nuevo       y={OldTopY:F4} / y={mb.max.y:F4}");
            sb.AppendLine($"  Salida de la válvula      {outletPos:F4} (hacia {OldOutletDir})");
            sb.AppendLine($"  Mando → eje del tanque    {Vector2.Distance(new Vector2(anchorPos.x, anchorPos.z), OldAxisXZ) * 100f:0.0} cm");
            sb.AppendLine($"  Mando sobre el fondo      {(anchorPos.y - mb.min.y) * 100f:0.0} cm del nuevo (eje del rig)");
            sb.AppendLine($"  Renders en {Path.GetFullPath(PreviewDir)}");
            Debug.Log(sb.ToString());

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); // descarta la temporal
        }

        private static Material[] Fill(Material m, int n)
        {
            var a = new Material[Mathf.Max(1, n)];
            for (int i = 0; i < a.Length; i++) a[i] = m;
            return a;
        }

        private static void SetVisible(List<Renderer> rs, bool on)
        {
            foreach (var r in rs) r.enabled = on;
        }

        private static readonly Color Key = new Color(0f, 1f, 0f, 1f);

        private static Texture2D Shot(Vector3 center, Vector3 dir)
        {
            const int W = 700, H = 900;
            var camGo = new GameObject("PreviewCam");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 0.30f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Key;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 5f;
            cam.allowMSAA = false;
            cam.allowHDR = false;
            camGo.transform.position = center + dir.normalized * 2f;
            camGo.transform.rotation = Quaternion.LookRotation(-dir.normalized,
                Mathf.Abs(dir.normalized.y) > 0.9f ? Vector3.forward : Vector3.up);
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGo);
            return tex;
        }

        private static bool IsKey(Color c) => c.g > 0.95f && c.r < 0.05f && c.b < 0.05f;

        private static Texture2D Composite(Texture2D oldImg, Texture2D newImg)
        {
            var a = oldImg.GetPixels();
            var b = newImg.GetPixels();
            var o = new Color[a.Length];
            var bg = new Color(0.96f, 0.96f, 0.96f);
            for (int i = 0; i < o.Length; i++)
            {
                Color c = bg;
                if (!IsKey(a[i])) c = Color.Lerp(c, a[i], 0.45f);
                if (!IsKey(b[i])) c = Color.Lerp(c, b[i], 0.85f);
                o[i] = c;
            }
            var t = new Texture2D(oldImg.width, oldImg.height, TextureFormat.RGBA32, false);
            t.SetPixels(o);
            t.Apply();
            return t;
        }

        // ------------------------------------------------------------------ swap
        public static void SwapBatch()
        {
            if (!PrepareAssets()) return;
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var nozzleAsset = AssetDatabase.LoadAssetAtPath<GameObject>(NozzlePath);
            if (!CheckModelAxes(modelAsset)) return;

            // 1) Escena: el calco plano 'Label_ABC' fue agregado en la escena bajo Visual. Con Visual
            //    a escala 1 quedaría deformado y flotando; el modelo nuevo trae su banda 'Label'.
            var scene = EditorSceneManager.OpenScene(TrainingScene, OpenSceneMode.Single);
            int removed = 0;
            foreach (var sw in Object.FindObjectsByType<ExtinguisherSwappable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                foreach (var t in sw.GetComponentsInChildren<Transform>(true))
                    if (t != null && t.name.StartsWith("Label") && PrefabUtility.IsAddedGameObjectOverride(t.gameObject))
                    {
                        Undo.DestroyObjectImmediate(t.gameObject);
                        removed++;
                    }
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[ExtinguisherModelTool] Escena: {removed} calco(s) viejo(s) borrado(s).");

            // 2) Prefabs.
            if (!SwapTank(modelAsset) || !SwapNozzle(nozzleAsset)) return;

            // 3) Escena otra vez: overrides huérfanos (material del FBX viejo) y cableado completo.
            scene = EditorSceneManager.OpenScene(TrainingScene, OpenSceneMode.Single);
            var instances = new List<GameObject>();
            foreach (var root in scene.GetRootGameObjects())
            {
                var src = PrefabUtility.GetCorrespondingObjectFromSource(root);
                string p = src != null ? AssetDatabase.GetAssetPath(src) : null;
                if (p == RigPrefab || p == NozzlePrefab) instances.Add(root);
            }
            if (instances.Count > 0)
                PrefabUtility.RemoveUnusedOverrides(instances.ToArray(), InteractionMode.AutomatedAction);
            SceneWiringFixer.FixScene();
            if (!EditorSceneManager.SaveScene(scene))
                Debug.LogError($"[ExtinguisherModelTool] No se pudo guardar {TrainingScene}.");
            SceneWiringFixer.Validate();
        }

        private static bool SwapTank(GameObject modelAsset)
        {
            var root = PrefabUtility.LoadPrefabContents(RigPrefab);
            try
            {
                var rt = root.transform;
                var visual = rt.Find("Visual");
                if (visual == null) { Debug.LogError("[ExtinguisherModelTool] ExtinguisherRig sin 'Visual'."); return false; }
                if (FindModel(visual, ModelPath) != null)
                {
                    Debug.Log("[ExtinguisherModelTool] ExtinguisherRig ya tiene el modelo de 1 kg: no se toca.");
                    return true;
                }

                var grip = FindChild(rt, "GripPoint");
                Vector3 gripBefore = rt.InverseTransformPoint(grip.position);
                Quaternion gripRotBefore = Quaternion.Inverse(rt.rotation) * grip.rotation;

                // Poses (espacio raíz) de los hijos de Visual que no son el modelo: se restauran después
                // de devolver Visual a escala 1, así el GripPoint no se mueve ni un milímetro.
                var oldModel = FindModel(visual, OldModel);
                var keep = new List<(Transform t, Vector3 p, Quaternion r)>();
                foreach (Transform c in visual)
                    if (c != oldModel) keep.Add((c, c.position, c.rotation));

                // El Sizer es [ExecuteAlways] y en OnEnable reescalaría el modelo nuevo a 48 cm.
                foreach (var sizer in visual.GetComponents<ExtinguisherSizer>()) Object.DestroyImmediate(sizer);
                visual.localScale = Vector3.one;
                foreach (var (t, p, r) in keep) t.SetPositionAndRotation(p, r);

                if (oldModel != null) Object.DestroyImmediate(oldModel.gameObject);

                Placement(modelAsset, out Vector3 pos, out Quaternion rot);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, visual);
                model.name = "Extintor_1kg";
                model.transform.SetPositionAndRotation(rt.TransformPoint(pos), rt.rotation * rot);
                AssignModelMaterials(model);

                // La manguera sale de la válvula hacia afuera (radial), no según el empty del FBX.
                var outlet = FindChild(model.transform, ExtinguisherHose.OutletName);
                Vector3 axisPoint = rt.TransformPoint(new Vector3(OldAxisXZ.x, rt.InverseTransformPoint(outlet.position).y, OldAxisXZ.y));
                outlet.rotation = Quaternion.LookRotation((outlet.position - axisPoint).normalized, rt.up);

                Vector3 gripAfter = rt.InverseTransformPoint(grip.position);
                Debug.Log($"[ExtinguisherModelTool] ExtinguisherRig: GripPoint antes {gripBefore:F4} / después {gripAfter:F4}, " +
                          $"rotación {gripRotBefore.eulerAngles:F2} / {(Quaternion.Inverse(rt.rotation) * grip.rotation).eulerAngles:F2}. " +
                          $"Modelo en {pos:F4}, salida en {rt.InverseTransformPoint(outlet.position):F4}.");
                if ((gripAfter - gripBefore).magnitude > 1e-4f)
                {
                    Debug.LogError("[ExtinguisherModelTool] El GripPoint se movió: no se guarda el prefab.");
                    return false;
                }

                PrefabUtility.SaveAsPrefabAsset(root, RigPrefab);
                return true;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static bool SwapNozzle(GameObject nozzleAsset)
        {
            var root = PrefabUtility.LoadPrefabContents(NozzlePrefab);
            try
            {
                var rt = root.transform;
                var spray = root.GetComponentInChildren<ExtinguisherSpray>(true);
                if (spray == null) { Debug.LogError("[ExtinguisherModelTool] NozzleRig sin ExtinguisherSpray."); return false; }
                var tip = spray.transform;
                var parent = tip.parent != null ? tip.parent : rt;

                if (FindModel(rt, NozzlePath) == null)
                {
                    foreach (var n in new[] { "Hose", "Horn" })
                    {
                        var old = parent.Find(n);
                        if (old != null) Object.DestroyImmediate(old.gameObject);
                    }

                    var nozzle = (GameObject)PrefabUtility.InstantiatePrefab(nozzleAsset, parent);
                    nozzle.name = "Boquilla";
                    nozzle.transform.SetPositionAndRotation(tip.position, parent.rotation);
                    var inletT = FindChild(nozzle.transform, ExtinguisherHose.InletName);
                    // El origen del modelo es la punta: la entrada queda hacia atrás del chorro (−Z del tip).
                    Vector3 back = (inletT.position - nozzle.transform.position).normalized;
                    nozzle.transform.rotation = Quaternion.FromToRotation(back, -tip.forward) * nozzle.transform.rotation;
                    inletT.rotation = Quaternion.LookRotation(-tip.forward, tip.up);
                    foreach (var r in nozzle.GetComponentsInChildren<Renderer>(true))
                        r.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(PaintMaterial);
                    Debug.Log($"[ExtinguisherModelTool] NozzleRig: boquilla con la punta en {rt.InverseTransformPoint(tip.position):F4}, " +
                              $"entrada de la manguera en {rt.InverseTransformPoint(inletT.position):F4}.");
                }

                var inlet = FindChild(rt, ExtinguisherHose.InletName);
                var hose = root.GetComponentInChildren<ExtinguisherHose>(true);
                if (hose == null)
                {
                    var go = new GameObject("HoseTube");
                    go.transform.SetParent(rt, false);
                    hose = go.AddComponent<ExtinguisherHose>();
                }
                var so = new SerializedObject(hose);
                so.FindProperty("inlet").objectReferenceValue = inlet;
                so.ApplyModifiedPropertiesWithoutUndo();
                var mr = hose.GetComponent<MeshRenderer>();
                mr.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(PaintMaterial);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                PrefabUtility.SaveAsPrefabAsset(root, NozzlePrefab);
                return true;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // ------------------------------------------------------------------ helpers
        /// <summary>Cuerpo y banda de etiqueta: tintable / etiqueta; el resto (bronce, manómetro): atlas.</summary>
        private static void AssignModelMaterials(GameObject model)
        {
            var paint = AssetDatabase.LoadAssetAtPath<Material>(PaintMaterial);
            var details = AssetDatabase.LoadAssetAtPath<Material>(DetailsMaterial);
            var label = AssetDatabase.LoadAssetAtPath<Material>(LabelMaterial);
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.gameObject.name;
                r.sharedMaterial = n.StartsWith("Label") ? label : n.StartsWith("Details") ? details : paint;
                r.shadowCastingMode = n.StartsWith("Label") ? UnityEngine.Rendering.ShadowCastingMode.Off
                                                            : UnityEngine.Rendering.ShadowCastingMode.On;
            }
        }

        /// <summary>Raíz de la instancia del modelo <paramref name="assetPath"/> colgada de <paramref name="parent"/>.</summary>
        private static Transform FindModel(Transform parent, string assetPath)
        {
            foreach (var t in parent.GetComponentsInChildren<Transform>(true))
            {
                if (!PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)) continue;
                var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
                if (src != null && AssetDatabase.GetAssetPath(src) == assetPath) return t;
            }
            return null;
        }

        private static Transform FindChild(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        private static Bounds LocalBounds(Transform space, Transform root)
        {
            bool any = false;
            Bounds b = default;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                foreach (var v in mf.sharedMesh.vertices)
                {
                    Vector3 p = space.InverseTransformPoint(mf.transform.TransformPoint(v));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            return b;
        }

        private static Bounds WorldBounds(GameObject go)
        {
            bool any = false;
            Bounds b = default;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            return b;
        }

        /// <summary>Triángulos de todas las mallas bajo <paramref name="root"/>. Lo usa Validate.</summary>
        public static int TriangleCount(Transform root)
        {
            int n = 0;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                for (int s = 0; s < mf.sharedMesh.subMeshCount; s++)
                    n += (int)mf.sharedMesh.GetIndexCount(s) / 3;
            }
            return n;
        }
    }
}
