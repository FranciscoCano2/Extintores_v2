using System.IO;
using UnityEditor;
using UnityEngine;

namespace Extintores.EditorTools
{
    /// <summary>
    /// Genera el material con el que el tanque puede <b>pintarse del color de su agente</b>
    /// (azul agua, negro CO2, amarillo K…), conservando el desgaste y los rótulos del modelo.
    ///
    /// El problema: URP/Lit multiplica <c>_BaseMap × _BaseColor</c>, y la textura del pack es un
    /// tanque ROJO. Rojo × azul ≈ negro: teñir directamente sobre ella no da un extintor azul, da
    /// un extintor sucio. Por eso se genera una copia <b>desaturada</b> y el tinte se aplica sobre
    /// esa; el resultado es chapa pintada del color correcto, con todas las marcas de uso intactas.
    ///
    /// Se desatura con <b>max(R,G,B)</b> y no con la luminancia perceptual: la luminancia de un rojo
    /// saturado es ~0,16, o sea que el tanque quedaría casi negro y cualquier tinte encima saldría
    /// apagado. max(R,G,B) manda la pintura viva a gris claro, que es lo que hace falta para que
    /// después tome cualquier color con el mismo brillo aparente.
    ///
    /// Uso: menú <b>Extintores ▸ Build Extinguisher Body Material</b>. Idempotente. Después
    /// <b>Extintores ▸ Fix Scene Wiring</b> se lo asigna al tanque de la escena.
    /// </summary>
    public static class ExtinguisherBodyTool
    {
        private const string SourceTexture = "Assets/Fire_Extinguisher/Textures/FireExtinguisher_Diffuse.tga";
        private const string OutFolder = "Assets/Extintores/Extinguisher";
        private const string GrayTexture = OutFolder + "/FireExtinguisher_Diffuse_Gray.png";

        /// <summary>Material del cuerpo del tanque. Lo busca <c>SceneWiringFixer</c>.</summary>
        public const string BodyMaterial = OutFolder + "/M_Extinguisher_Body.mat";

        [MenuItem("Extintores/Build Extinguisher Body Material", priority = 34)]
        public static void Build()
        {
            var src = AssetDatabase.LoadAssetAtPath<Texture2D>(SourceTexture);
            if (src == null)
            {
                Debug.LogError($"[ExtinguisherBodyTool] No encontré '{SourceTexture}'.");
                return;
            }

            EnsureFolder(OutFolder);

            if (!WriteGrayscale(src)) return;

            var gray = AssetDatabase.LoadAssetAtPath<Texture2D>(GrayTexture);
            var mat = GetOrCreateMaterial(gray);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[ExtinguisherBodyTool] '{BodyMaterial}' listo (mapa desaturado en " +
                      $"'{GrayTexture}'). Ahora corré 'Extintores ▸ Fix Scene Wiring' para " +
                      "asignárselo al tanque y cablear el tinte por tipo de extintor.\n" +
                      $"Material: {mat.name}");
        }

        /// <summary>
        /// Lee la textura del pack y escribe la copia desaturada. Enciende <c>isReadable</c> en el
        /// importador y lo <b>deja como estaba</b>: dejarlo prendido duplica la memoria de una
        /// textura de 2048² en el build sin que nada lo avise.
        /// </summary>
        private static bool WriteGrayscale(Texture2D src)
        {
            var importer = AssetImporter.GetAtPath(SourceTexture) as TextureImporter;
            if (importer == null)
            {
                Debug.LogError($"[ExtinguisherBodyTool] '{SourceTexture}' no tiene TextureImporter.");
                return false;
            }

            bool wasReadable = importer.isReadable;
            try
            {
                if (!wasReadable)
                {
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                    src = AssetDatabase.LoadAssetAtPath<Texture2D>(SourceTexture);
                }

                var pixels = src.GetPixels32();
                for (int i = 0; i < pixels.Length; i++)
                {
                    var p = pixels[i];
                    byte v = p.r > p.g ? (p.r > p.b ? p.r : p.b) : (p.g > p.b ? p.g : p.b);
                    pixels[i] = new Color32(v, v, v, p.a);
                }

                var dst = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
                dst.SetPixels32(pixels);
                dst.Apply();

                File.WriteAllBytes(GrayTexture, dst.EncodeToPNG());
                Object.DestroyImmediate(dst);

                AssetDatabase.ImportAsset(GrayTexture, ImportAssetOptions.ForceUpdate);
                Debug.Log($"[ExtinguisherBodyTool] Copia desaturada de {src.width}×{src.height} escrita.");
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ExtinguisherBodyTool] No pude generar la textura: {e.Message}");
                return false;
            }
            finally
            {
                if (!wasReadable)
                {
                    importer.isReadable = false;
                    importer.SaveAndReimport();
                }
            }
        }

        private static Material GetOrCreateMaterial(Texture2D gray)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(BodyMaterial);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, BodyMaterial);
            }

            mat.SetTexture("_BaseMap", gray);
            mat.SetTexture("_MainTex", gray);
            // Arranca en el rojo del ABC, que es el agente por defecto: así el tanque se ve bien en
            // el editor y no parece roto. En runtime lo pisa ExtinguisherSwappable según el agente.
            mat.SetColor("_BaseColor", AgentColor.Of(AgentType.DryChemABC));
            mat.SetFloat("_Smoothness", 0.45f); // chapa pintada, no plástico mate
            mat.SetFloat("_Metallic", 0.1f);
            EditorUtility.SetDirty(mat);
            return mat;
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
