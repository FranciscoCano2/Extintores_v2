using System.IO;
using UnityEditor;
using UnityEngine;

namespace Extintores.EditorTools
{
    /// <summary>
    /// Construye el prefab de la PARED DE RECARGA (<see cref="ExtinguisherStation"/>) con una bahía
    /// (<see cref="ExtinguisherBay"/>) por tipo de extintor: panel de fondo + por bahía un marco
    /// coloreado, una etiqueta (textura de <c>Assets/Extintores/Labels</c>) y un volumen-trigger.
    ///
    /// Con primitivas y materiales URP generados: no depende de assets externos. El usuario arrastra
    /// el prefab a la escena, lo posiciona sobre una pared y ajusta tamaños. Para que funcione el
    /// swap: layer <b>Interactor</b> creada, y el tanque (ExtinguisherRig) con
    /// <see cref="ExtinguisherSwappable"/> + un collider sensor (trigger) en esa layer.
    ///
    /// Uso: menú <b>Extintores ▸ Build Extinguisher Wall</b>. Idempotente (sobre-escribe).
    /// </summary>
    public static class WallBuilder
    {
        private const string LabelsFolder = "Assets/Extintores/Labels";
        private const string MatFolder = "Assets/Extintores/Wall/Materials";
        private const string OutPrefab = "Assets/Prefabs/ExtinguisherStation.prefab";

        private struct BayDef { public AgentType Agent; public string Label; public Color Color; }

        private static readonly BayDef[] Bays =
        {
            new BayDef { Agent = AgentType.Water,       Label = "Label_Water",       Color = new Color(0.00f, 0.37f, 0.70f) },
            new BayDef { Agent = AgentType.Foam,        Label = "Label_Foam",        Color = new Color(0.75f, 0.60f, 0.28f) },
            new BayDef { Agent = AgentType.CO2,         Label = "Label_CO2",         Color = new Color(0.14f, 0.14f, 0.14f) },
            new BayDef { Agent = AgentType.DryChemABC,  Label = "Label_ABC",         Color = new Color(0.78f, 0.10f, 0.10f) },
            new BayDef { Agent = AgentType.WetChemical, Label = "Label_WetChemical", Color = new Color(0.80f, 0.70f, 0.22f) },
        };

        [MenuItem("Extintores/Build Extinguisher Wall")]
        public static void Build()
        {
            EnsureFolder(MatFolder);
            EnsureFolder("Assets/Prefabs");

            var root = new GameObject("ExtinguisherStation");
            root.AddComponent<ExtinguisherStation>();
            root.AddComponent<BayHighlighter>(); // resalta la bahía correcta/peligrosa

            int n = Bays.Length;
            const float pitch = 0.45f, height = 1.3f;
            float width = n * pitch + 0.2f;

            // Panel de fondo.
            var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            board.name = "Backboard";
            StripCollider(board);
            board.transform.SetParent(root.transform, false);
            board.transform.localScale = new Vector3(width, height, 0.08f);
            board.transform.localPosition = new Vector3(0f, height / 2f, 0f);
            board.GetComponent<MeshRenderer>().sharedMaterial =
                GetColorMat("Board", new Color(0.85f, 0.85f, 0.85f));

            float x0 = -(pitch * (n - 1)) / 2f;
            float bayY = height * 0.55f;

            for (int i = 0; i < n; i++)
            {
                var b = Bays[i];
                var bay = new GameObject($"Bay_{b.Agent}");
                bay.transform.SetParent(root.transform, false);
                bay.transform.localPosition = new Vector3(x0 + i * pitch, bayY, 0.06f);

                // Volumen-trigger de la bahía + componente (auto agrega RB kinemático y layer Interactor).
                var box = bay.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(0.30f, 0.40f, 0.35f);
                box.center = new Vector3(0f, 0f, 0.12f);

                var comp = bay.AddComponent<ExtinguisherBay>();
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{LabelsFolder}/{b.Label}.png");
                ConfigureBay(comp, b.Agent, b.Color, tex);

                // Marco coloreado.
                var frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
                frame.name = "Frame";
                StripCollider(frame);
                frame.transform.SetParent(bay.transform, false);
                frame.transform.localScale = new Vector3(0.28f, 0.34f, 0.04f);
                frame.GetComponent<MeshRenderer>().sharedMaterial = GetColorMat(b.Agent.ToString(), b.Color);

                // Etiqueta (quad al frente).
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = "Label";
                StripCollider(q);
                q.transform.SetParent(bay.transform, false);
                q.transform.localPosition = new Vector3(0f, 0f, 0.021f); // apenas delante del marco
                q.transform.localScale = Vector3.one * 0.26f;
                if (tex != null)
                    q.GetComponent<MeshRenderer>().sharedMaterial = GetLabelMat(b.Label, tex);
            }

            PrefabUtility.SaveAsPrefabAsset(root, OutPrefab);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[WallBuilder] {OutPrefab} con {n} bahías. Arrastralo a la escena, posicionalo " +
                      "en una pared y ajustá tamaño/orientación. El tanque necesita ExtinguisherSwappable " +
                      "+ collider sensor (trigger) en layer 'Interactor'. Si las etiquetas se ven al revés, " +
                      "rotá el quad 'Label' 180° en Y.");
        }

        private static void ConfigureBay(ExtinguisherBay comp, AgentType agent, Color color, Texture2D label)
        {
            var so = new SerializedObject(comp);
            var pa = so.FindProperty("agent");     if (pa != null) pa.enumValueIndex = (int)agent;
            var pc = so.FindProperty("bodyColor");  if (pc != null) pc.colorValue = color;
            var pl = so.FindProperty("label");      if (pl != null) pl.objectReferenceValue = label;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void StripCollider(GameObject go)
        {
            var c = go.GetComponent<Collider>();
            if (c != null) Object.DestroyImmediate(c);
        }

        // --- Materiales ---
        private static Material GetColorMat(string id, Color color)
        {
            string path = $"{MatFolder}/M_Wall_{id}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) { SetColor(existing, color); return existing; }

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            SetColor(mat, color);
            mat.SetFloat("_Smoothness", 0.2f);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static Material GetLabelMat(string id, Texture2D tex)
        {
            string path = $"{MatFolder}/M_{id}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0.1f);
            mat.SetFloat("_AlphaClip", 1f);
            mat.SetFloat("_Cutoff", 0.5f);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetFloat("_Cull", 0f); // doble cara: la etiqueta se ve desde ambos lados
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
