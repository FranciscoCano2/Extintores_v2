using UnityEngine;

namespace Extintores
{
    /// <summary>
    /// Submenú "Configuración" del lobby. Lee y escribe <see cref="GameSettings"/>, que persiste
    /// solo; este componente sólo traduce botones a valores. Cuatro páginas:
    /// <list type="bullet">
    ///   <item><b>General</b>: volumen, vibración y los accesos a las otras tres.</item>
    ///   <item><b>Extintor</b>: calibración del tanque — altura de la abrazadera sobre el tanque
    ///         (su eje Y), desplazamiento transversal X/Z y rotación en X/Y/Z alrededor de la
    ///         abrazadera. Se ve al instante en el extintor que el usuario tiene en la mano.</item>
    ///   <item><b>Boquilla</b>: lo mismo para la boquilla sobre el mando izquierdo — corrimiento
    ///         sobre su eje (Z, hacia donde sale el chorro), desplazamiento X/Y y rotación X/Y/Z.</item>
    ///   <item><b>Panel</b>: lateral / altura / distancia del panel de extintores. Mientras está
    ///         abierta, <see cref="TrainingSession.PreviewPanel"/> lo muestra en el lobby.</item>
    /// </list>
    ///
    /// Lo arma <c>Extintores ▸ Fix Scene Wiring</c> (<c>SettingsMenuBuilder</c>). El botón de
    /// entrada y el "Volver" de la página general los maneja <see cref="MainMenu"/>.
    /// </summary>
    public class SettingsMenu : MonoBehaviour
    {
        private enum Page { General, Extinguisher, Nozzle, Panel }

        [Header("Páginas")]
        [SerializeField] private GameObject generalPage;
        [SerializeField] private GameObject extinguisherPage;
        [SerializeField] private GameObject nozzlePage;
        [SerializeField] private GameObject panelPage;

        [Header("General")]
        [SerializeField] private SprayButton volumeDown;
        [SerializeField] private SprayButton volumeUp;
        [SerializeField] private TextMesh volumeLabel;
        [SerializeField] private SprayButton vibrationToggle;
        [SerializeField] private SprayButton extinguisherPageButton;
        [SerializeField] private SprayButton nozzlePageButton;
        [SerializeField] private SprayButton panelPageButton;
        [SerializeField] private SprayButton resetButton;

        [Header("Extintor")]
        [SerializeField] private SprayButton tankDown;
        [SerializeField] private SprayButton tankUp;
        [SerializeField] private TextMesh tankLabel;
        [SerializeField] private SprayButton shiftXDown;
        [SerializeField] private SprayButton shiftXUp;
        [SerializeField] private TextMesh shiftXLabel;
        [SerializeField] private SprayButton shiftZDown;
        [SerializeField] private SprayButton shiftZUp;
        [SerializeField] private TextMesh shiftZLabel;
        [SerializeField] private SprayButton rotXDown;
        [SerializeField] private SprayButton rotXUp;
        [SerializeField] private TextMesh rotXLabel;
        [SerializeField] private SprayButton rotYDown;
        [SerializeField] private SprayButton rotYUp;
        [SerializeField] private TextMesh rotYLabel;
        [SerializeField] private SprayButton rotZDown;
        [SerializeField] private SprayButton rotZUp;
        [SerializeField] private TextMesh rotZLabel;
        [SerializeField] private SprayButton extinguisherReset;
        [SerializeField] private SprayButton extinguisherDone;

        [Header("Boquilla")]
        [SerializeField] private SprayButton nozzleDown;
        [SerializeField] private SprayButton nozzleUp;
        [SerializeField] private TextMesh nozzleLabel;
        [SerializeField] private SprayButton nozzleShiftXDown;
        [SerializeField] private SprayButton nozzleShiftXUp;
        [SerializeField] private TextMesh nozzleShiftXLabel;
        [SerializeField] private SprayButton nozzleShiftYDown;
        [SerializeField] private SprayButton nozzleShiftYUp;
        [SerializeField] private TextMesh nozzleShiftYLabel;
        [SerializeField] private SprayButton nozzleRotXDown;
        [SerializeField] private SprayButton nozzleRotXUp;
        [SerializeField] private TextMesh nozzleRotXLabel;
        [SerializeField] private SprayButton nozzleRotYDown;
        [SerializeField] private SprayButton nozzleRotYUp;
        [SerializeField] private TextMesh nozzleRotYLabel;
        [SerializeField] private SprayButton nozzleRotZDown;
        [SerializeField] private SprayButton nozzleRotZUp;
        [SerializeField] private TextMesh nozzleRotZLabel;
        [SerializeField] private SprayButton nozzleReset;
        [SerializeField] private SprayButton nozzleDone;

