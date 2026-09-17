using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Extintores.EditorTools
{
    /// <summary>
    /// Arma el PATIO INDUSTRIAL alrededor de los puestos con el pack <i>RPG/FPS Game Assets —
    /// Industrial Set v2.0</i>: piso de asfalto, muro perimetral con portón, props de fondo por zona y
    /// una silueta de hangares y tanques afuera del muro.
    ///
    /// Es un patio y no un galpón porque el pack es de exterior: sus hangares son cáscaras de 74 a
    /// 1.500 triángulos, sin interior. Parado adentro no se vería nada; desde afuera del muro
    /// funcionan perfecto como horizonte.
    ///
    /// Sólo genera escenografía. Los anchors de los puestos siguen viviendo en
    /// <c>Warehouse_Blockout</c> y no se tocan: <see cref="FireStation"/> los referencia por fileID.
    /// <c>Extintores ▸ Fix Scene Wiring</c> apaga la cáscara del blockout, instancia este prefab en la
    /// escena y pone el cielo; <c>Validate Setup</c> verifica que ningún prop tape un puesto.
    ///
    /// ⚠ <b>Correrlo sobre-escribe el asset.</b> Es seguro mientras nada de la escena apunte a objetos
    /// de adentro del patio (hoy nada lo hace). Si se ajusta un prop a mano, ajustarlo ACÁ.
    ///
    /// Uso: menú <b>Extintores ▸ Build Industrial Yard</b>. Idempotente.
    /// </summary>
    public static class YardBuilder
    {
        internal const string OutPrefab = "Assets/Prefabs/Yard_Industrial.prefab";
        internal const string GroundName = "Ground";

        private const string Pack = "Assets/RPG_FPS_game_assets_industrial";
        private const string MatFolder = "Assets/Extintores/Yard/Materials";
        private const string AsphaltSource = Pack + "/Textures/Asphalt/Seamless_asphalt_v1/Seamless_asphalt_v1.mat";

        // Mismas medidas que el blockout (WarehouseBuilder.HalfW/HalfD): el muro va sobre sus paredes.
        private const float HalfW = 12f;
        private const float HalfD = 8f;

        /// <summary>El piso sobra para los costados: sin esto se ve el borde del asfalto por encima del muro.</summary>
        private static readonly Vector2 GroundSize = new Vector2(40f, 32f);
        private const float AsphaltTileMeters = 4f;

        /// <summary>Por debajo de este tamaño un prop no proyecta sombra: en Quest no se nota y cuesta.</summary>
        private const float SmallProp = 3f;

        private const string Fence = Pack + "/Fences/Concrete_fences/Concrete_fence_v2/Concrete_fence_v2_S.prefab";
        private const string Gate = Pack + "/Fences/Concrete_fences/Concrete_fence_v2/Concrete_fence_v2_Gate.prefab";

        /// <summary>
        /// Un prop. <c>Center</c> es dónde queda el CENTRO de su bounds en XZ (y la base apoyada en el
        /// piso), no su pivote: los pivotes del pack no son confiables y así la tabla dice exactamente
        /// dónde termina cada cosa, que es lo que después valida SceneWiringFixer.
        /// </summary>
        private struct Placement
        {
            public string Group;
            public string Prefab;
            public Vector2 Center; // (x, z)
            public float Yaw;

            public Placement(string group, string prefab, float x, float z, float yaw = 0f)
            {
                Group = group; Prefab = $"{Pack}/{prefab}.prefab"; Center = new Vector2(x, z); Yaw = yaw;
            }
        }

        // Regla del layout: todo va DETRÁS del fuego (contra el muro), nunca entre el usuario y el
        // fuego, y a más de 1,8 m del FireAnchor. Las zonas A/C miran al oeste y B/K al este.
        private static readonly Placement[] Props =
        {
            // Zona A (−8, 3,5): sólidos → pallets y cajones.
            // Yaw 0 = lado LARGO sobre X (ver Place). Contra los muros este/oeste va a 90°.
            new Placement("Zone_A", "Other_props/Palets/Palet_v1/Palet_v1_set", -10.5f, 6.0f),
            new Placement("Zone_A", "Other_props/Palets/Bags_on_pallet_v1/Bags_on_pallet_v1_1", -10.6f, 1.2f, 90f),
            new Placement("Zone_A", "Boxes/Wooden_box_v1/Wooden_box_v1_LD1square", -8.2f, 6.8f),

            // Zona B (8, 3,5): auto → playa de carga. El container va a lo largo del muro norte:
            // parado contra el muro este le quedaba a ~1 m del paragolpes.
            new Placement("Zone_B", "Containers/Cargo_container_v1/Cargo_container_v1_LD1open", 8.5f, 6.7f),
            new Placement("Zone_B", "Fences/Road_blocks/Road_block_v1/Road_block_v1", 10.8f, 0.9f, 90f),

            // Zona C (−8, −3,5): eléctrico → rincón de máquinas.
            new Placement("Zone_C", "Other_props/Electric_box/Electric_box_v3/Electric_box_v3", -10.9f, -1.0f, 90f),
            new Placement("Zone_C", "Other_props/Conditioners/Conditioner_v1/Conditioner_v1", -11.2f, -4.8f, 90f),
            new Placement("Zone_C", "Other_props/Generators/Generator_v1/Generator_v1", -9.8f, -6.9f),

            // Zona K (8, −3,5): cocina → container comedor y basura.
            new Placement("Zone_K", "Containers/Cargo_container_v1/Cargo_container_v1_LD1close", 8.5f, -6.7f),
            new Placement("Zone_K", "Dumpsters/Dumpsters_v1/Dumpsters_v1_garbadge", 10.9f, -0.8f, 90f),
            new Placement("Zone_K", "Barrels/Barrel_v3/Barrel_v3_single", 11.2f, -4.4f),

            // Centro: separa los dos lados sin cruzarse con la línea lobby → menú (x = 0).
            new Placement("Center", "Barrels/Barrel_v2/Barrel_v2_quadro", -2.2f, 4.2f),
            new Placement("Center", "Other_props/Palets/Palet_v1/Palet_v1_single", 2.2f, 3.0f),

            // Horizonte, afuera del muro: acá no importa que los hangares no tengan interior.
            new Placement("Skyline", "Buildings/Industrial/Hangars/Hangar_v4/Hangar_v4", -7f, 19f, 180f),
            new Placement("Skyline", "Buildings/Industrial/Hangars/Hangar_v2/Hangar_v2_basic", 10f, 20f, 180f),
            new Placement("Skyline", "Oil_tanks/Oil_tank_v1/Oil_tank_v1", -22f, -3f),
            new Placement("Skyline", "Oil_tanks/Oil_tank_v2/Oil_tank_v2", 18f, -4.5f),
            new Placement("Skyline", "Oil_tanks/Oil_tank_v2/Oil_tank_v2", 18f, -8f),
            new Placement("Skyline", "Other_props/Pipes/Industrial_pipes/Industrial_pipe_v1/Industrial_pipe_v1", 21f, -6.2f),
        };

        [MenuItem("Extintores/Build Industrial Yard", priority = 31)]
        public static void Build()
        {
            EnsureFolder(MatFolder);
            EnsureFolder("Assets/Prefabs");

            var root = new GameObject("Yard_Industrial");
            var missing = new List<string>();
            try
            {
                BuildGround(root.transform);
                BuildWalls(root.transform, missing);

                var groups = new Dictionary<string, Transform>();
                foreach (var p in Props)
                {
                    if (!groups.TryGetValue(p.Group, out var g))
                    {
                        g = new GameObject(p.Group).transform;
                        g.SetParent(root.transform, false);
                        groups[p.Group] = g;
                    }
                    Place(g, p.Prefab, p.Center, p.Yaw, missing);
                }

                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    t.gameObject.isStatic = true; // batching en Quest

                CountGeometry(root, out int renderers, out int tris);
                PrefabUtility.SaveAsPrefabAsset(root, OutPrefab);

                string warn = missing.Count > 0 ? $" ⚠ No encontré: {string.Join(", ", missing)}." : "";
                string msg = $"[YardBuilder] {OutPrefab}: {renderers} renderers, {tris:N0} triángulos " +
                             "(techo orientativo en Quest: ~100 / ~50k)." + warn +
                             " Ahora corré 'Extintores ▸ Fix Scene Wiring', guardá y 'Validate Setup'.";
                if (missing.Count > 0) Debug.LogWarning(msg); else Debug.Log(msg);
            }
            finally
            {
                Object.DestroyImmediate(root);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        // ---------------------------------------------------------------- piso y muro

        private static void BuildGround(Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Plane); // 10 × 10 m
            go.name = GroundName;
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(GroundSize.x / 10f, 1f, GroundSize.y / 10f);

            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = GetAsphalt();
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>
        /// Copia del asfalto del pack con el tiling del patio. El .mat del pack no se toca: cambiarle el
        /// tiling alteraría cualquier otro uso y ensuciaría el diff del pack.
        /// </summary>
        private static Material GetAsphalt()
        {
            string path = $"{MatFolder}/M_Yard_Asphalt.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                var src = AssetDatabase.LoadAssetAtPath<Material>(AsphaltSource);
                mat = src != null ? new Material(src) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }

            var tiling = GroundSize / AsphaltTileMeters;
            if (mat.HasProperty("_BaseMap")) mat.SetTextureScale("_BaseMap", tiling);
            if (mat.HasProperty("_MainTex")) mat.SetTextureScale("_MainTex", tiling);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// Muro perimetral en segmentos, con el largo medido del propio prefab. Deja un hueco en el
        /// medio del muro sur (detrás del lobby) para el portón.
        /// </summary>
        private static void BuildWalls(Transform parent, List<string> missing)
        {
            var walls = new GameObject("Walls").transform;
            walls.SetParent(parent, false);

            if (!TryMeasureLength(Fence, out float seg)) { missing.Add(Path.GetFileNameWithoutExtension(Fence)); return; }
            TryMeasureLength(Gate, out float gateLen);

            WallRun(walls, "North", -HalfW, HalfW, seg, x => new Vector2(x, HalfD), 0f, missing);
            WallRun(walls, "East", -HalfD, HalfD, seg, z => new Vector2(HalfW, z), 90f, missing);
            WallRun(walls, "West", -HalfD, HalfD, seg, z => new Vector2(-HalfW, z), 90f, missing);

            // Muro sur: dos tramos que terminan justo en el portón. Sin portón, muro corrido.
            if (gateLen > 0f)
            {
                WallRun(walls, "SouthW", -HalfW, -gateLen / 2f, seg, x => new Vector2(x, -HalfD), 0f, missing);
                WallRun(walls, "SouthE", gateLen / 2f, HalfW, seg, x => new Vector2(x, -HalfD), 0f, missing);
                Place(walls, Gate, new Vector2(0f, -HalfD), 0f, missing, "Gate");
            }
            else
            {
                missing.Add(Path.GetFileNameWithoutExtension(Gate));
                WallRun(walls, "South", -HalfW, HalfW, seg, x => new Vector2(x, -HalfD), 0f, missing);
            }
        }

        /// <summary>
        /// Cubre [from, to] con segmentos del muro. Se reparte el tramo entre n segmentos enteros, así
        /// que solapan apenas en vez de dejar un hueco en la esquina.
        /// </summary>
        private static void WallRun(Transform parent, string side, float from, float to, float seg,
                                    System.Func<float, Vector2> at, float yaw, List<string> missing)
        {
            if (to - from < 0.1f) return;
            int n = Mathf.Max(1, Mathf.CeilToInt((to - from) / seg));
            float step = (to - from) / n;
            for (int i = 0; i < n; i++)
                Place(parent, Fence, at(from + step * (i + 0.5f)), yaw, missing, $"Fence_{side}_{i}");
        }

        // ---------------------------------------------------------------- colocación

        /// <summary>
        /// Instancia el prefab, lo gira para que su lado LARGO quede sobre el eje X local cuando
        /// <paramref name="yaw"/> es 0 (los muros del pack no vienen todos orientados igual), y lo corre
        /// para que su bounds quede centrado en <paramref name="center"/> y apoyado en y = 0.
        /// </summary>
        private static GameObject Place(Transform parent, string prefabPath, Vector2 center, float yaw,
                                        List<string> missing, string name = null)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (asset == null) { missing.Add(Path.GetFileNameWithoutExtension(prefabPath)); return null; }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            if (name != null) go.name = name;
            var t = go.transform;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;

            if (!TryBounds(go, out var b)) return go;
            float align = b.size.z > b.size.x ? 90f : 0f;
            t.localRotation = Quaternion.Euler(0f, yaw + align, 0f);

            TryBounds(go, out b);
            var target = parent.TransformPoint(new Vector3(center.x, 0f, center.y));
            t.position += new Vector3(target.x - b.center.x, target.y - b.min.y, target.z - b.center.z);

            // Las sombras de los props chicos no se notan y en Quest cuestan un pase entero.
            if (b.size.x < SmallProp && b.size.y < SmallProp && b.size.z < SmallProp)
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    r.shadowCastingMode = ShadowCastingMode.Off;

            return go;
        }

        private static bool TryMeasureLength(string prefabPath, out float length)
        {
            length = 0f;
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (asset == null) return false;

            var probe = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            try
            {
                if (!TryBounds(probe, out var b)) return false;
                length = Mathf.Max(b.size.x, b.size.z);
                return length > 0.1f;
            }
            finally
            {
                Object.DestroyImmediate(probe);
            }
        }

        internal static bool TryBounds(GameObject go, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return any;
        }

        private static void CountGeometry(GameObject root, out int renderers, out int tris)
        {
            renderers = 0; tris = 0;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                renderers++;
                for (int s = 0; s < mf.sharedMesh.subMeshCount; s++)
                    tris += (int)mf.sharedMesh.GetIndexCount(s) / 3;
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
