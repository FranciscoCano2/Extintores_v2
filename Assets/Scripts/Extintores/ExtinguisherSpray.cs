using UnityEngine;

namespace Extintores
{
    /// <summary>
    /// Chorro de espuma/gas del extintor. Se coloca en la PUNTA de la boquilla (mano izquierda):
    /// las partículas salen a lo largo del +Z de este transform. El disparo se controla con el
    /// grip del mando indicado (por defecto el DERECHO), simulando apretar la palanca del tanque.
    ///
    /// Crea y configura su propio ParticleSystem por código, con una textura de punto suave
    /// generada en runtime, así no depende de ningún asset ni de shaders del pack.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class ExtinguisherSpray : MonoBehaviour
    {
        [Header("Disparo")]
        [Tooltip("Mando cuyo grip dispara el chorro (por defecto el derecho).")]
        [SerializeField] private OVRInput.Controller triggerController = OVRInput.Controller.RTouch;

        [Tooltip("Cuánto hay que apretar el grip (0..1) para empezar a rociar.")]
        [SerializeField, Range(0f, 1f)] private float threshold = 0.15f;

        [Header("Agente extintor")]
        [Tooltip("Tipo de agente de este extintor. Define su efectividad frente a cada clase de " +
                 "fuego (ver ExtinguishMatrix). Para tener 'tipos de extintor' distintos, duplicá " +
                 "el rig y cambiá este agente.")]
        [SerializeField] private AgentType agent = AgentType.DryChemABC;

        // Valores de referencia del chorro. Son públicos porque el prefab NozzleRig los tiene
        // serializados (y esos ganan sobre estos defaults): el tool Extintores ▸ Tune Spray Density
        // los lee de acá para reescribir el prefab, así los números viven en UN solo lugar.
        // 01/10/2026: 650 → 1000 p/s y partículas un poco más grandes y duraderas (nube más densa);
        // el alpha baja 0,3 → 0,22 para que no quede opaca como leche ni dispare el overdraw.
        public const float DefaultMaxRate = 1000f;
        public const float DefaultSpeed = 6f;
        public const float DefaultLifetime = 0.7f;
        public const float DefaultStartSize = 0.03f;
        public const float DefaultEndSize = 0.32f;
        public const float DefaultConeAngle = 14f;
        public const float DefaultAlpha = 0.22f;

        [Header("Chorro")]
        [Tooltip("Partículas por segundo con el grip a fondo. Es la densidad de la nube: subirlo " +
                 "la hace más compacta, pero cada partícula es un billboard traslúcido y el " +
                 "fillrate del Quest se paga en overdraw — al subirlo, bajar el alpha del tint.")]
        [SerializeField] private float maxRate = DefaultMaxRate;
        [Tooltip("Velocidad de salida (m/s).")]
        [SerializeField] private float speed = DefaultSpeed;
        [Tooltip("Duración de cada partícula (s).")]
        [SerializeField] private float lifetime = DefaultLifetime;
        [Tooltip("Tamaño inicial y final de cada partícula (m). Crece como espuma/gas.")]
        [SerializeField] private float startSize = DefaultStartSize;
        [SerializeField] private float endSize = DefaultEndSize;
        [Tooltip("Apertura del cono del chorro (grados).")]
        [SerializeField, Range(0f, 45f)] private float coneAngle = DefaultConeAngle;
        [Tooltip("Color/opacidad base del chorro.")]
        [SerializeField] private Color tint = new Color(1f, 1f, 1f, DefaultAlpha);
        [Tooltip("Variación aleatoria (0..1) de velocidad y vida por partícula. Con la nube densa, " +
                 "sin esto todas las partículas viajan iguales y se ven capas/bandas en vez de espuma.")]
        [SerializeField, Range(0f, 0.5f)] private float spread = 0.15f;

        [Header("Colisión con fuegos")]
        [Tooltip("Layers contra las que colisiona el chorro para apagar fuegos. Si queda en " +
                 "'Nothing' se usa la layer 'Fire'. Restringirlo evita que las partículas " +
                 "choquen contra el entorno/manos y encarezcan la simulación.")]
        [SerializeField] private LayerMask collidesWith = 0;

        [Header("Háptica")]
        [SerializeField] private bool haptics = true;
        [Tooltip("Mando de la boquilla (el que sostiene la manguera). También vibra mientras sale el " +
                 "chorro, un poco más suave: es por donde se siente la descarga en un extintor real.")]
        [SerializeField] private OVRInput.Controller nozzleController = OVRInput.Controller.LTouch;
        [SerializeField, Range(0f, 1f)] private float nozzleHapticScale = 0.7f;

        /// <summary>True mientras se está rociando. Lo consume la lógica de apagado de fuegos.</summary>
        public bool IsSpraying { get; private set; }

        /// <summary>ParticleSystem del chorro. Lo usa <see cref="FireSource"/> para leer los
        /// eventos de colisión (<c>GetCollisionEvents</c>) al recibir las partículas.</summary>
        public ParticleSystem System => _ps;

        /// <summary>Agente de este extintor. Lo lee <see cref="FireSource"/> para modular el
        /// apagado según la clase de fuego (matriz de efectividad).</summary>
        public AgentType Agent => agent;

        /// <summary>Caudal a fondo (partículas/s). Lo lee <see cref="FireSource"/> para que el
        /// ritmo de apagado no dependa de cuán densa se vea la nube.</summary>
        public float MaxRate => maxRate;

        /// <summary>Caudal con el que se calibró <c>FireSource.healthPerParticle</c>. El apagado se
        /// cobra POR PARTÍCULA, así que subir <see cref="MaxRate"/> apagaría los fuegos más rápido:
        /// <see cref="FireSource"/> divide por este caudal para compensarlo.</summary>
        public const float ReferenceRate = 220f;

        /// <summary>Cambia el agente en runtime (lo usa la pared de recarga al hacer el swap).</summary>
        public void SetAgent(AgentType newAgent) => agent = newAgent;

        private ParticleSystem _ps;
        private ParticleSystem.EmissionModule _emission;

        private void Awake()
        {
            BuildParticleSystem();
        }

        private void Update()
        {
            float g = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, triggerController);
            bool spraying = g >= threshold;
            Diagnose(g, spraying);

            if (spraying != IsSpraying)
            {
                IsSpraying = spraying;
                _emission.enabled = spraying;

                // Parar SÓLO al soltar, no en cada frame sin disparar: eso cortaba al instante los
                // pulsos de los botones, las bahías y el aviso de error, y el mando nunca vibraba.
                if (!spraying && haptics) StopHaptics();
            }

            if (spraying)
            {
                _emission.rateOverTime = Mathf.Lerp(maxRate * 0.35f, maxRate, g);
                if (haptics)
                {
                    float amp = Mathf.Lerp(0.2f, 0.6f, g);
                    Haptics.Set(0.35f, amp, triggerController);
                    if (nozzleController != OVRInput.Controller.None && nozzleController != triggerController)
                        Haptics.Set(0.35f, amp * nozzleHapticScale, nozzleController);
                }
            }
        }

