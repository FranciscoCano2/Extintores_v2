using UnityEngine;

namespace Extintores
{
    /// <summary>
    /// Oculta el modelo del mando (y/o la mano) mientras el extintor está sostenido, para que
    /// el jugador vea SOLO el extintor y no el control flotando dentro del cuerpo.
    ///
    /// Recomendado sobre "esconderlo dentro del extintor": apagar el render evita z-fighting y
    /// que el control asome por la geometría. Se apagan los <see cref="Renderer"/> de los objetos
    /// indicados (típicamente el OVRControllerPrefab de la mano que sostiene el extintor); se
    /// vuelven a encender al soltarlo.
    /// </summary>
    public class HeldControllerHider : MonoBehaviour
    {
        [Tooltip("Fuente del estado sostenido. Si queda vacío se busca en este GameObject.")]
        [SerializeField] private ExtinguisherHandle handle;

        [Tooltip("Raíces cuyos renderers se ocultan mientras el extintor está en la mano " +
                 "(p. ej. el OVRControllerPrefab del mando que lo sostiene).")]
        [SerializeField] private GameObject[] hideWhileHeld;

        private void Awake()
        {
            if (handle == null) handle = GetComponent<ExtinguisherHandle>();
        }

        private void OnEnable()
        {
            if (handle != null) handle.HeldChanged += OnHeldChanged;
            if (handle != null) OnHeldChanged(handle.IsHeld);
        }

        private void OnDisable()
        {
            if (handle != null) handle.HeldChanged -= OnHeldChanged;
            SetHidden(false); // no dejar nada apagado si se desactiva el componente
        }

        private void OnHeldChanged(bool held) => SetHidden(held);

        private void SetHidden(bool hidden)
        {
            if (hideWhileHeld == null) return;
            foreach (var go in hideWhileHeld)
            {
                if (go == null) continue;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    r.enabled = !hidden;
            }
        }
    }
}
