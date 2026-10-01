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
    ///   <item><b>Personalizado</b>: el puesto que el usuario elige apuntando, desde el centro del
    ///         patio, al elemento que quiere apagar (<see cref="FireStation.Selector"/>).</item>
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

        [Header("Botón de avance")]
        [Tooltip("Botón 'Continuar', oculto hasta que se apaga el fuego.")]
        [SerializeField] private SprayButton continueButton;
        [Tooltip("Dónde aparece el botón respecto de la CABEZA: X lateral, Y sobre/bajo la línea de " +
                 "los ojos, Z al frente.")]
        [SerializeField] private Vector3 continueOffsetFromHead = new Vector3(0f, -0.25f, 1.5f);

        [Header("Umbral del tutorial")]
        [Tooltip("Efectividad mínima para que una bahía quede habilitada en modo tutorial.")]
        [SerializeField, Range(0f, 1f)] private float effectiveThreshold = ExtinguishMatrix.EffectiveThreshold;

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

        /// <summary>Número de nivel en curso (1..<see cref="StageCount"/>), 0 fuera del ejercicio.</summary>
        public int StageNumber => InSession ? Mathf.Clamp(_index + 1, 0, _queue.Count) : 0;
        public int StageCount => _queue.Count;

        /// <summary>
        /// El fuego del puesto ya se apagó y se espera "Continuar". Junto con estar fuera de sesión,
        /// es cuando <see cref="LaserPointer"/> muestra el rayo siempre (hay menú, no fuego).
        /// </summary>
        public bool AwaitingContinue { get; private set; }

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

            foreach (var s in stations)
            {
                if (s == null || s.Selector == null) continue;
                var station = s; // capturar por valor
                s.Selector.Pressed += _ => StartCustom(station);
            }
        }

        private void OnEnable() => GameSettings.Changed += OnSettingsChanged;
        private void OnDisable() => GameSettings.Changed -= OnSettingsChanged;

        /// <summary>El ajuste del panel se ve al instante, esté en un puesto o en la vista previa.</summary>
        private void OnSettingsChanged()
        {
            if (panel != null && panel.gameObject.activeSelf) ApplyPanelPose();
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

        public void StartCustom(FireClass fireClass) => StartCustom(FindStation(fireClass));

        /// <summary>Personalizado sobre un puesto concreto: lo usa el cartel apuntable del elemento.</summary>
        public void StartCustom(FireStation station)
        {
            if (InSession) return; // un selector rezagado no debe cortar una sesión en curso
            _queue.Clear();
            if (station != null && station.Fire != null) _queue.Add(station);
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
            SetStationSelectorsVisible(false);

            // En Aleatorio la gracia es deducir el extintor: ni la letra sobre el fuego ni el
            // verde/rojo del panel, que son la respuesta.
            bool hide = mode == Mode.Random;
            FireClassBadge.SuppressedByMode = hide;
            BayHighlighter.HideAnswers = hide;

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
            AwaitingContinue = false;

            _index++;
            if (_index >= _queue.Count) { Finish(); return; }

            var station = _queue[_index];
            station.Activate();

            PlacePlayer(station.PlayerSpawn);
            SetPanelVisible(true);
            MovePanel(station.PanelAnchor, station.PlayerSpawn);
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
            AwaitingContinue = true;
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
            AwaitingContinue = false;
            SetAllBaysInteractable(true);
            SetStationSelectorsVisible(false);
            FireClassBadge.SuppressedByMode = false;
            BayHighlighter.HideAnswers = false;

            if (lobbySpawn != null) PlacePlayer(lobbySpawn);
            // En el lobby no hay fuego: el panel de extintores sólo estorbaría el menú.
            SetPanelVisible(false);

            StageChanged?.Invoke(null);
        }

        /// <summary>
        /// Muestra los carteles apuntables sobre los elementos. Lo prende <see cref="MainMenu"/> al
        /// abrir Personalizado; sólo tiene sentido en el lobby.
        /// </summary>
        public void SetStationSelectorsVisible(bool visible)
        {
            if (visible && InSession) return;
            foreach (var s in stations) if (s != null) s.SetSelectable(visible);
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

        private void SetPanelVisible(bool visible)
        {
            if (panel != null && panel.gameObject.activeSelf != visible)
                panel.gameObject.SetActive(visible);
        }

        /// <summary>
        /// Muestra el panel de extintores en el lobby, en el mismo lugar relativo que tendrá en los
        /// puestos, para que el usuario lo acomode desde Configuración viéndolo. Fuera del lobby no
        /// hace nada: en un puesto el panel lo maneja la etapa.
        /// </summary>
        public void PreviewPanel(bool on)
        {
            if (InSession) return;
            if (!on) { SetPanelVisible(false); return; }

            var anchor = LobbyPanelAnchor();
            if (anchor == null)
            {
                Debug.LogWarning("[TrainingSession] No encontré 'Lobby/PanelAnchor': no puedo mostrar " +
                                 "la vista previa del panel.");
                return;
            }
            SetPanelVisible(true);
            MovePanel(anchor, lobbySpawn);
        }

        /// <summary>El PanelAnchor del lobby (hermano de <see cref="lobbySpawn"/> en el galpón).</summary>
        private Transform LobbyPanelAnchor()
        {
            if (lobbySpawn == null || lobbySpawn.parent == null) return null;
            return lobbySpawn.parent.Find("PanelAnchor");
        }

        private Transform _panelAnchor;
        private Transform _panelSpawn;

        private void MovePanel(Transform anchor, Transform spawn)
        {
            _panelAnchor = anchor;
            _panelSpawn = spawn;
            ApplyPanelPose();
        }

        /// <summary>
        /// Pose del panel = su anchor + el ajuste del usuario (<see cref="GameSettings.PanelOffset"/>,
        /// en el espacio del anchor). Si el ajuste lo corre de costado o en profundidad, se re-gira
        /// en yaw para que siga mirando al spawn: la cara legible (−Z) tiene que quedar de frente al
        /// usuario (ver PLAN §3); corrido medio metro sin girar, se leería de canto.
        /// </summary>
        private void ApplyPanelPose()
        {
            if (panel == null || _panelAnchor == null) return;

            Vector3 pos = _panelAnchor.position + _panelAnchor.rotation * GameSettings.PanelOffset;
            Quaternion rot = _panelAnchor.rotation;

            if (_panelSpawn != null)
            {
                Vector3 from = _panelAnchor.position - _panelSpawn.position; from.y = 0f;
                Vector3 to = pos - _panelSpawn.position; to.y = 0f;
                if (from.sqrMagnitude > 1e-4f && to.sqrMagnitude > 1e-4f)
                    rot = Quaternion.FromToRotation(from, to) * rot;
            }

            panel.transform.SetPositionAndRotation(pos, rot);
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
        /// Muestra u oculta "Continuar". Al mostrarlo lo planta <b>delante de la cabeza</b>, no en
        /// un punto fijo derivado del <c>PlayerSpawn</c> del puesto.
        ///
        /// ⚠ Esa era la causa de "el botón Continuar no responde a ningún estímulo": el usuario se
        /// gira dentro del guardian (para usar el panel de extintores, sin ir más lejos), y un botón
        /// clavado a 1,6 m del anchor le quedaba <b>a la espalda</b>. Nunca lo veía, así que parecía
        /// muerto. Anclado a la mirada aparece siempre a la vista.
        ///
        /// Se orienta con la cara legible (−Z) hacia el usuario y sin inclinación: sólo yaw.
        /// </summary>
        private void SetContinueVisible(bool visible)
        {
            if (continueButton == null) return;

            if (visible) PlaceContinueInView();
            continueButton.gameObject.SetActive(visible);
        }

        private void PlaceContinueInView()
        {
            // Fallback al anchor del puesto sólo si no hay cabeza que consultar.
            Transform reference = centerEye;
            Vector3 offset = continueOffsetFromHead;
            if (reference == null)
            {
                reference = CurrentStation != null ? CurrentStation.PlayerSpawn : lobbySpawn;
                if (reference == null)
                {
                    Debug.LogWarning("[TrainingSession] No tengo cabeza ni spawn para ubicar " +
                                     "'Continuar': queda donde estaba.");
                    return;
                }
                offset.y += 1.6f; // el spawn está en el piso; la cabeza no
            }

            var rot = Quaternion.Euler(0f, reference.eulerAngles.y, 0f);
            continueButton.transform.SetPositionAndRotation(reference.position + rot * offset, rot);
            Debug.Log($"[TrainingSession] 'Continuar' a la vista en {continueButton.transform.position}.");
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
