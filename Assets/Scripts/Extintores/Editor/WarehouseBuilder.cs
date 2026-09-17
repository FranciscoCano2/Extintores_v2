using System.IO;
using UnityEditor;
using UnityEngine;

namespace Extintores.EditorTools
{
    /// <summary>
    /// Genera el BLOCKOUT del galpón: piso, paredes, columnas, estanterías y —lo importante— los
    /// <b>anchors</b> de cada zona (dónde va el fuego, dónde se para el usuario, dónde va el panel).
    ///
    /// Todo con primitivas y materiales URP generados: cero assets externos, cero MB, y corre barato
    /// en Quest. El objetivo NO es que se vea lindo, sino <b>fijar las medidas y el flujo hoy</b>:
    /// como <see cref="FireStation"/> solo conoce Transforms, después se puede tirar toda esta
    /// geometría y poner un pack de assets encima sin tocar una línea de lógica.
    ///
    /// Uso: menú <b>Extintores ▸ Build Warehouse Blockout</b>. Idempotente (sobre-escribe).
    /// </summary>
    public static class WarehouseBuilder
    {
        private const string MatFolder = "Assets/Extintores/Warehouse/Materials";
        private const string OutPrefab = "Assets/Prefabs/Warehouse_Blockout.prefab";

        // Medidas del galpón (m). X = ancho, Z = profundidad, Y = altura. YardBuilder usa las mismas.
        private const float HalfW = 12f;
        private const float HalfD = 8f;
        private const float WallH = 6f;
        private const float WallT = 0.3f;

        /// <summary>
        /// Distancia por defecto a la que se para el usuario respecto del fuego.
        ///
        /// ⚠ <b>Tiene un techo duro:</b> el chorro llega hasta <c>speed × lifetime</c> = 6 × 0,6 =
        /// <b>3,6 m</b> (ver <see cref="ExtinguisherSpray"/>). Pasado eso las partículas mueren antes
        /// de tocar el fuego y el puesto se vuelve imposible, sin ningún error que lo explique. Si
        /// hace falta alejar más al usuario, hay que subir también el alcance del chorro.
        /// </summary>
        private const float ApproachDistance = 2.2f;

        /// <summary>Altura del pivote del panel de extintores (queda a la altura de la mano).</summary>
        private const float PanelHeight = 0.85f;

        /// <summary>Separación lateral del panel respecto del usuario.</summary>
        private const float PanelSide = 0.75f;

        /// <summary>
        /// Cáscara (piso, paredes, techo, columnas) y estanterías de primitivas. Apagado desde que el
        /// escenario es el patio del pack (<see cref="YardBuilder"/>): con techo y paredes el patio
        /// quedaría encerrado. <c>readonly</c> y no <c>const</c> para no generar código inalcanzable.
        /// </summary>
        private static readonly bool IncludeBlockoutGeometry = false;

        private struct Zone
        {
            public FireClass Class;
            public Vector3 Fire;      // dónde se planta el fuego
            public float ApproachYaw; // hacia dónde mira el usuario al enfrentarlo
            public float Approach;    // 0 = usar ApproachDistance
        }

        private static readonly Zone[] Zones =
        {
            new Zone { Class = FireClass.A, Fire = new Vector3(-8f, 0f,  3.5f), ApproachYaw = 270f },
            // Clase B: la causa es un AUTO, ~4,5 m de largo. A 2,2 m del anchor del fuego el usuario
            // aparecía prácticamente encima del paragolpes. 3,0 m lo deja afuera del auto y todavía
            // dentro del alcance del chorro (3,6 m) — ver ApproachDistance.
            new Zone { Class = FireClass.B, Fire = new Vector3( 8f, 0f,  3.5f), ApproachYaw =  90f, Approach = 3.0f },
            new Zone { Class = FireClass.C, Fire = new Vector3(-8f, 0f, -3.5f), ApproachYaw = 270f },
            new Zone { Class = FireClass.K, Fire = new Vector3( 8f, 0f, -3.5f), ApproachYaw =  90f },
        };

        /// <summary>
        /// Distancia de aproximación de una zona por nombre (<c>Zone_B</c>). La consulta
        /// <c>SceneWiringFixer</c> para reacomodar los anchors del prefab ya generado sin tener que
        /// rehacer el galpón entero.
        /// </summary>
        internal static bool TryGetApproach(string zoneName, out float distance)
        {
            foreach (var z in Zones)
            {
                if ($"Zone_{z.Class}" != zoneName) continue;
                distance = z.Approach > 0f ? z.Approach : ApproachDistance;
                return true;
            }
            distance = ApproachDistance;
            return false;
        }

