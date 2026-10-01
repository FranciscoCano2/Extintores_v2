using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace Extintores
{
    /// <summary>
    /// Evita que el usuario aparezca <b>hundido en el piso</b>. Lo usa <see cref="TrainingSession"/>,
    /// que es quien ubica el rig: no requiere cableado en la escena.
    ///
    /// <b>Por qué pasaba (y sólo a veces).</b> Con el backend OpenXR, <c>OVRManager</c> pide el origen
    /// de piso (<c>TrySetTrackingOriginMode(Floor)</c>) <b>una sola vez</b> al iniciar. Si en ese
    /// momento el input subsystem de XR todavía no arrancó, loguea un error y sigue: el origen queda
    /// en <c>Device</c> (a la altura de los ojos), la cabeza arranca en y≈0 y todo el patio queda por
    /// encima de la mirada. Un recenter (mantener el botón Meta) o un cambio de reference space
    /// también pueden mover el piso a mitad de la sesión.
    ///
    /// Dos defensas, que se evalúan en momentos DISCRETOS (arranque, visor puesto, recenter, cambio de
    /// origen, cada reubicación) y nunca cada frame — evaluarlo siempre "levantaría" a quien se agacha
    /// frente al fuego, que es justo lo que se enseña:
    /// <list type="number">
    ///   <item>Si el origen no es de piso, se vuelve a pedir (<see cref="EnsureFloorOrigin"/>).</item>
    ///   <item>Si aun así los ojos quedan a menos de <see cref="MinEyeHeight"/> del piso (imposible
    ///         parado o arrodillado con origen de piso), se compensa la altura del rig para dejarlos a
    ///         <see cref="FallbackEyeHeight"/> (<see cref="HeightFix"/>).</item>
    /// </list>
    /// </summary>
    public static class FloorGuard
    {
        /// <summary>Por debajo de esto el origen no es de piso: arrodillado los ojos quedan a ~0,8 m.</summary>
        public const float MinEyeHeight = 0.5f;

        /// <summary>Altura de ojos que se asume cuando hay que compensar (adulto parado).</summary>
        public const float FallbackEyeHeight = 1.6f;

        private static readonly List<XRInputSubsystem> Subsystems = new List<XRInputSubsystem>();

        /// <summary>El input subsystem de XR activo, o null si todavía no arrancó.</summary>
        public static XRInputSubsystem InputSubsystem
        {
            get
            {
                SubsystemManager.GetSubsystems(Subsystems);
                foreach (var s in Subsystems)
                    if (s != null && s.running) return s;
                return null;
            }
        }

        /// <summary>
        /// Si el origen de tracking no es de piso, lo vuelve a pedir. Devuelve true si quedó (o ya
        /// estaba) en piso; <paramref name="detail"/> trae el mensaje para el log.
        /// </summary>
        public static bool EnsureFloorOrigin(out string detail)
        {
            var input = InputSubsystem;
            if (input == null)
            {
                detail = "sin input subsystem de XR todavía";
                return false;
            }

            var mode = input.GetTrackingOriginMode();
            if (mode == TrackingOriginModeFlags.Floor)
            {
                detail = "origen de piso OK";
                return true;
            }

            // Por OVRManager si está: además del subsystem ajusta el recentering de OpenXR y deja su
            // propio campo sincronizado. Si no, directo al subsystem.
            if (OVRManager.instance != null)
                OVRManager.instance.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
            else
                input.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);

            var now = input.GetTrackingOriginMode();
            detail = $"origen {mode} → {now}";
            return now == TrackingOriginModeFlags.Floor;
        }

        /// <summary>
        /// Compensación vertical del rig para que los ojos queden sobre el piso. <paramref name="rawEyeHeight"/>
        /// es la altura de los ojos que reporta el tracking respecto del origen del rig (sin
        /// compensar). Devuelve 0 si la altura es creíble.
        /// </summary>
        public static float HeightFix(float rawEyeHeight) =>
            rawEyeHeight < MinEyeHeight ? FallbackEyeHeight - rawEyeHeight : 0f;

        /// <summary>¿La cabeza ya tiene pose de tracking? Antes de eso cualquier altura es basura.</summary>
        public static bool HeadTracked =>
            OVRManager.isHmdPresent && OVRManager.tracker != null && OVRManager.tracker.isPositionTracked;
    }
}
