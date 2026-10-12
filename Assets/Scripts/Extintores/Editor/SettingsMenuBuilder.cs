using UnityEditor;
using UnityEngine;

namespace Extintores.EditorTools
{
    /// <summary>
    /// Arma el submenú Configuración del lobby (<see cref="SettingsMenu"/>) y el botón que lo abre, y
    /// los cablea en <see cref="MainMenu"/>. Todo con <see cref="SerializedObject"/>: las referencias
    /// quedan en la escena, a la vista en el inspector y chequeadas por <c>Validate Setup</c>.
    ///
    /// Disposición (espacio local del menú; cara legible −Z hacia el usuario, +X a su derecha):
    /// <code>
    ///  General                        Extintor                     Boquilla                      Panel
    ///        CONFIGURACIÓN              AJUSTAR EXTINTOR             AJUSTAR BOQUILLA             PANEL DE EXTINTORES
    ///  [−]   VOLUMEN 100 %   [+]    [−] ALTURA DEL MANDO [+]    [−] AVANCE (Z, eje) [+]    (aparece a tu derecha)
    ///      [ VIBRACIÓN: SÍ ]         [−] DESPLAZAMIENTO X[+]    [−] DESPLAZAMIENTO X[+]    [−]   LATERAL   [+]
    ///    [ AJUSTAR EXTINTOR ]        [−] DESPLAZAMIENTO Z[+]    [−] DESPLAZAMIENTO Y[+]    [−]   ALTURA    [+]
    ///    [ AJUSTAR BOQUILLA ]        [−]   ROTACIÓN X    [+]    [−]   ROTACIÓN X    [+]    [−]  DISTANCIA  [+]
    ///   [ POSICIÓN DEL PANEL ]       [−] ROTACIÓN Y (eje)[+]    [−]   ROTACIÓN Y    [+]    [ RESTABLECER ] [ LISTO ]
    ///  [ RESTABLECER ] [ VOLVER ]    [−]   ROTACIÓN Z    [+]    [−] ROTACIÓN Z (eje)[+]
    ///                                [ RESTABLECER ] [ LISTO ]  [ RESTABLECER ] [ LISTO ]
    /// </code>
    ///
    /// El <c>SettingsPanel</c> es 100 % generado: si le falta algo (p. ej. una versión anterior sin
    /// la página del panel) se borra y se rehace entero, en vez de parchear pieza por pieza.
    ///
    /// Lo usan <see cref="TrainingSceneBuilder"/> y <see cref="SceneWiringFixer"/>.
    /// </summary>
    internal static class SettingsMenuBuilder
    {
        internal const string PanelName = "SettingsPanel";
        internal const string OpenButtonName = "Button_Settings";

        private static readonly Vector2 OpenSize = new Vector2(0.5f, 0.16f);
        private static readonly Vector3 OpenPos = new Vector3(0f, -0.5f, 0f); // debajo de PERSONALIZADO

        private static readonly Vector2 StepSize = new Vector2(0.14f, 0.12f);
        private static readonly Vector2 WideSize = new Vector2(0.62f, 0.11f);
        private static readonly Vector2 HalfSize = new Vector2(0.3f, 0.12f);
        private const float StepX = 0.36f;

        private static bool _undo;

        /// <summary>
        /// Deja el botón de entrada y el submenú completos y cableados. Devuelve cuántas piezas creó
        /// o rehizo (0 = ya estaba todo).
        /// </summary>
        internal static int Ensure(MainMenu menu, bool undo)
        {
            if (menu == null) return 0;
            _undo = undo;
            int changes = 0;

            var menuSo = new SerializedObject(menu);

            // --- botón de entrada, en el panel principal
            if (menu.SettingsButton == null && menu.MainPanel != null)
            {
                var existing = menu.MainPanel.transform.Find(OpenButtonName);
                var open = existing != null ? existing.GetComponent<SprayButton>()
                                            : NewButton(menu.MainPanel.transform, OpenButtonName, "CONFIGURACIÓN", OpenSize, OpenPos);
                SetRef(menuSo, "settingsButton", open);
                changes++;
            }

            // --- submenú: completo o se rehace
            if (!PanelComplete(menu, out _))
            {
                var old = menu.SettingsPanel != null ? menu.SettingsPanel.transform : menu.transform.Find(PanelName);
                if (old != null)
                {
                    if (_undo) Undo.DestroyObjectImmediate(old.gameObject);
                    else Object.DestroyImmediate(old.gameObject);
                }

                BuildPanel(menu.transform, out var panel, out var back);
                SetRef(menuSo, "settingsPanel", panel);
                SetRef(menuSo, "settingsBackButton", back);
                changes++;
            }

            Apply(menuSo);
            return changes;
        }

