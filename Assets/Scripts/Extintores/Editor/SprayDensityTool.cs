using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Extintores.EditorTools
{
    /// <summary>
    /// Reescribe los parámetros del chorro (<see cref="ExtinguisherSpray"/>) del prefab
    /// <c>NozzleRig</c> con los valores de referencia del script (<c>ExtinguisherSpray.Default*</c>).
    ///
    /// ⚠️ Hace falta un tool porque los campos están <b>serializados en el prefab</b>: cambiar los
    /// defaults en el .cs no toca una instancia ya guardada, así que el chorro seguiría saliendo con
    /// la densidad vieja sin que nada falle ni avise — el mismo modo de fallo silencioso que el
    /// cableado de escena.
    ///
    /// También reporta (sin tocarlas) las instancias de la escena abierta que tengan override propio
    /// de esos campos: ahí el prefab ya no manda.
    ///
    /// Idempotente. Uso: menú <b>Extintores ▸ Tune Spray Density</b>.
    /// </summary>
    public static class SprayDensityTool
    {
        private const string NozzlePrefab = "Assets/Prefabs/NozzleRig.prefab";

        /// <summary>Campo serializado → valor de referencia.</summary>
        private static readonly (string prop, float value)[] Values =
        {
            ("maxRate",   ExtinguisherSpray.DefaultMaxRate),
            ("speed",     ExtinguisherSpray.DefaultSpeed),
            ("lifetime",  ExtinguisherSpray.DefaultLifetime),
            ("startSize", ExtinguisherSpray.DefaultStartSize),
            ("endSize",   ExtinguisherSpray.DefaultEndSize),
            ("coneAngle", ExtinguisherSpray.DefaultConeAngle),
        };

        [MenuItem("Extintores/Tune Spray Density", priority = 35)]
        public static void Tune()
        {
            var log = new List<string>();
            bool changed = ApplyToPrefab(NozzlePrefab, log);
            ReportSceneOverrides(log);

            var sb = new System.Text.StringBuilder(
                $"[SprayDensityTool] {(changed ? "prefab actualizado" : "sin cambios")}:\n");
            foreach (var l in log) sb.AppendLine("  · " + l);
            Debug.Log(sb.ToString());
        }

        private static bool ApplyToPrefab(string path, List<string> log)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                log.Add($"⚠ Falta '{path}'.");
                return false;
            }

            var contents = PrefabUtility.LoadPrefabContents(path);
            bool changed = false;

            try
            {
                var sprays = contents.GetComponentsInChildren<ExtinguisherSpray>(true);
                if (sprays.Length == 0)
                {
                    log.Add($"⚠ '{path}' no tiene ningún ExtinguisherSpray.");
                    return false;
                }

                foreach (var spray in sprays)
                {
                    var so = new SerializedObject(spray);

                    foreach (var (prop, value) in Values)
                    {
                        var p = so.FindProperty(prop);
                        if (p == null)
                        {
                            log.Add($"⚠ '{prop}' no existe en ExtinguisherSpray (¿se renombró?).");
                            continue;
                        }
                        if (!Mathf.Approximately(p.floatValue, value))
                        {
                            log.Add($"{spray.name}.{prop}: {p.floatValue:0.###} → {value:0.###}");
                            p.floatValue = value;
                            changed = true;
                        }
                    }

                    // El alpha compensa el overdraw: más partículas con el mismo alpha dejarían la
                    // nube opaca como leche y costarían el doble de fillrate.
                    var tint = so.FindProperty("tint");
                    if (tint != null && !Mathf.Approximately(tint.colorValue.a, ExtinguisherSpray.DefaultAlpha))
                    {
                        var c = tint.colorValue;
                        log.Add($"{spray.name}.tint.a: {c.a:0.###} → {ExtinguisherSpray.DefaultAlpha:0.###}");
                        c.a = ExtinguisherSpray.DefaultAlpha;
                        tint.colorValue = c;
                        changed = true;
                    }

                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                if (changed) PrefabUtility.SaveAsPrefabAsset(contents, path);
                else log.Add("El prefab ya tenía los valores de referencia.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            if (changed)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            return changed;
        }

        /// <summary>Avisa si alguna instancia de la escena abierta pisa estos campos con un override.</summary>
        private static void ReportSceneOverrides(List<string> log)
        {
            foreach (var spray in Object.FindObjectsByType<ExtinguisherSpray>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!PrefabUtility.IsPartOfPrefabInstance(spray)) continue;

                var so = new SerializedObject(spray);
                foreach (var (prop, _) in Values)
                {
                    var p = so.FindProperty(prop);
                    if (p != null && p.prefabOverride)
                        log.Add($"⚠ '{spray.name}' en la escena pisa '{prop}' con un override: " +
                                "ahí manda la escena, no el prefab.");
                }
            }
        }
    }
}
