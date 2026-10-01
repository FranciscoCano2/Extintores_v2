using System;
using System.Collections;
using UnityEngine;

namespace Extintores
{
    /// <summary>
    /// Estado e input del extintor sostenido, multimodal (mando Touch / hand tracking).
    /// Expone el valor del gatillo y si está "activo" (con pose válida en la mano),
    /// además de un pulso háptico. No implementa la lógica del chorro: solo el contrato
    /// (TriggerValue, IsHeld) que consumirá el Bloque B.
    /// </summary>
    [RequireComponent(typeof(ExtinguisherMount))]
    public class ExtinguisherHandle : MonoBehaviour
    {
        [SerializeField] private ExtinguisherMount mount;

        [Header("Háptica")]
        [SerializeField, Range(0f, 1f)] private float grabHapticAmplitude = 0.5f;
        [SerializeField] private float grabHapticDuration = 0.08f;

        /// <summary>Presión del gatillo/pinch en 0..1. Consumido por el sistema de spray.</summary>
        public float TriggerValue { get; private set; }

        /// <summary>True si el extintor tiene una pose válida en la mano (mando o mano rastreada).</summary>
        public bool IsHeld { get; private set; }

        /// <summary>Se dispara al cambiar el estado sostenido (true = tomado).</summary>
        public event Action<bool> HeldChanged;

        private OVRInput.Controller _controllerMask;
        private OVRInput.Hand _handMask;

        private void Reset() => mount = GetComponent<ExtinguisherMount>();

        private void Awake()
        {
            if (mount == null) mount = GetComponent<ExtinguisherMount>();

            bool right = mount.Hand == ExtinguisherMount.MountHand.Right;
            _controllerMask = right ? OVRInput.Controller.RTouch : OVRInput.Controller.LTouch;
            _handMask = right ? OVRInput.Hand.HandRight : OVRInput.Hand.HandLeft;
        }

        private void Update()
        {
            // Nota (ver CLAUDE.md): ante pérdida de tracking puede reportarse ControllerNotInHand
            // falsamente. Aquí solo controla visibilidad/estado, nada irreversible.
            var inHandState = OVRInput.GetControllerIsInHandState(_handMask);
            SetHeld(inHandState != OVRInput.ControllerInHandState.NoHand);

            // Gatillo por mando. El pinch de hand tracking se conectará en el bloque multimodal.
            TriggerValue = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, _controllerMask);
        }

        private void SetHeld(bool value)
        {
            if (value == IsHeld) return;
            IsHeld = value;
            if (value) Pulse(grabHapticAmplitude, grabHapticDuration);
            HeldChanged?.Invoke(value);
        }

        /// <summary>Pulso háptico en el mando de la mano que sostiene el extintor.</summary>
        public void Pulse(float amplitude, float duration)
        {
            StopAllCoroutines();
            StartCoroutine(PulseRoutine(amplitude, duration));
        }

        private IEnumerator PulseRoutine(float amplitude, float duration)
        {
            Haptics.Set(1f, amplitude, _controllerMask);
            yield return new WaitForSeconds(duration);
            Haptics.Stop(_controllerMask);
        }

        private void OnDisable()
        {
            Haptics.Stop(_controllerMask);
        }
    }
}
