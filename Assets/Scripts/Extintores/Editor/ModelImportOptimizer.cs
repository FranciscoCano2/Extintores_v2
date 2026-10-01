using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Extintores.EditorTools
{
    /// <summary>
    /// Ajusta los importadores de los FBX de las causas de fuego para que pesen menos en el build de
    /// Quest, y reporta vértices/triángulos de cada uno.
    ///
    /// El reporte es la parte importante: <c>meshCompression</c> reduce el tamaño del archivo en el
    /// build pero NO los triángulos que la GPU dibuja cada frame. El modelo de clase A
    /// (<c>Cardboard_Box</c>, 64 MB) es un escaneo de Tripo y puede traer cientos de miles de
    /// triángulos; con el número a la vista se decide si hace falta decimarlo o reemplazarlo.
    ///
    /// Uso: menú <b>Extintores ▸ Optimize FireCause Models</b>. Idempotente.
    /// </summary>
    public static class ModelImportOptimizer
    {
        private const string CausesFolder = "Assets/ThirdParty/FireCauses";

        /// <summary>Umbral orientativo por prop para VR a 72–90 Hz.</summary>
        private const int TriWarnThreshold = 50000;

        [MenuItem("Extintores/Optimize FireCause Models", priority = 210)]
        public static void Optimize()
        {
            var guids = AssetDatabase.FindAssets("t:Model", new[] { CausesFolder });
            if (guids.Length == 0)
            {
                Debug.LogWarning($"[ModelImportOptimizer] No se encontraron modelos en '{CausesFolder}'.");
                return;
            }

            var rows = new List<string>();
            int changed = 0, heavy = 0;

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var im = AssetImporter.GetAtPath(path) as ModelImporter;
                if (im == null) continue;

                if (ApplySettings(im))
                {
                    im.SaveAndReimport();
                    changed++;
                }

                CountMesh(path, out int verts, out int tris);
                bool isHeavy = tris > TriWarnThreshold;
                if (isHeavy) heavy++;

                rows.Add($"  {(isHeavy ? "⚠" : " ")} {System.IO.Path.GetFileName(path),-46} " +
                         $"{verts,9:N0} vért  {tris,9:N0} tri");
            }

            var sb = new StringBuilder();
            sb.AppendLine($"[ModelImportOptimizer] {guids.Length} modelo(s), {changed} reimportado(s):");
            rows.Sort();
            foreach (var r in rows) sb.AppendLine(r);
            sb.AppendLine();
            sb.AppendLine("Aplicado: meshCompression=High, optimizeMesh, isReadable=off, sin cámaras/luces/animación.");

            if (heavy > 0)
            {
                sb.Append($"⚠ {heavy} modelo(s) superan {TriWarnThreshold:N0} triángulos. meshCompression achica el " +
                          "archivo, NO el costo de dibujarlos: si el framerate en el visor cae, hay que decimar " +
                          "esos modelos (Blender ▸ Decimate) o reemplazarlos por uno low-poly.");
                Debug.LogWarning(sb.ToString());
            }
            else
            {
                Debug.Log(sb.ToString());
            }
        }

        /// <summary>Devuelve true si hubo algún cambio (evita reimportar de gusto al re-ejecutar).</summary>
        private static bool ApplySettings(ModelImporter im)
        {
            bool dirty = false;

            if (im.meshCompression != ModelImporterMeshCompression.High) { im.meshCompression = ModelImporterMeshCompression.High; dirty = true; }
            if (!im.optimizeMeshPolygons) { im.optimizeMeshPolygons = true; dirty = true; }
            if (!im.optimizeMeshVertices) { im.optimizeMeshVertices = true; dirty = true; }
            if (im.isReadable) { im.isReadable = false; dirty = true; }          // ahorra una copia en RAM
            if (im.importCameras) { im.importCameras = false; dirty = true; }
            if (im.importLights) { im.importLights = false; dirty = true; }
            if (im.importVisibility) { im.importVisibility = false; dirty = true; }
            if (im.animationType != ModelImporterAnimationType.None) { im.animationType = ModelImporterAnimationType.None; dirty = true; }

            return dirty;
        }

        private static void CountMesh(string path, out int verts, out int tris)
        {
            verts = 0;
            tris = 0;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (o is not Mesh m) continue;
                verts += m.vertexCount;

                // GetIndexCount es metadato del submesh: funciona aunque el importador tenga
                // isReadable = false, a diferencia de mesh.triangles.
                for (int i = 0; i < m.subMeshCount; i++)
                {
                    var topo = m.GetTopology(i);
                    uint n = m.GetIndexCount(i);
                    if (topo == MeshTopology.Triangles) tris += (int)(n / 3);
                    else if (topo == MeshTopology.Quads) tris += (int)(n / 4) * 2;
                }
            }
        }
    }
}
