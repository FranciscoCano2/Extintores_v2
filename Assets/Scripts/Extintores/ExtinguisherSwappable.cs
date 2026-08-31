using System;
using UnityEngine;

namespace Extintores
{
    /// <summary>
    /// Marca al extintor sostenido como "intercambiable" y aplica el cambio de tipo que dispara una
    /// <see cref="ExtinguisherBay"/> de la pared de recarga: cambia el <see cref="AgentType"/> del
    /// chorro y, opcionalmente, el color del cuerpo y la textura de la etiqueta.
    ///
    /// Va en el TANQUE (ExtinguisherRig, mano derecha). Necesita un collider "sensor" (trigger, en
    /// la layer <b>Interactor</b>) en este objeto o en un hijo para que las bahías lo detecten.
    /// El chorro (<see cref="ExtinguisherSpray"/>) suele estar en otro objeto (la boquilla, mano
    /// izquierda): si no se asigna, se busca en la escena.
    /// </summary>
    public class ExtinguisherSwappable : MonoBehaviour
    {
        [Tooltip("Chorro (boquilla) cuyo agente cambia. Si queda vacío se busca en la escena.")]
        [SerializeField] private ExtinguisherSpray spray;

        [Tooltip("Renderer del cuerpo del tanque a teñir según el tipo (opcional).")]
        [SerializeField] private Renderer bodyRenderer;

        [Tooltip("Si está activo, tiñe el cuerpo con el color de la bahía. Dejalo apagado si el " +
                 "tanque ya tiene su color por textura y no querés pisarlo.")]
        [SerializeField] private bool tintBody = false;

        [Tooltip("Renderer de la etiqueta/calco cuyo mapa se reemplaza según el tipo (opcional).")]
        [SerializeField] private Renderer labelRenderer;

        /// <summary>Agente actual (lee el del chorro).</summary>
        public AgentType CurrentAgent => spray != null ? spray.Agent : AgentType.DryChemABC;

        /// <summary>Se dispara al cambiar de tipo de extintor.</summary>
        public event Action<AgentType> AgentChanged;

        private void Awake()
        {
            if (spray == null) spray = FindFirstObjectByType<ExtinguisherSpray>();
        }

        /// <summary>Aplica un tipo de extintor: agente + (opcional) color de cuerpo + etiqueta.</summary>
        public void SetAgent(AgentType agent, Color bodyColor, Texture2D label)
        {
            if (spray != null) spray.SetAgent(agent);
            if (tintBody && bodyRenderer != null) SetColor(bodyRenderer.material, bodyColor);
            if (labelRenderer != null && label != null) SetTexture(labelRenderer.material, label);
            AgentChanged?.Invoke(agent);
        }

        private static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            else m.color = c;
        }

        private static void SetTexture(Material m, Texture t)
        {
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", t);
            m.mainTexture = t;
        }
    }
}