        /// <summary>False si falta algo que <see cref="Ensure"/> crearía.</summary>
        internal static bool IsComplete(MainMenu menu, out string missing)
        {
            if (menu.SettingsButton == null) { missing = "MainMenu.settingsButton sin asignar"; return false; }
            return PanelComplete(menu, out missing);
        }

        private static bool PanelComplete(MainMenu menu, out string missing)
        {
            missing = null;
            if (menu.SettingsPanel == null) { missing = "MainMenu.settingsPanel sin asignar"; return false; }
            if (menu.SettingsBackButton == null) { missing = "MainMenu.settingsBackButton sin asignar"; return false; }

            var s = menu.SettingsPanel.GetComponent<SettingsMenu>();
            if (s == null) { missing = $"'{PanelName}' no tiene SettingsMenu"; return false; }
            foreach (var r in s.RequiredRefs)
                if (r == null) { missing = "SettingsMenu desactualizado o con referencias sin asignar"; return false; }
            return true;
        }

        // ------------------------------------------------------------------ armado

        private static void BuildPanel(Transform menu, out GameObject panel, out SprayButton back)
        {
            panel = new GameObject(PanelName);
            if (_undo) Undo.RegisterCreatedObjectUndo(panel, "Crear menú de configuración");
            panel.transform.SetParent(menu, false);

            var settings = panel.AddComponent<SettingsMenu>();
            var so = new SerializedObject(settings);

            // ---- página general
            var general = NewGroup(panel.transform, "GeneralPage");
            SetRef(so, "generalPage", general);

            // Botones anchos cada 13 cm (miden 11): entran los tres accesos sin pasar el alto del menú.
            NewText(general.transform, "Title", "CONFIGURACIÓN", new Vector3(0f, 0.46f, 0f), 0.013f);
            Row(general.transform, "Volume", 0.31f, so, "volumeDown", "volumeUp", "volumeLabel", "VOLUMEN\n100 %");
            SetRef(so, "vibrationToggle",
                   NewButton(general.transform, "Button_Vibration", "VIBRACIÓN: SÍ", WideSize, new Vector3(0f, 0.17f, 0f)));
            SetRef(so, "extinguisherPageButton",
                   NewButton(general.transform, "Button_ExtinguisherSetup", "AJUSTAR EXTINTOR", WideSize, new Vector3(0f, 0.04f, 0f)));
            SetRef(so, "nozzlePageButton",
                   NewButton(general.transform, "Button_NozzleSetup", "AJUSTAR BOQUILLA", WideSize, new Vector3(0f, -0.09f, 0f)));
            SetRef(so, "panelPageButton",
                   NewButton(general.transform, "Button_PanelPosition", "POSICIÓN DEL PANEL", WideSize, new Vector3(0f, -0.22f, 0f)));
            SetRef(so, "resetButton",
                   NewButton(general.transform, "Button_Reset", "RESTABLECER", HalfSize, new Vector3(-0.17f, -0.39f, 0f)));
            back = NewButton(general.transform, "Button_SettingsBack", "VOLVER", HalfSize, new Vector3(0.17f, -0.39f, 0f));

            // ---- página del extintor (calibración del tanque)
            // 6 filas: más apretadas que en las otras páginas (paso 12 cm, botones de 10 cm).
            var t = NewPage(panel.transform, "ExtinguisherPage", so, "extinguisherPage", "AJUSTAR EXTINTOR");
            Row(t, "Tank", RowY(0), so, "tankDown", "tankUp", "tankLabel", "ALTURA DEL MANDO (Y)\n0 cm", compact: true);
            Row(t, "ShiftX", RowY(1), so, "shiftXDown", "shiftXUp", "shiftXLabel", "DESPLAZAMIENTO X\n0 cm", compact: true);
            Row(t, "ShiftZ", RowY(2), so, "shiftZDown", "shiftZUp", "shiftZLabel", "DESPLAZAMIENTO Z\n0 cm", compact: true);
            Row(t, "RotX", RowY(3), so, "rotXDown", "rotXUp", "rotXLabel", "ROTACIÓN X\n0°", compact: true);
            Row(t, "RotY", RowY(4), so, "rotYDown", "rotYUp", "rotYLabel", "ROTACIÓN Y (eje)\n0°", compact: true);
            Row(t, "RotZ", RowY(5), so, "rotZDown", "rotZUp", "rotZLabel", "ROTACIÓN Z\n0°", compact: true);
            PageFooter(t, "Extinguisher", so, "extinguisherReset", "extinguisherDone");

            // ---- página de la boquilla: los mismos ajustes, con su eje en Z (hacia donde sale el chorro)
            t = NewPage(panel.transform, "NozzlePage", so, "nozzlePage", "AJUSTAR BOQUILLA");
            Row(t, "Nozzle", RowY(0), so, "nozzleDown", "nozzleUp", "nozzleLabel", "AVANCE (Z, eje)\n0 cm", compact: true);
            Row(t, "NozzleShiftX", RowY(1), so, "nozzleShiftXDown", "nozzleShiftXUp", "nozzleShiftXLabel", "DESPLAZAMIENTO X\n0 cm", compact: true);
            Row(t, "NozzleShiftY", RowY(2), so, "nozzleShiftYDown", "nozzleShiftYUp", "nozzleShiftYLabel", "DESPLAZAMIENTO Y\n0 cm", compact: true);
            Row(t, "NozzleRotX", RowY(3), so, "nozzleRotXDown", "nozzleRotXUp", "nozzleRotXLabel", "ROTACIÓN X\n0°", compact: true);
            Row(t, "NozzleRotY", RowY(4), so, "nozzleRotYDown", "nozzleRotYUp", "nozzleRotYLabel", "ROTACIÓN Y\n0°", compact: true);
            Row(t, "NozzleRotZ", RowY(5), so, "nozzleRotZDown", "nozzleRotZUp", "nozzleRotZLabel", "ROTACIÓN Z (eje)\n0°", compact: true);
            PageFooter(t, "Nozzle", so, "nozzleReset", "nozzleDone");

            // ---- página del panel de extintores
            var panelPage = NewGroup(panel.transform, "PanelPage");
            SetRef(so, "panelPage", panelPage);

            // ~2 cm por carácter a este tamaño: renglones cortos para no pasar el ancho del menú (0,86 m).
            NewText(panelPage.transform, "Title", "PANEL DE EXTINTORES", new Vector3(0f, 0.44f, 0f), 0.011f);
            NewText(panelPage.transform, "Hint", "Aparece a tu derecha,\ncomo en cada puesto",
                    new Vector3(0f, 0.31f, 0f), 0.006f);
            Row(panelPage.transform, "PanelLateral", 0.13f, so, "panelLeft", "panelRight", "panelLateralLabel", "LATERAL\nde fábrica");
            Row(panelPage.transform, "PanelHeight", -0.02f, so, "panelDown", "panelUp", "panelHeightLabel", "ALTURA\nde fábrica");
            Row(panelPage.transform, "PanelDistance", -0.17f, so, "panelNearer", "panelFarther", "panelDistanceLabel", "DISTANCIA\nde fábrica");
            SetRef(so, "panelReset",
                   NewButton(panelPage.transform, "Button_PanelReset", "RESTABLECER", HalfSize, new Vector3(-0.17f, -0.36f, 0f)));
            SetRef(so, "panelDone",
                   NewButton(panelPage.transform, "Button_PanelDone", "LISTO", HalfSize, new Vector3(0.17f, -0.36f, 0f)));
            panelPage.SetActive(false);

            Apply(so);
            panel.SetActive(false); // lo abre MainMenu
        }

