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

        [Tooltip("Si está activo, el extintor sigue la pose del MANDO FÍSICO (RTouch/LTouch) " +
                 "en vez de la del anchor. Imprescindible en multimodal: RightHandAnchor sigue " +
                 "el input 'activo' dando prioridad a la mano rastreada, no al mando. Como el " +
                 "prop real lleva el mando atado, hay que seguir el mando siempre.")]
        [SerializeField] private bool followController = true;

        [Tooltip("Espacio de tracking del OVRCameraRig (nodo 'TrackingSpace'), en el que están " +
                 "definidas las poses de OVRInput. Si queda vacío se usa el padre del anchor " +
                 "(RightHandAnchor cuelga de TrackingSpace), que es lo correcto para este rig.")]
        [SerializeField] private Transform trackingSpace;

        [Header("Offset de calibración (mando ↔ extintor real)")]
        [Tooltip("Posición del extintor relativa al anchor, en espacio local del anchor (metros). " +
                 "Se calibrará más adelante para casar con el prop físico.")]
        [SerializeField] private Vector3 positionOffset = Vector3.zero;

        [Tooltip("Rotación del extintor relativa al anchor (euler, grados).")]
        [SerializeField] private Vector3 rotationOffset = Vector3.zero;

        [Header("Ajuste del usuario (menú Configuración)")]
        [Tooltip("Qué ajuste de GameSettings aplica a este objeto. Los dos reciben deslizamiento " +
                 "sobre su eje, desplazamiento transversal y rotación en los 3 ejes alrededor del " +
                 "agarre. Tanque: eje +Y (altura de la abrazadera). Boquilla: eje +Z.")]
        [SerializeField] private UserSlide userSlide = UserSlide.ByHand;

        [Tooltip("Eje sobre el que se desliza, en el espacio del punto de agarre (o de la raíz si no " +
                 "hay). Tanque: +Y, su eje largo. Boquilla: +Z, hacia donde sale el chorro.")]
        [SerializeField] private Vector3 slideAxis = Vector3.zero;

        public enum UserSlide
        {
            /// <summary>Mano derecha = tanque, izquierda = boquilla (el esquema a dos manos).</summary>
            ByHand,
            None,
            TankHeight,
            NozzleOffset,
        }

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
            // Pose base del "anclaje": o la del mando físico (multimodal) o la del anchor.
            if (!TryGetAnchorPose(out Vector3 anchorPos, out Quaternion anchorRot)) return;

            // Pose objetivo: dónde debe quedar el punto de agarre del extintor.
            Vector3 targetPos = anchorPos + anchorRot * positionOffset;
            Quaternion targetRot = anchorRot * Quaternion.Euler(rotationOffset);

            // Ajuste del usuario, encima de la calibración de fábrica, en tres pasos:
            //  1. Inclinación (los dos ejes transversales): como targetRot/targetPos son la pose del
            //     AGARRE, hace pivotar el prop alrededor de la abrazadera, que es por donde está unido
            //     al mando.
            //  2. Deslizamiento sobre el eje y desplazamiento transversal, en el espacio ya inclinado:
            //     ubican el eje del prop respecto del mando.
            //  3. Giro sobre el eje propio, AL FINAL: así gira el prop sobre sí mismo. Con un único
            //     Quaternion.Euler la Y del tanque quedaba por fuera de X/Z → con el tanque inclinado,
            //     tocar "ROTACIÓN Y (eje)" lo hacía describir un cono alrededor del eje del mando; y
            //     el desplazamiento, aplicado después del giro, hacía orbitar el eje alrededor del mando.
            UserRotation(out Quaternion tilt, out Quaternion spin);
            targetRot *= tilt;
            targetPos += targetRot * (SlideAxis() * SlideDistance() + UserShift());
            targetRot *= spin;

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

        private UserSlide ResolvedSlide() =>
            userSlide != UserSlide.ByHand ? userSlide
            : hand == MountHand.Right ? UserSlide.TankHeight : UserSlide.NozzleOffset;

        /// <summary>
        /// Rotación elegida en Configuración, partida en <paramref name="tilt"/> (los dos ejes
        /// transversales) y <paramref name="spin"/> (el giro sobre el eje propio: Y del tanque, Z de
        /// la boquilla). La composición la explica <see cref="LateUpdate"/>.
        /// </summary>
        private void UserRotation(out Quaternion tilt, out Quaternion spin)
        {
            switch (ResolvedSlide())
            {
                case UserSlide.TankHeight:
                    GameSettings.SplitTankRotation(GameSettings.TankRotation, out tilt, out spin);
                    return;
                case UserSlide.NozzleOffset:
                    Vector3 n = GameSettings.NozzleRotation;
                    tilt = Quaternion.Euler(n.x, n.y, 0f);
                    spin = Quaternion.AngleAxis(n.z, Vector3.forward);
                    return;
                default:
                    tilt = spin = Quaternion.identity;
                    return;
            }
        }

        /// <summary>
        /// Desplazamiento transversal elegido en Configuración: X/Z del tanque, X/Y de la boquilla
        /// (siempre perpendicular a su eje; el corrimiento sobre el eje es <see cref="SlideDistance"/>).
        /// </summary>
        private Vector3 UserShift()
        {
            switch (ResolvedSlide())
            {
                case UserSlide.TankHeight: return GameSettings.TankShift;
                case UserSlide.NozzleOffset: return GameSettings.NozzleShift;
                default: return Vector3.zero;
            }
        }

        private Vector3 SlideAxis()
        {
            if (slideAxis.sqrMagnitude > 1e-6f) return slideAxis.normalized;
            return ResolvedSlide() == UserSlide.NozzleOffset ? Vector3.forward : Vector3.up;
        }

        /// <summary>
        /// Cuánto se corre el prop sobre <see cref="SlideAxis"/>. Para el tanque el valor del menú es
        /// la altura del MANDO: si la abrazadera sube, el tanque queda más abajo respecto del mando,
        /// de ahí el signo.
        /// </summary>
        private float SlideDistance()
        {
            switch (ResolvedSlide())
            {
                case UserSlide.TankHeight: return -GameSettings.TankControllerHeight;
                case UserSlide.NozzleOffset: return GameSettings.NozzleOffset;
                default: return 0f;
            }
        }

        /// <summary>
        /// Pose base a la que se acopla el extintor. En modo <see cref="followController"/> es la
        /// pose del mando físico (RTouch/LTouch) expresada en mundo; si no, la del anchor.
        /// </summary>
        private bool TryGetAnchorPose(out Vector3 pos, out Quaternion rot)
        {
            if (followController)
            {
                // Espacio en el que OVRInput reporta las poses (TrackingSpace del rig).
                Transform space = trackingSpace != null ? trackingSpace
                                : anchor != null ? anchor.parent
                                : ResolveTrackingSpace();
                if (space != null)
                {
                    var mask = hand == MountHand.Right ? OVRInput.Controller.RTouch
                                                       : OVRInput.Controller.LTouch;
                    pos = space.TransformPoint(OVRInput.GetLocalControllerPosition(mask));
                    rot = space.rotation * OVRInput.GetLocalControllerRotation(mask);
                    return true;
                }
                // Sin espacio de tracking válido: caemos al anchor como respaldo.
            }

            if (anchor == null)
            {
                pos = default;
                rot = default;
                return false;
            }

            pos = anchor.position;
            rot = anchor.rotation;
            return true;
        }

        private Transform _autoSpace;

        /// <summary>Busca el TrackingSpace del OVRCameraRig si no se asignó anchor/trackingSpace.
        /// Permite montar un objeto (p. ej. la boquilla en la izquierda) sin cablear el anchor.</summary>
        private Transform ResolveTrackingSpace()
        {
            if (_autoSpace != null) return _autoSpace;
            var rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig != null) _autoSpace = rig.trackingSpace;
            return _autoSpace;
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
