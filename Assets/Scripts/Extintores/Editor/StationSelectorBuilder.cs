using UnityEditor;
using UnityEngine;

namespace Extintores.EditorTools
{
    /// <summary>
    /// Crea el cartel apuntable de un <see cref="FireStation"/> (<see cref="FireStation.Selector"/>):
    /// el <see cref="SprayButton"/> con el que, en modo Personalizado, el usuario elige desde el centro
    /// del patio el elemento que quiere ir a apagar.
    ///
    /// El cartel flota sobre el elemento (la <c>Cause</c> del prefab del fuego) con la cara legible
    /// (−Z) hacia el lobby, y su <c>BoxCollider</c> cubre también el elemento entero: se puede apuntar
    /// al auto o al cartel, da igual. Cuelga del <c>Station_X</c> y no de la raíz del fuego, para que
    /// no dependa de si la llama está prendida.
    ///
    /// Lo usan <see cref="TrainingSceneBuilder"/> y <see cref="SceneWiringFixer"/>.
    /// </summary>
    internal static class StationSelectorBuilder
    {
        internal const string ObjectName = "Selector";

        /// <summary>Tamaño del cartel. A ~9 m cada letra mide ~1°: más chico no se lee.</summary>
        private static readonly Vector2 Size = new Vector2(1.2f, 0.45f);

        /// <summary>Altura del cartel por encima del elemento (m).</summary>
        private const float Lift = 0.6f;

        /// <summary>Nombre del elemento, no la clase: la clase es lo que el ejercicio enseña.</summary>
        internal static string Caption(FireClass c) => c switch
        {
            FireClass.A => "CARTÓN",
            FireClass.B => "AUTO",
            FireClass.C => "TABLERO",
            _ => "COCINA",
        };

        /// <summary>
        /// Devuelve el selector del puesto, creándolo si falta. <paramref name="lobby"/> (posición del
        /// usuario en el lobby, en mundo) define hacia dónde mira el cartel.
        /// </summary>
        internal static SprayButton Ensure(FireStation station, Vector3 lobby, bool undo)
        {
            if (station == null) return null;
            if (station.Selector != null) return station.Selector;

            Bounds b = ElementBounds(station);

            var go = new GameObject(ObjectName);
            if (undo) Undo.RegisterCreatedObjectUndo(go, "Crear selector de puesto");
            go.transform.SetParent(station.transform, false);

            Vector3 pos = new Vector3(b.center.x, b.max.y + Lift + Size.y / 2f, b.center.z);
            Vector3 away = pos - lobby;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) away = Vector3.forward;
            // Cara legible −Z hacia el lobby → el +Z apunta en sentido contrario, lejos del usuario.
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(away.normalized, Vector3.up));

            int layer = LayerMask.NameToLayer(SprayButton.LayerName);
            if (layer >= 0) go.layer = layer;

            var button = go.AddComponent<SprayButton>();
            var so = new SerializedObject(button);
            var t = so.FindProperty("text"); if (t != null) t.stringValue = Caption(station.Class);
            var s = so.FindProperty("size"); if (s != null) s.vector2Value = Size;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Collider que abarca el cartel Y el elemento, en el espacio local del selector.
            var local = new Bounds(Vector3.zero, new Vector3(Size.x, Size.y, 0.06f));
            foreach (var corner in Corners(b))
                local.Encapsulate(go.transform.InverseTransformPoint(corner));
            var box = go.AddComponent<BoxCollider>();
            box.center = local.center;
            box.size = local.size;

            go.SetActive(false); // lo muestra el menú al abrir Personalizado

            var stSo = new SerializedObject(station);
            var p = stSo.FindProperty("selector");
            if (p == null)
            {
                Debug.LogWarning("[StationSelectorBuilder] FireStation no tiene el campo 'selector'.");
            }
            else
            {
                p.objectReferenceValue = button;
                if (undo) stSo.ApplyModifiedProperties();
                else stSo.ApplyModifiedPropertiesWithoutUndo();
            }
            return button;
        }

        /// <summary>
        /// Bounds del elemento: los renderers de la <c>Cause</c>; si no hay, los del prefab del fuego
        /// sin partículas; si tampoco, un metro alrededor del puesto.
        /// </summary>
        private static Bounds ElementBounds(FireStation station)
        {
            var rootProp = new SerializedObject(station).FindProperty("fireRoot");
            var root = rootProp != null ? rootProp.objectReferenceValue as GameObject : null;
            Transform search = root != null ? root.transform : station.transform;
            var cause = search.Find("Cause");
            if (cause != null) search = cause;

            bool any = false;
            var b = new Bounds();
            foreach (var r in search.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            if (any) return b;

            Vector3 at = station.Fire != null ? station.Fire.transform.position : station.transform.position;
            return new Bounds(at + Vector3.up * 0.5f, Vector3.one);
        }

        private static Vector3[] Corners(Bounds b)
        {
            Vector3 min = b.min, max = b.max;
            return new[]
            {
                new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z),
                new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z),
                new Vector3(min.x, min.y, max.z), new Vector3(max.x, min.y, max.z),
                new Vector3(min.x, max.y, max.z), new Vector3(max.x, max.y, max.z),
            };
        }
    }
}
