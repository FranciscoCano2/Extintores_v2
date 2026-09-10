using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Extintores.EditorTools
{
    /// <summary>
    /// Repara los <c>Fire_X.prefab</c> ya generados, <b>sin regenerarlos</b>:
    /// <list type="bullet">
    ///   <item><b>Re-parenta <c>Flame</c> a la raíz.</b> En <c>Fire_B</c> la llama colgaba de
    ///         <c>Cause</c> (el auto, escala ×100), que es lo que producía la "B gigante": el badge
    ///         heredaba esa escala. <see cref="FireClassBadge"/> ya lo compensa, pero mientras la
    ///         jerarquía siga distinta a la de A/C/K cualquier cosa que lea escalas vuelve a
    ///         tropezarse con lo mismo.</item>
    ///   <item><b>Asigna el crepitar</b> a cada <see cref="FireSource"/> según su clase.</item>
    /// </list>
    ///
    /// ⚠️ Se edita el ASSET con <c>LoadPrefabContents</c> → <c>SaveAsPrefabAsset</c>. Correr
    /// <c>Build Fire Prefabs</c> haría lo mismo pero desde una raíz nueva, y eso les cambia los
    /// fileID: los <see cref="FireStation"/> de <c>Training.unity</c> quedarían apuntando a la nada.
    ///
    /// Idempotente. Uso: menú <b>Extintores ▸ Fix Fire Prefabs</b>.
    /// </summary>
    public static class FirePrefabRepair
    {
        private const string FiresFolder = "Assets/Prefabs/Fires";
        private const string FlameName = "Flame";

        [MenuItem("Extintores/Fix Fire Prefabs", priority = 33)]
        public static void Fix()
        {
            var log = new List<string>();
            int touched = 0;

            foreach (FireClass fc in System.Enum.GetValues(typeof(FireClass)))
            {
                string path = $"{FiresFolder}/Fire_{fc}.prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                {
                    log.Add($"⚠ Falta '{path}'.");
                    continue;
                }
                if (FixOne(path, fc, log)) touched++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var sb = new System.Text.StringBuilder($"[FirePrefabRepair] {touched} prefab(s) modificado(s):\n");
            foreach (var l in log) sb.AppendLine("  · " + l);
            Debug.Log(sb.ToString());
        }

        private static bool FixOne(string path, FireClass fireClass, List<string> log)
        {
            var contents = PrefabUtility.LoadPrefabContents(path);
            bool changed = false;

            try
            {
                var flame = FindChild(contents.transform, FlameName);
                if (flame == null)
                {
                    log.Add($"Fire_{fireClass}: no encontré '{FlameName}'.");
                    return false;
                }

                // 1. Flame directo bajo la raíz, conservando su pose en el mundo. Sin
                //    worldPositionStays la llama saltaría de lugar: hoy su transform local está
                //    expresado dentro de un padre escalado ×100.
                if (flame.parent != contents.transform)
                {
                    string from = flame.parent != null ? flame.parent.name : "(nada)";
                    flame.SetParent(contents.transform, true);
                    log.Add($"Fire_{fireClass}: '{FlameName}' movido de '{from}' a la raíz " +
                            $"(escala local {flame.localScale.x:0.###}).");
                    changed = true;
                }

                // 2. Crepitar según la clase.
                var source = flame.GetComponent<FireSource>() ?? contents.GetComponentInChildren<FireSource>(true);
                if (source == null)
                {
                    log.Add($"Fire_{fireClass}: sin FireSource, no puedo asignar el sonido.");
                }
                else
                {
                    var clip = FirePrefabBuilder.SfxFor(fireClass);
                    if (clip == null)
                    {
                        log.Add($"Fire_{fireClass}: no encontré el clip de audio del pack de VFX.");
                    }
                    else
                    {
                        var so = new SerializedObject(source);
                        var p = so.FindProperty("loopClip");
                        if (p != null && p.objectReferenceValue != clip)
                        {
                            p.objectReferenceValue = clip;
                            so.ApplyModifiedPropertiesWithoutUndo();
                            log.Add($"Fire_{fireClass}: crepitar '{clip.name}' asignado.");
                            changed = true;
                        }
                    }
                }

                if (changed) PrefabUtility.SaveAsPrefabAsset(contents, path);
                else log.Add($"Fire_{fireClass}: ya estaba bien.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            return changed;
        }

        private static Transform FindChild(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
