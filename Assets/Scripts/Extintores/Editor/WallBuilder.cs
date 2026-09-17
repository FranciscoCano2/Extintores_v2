using System.IO;
using UnityEditor;
using UnityEngine;

namespace Extintores.EditorTools
{
    /// <summary>
    /// Construye el prefab de la PARED DE RECARGA (<see cref="ExtinguisherStation"/>) con una bahía
    /// (<see cref="ExtinguisherBay"/>) por tipo de extintor: panel de fondo + por bahía un marco
    /// coloreado, una etiqueta (textura de <c>Assets/Extintores/Labels</c>), un volumen-trigger y un
    /// <see cref="SprayButton"/> para elegirla con el chorro.
    ///
    /// Con primitivas y materiales URP generados: no depende de assets externos. El usuario arrastra
    /// el prefab a la escena, lo posiciona sobre una pared y ajusta tamaños. Para que funcione el
    /// swap: layer <b>Interactor</b> creada, y el tanque (ExtinguisherRig) con
    /// <see cref="ExtinguisherSwappable"/> + un collider sensor (trigger) en esa layer.
    ///
    /// ⚠ <b>Correrlo sobre-escribe el asset</b>, y con eso cambian los fileID internos: los overrides
    /// que la instancia de la escena tenga sobre objetos de adentro del prefab se pierden. La
    /// instancia de <c>Training.unity</c> sólo overridea su propio transform y el nombre, así que es
    /// seguro; si en el futuro se ajusta algo dentro del panel a mano, ajustarlo ACÁ.
    ///
    /// Uso: menú <b>Extintores ▸ Build Extinguisher Wall</b>. Idempotente (sobre-escribe).
    /// </summary>
    public static class WallBuilder
    {
        private const string LabelsFolder = "Assets/Extintores/Labels";
        private const string MatFolder = "Assets/Extintores/Wall/Materials";
        private const string OutPrefab = "Assets/Prefabs/ExtinguisherStation.prefab";

        // Medidas del panel. Es de MESA, no de pared: tiene que entrar entero al costado del usuario
        // y leerse de un vistazo. La versión original (2,45 × 1,3 m) tapaba media zona.
        private const float Pitch = 0.20f;   // separación entre bahías
        private const float Height = 0.62f;  // alto del panel
        private const float Margin = 0.10f;  // sobrante a los costados

        private struct BayDef
        {
            public AgentType Agent;
            public string Label;
            public string Caption; // rótulo del botón: corto, se lee a 1,5 m

            // El color no se declara acá ni se serializa en la bahía: sale siempre de AgentColor,
            // el mismo que usa el tanque de la mano. Duplicarlo dejaba que panel y extintor
            // discreparan sin que nada lo delatara.
        }

        // Los 6 agentes de AgentType, sin excepciones: un agente que existe en la matriz de
        // efectividad pero no tiene bahía es inalcanzable en juego, y BC enseña algo que ABC no
        // (el polvo BC NO sirve para clase A). El builder tolera una etiqueta faltante (el botón ya
        // lleva el nombre), pero el quad queda liso: SceneWiringFixer la asigna si aparece después.
        private static readonly BayDef[] Bays =
        {
            new BayDef { Agent = AgentType.Water,       Label = "Label_Water",       Caption = "AGUA" },
            new BayDef { Agent = AgentType.Foam,        Label = "Label_Foam",        Caption = "ESPUMA" },
            new BayDef { Agent = AgentType.CO2,         Label = "Label_CO2",         Caption = "CO2" },
            new BayDef { Agent = AgentType.DryChemABC,  Label = "Label_ABC",         Caption = "ABC" },
            new BayDef { Agent = AgentType.DryChemBC,   Label = "Label_BC",          Caption = "BC" },
            new BayDef { Agent = AgentType.WetChemical, Label = "Label_WetChemical", Caption = "K" },
        };

