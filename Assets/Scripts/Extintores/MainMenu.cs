using UnityEngine;

namespace Extintores
{
    /// <summary>
    /// Menú de inicio del entrenamiento. Tres opciones —Tutorial, Aleatorio, Personalizado— y, dentro
    /// de la última, un submenú con las cuatro clases de fuego.
    ///
    /// Todos los botones son <see cref="SprayButton"/>: se accionan apuntándoles el chorro del
    /// extintor. No hay Canvas ni EventSystem en el proyecto y no hacen falta.
    ///
    /// El menú se oculta al arrancar una sesión y vuelve solo cuando <see cref="TrainingSession"/>
    /// termina, así que no compite con el botón "Continuar".
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        [SerializeField] private TrainingSession session;

        [Header("Paneles")]
        [Tooltip("Contenedor de las tres opciones principales.")]
        [SerializeField] private GameObject mainPanel;
        [Tooltip("Contenedor del submenú de clases (A/B/C/K).")]
        [SerializeField] private GameObject customPanel;

        [Header("Botones principales")]
        [SerializeField] private SprayButton tutorialButton;
        [SerializeField] private SprayButton randomButton;
        [SerializeField] private SprayButton customButton;

        [Header("Submenú de clases")]
        [Tooltip("Un botón por clase, en el orden A, B, C, K.")]
        [SerializeField] private SprayButton[] classButtons = new SprayButton[4];
        [SerializeField] private SprayButton backButton;

        private void Awake()
        {
            if (session == null) session = FindFirstObjectByType<TrainingSession>();

            if (tutorialButton != null) tutorialButton.Pressed += _ => Launch(TrainingSession.Mode.Tutorial, 0);
            if (randomButton != null) randomButton.Pressed += _ => Launch(TrainingSession.Mode.Random, 0);
            if (customButton != null) customButton.Pressed += _ => ShowCustom(true);
            if (backButton != null) backButton.Pressed += _ => ShowCustom(false);

            for (int i = 0; i < classButtons.Length; i++)
            {
                if (classButtons[i] == null) continue;
                int index = i; // capturar por valor: sin esto todos los botones lanzarían la clase K
                classButtons[i].Pressed += _ => Launch(TrainingSession.Mode.Custom, index);
            }
        }

        private void OnEnable()
        {
            if (session != null) session.onSessionFinished.AddListener(Show);
        }

        private void OnDisable()
        {
            if (session != null) session.onSessionFinished.RemoveListener(Show);
        }

        private void Start() => Show();

        /// <summary>Muestra el menú principal (y esconde el submenú).</summary>
        public void Show()
        {
            SetActive(mainPanel, true);
            SetActive(customPanel, false);
        }

        /// <summary>Esconde el menú entero, mientras dura la sesión.</summary>
        public void Hide()
        {
            SetActive(mainPanel, false);
            SetActive(customPanel, false);
        }

        private void ShowCustom(bool value)
        {
            SetActive(mainPanel, !value);
            SetActive(customPanel, value);
        }

        private void Launch(TrainingSession.Mode mode, int fireClassIndex)
        {
            if (session == null) return;
            Hide();

            switch (mode)
            {
                case TrainingSession.Mode.Tutorial: session.StartTutorial(); break;
                case TrainingSession.Mode.Random: session.StartRandom(); break;
                case TrainingSession.Mode.Custom: session.StartCustom(fireClassIndex); break;
            }
        }

        private static void SetActive(GameObject go, bool value)
        {
            if (go != null && go.activeSelf != value) go.SetActive(value);
        }
    }
}