        [MenuItem("Extintores/Build Warehouse Blockout", priority = 30)]
        public static void Build()
        {
            EnsureFolder(MatFolder);
            EnsureFolder("Assets/Prefabs");

            var root = new GameObject("Warehouse_Blockout");

            // La escenografía la pone ahora el patio del pack industrial (YardBuilder). Este prefab
            // queda sólo con los anchors y los Pad de cada zona. Se deja la opción para volver a ver
            // el blockout sin el pack.
            if (IncludeBlockoutGeometry)
            {
                BuildShell(root.transform);
                BuildDressing(root.transform);
            }

            foreach (var z in Zones) BuildZone(root.transform, z);
            BuildLobby(root.transform);

            PrefabUtility.SaveAsPrefabAsset(root, OutPrefab);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[WarehouseBuilder] {OutPrefab} — galpón de {HalfW * 2}×{HalfD * 2} m con " +
                      $"{Zones.Length} zonas + lobby. Es un blockout a propósito: fija medidas y " +
                      "flujo, y se reemplaza por assets sin tocar la lógica. " +
                      "Ahora corré 'Extintores ▸ Build Training Scene'.");
        }

        // ---------------------------------------------------------------- estructura

        private static void BuildShell(Transform parent)
        {
            var shell = new GameObject("Shell").transform;
            shell.SetParent(parent, false);

            var floor = GetMat("Floor", new Color(0.34f, 0.34f, 0.36f));
            var wall = GetMat("Wall", new Color(0.62f, 0.61f, 0.58f));
            var trim = GetMat("Trim", new Color(0.42f, 0.45f, 0.50f));

            Box(shell, "Floor", new Vector3(0f, -WallT / 2f, 0f),
                new Vector3(HalfW * 2f, WallT, HalfD * 2f), floor);

            // Techo: cierra el volumen para que se lea "interior" y no un descampado.
            Box(shell, "Ceiling", new Vector3(0f, WallH, 0f),
                new Vector3(HalfW * 2f, WallT, HalfD * 2f), trim);

            Box(shell, "Wall_North", new Vector3(0f, WallH / 2f, HalfD),
                new Vector3(HalfW * 2f, WallH, WallT), wall);
            Box(shell, "Wall_South", new Vector3(0f, WallH / 2f, -HalfD),
                new Vector3(HalfW * 2f, WallH, WallT), wall);
            Box(shell, "Wall_East", new Vector3(HalfW, WallH / 2f, 0f),
                new Vector3(WallT, WallH, HalfD * 2f), wall);
            Box(shell, "Wall_West", new Vector3(-HalfW, WallH / 2f, 0f),
                new Vector3(WallT, WallH, HalfD * 2f), wall);

            // Columnas: dan escala y sensación de nave industrial.
            for (int i = -1; i <= 1; i++)
                foreach (float z in new[] { -HalfD + 2.5f, HalfD - 2.5f })
                    Box(shell, $"Column_{i}_{(z < 0 ? "S" : "N")}",
                        new Vector3(i * 5.5f, WallH / 2f, z),
                        new Vector3(0.45f, WallH, 0.45f), trim);
        }

        /// <summary>Estanterías y pilas: sirven para juzgar distancias y tapar el vacío.</summary>
        private static void BuildDressing(Transform parent)
        {
            var props = new GameObject("Dressing").transform;
            props.SetParent(parent, false);

            var rack = GetMat("Rack", new Color(0.55f, 0.35f, 0.18f));
            var crate = GetMat("Crate", new Color(0.48f, 0.40f, 0.28f));

            // Estanterías contra las paredes largas, fuera de las zonas de fuego.
            for (int i = 0; i < 3; i++)
            {
                float x = -3.5f + i * 3.5f;
                Shelf(props, $"Shelf_N{i}", new Vector3(x, 0f, HalfD - 1.2f), rack);
                Shelf(props, $"Shelf_S{i}", new Vector3(x, 0f, -HalfD + 1.2f), rack);
            }

            // Pilas de cajones sueltas.
            Box(props, "Crates_1", new Vector3(-2.2f, 0.4f, 0f), new Vector3(0.8f, 0.8f, 0.8f), crate);
            Box(props, "Crates_2", new Vector3(-2.2f, 1.15f, 0.15f), new Vector3(0.7f, 0.7f, 0.7f), crate);
            Box(props, "Crates_3", new Vector3(2.4f, 0.4f, 1.1f), new Vector3(0.9f, 0.8f, 0.9f), crate);
        }

