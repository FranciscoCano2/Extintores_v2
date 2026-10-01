using UnityEngine;

namespace Extintores
{
    /// <summary>
    /// Menú de inicio del entrenamiento. Tres opciones —Tutorial, Aleatorio, Personalizado— más
    /// Configuración (<see cref="SettingsMenu"/>: volumen, vibración, calibración del prop). En
    /// Personalizado no hay botones por clase: el usuario apunta, desde el centro del patio, al
    /// elemento que quiere apagar (<see cref="FireStation.Selector"/>), y el submenú sólo tiene la
    /// indicación y "Volver".
    ///
    /// Todos los botones son <see cref="SprayButton"/>: se accionan apuntándoles el chorro del
    /// extintor. No hay Canvas ni EventSystem en el proyecto y no hacen falta.
    ///
    /// El menú se oculta al arrancar una sesión (sea desde un botón o desde un elemento) y vuelve
    /// solo cuando <see cref="TrainingSession"/> termina, así que no compite con "Continuar".
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        [SerializeField] private TrainingSession session;

        [Header("Paneles")]
        [Tooltip("Contenedor de las tres opciones principales.")]
        [SerializeField] private GameObject mainPanel;
        [Tooltip("Contenedor del submenú de Personalizado (indicación + Volver).")]
        [SerializeField] private GameObject customPanel;

        [Header("Botones principales")]
        [SerializeField] private SprayButton tutorialButton;
        [SerializeField] private SprayButton randomButton;
        [SerializeField] private SprayButton customButton;

        [Header("Submenú Personalizado")]
        [SerializeField] private SprayButton backButton;

        [Header("Configuración")]
        [Tooltip("Botón del menú principal que abre la configuración.")]
        [SerializeField] private SprayButton settingsButton;
        [Tooltip("Contenedor del submenú de configuración (SettingsMenu).")]
        [SerializeField] private GameObject settingsPanel;
        [SerializeField] private SprayButton settingsBackButton;

        public SprayButton SettingsButton => settingsButton;
        public GameObject SettingsPanel => settingsPanel;
        public SprayButton SettingsBackButton => settingsBackButton;
        public GameObject MainPanel => mainPanel;

        private void Awake()
        {
            if (session == null) session = FindFirstObjectByType<TrainingSession>();

            if (tutorialButton != null) tutorialButton.Pressed += _ => Launch(TrainingSession.Mode.Tutorial);
            if (randomButton != null) randomButton.Pressed += _ => Launch(TrainingSession.Mode.Random);
            if (customButton != null) customButton.Pressed += _ => ShowCustom(true);
            if (backButton != null) backButton.Pressed += _ => ShowCustom(false);
            if (settingsButton != null) settingsButton.Pressed += _ => ShowSettings(true);
            if (settingsBackButton != null) settingsBackButton.Pressed += _ => ShowSettings(false);
        }

        private void OnEnable()
        {
            if (session == null) return;
            session.onSessionFinished.AddListener(Show);
            session.onSessionStarted.AddListener(Hide);
        }

        private void OnDisable()
        {
            if (session == null) return;
            session.onSessionFinished.RemoveListener(Show);
            session.onSessionStarted.RemoveListener(Hide);
        }

        private void Start() => Show();

        /// <summary>Muestra el menú principal (y esconde el submenú).</summary>
        public void Show()
        {
            SetActive(mainPanel, true);
            SetActive(customPanel, false);
            SetActive(settingsPanel, false);
            if (session != null) session.SetStationSelectorsVisible(false);
        }

        /// <summary>Esconde el menú entero, mientras dura la sesión.</summary>
        public void Hide()
        {
            SetActive(mainPanel, false);
            SetActive(customPanel, false);
            SetActive(settingsPanel, false);
            if (session != null) session.SetStationSelectorsVisible(false);
        }

        private void ShowCustom(bool value)
        {
            SetActive(mainPanel, !value);
            SetActive(customPanel, value);
            if (session != null) session.SetStationSelectorsVisible(value);
        }

        private void ShowSettings(bool value)
        {
            SetActive(mainPanel, !value);
            SetActive(settingsPanel, value);
        }

        private void Launch(TrainingSession.Mode mode)
        {
            if (session == null) return;
            Hide();

            switch (mode)
            {
                case TrainingSession.Mode.Tutorial: session.StartTutorial(); break;
                case TrainingSession.Mode.Random: session.StartRandom(); break;
            }
        }

        private static void SetActive(GameObject go, bool value)
        {
            if (go != null && go.activeSelf != value) go.SetActive(value);
        }
    }
}
