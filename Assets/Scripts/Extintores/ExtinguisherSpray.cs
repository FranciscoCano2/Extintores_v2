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

        [Header("Chorro")]
        [Tooltip("Partículas por segundo con el grip a fondo.")]
        [SerializeField] private float maxRate = 220f;
        [Tooltip("Velocidad de salida (m/s).")]
        [SerializeField] private float speed = 6f;
        [Tooltip("Duración de cada partícula (s).")]
        [SerializeField] private float lifetime = 0.6f;
        [Tooltip("Tamaño inicial y final de cada partícula (m). Crece como espuma/gas.")]
        [SerializeField] private float startSize = 0.04f;
        [SerializeField] private float endSize = 0.35f;
        [Tooltip("Apertura del cono del chorro (grados).")]
        [SerializeField, Range(0f, 45f)] private float coneAngle = 14f;
        [Tooltip("Color/opacidad base del chorro.")]
        [SerializeField] private Color tint = new Color(1f, 1f, 1f, 0.5f);

        [Header("Colisión con fuegos")]
        [Tooltip("Layers contra las que colisiona el chorro para apagar fuegos. Si queda en " +
                 "'Nothing' se usa la layer 'Fire'. Restringirlo evita que las partículas " +
                 "choquen contra el entorno/manos y encarezcan la simulación.")]
        [SerializeField] private LayerMask collidesWith = 0;

        [Header("Háptica")]
        [SerializeField] private bool haptics = true;

        /// <summary>True mientras se está rociando. Lo consume la lógica de apagado de fuegos.</summary>
        public bool IsSpraying { get; private set; }

        /// <summary>ParticleSystem del chorro. Lo usa <see cref="FireSource"/> para leer los
        /// eventos de colisión (<c>GetCollisionEvents</c>) al recibir las partículas.</summary>
        public ParticleSystem System => _ps;

        /// <summary>Agente de este extintor. Lo lee <see cref="FireSource"/> para modular el
        /// apagado según la clase de fuego (matriz de efectividad).</summary>
        public AgentType Agent => agent;

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

            if (spraying != IsSpraying)
            {
                IsSpraying = spraying;
                _emission.enabled = spraying;
            }

            if (spraying)
            {
                _emission.rateOverTime = Mathf.Lerp(maxRate * 0.35f, maxRate, g);
                if (haptics)
                    OVRInput.SetControllerVibration(0.35f, Mathf.Lerp(0.2f, 0.6f, g), triggerController);
            }
            else if (haptics)
            {
                OVRInput.SetControllerVibration(0f, 0f, triggerController);
            }
        }

        private void OnDisable()
        {
            if (haptics) OVRInput.SetControllerVibration(0f, 0f, triggerController);
        }

        private void BuildParticleSystem()
        {
            _ps = GetComponent<ParticleSystem>();
            if (_ps == null) _ps = gameObject.AddComponent<ParticleSystem>();

            var main = _ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World; // el chorro queda en el aire
            main.startSpeed = speed;
            main.startLifetime = lifetime;
            main.startColor = tint;
            main.startSize = endSize;                 // el tamaño final; la curva lo escala hacia abajo al nacer
            main.gravityModifier = 0.15f;
            main.maxParticles = 2000;
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
