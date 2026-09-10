using UnityEditor;
using UnityEngine;

namespace Extintores.EditorTools
{
    /// <summary>
    /// Pone el tracking origin del rig en <b>FloorLevel</b>, que es lo que hace que el usuario
    /// aparezca parado SOBRE el piso y no con los ojos al ras del suelo.
    ///
    /// ⚠ El campo <c>_trackingOriginType</c> vive en <b>OVRManager</b>, no en <b>OVRCameraRig</b>.
    /// Los dos componentes están en el mismo GameObject del prefab, así que es fácil confundirlos —
    /// y esa confusión ya costó una sesión de visor: el arreglo anterior hacía
    /// <c>new SerializedObject(ovrCameraRig).FindProperty("_trackingOriginType")</c>, que devuelve
    /// <c>null</c>, y se salía sin tocar nada ni avisar. Cero errores en consola y la escena seguía
    /// en EyeLevel.
    ///
    /// El default del prefab <c>OVRCameraRig</c> del SDK es EyeLevel (0), así que si la escena no
    /// tiene un override, está en EyeLevel. Los dos componentes se buscan por NOMBRE DE TIPO para no
    /// tener que referenciar el assembly del SDK desde acá.
    /// </summary>
    internal static class OvrTrackingOrigin
    {
        private const string Field = "_trackingOriginType";
        private const int FloorLevel = 1; // OVRManager.TrackingOrigin: EyeLevel=0, FloorLevel=1

        internal enum Result { NoManager, NoField, AlreadyFloor, NeedsFix, Fixed }

        /// <summary>
        /// Revisa (y opcionalmente corrige) el tracking origin. <paramref name="detail"/> trae el
        /// mensaje listo para loguear.
        /// </summary>
        internal static Result Ensure(bool apply, out string detail)
        {
            var manager = FindComponent("OVRManager");
            if (manager == null)
            {
                detail = "No encontré el OVRManager en la escena: no puedo fijar el tracking origin " +
                         "y el usuario va a aparecer con los ojos al ras del piso.";
                return Result.NoManager;
            }

            var so = new SerializedObject(manager);
            var p = so.FindProperty(Field);
            if (p == null)
            {
                detail = $"El OVRManager de '{manager.name}' no expone '{Field}' " +
                         "(¿cambió la versión del Meta XR SDK?). Ponelo a mano en FloorLevel.";
                return Result.NoField;
            }

            if (p.enumValueIndex == FloorLevel)
            {
                detail = "Tracking origin del rig en FloorLevel.";
                return Result.AlreadyFloor;
            }

            if (!apply)
            {
                detail = $"OVRManager.trackingOriginType está en EyeLevel: el rig está en y=0, así " +
                         "que los ojos arrancan al ras del piso y todo el galpón queda por encima " +
                         "de la mirada. Debe ser FloorLevel.";
                return Result.NeedsFix;
            }

            p.enumValueIndex = FloorLevel;
            so.ApplyModifiedProperties();
            detail = "OVRManager: tracking origin EyeLevel → FloorLevel.";
            return Result.Fixed;
        }

        /// <summary>Busca un componente por nombre de tipo, sin depender del assembly del SDK.</summary>
        internal static Component FindComponent(string typeName)
        {
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include,
                                                                      FindObjectsSortMode.None))
                if (mb != null && mb.GetType().Name == typeName) return mb;
            return null;
        }
    }
}
