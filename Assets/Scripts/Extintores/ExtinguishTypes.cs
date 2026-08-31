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
