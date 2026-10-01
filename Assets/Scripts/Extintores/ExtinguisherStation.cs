using UnityEngine;

namespace Extintores
{
    /// <summary>
    /// Contenedor de la pared de recarga: agrupa las <see cref="ExtinguisherBay"/> hijas. Punto de
    /// enganche para futura lógica (p. ej. resaltar la bahía correcta para el fuego cercano
    /// leyendo <see cref="FireSource.EffectivenessOf"/>).
    /// </summary>
    public class ExtinguisherStation : MonoBehaviour
    {
        /// <summary>Bahías de esta estación (se recogen al iniciar).</summary>
        public ExtinguisherBay[] Bays { get; private set; }

        private void Awake()
        {
            Bays = GetComponentsInChildren<ExtinguisherBay>(true);
        }
    }
}
