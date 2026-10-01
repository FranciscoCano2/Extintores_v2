using System.Collections;
using UnityEngine;

namespace Extintores
{
    /// <summary>
    /// Una bahía de la pared de recarga: representa UN tipo de extintor (color + letras + agente).
    ///
    /// Se elige de dos maneras, y las dos hacen exactamente lo mismo (<see cref="Select"/>):
    /// <list type="bullet">
    ///   <item><b>Con el chorro</b>, apuntándole al <see cref="selector"/> de la bahía. Es la forma
    ///         normal: funciona a distancia y es el mismo gesto que el resto de la app (menú,
    ///         "Continuar"), así no hay dos vocabularios de interacción para aprender.</item>
    ///   <item><b>Metiendo el tanque</b> en el volumen-trigger y sosteniéndolo
    ///         <see cref="dwellSeconds"/>. Queda como gesto realista para quien esté al lado del
    ///         panel.</item>
    /// </list>
    ///
    /// El trigger va en la layer <b>Interactor</b> (NO 'Fire', para no interferir con el chorro).
    /// Se auto-configura: fuerza <c>isTrigger</c>, agrega un Rigidbody kinemático (los eventos de
    /// trigger necesitan un Rigidbody en alguno de los dos colliders y el tanque se mueve por
    /// transform) y setea la layer Interactor si existe. El botón, en cambio, vive en la layer
    /// <see cref="SprayButton.LayerName"/>: por eso es un HIJO y no un componente más de la bahía.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ExtinguisherBay : MonoBehaviour
    {
        [Header("Tipo de extintor de esta bahía")]
        [SerializeField] private AgentType agent = AgentType.DryChemABC;
        [Tooltip("Etiqueta que se pone en el extintor al elegir esta bahía (opcional).")]
        [SerializeField] private Texture2D label;

        [Header("Interacción")]
        [Tooltip("Botón que selecciona esta bahía con el chorro. Lo crea el WallBuilder como hijo " +
                 "'Selector'. Si queda vacío se busca entre los hijos.")]
        [SerializeField] private SprayButton selector;
        [Tooltip("Segundos que hay que sostener el extintor en la bahía para cambiar (evita swaps " +
                 "accidentales al pasar cerca).")]
        [SerializeField] private float dwellSeconds = 0.3f;
        [SerializeField] private bool haptics = true;

        public AgentType Agent => agent;
        /// <summary>Color de este tipo de extintor. Sale de la tabla común, no de un campo propio:
        /// serializarlo dejaba que el marco de la bahía y el tanque discreparan.</summary>
        public Color BodyColor => AgentColor.Of(agent);
        public Texture2D Label => label;

        /// <summary>
        /// Si es <c>false</c> la bahía no entrega su extintor. Lo usa el modo tutorial para dejar
        /// disponibles solo los agentes efectivos contra el fuego actual; <see cref="BayHighlighter"/>
        /// las pinta grises. Por defecto todas están habilitadas.
        ///
        /// Apaga también el botón del chorro: si sólo bloqueara el trigger, el tutorial se saltearía
        /// entero apuntándole al botón de la bahía equivocada, sin ningún error que lo delate.
        /// </summary>
        public bool Interactable
        {
            get => _interactable;
            set
            {
                _interactable = value;
                if (selector != null) selector.Interactable = value;
            }
        }

        private bool _interactable = true;
        private ExtinguisherSwappable _candidate;
        private ExtinguisherSwappable _swappable;
        private float _readyAt;

        private void Reset()
        {
            var c = GetComponent<Collider>();
            if (c != null) c.isTrigger = true;
        }

        private void Awake()
        {
            var c = GetComponent<Collider>();
            if (c != null) c.isTrigger = true;

            var rb = GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            int l = LayerMask.NameToLayer("Interactor");
            if (l >= 0) gameObject.layer = l;

            // El botón cuelga de la bahía: así el WallBuilder no tiene que cablear nada y una bahía
            // arrastrada a mano a la escena sigue funcionando.
            if (selector == null) selector = GetComponentInChildren<SprayButton>(true);
            if (selector != null)
            {
                selector.Pressed += _ => Select();
                selector.Interactable = _interactable;
            }
        }

        /// <summary>
        /// Entrega el extintor de esta bahía al tanque que sostiene el usuario. Es el punto único al
        /// que llegan las dos formas de elegir (chorro y tanque-en-la-bahía).
        /// </summary>
        public void Select()
        {
            if (!Interactable) return;

            if (_swappable == null) _swappable = FindFirstObjectByType<ExtinguisherSwappable>();
            if (_swappable == null)
            {
                Debug.LogWarning($"[ExtinguisherBay] '{name}': no hay ningún ExtinguisherSwappable " +
                                 "en la escena, no tengo a quién cambiarle el agente.");
                return;
            }

            Apply(_swappable);
        }

        private void Apply(ExtinguisherSwappable sw)
        {
            if (sw.CurrentAgent == agent) return;

            sw.SetAgent(agent, label);
            if (haptics) StartCoroutine(Pulse());
            Debug.Log($"[ExtinguisherBay] Extintor cambiado a {agent}.");
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!Interactable) return;
            var sw = other.GetComponentInParent<ExtinguisherSwappable>();
            if (sw == null) return;
            _candidate = sw;
            _readyAt = Time.time + dwellSeconds;
        }

        private void OnTriggerStay(Collider other)
        {
            if (!Interactable || _candidate == null) return;
            var sw = other.GetComponentInParent<ExtinguisherSwappable>();
            if (sw != _candidate || Time.time < _readyAt) return;

            Apply(sw);
            _swappable = sw;
            _candidate = null; // ya aplicado; hay que salir y re-entrar para otro cambio
        }

        private void OnTriggerExit(Collider other)
        {
            var sw = other.GetComponentInParent<ExtinguisherSwappable>();
            if (sw == _candidate) _candidate = null;
        }

        private static IEnumerator Pulse()
        {
            Haptics.Set(0.4f, 0.6f, OVRInput.Controller.RTouch);
            yield return new WaitForSeconds(0.12f);
            Haptics.Stop(OVRInput.Controller.RTouch);
        }
    }
}