        /// <summary>Id de la etiqueta de un agente (<c>Label_BC</c>), o null si no tiene bahía.</summary>
        internal static string LabelIdOf(AgentType agent)
        {
            foreach (var b in Bays)
                if (b.Agent == agent) return b.Label;
            return null;
        }

        internal static string LabelTexturePath(string labelId) => $"{LabelsFolder}/{labelId}.png";

        [MenuItem("Extintores/Build Extinguisher Wall")]
        public static void Build()
        {
            EnsureFolder(MatFolder);
            EnsureFolder("Assets/Prefabs");

            var root = new GameObject("ExtinguisherStation");
            root.AddComponent<ExtinguisherStation>();
            root.AddComponent<BayHighlighter>(); // resalta la bahía correcta/peligrosa

            // Panel de mesa, no pared: ~1,1 m de ancho para que entre entero al costado del usuario
            // y se lea de un vistazo. Antes era 2.45 × 1.3 m y quedaba enorme.
            int n = Bays.Length;
            float width = n * Pitch + Margin;

            // Panel de fondo.
            var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            board.name = "Backboard";
            StripCollider(board);
            board.transform.SetParent(root.transform, false);
            board.transform.localScale = new Vector3(width, Height, 0.05f);
            board.transform.localPosition = new Vector3(0f, Height / 2f, 0f);
            board.GetComponent<MeshRenderer>().sharedMaterial =
                GetColorMat("Board", new Color(0.85f, 0.85f, 0.85f));

            // El eje X del panel corre al revés que el del usuario (mira desde −Z), así que las
            // bahías se dibujan de derecha a izquierda para que el orden Agua→K se lea normal.
            float x0 = (Pitch * (n - 1)) / 2f;
            float bayY = Height * 0.68f;   // etiqueta arriba
            float buttonY = Height * 0.27f; // botón abajo, a la altura de la mano

            for (int i = 0; i < n; i++)
            {
                var b = Bays[i];
                var bay = new GameObject($"Bay_{b.Agent}");
                bay.transform.SetParent(root.transform, false);
                // Todo el frente del panel es su −Z: es la cara legible de los Quad/TextMesh de
                // Unity y por lo tanto el lado desde el que mira el usuario. El trigger tiene que
                // abrirse hacia ESE lado, o habría que leer los rótulos de un lado y meter el
                // tanque por el otro.
                bay.transform.localPosition = new Vector3(x0 - i * Pitch, bayY, -0.04f);

                // Volumen-trigger de la bahía + componente (auto agrega RB kinemático y layer Interactor).
                // Es más profundo (Z) que ancho: se entra de frente con el tanque y no se dispara la
                // bahía de al lado, que ahora está a solo 18 cm.
                var box = bay.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(0.16f, 0.18f, 0.22f);
                box.center = new Vector3(0f, 0f, -0.10f);

                var comp = bay.AddComponent<ExtinguisherBay>();
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(LabelTexturePath(b.Label));

                // Botón del chorro: es la forma normal de elegir el extintor. Cuelga de la bahía
                // (ExtinguisherBay lo engancha solo en Awake) pero queda por DEBAJO de la etiqueta y
                // salido del panel, para que las partículas le peguen sin rozar el backboard.
                var selector = NewSelectorButton(bay.transform, b.Caption,
                                                 new Vector3(0f, buttonY - bayY, -0.03f));
                ConfigureBay(comp, b.Agent, tex, selector);

                // Marco coloreado.
                var frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
                frame.name = "Frame";
                StripCollider(frame);
                frame.transform.SetParent(bay.transform, false);
                frame.transform.localScale = new Vector3(0.12f, 0.14f, 0.03f);
                frame.GetComponent<MeshRenderer>().sharedMaterial = GetAgentMat(b.Agent);

                // Etiqueta (quad al frente).
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = "Label";
                StripCollider(q);
                q.transform.SetParent(bay.transform, false);
                // Del lado −Z, que es la cara legible de un Quad de Unity. El builder original lo
                // ponía en +Z y hubo que corregirlo a mano en el prefab; queda arreglado en origen.
                // El trigger se abre igual hacia +Z (ver el center de arriba): el usuario mete el
                // tanque por atrás del rótulo, que es lo que se ve desde el lobby.
                q.transform.localPosition = new Vector3(0f, 0f, -0.025f);
                q.transform.localScale = Vector3.one * 0.13f;
                if (tex != null)
                    q.GetComponent<MeshRenderer>().sharedMaterial = GetLabelMat(b.Label, tex);
            }

            PrefabUtility.SaveAsPrefabAsset(root, OutPrefab);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[WallBuilder] {OutPrefab} con {n} bahías, {width:0.##} × {Height:0.##} m, cada " +
                      "una con su botón 'Selector' accionable con el chorro. Si ya había una instancia " +
                      "en la escena, revisala: el asset se rehizo entero. El tanque necesita " +
                      "ExtinguisherSwappable + collider sensor (trigger) en layer 'Interactor'. Si las " +
                      "etiquetas se ven al revés, rotá el quad 'Label' 180° en Y.");
        }

