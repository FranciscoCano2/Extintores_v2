"""
Extintor ABC de 1 kg (de auto) — modelo paramétrico a partir de las medidas del extintor real.

Genera, desde las medidas de P (abajo):
  * Assets/Extintores/Extinguisher/Real/Extintor_1kg.fbx  — juego (Quest): objetos Body (tintable),
    Details (bronce / cromo / manómetro, con atlas), Label (banda para la etiqueta) y el empty
    HoseOutlet en la salida de la válvula.
  * Assets/Extintores/Extinguisher/Real/Boquilla.fbx       — boquilla de la mano izquierda (Nozzle),
    origen en la PUNTA, empty HoseInlet en la entrada de la manguera.
  * Assets/Extintores/Extinguisher/Real/T_Extinguisher_Details.png — atlas de 512 px.
  * models/print/extintor_1kg.stl                           — impresión 3D: un solo sólido cerrado
    (manifold), en MILÍMETROS.

Correr con el editor de Unity cerrado o abierto (no toca la escena), desde la raíz del repo:
    powershell -ExecutionPolicy Bypass -File tools\\blender\\build-extintor.ps1 [-Render]

Convención (Blender, Z arriba, metros): pivote en el centro del fondo, eje del tanque = +Z, palanca
y manija hacia +X, salida de la válvula hacia −X, manómetro al frente (−Y) y etiqueta girada
label_turn_deg desde ahí (atrás, +Y: del lado que mira al usuario). En Unity el
editor tool (ExtinguisherModelTool) NO asume cómo convierte el FBX los ejes: mide el eje, la salida
y la cabeza sobre la malla importada.

Las medidas marcadas "medida" son del extintor real; las "estimada" salen de las fotos, con la
escala tomada del diámetro del tanque. Para afinarlas, cambiar P y volver a correr.
"""

import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

# --------------------------------------------------------------------------- medidas (metros)
P = dict(
    height_total=0.348,       # medida: piso → punta de la palanca
    body_d=0.0765,            # medida: diámetro del tanque
    outlet_z=0.298,           # medida: altura de la salida de la válvula
    bottom_fillet=0.008,      # estimada: radio del canto del fondo
    shoulder_z=0.255,         # estimada: donde termina el cilindro y empieza el domo
    neck_z=0.290,             # estimada: unión domo ↔ válvula
    neck_r=0.012,             # estimada
    valve_r=0.013,            # estimada: cuerpo de la válvula (rojo, se tiñe)
    valve_top=0.327,          # estimada
    outlet_r=0.0055,          # estimada: pico de bronce
    outlet_len=0.020,         # estimada: cuánto sobresale de la válvula
    gauge_d=0.033,            # estimada: manómetro
    gauge_z=0.287,            # estimada: centro del manómetro
    gauge_x=0.006,            # estimada: corrido hacia la palanca (foto de frente)
    gauge_depth=0.010,        # estimada
    gauge_front_y=-0.036,     # estimada: cara del manómetro (−Y = frente)
    lever_w=0.014,            # estimada: ancho de la palanca
    lever_t=0.006,            # estimada: alto de la sección vista de frente (≥ 2,5 mm para imprimir)
    lever_path=[(-0.006, 0.3385), (0.030, 0.345), (0.060, 0.344), (0.082, 0.3375)],   # estimada (x, z)
    handle_w=0.016,           # estimada: manija de transporte
    handle_t=0.009,           # estimada: es un perfil en U; de frente se ve ~9 mm
    handle_path=[(0.008, 0.320), (0.030, 0.307), (0.050, 0.289), (0.068, 0.268), (0.085, 0.245), (0.098, 0.225), (0.104, 0.214)],  # estimada
    label_arc_deg=140.0,      # banda de la etiqueta (las texturas son cuadradas)
    label_turn_deg=180.0,     # giro de la etiqueta sobre el eje, desde el frente (manómetro); + = hacia la
                              # palanca (+X). En el real está al frente, pero así quedaba del lado de
                              # afuera de la mano y no se leía: va del lado que mira al usuario.
    label_center_z=0.140,
    label_lift=0.0006,        # despegue de la etiqueta sobre la chapa (evita z-fighting)
    # Boquilla (no existe en el real de 1 kg: tipo ABC de 5 kg)
    nozzle_len=0.200,
    nozzle_profile=[          # (r, d) con d = distancia hacia atrás desde la punta
        (0.0, 0.0), (0.0060, 0.0), (0.0080, 0.004), (0.0095, 0.045), (0.0118, 0.060),
        (0.0122, 0.110), (0.0118, 0.160), (0.0100, 0.166), (0.0105, 0.170), (0.0105, 0.196),
        (0.0085, 0.200), (0.0, 0.200),
    ],
)

