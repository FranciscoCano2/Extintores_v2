using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.OpenXR.Input;
using XRDevice = UnityEngine.XR.InputDevice;
using XRDevices = UnityEngine.XR.InputDevices;
using XRChar = UnityEngine.XR.InputDeviceCharacteristics;

namespace Extintores
{
    /// <summary>
    /// Único punto por el que el juego hace vibrar los mandos, para que el switch de
    /// <see cref="GameSettings.Vibration"/> alcance a todos.
    ///
    /// Lo que NO anda en este proyecto (backend OpenXR de Unity + Meta XR SDK), probado en el visor:
    /// <list type="bullet">
    ///   <item><c>OVRInput.SetControllerVibration</c>: la sesión es de Unity y el action set de
    ///         háptica de OVRPlugin nunca se engancha — <c>XR_ERROR_ACTIONSET_NOT_ATTACHED</c> en el
    ///         log, el mando quieto, ninguna excepción.</item>
    ///   <item>La API vieja <c>UnityEngine.XR.InputDevices</c> filtrando por
    ///         Controller|HeldInHand: no devolvió ningún mando en toda la sesión.</item>
    /// </list>
    ///
    /// Camino principal: <b>Input System</b>. Cada perfil de mando de OpenXR (Touch, Touch Plus,
    /// Touch Pro y sus variantes <i>detached</i> del multimodal) expone un control con usage
    /// <c>Haptic</c>; una <see cref="InputAction"/> ligada a <c>&lt;XRController&gt;{RightHand}/{Haptic}</c>
    /// los agarra a todos y <see cref="OpenXRInput.SendHapticImpulse(InputAction,float,float,float,UnityEngine.InputSystem.InputDevice)"/>
    /// vibra por la acción que Unity sí enganchó a la sesión. Se manda a CADA dispositivo ligado: en
    /// multimodal el mismo mando físico aparece con el perfil normal y con el detached, y sólo uno
    /// está activo.
    ///
    /// Semántica de <see cref="Set"/>: la vibración queda sonando <see cref="HoldSeconds"/> o hasta
    /// que se la cambie o se la pare. <b>No llamar a <see cref="Stop"/> en cada frame</b>: corta los
    /// pulsos de todos los demás en ese mando.
    /// </summary>
    public static class Haptics
    {
        /// <summary>Cuánto dura una vibración "sostenida" si nadie la renueva ni la para.</summary>
        private const float HoldSeconds = 2f;

        public static void Set(float frequency, float amplitude, OVRInput.Controller controller)
        {
            if (amplitude > 0f && !GameSettings.Vibration) return;
            Send(Mathf.Clamp01(amplitude), HoldSeconds, controller);
        }

        public static void Stop(OVRInput.Controller controller) => Send(0f, 0f, controller);

        public static void StopAll()
        {
            Stop(OVRInput.Controller.RTouch);
            Stop(OVRInput.Controller.LTouch);
        }

        /// <summary>Pulso corto que se apaga solo. No necesita ningún componente en la escena.</summary>
        public static void Pulse(float amplitude, float duration, OVRInput.Controller controller)
        {
            if (!GameSettings.Vibration) return;
            Runner.Instance.StartCoroutine(PulseRoutine(amplitude, duration, controller));
        }

        private static IEnumerator PulseRoutine(float amplitude, float duration, OVRInput.Controller controller)
        {
            Set(1f, amplitude, controller);
            yield return new WaitForSeconds(duration);
            Stop(controller);
        }

        // ------------------------------------------------------------------ envío

        private static InputAction _left, _right;
        private static readonly HashSet<UnityEngine.InputSystem.InputDevice> Seen =
            new HashSet<UnityEngine.InputSystem.InputDevice>();
        private static readonly List<XRDevice> Legacy = new List<XRDevice>();
        private static bool _loggedOk, _loggedMissing;

