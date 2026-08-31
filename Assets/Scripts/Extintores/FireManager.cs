using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Extintores
{
    /// <summary>
    /// Orquesta los fuegos de la escena y decide la CONDICIÓN DE ÉXITO (todos apagados).
    ///
    /// Al iniciar recoge todos los <see cref="FireSource"/> de la escena (o se registran a mano)
    /// y se suscribe a su evento <see cref="FireSource.Extinguished"/>. Lleva la cuenta de cuántos
    /// quedan activos y, cuando llega a 0, dispara los eventos de "nivel superado".
    ///
    /// Expone eventos tanto UnityEvent (para cablear cosas simples desde el inspector, p. ej.
    /// activar un cartel de victoria) como C# (para el futuro HUD/puntaje de la Fase 4).
    /// </summary>
    [DefaultExecutionOrder(70)] // después de FireSource (60): sus eventos ya están listos
    public class FireManager : MonoBehaviour
    {
        /// <summary>UnityEvent con un int, para poder cablearlo desde el inspector.</summary>
        [System.Serializable] public class IntEvent : UnityEvent<int> { }

        [Header("Descubrimiento de fuegos")]
        [Tooltip("Si está activo, al iniciar busca todos los FireSource de la escena y los " +
                 "registra. Desactivalo solo si vas a registrarlos a mano (p. ej. fuegos que " +
                 "aparecen durante la partida).")]
        [SerializeField] private bool autoCollectOnStart = true;

        [Header("Eventos")]
        [Tooltip("Se dispara cada vez que se apaga un fuego. Parámetro: cuántos quedan activos.")]
        public IntEvent onFireExtinguished;

        [Tooltip("Se dispara UNA vez cuando se apagan TODOS los fuegos (condición de éxito).")]
        public UnityEvent onAllFiresExtinguished;

        // Equivalentes en C# para la lógica/HUD.
        public event System.Action<FireSource, int> FireExtinguished; // (fuego apagado, quedan)
        public event System.Action AllFiresExtinguished;

        // --- API pública de estado ---
        public int TotalFires { get; private set; }
        public int RemainingFires { get; private set; }
        public int ExtinguishedFires => TotalFires - RemainingFires;
        public float Progress01 => TotalFires > 0 ? (float)ExtinguishedFires / TotalFires : 0f;
        public bool AllExtinguished => TotalFires > 0 && RemainingFires == 0;

        private readonly HashSet<FireSource> _fires = new HashSet<FireSource>();
        private bool _completed;

        private void Start()
        {
            if (!autoCollectOnStart) return;

            foreach (var f in FindObjectsByType<FireSource>(FindObjectsSortMode.None))
                Register(f);

            // Escena sin fuegos: no hay nada que superar, evitamos falso "completado".
            if (TotalFires == 0)
                Debug.LogWarning("[FireManager] No se encontró ningún FireSource en la escena.");
        }

        /// <summary>Registra un fuego para el conteo. Ignora nulos, repetidos y ya apagados.</summary>
        public void Register(FireSource fire)
        {
            if (fire == null || fire.IsExtinguished) return;
            if (!_fires.Add(fire)) return; // ya estaba registrado

            fire.Extinguished += HandleExtinguished;
            TotalFires++;
            RemainingFires++;
        }

        private void HandleExtinguished(FireSource fire)
        {
            if (!_fires.Remove(fire)) return;
            fire.Extinguished -= HandleExtinguished;

            RemainingFires = Mathf.Max(0, RemainingFires - 1);

            onFireExtinguished?.Invoke(RemainingFires);
            FireExtinguished?.Invoke(fire, RemainingFires);

            Debug.Log($"[FireManager] Fuego apagado. Quedan {RemainingFires}/{TotalFires}.");

            if (RemainingFires == 0 && !_completed)
            {
                _completed = true;
                onAllFiresExtinguished?.Invoke();
                AllFiresExtinguished?.Invoke();
                Debug.Log("[FireManager] ¡Todos los fuegos apagados! Nivel superado.");
            }
        }

        private void OnDestroy()
        {
            foreach (var f in _fires)
                if (f != null) f.Extinguished -= HandleExtinguished;
        }
    }
}