SIDES_GAME = 32
SIDES_PRINT = 64
ATLAS_PX = 512

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT_DIR = os.path.join(ROOT, "Assets", "Extintores", "Extinguisher", "Real")
PRINT_DIR = os.path.join(ROOT, "models", "print")
ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
RENDER_DIR = None
if "--render" in ARGS:
    i = ARGS.index("--render")
    RENDER_DIR = os.path.abspath(ARGS[i + 1] if i + 1 < len(ARGS) else os.path.join(ROOT, "Logs", "blender-render"))

# Regiones del atlas (u0, v0, u1, v1)
UV_DIAL = (0.0, 0.5, 0.5, 1.0)
UV_BRASS = (0.5, 0.75, 0.75, 1.0)
UV_CHROME = (0.75, 0.75, 1.0, 1.0)
UV_BLACK = (0.5, 0.5, 0.75, 0.75)


# --------------------------------------------------------------------------- primitivas (bmesh)
def revolve(bm, profile, sides, axis_matrix=Matrix.Identity(4)):
    """Sólido de revolución alrededor de Z. profile = [(r, z)], con r = 0 en las puntas → polos (cerrado)."""
    rings = []
    for r, z in profile:
        if r <= 1e-9:
            rings.append([bm.verts.new(axis_matrix @ Vector((0, 0, z)))])
        else:
            ring = []
            for k in range(sides):
                a = 2 * math.pi * k / sides
                ring.append(bm.verts.new(axis_matrix @ Vector((r * math.cos(a), r * math.sin(a), z))))
            rings.append(ring)
    faces = []
    for ra, rb in zip(rings, rings[1:]):
        if len(ra) == 1 and len(rb) == 1:
            continue
        for k in range(sides):
            k2 = (k + 1) % sides
            if len(ra) == 1:
                vs = (ra[0], rb[k], rb[k2])
            elif len(rb) == 1:
                vs = (ra[k], rb[0], ra[k2])
            else:
                vs = (ra[k], rb[k], rb[k2], ra[k2])
            # orientación hacia afuera: se recalcula al final
            faces.append(bm.faces.new(vs))
    return faces


def cylinder(bm, p0, p1, r, sides):
    """Cilindro cerrado entre p0 y p1 (Vector)."""
    d = (p1 - p0)
    rot = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix().to_4x4()
    m = Matrix.Translation(p0) @ rot
    return revolve(bm, [(0, 0), (r, 0), (r, d.length), (0, d.length)], sides, m)


def strip(bm, path_xz, width, thick, y=0.0):
    """Barra de sección rectangular a lo largo de un camino en el plano XZ (palanca, manija). Cerrada."""
    pts = [Vector((x, y, z)) for x, z in path_xz]
    sections = []
    for i, p in enumerate(pts):
        a = pts[max(i - 1, 0)]
        b = pts[min(i + 1, len(pts) - 1)]
        t = (b - a).normalized()
        n = Vector((0, 1, 0)).cross(t).normalized()  # normal en el plano XZ
        w = Vector((0, width / 2, 0))
        h = n * (thick / 2)
        sections.append([bm.verts.new(p - w - h), bm.verts.new(p + w - h),
                         bm.verts.new(p + w + h), bm.verts.new(p - w + h)])
    faces = []
    for sa, sb in zip(sections, sections[1:]):
        for k in range(4):
            k2 = (k + 1) % 4
            faces.append(bm.faces.new((sa[k], sb[k], sb[k2], sa[k2])))
    faces.append(bm.faces.new(list(reversed(sections[0]))))
    faces.append(bm.faces.new(sections[-1]))
    return faces


def box(bm, mn, mx):
    v = [bm.verts.new((x, y, z)) for x in (mn[0], mx[0]) for y in (mn[1], mx[1]) for z in (mn[2], mx[2])]
    q = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
    return [bm.faces.new([v[i] for i in f]) for f in q]


def to_object(bm, name, smooth_angle=40.0):
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    me.shade_smooth()
    me.set_sharp_from_angle(angle=math.radians(smooth_angle))
    return obj


