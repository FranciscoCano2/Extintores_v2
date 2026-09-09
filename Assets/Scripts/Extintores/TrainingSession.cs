using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Extintores
{
    /// <summary>
    /// Orquesta el ejercicio: elige qué puestos se recorren según el modo, ubica al usuario en cada
    /// uno, mueve el panel de extintores a su lado y espera el botón "Continuar" para avanzar.
    ///
    /// Los tres modos:
    /// <list type="bullet">
    ///   <item><b>Tutorial</b>: los 4 puestos en orden, y en cada uno solo quedan habilitadas las
    ///         bahías cuyo agente es efectivo contra ese fuego.</item>
    ///   <item><b>Aleatorio</b>: un puesto al azar, con todas las bahías disponibles.</item>
    ///   <item><b>Personalizado</b>: el puesto de la clase elegida.</item>
    /// </list>
    ///
    /// Nunca teletransporta solo: al apagarse el fuego aparece el botón "Continuar" y el usuario
    /// decide cuándo moverse.
    /// </summary>
    [DefaultExecutionOrder(90)] // después de FireManager (70) y TrainingManager (80)
    public class TrainingSession : MonoBehaviour
    {
        public enum Mode { Tutorial, Random, Custom }

        [Header("Puestos")]
        [Tooltip("Los puestos del galpón. Si queda vacío se buscan en la escena.")]
        [SerializeField] private FireStation[] stations;

        [Header("Referencias")]
        [SerializeField] private FireManager fireManager;
        [SerializeField] private ExtinguisherStation panel;
        [Tooltip("Raíz del rig de OVR que se reposiciona (el objeto que contiene la cámara).")]
        [SerializeField] private Transform playerRig;
        [Tooltip("Anchor del ojo, para centrar al USUARIO y no al origen del rig.")]
        [SerializeField] private Transform centerEye;

        [Header("Lobby")]
        [Tooltip("Dónde queda el usuario en el menú.")]
        [SerializeField] private Transform lobbySpawn;
        [Tooltip("Dónde va el panel de extintores mientras se está en el menú.")]
        [SerializeField] private Transform lobbyPanelAnchor;

        [Header("Botón de avance")]
        [Tooltip("Botón 'Continuar', oculto hasta que se apaga el fuego.")]
        [SerializeField] private SprayButton continueButton;
        [Tooltip("Dónde aparece el botón respecto del usuario: X lateral, Y altura, Z al frente.")]
        [SerializeField] private Vector3 continueOffset = new Vector3(-0.55f, 1.3f, 1.6f);

        [Header("Umbral del tutorial")]
        [Tooltip("Efectividad mínima para que una bahía quede habilitada en modo tutorial.")]
        [SerializeField, Range(0f, 1f)] private float effectiveThreshold = 0.9f;

        [Header("Eventos")]
        public UnityEvent onSessionStarted;
        public UnityEvent onStageCleared;
        public UnityEvent onSessionFinished;

        /// <summary>Se dispara al entrar a un puesto (o null al volver al lobby).</summary>
        public event Action<FireStation> StageChanged;

        public Mode CurrentMode { get; private set; }
        public bool InSession { get; private set; }
        public FireStation CurrentStation =>
            _queue != null && _index >= 0 && _index < _queue.Count ? _queue[_index] : null;

        private readonly List<FireStation> _queue = new List<FireStation>();
        private int _index = -1;
        private FireSource _watched;

        private void Awake()
        {
            if (stations == null || stations.Length == 0)
                stations = FindObjectsByType<FireStation>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (fireManager == null) fireManager = FindFirstObjectByType<FireManager>();
            if (panel == null) panel = FindFirstObjectByType<ExtinguisherStation>();
            if (centerEye == null)
            {
                var cam = Camera.main;
                if (cam != null) centerEye = cam.transform;
            }
            if (playerRig == null && centerEye != null) playerRig = centerEye.root;
        }

        private void Start()
        {
            foreach (var s in stations) if (s != null) s.Deactivate();
            if (continueButton != null) SetContinueVisible(false);
            ReturnToLobby();
        }

        // ---------------------------------------------------------------- arranque de modos

        public void StartTutorial()
        {
            _queue.Clear();
            // Orden pedagógico A → B → C → K, no el orden en que estén en la escena.
            foreach (FireClass fc in Enum.GetValues(typeof(FireClass)))
            {
                var s = FindStation(fc);
                if (s != null) _queue.Add(s);
            }
            Begin(Mode.Tutorial);
        }

        public void StartRandom()
        {
            _queue.Clear();
            var pool = new List<FireStation>();
            foreach (var s in stations) if (s != null && s.Fire != null) pool.Add(s);
            if (pool.Count > 0) _queue.Add(pool[UnityEngine.Random.Range(0, pool.Count)]);
            Begin(Mode.Random);
        }

        public void StartCustom(FireClass fireClass)
        {
            _queue.Clear();
            var s = FindStation(fireClass);
            if (s != null) _queue.Add(s);
            Begin(Mode.Custom);
        }

        /// <summary>Sobrecarga para cablear desde UnityEvent del inspector (0=A,1=B,2=C,3=K).</summary>
        public void StartCustom(int fireClassIndex) => StartCustom((FireClass)fireClassIndex);

        private void Begin(Mode mode)
        {
            if (_queue.Count == 0)
            {
                Debug.LogWarning($"[TrainingSession] Modo {mode} sin puestos disponibles.");
                return;
            }

            CurrentMode = mode;
            InSession = true;
            _index = -1;
            onSessionStarted?.Invoke();
            Debug.Log($"[TrainingSession] Modo {mode}, {_queue.Count} puesto(s).");
            NextStage();
        }

        // ---------------------------------------------------------------- flujo

        private void NextStage()
        {
            Unwatch();

            foreach (var s in stations) if (s != null) s.Deactivate();
            SetContinueVisible(false);

            _index++;
            if (_index >= _queue.Count) { Finish(); return; }

            var station = _queue[_index];
            station.Activate();

            PlacePlayer(station.PlayerSpawn);
            MovePanel(station.PanelAnchor);
            ApplyBayGating(station);

            if (fireManager != null)
                fireManager.BeginSession(new[] { station.Fire });

            _watched = station.Fire;
            if (_watched != null) _watched.Extinguished += HandleFireOut;

            StageChanged?.Invoke(station);
            Debug.Log($"[TrainingSession] Puesto {_index + 1}/{_queue.Count}: clase {station.Class}.");
        }

        private void HandleFireOut(FireSource fire)
        {
            Unwatch();
            onStageCleared?.Invoke();

            // No avanza solo: el usuario decide cuándo con el botón "Continuar".
            SetContinueVisible(true);
            Debug.Log("[TrainingSession] Fuego apagado. Esperando 'Continuar'.");
        }

        /// <summary>La cablea el botón "Continuar".</summary>
        public void Continue()
        {
            if (!InSession) return;
            NextStage();
        }

        private void Finish()
        {
            InSession = false;
            _index = -1;
            onSessionFinished?.Invoke();
            ReturnToLobby();
            Debug.Log("[TrainingSession] Sesión terminada.");
        }

        public void ReturnToLobby()
        {
            Unwatch();
            foreach (var s in stations) if (s != null) s.Deactivate();
            SetContinueVisible(false);
            SetAllBaysInteractable(true);

            if (lobbySpawn != null) PlacePlayer(lobbySpawn);
            MovePanel(lobbyPanelAnchor);

            StageChanged?.Invoke(null);
        }

        // ---------------------------------------------------------------- piezas

        /// <summary>
        /// Reposiciona el rig para que <b>el usuario</b> quede en <paramref name="target"/>.
        ///
        /// Con tracking origin en FloorLevel el rig es el piso del guardian, y la cabeza está donde
        /// la persona esté parada dentro de él. Mover el rig al punto dejaría al usuario corrido esa
        /// misma distancia, así que se compensa el offset de la cabeza (solo en XZ; la altura la
        /// pone el tracking) y se rota el rig ALREDEDOR de la cabeza para alinear el yaw.
        /// </summary>
        private void PlacePlayer(Transform target)
        {
            if (playerRig == null || target == null) return;

            if (centerEye != null)
            {
                float yawDelta = target.eulerAngles.y - centerEye.eulerAngles.y;
                playerRig.RotateAround(centerEye.position, Vector3.up, yawDelta);

                Vector3 offset = target.position - centerEye.position;
                offset.y = 0f;
                playerRig.position += offset;
            }
            else
            {
                playerRig.SetPositionAndRotation(target.position, Quaternion.Euler(0f, target.eulerAngles.y, 0f));
            }
        }

        private void MovePanel(Transform anchor)
        {
            if (panel == null || anchor == null) return;
            panel.transform.SetPositionAndRotation(anchor.position, anchor.rotation);
        }

        /// <summary>
        /// Modo tutorial: deja habilitadas solo las bahías cuyo agente es realmente efectivo contra
        /// esta clase. Se habilitan TODAS las que superen el umbral, no una sola: para clase A sirven
        /// Agua, Espuma y ABC, y mostrar una única opción enseñaría algo falso.
        /// </summary>
        private void ApplyBayGating(FireStation station)
        {
            if (CurrentMode != Mode.Tutorial || station.Fire == null)
            {
                SetAllBaysInteractable(true);
                return;
            }

            foreach (var bay in Bays())
                bay.Interactable = station.Fire.EffectivenessOf(bay.Agent) >= effectiveThreshold;
        }

        private void SetAllBaysInteractable(bool value)
        {
            foreach (var bay in Bays()) bay.Interactable = value;
        }

        private IEnumerable<ExtinguisherBay> Bays()
        {
            if (panel == null) yield break;
            foreach (var b in panel.GetComponentsInChildren<ExtinguisherBay>(true))
                if (b != null) yield return b;
        }

        /// <summary>
        /// Muestra u oculta "Continuar". Al mostrarlo lo planta delante del puesto actual: el
        /// usuario ya está ubicado ahí, así que el botón aparece a la vista sin necesidad de un
        /// anchor por zona. Se orienta con la cara legible (−Z) hacia el usuario.
        /// </summary>
        private void SetContinueVisible(bool visible)
        {
            if (continueButton == null) return;

            if (visible)
            {
                var at = CurrentStation != null ? CurrentStation.PlayerSpawn : lobbySpawn;
                if (at != null)
                {
                    continueButton.transform.SetPositionAndRotation(
                        at.position + at.rotation * continueOffset,
                        Quaternion.Euler(0f, at.eulerAngles.y, 0f));
                }
            }

            continueButton.gameObject.SetActive(visible);
        }

        private FireStation FindStation(FireClass fireClass)
        {
            foreach (var s in stations)
                if (s != null && s.Fire != null && s.Class == fireClass) return s;
            Debug.LogWarning($"[TrainingSession] No hay puesto para la clase {fireClass}.");
            return null;
        }

        private void Unwatch()
        {
            if (_watched != null) _watched.Extinguished -= HandleFireOut;
            _watched = null;
        }

        private void OnDestroy() => Unwatch();
    }
}
