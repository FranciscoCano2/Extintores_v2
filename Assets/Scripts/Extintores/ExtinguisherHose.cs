using UnityEngine;
using UnityEngine.Rendering;

namespace Extintores
{
    /// <summary>
    /// Manguera continua entre la salida de la válvula del tanque (mano derecha) y la entrada de la
    /// boquilla (mano izquierda). Son dos rigs que se mueven por separado, así que la manguera se
    /// rehace cada frame como un tubo a lo largo de una Bézier cúbica:
    /// <c>A, A + tA·h + g·s, B + tB·h + g·s, B</c>, con las tangentes de los conectores (+Z de cada
    /// anchor) y una caída <c>s</c> hacia abajo (g, en mundo) que se resuelve para que el largo de arco
    /// sea SIEMPRE <see cref="length"/>: con las manos juntas cuelga, con las manos separadas se tensa.
    /// Si la distancia entre conectores supera el largo, queda recta: los dos extremos van atados a los
    /// mandos, así que no hay de dónde sacar más manguera.
    ///
    /// Barata a propósito (Quest): sin física ni collider, una sola malla reutilizada, arrays
    /// preasignados y cero allocs por frame. Sin collider es invisible para el raycast del
    /// <see cref="LaserPointer"/> y para la colisión de partículas del chorro.
    ///
    /// Va en un hijo del NozzleRig: los vértices se escriben en el espacio de ESTE transform. Así
    /// <c>SceneWiringFixer.FixNozzleTint</c> la cuenta como pieza de la boquilla y la tiñe con el
    /// extintor (<see cref="ExtinguisherSwappable"/>).
    /// </summary>
    [DefaultExecutionOrder(200)] // después de los ExtinguisherMount (100): usa las poses finales
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class ExtinguisherHose : MonoBehaviour
    {
        /// <summary>Nombre del conector del tanque; lo busca el respaldo de runtime y el fixer.</summary>
        public const string OutletName = "HoseOutlet";

        /// <summary>Nombre del conector de la boquilla.</summary>
        public const string InletName = "HoseInlet";

        [Header("Conectores (+Z = hacia dónde sale la manguera)")]
        [Tooltip("Salida de la válvula del tanque. Si queda vacío, se busca un hijo 'HoseOutlet' " +
                 "del ExtinguisherSwappable.")]
        [SerializeField] private Transform outlet;

        [Tooltip("Entrada de la boquilla. Su +Z apunta hacia atrás, hacia el tanque.")]
        [SerializeField] private Transform inlet;

        [Header("Forma")]
        [Tooltip("Largo real de la manguera (m). No se estira: si las manos se separan más, queda recta.")]
        [SerializeField, Range(0.2f, 1.5f)] private float length = 0.60f;

        [Tooltip("Radio exterior del tubo (m).")]
        [SerializeField, Range(0.003f, 0.03f)] private float radius = 0.009f;

        [Tooltip("Cuánto sale recta de cada conector antes de curvarse, como fracción del largo.")]
        [SerializeField, Range(0f, 0.5f)] private float stiffness = 0.2f;

        [Tooltip("Qué tan rápido acompaña la caída a las manos (1/s). Más alto = más rígida.")]
        [SerializeField, Range(1f, 60f)] private float sagResponse = 14f;

        [Header("Malla (fija al arrancar)")]
        [SerializeField, Range(4, 32)] private int segments = 16;
        [SerializeField, Range(3, 16)] private int sides = 8;

        private const int SolveIterations = 8;

        private Mesh _mesh;
        private MeshRenderer _renderer;
        private Vector3[] _points;   // centro del tubo, espacio local
        private Vector3[] _vertices;
        private Vector3[] _normals;
        private float _sag;
        private bool _hasSag;

        public Transform Outlet { get => outlet; set => outlet = value; }
        public Transform Inlet { get => inlet; set => inlet = value; }
        public float Length => length;

        /// <summary>True si los conectores están más lejos que el largo de la manguera.</summary>
        public bool IsTaut { get; private set; }

        private void Awake()
        {
            _renderer = GetComponent<MeshRenderer>();
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            BuildMesh();
            if (outlet == null) outlet = FindOutlet();
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }

        /// <summary>Topología fija: anillos de <see cref="sides"/>+1 vértices (costura de UV).</summary>
        private void BuildMesh()
        {
            int ring = sides + 1;
            int count = (segments + 1) * ring;
            _points = new Vector3[segments + 1];
            _vertices = new Vector3[count];
            _normals = new Vector3[count];
            var uv = new Vector2[count];
            var tris = new int[segments * sides * 6];

            for (int i = 0; i <= segments; i++)
                for (int j = 0; j <= sides; j++)
                    uv[i * ring + j] = new Vector2((float)j / sides, (float)i / segments);

            int t = 0;
            for (int i = 0; i < segments; i++)
                for (int j = 0; j < sides; j++)
                {
                    int a = i * ring + j, b = a + ring;
                    tris[t++] = a; tris[t++] = b; tris[t++] = a + 1;
                    tris[t++] = a + 1; tris[t++] = b; tris[t++] = b + 1;
                }

            _mesh = new Mesh { name = "HoseTube" };
            _mesh.MarkDynamic();
            _mesh.vertices = _vertices;
            _mesh.normals = _normals;
            _mesh.uv = uv;
            _mesh.triangles = tris;
            GetComponent<MeshFilter>().sharedMesh = _mesh;
        }

        private static Transform FindOutlet()
        {
            var tank = FindFirstObjectByType<ExtinguisherSwappable>();
            if (tank == null) return null;
            foreach (var t in tank.GetComponentsInChildren<Transform>(true))
                if (t.name == OutletName) return t;
            return null;
        }

        private void LateUpdate()
        {
            bool ok = outlet != null && inlet != null && outlet.gameObject.activeInHierarchy
                      && inlet.gameObject.activeInHierarchy;
            _renderer.enabled = ok;
            if (!ok) return;

            Vector3 a = outlet.position, b = inlet.position;
            Vector3 ta = outlet.forward, tb = inlet.forward;
            float chord = Vector3.Distance(a, b);

            float h = stiffness * length;
            float targetSag = 0f;
            IsTaut = chord >= length;

            if (IsTaut)
            {
                h = 0f; // recta: con h = 0 y sin caída los puntos de control caen sobre la cuerda
            }
            else if (ArcLength(a, ta, b, tb, h, 0f) > length)
            {
                // Las tangentes solas ya se pasan del largo: se acortan hasta que entre, sin caída.
                float lo = 0f, hi = h;
                for (int k = 0; k < SolveIterations; k++)
                {
                    float mid = 0.5f * (lo + hi);
                    if (ArcLength(a, ta, b, tb, mid, 0f) > length) hi = mid; else lo = mid;
                }
                h = lo;
            }
            else
            {
                // Caída que deja el largo de arco igual al de la manguera.
                float lo = 0f, hi = length;
                for (int k = 0; k < SolveIterations; k++)
                {
                    float mid = 0.5f * (lo + hi);
                    if (ArcLength(a, ta, b, tb, h, mid) > length) hi = mid; else lo = mid;
                }
                targetSag = lo;
            }

            // La caída acompaña con un poco de inercia; al tensarse no se demora (no se estira).
            if (!_hasSag || IsTaut) _sag = targetSag;
            else _sag = Mathf.Lerp(_sag, targetSag, 1f - Mathf.Exp(-sagResponse * Time.deltaTime));
            _hasSag = true;

            Sample(a, ta, b, tb, h, _sag);
            WriteTube();
        }

        /// <summary>Largo de la polilínea de <see cref="segments"/> tramos (la misma que se dibuja).</summary>
        private float ArcLength(Vector3 a, Vector3 ta, Vector3 b, Vector3 tb, float h, float sag)
        {
            Control(a, ta, b, tb, h, sag, out Vector3 p1, out Vector3 p2);
            float sum = 0f;
            Vector3 prev = a;
            for (int i = 1; i <= segments; i++)
            {
                Vector3 p = Bezier(a, p1, p2, b, (float)i / segments);
                sum += Vector3.Distance(prev, p);
                prev = p;
            }
            return sum;
        }

        private static void Control(Vector3 a, Vector3 ta, Vector3 b, Vector3 tb, float h, float sag,
                                    out Vector3 p1, out Vector3 p2)
        {
            Vector3 drop = Vector3.down * sag;
            p1 = a + ta * h + drop;
            p2 = b + tb * h + drop;
        }

        private static Vector3 Bezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float u = 1f - t;
            return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
        }

