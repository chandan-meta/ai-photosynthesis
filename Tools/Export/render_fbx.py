import bpy, sys, os
from mathutils import Vector
src, out, name = sys.argv[sys.argv.index("--") + 1:][:3]
bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.fbx(filepath=src); sc = bpy.context.scene
ms = [o for o in sc.objects if o.type == 'MESH']
lo = Vector((1e9,) * 3); hi = Vector((-1e9,) * 3)
for o in ms:
    for c in o.bound_box:
        p = o.matrix_world @ Vector(c); lo = Vector(map(min, lo, p)); hi = Vector(map(max, hi, p))
ctr = (lo + hi) / 2; size = max((hi - lo).length, 1e-3)
print(f"[bin] {name}: {len(ms)} meshes, {sum(len(o.data.polygons) for o in ms)} polys, size {tuple(round(v,3) for v in (hi-lo))}")
sc.render.engine = 'BLENDER_EEVEE'; sc.render.resolution_x, sc.render.resolution_y = 1280, 720
w = bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True; w.node_tree.nodes["Background"].inputs[0].default_value = (0.16, 0.18, 0.2, 1)
sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", 'SUN')); sun.data.energy = 3; sc.collection.objects.link(sun); sun.rotation_euler = (0.9, 0.3, -0.7)
cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); sc.collection.objects.link(cam); sc.camera = cam; cam.data.lens = 35; cam.data.clip_start = size * 0.001; cam.data.clip_end = size * 50
cam.location = ctr + Vector((0.75, -1.0, 0.55)).normalized() * size * 1.1
cam.rotation_euler = (ctr - cam.location).to_track_quat('-Z', 'Y').to_euler()
sc.render.filepath = os.path.join(out, name + ".png"); bpy.ops.render.render(write_still=True)
