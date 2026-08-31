using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Extintores
{
    /// <summary>
    /// Orquesta el feedback pedagógico y el estado del entrenamiento:
    ///  • <b>Modo examen</b>: muestra/oculta los <see cref="FireClassBadge"/> (toggle con un botón).
    ///  • <b>Agente equivocado</b>: <see cref="FireSource.WrongAgentUsed"/> → háptica + sonido +
    ///    <see cref="onWrongAgent"/> / evento C# <see cref="WrongAgent"/>.
    ///  • <b>Éxito</b>: <see cref="FireManager.AllFiresExtinguished"/> → <see cref="onAllExtinguished"/>
    ///    / evento C# <see cref="Success"/>, y detiene el temporizador.
    ///  • <b>Datos de HUD</b>: fuegos restantes, tiempo y agente actual (los consume
    ///    <see cref="TrainingHud"/>).
    ///
    /// Corre después del <see cref="FireManager"/> (orden 80 > 70) para leer bien el total al iniciar.
    /// </summary>
    [DefaultExecutionOrder(80)]
    public class TrainingManager : MonoBehaviour
    {
        [SerializeField] private FireManager fireManager;
        [Tooltip("Extintor (chorro) para mostrar el agente actual en el HUD. Si queda vacío se busca.")]
        [SerializeField] private ExtinguisherSpray spray;

        [Header("Modo examen (ayudas)")]
        [Tooltip("Si está activo, arranca mostrando los badges de clase sobre los fuegos.")]
        [SerializeField] private bool showBadges = true;
        [Tooltip("Botón del mando que alterna las ayudas (badges).")]
        [SerializeField] private OVRInput.Button toggleButton = OVRInput.Button.Two;

        [Header("Feedback 'extintor equivocado'")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip wrongClip;
        [SerializeField] private bool haptics = true;

        [Header("Eventos (cableables en el inspector)")]
        public UnityEvent onWrongAgent;
        public UnityEvent onAllExtinguished;

        // Eventos C# para el HUD.
        public event System.Action WrongAgent;
        public event System.Action Success;

        // --- Datos para el HUD ---
        public int Remaining => fireManager != null ? fireManager.RemainingFires : 0;
        public int Total => fireManager != null ? fireManager.TotalFires : 0;
        public AgentType CurrentAgent => spray != null ? spray.Agent : AgentType.DryChemABC;
        public bool TimerRunning { get; private set; }
        public float Elapsed { get; private set; }
        public string TimeText => FormatTime(Elapsed);

        private readonly List<FireSource> _fires = new List<FireSource>();
        private float _nextWrong;

        private void Awake()
        {
            if (fireManager == null) fireManager = FindFirstObjectByType<FireManager>();
            if (spray == null) spray = FindFirstObjectByType<ExtinguisherSpray>();
        }

        private void Start()
        {
            FireClassBadge.GloballyVisible = showBadges;

            foreach (var f in FindObjectsByType<FireSource>(FindObjectsSortMode.None))
            {
                _fires.Add(f);
                f.WrongAgentUsed += HandleWrong;
            }

            if (fireManager != null) fireManager.AllFiresExtinguished += HandleSuccess;

            TimerRunning = Total > 0; // arranca el reloj si hay fuegos
        }

        private void OnDestroy()
        {
            foreach (var f in _fires)
                if (f != null) f.WrongAgentUsed -= HandleWrong;
            if (fireManager != null) fireManager.AllFiresExtinguished -= HandleSuccess;
        }

        private void Update()
        {
            if (TimerRunning) Elapsed += Time.deltaTime;

            if (OVRInput.GetDown(toggleButton))
            {
                showBadges = !showBadges;
                FireClassBadge.GloballyVisible = showBadges;
            }
        }

        private void HandleWrong(FireSource fire, AgentType agent)
        {
            if (Time.time < _nextWrong) return; // throttle
            _nextWrong = Time.time + 0.6f;

            if (haptics) StartCoroutine(Buzz());
            if (audioSource != null && wrongClip != null) audioSource.PlayOneShot(wrongClip);
            onWrongAgent?.Invoke();
            WrongAgent?.Invoke();
        }

        private void HandleSuccess()
        {
            TimerRunning = false;
            onAllExtinguished?.Invoke();
            Success?.Invoke();
        }

        private static string FormatTime(float seconds)
        {
            int s = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return $"{s / 60:00}:{s % 60:00}";
        }

        private static IEnumerator Buzz()
        {
            OVRInput.SetControllerVibration(0.6f, 0.8f, OVRInput.Controller.RTouch);
            yield return new WaitForSeconds(0.2f);
            OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
        }
    }
}
