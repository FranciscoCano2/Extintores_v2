using UnityEngine;

namespace Extintores
{
    /// <summary>
    /// Clase de fuego (clasificación tipo NFPA/US, la que aparece en las etiquetas de los
    /// extintores). El orden DEBE coincidir con las columnas de <see cref="ExtinguishMatrix"/>.
    /// </summary>
    public enum FireClass
    {
        A, // Sólidos: madera, papel, tela, plástico
        B, // Líquidos inflamables: gasolina, aceite, pintura, solventes
        C, // Equipos eléctricos energizados
        K, // Aceites y grasas de cocina
    }

    /// <summary>
    /// Agente extintor. Determina la efectividad frente a cada clase de fuego. El orden DEBE
    /// coincidir con las filas de <see cref="ExtinguishMatrix"/>.
    /// </summary>
    public enum AgentType
    {
        Water,        // Agua — clase A
        Foam,         // Espuma — A, B
        CO2,          // Dióxido de carbono — B, C
        DryChemABC,   // Polvo químico seco ABC (multipropósito, el rojo típico)
        DryChemBC,    // Polvo químico seco BC
        WetChemical,  // Químico húmedo — K (cocina)
    }

    /// <summary>
    /// Aspecto del cuerpo de cada tipo de extintor: color y cuánto de metal tiene.
    ///
    /// Es la <b>fuente única</b>: la usan la pared de recarga para pintar cada bahía
    /// (<c>WallBuilder</c>) y el tanque de la mano para teñirse al cambiar de agente
    /// (<see cref="ExtinguisherSwappable"/>). Tenerlo duplicado en el builder hacía que el panel y
    /// el extintor pudieran discrepar sin que nada lo delatara.
    ///
    /// ⚠️ <b>Es una paleta didáctica, no una norma.</b> Ninguna norma pinta el cuerpo entero así:
    /// IRAM (Argentina) y NFPA usan cuerpo rojo con etiqueta, y BS EN 3 usa rojo con una banda de
    /// color. Acá se pinta el cuerpo completo porque en VR el color se lee de un vistazo y a 3 m
    /// una etiqueta no se distingue — que es justo lo que el ejercicio quiere enseñar. La excepción
    /// es el de clase K: el químico húmedo se fabrica en <b>acero inoxidable</b>, y ese sí es su
    /// aspecto real.
    /// </summary>
    public static class AgentColor
    {
        public static Color Of(AgentType agent) => agent switch
        {
            AgentType.Water => new Color(0.00f, 0.37f, 0.70f), // azul
            AgentType.Foam => new Color(0.85f, 0.72f, 0.42f), // crema
            AgentType.CO2 => new Color(0.10f, 0.10f, 0.11f), // negro
            AgentType.DryChemABC => new Color(0.78f, 0.10f, 0.10f), // rojo (el multipropósito típico)
            AgentType.DryChemBC => new Color(0.72f, 0.30f, 0.10f), // rojo anaranjado
            _ => new Color(0.80f, 0.82f, 0.85f),                    // WetChemical: acero inoxidable
        };

        /// <summary>
        /// Cuánto metal tiene el cuerpo (0 = pintado, 1 = metal desnudo). Sin esto el de clase K
        /// sale gris claro mate, que se lee como "pintado de gris" y no como acero inoxidable.
        /// </summary>
        public static float MetallicOf(AgentType agent) =>
            agent == AgentType.WetChemical ? 0.85f : 0.05f;

        /// <summary>Los pintados son más brillantes que el acero cepillado del de cocina.</summary>
        public static float SmoothnessOf(AgentType agent) =>
            agent == AgentType.WetChemical ? 0.62f : 0.45f;
    }

    /// <summary>
    /// Matriz de efectividad agente × clase de fuego. Devuelve un MULTIPLICADOR sobre el poder
    /// de apagado de cada partícula del chorro:
    /// <list type="bullet">
    ///   <item><c>1</c>  = efectivo (apaga a ritmo normal).</item>
    ///   <item><c>~0</c> = casi inútil (el agente no sirve para esa clase).</item>
    ///   <item><c>&lt;0</c> = CONTRAPRODUCENTE: el extintor equivocado no apaga y AVIVA el fuego
    ///         (p. ej. agua sobre líquidos/eléctrico/cocina). Es el núcleo pedagógico de la fase.</item>
    /// </list>
    /// Valores aproximados con fines de entrenamiento; ajustables aquí en un solo lugar.
    /// </summary>
    public static class ExtinguishMatrix
    {
        // Filas = AgentType, Columnas = FireClass (A, B, C, K).
        private static readonly float[,] Table =
        {
            //               A      B      C      K
            /* Water      */ { 1.0f, -0.5f, -0.8f, -1.0f },
            /* Foam       */ { 1.0f,  1.0f, -0.5f,  0.2f },
            /* CO2        */ { 0.3f,  1.0f,  1.0f,  0.3f },
            /* DryChemABC */ { 1.0f,  1.0f,  1.0f,  0.3f },
            /* DryChemBC  */ { 0.2f,  1.0f,  1.0f,  0.3f },
            /* WetChemical*/ { 0.4f,  0.3f, -0.3f,  1.0f },
        };

        /// <summary>Multiplicador de apagado del <paramref name="agent"/> sobre <paramref name="fire"/>.</summary>
        public static float Effectiveness(AgentType agent, FireClass fire)
            => Table[(int)agent, (int)fire];

        /// <summary>True si el agente empeora ese fuego (multiplicador negativo).</summary>
        public static bool IsCounterproductive(AgentType agent, FireClass fire)
            => Effectiveness(agent, fire) < 0f;
    }
}
