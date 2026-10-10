"""Blender: retain supplied geometry/UVs, expose envelope and reaction parts for Unity."""
import bpy, bmesh, json
from pathlib import Path
from mathutils import Vector, Matrix

ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT/'Assets/Photosynthesis/Art/G7/neww'
OUT=ROOT/'Assets/Photosynthesis/Art/Upgraded/Models'
OUT.mkdir(parents=True,exist_ok=True)
TEX=OUT/'Textures';TEX.mkdir(exist_ok=True)

def save_images(prefix):
    records=[]
    for image in list(bpy.data.images):
        if image.type!='IMAGE': continue
        try: image.pixels[0]
        except (RuntimeError,IndexError): continue
        if not image.has_data: continue
        name=prefix+'_'+image.name.replace('.png','')+'.png'
        image.filepath_raw=str(TEX/name);image.file_format='PNG';image.save()
        records.append({'original':image.name,'file':name})
        if '_Roughness' in image.name:
            import numpy as np
            pixels=np.array(image.pixels[:],dtype=np.float32).reshape(-1,4)
            packed=np.zeros_like(pixels);packed[:,3]=1-pixels[:,0]
            mask=bpy.data.images.new('Packed',width=image.size[0],height=image.size[1],alpha=True)
            mask.colorspace_settings.name='Non-Color';mask.pixels[:]=packed.ravel()
            mask.filepath_raw=str(TEX/name.replace('_Roughness','_MetallicSmoothness'));mask.file_format='PNG';mask.save()
            bpy.data.images.remove(mask)
    return records

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(SOURCE/'LeafInside.fbx'))
images={'leaf':save_images('Leaf')}
leaf_bindings={}
for ob in [o for o in bpy.context.scene.objects if o.type=='MESH']:
    material=ob.data.materials[0]
    bsdf=next(n for n in material.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    link=bsdf.inputs['Base Color'].links
    leaf_bindings[ob.name]=link[0].from_node.image.name if link and link[0].from_node.type=='TEX_IMAGE' else ''
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(SOURCE/'chloroplast.glb'))
images['chloroplast']=save_images('Chloroplast')

# UV seam duplicates must be welded before loose-part separation; per-corner UVs remain intact.
for ob in [o for o in bpy.context.scene.objects if o.type=='MESH']:
    bm=bmesh.new();bm.from_mesh(ob.data)
    bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00001)
    bm.to_mesh(ob.data);bm.free()
    bpy.ops.object.select_all(action='DESELECT');ob.select_set(True);bpy.context.view_layer.objects.active=ob
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.separate(type='LOOSE');bpy.ops.object.mode_set(mode='OBJECT')

def bounds(ob):
    p=[ob.matrix_world@v.co for v in ob.data.vertices]
    lo=Vector([min(v[j] for v in p) for j in range(3)])
    hi=Vector([max(v[j] for v in p) for j in range(3)])
    return (lo+hi)/2,hi-lo

parts=[o for o in bpy.context.scene.objects if o.type=='MESH']
shells=sorted([o for o in parts if bounds(o)[1].x>2],key=lambda o:-bounds(o)[1].x)
assert len(shells)==2, 'Expected double envelope'
groups={};grana=[]
for ob in parts:
    c,s=bounds(ob)
    if ob in shells:
        # Split the supplied cutaway at its horizontal midline. Existing story peels both caps.
        for upper in [False,True]:
            new=ob.copy();new.data=ob.data.copy();bpy.context.collection.objects.link(new)
            bm=bmesh.new();bm.from_mesh(new.data)
            local_plane=ob.matrix_world.inverted()@Vector((0,0,.546353))
            local_normal=ob.matrix_world.to_3x3().inverted()@Vector((0,0,1))
            bmesh.ops.bisect_plane(bm,geom=list(bm.verts)+list(bm.edges)+list(bm.faces),dist=.000001,
                plane_co=local_plane,plane_no=local_normal,clear_inner=upper,clear_outer=not upper)
            bm.to_mesh(new.data);bm.free()
            key=('Outer' if ob==shells[0] else 'Inner')+('Cap' if upper else 'Membrane')
            groups.setdefault(key,[]).append(new)
        bpy.data.objects.remove(ob,do_unlink=True)
    elif s.z<.05 and max(s.x,s.y)>.08 and len(ob.data.vertices)>100:
        groups.setdefault('Thylakoids',[]).append(ob)
        # Vertically aligned disc centres describe the imported stacks, not the old random layout.
        g=next((g for g in grana if (Vector((c.x,c.y))-Vector(g['xy'])).length<.055),None)
        if g is None: g={'xy':[c.x,c.y],'lo':c.z-s.z/2,'hi':c.z+s.z/2,'radius':max(s.x,s.y)/2,'discs':0};grana.append(g)
        g['lo']=min(g['lo'],c.z-s.z/2);g['hi']=max(g['hi'],c.z+s.z/2);g['discs']+=1
    elif len(ob.data.vertices)>6000: groups.setdefault('Starch',[]).append(ob)
    elif s.x>1.5: groups.setdefault('StromaBase',[]).append(ob)
    else: groups.setdefault('StromaContents',[]).append(ob)

meshes=[]
for name,obs in groups.items():
    bpy.ops.object.select_all(action='DESELECT')
    for ob in obs: ob.select_set(True)
    bpy.context.view_layer.objects.active=obs[0];bpy.ops.object.join()
    ob=bpy.context.object;ob.name=name
    # Bake world coordinates and normalise to the established gameplay envelope (Unity x/y/z).
    matrix=ob.matrix_world.copy()
    for v in ob.data.vertices:
        p=matrix@v.co
        v.co=Vector((p.x*6.89159,-p.y*8.40564,(p.z-.546353)*5.5386))
    ob.parent=None;ob.matrix_world=Matrix.Identity(4)
    for poly in ob.data.polygons:poly.use_smooth=True
    meshes.append(ob)

anchors=[]
for g in sorted(grana,key=lambda g:-g['discs']):
    if g['discs']<3:continue
    p=Vector((g['xy'][0]*6.89159,-g['xy'][1]*8.40564,((g['lo']+g['hi'])/2-.546353)*5.5386))
    ob=bpy.data.objects.new('GranumAnchor_%02d'%len(anchors),None);bpy.context.collection.objects.link(ob);ob.location=p
    ob.scale=(g['radius']*6.89159,g['radius']*6.89159,(g['hi']-g['lo'])/2*5.5386)
    anchors.append({'name':ob.name,'center':[p.x,p.z,-p.y],'radius':g['radius']*6.89159,'half_height':(g['hi']-g['lo'])/2*5.5386,'discs':g['discs']})
    meshes.append(ob)
assert len(anchors)>=4 and all(name in groups for name in ['OuterCap','InnerCap','Thylakoids'])
bpy.ops.object.select_all(action='DESELECT')
for ob in meshes:ob.select_set(True)
bpy.ops.export_scene.fbx(filepath=str(OUT/'ChloroplastAnimatedParts.fbx'),use_selection=True,
    object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',add_leaf_bones=False,bake_anim=False,path_mode='STRIP')
(OUT/'model_manifest.json').write_text(json.dumps({'source':'Assets/Photosynthesis/Art/G7/neww/chloroplast.glb','images':images,'leaf_bindings':leaf_bindings,'anchors':anchors,'parts':list(groups)},indent=2))
print('MODEL PREPARATION PASS',list(groups),len(anchors),'grana anchors')
