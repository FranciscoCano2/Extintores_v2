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

        [Tooltip("Si está activo, tiñe el cuerpo con el color del tipo de extintor. Necesita que el " +
                 "material del tanque use la textura DESATURADA (ver 'Extintores ▸ Build " +
                 "Extinguisher Body Material'): teñir sobre la roja original da negro.")]
        [SerializeField] private bool tintBody = true;

        [Tooltip("Otras piezas que se tiñen con el mismo color que el cuerpo: la boquilla/manguera, " +
                 "que vive en OTRO rig (la mano izquierda) y por eso no aparece entre los hijos.")]
        [SerializeField] private Renderer[] extraBodyRenderers;

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

        /// <summary>
        /// Pinta el tanque del color que le corresponde al agente con el que arranca. Sin esto el
        /// extintor sale del color "crudo" del material hasta que el usuario toca una bahía, que es
        /// justo lo contrario de lo que se quiere enseñar.
        /// </summary>
        private void Start() => ApplyBodyColor(CurrentAgent);

        /// <summary>
        /// Aplica un tipo de extintor: agente + aspecto del cuerpo + etiqueta.
        ///
        /// El aspecto NO se recibe como parámetro: se resuelve por <paramref name="agent"/> contra
        /// <see cref="AgentColor"/>. Pasarlo desde la bahía era otro lugar donde el color podía
        /// discrepar, y no alcanzaba: el de clase K necesita metal, no sólo un color.
        /// </summary>
        public void SetAgent(AgentType agent, Texture2D label)
        {
            if (spray != null) spray.SetAgent(agent);
            ApplyBodyColor(agent);
            if (labelRenderer != null && label != null) SetTexture(labelRenderer.material, label);
            AgentChanged?.Invoke(agent);
        }

        /// <summary>
        /// Aspecto del cuerpo según el agente, tomado de la tabla común. Se aplica al tanque y a la
        /// boquilla (<see cref="extraBodyRenderers"/>): son el mismo extintor, así que tienen que
        /// cambiar juntos.
        /// </summary>
        public void ApplyBodyColor(AgentType agent)
        {
            if (!tintBody) return;

            Tint(bodyRenderer, agent);
            if (extraBodyRenderers == null) return;
            foreach (var r in extraBodyRenderers) Tint(r, agent);
        }

        private static void Tint(Renderer renderer, AgentType agent)
        {
            if (renderer == null) return;

            var m = renderer.material; // instancia: no toca el material compartido
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", AgentColor.Of(agent));
            else m.color = AgentColor.Of(agent);

            // El de clase K es acero inoxidable, no chapa pintada: sin metallic sale gris mate.
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", AgentColor.MetallicOf(agent));
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", AgentColor.SmoothnessOf(agent));
        }

        private static void SetTexture(Material m, Texture t)
        {
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", t);
            m.mainTexture = t;
        }
    }
}
