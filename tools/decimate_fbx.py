"""Decima un FBX con Blender headless conservando nombres, materiales y UVs.

Uso:
    blender --background --python decimate_fbx.py -- <entrada.fbx> <salida.fbx> <tris_objetivo>

Conserva los nombres de objeto y de malla porque Unity referencia los sub-assets del FBX por
nombre (fileIDToRecycleName en el .meta): si cambian, los prefabs que usan esas mallas quedan
apuntando a nada y el modelo desaparece de la escena.
"""
import sys
import bpy


def argv_after_ddash():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def tri_count(obj):
    """Triángulos de una malla, contando ngons como (verts - 2)."""
    return sum(max(0, len(p.vertices) - 2) for p in obj.data.polygons)


def main():
    args = argv_after_ddash()
    if len(args) < 3:
        print("ERROR: faltan argumentos (entrada, salida, tris_objetivo)")
        sys.exit(1)

    src, dst, target = args[0], args[1], int(args[2])

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=src)

    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if not meshes:
        print("ERROR: el FBX no trajo ninguna malla")
        sys.exit(1)

    total_before = sum(tri_count(o) for o in meshes)
    print(f"[decimate] mallas: {len(meshes)}  triangulos originales: {total_before:,}")

    for obj in meshes:
        before = tri_count(obj)
        if before == 0:
            continue

        # Reparto el presupuesto proporcionalmente al tamaño de cada malla.
        share = max(64, int(target * before / total_before))
        ratio = min(1.0, share / before)
        if ratio >= 0.999:
            print(f"[decimate] '{obj.name}': {before:,} tri, ya esta por debajo del objetivo")
            continue

        mesh_name = obj.data.name  # Unity referencia por este nombre

        bpy.context.view_layer.objects.active = obj
        mod = obj.modifiers.new(name="Decimate", type="DECIMATE")
        mod.decimate_type = "COLLAPSE"
        mod.ratio = ratio
        mod.use_collapse_triangulate = False  # deja que respete los quads donde pueda
        bpy.ops.object.modifier_apply(modifier=mod.name)

        obj.data.name = mesh_name
        print(f"[decimate] '{obj.name}': {before:,} -> {tri_count(obj):,} tri (ratio {ratio:.5f})")

    total_after = sum(tri_count(o) for o in meshes)
    print(f"[decimate] TOTAL: {total_before:,} -> {total_after:,} triangulos")

    bpy.ops.export_scene.fbx(
        filepath=dst,
        use_selection=False,
        apply_unit_scale=True,
        bake_space_transform=False,
        object_types={"MESH", "EMPTY"},
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        path_mode="COPY",          # empaqueta las texturas junto al FBX
        embed_textures=False,
        axis_forward="-Z",
        axis_up="Y",
    )
    print(f"[decimate] exportado: {dst}")


main()