        private void Sample(Vector3 a, Vector3 ta, Vector3 b, Vector3 tb, float h, float sag)
        {
            Control(a, ta, b, tb, h, sag, out Vector3 p1, out Vector3 p2);
            for (int i = 0; i <= segments; i++)
                _points[i] = transform.InverseTransformPoint(Bezier(a, p1, p2, b, (float)i / segments));
        }

        /// <summary>
        /// Anillos con marcos de transporte paralelo: cada normal es la anterior girada lo mínimo
        /// para seguir a la tangente, así el tubo no se retuerce como pasaría con un "up" fijo.
        /// </summary>
        private void WriteTube()
        {
            int ring = sides + 1;
            Vector3 tangent = (_points[1] - _points[0]).normalized;
            Vector3 normal = Vector3.ProjectOnPlane(transform.InverseTransformDirection(outlet.up), tangent);
            if (normal.sqrMagnitude < 1e-6f) normal = Vector3.ProjectOnPlane(Vector3.right, tangent);
            normal.Normalize();

            Vector3 min = _points[0], max = _points[0];
            for (int i = 0; i <= segments; i++)
            {
                Vector3 next = i < segments ? _points[i + 1] - _points[i] : _points[i] - _points[i - 1];
                if (next.sqrMagnitude > 1e-10f)
                {
                    next.Normalize();
                    normal = Quaternion.FromToRotation(tangent, next) * normal;
                    tangent = next;
                }
                Vector3 binormal = Vector3.Cross(tangent, normal);

                Vector3 c = _points[i];
                min = Vector3.Min(min, c);
                max = Vector3.Max(max, c);
                for (int j = 0; j <= sides; j++)
                {
                    float ang = j * (2f * Mathf.PI / sides);
                    Vector3 dir = Mathf.Cos(ang) * normal + Mathf.Sin(ang) * binormal;
                    int v = i * ring + j;
                    _vertices[v] = c + dir * radius;
                    _normals[v] = dir;
                }
            }

            _mesh.SetVertices(_vertices);
            _mesh.SetNormals(_normals);
            Vector3 pad = Vector3.one * radius;
            _mesh.bounds = new Bounds((min + max) * 0.5f, max - min + 2f * pad);
        }

        private void OnDrawGizmosSelected()
        {
            if (outlet == null || inlet == null) return;
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(outlet.position, radius * 1.5f);
            Gizmos.DrawWireSphere(inlet.position, radius * 1.5f);
            Gizmos.DrawRay(outlet.position, outlet.forward * 0.05f);
            Gizmos.DrawRay(inlet.position, inlet.forward * 0.05f);
            Gizmos.DrawLine(outlet.position, inlet.position);
        }
    }
}
