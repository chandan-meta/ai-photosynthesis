# The user's green leaf scarf (from D:/AI unity project/charcter/dr.blend) for the classic Dr. Curio:
#   blender -b --factory-startup -P Tools/Curio/scarf.py
# dr.blend is only read. The scarf was modelled in the classic robot's frame (the user built it on the exported classic FBX), so it only
# needs a snug fit: 85 % about the knot, 2 cm back. Writes
#   Assets/Photosynthesis/Resources/Characters/Curio_ScarfMesh.obj   (Curio's rig space once Unity's OBJ import mirrors X)
#   Assets/Photosynthesis/Resources/Characters/Curio_Scarf.png       (the fabric baked to 512 px; empty texels filled for mipmaps)
import bpy, os
import numpy as np
from mathutils import Vector, Matrix

SRC = r"D:/AI unity project/charcter/dr.blend"
OUT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "Photosynthesis", "Resources", "Characters"))
SCARF = "tripo_8ad500c3_61ed_4490_a1a7_0d99d499edda.006"
FIT_SCALE, FIT_BACK = 0.85, 0.02

bpy.ops.wm.open_mainfile(filepath=SRC)
sc = bpy.context.scene
scarf = bpy.data.objects[SCARF]
for o in list(bpy.data.objects):
    if o != scarf: bpy.data.objects.remove(o)
me = scarf.data.copy(); me.transform(scarf.matrix_world); scarf.parent = None; scarf.matrix_world = Matrix(); scarf.data = me
co = np.array([v.co[:] for v in me.vertices]); lo, hi = co.min(0), co.max(0)
knot = Vector(((lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, hi[2]))
me.transform(Matrix.Translation(knot + Vector((0, FIT_BACK, 0))) @ Matrix.Scale(FIT_SCALE, 4) @ Matrix.Translation(-knot))
for p in me.polygons: p.use_smooth = True

# bake the fabric (cloth texture through the user's HSV / curves nodes) to a plain colour map on the scarf's own UVs
cloth = me.materials[0]; nt = cloth.node_tree
os.makedirs(OUT, exist_ok=True)
img = bpy.data.images.new("Curio_Scarf", 512, 512); img.filepath_raw = os.path.join(OUT, "Curio_Scarf.png"); img.file_format = 'PNG'
tn = nt.nodes.new("ShaderNodeTexImage"); tn.image = img; nt.nodes.active = tn
sc.render.engine = 'CYCLES'; sc.cycles.samples = 8; sc.cycles.device = 'CPU'
scarf.select_set(True); bpy.context.view_layer.objects.active = scarf
bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'}, margin=8, use_clear=True)
px = np.array(img.pixels[:], np.float32).reshape(-1, 4); hit = px[:, :3].sum(1) > 0.01
px[~hit, :3] = px[hit, :3].mean(0); px[:, 3] = 1.0   # the islands cover a small part: fill the rest with their mean so mipmaps never darken
img.pixels = px.ravel().tolist(); img.save()

# OBJ: Blender (front -Y, up Z) -> OBJ (front +Z after Unity mirrors X), which is the classic Curio's rig space
scarf.name = "Scarf"
bpy.ops.wm.obj_export(filepath=os.path.join(OUT, "Curio_ScarfMesh.obj"), export_selected_objects=True, forward_axis='NEGATIVE_Z', up_axis='Y',
                      export_uv=True, export_normals=True, export_materials=False, apply_modifiers=True)
print(f"[scarf] {len(me.vertices)} verts, fabric {np.round(px[hit, :3].mean(0) * 255).astype(int).tolist()} (sRGB-ish), written to {OUT}")
