using UnityEngine;

namespace Extintores
{
    /// <summary>
    /// Puntero láser para accionar los <see cref="SprayButton"/>. Sale de la punta de la boquilla,
    /// con el mismo origen y dirección que el chorro, así apuntar se siente igual en el menú que
    /// frente a un fuego.
    ///
    /// Existe porque el chorro es un instrumento pésimo para elegir: el cono se abre ~14°, así que
    /// a la distancia de un menú <b>las partículas pegan en varios carteles a la vez</b> y no hay
    /// forma de saber cuál se va a accionar. Un rayo pega en uno solo, siempre.
    ///
    /// <b>Apuntar y disparar son dos actos distintos.</b> El rayo sólo señala; para accionar el
    /// botón hay que <b>disparar el extintor</b> (el grip, el mismo gesto que apaga un fuego) con el
    /// rayo encima, y sostenerlo hasta que se llene la barra
    /// (<see cref="SprayButton.Hold"/>, ~⅓ de segundo). Apoyar el rayo por sí solo no hace nada:
    /// si bastara con posarlo, cruzar la mirada por el menú activaría cosas sin querer.
    ///
    /// <b>Cuándo se ve el rayo:</b> en los menús (lobby, y "Continuar" con el fuego ya apagado) se ve
    /// <b>siempre</b>, para poder apuntar. Mientras hay fuego se oculta y aparece sólo sobre un botón
    /// accionable (el panel de extintores): un láser permanente es ruido visual al apagar.
    ///
    /// <b>Tras accionar un botón hay que soltar el gatillo</b> antes de cargar otro. Al cambiar de
    /// menú aparece un botón nuevo bajo el rayo (VOLVER justo debajo de PERSONALIZADO) y, con el
    /// gatillo todavía apretado, se accionaba solo.
    ///
    /// Mientras haya un LaserPointer activo, el chorro <b>deja</b> de accionar botones por colisión
    /// de partículas (<see cref="ActiveCount"/>). Si no hay ninguno, el chorro sigue funcionando
    /// como antes: así un error de cableado degrada a la interacción vieja en vez de dejar la app
    /// sin manera de apretar nada.
    /// </summary>
    [DefaultExecutionOrder(55)] // después del chorro (50), antes de los botones (60)
    public class LaserPointer : MonoBehaviour
    {
        /// <summary>Cuántos punteros activos hay. Lo consulta <see cref="SprayButton"/>.</summary>
        public static int ActiveCount { get; private set; }

        [Tooltip("De dónde sale el rayo. Si queda vacío, este mismo transform (el +Z es la dirección).")]
        [SerializeField] private Transform origin;
        [Tooltip("Chorro cuyo gatillo acciona el botón apuntado. Si queda vacío se busca en este " +
                 "objeto y después en la escena.")]
        [SerializeField] private ExtinguisherSpray spray;

        [Header("Alcance")]
        /// <summary>Alcance mínimo: desde el centro del patio los elementos quedan a ~8,7 m.</summary>
        public const float MinReach = 12f;

        [Tooltip("Largo máximo del rayo (m). Desde el centro del patio hay que llegar a los elementos " +
                 "de Personalizado, que están a ~8,7 m.")]
        [SerializeField] private float maxDistance = MinReach;
        [Tooltip("Contra qué layers apunta. En 'Nothing' usa la layer de los botones.")]
        [SerializeField] private LayerMask hits = 0;

        [Header("Aspecto")]
        [SerializeField] private float width = 0.005f;
        // Nombres nuevos a propósito: los viejos ('idle'/'hover') quedaron serializados en la escena
        // con el alfa de cuando el rayo tenía que ser tenue. Ahora se usa para apuntar a propósito
        // (menús, o sobre un botón), así que tiene que verse fuerte — y renombrar el campo es la forma
        // de que la escena tome el valor nuevo sin editar el YAML a mano.
        [Tooltip("Color del rayo apuntando a un botón, sin disparar todavía.")]
        [SerializeField] private Color aimColor = new Color(0.45f, 0.78f, 1f, 0.9f);
        [Tooltip("Color del rayo mientras el disparo del extintor lo está accionando.")]
        [SerializeField] private Color firingColor = new Color(0.30f, 0.95f, 0.45f, 1f);
        [Tooltip("Diámetro del punto que marca dónde pega el rayo (m).")]
        [SerializeField] private float dotSize = 0.035f;

        /// <summary>Botón que el rayo está tocando ahora, o null.</summary>
        public SprayButton Target { get; private set; }

        private LineRenderer _line;
        private Transform _dot;
        private Renderer _dotRend;
        // Dos materiales: el del rayo queda BLANCO y el color lo pone el vertex color
        // (start/endColor); si además se tiñera el material, el alfa se aplicaría dos veces.
        private Material _lineMaterial;
        private Material _dotMaterial;
        private TrainingSession _session;
        private bool _waitRelease;

