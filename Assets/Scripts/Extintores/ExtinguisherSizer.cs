using UnityEngine;

namespace Extintores
{
    /// <summary>
    /// Escala el modelo del extintor para que mida unas dimensiones REALES dadas
    /// (diámetro del cuerpo y altura total), en metros. Mide los bounds combinados de
    /// los renderers hijos y calcula la escala de ESTE transform.
    ///
    /// Va en el nodo "Visual" (padre del modelo + GripPoint + NozzleTip): al escalar aquí,
    /// los marcadores acompañan al modelo y la alineación del grip con el mando se conserva.
    ///
    /// Nota sobre el diámetro: para que el diámetro sea independiente de la altura sin
    /// deformar (shear), el eje largo del modelo (el cilindro del cuerpo) debe coincidir con
    /// uno de los ejes locales de este nodo. Si el modelo trae una rotación "de fábrica" que
    /// lo deja diagonal, endereza el modelo (rotación local a 0 o a un múltiplo de 90°) o
    /// deja el diámetro = altura·proporción usando <see cref="uniform"/>.
    /// </summary>
    [ExecuteAlways]
    public class ExtinguisherSizer : MonoBehaviour
    {
        public enum Axis { Auto, X, Y, Z }

        [Header("Medidas reales (metros)")]
        [Tooltip("Altura total del extintor (cuerpo, sin contar la manguera).")]
        [SerializeField, Min(0.001f)] private float heightMeters = 0.48f;

        [Tooltip("Diámetro del cuerpo cilíndrico. Se ignora si 'uniform' está activo.")]
        [SerializeField, Min(0.001f)] private float diameterMeters = 0.15f;

        [Tooltip("Escala uniforme por altura (respeta la proporción original del modelo). " +
                 "Úsalo si el diámetro sale deformado.")]
        [SerializeField] private bool uniform = false;

        [Header("Ejes del modelo")]
        [Tooltip("Eje LOCAL de este nodo que corresponde a la ALTURA del extintor. " +
                 "'Auto' toma el eje más largo de los bounds medidos.")]
        [SerializeField] private Axis heightAxis = Axis.Auto;

        [Tooltip("Solo se mide el modelo bajo este nodo (el cuerpo del extintor). Deja fuera de " +
                 "la medición la boquilla u otros añadidos. Si queda vacío, se mide todo lo que " +
                 "cuelga de este objeto.")]
        [SerializeField] private Transform modelRoot;

        [Tooltip("Recalcula la escala en el editor al cambiar los valores.")]
        [SerializeField] private bool applyInEditor = true;

        private void OnEnable() => Apply();

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!applyInEditor || Application.isPlaying) return;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null) Apply();
            };
        }
#endif

        /// <summary>Mide el modelo y aplica la escala para casar altura/diámetro objetivo.</summary>
        [ContextMenu("Aplicar medidas")]
        public void Apply()
        {
            transform.localScale = Vector3.one;             // medir con escala neutra
            if (!TryMeasureLocalSize(out Vector3 size)) return;

            int h = heightAxis == Axis.Auto ? LongestAxis(size) : (int)heightAxis - 1;

            float natHeight = size[h];
            if (natHeight <= 1e-6f) return;
            float scaleH = heightMeters / natHeight;

            float scaleD = scaleH;                          // uniforme por defecto
            if (!uniform)
            {
                float natDiam = 0f;                         // mayor de los dos ejes radiales
                for (int i = 0; i < 3; i++) if (i != h) natDiam = Mathf.Max(natDiam, size[i]);
                if (natDiam > 1e-6f) scaleD = diameterMeters / natDiam;
            }

            Vector3 scale = Vector3.one;
            for (int i = 0; i < 3; i++) scale[i] = (i == h) ? scaleH : scaleD;
            transform.localScale = scale;
        }

        /// <summary>
        /// Tamaño del modelo (bounds combinados de los renderers hijos) en el espacio local
        /// de ESTE nodo, con su escala ya neutralizada. Devuelve false si no hay renderers.
        /// </summary>
        private bool TryMeasureLocalSize(out Vector3 size)
        {
            size = Vector3.zero;
            Transform source = modelRoot != null ? modelRoot : transform;
            var renderers = source.GetComponentsInChildren<Renderer>(false);
            if (renderers.Length == 0) return false;

            Matrix4x4 toLocal = transform.worldToLocalMatrix;
            bool has = false;
            Bounds local = default;

            foreach (var r in renderers)
            {
                var mf = r.GetComponent<MeshFilter>();
                Mesh mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null) continue;

                Bounds mb = mesh.bounds;                    // local a la malla
                Matrix4x4 m = toLocal * r.transform.localToWorldMatrix;
                Vector3 c = mb.center, e = mb.extents;

                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = c + new Vector3(
                        (i & 1) == 0 ? -e.x : e.x,
                        (i & 2) == 0 ? -e.y : e.y,
                        (i & 4) == 0 ? -e.z : e.z);
                    Vector3 p = m.MultiplyPoint3x4(corner);
                    if (!has) { local = new Bounds(p, Vector3.zero); has = true; }
                    else local.Encapsulate(p);
                }
            }

            if (!has) return false;
            size = local.size;
            return true;
        }

        private static int LongestAxis(Vector3 v)
        {
            int idx = 0;
            if (v.y > v[idx]) idx = 1;
            if (v.z > v[idx]) idx = 2;
            return idx;
        }
    }
}
