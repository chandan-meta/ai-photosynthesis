# New chamber environment (the user's "Ai Photoshynthesis1.fbx") -> Unity: Assets/Photosynthesis/Art/Environment/Chamber.fbx + Textures/
#   blender -b --factory-startup -P Tools/Chamber/build_env.py
# The source FBX is only read. Changes for the game:
#   * the centre planter keeps its white rim and steps, but its own soil fill is cut out: the game's soil bed (the hero plant's) sits there;
#   * the floor disc is rebuilt as "Floor Inner" (planter wall r 2.9 to 6.4, fades for the roots x-ray; open under the soil like
#     the old platform) + "Floor Outer", with the same planar UVs;
#   * the five microscopes (19k triangles each) are decimated;
#   * textures are written next to the FBX at 2048 (architecture) / 1024 (props, plants) so Unity imports them with their own settings;
#     a separate opacity map (the acid/base glassware) is merged into its base colour's alpha, which Unity reads.
import bpy, bmesh, math, os, re
import numpy as np
from mathutils import Vector

SRC = r"C:/Users/ADMIN/Documents/Received Files/Ai Photoshynthesis1.fbx"
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, "..", "..", "Assets", "Photosynthesis", "Art", "Environment"))
TEX = os.path.join(OUT, "Textures")
SOIL_R, PLANTER_WALL, FLOOR_SPLIT = 2.85, 2.9, 6.4
BIG = ("Roof1", "Floor1", "Outline1", "Rack_and_Beam", "Wood_and_UI", "Console_and_Monitor", "Sci_fi_Table4", "Door", "UI1", "Ui2")

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
sc = bpy.context.scene
O = {o.name: o for o in sc.objects}

# ---- centre planter: cut out the soil fill (faces centred inside the rim's inner wall at r ≈ 2.9, above the floor)
con = O["Console"]; bm = bmesh.new(); bm.from_mesh(con.data); mw = con.matrix_world
soil = [f for f in bm.faces if (lambda c: math.hypot(c.x, c.y) < SOIL_R and c.z > 0.3)(mw @ f.calc_center_median())]
bmesh.ops.delete(bm, geom=soil, context='FACES'); bm.to_mesh(con.data); bm.free()
print(f"[env] planter: removed {len(soil)} soil faces inside r {SOIL_R}")

# ---- floor: inner / outer annuli with the original planar UV mapping
fl = O["Floor"]; me = fl.data; uvl = me.uv_layers.active
P, U = [], []
for poly in me.polygons:
    for li in poly.loop_indices:
        v = fl.matrix_world @ me.vertices[me.loops[li].vertex_index].co; P.append((v.x, v.y, 1)); U.append(uvl.data[li].uv[:])
A = np.linalg.lstsq(np.array(P), np.array(U), rcond=None)[0]
R_OUT = max(math.hypot(*(fl.matrix_world @ v.co).xy) for v in me.vertices)
def annulus(name, r0, r1, segs=160):
    bm = bmesh.new(); uv = bm.loops.layers.uv.new("UVMap"); ring = []
    for r in (r0, r1):
        ring.append([bm.verts.new((r * math.cos(a), r * math.sin(a), 0.0)) for a in (2 * math.pi * i / segs for i in range(segs))])
    for i in range(segs):
        j = (i + 1) % segs
        f = bm.faces.new((ring[0][i], ring[0][j], ring[1][j], ring[1][i]))
        for l in f.loops: l[uv].uv = tuple(np.array([l.vert.co.x, l.vert.co.y, 1.0]) @ A)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for f in bm.faces:
        if f.normal.z < 0: f.normal_flip()
    m = bpy.data.meshes.new(name); bm.to_mesh(m); bm.free(); m.materials.append(me.materials[0])
    o = bpy.data.objects.new(name, m); o.parent = fl.parent; o.matrix_parent_inverse = fl.parent.matrix_world.inverted()   # verts are in world metres
    sc.collection.objects.link(o); return o
annulus("Floor Inner", PLANTER_WALL, FLOOR_SPLIT); annulus("Floor Outer", FLOOR_SPLIT, R_OUT)
print(f"[env] floor: inner r {PLANTER_WALL}-{FLOOR_SPLIT}, outer to r {R_OUT:.2f}")
bpy.data.objects.remove(fl)

