using UnityEngine;

namespace Extintores
{
    /// <summary>
    /// HUD de entrenamiento en world-space, hecho con <see cref="TextMesh"/> (no depende de UGUI ni
    /// TextMeshPro). Muestra fuegos restantes, tiempo y agente actual, leídos de
    /// <see cref="TrainingManager"/>; parpadea "EXTINTOR EQUIVOCADO" al usar el agente incorrecto y
    /// muestra el cartel de éxito con el tiempo final.
    ///
    /// Uso: creá un GameObject vacío donde quieras el cartel (una pared, sobre los fuegos, o pegado
    /// al rig), agregale este componente y posicionalo/escalalo. Si no tiene TextMesh, se crea uno
    /// con la fuente por defecto de Unity.
    /// </summary>
    public class TrainingHud : MonoBehaviour
    {
        [SerializeField] private TrainingManager training;
        [SerializeField] private TextMesh text;
        [SerializeField] private Color normalColor = Color.white;
        [SerializeField] private Color warnColor = new Color(1f, 0.3f, 0.2f);

        private float _warnUntil;
        private bool _done;

        private void Awake()
        {
            if (training == null) training = FindFirstObjectByType<TrainingManager>();
            if (text == null) text = GetComponent<TextMesh>();
            if (text == null) text = CreateText();
        }

        private void OnEnable()
        {
            if (training == null) return;
            training.WrongAgent += OnWrong;
            training.Success += OnSuccess;
        }

        private void OnDisable()
        {
            if (training == null) return;
            training.WrongAgent -= OnWrong;
            training.Success -= OnSuccess;
        }

        private void OnWrong() => _warnUntil = Time.time + 1.2f;
        private void OnSuccess() => _done = true;

        private void Update()
        {
            if (training == null || text == null) return;

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
            tm.characterSize = 0.05f;
            tm.fontSize = 90;
            tm.anchor = TextAnchor.UpperLeft;
            tm.color = normalColor;
            return tm;
        }
    }
}