        /// <summary>Altura de la fila <paramref name="i"/> en las páginas de calibración (6 filas de a 12 cm).</summary>
        private static float RowY(int i) => 0.34f - 0.12f * i;

        /// <summary>Página de calibración (oculta) con su título; devuelve dónde colgar las filas.</summary>
        private static Transform NewPage(Transform panel, string name, SerializedObject so, string field, string title)
        {
            var page = NewGroup(panel, name);
            SetRef(so, field, page);
            NewText(page.transform, "Title", title, new Vector3(0f, 0.48f, 0f), 0.011f);
            page.SetActive(false);
            return page.transform;
        }

        /// <summary>"[ RESTABLECER ] [ LISTO ]" al pie de una página de calibración.</summary>
        private static void PageFooter(Transform page, string name, SerializedObject so, string resetField, string doneField)
        {
            SetRef(so, resetField, NewButton(page, $"Button_{name}Reset", "RESTABLECER", HalfSize, new Vector3(-0.17f, -0.44f, 0f)));
            SetRef(so, doneField, NewButton(page, $"Button_{name}Done", "LISTO", HalfSize, new Vector3(0.17f, -0.44f, 0f)));
        }

        private static GameObject NewGroup(Transform parent, string name)
        {
            var go = new GameObject(name);
            if (_undo) Undo.RegisterCreatedObjectUndo(go, "Crear página");
            go.transform.SetParent(parent, false);
            return go;
        }

