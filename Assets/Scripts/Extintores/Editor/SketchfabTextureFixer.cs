using System.IO;
using UnityEditor;
using UnityEngine;

namespace Extintores.EditorTools
{
    /// <summary>
    /// Corrige los ajustes de import de las texturas descargadas de Sketchfab (que Unity trae mal
    /// por defecto y hacen que los modelos se vean "raros" aunque no salgan rosa):
    ///  • Normal maps  → <c>textureType = NormalMap</c> (si no, dan relieve/brillo incorrecto).
    ///  • Mapas de DATOS (Metallic, Roughness, AO, Curvature, Height, Mask, Opacity, MetallicSmoothness)
    ///    → <c>sRGB = OFF</c> (lineal); si quedan en sRGB la iluminación PBR sale mal.
    ///  • Color/Emissive (BaseColor, Albedo, Diffuse, Emissive) → sRGB ON.
    ///
    /// NO reasigna los mapas a los materiales (eso es manual o con un builder aparte): solo deja
    /// las TEXTURAS bien para que, al asignarlas al material URP/Lit, se vean correctas.
    ///
    /// Uso: menú <b>Extintores ▸ Fix Sketchfab Textures (FireCauses)</b>. Corre sobre
    /// <c>Assets/ThirdParty/FireCauses</c> (o sobre la carpeta seleccionada en el Project).
    /// </summary>
    public static class SketchfabTextureFixer
    {
        private const string DefaultFolder = "Assets/ThirdParty/FireCauses";

        [MenuItem("Extintores/Fix Sketchfab Textures (FireCauses)")]
        public static void FixDefault()
        {
            string folder = SelectedFolderOr(DefaultFolder);
            FixFolder(folder);
        }

        public static void FixFolder(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                Debug.LogWarning($"[TexFixer] Carpeta inválida: '{folder}'. Seleccioná una carpeta " +
                                 "en el Project o creá Assets/ThirdParty/FireCauses.");
                return;
            }

            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder });
            int normals = 0, linear = 0, srgb = 0;

            foreach (var g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) continue;
                string n = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();

                if (IsNormal(n))
                {
                    if (ti.textureType != TextureImporterType.NormalMap)
                    {
                        ti.textureType = TextureImporterType.NormalMap;
                        ti.SaveAndReimport();
                        normals++;
                    }
                }
                else if (IsData(n))
                {
                    if (ti.sRGBTexture)
                    {
                        ti.sRGBTexture = false;
                        ti.SaveAndReimport();
                        linear++;
                    }
                }
                else // color / emissive
                {
                    if (!ti.sRGBTexture)
                    {
                        ti.sRGBTexture = true;
                        ti.SaveAndReimport();
                        srgb++;
                    }
                }
            }

            AssetDatabase.Refresh();
            Debug.Log($"[TexFixer] {folder}: {normals} marcadas como Normal, {linear} pasadas a lineal, " +
                      $"{srgb} a sRGB. Revisadas {guids.Length}. Ahora asigná los mapas al material URP/Lit " +
                      "(BaseMap, Normal, Metallic, Occlusion, Emission).");
        }

        private static string SelectedFolderOr(string fallback)
        {
            foreach (var o in Selection.GetFiltered<Object>(SelectionMode.Assets))
            {
                string p = AssetDatabase.GetAssetPath(o);
                if (AssetDatabase.IsValidFolder(p)) return p;
            }
            return fallback;
        }

        // Normal: "normal", "_nrm", o sufijo "_n".
        private static bool IsNormal(string n) =>
            n.Contains("normal") || n.Contains("_nrm") || n.EndsWith("_n");

        // Datos (lineal): mapas que NO son color.
        private static bool IsData(string n) =>
            n.Contains("metal") || n.Contains("rough") || n.Contains("gloss") || n.Contains("smoothness") ||
            n.Contains("occlusion") || n.Contains("_ao") || n.EndsWith("ao") || n.Contains("curv") ||
            n.Contains("height") || n.Contains("mask") || n.Contains("opacity") || n.Contains("orm") ||
            n.Contains("specular") || n.EndsWith("_r") || n.EndsWith("_m");
    }
}
