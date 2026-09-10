using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Extintores
{
    /// <summary>
    /// Botón que se acciona <b>apuntándole</b>, a distancia, con lo que el usuario ya tiene en la
    /// mano. No hace falta UGUI ni TextMeshPro. Dos fuentes lo alimentan, las dos por
    /// <see cref="Hold"/>:
    /// <list type="bullet">
    ///   <item><see cref="LaserPointer"/> — el rayo de la boquilla. Es el camino normal: pega en un
    ///         solo botón, y su gatillo lo acciona al toque vía <see cref="TryPress"/>.</item>
    ///   <item>El chorro (<see cref="OnParticleCollision"/>), <b>sólo si no hay ningún láser</b>.</item>
    /// </list>
    ///
    /// Usa el mismo mecanismo que <see cref="FireSource"/>: el ParticleSystem del chorro tiene
    /// <c>sendCollisionMessages</c>, así que cada partícula que impacta llama a
    /// <see cref="OnParticleCollision"/>. En vez de restar salud, acá se ACUMULA carga: la barra se
    /// llena mientras le tirás y se vacía sola si dejás de apuntar. Ese llenado progresivo cumple el
    /// mismo rol que el <c>dwellSeconds</c> de <see cref="ExtinguisherBay"/>: evita que un barrido
    /// accidental del chorro dispare el botón.
    ///
    /// ⚠ La carga se mide en <b>segundos de chorro sostenido</b>, no en cantidad de partículas. Con
    /// el conteo de partículas el ritmo dependía de la distancia al botón (el cono se abre: a 3 m
    /// llega la cuarta parte que a 1,5 m) y de cuánto se apretaba el grip, así que era imposible de
    /// calibrar y en el visor se sentía lentísimo. Las partículas ahora sólo dicen <i>si</i> el
    /// chorro está pegando; el reloj hace el resto.
    ///
    /// Se auto-configura: si no tiene collider le agrega un BoxCollider y se pone en la layer
    /// <see cref="LayerName"/> (contra la que el chorro colisiona siempre).
    /// </summary>
    [DefaultExecutionOrder(60)]
    public class SprayButton : MonoBehaviour
    {
        /// <summary>Layer contra la que el chorro colisiona para accionar botones.</summary>
        public const string LayerName = "Interactable";

        [Header("Contenido")]
        [Tooltip("Texto del botón. Se dibuja con TextMesh (igual que el HUD), así no hace falta " +
                 "UGUI ni generar una textura por cada rótulo.")]
        [SerializeField] private string text = "OK";
        [Tooltip("Textura opcional. Si se asigna, reemplaza al texto.")]
        [SerializeField] private Texture2D label;
        [Tooltip("Tamaño del botón en metros (ancho × alto).")]
        [SerializeField] private Vector2 size = new Vector2(0.36f, 0.26f);

        [Header("Activación")]
        [Tooltip("Segundos de chorro sostenido para accionar el botón. No depende de la distancia " +
                 "ni de cuánto aprietes el grip.")]
        [SerializeField] private float fillSeconds = 0.35f;
        [Tooltip("Segundos que tarda la barra en vaciarse del todo si dejás de apuntar.")]
        [SerializeField] private float emptySeconds = 0.30f;
        [Tooltip("Segundos de bloqueo tras accionarse, para no dispararlo dos veces seguidas.")]
        [SerializeField] private float rearmSeconds = 0.6f;
        [Tooltip("Margen sin impactos que se tolera antes de empezar a vaciar. La colisión de " +
                 "partículas titila de un frame a otro; sin esto la barra bajaría a los saltos.")]
        [SerializeField] private float hitGraceSeconds = 0.12f;

        [Header("Colores")]
        [SerializeField] private Color idleColor = new Color(0.20f, 0.22f, 0.26f);
        [Tooltip("Placa cuando el láser lo está apuntando. Además la placa se agranda un poco, así " +
                 "asoma por detrás y se lee como un borde resaltado.")]
        [SerializeField] private Color hoverColor = new Color(0.30f, 0.55f, 0.85f);
        [SerializeField] private Color fillColor = new Color(0.20f, 0.80f, 0.35f);
        [SerializeField] private Color disabledColor = new Color(0.32f, 0.32f, 0.32f);
        [Tooltip("Cuánto se agranda la placa al apuntarlo (1 = nada).")]
        [SerializeField] private float hoverScale = 1.1f;

        [Header("Feedback")]
        [SerializeField] private bool haptics = true;

        [Header("Evento")]
        public UnityEvent onPressed;

        /// <summary>Se dispara al accionarse el botón.</summary>
        public event Action<SprayButton> Pressed;

        /// <summary>Si es false el botón se ve gris e ignora el chorro.</summary>
        public bool Interactable
        {
            get => _interactable;
            set
            {
                _interactable = value;
                if (!value) { _charge = 0f; _lastHitTime = float.NegativeInfinity; }
                UpdateVisual();
            }
        }

        /// <summary>Progreso de carga 0..1, para que el llenado sea visible.</summary>
        public float Charge01 => Mathf.Clamp01(_charge);

        private bool _interactable = true;
        private bool _hovered;
        private float _charge;      // 0..1
        private float _lastHitTime = float.NegativeInfinity;
        private float _lockedUntil;

        private Transform _plate;
        private Transform _fill;
        private Renderer _fillRend;
        private Renderer _plateRend;
        private Renderer _faceRend;
        private TextMesh _textMesh;
        private ExtinguisherHandle _handle;
        private float _drawnCharge = -1f;
        private bool _drawnInteractable;
        private bool _drawnHovered;
        private MaterialPropertyBlock _mpb;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private readonly List<ParticleCollisionEvent> _events = new List<ParticleCollisionEvent>();

        private void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            EnsureLayerAndCollider();
            BuildVisual();
            UpdateVisual();
        }

        private void EnsureLayerAndCollider()
        {
            int l = LayerMask.NameToLayer(LayerName);
            if (l >= 0) gameObject.layer = l;
            else Debug.LogWarning($"[SprayButton] Falta la layer '{LayerName}': el chorro no va a " +
                                  "accionar los botones. Creala en Project Settings ▸ Tags and Layers.");

            var col = GetComponent<Collider>();
            if (col == null)
            {
                var box = gameObject.AddComponent<BoxCollider>();
                box.size = new Vector3(size.x, size.y, 0.06f);
            }
            // Sólido a propósito: la colisión de partículas actúa sobre colliders no-trigger.
            foreach (var c in GetComponentsInChildren<Collider>(true)) c.isTrigger = false;
        }

        /// <summary>
        /// Placa de fondo + barra de llenado + cara con la etiqueta, todo por código.
        ///
        /// <b>La cara legible del botón es su −Z</b>, así que colocalo con el forward apuntando en
        /// dirección CONTRARIA al usuario (típicamente hacia la pared). Es la convención del Quad y
        /// del TextMesh de Unity, y la misma que terminó teniendo el panel de extintores cuando se
        /// corrigieron a mano sus etiquetas. Las capas se apilan hacia −Z para que el rótulo quede
        /// siempre delante de la barra y de la placa.
        /// </summary>
        private void BuildVisual()
        {
            // Fondo: sin él, con la carga en cero el botón se vería vacío.
            _plate = CreateQuadChild("Plate", 0f);
            _plateRend = _plate.GetComponent<Renderer>();

            _fill = CreateQuadChild("Fill", -0.004f);
            _fillRend = _fill.GetComponent<Renderer>();

            if (label != null)
            {
                var face = CreateQuadChild("Face", -0.008f);
                _faceRend = face.GetComponent<Renderer>();
                _faceRend.sharedMaterial = NewUnlitMaterial(label);
            }
            else
            {
                BuildTextLabel();
            }
        }

        /// <summary>Rótulo de texto con TextMesh legacy, el mismo recurso que usa el HUD.</summary>
        private void BuildTextLabel()
        {
            var go = new GameObject("Text");
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, -0.008f);
            go.transform.localRotation = Quaternion.identity; // el TextMesh ya se lee desde −Z

            var tm = go.AddComponent<TextMesh>(); // agrega MeshRenderer por RequireComponent
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                       ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font != null)
            {
                tm.font = font;
                go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }

            tm.text = text;
            tm.fontSize = 90;
            tm.characterSize = size.y * 0.042f; // escala con el botón
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.white;

            _textMesh = tm;
            _faceRend = go.GetComponent<MeshRenderer>();
        }

        private Transform CreateQuadChild(string name, float zOffset)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            var c = go.GetComponent<Collider>();
            if (c != null) Destroy(c);
            go.layer = gameObject.layer;

            // Unlit y doble cara (Sprites/Default tiene Cull Off): se lee bien en VR sin depender
            // de que haya una luz apuntándole, y no se pierde si el botón queda de espaldas.
            go.GetComponent<MeshRenderer>().sharedMaterial = NewUnlitMaterial(null);

            var t = go.transform;
            t.SetParent(transform, false);
            t.localPosition = new Vector3(0f, 0f, zOffset);
            t.localRotation = Quaternion.identity;
            t.localScale = new Vector3(size.x, size.y, 1f);
            return t;
        }

        private static Material NewUnlitMaterial(Texture tex)
        {
            var sh = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Texture");
            var m = new Material(sh);
            if (tex != null) m.mainTexture = tex;
            return m;
        }

        /// <summary>
        /// Marca que algo está apuntando al botón <b>este frame</b>. Es la única señal que llena la
        /// barra, y la comparten el chorro y el <see cref="LaserPointer"/>: cuánto carga eso lo
        /// decide <see cref="Update"/> contra el reloj.
        /// </summary>
        public void Hold()
        {
            if (!_interactable || Time.time < _lockedUntil) return;
            _lastHitTime = Time.time;
        }

        /// <summary>
        /// Acciona el botón ya, sin esperar el llenado. Lo usa el gatillo del láser.
        /// Devuelve false si el botón está gris o todavía en el bloqueo del rearme.
        /// </summary>
        public bool TryPress()
        {
            if (!_interactable || Time.time < _lockedUntil) return false;
            Press();
            return true;
        }

        /// <summary>
        /// Marca el botón como apuntado por el láser: la placa se agranda y se tiñe, así se ve
        /// CUÁL está seleccionado antes de disparar. Lo llama <see cref="LaserPointer"/>.
        /// </summary>
        public void SetHovered(bool value)
        {
            if (_hovered == value) return;
            _hovered = value;
            UpdateVisual();
        }

        /// <summary>
        /// Recibe las partículas del chorro. <paramref name="other"/> es el GameObject del
        /// ParticleSystem que colisionó (la punta de la boquilla).
        ///
        /// Con un <see cref="LaserPointer"/> en escena el chorro deja de accionar botones: el cono
        /// se abre ~14°, así que a la distancia de un menú pega en varios carteles a la vez y no hay
        /// forma de saber cuál se va a accionar. Si NO hay láser se mantiene el comportamiento
        /// viejo, para que un error de cableado degrade en vez de dejar la app sin forma de apretar
        /// nada.
        /// </summary>
        private void OnParticleCollision(GameObject other)
        {
            if (LaserPointer.ActiveCount > 0) return;
            if (!_interactable || Time.time < _lockedUntil) return;

            var spray = other.GetComponent<ExtinguisherSpray>();
            if (spray == null || spray.System == null || !spray.IsSpraying) return;

            if (spray.System.GetCollisionEvents(gameObject, _events) > 0) Hold();
        }

        private void Update()
        {
            bool locked = Time.time < _lockedUntil;
            bool hit = _interactable && !locked && Time.time - _lastHitTime <= hitGraceSeconds;

            if (hit)
            {
                _charge += fillSeconds > 0f ? Time.deltaTime / fillSeconds : 1f;
                if (_charge >= 1f) Press();
            }
            else if (_charge > 0f)
            {
                _charge = Mathf.Max(0f, _charge - (emptySeconds > 0f ? Time.deltaTime / emptySeconds : 1f));
            }

            // Solo se redibuja cuando algo cambió: en el menú hay 8 botones y esto corre en Quest.
            float t = Charge01;
            if (!Mathf.Approximately(t, _drawnCharge) || _interactable != _drawnInteractable
                || _hovered != _drawnHovered)
                UpdateVisual();
        }

        private void Press()
        {
            _charge = 0f;
            _lastHitTime = float.NegativeInfinity; // si no, el chorro que sigue pegando recarga solo
            _lockedUntil = Time.time + rearmSeconds;

            if (haptics)
            {
                if (_handle == null) _handle = FindFirstObjectByType<ExtinguisherHandle>();
                if (_handle != null) _handle.Pulse(0.6f, 0.12f);
            }

            Debug.Log($"[SprayButton] '{name}' accionado.");
            onPressed?.Invoke();
            Pressed?.Invoke(this);
        }

        private void UpdateVisual()
        {
            if (_fill == null || _fillRend == null) return;

            float t = Charge01;
            _drawnCharge = t;
            _drawnInteractable = _interactable;
            _drawnHovered = _hovered;

            bool lit = _hovered && _interactable;
            SetColor(_plateRend, !_interactable ? disabledColor : (lit ? hoverColor : idleColor));

            // La placa está DETRÁS de la barra y del rótulo, así que agrandarla los deja intactos y
            // asoma alrededor como un borde. Más barato que un quad de contorno por botón.
            if (_plate != null)
            {
                float k = lit ? hoverScale : 1f;
                _plate.localScale = new Vector3(size.x * k, size.y * k, 1f);
            }

            // La barra crece desde abajo: se escala en Y y se compensa la posición.
            _fill.localScale = new Vector3(size.x, Mathf.Max(0.0001f, size.y * t), 1f);
            _fill.localPosition = new Vector3(0f, -size.y * 0.5f + size.y * t * 0.5f, -0.004f);
            _fillRend.enabled = _interactable && t > 0.001f;
            SetColor(_fillRend, fillColor);

            var faceColor = _interactable ? Color.white : new Color(0.6f, 0.6f, 0.6f);
            if (_textMesh != null) _textMesh.color = faceColor;
            else if (_faceRend != null && label != null) SetColor(_faceRend, faceColor);
        }

        private void SetColor(Renderer r, Color c)
        {
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, c);
            _mpb.SetColor(ColorId, c);
            r.SetPropertyBlock(_mpb);
        }
    }
}