        private static void Send(float amplitude, float duration, OVRInput.Controller controller)
        {
            bool left = controller == OVRInput.Controller.LTouch;
            if (!left && controller != OVRInput.Controller.RTouch) return; // sólo se usan RTouch/LTouch

            int sent = SendInputSystem(left, amplitude, duration);
            if (sent == 0) sent = SendLegacy(left, amplitude, duration);

            if (amplitude <= 0f) return;
            if (sent > 0 && !_loggedOk)
            {
                _loggedOk = true;
                Debug.Log($"[Haptics] Vibrando el mando {(left ? "izquierdo" : "derecho")} ({sent} dispositivo/s).\n{Inventory()}");
            }
            // Al arrancar los mandos todavía no existen: no vale la pena avisar en los primeros segundos.
            else if (sent == 0 && !_loggedMissing && Time.realtimeSinceStartup > 5f)
            {
                _loggedMissing = true;
                Debug.LogWarning($"[Haptics] No encontré por dónde vibrar el mando {(left ? "izquierdo" : "derecho")}.\n{Inventory()}");
            }
        }

        private static int SendInputSystem(bool left, float amplitude, float duration)
        {
            var action = Action(left);
            Seen.Clear();
            int sent = 0;
            foreach (var control in action.controls)
            {
                var device = control.device;
                if (device == null || !device.added || !Seen.Add(device)) continue;
                if (OpenXRInput.GetActionHandle(action, device) == 0) continue; // no es un dispositivo OpenXR

                if (amplitude <= 0f) OpenXRInput.StopHaptics(action, device);
                else OpenXRInput.SendHapticImpulse(action, amplitude, 0f, duration, device); // 0 Hz = la del runtime
                sent++;
            }
            return sent;
        }

        /// <summary>Respaldo por la API vieja, sin filtrar por HeldInHand (no lo reporta en este setup).</summary>
        private static int SendLegacy(bool left, float amplitude, float duration)
        {
            XRDevices.GetDevicesWithCharacteristics(left ? XRChar.Left : XRChar.Right, Legacy);
            int sent = 0;
            foreach (var d in Legacy)
            {
                if ((d.characteristics & XRChar.HandTracking) != 0) continue; // la mano no vibra
                if (amplitude <= 0f) OpenXRInput.StopHapticImpulse(d);
                else OpenXRInput.SendHapticImpulse(d, amplitude, 0f, duration);
                sent++;
            }
            return sent;
        }

        private static InputAction Action(bool left)
        {
            ref InputAction a = ref left ? ref _left : ref _right;
            if (a != null) return a;

            string hand = left ? "{LeftHand}" : "{RightHand}";
            a = new InputAction($"Haptic{(left ? "Left" : "Right")}", InputActionType.PassThrough);
            a.AddBinding($"<XRController>{hand}/{{Haptic}}"); // por usage: vale para todos los perfiles
            a.Enable(); // sin habilitar, action.controls queda vacío
            return a;
        }

        /// <summary>Qué dispositivos XR ve el juego. Sale en el log para diagnosticar desde el visor.</summary>
        private static string Inventory()
        {
            var sb = new StringBuilder("  Input System:");
            foreach (var d in InputSystem.devices)
            {
                if (!(d is UnityEngine.InputSystem.XR.XRController)) continue;
                sb.Append($"\n    · {d.layout} usages=[{string.Join(",", d.usages)}]");
            }
            var all = new List<XRDevice>();
            XRDevices.GetDevices(all);
            sb.Append("\n  XR InputDevices:");
            foreach (var d in all) sb.Append($"\n    · {d.name} ({d.characteristics})");
            return sb.ToString();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _loggedOk = _loggedMissing = false;
            _left = _right = null; // el Input System se reinicia con el dominio
        }

        /// <summary>Host invisible para las corrutinas de <see cref="Pulse"/>.</summary>
        private sealed class Runner : MonoBehaviour
        {
            private static Runner _instance;

            public static Runner Instance
            {
                get
                {
                    if (_instance != null) return _instance;
                    var go = new GameObject("[Haptics]") { hideFlags = HideFlags.HideAndDontSave };
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<Runner>();
                    return _instance;
                }
            }
        }
    }
}