def uv_solid(obj, region, faces_filter=None):
    """Todas las caras (o las filtradas) al centro de una región del atlas: color liso."""
    me = obj.data
    if not me.uv_layers:
        me.uv_layers.new(name="UVMap")
    uv = me.uv_layers.active.data
    cu = (region[0] + region[2]) / 2
    cv = (region[1] + region[3]) / 2
    for poly in me.polygons:
        if faces_filter and not faces_filter(poly):
            continue
        for li in poly.loop_indices:
            uv[li].uv = (cu, cv)


# --------------------------------------------------------------------------- piezas
def body_profile(sides):
    R = P["body_d"] / 2
    f = P["bottom_fillet"]
    prof = [(0.0, 0.0), (R - f, 0.0)]
    for k in range(1, 5):                          # canto del fondo
        a = -math.pi / 2 + (math.pi / 2) * k / 4
        prof.append((R - f + f * math.cos(a), f + f * math.sin(a)))
    prof.append((R, P["shoulder_z"]))
    dome_h = P["neck_z"] - P["shoulder_z"]
    for k in range(1, 9):                          # domo elíptico hasta el cuello
        a = (math.pi / 2) * k / 8
        r = P["neck_r"] + (R - P["neck_r"]) * math.cos(a)
        prof.append((r, P["shoulder_z"] + dome_h * math.sin(a)))
    prof.append((P["neck_r"], P["neck_z"] + 0.002))
    prof.append((0.0, P["neck_z"] + 0.002))
    return prof


