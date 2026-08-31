using System.IO;
using UnityEditor;
using UnityEngine;

namespace Extintores.EditorTools
{
    /// <summary>
    /// Construye los prefabs de fuego por CLASE (A/B/C/K):
    /// <code>
    /// Fire_X (raíz vacía)
    ///  ├─ Flame  = VFX del pack "Free Fire VFX URP" + FireSource(clase X)   ← solo esto mengua
    ///  └─ Cause  = modelo PBR de la causa (Assets/ThirdParty/FireCauses/...) ← decoración
    /// </code>
    /// FireSource va en <b>Flame</b> (no en la raíz) para que al apagarse escale/atenúe solo la
    /// llama y NO el objeto de la causa (el auto y la hornalla no deben encogerse).
    ///
    /// No escribe YAML a mano: instancia VFX y FBX como prefabs anidados y guarda el resultado en
    /// <c>Assets/Prefabs/Fires/Fire_X.prefab</c>. Idempotente (re-ejecutar sobre-escribe).
    ///
    /// Uso: menú <b>Extintores ▸ Build Fire Prefabs (A B C K)</b>. Tras generarlos, en cada prefab
    /// posicioná la llama sobre la causa (capó del auto, hornallas, etc.) y ajustá la escala del FBX.
    /// </summary>
    public static class FirePrefabBuilder
    {
        private const string OutputFolder = "Assets/Prefabs/Fires";
        private const string VfxFolder = "Assets/Vefects/Free Fire VFX URP/Particles";
        private const string CausesFolder = "Assets/ThirdParty/FireCauses";

        private struct ClassDef
        {
            public FireClass Class;
            public string VfxPrefab;   // .prefab dentro de VfxFolder
            public string CauseFbx;    // ruta relativa a CausesFolder (o null)
            public Color TintHint;     // tinte orientativo (multiplica startColor si es color constante)
        }

        private static readonly ClassDef[] Defs =
        {
            new ClassDef { Class = FireClass.A, VfxPrefab = "VFX_Fire_01_Medium.prefab",      CauseFbx = "Cardboard_Box/tripo_convert_9112ddbf-ab82-4c6a-a198-91410117ae59.fbx", TintHint = new Color(1.00f, 0.85f, 0.55f) },
            new ClassDef { Class = FireClass.B, VfxPrefab = "VFX_Fire_01_Medium.prefab",       CauseFbx = "Sports_Car/sportcar.017.fbx",                                        TintHint = new Color(1.00f, 0.70f, 0.35f) },
            new ClassDef { Class = FireClass.C, VfxPrefab = "VFX_Fire_01_Small.prefab",        CauseFbx = "Electrical_Breaker_Panel/Tz-ExteriorElectricBox2.fbx",               TintHint = new Color(0.75f, 0.85f, 1.00f) },
            new ClassDef { Class = FireClass.K, VfxPrefab = "VFX_Fire_01_Medium_Smoke.prefab", CauseFbx = "Gas_Stove/gas_stove_main.fbx",                                       TintHint = new Color(1.00f, 0.60f, 0.30f) },
        };

        [MenuItem("Extintores/Build Fire Prefabs (A B C K)")]
        public static void BuildAll()
        {
            EnsureFolder(OutputFolder);
            int ok = 0;
            foreach (var def in Defs)
                if (BuildOne(def)) ok++;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[FirePrefabBuilder] Listo: {ok}/{Defs.Length} prefabs en {OutputFolder}. " +
                      "En cada uno, posicioná 'Flame' sobre 'Cause' y ajustá la escala del FBX.");
        }

        private static bool BuildOne(ClassDef def)
        {
            string vfxPath = $"{VfxFolder}/{def.VfxPrefab}";
            var vfx = AssetDatabase.LoadAssetAtPath<GameObject>(vfxPath);
            if (vfx == null)
            {
                Debug.LogWarning($"[FirePrefabBuilder] Falta el VFX '{vfxPath}'. Se saltea {def.Class}.");
                return false;
            }

            // Raíz vacía.
            var root = new GameObject($"Fire_{def.Class}");

            // Hijo Flame = VFX + FireSource.
            var flame = (GameObject)PrefabUtility.InstantiatePrefab(vfx);
            flame.name = "Flame";
            flame.transform.SetParent(root.transform, false);

            var fs = flame.GetComponent<FireSource>();
            if (fs == null) fs = flame.AddComponent<FireSource>();
            var so = new SerializedObject(fs);
            var prop = so.FindProperty("fireClass");
            if (prop != null) prop.enumValueIndex = (int)def.Class;
            so.ApplyModifiedPropertiesWithoutUndo();

            ApplyTint(flame, def.TintHint);

            // Badge de clase (billboard con la letra) — textura según la clase.
            var badge = flame.GetComponent<FireClassBadge>();
            if (badge == null) badge = flame.AddComponent<FireClassBadge>();
            var badgeTex = AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/Extintores/Badges/Badge_{def.Class}.png");
            if (badgeTex != null)
            {
                var bso = new SerializedObject(badge);
                var bp = bso.FindProperty("badge");
                if (bp != null) bp.objectReferenceValue = badgeTex;
                bso.ApplyModifiedPropertiesWithoutUndo();
            }

            // Hijo Cause = FBX de la causa (si existe).
            if (!string.IsNullOrEmpty(def.CauseFbx))
            {
                string causePath = $"{CausesFolder}/{def.CauseFbx}";
                var causeAsset = AssetDatabase.LoadAssetAtPath<GameObject>(causePath);
                if (causeAsset != null)
                {
                    var cause = (GameObject)PrefabUtility.InstantiatePrefab(causeAsset);
                    cause.name = "Cause";
                    cause.transform.SetParent(root.transform, false);
                }
                else
                {
                    Debug.LogWarning($"[FirePrefabBuilder] {def.Class}: no se encontró la causa " +
                                     $"'{causePath}' (¿importada?). Se crea el fuego sin causa.");
                }
            }

            string outPath = $"{OutputFolder}/Fire_{def.Class}.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, outPath);
            Object.DestroyImmediate(root);

            Debug.Log($"[FirePrefabBuilder] {outPath}  (clase {def.Class}, VFX {def.VfxPrefab})");
            return true;
        }

        private static void ApplyTint(GameObject go, Color tint)
        {
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                var sc = main.startColor;
                if (sc.mode == ParticleSystemGradientMode.Color)
                {
                    sc.color = sc.color * tint;
                    main.startColor = sc;
                }
            }
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
