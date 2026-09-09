using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Extintores.EditorTools
{
    /// <summary>
    /// Arma el ejercicio completo <b>sobre la escena abierta</b>: galpón, los 4 puestos, el menú del
    /// lobby y el botón "Continuar", y cablea <see cref="TrainingSession"/> con todo.
    ///
    /// Trabaja sobre la escena actual —en vez de crear una vacía— para <b>conservar la calibración
    /// del rig</b>: los offsets de <see cref="ExtinguisherMount"/> del tanque y de la boquilla se
    /// ajustaron a mano en el visor y rehacerlos sería tirar ese trabajo. Después de correrlo,
    /// guardá con <b>File ▸ Save As…</b> como <c>Assets/Scenes/Training.unity</c>.
    ///
    /// Idempotente: vuelve a correrlo y reemplaza lo que había generado antes.
    /// Uso: menú <b>Extintores ▸ Build Training Scene</b>.
    /// </summary>
    public static class TrainingSceneBuilder
    {
        private const string WarehousePrefab = "Assets/Prefabs/Warehouse_Blockout.prefab";
        private const string StationPrefab = "Assets/Prefabs/ExtinguisherStation.prefab";
        private const string FiresFolder = "Assets/Prefabs/Fires";
        private const string GeneratedRoot = "TrainingSetup";

        [MenuItem("Extintores/Build Training Scene", priority = 31)]
        public static void Build()
        {
            var warehouseAsset = AssetDatabase.LoadAssetAtPath<GameObject>(WarehousePrefab);
            if (warehouseAsset == null)
            {
                Debug.LogError($"[TrainingSceneBuilder] Falta '{WarehousePrefab}'. " +
                               "Corré primero 'Extintores ▸ Build Warehouse Blockout'.");
                return;
            }

            var spray = Object.FindFirstObjectByType<ExtinguisherSpray>(FindObjectsInactive.Include);
            if (spray == null)
            {
                Debug.LogError("[TrainingSceneBuilder] La escena abierta no tiene ExtinguisherSpray. " +
                               "Abrí la escena con los rigs calibrados (SampleScene) y volvé a correrlo.");
                return;
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Extintores: Build Training Scene");
            int group = Undo.GetCurrentGroup();

            var log = new List<string>();

            CleanPreviousRun(log);
            FixTrackingOrigin(log);

            var root = new GameObject(GeneratedRoot);
            Undo.RegisterCreatedObjectUndo(root, "TrainingSetup");

            var warehouse = (GameObject)PrefabUtility.InstantiatePrefab(warehouseAsset);
            warehouse.transform.SetParent(root.transform, false);
            log.Add($"Galpón instanciado desde {WarehousePrefab}.");

            var stations = BuildStations(root.transform, warehouse.transform, log);
            var panel = EnsurePanel(root.transform, log);
            var continueButton = BuildContinueButton(root.transform, log);
            var menu = BuildMenu(root.transform, warehouse.transform, log);

            WireManagers(root.transform, warehouse.transform, stations, panel, continueButton, menu, spray, log);

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Undo.CollapseUndoOperations(group);

            var sb = new System.Text.StringBuilder("[TrainingSceneBuilder] Listo:\n");
            foreach (var l in log) sb.AppendLine("  · " + l);
            sb.AppendLine();
            sb.Append("GUARDÁ CON File ▸ Save As… como 'Assets/Scenes/Training.unity' y agregala a " +
                      "Build Settings como primera escena. Después probá con Build and Run.");
            Debug.Log(sb.ToString());
        }

        // ---------------------------------------------------------------- limpieza

        /// <summary>
        /// Borra lo que generó una corrida anterior y los fuegos sueltos de la escena vieja: ahora
        /// los fuegos viven dentro de su puesto, no tirados en el mundo.
        /// </summary>
        private static void CleanPreviousRun(List<string> log)
        {
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (go.name == GeneratedRoot)
                {
                    Undo.DestroyObjectImmediate(go);
                    log.Add("Se reemplazó el TrainingSetup anterior.");
                }
                else if (go.name.StartsWith("Fire_"))
                {
                    Undo.DestroyObjectImmediate(go);
                    log.Add($"Fuego suelto '{go.name}' quitado de la raíz de la escena.");
                }
            }
        }

        /// <summary>
        /// EyeLevel deja los ojos del usuario al ras del piso y todo el escenario por encima de la
        /// mirada — era la causa real del "estoy lejos/abajo de la plataforma".
        /// </summary>
        private static void FixTrackingOrigin(List<string> log)
        {
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (mb == null || mb.GetType().Name != "OVRCameraRig") continue;

                var so = new SerializedObject(mb);
                var p = so.FindProperty("_trackingOriginType");
                if (p != null && p.enumValueIndex != 1)
                {
                    p.enumValueIndex = 1; // FloorLevel
                    so.ApplyModifiedProperties();
                    log.Add("OVRCameraRig: tracking origin EyeLevel → FloorLevel.");
                }
                return;
            }
            log.Add("⚠ No encontré el OVRCameraRig para fijar el tracking origin.");
        }

        // ---------------------------------------------------------------- puestos

        private static List<FireStation> BuildStations(Transform root, Transform warehouse, List<string> log)
        {
            var result = new List<FireStation>();

            foreach (FireClass fc in System.Enum.GetValues(typeof(FireClass)))
            {
                var zone = warehouse.Find($"Zone_{fc}");
                if (zone == null) { log.Add($"⚠ El galpón no tiene 'Zone_{fc}'."); continue; }

                var firePrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{FiresFolder}/Fire_{fc}.prefab");
                if (firePrefab == null) { log.Add($"⚠ Falta {FiresFolder}/Fire_{fc}.prefab."); continue; }

                var stationGo = new GameObject($"Station_{fc}");
                stationGo.transform.SetParent(root, false);

                var fireAnchor = zone.Find("FireAnchor");
                var fire = (GameObject)PrefabUtility.InstantiatePrefab(firePrefab);
                fire.transform.SetParent(stationGo.transform, false);
                if (fireAnchor != null)
                    fire.transform.SetPositionAndRotation(fireAnchor.position, fireAnchor.rotation);

                var station = stationGo.AddComponent<FireStation>();
                var so = new SerializedObject(station);
                SetRef(so, "fire", fire.GetComponentInChildren<FireSource>(true));
                SetRef(so, "fireRoot", fire); // el prefab entero: llama + causa + badge
                SetRef(so, "playerSpawn", zone.Find("PlayerSpawn"));
                SetRef(so, "panelAnchor", zone.Find("PanelAnchor"));
                so.ApplyModifiedPropertiesWithoutUndo();

                result.Add(station);
                log.Add($"Puesto clase {fc} armado en Zone_{fc}.");
            }

            return result;
        }

        /// <summary>Deja UNA sola ExtinguisherStation en la escena y la cuelga del setup.</summary>
        private static ExtinguisherStation EnsurePanel(Transform root, List<string> log)
        {
            var existing = Object.FindObjectsByType<ExtinguisherStation>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 1; i < existing.Length; i++)
            {
                log.Add($"Panel duplicado '{existing[i].name}' quitado.");
                Undo.DestroyObjectImmediate(existing[i].gameObject);
            }

            if (existing.Length > 0)
            {
                existing[0].transform.SetParent(root, true);
                log.Add("Se reutilizó el panel de extintores que ya estaba en la escena.");
                return existing[0];
            }

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(StationPrefab);
            if (asset == null) { log.Add($"⚠ Falta {StationPrefab}."); return null; }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            go.transform.SetParent(root, false);
            log.Add("Panel de extintores instanciado.");
            return go.GetComponent<ExtinguisherStation>();
        }

        // ---------------------------------------------------------------- botones

        private static SprayButton BuildContinueButton(Transform root, List<string> log)
        {
            var b = NewButton(root, "Button_Continue", "CONTINUAR", new Vector2(0.5f, 0.22f));
            b.gameObject.SetActive(false); // lo enciende TrainingSession al apagarse el fuego
            log.Add("Botón 'Continuar' creado (arranca oculto).");
            return b;
        }

        private static MainMenu BuildMenu(Transform root, Transform warehouse, List<string> log)
        {
            var menuGo = new GameObject("Menu");
            menuGo.transform.SetParent(root, false);

            var anchor = warehouse.Find("Lobby/MenuAnchor");
            if (anchor != null)
                menuGo.transform.SetPositionAndRotation(anchor.position, anchor.rotation);
            else
                log.Add("⚠ El galpón no tiene 'Lobby/MenuAnchor'; el menú queda en el origen.");

            // Panel principal: las tres opciones, apiladas en vertical.
            var main = new GameObject("MainPanel");
            main.transform.SetParent(menuGo.transform, false);

            var size = new Vector2(0.62f, 0.2f);
            var tutorial = NewButton(main.transform, "Button_Tutorial", "TUTORIAL", size, new Vector3(0f, 0.26f, 0f));
            var random = NewButton(main.transform, "Button_Random", "ALEATORIO", size, Vector3.zero);
            var custom = NewButton(main.transform, "Button_Custom", "PERSONALIZADO", size, new Vector3(0f, -0.26f, 0f));

            // Submenú: una clase por botón, en fila.
            var customPanel = new GameObject("CustomPanel");
            customPanel.transform.SetParent(menuGo.transform, false);

            var classButtons = new SprayButton[4];
            var classSize = new Vector2(0.24f, 0.24f);
            int i = 0;
            foreach (FireClass fc in System.Enum.GetValues(typeof(FireClass)))
            {
                // Se dibuja de izquierda a derecha DESDE EL PUNTO DE VISTA DEL USUARIO: como la cara
                // legible del botón es su −Z, el eje X local corre al revés respecto de quien mira.
                float x = -(i - 1.5f) * 0.28f;
                classButtons[i] = NewButton(customPanel.transform, $"Button_Class_{fc}", fc.ToString(),
                                            classSize, new Vector3(x, 0.16f, 0f));
                i++;
            }
            var back = NewButton(customPanel.transform, "Button_Back", "VOLVER",
                                 new Vector2(0.4f, 0.16f), new Vector3(0f, -0.16f, 0f));

            customPanel.SetActive(false);

            var menu = menuGo.AddComponent<MainMenu>();
            var so = new SerializedObject(menu);
            SetRef(so, "mainPanel", main);
            SetRef(so, "customPanel", customPanel);
            SetRef(so, "tutorialButton", tutorial);
            SetRef(so, "randomButton", random);
            SetRef(so, "customButton", custom);
            SetRef(so, "backButton", back);
            var arr = so.FindProperty("classButtons");
            if (arr != null)
            {
                arr.arraySize = classButtons.Length;
                for (int k = 0; k < classButtons.Length; k++)
                    arr.GetArrayElementAtIndex(k).objectReferenceValue = classButtons[k];
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            log.Add("Menú del lobby creado: 3 opciones + submenú de 4 clases.");
            return menu;
        }

        private static SprayButton NewButton(Transform parent, string name, string text,
                                             Vector2 size, Vector3 localPos = default)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;

            var b = go.AddComponent<SprayButton>();
            var so = new SerializedObject(b);
            var t = so.FindProperty("text"); if (t != null) t.stringValue = text;
            var s = so.FindProperty("size"); if (s != null) s.vector2Value = size;
            so.ApplyModifiedPropertiesWithoutUndo();

            // El collider lo agrega SprayButton en Awake, pero en el editor conviene que ya exista
            // con la medida correcta para poder verlo y acomodarlo antes de darle Play.
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(size.x, size.y, 0.06f);
            int layer = LayerMask.NameToLayer(SprayButton.LayerName);
            if (layer >= 0) go.layer = layer;

            return b;
        }

        // ---------------------------------------------------------------- managers

        private static void WireManagers(Transform root, Transform warehouse, List<FireStation> stations,
                                         ExtinguisherStation panel, SprayButton continueButton,
                                         MainMenu menu, ExtinguisherSpray spray, List<string> log)
        {
            var fireManager = Object.FindFirstObjectByType<FireManager>(FindObjectsInactive.Include);
            if (fireManager == null)
            {
                var go = new GameObject("FireManager");
                go.transform.SetParent(root, false);
                fireManager = go.AddComponent<FireManager>();
                log.Add("FireManager creado.");
            }

            // Ahora quien decide qué fuegos cuentan es TrainingSession, según el modo.
            var fmSo = new SerializedObject(fireManager);
            var auto = fmSo.FindProperty("autoCollectOnStart");
            if (auto != null && auto.boolValue)
            {
                auto.boolValue = false;
                fmSo.ApplyModifiedPropertiesWithoutUndo();
                log.Add("FireManager.autoCollectOnStart apagado (lo maneja TrainingSession).");
            }

            var sessionGo = new GameObject("TrainingSession");
            sessionGo.transform.SetParent(root, false);
            var session = sessionGo.AddComponent<TrainingSession>();

            var so = new SerializedObject(session);
            var arr = so.FindProperty("stations");
            if (arr != null)
            {
                arr.arraySize = stations.Count;
                for (int i = 0; i < stations.Count; i++)
                    arr.GetArrayElementAtIndex(i).objectReferenceValue = stations[i];
            }
            SetRef(so, "fireManager", fireManager);
            SetRef(so, "panel", panel);
            SetRef(so, "continueButton", continueButton);
            SetRef(so, "lobbySpawn", warehouse.Find("Lobby/PlayerSpawn"));
            SetRef(so, "lobbyPanelAnchor", warehouse.Find("Lobby/PanelAnchor"));

            var rig = FindOvrRig();
            if (rig != null) SetRef(so, "playerRig", rig);
            var cam = Camera.main;
            if (cam != null) SetRef(so, "centerEye", cam.transform);
            so.ApplyModifiedPropertiesWithoutUndo();

            if (menu != null)
            {
                var mSo = new SerializedObject(menu);
                SetRef(mSo, "session", session);
                mSo.ApplyModifiedPropertiesWithoutUndo();
            }

            if (continueButton != null)
            {
                UnityEditor.Events.UnityEventTools.AddPersistentListener(
                    continueButton.onPressed, session.Continue);
                log.Add("Botón 'Continuar' cableado a TrainingSession.Continue().");
            }

            // TrainingManager/TrainingHud siguen sirviendo tal cual: leen del FireManager.
            var tm = Object.FindFirstObjectByType<TrainingManager>(FindObjectsInactive.Include);
            if (tm == null) log.Add("⚠ No hay TrainingManager: corré 'Extintores ▸ Fix Scene Wiring'.");

            log.Add($"TrainingSession cableada con {stations.Count} puesto(s).");
        }

        private static Transform FindOvrRig()
        {
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (mb != null && mb.GetType().Name == "OVRCameraRig") return mb.transform;
            return null;
        }

        private static void SetRef(SerializedObject so, string field, Object value)
        {
            var p = so.FindProperty(field);
            if (p != null) p.objectReferenceValue = value;
        }
    }
}
