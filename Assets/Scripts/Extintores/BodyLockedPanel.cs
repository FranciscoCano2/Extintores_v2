using UnityEngine;

namespace Extintores
{
    /// <summary>
    /// Hace que un cartel acompañe al usuario <b>sin ir pegado a la cabeza</b>: queda quieto en el
    /// mundo mientras mirás alrededor y recién se reacomoda —despacio— cuando la mirada se va más
    /// de <see cref="deadZoneDegrees"/>.
    ///
    /// Existe porque el <see cref="TrainingHud"/> colgaba directamente del <c>CenterEyeAnchor</c>:
    /// eso lo clava a la cara, así que gira exactamente con vos y <b>nunca podés mirarlo de
    /// frente</b> —siempre queda en el mismo rincón del ojo, y en VR además marea—. Un panel
    /// "body-locked" con zona muerta es el patrón de confort habitual: te espera donde lo dejaste y
    /// te sigue sólo cuando de verdad te diste vuelta.
    ///
    /// La cara legible del cartel es su <b>−Z</b> (convención de TextMesh/Quad de Unity, la misma de
    /// <see cref="SprayButton"/>), así que el panel apunta su +Z en la dirección en la que mira el
    /// usuario, no hacia él.
    ///
    /// <b>Modo esquina</b> (<see cref="Corner"/>): el cartel chico va en un rincón de la vista y la
    /// sigue (yaw y pitch) con suavizado. Sigue sin colgar del <c>CenterEyeAnchor</c>: el retraso del
    /// seguimiento es lo que evita que se sienta pegado a la cara. Es para información secundaria
    /// que no hace falta leer de frente (tiempo y nivel).
    /// </summary>
    [DefaultExecutionOrder(100)] // después de que el rig haya movido la cabeza este frame
    public class BodyLockedPanel : MonoBehaviour
    {
        [Tooltip("La cabeza. Si queda vacío se usa Camera.main (el CenterEyeAnchor del OVRCameraRig).")]
        [SerializeField] private Transform head;

        [Header("Colocación")]
        [Tooltip("A qué distancia de la cabeza se planta el cartel (m).")]
        [SerializeField] private float distance = 1.5f;
        [Tooltip("Altura respecto de los ojos (m). Negativo = por debajo de la línea de la mirada.")]
        [SerializeField] private float heightOffset = -0.45f;
        [Tooltip("Corrimiento lateral (m). Negativo = a la izquierda.")]
        [SerializeField] private float sideOffset = 0f;

        [Header("Seguimiento")]
        [Tooltip("Cuántos grados podés girar la cabeza antes de que el cartel se mueva. Mientras " +
                 "estés dentro de este margen queda clavado en el mundo y lo podés leer tranquilo.")]
        [SerializeField, Range(0f, 90f)] private float deadZoneDegrees = 35f;
        [Tooltip("Velocidad a la que se reacomoda una vez que se pasó la zona muerta (grados/s).")]
        [SerializeField] private float turnSpeed = 140f;
        [Tooltip("Suavizado del desplazamiento (m/s). El giro y la posición van juntos.")]
        [SerializeField] private float moveSpeed = 3f;
        [Tooltip("Si la cabeza aparece a más de esta distancia, se recoloca de una (cambio de " +
                 "puesto: TrainingSession teletransporta el rig y el cartel no debe viajar volando).")]
        [SerializeField] private float snapDistance = 1.5f;

        [Header("Modo esquina")]
        [Tooltip("Dónde va el cartel chico respecto de la cabeza: X lateral (negativo = izquierda), " +
                 "Y sobre la línea de los ojos, Z al frente.")]
        [SerializeField] private Vector3 cornerOffset = new Vector3(-0.22f, 0.16f, 0.8f);
        [Tooltip("Qué tan rápido alcanza a la mirada (1/s). Más alto = más pegado a la cabeza.")]
        [SerializeField] private float cornerFollowSpeed = 10f;

        private float _yaw;
        private bool _chasing;
        private bool _corner;

        /// <summary>
        /// Cartel chico en un rincón de la vista (true) o cartel con zona muerta delante (false). Al
        /// cambiar de modo se recoloca de una: no viaja volando por el campo visual.
        /// </summary>
        public bool Corner
        {
            get => _corner;
            set
            {
                if (_corner == value) return;
                _corner = value;
                Snap();
            }
        }

        private void Awake()
        {
            if (head == null)
            {
                var cam = Camera.main;
                if (cam != null) head = cam.transform;
            }
            if (head == null)
                Debug.LogWarning("[BodyLockedPanel] No encontré la cabeza (Camera.main): el cartel " +
                                 "se queda donde esté.");
        }

        private void OnEnable() => Snap();

        /// <summary>Recoloca el cartel delante de la cabeza sin animación.</summary>
        public void Snap()
        {
            if (head == null) return;
            _yaw = head.eulerAngles.y;
            _chasing = false;
            if (_corner) transform.SetPositionAndRotation(CornerPosition(), CornerRotation());
            else transform.SetPositionAndRotation(TargetPosition(_yaw), Quaternion.Euler(0f, _yaw, 0f));
        }

        private void LateUpdate()
        {
            if (head == null) return;

            if (_corner)
            {
                if ((transform.position - CornerPosition()).sqrMagnitude > snapDistance * snapDistance)
                {
                    Snap();
                    return;
                }
                float k = 1f - Mathf.Exp(-cornerFollowSpeed * Time.deltaTime);
                transform.position = Vector3.Lerp(transform.position, CornerPosition(), k);
                transform.rotation = Quaternion.Slerp(transform.rotation, CornerRotation(), k);
                return;
            }

            // Un salto grande (cambio de puesto) no se persigue: se aparece ya colocado.
            if ((transform.position - TargetPosition(_yaw)).sqrMagnitude > snapDistance * snapDistance)
            {
                Snap();
                return;
            }

            float headYaw = head.eulerAngles.y;
            float off = Mathf.DeltaAngle(_yaw, headYaw);

            // Histéresis: se engancha al salir de la zona muerta y no se suelta hasta alcanzarla.
            // Sin esto el cartel arrancaría y frenaría en el borde, temblando.
            if (!_chasing && Mathf.Abs(off) > deadZoneDegrees) _chasing = true;
            if (_chasing)
            {
                _yaw = Mathf.MoveTowardsAngle(_yaw, headYaw, turnSpeed * Time.deltaTime);
                if (Mathf.Abs(Mathf.DeltaAngle(_yaw, headYaw)) < 0.5f) _chasing = false;
            }

            transform.position = Vector3.MoveTowards(transform.position, TargetPosition(_yaw),
                                                     moveSpeed * Time.deltaTime);
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
        }

        /// <summary>Rincón de la vista: sigue yaw y pitch de la cabeza, pero nunca el roll.</summary>
        private Quaternion CornerRotation()
        {
            var e = head.eulerAngles;
            return Quaternion.Euler(e.x, e.y, 0f);
        }

        private Vector3 CornerPosition() => head.position + CornerRotation() * cornerOffset;

        /// <summary>Dónde va el cartel para un yaw dado. Siempre a nivel: nunca se inclina.</summary>
        private Vector3 TargetPosition(float yaw)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            return head.position + rot * new Vector3(sideOffset, heightOffset, distance);
        }
    }
}