        [Header("Panel de extintores")]
        [SerializeField] private SprayButton panelLeft;
        [SerializeField] private SprayButton panelRight;
        [SerializeField] private TextMesh panelLateralLabel;
        [SerializeField] private SprayButton panelDown;
        [SerializeField] private SprayButton panelUp;
        [SerializeField] private TextMesh panelHeightLabel;
        [SerializeField] private SprayButton panelNearer;
        [SerializeField] private SprayButton panelFarther;
        [SerializeField] private TextMesh panelDistanceLabel;
        [SerializeField] private SprayButton panelReset;
        [SerializeField] private SprayButton panelDone;

        /// <summary>
        /// Todas las referencias que tienen que estar cableadas. La usa <c>Validate Setup</c>: un
        /// campo nuevo que no se agregue acá quedaría sin chequear, y el panel viejo no se rehace.
        /// </summary>
        public Object[] RequiredRefs => new Object[]
        {
            generalPage, extinguisherPage, nozzlePage, panelPage,
            volumeDown, volumeUp, volumeLabel, vibrationToggle,
            extinguisherPageButton, nozzlePageButton, panelPageButton, resetButton,
            tankDown, tankUp, tankLabel,
            shiftXDown, shiftXUp, shiftXLabel, shiftZDown, shiftZUp, shiftZLabel,
            rotXDown, rotXUp, rotXLabel, rotYDown, rotYUp, rotYLabel, rotZDown, rotZUp, rotZLabel,
            extinguisherReset, extinguisherDone,
            nozzleDown, nozzleUp, nozzleLabel,
            nozzleShiftXDown, nozzleShiftXUp, nozzleShiftXLabel, nozzleShiftYDown, nozzleShiftYUp, nozzleShiftYLabel,
            nozzleRotXDown, nozzleRotXUp, nozzleRotXLabel, nozzleRotYDown, nozzleRotYUp, nozzleRotYLabel,
            nozzleRotZDown, nozzleRotZUp, nozzleRotZLabel, nozzleReset, nozzleDone,
            panelLeft, panelRight, panelLateralLabel, panelDown, panelUp, panelHeightLabel,
            panelNearer, panelFarther, panelDistanceLabel, panelReset, panelDone,
        };

        private TrainingSession _session;

        private void Awake()
        {
            _session = FindFirstObjectByType<TrainingSession>();

            // General
            Bind(volumeDown, () => GameSettings.Volume -= GameSettings.VolumeStep);
            Bind(volumeUp, () => GameSettings.Volume += GameSettings.VolumeStep);
            Bind(vibrationToggle, ToggleVibration);
            Bind(extinguisherPageButton, () => ShowPage(Page.Extinguisher));
            Bind(nozzlePageButton, () => ShowPage(Page.Nozzle));
            Bind(panelPageButton, () => ShowPage(Page.Panel));
            Bind(resetButton, GameSettings.ResetToDefaults);

            // Extintor
            const float cm = GameSettings.LengthStep, deg = GameSettings.RotationStep;
            Bind(tankDown, () => GameSettings.TankControllerHeight -= cm);
            Bind(tankUp, () => GameSettings.TankControllerHeight += cm);
            Bind(shiftXDown, () => GameSettings.TankShiftX -= cm);
            Bind(shiftXUp, () => GameSettings.TankShiftX += cm);
            Bind(shiftZDown, () => GameSettings.TankShiftZ -= cm);
            Bind(shiftZUp, () => GameSettings.TankShiftZ += cm);
            Bind(rotXDown, () => GameSettings.TankRotationX -= deg);
            Bind(rotXUp, () => GameSettings.TankRotationX += deg);
            Bind(rotYDown, () => GameSettings.TankRotationY -= deg);
            Bind(rotYUp, () => GameSettings.TankRotationY += deg);
            Bind(rotZDown, () => GameSettings.TankRotationZ -= deg);
            Bind(rotZUp, () => GameSettings.TankRotationZ += deg);
            Bind(extinguisherReset, GameSettings.ResetExtinguisher);
            Bind(extinguisherDone, () => ShowPage(Page.General));

            // Boquilla
            Bind(nozzleDown, () => GameSettings.NozzleOffset -= cm);
            Bind(nozzleUp, () => GameSettings.NozzleOffset += cm);
            Bind(nozzleShiftXDown, () => GameSettings.NozzleShiftX -= cm);
            Bind(nozzleShiftXUp, () => GameSettings.NozzleShiftX += cm);
            Bind(nozzleShiftYDown, () => GameSettings.NozzleShiftY -= cm);
            Bind(nozzleShiftYUp, () => GameSettings.NozzleShiftY += cm);
            Bind(nozzleRotXDown, () => GameSettings.NozzleRotationX -= deg);
            Bind(nozzleRotXUp, () => GameSettings.NozzleRotationX += deg);
            Bind(nozzleRotYDown, () => GameSettings.NozzleRotationY -= deg);
            Bind(nozzleRotYUp, () => GameSettings.NozzleRotationY += deg);
            Bind(nozzleRotZDown, () => GameSettings.NozzleRotationZ -= deg);
            Bind(nozzleRotZUp, () => GameSettings.NozzleRotationZ += deg);
            Bind(nozzleReset, GameSettings.ResetNozzle);
            Bind(nozzleDone, () => ShowPage(Page.General));

            // Panel
            float step = GameSettings.PanelStep;
            Bind(panelLeft, () => GameSettings.PanelLateral -= step);
            Bind(panelRight, () => GameSettings.PanelLateral += step);
            Bind(panelDown, () => GameSettings.PanelHeight -= step);
            Bind(panelUp, () => GameSettings.PanelHeight += step);
            Bind(panelNearer, () => GameSettings.PanelDistance -= step);
            Bind(panelFarther, () => GameSettings.PanelDistance += step);
            Bind(panelReset, GameSettings.ResetPanel);
            Bind(panelDone, () => ShowPage(Page.General));
        }