        private void OnDisable()
        {
            if (haptics && IsSpraying) StopHaptics();
        }

        // ------------------------------------------------------------------ diagnóstico
        // "No sale espuma" tiene tres causas que no dan ningún error: el mando no llega al juego,
        // se aprieta el gatillo en vez del grip, o el chorro sale pero fuera de la vista. Cada aviso
        // se loguea UNA vez por sesión (log `[Spray]`).
        private bool _warnedNoController, _warnedIndexTrigger, _reportedFirstSpray;

        private void Diagnose(float grip, bool spraying)
        {
            if (!_warnedNoController && Time.timeSinceLevelLoad > 3f &&
                (OVRInput.GetConnectedControllers() & triggerController) == 0)
            {
                _warnedNoController = true;
                Debug.LogWarning($"[Spray] El mando {triggerController} no figura conectado: el grip " +
                                 "siempre lee 0 y el chorro no sale. Conectados: " +
                                 $"{OVRInput.GetConnectedControllers()}, activo: {OVRInput.GetActiveController()}.");
            }

            if (!_warnedIndexTrigger && !spraying &&
                OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, triggerController) > 0.5f)
            {
                _warnedIndexTrigger = true;
                Debug.Log($"[Spray] Se apretó el GATILLO del {triggerController} (grip = {grip:0.00}). " +
                          "El chorro sale con el GRIP (botón lateral, el del dedo mayor), no con el gatillo.");
            }