        private static void Shelf(Transform parent, string name, Vector3 basePos, Material mat)
        {
            var shelf = new GameObject(name).transform;
            shelf.SetParent(parent, false);
            shelf.localPosition = basePos;

            for (int level = 0; level < 3; level++)
                Box(shelf, $"Level_{level}", new Vector3(0f, 0.6f + level * 0.9f, 0f),
                    new Vector3(2.6f, 0.08f, 0.9f), mat);

            foreach (float x in new[] { -1.25f, 1.25f })
                Box(shelf, $"Post_{(x < 0 ? "L" : "R")}", new Vector3(x, 1.3f, 0f),
                    new Vector3(0.1f, 2.6f, 0.9f), mat);
        }

        // ---------------------------------------------------------------- zonas

        /// <summary>
        /// Una zona = anchors puros. <c>FireAnchor</c> es donde el builder de escena instancia el
        /// prefab del fuego; <c>PlayerSpawn</c> y <c>PanelAnchor</c> los consume
        /// <see cref="FireStation"/>.
        /// </summary>
        /// <summary>
        /// Orientación del panel de extintores: <b>el yaw del usuario + 90°</b>.
        ///
        /// El panel va al COSTADO (a la derecha, <see cref="PanelSide"/>) y su cara legible es el −Z.
        /// Orientarlo igual que el usuario —como estaba— apunta ese −Z hacia la espalda de quien lo
        /// usa: había que darse vuelta entero para leerlo. Con +90° el −Z mira hacia el usuario, o
        /// sea que basta con girar la cabeza a la derecha.
        /// </summary>
        private static Quaternion PanelRotation(float approachYaw) =>
            Quaternion.Euler(0f, approachYaw + 90f, 0f);

        private static void BuildZone(Transform parent, Zone z)
        {
            var zone = new GameObject($"Zone_{z.Class}").transform;
            zone.SetParent(parent, false);

            var look = Quaternion.Euler(0f, z.ApproachYaw, 0f);
            Vector3 forward = look * Vector3.forward;
            Vector3 right = look * Vector3.right;

            Anchor(zone, "FireAnchor", z.Fire, Quaternion.Euler(0f, z.ApproachYaw + 180f, 0f));

            TryGetApproach($"Zone_{z.Class}", out float approach);
            Vector3 spawn = z.Fire - forward * approach;
            Anchor(zone, "PlayerSpawn", spawn, look);

            Anchor(zone, "PanelAnchor",
                spawn + right * PanelSide + Vector3.up * PanelHeight,
                PanelRotation(z.ApproachYaw));

            // Marca visual en el piso para ver el puesto desde lejos.
            var pad = GetMat($"Pad_{z.Class}", ClassColor(z.Class));
            Box(zone, "Pad", z.Fire + Vector3.up * 0.01f, new Vector3(3.2f, 0.02f, 3.2f), pad);
        }

        private static void BuildLobby(Transform parent)
        {
            var lobby = new GameObject("Lobby").transform;
            lobby.SetParent(parent, false);

            // Mirando a +Z, con el menú al frente.
            var look = Quaternion.identity;
            Vector3 spawn = new Vector3(0f, 0f, -5.5f);

            // El menú está AL FRENTE, así que se orienta igual que el usuario (su cara legible −Z
            // le queda de cara). El panel está AL COSTADO y por eso va a 90° — ver PanelRotation.
            Anchor(lobby, "PlayerSpawn", spawn, look);
            Anchor(lobby, "MenuAnchor", spawn + new Vector3(0f, 1.45f, 1.8f), look);
            Anchor(lobby, "PanelAnchor", spawn + new Vector3(PanelSide, PanelHeight, 0f),
                   PanelRotation(look.eulerAngles.y));

            var pad = GetMat("Pad_Lobby", new Color(0.25f, 0.45f, 0.70f));
            Box(lobby, "Pad", spawn + Vector3.up * 0.01f, new Vector3(2.4f, 0.02f, 2.4f), pad);
        }

        private static Color ClassColor(FireClass c) => c switch
        {
            FireClass.A => new Color(0.20f, 0.50f, 0.25f),
            FireClass.B => new Color(0.65f, 0.45f, 0.15f),
            FireClass.C => new Color(0.20f, 0.35f, 0.65f),
            _ => new Color(0.55f, 0.25f, 0.45f),
        };

        // ---------------------------------------------------------------- helpers

        private static Transform Anchor(Transform parent, string name, Vector3 pos, Quaternion rot)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetLocalPositionAndRotation(pos, rot);
            return go.transform;
        }

        private static void Box(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.isStatic = true; // permite batching/lightmaps en Quest
        }

        private static Material GetMat(string id, Color color)
        {
            string path = $"{MatFolder}/M_Wh_{id}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) { SetColor(existing, color); return existing; }

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            SetColor(mat, color);
            mat.SetFloat("_Smoothness", 0.1f);
            mat.enableInstancing = true; // el blockout repite mucho el mismo material
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
