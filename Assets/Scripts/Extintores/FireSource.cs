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

        private void Awake()
        {
            Health = maxHealth;
            _baseScale = transform.localScale;
            _systems = GetComponentsInChildren<ParticleSystem>(true);
            if (fireLight != null) _baseLightIntensity = fireLight.intensity;
            EnsureCollider();
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
            //   >0 apaga (más rápido cuanto mayor), ~0 casi nada, <0 lo aviva (extintor equivocado).
            float mult = ExtinguishMatrix.Effectiveness(spray.Agent, fireClass);
            ApplyExtinguish(weighted * healthPerParticle * mult);

            if (mult < 0f && Time.time >= _nextWrongWarnTime)
            {
                _nextWrongWarnTime = Time.time + 0.5f;
                WrongAgentUsed?.Invoke(this, spray.Agent);
                Debug.Log($"[FireSource] Agente equivocado ({spray.Agent}) sobre fuego clase " +
                          $"{fireClass}: no se apaga, se aviva.");
            }
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
            if (fadeOutSeconds > 0f) Invoke(nameof(Deactivate), fadeOutSeconds);
            else Deactivate();
        }

        private void Deactivate() => gameObject.SetActive(false);
    }
}
