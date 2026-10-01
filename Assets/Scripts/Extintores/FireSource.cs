using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Extintores
{
    /// <summary>
    /// Envuelve un VFX de fuego (pack "Free Fire VFX") y lo hace APAGABLE.
    ///
    /// El chorro del extintor (<see cref="ExtinguisherSpray"/>) tiene el módulo Collision
    /// activo con <c>sendCollisionMessages</c>: cada partícula que impacta este fuego dispara
    /// <see cref="OnParticleCollision"/> y baja la "salud". Al llegar a 0 el fuego se extingue
    /// (deja de emitir, se desactiva su collider) y se avisa por el evento
    /// <see cref="Extinguished"/> — que consumirá el futuro FireManager (condición de éxito).
    ///
    /// Se auto-configura: si no hay collider agrega un SphereCollider que cubre las llamas y
    /// fija la layer <b>Fire</b> (contra la que colisiona el chorro). Basta con arrastrar un
    /// prefab de fuego a la escena y añadir este componente.
    ///
    /// ⚠️ La colisión de partículas World+High envía mensajes por-collider pero NO se aprecia
    /// bien por Oculus/PC Link (igual que la pose de los mandos): probar con Build and Run.
    /// </summary>
    [DefaultExecutionOrder(60)]
    public class FireSource : MonoBehaviour
    {
        [Header("Clasificación")]
        [Tooltip("Clase de fuego. Determina qué agentes lo apagan (ver ExtinguishMatrix): " +
                 "A=sólidos, B=líquidos inflamables, C=eléctrico, K=aceites de cocina. Con el " +
                 "agente equivocado el fuego NO se apaga (o se aviva).")]
        [SerializeField] private FireClass fireClass = FireClass.A;

        [Header("Salud")]
        [SerializeField] private float maxHealth = 100f;

        [Tooltip("Salud que resta cada partícula del chorro al impactar. Sube para apagar más " +
                 "rápido; el ritmo real depende también del rate del ParticleSystem del chorro.")]
        [SerializeField] private float healthPerParticle = 0.6f;

        [Header("Técnica PASS (apuntar a la BASE)")]
        [Tooltip("Si está activo, las partículas que impactan en la base del fuego apagan a full y " +
                 "las que pegan más arriba tienen efecto reducido: enseña a apuntar a la base.")]
        [SerializeField] private bool requireBaseAim = true;
        [Tooltip("Altura desde la base considerada 'base' del fuego (m).")]
        [SerializeField] private float baseAimHeight = 0.35f;
        [Tooltip("Factor de efecto para impactos FUERA de la base (0 = inútil, 1 = igual que la base).")]
        [SerializeField, Range(0f, 1f)] private float offBaseFactor = 0.3f;

        [Header("Auto-configuración del collider")]
        [Tooltip("Radio del SphereCollider si se crea automáticamente (m).")]
        [SerializeField] private float autoColliderRadius = 0.4f;

        [Tooltip("Altura del centro del collider sobre el pivote del fuego (m). El pivote del " +
                 "VFX está en la base; el volumen de llamas queda por encima.")]
        [SerializeField] private float autoColliderHeight = 0.4f;

        [Header("Feedback visual")]
        [Tooltip("El fuego 'mengua' al apagarse: se escala entre este factor (salud 0) y 1 (salud full).")]
        [SerializeField, Range(0f, 1f)] private float minScaleFactor = 0.35f;

        [Tooltip("Luz opcional del fuego; su intensidad baja con la salud. Los VFX del pack no " +
                 "traen un Light real (el brillo es por partículas), así que suele quedar vacío.")]
        [SerializeField] private Light fireLight;

        [Tooltip("Segundos que se dejan tras extinguir para que las partículas restantes se " +
                 "desvanezcan antes de desactivar el GameObject.")]
        [SerializeField] private float fadeOutSeconds = 3f;

        [Header("Sonido")]
        [Tooltip("Crepitar del fuego, en loop. Los clips del pack de VFX sirven " +
                 "(Vefects/Free Fire VFX URP/Audio/SFX_Fire*_L). Sin clip, no suena nada.")]
        [SerializeField] private AudioClip loopClip;
        [Tooltip("Volumen con el fuego a salud llena. Baja solo a medida que se apaga.")]
        [SerializeField, Range(0f, 1f)] private float maxVolume = 0.8f;
        [Tooltip("A partir de acá el sonido empieza a atenuarse con la distancia (m).")]
        [SerializeField] private float minDistance = 2f;
        [Tooltip("Distancia a la que ya no se oye (m).")]
        [SerializeField] private float maxDistance = 18f;

        // --- API pública ---
        public float Health { get; private set; }
        public float Health01 => maxHealth > 0f ? Health / maxHealth : 0f;
        public bool IsExtinguished { get; private set; }

        /// <summary>Calidad del último impacto: 1 = pegó en la base, ~offBaseFactor = pegó arriba. Para HUD.</summary>
        public float LastAimQuality01 { get; private set; }

        /// <summary>Time.time del último impacto del chorro sobre este fuego (para HUD / lógica).</summary>
        public float LastHitTime { get; private set; }

        /// <summary>Clase de este fuego (A/B/C/K).</summary>
        public FireClass Class => fireClass;

        /// <summary>Efectividad (multiplicador) de un agente contra este fuego. Útil para HUD/tutorial.</summary>
        public float EffectivenessOf(AgentType agent) => ExtinguishMatrix.Effectiveness(agent, fireClass);

        /// <summary>Se dispara UNA vez cuando el fuego termina de apagarse (salud llega a 0).</summary>
        public event System.Action<FireSource> Extinguished;

        /// <summary>Se dispara (throttled) cuando un agente CONTRAPRODUCENTE golpea este fuego: el
        /// extintor equivocado no apaga y lo aviva. Para feedback "estás usando el extintor mal".</summary>
        public event System.Action<FireSource, AgentType> WrongAgentUsed;

        private ParticleSystem[] _systems;
        private Vector3 _baseScale;
        private float _baseLightIntensity;
        private readonly List<ParticleCollisionEvent> _events = new List<ParticleCollisionEvent>();
        private float _nextWrongWarnTime; // throttle del evento WrongAgentUsed
        private AudioSource _audio;

        private void Awake()
        {
            Health = maxHealth;
            _baseScale = transform.localScale;
            _systems = GetComponentsInChildren<ParticleSystem>(true);
            if (fireLight != null) _baseLightIntensity = fireLight.intensity;
            EnsureCollider();
            EnsureAudio();
        }

        private void OnEnable()
        {
            // El fuego se apaga desactivando el GameObject, así que el sonido tiene que volver a
            // arrancar cuando el puesto se repite (modo aleatorio / personalizado).
            if (_audio != null && loopClip != null && !IsExtinguished) _audio.Play();
        }

        private void OnDisable()
        {
            if (_audio != null) _audio.Stop();
        }

        /// <summary>
        /// AudioSource 3D en loop, creado por código igual que el collider: así basta con arrastrar
        /// el prefab del fuego y asignarle un clip, sin cablear nada a mano.
        /// </summary>
        private void EnsureAudio()
        {
            if (loopClip == null) return;

            _audio = GetComponent<AudioSource>();
            if (_audio == null) _audio = gameObject.AddComponent<AudioSource>();

            _audio.clip = loopClip;
            _audio.loop = true;
            _audio.playOnAwake = false;
            _audio.spatialBlend = 1f; // 3D: el fuego tiene que sonar DONDE está
            _audio.rolloffMode = AudioRolloffMode.Linear; // lineal: en VR se juzga la distancia mejor
            _audio.minDistance = minDistance;
            _audio.maxDistance = maxDistance;
            _audio.volume = maxVolume;
            _audio.dopplerLevel = 0f; // el usuario se teletransporta: sin esto se oye un pitch raro
        }

        /// <summary>
        /// El crepitar baja junto con las llamas, con el MISMO piso que la escala
        /// (<see cref="minScaleFactor"/>): mientras se siga viendo fuego se tiene que seguir
        /// oyendo. El silencio final lo hace <see cref="FadeAudio"/>, a la par del fade de las
        /// partículas.
        /// </summary>
        private void UpdateAudio()
        {
            if (_audio == null) return;
            _audio.volume = maxVolume * Mathf.Lerp(minScaleFactor, 1f, Health01);
        }

        // Deja la layer correcta ya en el editor para que el chorro colisione desde el primer frame.
        private void OnValidate()
        {
            int fire = LayerMask.NameToLayer("Fire");
            if (fire >= 0 && gameObject.layer != fire) gameObject.layer = fire;
        }

        private void EnsureCollider()
        {
            int fire = LayerMask.NameToLayer("Fire");
            if (fire >= 0) gameObject.layer = fire;

            if (GetComponent<Collider>() == null)
            {
                var sc = gameObject.AddComponent<SphereCollider>();
                sc.radius = autoColliderRadius;
                sc.center = new Vector3(0f, autoColliderHeight, 0f);
                sc.isTrigger = false; // la colisión de partículas actúa sobre colliders sólidos
            }
        }

        /// <summary>
        /// Recibe las partículas del chorro. <paramref name="other"/> es el GameObject del
        /// ParticleSystem que colisionó (la punta de la boquilla).
        /// </summary>
        private void OnParticleCollision(GameObject other)
        {
            if (IsExtinguished) return;

            var spray = other.GetComponent<ExtinguisherSpray>();
            if (spray == null || spray.System == null || !spray.IsSpraying) return;

            int n = spray.System.GetCollisionEvents(gameObject, _events);
            if (n <= 0) return;

            // Técnica PASS: pondera cada impacto según pegue en la base (peso 1) o más arriba
            // (peso offBaseFactor). Si requireBaseAim está apagado, todos valen 1.
            float weighted;
            if (requireBaseAim)
            {
                float baseY = transform.position.y;
                weighted = 0f;
                for (int i = 0; i < n; i++)
                {
                    float h = _events[i].intersection.y - baseY;
                    weighted += (h <= baseAimHeight) ? 1f : offBaseFactor;
                }
            }
            else
            {
                weighted = n;
            }

            LastAimQuality01 = weighted / n;
            LastHitTime = Time.time;

            // Efectividad del agente contra ESTA clase de fuego:
            //   ≥ umbral apaga, entre 0 y el umbral NO hace nada, <0 lo aviva.
            // Los intermedios antes apagaban "más lento" y en la práctica cualquier extintor servía.
            float mult = ExtinguishMatrix.Effectiveness(spray.Agent, fireClass);
            bool effective = mult >= ExtinguishMatrix.EffectiveThreshold;
            if (effective || mult < 0f)
                ApplyExtinguish(weighted * healthPerParticle * mult * RateNormalization(spray));

            if (!effective && Time.time >= _nextWrongWarnTime)
            {
                _nextWrongWarnTime = Time.time + 0.5f;
                WrongAgentUsed?.Invoke(this, spray.Agent);
                Debug.Log($"[FireSource] Agente equivocado ({spray.Agent}) sobre fuego clase " +
                          $"{fireClass}: no se apaga{(mult < 0f ? ", se aviva" : "")}.");
            }
        }

        /// <summary>
        /// Compensa la densidad de la nube: el daño se cobra POR PARTÍCULA, así que duplicar el
        /// caudal del chorro duplicaría la velocidad de apagado y desbalancearía el ejercicio sin
        /// que nada lo delate. Dividiendo por el caudal de referencia, <see cref="healthPerParticle"/>
        /// sigue significando lo mismo y la densidad queda como lo que es: una decisión visual.
        /// </summary>
        private static float RateNormalization(ExtinguisherSpray spray)
        {
            float rate = spray.MaxRate;
            return rate > 1f ? ExtinguisherSpray.ReferenceRate / rate : 1f;
        }

        /// <summary>
        /// Modula la salud del fuego: <paramref name="amount"/> positivo apaga, negativo lo aviva.
        /// Al llegar a 0 lo extingue. Público para que otra lógica (técnica PASS, etc.) lo use.
        /// </summary>
        public void ApplyExtinguish(float amount)
        {
            if (IsExtinguished || Mathf.Approximately(amount, 0f)) return;

            // amount>0 baja salud (apaga); amount<0 la sube (agente contraproducente aviva el
            // fuego), acotado a [0, maxHealth] — nunca supera su tamaño inicial.
            Health = Mathf.Clamp(Health - amount, 0f, maxHealth);
            UpdateVisual();

            if (Health <= 0f) Extinguish();
        }

        private void UpdateVisual()
        {
            float t = Health01;
            transform.localScale = _baseScale * Mathf.Lerp(minScaleFactor, 1f, t);
            if (fireLight != null) fireLight.intensity = _baseLightIntensity * t;
            UpdateAudio();
        }

        /// <summary>
        /// Vuelve a encender el fuego desde cero: salud llena, collider y partículas activos, escala
        /// original. Lo usa <see cref="TrainingSession"/> para repetir un puesto (modo aleatorio o
        /// personalizado) sin recargar la escena.
        ///
        /// ⚠️ Cancela el <c>Invoke</c> de <see cref="Deactivate"/> que dejó pendiente
        /// <see cref="Extinguish"/>: sin eso, un fuego reseteado dentro de la ventana de
        /// <see cref="fadeOutSeconds"/> se apagaría solo unos segundos después.
        /// </summary>
        public void ResetFire()
        {
            CancelInvoke(nameof(Deactivate));

            gameObject.SetActive(true);
            IsExtinguished = false;
            Health = maxHealth;
            LastAimQuality01 = 0f;
            LastHitTime = 0f;
            _nextWrongWarnTime = 0f;

            transform.localScale = _baseScale;
            if (fireLight != null) fireLight.intensity = _baseLightIntensity;

            var col = GetComponent<Collider>();
            if (col != null) col.enabled = true;

            if (_audio != null) { _audio.volume = maxVolume; _audio.Play(); }

            if (_systems != null)
                foreach (var ps in _systems)
                    if (ps != null) { ps.Clear(true); ps.Play(true); }
        }

        private void Extinguish()
        {
            IsExtinguished = true;

            if (_systems != null)
                foreach (var ps in _systems)
                    if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);

            var col = GetComponent<Collider>();
            if (col != null) col.enabled = false;

            Extinguished?.Invoke(this);

            // Deja que las partículas vivas terminen de desvanecerse antes de ocultar el objeto.
            if (fadeOutSeconds > 0f)
            {
                if (_audio != null) StartCoroutine(FadeAudio(fadeOutSeconds));
                Invoke(nameof(Deactivate), fadeOutSeconds);
            }
            else Deactivate();
        }

        /// <summary>
        /// Baja el crepitar junto con las llamas. Cortarlo de golpe al llegar a salud 0 suena a bug;
        /// acompañar el fade de las partículas se lee como "se terminó de apagar".
        /// </summary>
        private IEnumerator FadeAudio(float seconds)
        {
            float from = _audio.volume;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                _audio.volume = Mathf.Lerp(from, 0f, t / seconds);
                yield return null;
            }
            _audio.volume = 0f;
        }

        private void Deactivate() => gameObject.SetActive(false);
    }
}
