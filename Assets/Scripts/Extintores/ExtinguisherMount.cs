using UnityEngine;

namespace Extintores
{
    /// <summary>
    /// Acopla el extintor virtual al anchor de la mano/mando activo con un offset de
    /// calibración. El offset representa la pose del extintor REAL respecto del mando que
    /// lleva atado (caso passthrough con prop físico). Se deja expuesto y editable en
    /// runtime para calibrarlo más adelante.
    ///
    /// No usa física ni Rigidbody: el extintor es un tool-in-hand, se conduce por transform
    /// en LateUpdate (después de que OVR actualiza los anchors).
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class ExtinguisherMount : MonoBehaviour
    {
        public enum MountHand { Right, Left }

        [Header("Anclaje")]
        [Tooltip("Anchor del OVRCameraRig al que se acopla el extintor. " +
                 "Para el prop real: el anchor del mando de la mano que lo sostiene " +
                 "(p. ej. RightHandAnchor).")]
        [SerializeField] private Transform anchor;

        [Tooltip("Mano que sostiene el extintor. Determina el mando leído para trigger/háptica.")]
        [SerializeField] private MountHand hand = MountHand.Right;

        [Header("Offset de calibración (mando ↔ extintor real)")]
        [Tooltip("Posición del extintor relativa al anchor, en espacio local del anchor (metros). " +
                 "Se calibrará más adelante para casar con el prop físico.")]
        [SerializeField] private Vector3 positionOffset = Vector3.zero;

        [Tooltip("Rotación del extintor relativa al anchor (euler, grados).")]
        [SerializeField] private Vector3 rotationOffset = Vector3.zero;

        [Header("Referencias")]
        [Tooltip("Punto de agarre: dónde queda atado el mando sobre el extintor real. " +
                 "Es ESTE punto el que se alinea con el anchor, no el pivote del modelo " +
                 "(el FBX trae el pivote descentrado). Si queda vacío, se alinea la raíz.")]
        [SerializeField] private Transform gripPoint;

        [Tooltip("Punta de la boquilla: origen (+Z) del chorro. Contrato para el sistema de spray.")]
        [SerializeField] private Transform nozzleTip;

        [Header("Debug")]
        [SerializeField] private bool drawGizmos = true;

        // --- API pública ---
        public Transform Anchor { get => anchor; set => anchor = value; }
        public Transform NozzleTip => nozzleTip;
        public Transform GripPoint => gripPoint;
        public MountHand Hand => hand;

        /// <summary>Offset de posición actual (espacio local del anchor).</summary>
        public Vector3 PositionOffset { get => positionOffset; set => positionOffset = value; }

        /// <summary>Offset de rotación actual (euler, grados).</summary>
        public Vector3 RotationOffset { get => rotationOffset; set => rotationOffset = value; }

        /// <summary>Punto de entrada para el futuro sistema de calibración en runtime.</summary>
        public void SetOffset(Vector3 localPosition, Vector3 eulerRotation)
        {
            positionOffset = localPosition;
            rotationOffset = eulerRotation;
        }

        private void LateUpdate()
        {
            if (anchor == null) return;

            // Pose objetivo: dónde debe quedar el punto de agarre del extintor.
            Vector3 targetPos = anchor.TransformPoint(positionOffset);
            Quaternion targetRot = anchor.rotation * Quaternion.Euler(rotationOffset);

            if (gripPoint == null)
            {
                // Sin punto de agarre: se alinea la raíz (queda expuesto el pivote del FBX).
                transform.SetPositionAndRotation(targetPos, targetRot);
                return;
            }

            // Pose del grip relativa a la raíz (constante: es un hijo).
            Quaternion gripLocalRot = Quaternion.Inverse(transform.rotation) * gripPoint.rotation;
            Vector3 gripLocalPos = transform.InverseTransformPoint(gripPoint.position);

            // Resolvemos la pose de la raíz para que el grip caiga en la pose objetivo.
            Quaternion rootRot = targetRot * Quaternion.Inverse(gripLocalRot);
            Vector3 rootPos = targetPos - rootRot * gripLocalPos;

            transform.SetPositionAndRotation(rootPos, rootRot);
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos) return;

            // Dirección del chorro (+Z de la boquilla).
            if (nozzleTip != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawRay(nozzleTip.position, nozzleTip.forward * 0.5f);
                Gizmos.DrawWireSphere(nozzleTip.position, 0.01f);
            }

            // Punto de agarre: lo que se alinea con el mando.
            if (gripPoint != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(gripPoint.position, 0.03f);
            }

            if (anchor != null && gripPoint != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(anchor.position, gripPoint.position);
            }
        }
    }
}