            if (!_reportedFirstSpray && spraying)
            {
                _reportedFirstSpray = true;
                var cam = Camera.main;
                string where = cam == null ? "sin Camera.main" :
                    $"a {Vector3.Distance(cam.transform.position, transform.position):0.00} m de la cabeza, " +
                    $"{Vector3.Angle(cam.transform.forward, transform.position - cam.transform.position):0}° " +
                    $"fuera del centro de la vista, apuntando {Vector3.Angle(cam.transform.forward, transform.forward):0}° " +
                    "respecto de la mirada";
                Debug.Log($"[Spray] Primer chorro: boquilla en {transform.position} ({where}).");
            }
        }

        private void StopHaptics()
        {
            Haptics.Stop(triggerController);
            if (nozzleController != OVRInput.Controller.None && nozzleController != triggerController)
                Haptics.Stop(nozzleController);
        }

        private void BuildParticleSystem()
        {
            _ps = GetComponent<ParticleSystem>();
            if (_ps == null) _ps = gameObject.AddComponent<ParticleSystem>();

            var main = _ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World; // el chorro queda en el aire
            main.startSpeed = Ranged(speed, spread);
            main.startLifetime = Ranged(lifetime, spread);
            main.startColor = tint;
            main.startSize = endSize;                 // el tamaño final; la curva lo escala hacia abajo al nacer
            main.gravityModifier = 0.15f;
            // Techo de partículas vivas derivado del caudal: rate × vida (×1.5 de margen por la
            // variación de 'spread'). Si estuviera fijo, subir maxRate recortaría el chorro en seco
            // al llegar al tope y no habría ningún error que lo delate.
            main.maxParticles = Mathf.Max(512, Mathf.CeilToInt(maxRate * lifetime * 1.5f));
            main.playOnAwake = false;

            _emission = _ps.emission;
            _emission.enabled = false;
            _emission.rateOverTime = 0f;

            var shape = _ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;   // emite a lo largo del +Z local
            shape.angle = coneAngle;
            shape.radius = 0.01f;

            // Colisión con los fuegos: cada partícula que impacta un FireSource dispara su
            // OnParticleCollision. World + High envía mensajes precisos por-collider; se limita
            // a la layer 'Fire' para no colisionar contra el entorno ni encarecer la simulación.
            var collision = _ps.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.High;
            collision.sendCollisionMessages = true; // habilita OnParticleCollision en el fuego
            collision.dampen = 0.8f;                 // el chorro no rebota lejos
            collision.bounce = 0f;
            collision.lifetimeLoss = 0.35f;          // las partículas se consumen al pegar
            // La layer de los botones (SprayButton) entra SIEMPRE, aunque el LayerMask serializado
            // en el prefab sea anterior a que esa layer existiera: si dependiera del valor guardado,
            // el chorro atravesaría el menú sin accionarlo y no habría ningún error que lo delate.
            int mask = collidesWith.value != 0 ? collidesWith.value : LayerMask.GetMask("Fire");
            int buttons = LayerMask.GetMask(SprayButton.LayerName);
            collision.collidesWith = mask | buttons;

            // Crece de startSize a endSize a lo largo de la vida.
            var sol = _ps.sizeOverLifetime;
            sol.enabled = true;
            float k = endSize > 1e-4f ? startSize / endSize : 0.1f;
            sol.size = new ParticleSystem.MinMaxCurve(
                1f, new AnimationCurve(new Keyframe(0f, k), new Keyframe(1f, 1f)));

            // Aparece y se desvanece.
            var col = _ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.15f),
                    new GradientAlphaKey(0f, 1f)
                });
            col.color = grad;

            // Material propio: Sprites/Default (unlit + alpha, compatible URP) con punto suave.
            var pr = GetComponent<ParticleSystemRenderer>();
            var shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                var mat = new Material(shader) { mainTexture = BuildSoftDot(64) };
                pr.material = mat;
            }
            pr.renderMode = ParticleSystemRenderMode.Billboard;

            _ps.Play(); // corre siempre; sin emisión no hay partículas
        }

        /// <summary>Curva de rango aleatorio <c>[v·(1-s), v·(1+s)]</c> para dar variación por partícula.</summary>
        private static ParticleSystem.MinMaxCurve Ranged(float value, float s)
        {
            if (s <= 0f) return new ParticleSystem.MinMaxCurve(value);
            return new ParticleSystem.MinMaxCurve(value * (1f - s), value * (1f + s));
        }

        /// <summary>Textura de punto radial suave (blanco con alpha que cae hacia el borde).</summary>
        private static Texture2D BuildSoftDot(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - r) / r, dy = (y + 0.5f - r) / r;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            tex.Apply();
            return tex;
        }
    }
}
