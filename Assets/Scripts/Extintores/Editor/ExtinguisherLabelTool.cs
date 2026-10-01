using UnityEditor;
using UnityEngine;

namespace Extintores.EditorTools
{
    /// <summary>
    /// Pega una etiqueta (calco) de identificación en el extintor seleccionado. Crea un Quad hijo
    /// con un material URP/Lit + <b>Alpha Clipping</b> (el borde transparente del PNG se recorta y
    /// no hay problemas de orden/transparencia), usando la textura de <c>Assets/Extintores/Labels</c>.
    ///
    /// Uso: seleccioná el tanque del extintor (o el nodo visual) en la jerarquía y ejecutá
    /// <b>Extintores ▸ Add ABC Label to Selected</b>. Después movés/rotás/escalás el "Label_ABC"
    /// para pegarlo en la cara del tanque (si se ve del lado equivocado, rotalo 180° en Y).
    /// </summary>
    public static class ExtinguisherLabelTool
    {
        private const string LabelTex = "Assets/Extintores/Labels/Label_ABC.png";
        private const string MatPath  = "Assets/Extintores/Labels/M_Label_ABC.mat";

        [MenuItem("Extintores/Add ABC Label to Selected")]
        public static void AddAbcLabel()
        {
            var sel = Selection.activeTransform;
            if (sel == null)
            {
                Debug.LogWarning("[LabelTool] Seleccioná primero el tanque del extintor en la jerarquía.");
                return;
            }

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(LabelTex);
            if (tex == null)
            {
                Debug.LogWarning($"[LabelTool] No se encontró la textura '{LabelTex}'.");
                return;
            }

            var mat = GetOrCreateMaterial(tex);

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Label_ABC";
            var col = quad.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col); // el calco no colisiona
            quad.GetComponent<MeshRenderer>().sharedMaterial = mat;

            Undo.RegisterCreatedObjectUndo(quad, "Add ABC Label");
            quad.transform.SetParent(sel, false);
            quad.transform.localPosition = Vector3.zero;
            quad.transform.localRotation = Quaternion.identity;
            quad.transform.localScale = Vector3.one * 0.12f; // ~12 cm; ajustá al tamaño del tanque

            Selection.activeGameObject = quad;
            Debug.Log("[LabelTool] 'Label_ABC' creado. Posicionalo sobre la cara del tanque " +
                      "(muévelo en +Z local ~1-2 cm y rotalo si se ve invertido).");
        }

        private static Material GetOrCreateMaterial(Texture2D tex)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (existing != null) return existing;

            Material mat;
            var urp = Shader.Find("Universal Render Pipeline/Lit");
            if (urp != null)
            {
                mat = new Material(urp);
                mat.SetTexture("_BaseMap", tex);
                mat.SetColor("_BaseColor", Color.white);
                mat.SetFloat("_Smoothness", 0.1f);
                // Alpha clipping: recorta el borde transparente, queda opaco → sin sorting issues.
                mat.SetFloat("_AlphaClip", 1f);
                mat.SetFloat("_Cutoff", 0.5f);
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.SetFloat("_Cull", 0f); // doble cara: visible desde ambos lados
            }
            else
            {
                mat = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex };
            }

            AssetDatabase.CreateAsset(mat, MatPath);
            AssetDatabase.SaveAssets();
            return mat;
        }
    }
}
