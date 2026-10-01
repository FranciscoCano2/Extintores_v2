using UnityEngine;

namespace Extintores
{
    /// <summary>
    /// Un PUESTO de entrenamiento dentro del galpón: un fuego de una clase, el punto donde se ubica
    /// al usuario y dónde va el panel de extintores.
    ///
    /// Existe para desacoplar la lógica de la escenografía: <see cref="TrainingSession"/> solo
    /// conoce puestos, así que se puede mover un fuego a otro rincón del galpón —o reemplazar todo
    /// el blockout por un pack de assets— sin tocar una línea de código.
    /// </summary>
    public class FireStation : MonoBehaviour
    {
        [Tooltip("El fuego de este puesto. Si queda vacío se busca en los hijos.")]
        [SerializeField] private FireSource fire;

        [Tooltip("Raíz del prefab del fuego (Fire_A/B/C/K). Es lo que se prende y apaga: el " +
                 "FireSource vive en el hijo 'Flame', pero el badge de clase cuelga del PADRE, " +
                 "así que apagar solo el FireSource dejaría la letra flotando sola.")]
        [SerializeField] private GameObject fireRoot;

        [Tooltip("Dónde queda parado el usuario: posición y orientación (mirando al fuego).")]
        [SerializeField] private Transform playerSpawn;

        [Tooltip("Dónde se coloca el panel de extintores, adelante a la derecha del usuario.")]
        [SerializeField] private Transform panelAnchor;

        [Tooltip("Cartel apuntable sobre el elemento, para elegir este puesto desde el lobby en modo " +
                 "Personalizado. Lo crea Extintores ▸ Fix Scene Wiring. Arranca oculto.")]
        [SerializeField] private SprayButton selector;

        public FireSource Fire => fire;
        public Transform PlayerSpawn => playerSpawn != null ? playerSpawn : transform;
        public Transform PanelAnchor => panelAnchor;
        public SprayButton Selector => selector;

        /// <summary>Clase del fuego de este puesto (la lee del propio <see cref="FireSource"/>).</summary>
        public FireClass Class => fire != null ? fire.Class : FireClass.A;

        private void Awake()
        {
            if (fire == null) fire = GetComponentInChildren<FireSource>(true);
            if (fire == null)
            {
                Debug.LogWarning($"[FireStation] '{name}' no tiene FireSource: el puesto no sirve.");
                return;
            }

            // Sin raíz explícita, se sube hasta el hijo directo de este puesto: ese es el prefab
            // del fuego entero (llama + causa + badge).
            if (fireRoot == null)
            {
                var t = fire.transform;
                while (t.parent != null && t.parent != transform) t = t.parent;
                fireRoot = t.gameObject;
            }
        }

        /// <summary>Enciende el fuego de cero. Lo llama <see cref="TrainingSession"/> al entrar.</summary>
        public void Activate()
        {
            if (fireRoot != null) fireRoot.SetActive(true);
            if (fire != null) fire.ResetFire();
        }

        /// <summary>
        /// Deja el puesto en reposo sin contarlo como "extinguido": se apaga <b>sólo la llama</b> y
        /// el elemento (auto, tablero…) sigue a la vista, para poder elegirlo desde el centro del
        /// patio en modo Personalizado. El badge se esconde solo (<c>FireClassBadge.OnDisable</c>).
        /// </summary>
        public void Deactivate()
        {
            if (fire != null && fire.gameObject != fireRoot)
            {
                if (fireRoot != null && !fireRoot.activeSelf) fireRoot.SetActive(true);
                fire.gameObject.SetActive(false);
            }
            else if (fireRoot != null) fireRoot.SetActive(false);
        }

        /// <summary>Muestra u oculta el cartel apuntable de este puesto.</summary>
        public void SetSelectable(bool value)
        {
            if (selector != null && selector.gameObject.activeSelf != value)
                selector.gameObject.SetActive(value);
        }
    }
}