        private void Awake()
        {
            if (origin == null) origin = transform;
            if (spray == null) spray = GetComponent<ExtinguisherSpray>();
            if (spray == null) spray = FindFirstObjectByType<ExtinguisherSpray>();
            if (spray == null)
                Debug.LogWarning("[LaserPointer] No encontré el ExtinguisherSpray: sin su gatillo " +
                                 "no hay forma de accionar los botones apuntados.");
            if (hits.value == 0)
            {
                int l = LayerMask.NameToLayer(SprayButton.LayerName);
                if (l >= 0) hits = 1 << l;
                else Debug.LogWarning($"[LaserPointer] Falta la layer '{SprayButton.LayerName}': " +
                                      "el rayo no va a encontrar ningún botón.");
            }
            _session = FindFirstObjectByType<TrainingSession>();
            BuildVisual();
        }

        private void OnEnable()
        {
            ActiveCount++;
            SprayButton.AnyPressed += OnAnyPressed;
        }

        private void OnDisable()
        {
            SprayButton.AnyPressed -= OnAnyPressed;
            ActiveCount = Mathf.Max(0, ActiveCount - 1);
            SetTarget(null);
            Hide();
        }

        private void BuildVisual()
        {
            // Unlit y de una sola pasada: se ve igual con o sin luces, y no depende de ningún asset.
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            _lineMaterial = new Material(shader);
            _dotMaterial = new Material(shader);

            var go = new GameObject("Beam");
            go.transform.SetParent(transform, false);
            _line = go.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.positionCount = 2;
            _line.widthMultiplier = width;
            _line.sharedMaterial = _lineMaterial;
            _line.alignment = LineAlignment.View; // siempre de cara: un rayo fino no se pierde
            _line.numCapVertices = 0;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;

            // Esfera en vez de quad: no hay que orientarla a la cámara a mano.
            var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dot.name = "Dot";
            var col = dot.GetComponent<Collider>();
            if (col != null) Destroy(col); // si no, el propio rayo se choca con su punto
            dot.transform.SetParent(transform, false);
            dot.transform.localScale = Vector3.one * dotSize;
            _dotRend = dot.GetComponent<Renderer>();
            _dotRend.sharedMaterial = _dotMaterial;
            _dotRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _dotRend.receiveShadows = false;
            _dot = dot.transform;
        }

        private void Update()
        {
            if (origin == null || _line == null) return;

            Vector3 from = origin.position;
            Vector3 dir = origin.forward;
            Vector3 end = from + dir * maxDistance;

            bool firing = spray != null && spray.IsSpraying;
            if (!firing) _waitRelease = false;

            SprayButton found = null;
            // Collide y no Ignore: el fondo del panel de extintores es un collider TRIGGER (así el
            // chorro lo atraviesa y sigue llegando al fuego) y el rayo tiene que verse sobre todo el
            // tablero, no sólo encima de un botón.
            bool aimingAtSomething = Physics.Raycast(from, dir, out RaycastHit hit, maxDistance, hits,
                                                     QueryTriggerInteraction.Collide);
            if (aimingAtSomething)
            {
                end = hit.point;
                var button = hit.collider.GetComponentInParent<SprayButton>();
                if (button != null && button.Interactable) found = button;
            }

            SetTarget(found);
            if (Target == null)
            {
                if (MenuContext || aimingAtSomething) Draw(from, end, false, dot: false);
                else Hide();
                return;
            }

            // El rayo señala; el grip del extintor es el que acciona. Hold() llena la barra, así que
            // hay que sostener el disparo ~fillSeconds sobre el botón — y, si se acaba de accionar
            // otro, soltar primero.
            bool charging = firing && !_waitRelease;
            if (charging) Target.Hold();

            Draw(from, end, charging, dot: true);
        }

        /// <summary>
        /// Hay menú y no fuego: fuera de sesión (lobby) o con el fuego apagado esperando "Continuar".
        /// Sin <see cref="TrainingSession"/> (escena sandbox) se comporta como con fuego.
        /// </summary>
        private bool MenuContext =>
            _session != null && (!_session.InSession || _session.AwaitingContinue);

        private void OnAnyPressed(SprayButton _)
        {
            if (spray != null && spray.IsSpraying) _waitRelease = true;
        }

        private void Draw(Vector3 from, Vector3 end, bool firing, bool dot)
        {
            var c = firing ? firingColor : aimColor;

            _line.enabled = true;
            _line.SetPosition(0, from);
            _line.SetPosition(1, end);
            _line.startColor = c;
            _line.endColor = c;

            _dot.position = end;
            _dotRend.enabled = dot;
            if (_dotMaterial != null) _dotMaterial.color = c;
        }

        /// <summary>
        /// Cambia de botón apuntado, apagando el resaltado del anterior. Sin esto un botón queda
        /// iluminado para siempre en cuanto el rayo se va a otro lado.
        /// </summary>
        private void SetTarget(SprayButton button)
        {
            if (Target == button) return;
            if (Target != null) Target.SetHovered(false);
            Target = button;
            if (Target != null) Target.SetHovered(true);
        }

        /// <summary>Sin botón apuntado no se dibuja nada: un rayo permanente estorba al apagar fuego.</summary>
        private void Hide()
        {
            if (_line != null) _line.enabled = false;
            if (_dotRend != null) _dotRend.enabled = false;
        }

        private void OnDestroy()
        {
            if (_lineMaterial != null) Destroy(_lineMaterial);
            if (_dotMaterial != null) Destroy(_dotMaterial);
        }
    }
}
