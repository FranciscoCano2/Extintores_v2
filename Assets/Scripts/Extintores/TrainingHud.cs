using UnityEngine;

namespace Extintores
{
    /// <summary>
    /// HUD de entrenamiento en world-space, hecho con <see cref="TextMesh"/> (no depende de UGUI ni
    /// TextMeshPro). Muestra fuegos restantes, tiempo y agente actual, leídos de
    /// <see cref="TrainingManager"/>; parpadea "EXTINTOR EQUIVOCADO" al usar el agente incorrecto y
    /// muestra el cartel de éxito con el tiempo final.
    ///
    /// Uso: creá un GameObject vacío donde quieras el cartel (una pared, sobre los fuegos, o
    /// siguiendo al usuario), agregale este componente y posicionalo/escalalo. Si no tiene TextMesh,
    /// se crea uno con la fuente por defecto de Unity.
    ///
    /// ⚠ <b>No lo cuelgues del <c>CenterEyeAnchor</c>.</b> Ahí queda clavado a la cara: gira exacto
    /// con la cabeza, siempre en el mismo rincón del ojo, y no hay forma de mirarlo de frente. Para
    /// que acompañe al usuario usá <see cref="BodyLockedPanel"/> en este mismo objeto, suelto en la
    /// escena (es lo que deja armado <c>Extintores ▸ Fix Scene Wiring</c>).
    /// </summary>
    public class TrainingHud : MonoBehaviour
    {
        [SerializeField] private TrainingManager training;
        [Tooltip("Sesión, para saber si hay ejercicio en curso. Si queda vacío se busca en la escena.")]
        [SerializeField] private TrainingSession session;
        [SerializeField] private TextMesh text;
        [SerializeField] private Color normalColor = Color.white;
        [SerializeField] private Color warnColor = new Color(1f, 0.3f, 0.2f);
        [Tooltip("Cuánto se sostiene el cartel de 'nivel superado' antes de esconderse.")]
        [SerializeField] private float successHoldSeconds = 5f;

        private float _warnUntil;
        private bool _done;
        private float _doneUntil;
        private Renderer _renderer;

        private void Awake()
        {
            if (training == null) training = FindFirstObjectByType<TrainingManager>();
            if (session == null) session = FindFirstObjectByType<TrainingSession>();
            if (text == null) text = GetComponent<TextMesh>();
            if (text == null) text = CreateText();
            _renderer = GetComponent<Renderer>();
        }

        private void OnEnable()
        {
            if (training == null) return;
            training.WrongAgent += OnWrong;
            training.Success += OnSuccess;
            training.Started += OnStarted;
        }

        private void OnDisable()
        {
            if (training == null) return;
            training.WrongAgent -= OnWrong;
            training.Success -= OnSuccess;
            training.Started -= OnStarted;
        }

        private void OnWrong() => _warnUntil = Time.time + 1.2f;

        private void OnSuccess()
        {
            _done = true;
            _doneUntil = Time.time + successHoldSeconds;
        }

        /// <summary>
        /// Etapa nueva: se baja el cartel de éxito. Sin esto <c>_done</c> quedaba latcheado para
        /// siempre y "NIVEL SUPERADO" seguía colgado en el lobby, encima del menú.
        /// </summary>
        private void OnStarted() => _done = false;

        private void Update()
        {
            if (training == null || text == null) return;

            // Fuera del ejercicio el HUD se esconde: en el lobby no hay nada que informar y el
            // cartel se superpone con el menú, que vive a la misma altura. Se sostiene unos
            // segundos tras ganar para poder leer el tiempo final.
            bool showing = session == null || session.InSession || (_done && Time.time < _doneUntil);
            if (_renderer != null && _renderer.enabled != showing) _renderer.enabled = showing;
            if (!showing) return;

            bool warn = Time.time < _warnUntil;
            text.color = warn ? warnColor : normalColor;

            if (_done)
            {
                text.text = $"NIVEL SUPERADO\nTiempo: {training.TimeText}";
                return;
            }

            string t = $"Fuegos: {training.Remaining}/{training.Total}\n" +
                       $"Tiempo: {training.TimeText}\n" +
                       $"Agente: {training.CurrentAgent}";
            if (warn) t = "EXTINTOR EQUIVOCADO\n" + t;
            text.text = t;
        }

        private TextMesh CreateText()
        {
            var tm = gameObject.AddComponent<TextMesh>(); // agrega MeshRenderer por RequireComponent
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                       ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font != null)
            {
                tm.font = font;
                var mr = GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = font.material;
            }
            else
            {
                Debug.LogWarning("[TrainingHud] No se encontró la fuente por defecto; asigná una a mano.");
            }
            // Un TextMesh mide, en metros, ≈ fontSize × characterSize × 0.1 por renglón. Con los
            // 90/0.05 originales cada renglón daba ~0,45 m: a 1,2 m de la cara el bloque tapaba
            // medio campo visual y, anclado arriba-izquierda, se derramaba hacia el rincón inferior
            // derecho. Estos valores dan ~7 cm por renglón (~2,7° a 1,5 m), que es lo cómodo en VR.
            tm.characterSize = 0.012f;
            tm.fontSize = 60;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = normalColor;
            return tm;
        }
    }
}