def build_body(sides, for_print=False):
    """Cuerpo + válvula + palanca + manija: todo lo que se pinta del color del agente."""
    bm = bmesh.new()
    revolve(bm, body_profile(sides), sides)
    vs = max(12, sides // 2)
    revolve(bm, [(0, P["neck_z"] - 0.002), (P["valve_r"], P["neck_z"] - 0.002),
                 (P["valve_r"], P["valve_top"] - 0.003), (P["valve_r"] - 0.003, P["valve_top"]),
                 (0, P["valve_top"])], vs)
    # bloque del pivote de la palanca, arriba de la válvula
    box(bm, (-0.009, -0.0085, P["valve_top"] - 0.004), (0.016, 0.0085, P["valve_top"] + 0.012))
    strip(bm, P["lever_path"], P["lever_w"], max(P["lever_t"], 0.0025))
    strip(bm, P["handle_path"], P["handle_w"], max(P["handle_t"], 0.0025))
    return bm


def build_details(sides, for_print=False):
    """Pico de bronce + tuerca + manómetro (aro, esfera y vástago). Devuelve (bm, dial_face_ids)."""
    bm = bmesh.new()
    oz = P["outlet_z"]
    x0 = -P["valve_r"] + 0.002
    x1 = -P["valve_r"] - P["outlet_len"]
    cylinder(bm, Vector((x0, 0, oz)), Vector((x1, 0, oz)), P["outlet_r"], 12)
    # tuerca: arranca DENTRO de la válvula; apoyada justo en su superficie deja aristas no-manifold
    cylinder(bm, Vector((x0, 0, oz)), Vector((x0 - 0.009, 0, oz)), P["outlet_r"] + 0.0025, 6)
    gz, gx = P["gauge_z"], P["gauge_x"]
    gy1 = P["gauge_front_y"]
    gy0 = gy1 + P["gauge_depth"]
    cylinder(bm, Vector((gx, -P["valve_r"] + 0.002, gz)), Vector((gx, gy0, gz)), 0.0035, 8)  # vástago
    cylinder(bm, Vector((gx, gy0, gz)), Vector((gx, gy1, gz)), P["gauge_d"] / 2, 24 if not for_print else 48)
    return bm


def build_label(sides):
    R = P["body_d"] / 2 + P["label_lift"]
    arc = math.radians(P["label_arc_deg"])
    h = arc * R                                    # alto = largo del arco: la textura es cuadrada
    z0, z1 = P["label_center_z"] - h / 2, P["label_center_z"] + h / 2
    n = 12
    bm = bmesh.new()
    uv_layer = bm.loops.layers.uv.new("UVMap")
    cols = []
    for k in range(n + 1):
        u = k / n
        # centrada en −Y (frente) + label_turn_deg; u crece en sentido antihorario visto desde arriba,
        # que para quien la mira de afuera es siempre hacia la derecha: el texto no queda espejado.
        a = -math.pi / 2 + math.radians(P["label_turn_deg"]) - arc / 2 + arc * u
        cols.append((u, bm.verts.new((R * math.cos(a), R * math.sin(a), z0)),
                     bm.verts.new((R * math.cos(a), R * math.sin(a), z1))))
    for (ua, a0, a1), (ub, b0, b1) in zip(cols, cols[1:]):
        f = bm.faces.new((a0, b0, b1, a1))
        for loop, uvv in zip(f.loops, ((ua, 0), (ub, 0), (ub, 1), (ua, 1))):
            loop[uv_layer].uv = uvv
    me = bpy.data.meshes.new("Label")
    bm.normal_update()
    # la cara tiene que mirar hacia afuera del tanque
    for f in bm.faces:
        c = f.calc_center_median()
        if f.normal.dot(Vector((c.x, c.y, 0))) < 0:
            f.normal_flip()
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new("Label", me)
    bpy.context.scene.collection.objects.link(obj)
    me.shade_smooth()
    return obj


def build_nozzle(sides):
    """Boquilla a lo largo de +Y: punta en el origen, entrada de la manguera en y = nozzle_len."""
    bm = bmesh.new()
    to_y = Matrix.Rotation(-math.pi / 2, 4, "X")   # Z del perfil → +Y
    revolve(bm, [(r, d) for r, d in P["nozzle_profile"]], sides, to_y)
    return bm


# --------------------------------------------------------------------------- atlas
def build_atlas(path):
    N = ATLAS_PX
    px = [0.0] * (N * N * 4)

    def put(x, y, c):
        i = (y * N + x) * 4
        px[i:i + 4] = c

    def fill(region, c):
        u0, v0, u1, v1 = region
        for y in range(int(v0 * N), int(v1 * N)):
            for x in range(int(u0 * N), int(u1 * N)):
                put(x, y, c)

    fill((0, 0, 1, 1), (0.05, 0.05, 0.05, 1))
    fill(UV_BRASS, (0.78, 0.60, 0.25, 1))
    fill(UV_CHROME, (0.80, 0.81, 0.83, 1))
    fill(UV_BLACK, (0.04, 0.04, 0.04, 1))
    # esfera del manómetro: blanco, arco rojo-verde-rojo, aguja en verde (cargado)
    u0, v0, u1, v1 = UV_DIAL
    cx, cy = (u0 + u1) / 2 * N, (v0 + v1) / 2 * N
    rad = (u1 - u0) / 2 * N
    for y in range(int(v0 * N), int(v1 * N)):
        for x in range(int(u0 * N), int(u1 * N)):
            dx, dy = x + 0.5 - cx, y + 0.5 - cy
            d = math.hypot(dx, dy) / rad
            ang = math.degrees(math.atan2(dy, dx))     # 0° = derecha, 90° = arriba
            c = (0.80, 0.81, 0.83, 1)                  # aro cromado
            if d < 0.86:
                c = (0.95, 0.95, 0.93, 1)
                if 0.62 < d < 0.78 and -30 <= ang <= 210:
                    if 60 <= ang <= 120:
                        c = (0.15, 0.65, 0.20, 1)      # zona verde arriba
                    else:
                        c = (0.80, 0.12, 0.10, 1)
                # aguja hacia arriba (verde), del centro al arco
                if abs(dx) < rad * 0.025 and 0 <= dy < rad * 0.7:
                    c = (0.05, 0.05, 0.05, 1)
                if d < 0.07:
                    c = (0.05, 0.05, 0.05, 1)
            put(x, y, c)
    img = bpy.data.images.new("T_Extinguisher_Details", N, N, alpha=False)
    img.pixels = px
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    return img


def uv_dial(obj):
    """Proyección plana de la cara frontal del manómetro sobre la región de la esfera."""
    me = obj.data
    if not me.uv_layers:
        me.uv_layers.new(name="UVMap")
    uv = me.uv_layers.active.data
    gx, gz = P["gauge_x"], P["gauge_z"]
    r = P["gauge_d"] / 2
    u0, v0, u1, v1 = UV_DIAL
    front = P["gauge_front_y"]
    for poly in me.polygons:
        c = poly.center
        if abs(c.y - front) < 1e-4 and poly.normal.y < -0.9:
            for li in poly.loop_indices:
                v = me.vertices[me.loops[li].vertex_index].co
                # visto de frente (desde −Y): +X a la derecha, +Z arriba
                uu = (v.x - gx) / r * 0.5 + 0.5
                vv = (v.z - gz) / r * 0.5 + 0.5
                uv[li].uv = (u0 + (u1 - u0) * uu, v0 + (v1 - v0) * vv)


# --------------------------------------------------------------------------- util
def clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1.0


def material(name, color, metallic=0.0, rough=0.5, image=None):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = color
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = rough
    if image is not None:
        tex = m.node_tree.nodes.new("ShaderNodeTexImage")
        tex.image = image
        m.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    m.diffuse_color = color
    return m


def empty(name, loc):
    e = bpy.data.objects.new(name, None)
    e.empty_display_size = 0.01
    e.location = loc
    bpy.context.scene.collection.objects.link(e)
    return e


def tri_count(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def bbox(objs):
    pts = [o.matrix_world @ v.co for o in objs for v in o.data.vertices]
    mn = [min(p[i] for p in pts) for i in range(3)]
    mx = [max(p[i] for p in pts) for i in range(3)]
    return mn, mx


def export_fbx(path, objs):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH", "EMPTY"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS", bake_space_transform=True,
        axis_forward="-Z", axis_up="Y", mesh_smooth_type="FACE", use_mesh_modifiers=True,
        add_leaf_bones=False, bake_anim=False, path_mode="STRIP", embed_textures=False)


def log(msg):
    print("[extintor_1kg] " + msg, flush=True)


# --------------------------------------------------------------------------- juego (FBX)
def build_game():
    clear()
    atlas_path = os.path.join(OUT_DIR, "T_Extinguisher_Details.png")
    img = build_atlas(atlas_path)

    paint = material("M_Extinguisher_Paint", (0.75, 0.08, 0.06, 1), rough=0.35)
    details = material("M_Extinguisher_Details", (1, 1, 1, 1), metallic=0.6, rough=0.35, image=img)
    label_tex = os.path.join(ROOT, "Assets", "Extintores", "Labels", "Label_ABC.png")
    label_m = material("M_Label_ABC", (1, 1, 1, 1),
                       image=bpy.data.images.load(label_tex) if RENDER_DIR else None)

    body = to_object(build_body(SIDES_GAME), "Body")
    body.data.materials.append(paint)

    det = to_object(build_details(SIDES_GAME), "Details", smooth_angle=35)
    det.data.materials.append(details)
    uv_solid(det, UV_CHROME)
    # bronce: el pico y la tuerca (todo lo que está del lado −X de la válvula)
    uv_solid(det, UV_BRASS, lambda p: p.center.x < -P["valve_r"] + 0.001)
    uv_dial(det)

    label = build_label(SIDES_GAME)
    label.data.materials.append(label_m)

    outlet = empty("HoseOutlet", (-P["valve_r"] - P["outlet_len"], 0, P["outlet_z"]))

    objs = [body, det, label]
    mn, mx = bbox(objs)
    tris = {o.name: tri_count(o) for o in objs}
    log(f"tanque bbox mm: X {mn[0]*1000:.1f}..{mx[0]*1000:.1f}  Y {mn[1]*1000:.1f}..{mx[1]*1000:.1f}  "
        f"Z {mn[2]*1000:.1f}..{mx[2]*1000:.1f}")
    log(f"tanque altura = {(mx[2]-mn[2])*1000:.1f} mm (objetivo {P['height_total']*1000:.1f}), "
        f"diámetro cuerpo = {P['body_d']*1000:.1f} mm")
    log(f"tanque tris: {tris}  total {sum(tris.values())}")
    if abs((mx[2] - mn[2]) - P["height_total"]) > 0.0015:
        log("AVISO: la altura total no coincide con la medida (revisar lever_path)")

    export_fbx(os.path.join(OUT_DIR, "Extintor_1kg.fbx"), objs + [outlet])

    # boquilla, en su propia escena de exportación
    nozzle = to_object(build_nozzle(16), "Nozzle", smooth_angle=50)
    nozzle.data.materials.append(paint)
    inlet = empty("HoseInlet", (0, P["nozzle_len"], 0))
    log(f"boquilla tris: {tri_count(nozzle)}  largo {P['nozzle_len']*1000:.0f} mm")
    export_fbx(os.path.join(OUT_DIR, "Boquilla.fbx"), [nozzle, inlet])
    if RENDER_DIR:
        nozzle.hide_render = True
        render_views(objs, RENDER_DIR, "tanque")
        for o in objs:
            o.hide_render = True
        nozzle.hide_render = False
        nozzle.location = (0.03, -0.10, 0.18)
        render_views([nozzle], RENDER_DIR, "boquilla")


# --------------------------------------------------------------------------- impresión (STL)
def build_print():
    clear()
    parts = [to_object(build_body(SIDES_PRINT, True), "P_Body"),
             to_object(build_details(SIDES_PRINT, True), "P_Details")]
    # cada pieza es un conjunto de sólidos cerrados que se pisan: se separan en islas y se unen
    # con booleana EXACT para que quede UNA sola cáscara manifold.
    islands = []
    for p in parts:
        bpy.ops.object.select_all(action="DESELECT")
        p.select_set(True)
        bpy.context.view_layer.objects.active = p
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.mesh.separate(type="LOOSE")
        bpy.ops.object.mode_set(mode="OBJECT")
        islands += [o for o in bpy.context.selected_objects]
    base = max(islands, key=lambda o: len(o.data.vertices))
    others = [o for o in islands if o is not base]
    coll = bpy.data.collections.new("cutters")
    bpy.context.scene.collection.children.link(coll)
    for o in others:
        for c in o.users_collection:
            c.objects.unlink(o)
        coll.objects.link(o)
    mod = base.modifiers.new("union", "BOOLEAN")
    mod.operation = "UNION"
    mod.solver = "EXACT"
    mod.operand_type = "COLLECTION"
    mod.collection = coll
    bpy.context.view_layer.objects.active = base
    bpy.ops.object.modifier_apply(modifier=mod.name)
    for o in others:
        bpy.data.objects.remove(o)

    bm = bmesh.new()
    bm.from_mesh(base.data)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-7)
    bad_edges = [e for e in bm.edges if not e.is_manifold]
    bad_verts = [v for v in bm.verts if not v.is_manifold]
    for e in bad_edges[:8]:
        c = (e.verts[0].co + e.verts[1].co) / 2
        log(f"  arista no-manifold en mm ({c.x*1000:.1f}, {c.y*1000:.1f}, {c.z*1000:.1f}), caras {len(e.link_faces)}")
    vol = bm.calc_volume(signed=True)
    bm.to_mesh(base.data)
    bm.free()
    mn, mx = bbox([base])
    log(f"STL: {len(base.data.polygons)} triángulos, aristas no-manifold = {len(bad_edges)}, "
        f"vértices no-manifold = {len(bad_verts)}, volumen = {vol*1e6:.1f} cm³")
    log(f"STL bbox mm: {(mx[0]-mn[0])*1000:.1f} × {(mx[1]-mn[1])*1000:.1f} × {(mx[2]-mn[2])*1000:.1f}")
    if bad_edges or vol <= 0:
        log("ERROR: el STL no es un sólido cerrado")

    os.makedirs(PRINT_DIR, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    base.select_set(True)
    bpy.context.view_layer.objects.active = base
    bpy.ops.wm.stl_export(filepath=os.path.join(PRINT_DIR, "extintor_1kg.stl"),
                          export_selected_objects=True, global_scale=1000.0, ascii_format=False,
                          apply_modifiers=True)
    log("STL exportado en milímetros (global_scale 1000).")


# --------------------------------------------------------------------------- renders de control
def render_views(objs, out_dir, tag):
    os.makedirs(out_dir, exist_ok=True)
    scn = bpy.context.scene
    scn.render.engine = "BLENDER_WORKBENCH"
    scn.display.shading.light = "STUDIO"
    scn.display.shading.color_type = "TEXTURE"
    scn.display.shading.show_cavity = False
    scn.render.resolution_x = 700
    scn.render.resolution_y = 900
    scn.render.film_transparent = False
    world = bpy.data.worlds.new("w")
    scn.world = world
    cam_data = bpy.data.cameras.new("cam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = 0.42
    cam = bpy.data.objects.new("cam", cam_data)
    scn.collection.objects.link(cam)
    scn.camera = cam
    target = Vector((0.03, 0, 0.18))
    views = {"frente": Vector((0, -1, 0)), "costado": Vector((1, 0, 0)),
             "atras": Vector((0, 1, 0)), "arriba": Vector((0, -0.001, 1)),
             "tresc": Vector((0.7, -1, 0.5))}
    for name, d in views.items():
        cam.location = target + d.normalized() * 1.0
        cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
        scn.render.filepath = os.path.join(out_dir, f"{tag}_{name}.png")
        bpy.ops.render.render(write_still=True)
    log(f"renders en {out_dir}")


if __name__ == "__main__":
    os.makedirs(OUT_DIR, exist_ok=True)
    build_game()
    build_print()
    log("listo.")