# ---- microscopes: 19k triangles each -> ~4k
for o in [o for o in sc.objects if o.type == 'MESH' and o.name.startswith("Microscope")]:
    m = o.modifiers.new("dec", 'DECIMATE'); m.ratio = 0.2
    dg = bpy.context.evaluated_depsgraph_get(); new = bpy.data.meshes.new_from_object(o.evaluated_get(dg)); o.modifiers.remove(m); old = o.data; o.data = new
    if old.users == 0: bpy.data.meshes.remove(old)

# ---- separate opacity maps -> alpha of the base colour (Unity's FBX import ignores a TransparencyFactor map)
for m in bpy.data.materials:
    b = m.node_tree and next((n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
    if not b or not (b.inputs['Alpha'].is_linked and b.inputs['Base Color'].is_linked): continue
    a, c = b.inputs['Alpha'].links[0].from_node, b.inputs['Base Color'].links[0].from_node
    if a.type != 'TEX_IMAGE' or c.type != 'TEX_IMAGE' or not a.image or not c.image: continue
    if os.path.basename(a.image.filepath).lower() == os.path.basename(c.image.filepath).lower(): continue   # alpha already in the base file
    if not (a.image.packed_file or os.path.exists(bpy.path.abspath(a.image.filepath))): continue
    op = a.image.copy(); op.scale(*c.image.size)
    px = np.array(c.image.pixels[:], np.float32).reshape(-1, 4); px[:, 3] = np.array(op.pixels[:], np.float32).reshape(-1, 4)[:, 0]
    w, h = c.image.size; new = bpy.data.images.new(c.image.name + " rgba", w, h, alpha=True)   # a fresh RGBA image: save() keeps a source's RGB
    new.pixels = px.ravel(); new.file_format = 'PNG'; os.makedirs(TEX, exist_ok=True)
    new.filepath_raw = os.path.join(TEX, os.path.splitext(os.path.basename(c.image.filepath))[0] + ".png"); new.save()
    c.image = new; bpy.data.images.remove(op)
    print(f"[env] {m.name}: opacity {a.image.name} -> alpha of {c.image.name}")

# ---- textures: one file per source image, capped by use
os.makedirs(TEX, exist_ok=True)
users = {}
for m in bpy.data.materials:
    if m.node_tree:
        for n in m.node_tree.nodes:
            if n.type == 'TEX_IMAGE' and n.image: users.setdefault(n.image, set()).add(m.name)
written = {}
for img, mats in users.items():
    src = bpy.path.abspath(img.filepath) or img.name
    base = re.sub(r"[^\w\-. ]", "_", os.path.splitext(os.path.basename(src))[0])[:80]
    key = src.lower()
    if key in written: img.filepath = written[key]; continue
    if not img.packed_file and not os.path.exists(src): print(f"[env] skipped missing image {img.name} ({src})"); continue
    cap = 2048 if any(b in mats for b in BIG) else 1024
    w, h = img.size
    if max(w, h) > cap: img.scale(cap * w // max(w, h), cap * h // max(w, h))
    ext = ".png" if (img.file_format == 'PNG' or src.lower().endswith(".png")) else ".jpg"
    path = os.path.join(TEX, base + ext)
    img.filepath_raw = path; img.file_format = 'PNG' if ext == ".png" else 'JPEG'; img.save()
    written[key] = path; img.filepath = path

tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in sc.objects if o.type == 'MESH')
print(f"[env] {sum(1 for o in sc.objects if o.type == 'MESH')} meshes, {tris} triangles, {len(written)} textures -> {TEX}")
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, "Chamber.fbx"), use_selection=False, object_types={'EMPTY', 'MESH'},
    axis_forward='-Z', axis_up='Y', apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', mesh_smooth_type='OFF',
    use_mesh_modifiers=False, path_mode='STRIP', embed_textures=False, add_leaf_bones=False, bake_anim=False, use_custom_props=False)
print("[env] wrote", os.path.join(OUT, "Chamber.fbx"))