        /// <summary>Fila "[−] rótulo [+]".</summary>
        private static void Row(Transform parent, string name, float y, SerializedObject so,
                                string downField, string upField, string labelField, string initial,
                                bool compact = false)
        {
            var size = compact ? new Vector2(StepSize.x, 0.10f) : StepSize;
            var down = NewButton(parent, $"Button_{name}Down", "−", size, new Vector3(-StepX, y, 0f));
            var up = NewButton(parent, $"Button_{name}Up", "+", size, new Vector3(StepX, y, 0f));
            var label = NewText(parent, $"{name}Label", initial, new Vector3(0f, y, 0f), compact ? 0.0065f : 0.0075f);
            SetRef(so, downField, down);
            SetRef(so, upField, up);
            SetRef(so, labelField, label);
        }

        private static SprayButton NewButton(Transform parent, string name, string text, Vector2 size, Vector3 localPos)
        {
            var go = new GameObject(name);
            if (_undo) Undo.RegisterCreatedObjectUndo(go, "Crear botón");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;

            var b = go.AddComponent<SprayButton>();
            var so = new SerializedObject(b);
            var t = so.FindProperty("text"); if (t != null) t.stringValue = text;
            var s = so.FindProperty("size"); if (s != null) s.vector2Value = size;
            so.ApplyModifiedPropertiesWithoutUndo();

            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(size.x, size.y, 0.06f);
            int layer = LayerMask.NameToLayer(SprayButton.LayerName);
            if (layer >= 0) go.layer = layer;
            return b;
        }

        /// <summary>Rótulo suelto (no se acciona). ≈ <c>60 × characterSize × 0,1</c> m por renglón.</summary>
        private static TextMesh NewText(Transform parent, string name, string text, Vector3 localPos, float characterSize)
        {
            var go = new GameObject(name);
            if (_undo) Undo.RegisterCreatedObjectUndo(go, "Crear rótulo");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;

            var tm = go.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null)
            {
                tm.font = font;
                go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }
            tm.text = text;
            tm.fontSize = 60;
            tm.characterSize = characterSize;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.white;
            return tm;
        }

        private static void SetRef(SerializedObject so, string field, Object value)
        {
            var p = so.FindProperty(field);
            if (p == null)
            {
                // FindProperty devuelve null sin quejarse: que quede dicho (ver PLAN §7).
                Debug.LogWarning($"[SettingsMenuBuilder] {so.targetObject.GetType().Name} no tiene el campo '{field}'.");
                return;
            }
            p.objectReferenceValue = value;
        }

        private static void Apply(SerializedObject so)
        {
            if (_undo) so.ApplyModifiedProperties();
            else so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
