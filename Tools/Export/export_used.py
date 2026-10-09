# blender -b --factory-startup -P export_used.py -- <mode> <out.fbx> [args]
#   chamber <out.fbx>                      : Art/Environment/Chamber.fbx -> FBX with all textures embedded
#   leaf    <out.fbx> <leaf.obj>           : converted ZIP leaf (OBJ+MTL) -> FBX with textures embedded
#   curio   <out.fbx> <kit.fbx> <albedo.png> <scarf.obj> <scarf.png> : classic robot + scarf -> FBX with textures embedded
#   verify  <in.fbx>                       : re-import and report meshes / materials / packed images
import bpy, sys, os
from mathutils import Vector
argv = sys.argv[sys.argv.index("--") + 1:]; mode, out = argv[0], argv[1]
bpy.ops.wm.read_factory_settings(use_empty=True)

def bounds():
    lo = Vector((1e9,) * 3); hi = Vector((-1e9,) * 3)
    for o in bpy.context.scene.objects:
        if o.type != 'MESH': continue
        for c in o.bound_box:
            p = o.matrix_world @ Vector(c); lo = Vector(map(min, lo, p)); hi = Vector(map(max, hi, p))
    return lo, hi
def set_base_tex(mat, path, alpha=False):
    mat.use_nodes = True; nt = mat.node_tree; bsdf = next((n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'), None)
    if not bsdf: return
    img = bpy.data.images.load(path); tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = img
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    if alpha: nt.links.new(tex.outputs["Alpha"], bsdf.inputs["Alpha"])
def export(path):
    for img in bpy.data.images:   # has_data is lazy: pack anything whose file exists
        if img.packed_file is None and img.filepath and os.path.exists(bpy.path.abspath(img.filepath)):
            try: img.pack()
            except RuntimeError as e: print("[export] pack failed", img.name, e)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=False, object_types={'EMPTY', 'MESH'}, axis_forward='-Z', axis_up='Y',
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', mesh_smooth_type='FACE', use_mesh_modifiers=True,
                             path_mode='COPY', embed_textures=True, add_leaf_bones=False, bake_anim=False, use_custom_props=False)
    lo, hi = bounds(); ms = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    print(f"[export] {os.path.basename(path)}: {len(ms)} meshes, {sum(len(o.data.polygons) for o in ms)} polys, {len(bpy.data.materials)} materials, "
          f"{sum(1 for i in bpy.data.images if i.packed_file or i.has_data)} textures, size {tuple(round(v, 2) for v in (hi - lo))} m")

if mode == "chamber":
    bpy.ops.import_scene.fbx(filepath=r"D:/AI unity project/AI PHOTOSYNTHESIS/Assets/Photosynthesis/Art/Environment/Chamber.fbx")
    for img in [i for i in bpy.data.images if not i.has_data and not i.packed_file]: bpy.data.images.remove(img)   # the one map missing from the source
    export(out)
elif mode == "leaf":
    bpy.ops.wm.obj_import(filepath=argv[2], forward_axis='NEGATIVE_Z', up_axis='Y', use_split_objects=True, use_split_groups=True)
    export(out)
elif mode == "curio":
    kit, albedo, scarf_obj, scarf_png = argv[2:6]
    bpy.ops.import_scene.fbx(filepath=kit)
    robot = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    for o in robot:
        for slot in o.material_slots:
            m = slot.material
            if m and not any(n.type == 'TEX_IMAGE' and n.image for n in (m.node_tree.nodes if m.node_tree else [])): set_base_tex(m, albedo)
    lo, hi = bounds(); print(f"[curio] robot bounds {tuple(round(v,3) for v in lo)} .. {tuple(round(v,3) for v in hi)}")
    before = set(bpy.context.scene.objects)
    bpy.ops.wm.obj_import(filepath=scarf_obj, forward_axis='NEGATIVE_Z', up_axis='Y')
    scarf = [o for o in bpy.context.scene.objects if o not in before]
    for o in scarf:
        o.name = "Scarf"; m = bpy.data.materials.new("Scarf"); set_base_tex(m, scarf_png); o.data.materials.clear(); o.data.materials.append(m)
        sl = Vector((1e9,) * 3); sh = Vector((-1e9,) * 3)
        for c in o.bound_box:
            p = o.matrix_world @ Vector(c); sl = Vector(map(min, sl, p)); sh = Vector(map(max, sh, p))
        print(f"[curio] scarf bounds {tuple(round(v,3) for v in sl)} .. {tuple(round(v,3) for v in sh)}")
    export(out)
elif mode == "verify":
    bpy.ops.import_scene.fbx(filepath=out)
    ms = [o for o in bpy.context.scene.objects if o.type == 'MESH']; imgs = list(bpy.data.images)
    packed = [i for i in imgs if i.packed_file]
    print(f"[verify] {os.path.basename(out)}: {len(ms)} meshes, {len(bpy.data.materials)} materials, images {len(imgs)}, packed {len(packed)}, unresolved {[i.name for i in imgs if not i.packed_file and not i.has_data][:6]}")