        private void OnEnable()
        {
            GameSettings.Changed += Refresh;
            ShowPage(Page.General); // siempre se entra por la página general
            Refresh();
        }

        private void OnDisable()
        {
            GameSettings.Changed -= Refresh;
            if (_session != null) _session.PreviewPanel(false);
        }

        private void ShowPage(Page page)
        {
            if (generalPage != null) generalPage.SetActive(page == Page.General);
            if (extinguisherPage != null) extinguisherPage.SetActive(page == Page.Extinguisher);
            if (nozzlePage != null) nozzlePage.SetActive(page == Page.Nozzle);
            if (panelPage != null) panelPage.SetActive(page == Page.Panel);
            if (_session != null) _session.PreviewPanel(page == Page.Panel);
        }

        private static void Bind(SprayButton button, System.Action action)
        {
            if (button != null) button.Pressed += _ => action();
        }

        private static void ToggleVibration()
        {
            GameSettings.Vibration = !GameSettings.Vibration;
            // Confirmación táctil en los DOS mandos al prenderla: es la forma de saber que "SÍ" anda.
            Haptics.Pulse(0.8f, 0.25f, OVRInput.Controller.RTouch);
            Haptics.Pulse(0.8f, 0.25f, OVRInput.Controller.LTouch);
        }

