using UnityEngine;

namespace Extintores
{
    /// <summary>
    /// Muestra un cartelito (billboard) con la letra de la CLASE del fuego (A/B/C/K) flotando sobre
    /// las llamas — la ayuda del "modo entrenamiento". Se puede ocultar globalmente para el "modo
    /// examen" con <see cref="GloballyVisible"/> (lo maneja <see cref="TrainingManager"/>).
    ///
    /// Va en el mismo objeto que <see cref="FireSource"/> (el hijo 'Flame'). El cartel se crea por
    /// código (quad + Sprites/Default con la textura del badge) y se cuelga del PADRE del fuego para
    /// no encogerse cuando el fuego mengua. El <see cref="FirePrefabBuilder"/> le asigna la textura
    /// según la clase.
    ///
    /// ⚠️ <b>El tamaño se compensa contra la escala del padre en cada frame.</b> Si el cartel usara
    /// <c>localScale = size</c> a secas, heredaría la escala de ese padre: en Fire_B, donde la llama
    /// cuelga del auto (escala ×100), el cartel medía ~25 m de lado. La posición ya se fija en mundo,
    /// así que solo el tamaño necesitaba la corrección.
    /// </summary>
    [RequireComponent(typeof(FireSource))]
    public class FireClassBadge : MonoBehaviour
    {
        [Tooltip("Textura del badge (Badge_A/B/C/K). La asigna el FirePrefabBuilder por clase.")]
        [SerializeField] private Texture2D badge;
        [Tooltip("Altura del cartel sobre el pivote del fuego (m).")]
        [SerializeField] private float heightOffset = 0.9f;
        [Tooltip("Tamaño del cartel (m).")]
        [SerializeField] private float size = 0.25f;

        /// <summary>Visibilidad global de todos los badges (modo entrenamiento on/off).</summary>
        public static bool GloballyVisible = true;

        private FireSource _fire;
        private Transform _quad;
        private Renderer _rend;
        private Camera _cam;

        private void Awake() => _fire = GetComponent<FireSource>();

        private void Start()
        {
            if (badge == null) { enabled = false; return; }

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "ClassBadge";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            _rend = go.GetComponent<MeshRenderer>();
            var sh = Shader.Find("Sprites/Default");
            if (sh != null) _rend.sharedMaterial = new Material(sh) { mainTexture = badge };

            _quad = go.transform;
            _quad.SetParent(transform.parent != null ? transform.parent : transform, true);
            ApplyWorldSize();

            _cam = Camera.main;
            _fire.Extinguished += _ => { if (_rend != null) _rend.enabled = false; };
        }

        private void LateUpdate()
        {
            if (_quad == null || _rend == null) return;

            bool show = GloballyVisible && _fire != null && !_fire.IsExtinguished;
            if (_rend.enabled != show) _rend.enabled = show;
            if (!show) return;

            ApplyWorldSize();
            _quad.position = transform.position + Vector3.up * heightOffset;
            if (_cam == null) _cam = Camera.main;
            if (_cam != null)
                _quad.rotation = Quaternion.LookRotation(_quad.position - _cam.transform.position);
        }

        /// <summary>
        /// Deja el cartel de <see cref="size"/> metros REALES, sin importar cómo esté escalado el
        /// padre del que cuelga. Se recalcula cada frame porque el padre puede escalarse en runtime.
        /// </summary>
        private void ApplyWorldSize()
        {
            var parent = _quad.parent;
            if (parent == null) { _quad.localScale = Vector3.one * size; return; }

            var s = parent.lossyScale;
            _quad.localScale = new Vector3(
                size / NonZero(s.x),
                size / NonZero(s.y),
                size / NonZero(s.z));
        }

        private static float NonZero(float v) => Mathf.Approximately(v, 0f) ? 1f : Mathf.Abs(v);
    }
}