        private static void ConfigureBay(ExtinguisherBay comp, AgentType agent,
                                         Texture2D label, SprayButton selector)
        {
            // El color no se setea: ExtinguisherBay.BodyColor lo deriva del agente.
            var so = new SerializedObject(comp);
            var pa = so.FindProperty("agent");    if (pa != null) pa.enumValueIndex = (int)agent;
            var pl = so.FindProperty("label");    if (pl != null) pl.objectReferenceValue = label;
            var ps = so.FindProperty("selector"); if (ps != null) ps.objectReferenceValue = selector;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Botón de chorro de una bahía. Mismo componente que el menú, así que el gesto es idéntico.
        /// El BoxCollider se agrega acá aunque <see cref="SprayButton"/> lo cree solo en Awake: en el
        /// editor conviene verlo con la medida real para poder acomodar el panel sin darle Play.
        /// </summary>
        private static SprayButton NewSelectorButton(Transform parent, string caption, Vector3 localPos)
        {
            var size = new Vector2(0.17f, 0.13f);

            var go = new GameObject("Selector");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;

            var b = go.AddComponent<SprayButton>();
            var so = new SerializedObject(b);
            var t = so.FindProperty("text"); if (t != null) t.stringValue = caption;
            var s = so.FindProperty("size"); if (s != null) s.vector2Value = size;
            so.ApplyModifiedPropertiesWithoutUndo();

            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(size.x, size.y, 0.06f);

            // La bahía se pone en layer 'Interactor' en su Awake; el botón DEBE quedar en
            // 'Interactable' o el chorro lo atraviesa sin accionarlo.
            int layer = LayerMask.NameToLayer(SprayButton.LayerName);
            if (layer >= 0) go.layer = layer;

            return b;
        }

        private static void StripCollider(GameObject go)
        {
            var c = go.GetComponent<Collider>();
            if (c != null) Object.DestroyImmediate(c);
        }

        // --- Materiales ---
        /// <summary>
        /// Material del marco de una bahía, con el mismo aspecto que va a tomar el tanque al
        /// elegirla: color + metal. Si el marco de clase K fuese gris mate y el tanque acero, el
        /// panel estaría enseñando un color que después no aparece.
        /// </summary>
        private static Material GetAgentMat(AgentType agent)
        {
            var mat = GetColorMat(agent.ToString(), AgentColor.Of(agent));
            mat.SetFloat("_Metallic", AgentColor.MetallicOf(agent));
            mat.SetFloat("_Smoothness", AgentColor.SmoothnessOf(agent));
            EditorUtility.SetDirty(mat);
            return mat;
        }

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

        internal static Material GetLabelMat(string id, Texture2D tex)
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