        private void Refresh()
        {
            SetText(volumeLabel, $"VOLUMEN\n{Mathf.RoundToInt(GameSettings.Volume * 100f)} %");
            if (vibrationToggle != null)
                vibrationToggle.Text = GameSettings.Vibration ? "VIBRACIÓN: SÍ" : "VIBRACIÓN: NO";

            Vector3 r = GameSettings.TankRotation;
            Vector3 s = GameSettings.TankShift;
            SetText(tankLabel, $"ALTURA DEL MANDO (Y)\n{Signed(GameSettings.TankControllerHeight * 100f, "cm")}");
            SetText(shiftXLabel, $"DESPLAZAMIENTO X\n{Signed(s.x * 100f, "cm")}");
            SetText(shiftZLabel, $"DESPLAZAMIENTO Z\n{Signed(s.z * 100f, "cm")}");
            SetText(rotXLabel, $"ROTACIÓN X\n{Signed(r.x, "°")}");
            SetText(rotYLabel, $"ROTACIÓN Y (eje)\n{Signed(r.y, "°")}");
            SetText(rotZLabel, $"ROTACIÓN Z\n{Signed(r.z, "°")}");

            Vector3 nr = GameSettings.NozzleRotation;
            Vector3 ns = GameSettings.NozzleShift;
            SetText(nozzleLabel, $"AVANCE (Z, eje)\n{Signed(GameSettings.NozzleOffset * 100f, "cm")}");
            SetText(nozzleShiftXLabel, $"DESPLAZAMIENTO X\n{Signed(ns.x * 100f, "cm")}");
            SetText(nozzleShiftYLabel, $"DESPLAZAMIENTO Y\n{Signed(ns.y * 100f, "cm")}");
            SetText(nozzleRotXLabel, $"ROTACIÓN X\n{Signed(nr.x, "°")}");
            SetText(nozzleRotYLabel, $"ROTACIÓN Y\n{Signed(nr.y, "°")}");
            SetText(nozzleRotZLabel, $"ROTACIÓN Z (eje)\n{Signed(nr.z, "°")}");

            Vector3 p = GameSettings.PanelOffset;
            SetText(panelLateralLabel, $"LATERAL\n{Describe(p.x, "a la derecha", "a la izquierda")}");
            SetText(panelHeightLabel, $"ALTURA\n{Describe(p.y, "más arriba", "más abajo")}");
            SetText(panelDistanceLabel, $"DISTANCIA\n{Describe(p.z, "más lejos", "más cerca")}");

            Limits(volumeDown, volumeUp, GameSettings.Volume, 0f, 1f);
            Limits(tankDown, tankUp, GameSettings.TankControllerHeight, GameSettings.HeightMin, GameSettings.HeightMax);
            Limits(shiftXDown, shiftXUp, s.x, -GameSettings.ShiftMax, GameSettings.ShiftMax);
            Limits(shiftZDown, shiftZUp, s.z, -GameSettings.ShiftMax, GameSettings.ShiftMax);
            Limits(rotXDown, rotXUp, r.x, -GameSettings.RotationMax, GameSettings.RotationMax);
            Limits(rotYDown, rotYUp, r.y, -GameSettings.RotationMax, GameSettings.RotationMax);
            Limits(rotZDown, rotZUp, r.z, -GameSettings.RotationMax, GameSettings.RotationMax);
            Limits(nozzleDown, nozzleUp, GameSettings.NozzleOffset, GameSettings.NozzleMin, GameSettings.NozzleMax);
            Limits(nozzleShiftXDown, nozzleShiftXUp, ns.x, -GameSettings.ShiftMax, GameSettings.ShiftMax);
            Limits(nozzleShiftYDown, nozzleShiftYUp, ns.y, -GameSettings.ShiftMax, GameSettings.ShiftMax);
            Limits(nozzleRotXDown, nozzleRotXUp, nr.x, -GameSettings.RotationMax, GameSettings.RotationMax);
            Limits(nozzleRotYDown, nozzleRotYUp, nr.y, -GameSettings.RotationMax, GameSettings.RotationMax);
            Limits(nozzleRotZDown, nozzleRotZUp, nr.z, -GameSettings.RotationMax, GameSettings.RotationMax);
            Limits(panelLeft, panelRight, p.x, -GameSettings.PanelLateralMax, GameSettings.PanelLateralMax);
            Limits(panelDown, panelUp, p.y, -GameSettings.PanelHeightMax, GameSettings.PanelHeightMax);
            Limits(panelNearer, panelFarther, p.z, -GameSettings.PanelDistanceMax, GameSettings.PanelDistanceMax);
        }

        /// <summary>Grisa el "−" en el mínimo y el "+" en el máximo.</summary>
        private static void Limits(SprayButton down, SprayButton up, float value, float min, float max)
        {
            const float e = 0.001f;
            SetInteractable(down, value > min + e);
            SetInteractable(up, value < max - e);
        }

        private static void SetText(TextMesh tm, string value)
        {
            if (tm != null) tm.text = value;
        }

        private static void SetInteractable(SprayButton b, bool value)
        {
            if (b != null && b.Interactable != value) b.Interactable = value;
        }

        private static string Signed(float value, string unit)
        {
            int v = Mathf.RoundToInt(value);
            string sep = unit == "°" ? "" : " ";
            return v > 0 ? $"+{v}{sep}{unit}" : $"{v}{sep}{unit}";
        }

        /// <summary>"15 cm a la derecha" en vez de "+15": el signo no dice hacia dónde.</summary>
        private static string Describe(float meters, string positive, string negative)
        {
            int cm = Mathf.RoundToInt(meters * 100f);
            if (cm == 0) return "de fábrica";
            return $"{Mathf.Abs(cm)} cm {(cm > 0 ? positive : negative)}";
        }
    }
}
