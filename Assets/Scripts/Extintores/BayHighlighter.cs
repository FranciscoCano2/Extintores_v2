using UnityEngine;

namespace Extintores
{
    /// <summary>
    /// Resalta las bahías de la <see cref="ExtinguisherStation"/> según el fuego activo más cercano:
    /// el marco se pinta de VERDE si ese agente es efectivo, de ROJO si es contraproducente, y queda
    /// con su color propio si es neutro. Usa <see cref="FireSource.EffectivenessOf"/> y un
    /// <see cref="MaterialPropertyBlock"/> (no toca los materiales compartidos).
    ///
    /// Va en el mismo objeto que <see cref="ExtinguisherStation"/> (el WallBuilder lo agrega solo).
    /// </summary>
    [DefaultExecutionOrder(85)]
    public class BayHighlighter : MonoBehaviour
    {
        [SerializeField] private ExtinguisherStation station;
        [Tooltip("Cada cuánto recalcula el resaltado (s).")]
        [SerializeField] private float updateInterval = 0.25f;
        [SerializeField] private Color correctColor = new Color(0.20f, 0.90f, 0.30f);
        [SerializeField] private Color dangerColor = new Color(0.95f, 0.20f, 0.15f);
        [Tooltip("Efectividad mínima para considerar el agente 'correcto' (verde).")]
        [SerializeField, Range(0f, 1f)] private float correctThreshold = 0.9f;

        private MaterialPropertyBlock _mpb;
        private float _next;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private void Awake()
        {
            if (station == null) station = GetComponent<ExtinguisherStation>();
            _mpb = new MaterialPropertyBlock();
        }

        private void Update()
        {
            if (station == null || station.Bays == null) return;
            if (Time.time < _next) return;
            _next = Time.time + updateInterval;

            var fire = NearestActiveFire();

            foreach (var bay in station.Bays)
            {
                if (bay == null) continue;
                var frame = FrameRenderer(bay);
                if (frame == null) continue;

                Color c = bay.BodyColor;
                if (fire != null)
                {
                    float eff = fire.EffectivenessOf(bay.Agent);
                    if (eff < 0f) c = dangerColor;
                    else if (eff >= correctThreshold) c = correctColor;
                }

                frame.GetPropertyBlock(_mpb);
                _mpb.SetColor(BaseColorId, c);
                frame.SetPropertyBlock(_mpb);
            }
        }

        private FireSource NearestActiveFire()
        {
            FireSource best = null;
            float bestDist = float.MaxValue;
            foreach (var f in FindObjectsByType<FireSource>(FindObjectsSortMode.None))
            {
                if (f == null || f.IsExtinguished) continue;
                float d = (f.transform.position - transform.position).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = f; }
            }
            return best;
        }

        private static Renderer FrameRenderer(ExtinguisherBay bay)
        {
            var t = bay.transform.Find("Frame");
            return t != null ? t.GetComponent<Renderer>() : bay.GetComponentInChildren<Renderer>();
        }
    }
}
