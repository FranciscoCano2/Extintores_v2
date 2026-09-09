using System.Collections;
using UnityEngine;

namespace Extintores
{
    /// <summary>
    /// Una bahía de la pared de recarga: representa UN tipo de extintor (color + letras + agente).
    /// Cuando el extintor sostenido (<see cref="ExtinguisherSwappable"/>) se mantiene dentro de su
    /// volumen-trigger durante <see cref="dwellSeconds"/>, cambia el tipo del extintor de la mano.
    ///
    /// El trigger va en la layer <b>Interactor</b> (NO 'Fire', para no interferir con el chorro).
    /// Se auto-configura: fuerza <c>isTrigger</c>, agrega un Rigidbody kinemático (los eventos de
    /// trigger necesitan un Rigidbody en alguno de los dos colliders y el tanque se mueve por
    /// transform) y setea la layer Interactor si existe.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ExtinguisherBay : MonoBehaviour
    {
        [Header("Tipo de extintor de esta bahía")]
        [SerializeField] private AgentType agent = AgentType.DryChemABC;
        [Tooltip("Color con el que se tiñe el tanque al elegir esta bahía (si Swappable.tintBody).")]
        [SerializeField] private Color bodyColor = new Color(0.75f, 0.1f, 0.1f);
        [Tooltip("Etiqueta que se pone en el extintor al elegir esta bahía (opcional).")]
        [SerializeField] private Texture2D label;

        [Header("Interacción")]
        [Tooltip("Segundos que hay que sostener el extintor en la bahía para cambiar (evita swaps " +
                 "accidentales al pasar cerca).")]
        [SerializeField] private float dwellSeconds = 0.3f;
        [SerializeField] private bool haptics = true;

        public AgentType Agent => agent;
        public Color BodyColor => bodyColor;
        public Texture2D Label => label;

        /// <summary>
        /// Si es <c>false</c> la bahía no entrega su extintor. Lo usa el modo tutorial para dejar
        /// disponibles solo los agentes efectivos contra el fuego actual; <see cref="BayHighlighter"/>
        /// las pinta grises. Por defecto todas están habilitadas.
        /// </summary>
        public bool Interactable { get; set; } = true;

        private ExtinguisherSwappable _candidate;
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

            if (sw.CurrentAgent != agent)
            {
                sw.SetAgent(agent, bodyColor, label);
                if (haptics) StartCoroutine(Pulse());
                Debug.Log($"[ExtinguisherBay] Extintor cambiado a {agent}.");
            }
            _candidate = null; // ya aplicado; hay que salir y re-entrar para otro cambio
        }

        private void OnTriggerExit(Collider other)
        {
            var sw = other.GetComponentInParent<ExtinguisherSwappable>();
            if (sw == _candidate) _candidate = null;
        }

        private static IEnumerator Pulse()
        {
            OVRInput.SetControllerVibration(0.4f, 0.6f, OVRInput.Controller.RTouch);
            yield return new WaitForSeconds(0.12f);
            OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
        }
    }
}
