"""Deterministic, editable PBR recipes. Run after Unity's ExportSurfaceSources command.
Normal, AO and roughness share the same height/vein field; Unity uses metallic R,
smoothness A and separate AO G. No third-party texture or reference screenshot pixels.
"""
from pathlib import Path
import numpy as np
from PIL import Image, ImageFilter

OUT = Path(__file__).resolve().parents[1] / 'Assets/Photosynthesis/Art/Generated/Resources/SurfaceMaps'
OUT.mkdir(parents=True, exist_ok=True)
SOURCE = OUT.parents[1] / 'Sources'
SOURCE.mkdir(parents=True, exist_ok=True)
rng = np.random.default_rng(4209)

def noise(n, cells):
    a = rng.uniform(0, 255, (cells, cells)).astype('uint8')
    # Wrap border for continuity before bicubic filtering.
    a = np.pad(a, 1, mode='wrap')
    im = Image.fromarray(a).resize((n*(cells+2)//cells,)*2, Image.Resampling.BICUBIC)
    margin = n//cells
    return np.asarray(im.crop((margin,margin,margin+n,margin+n)),dtype=np.float32)/255

def save(name, a):
    destination = SOURCE if name.endswith(('_Height','_Roughness')) else OUT
    Image.fromarray(np.uint8(np.clip(a,0,1)*255)).save(destination/f'{name}.png')

def maps(name, rgb, height, rough, metallic=0., strength=2., repeat=True):
    h = np.asarray(height,dtype=np.float32)
    # Derivative in UV space, with wrapped sampling on tiled surfaces only.
    if repeat:
        dx=(np.roll(h,-1,1)-np.roll(h,1,1))*strength
        dy=(np.roll(h,-1,0)-np.roll(h,1,0))*strength
    else:
        dy,dx=np.gradient(h); dx*=strength*2;dy*=strength*2
    normals=np.stack((-dx,dy,np.ones_like(h)),axis=-1) # PNG rows run opposite Unity +V
    normals/=np.linalg.norm(normals,axis=-1,keepdims=True)
    smooth=np.clip(1-rough,0,1)
    blurred=np.asarray(Image.fromarray(np.uint8(h*255)).filter(ImageFilter.GaussianBlur(5)),dtype=np.float32)/255
    ao=np.clip(1-np.maximum(blurred-h,0)*1.3,.65,1)
    save(name+'_Albedo',rgb);save(name+'_Height',h);save(name+'_Normal',normals*.5+.5)
    save(name+'_Roughness',rough);save(name+'_AO',np.stack((ao,ao,ao),-1))
    save(name+'_MetallicSmoothness',np.stack((np.full_like(h,metallic),ao,np.zeros_like(h),smooth),-1))
    assert np.isfinite(normals).all() and np.max(abs(np.linalg.norm(normals,axis=-1)-1))<1e-5

families={
 'Soil':((.21,.13,.075),.87), 'Root':((.88,.79,.59),.63),
 'Guard':((.37,.57,.13),.44), 'Membrane':((.82,.93,.66),.45),
 'Grana':((.18,.46,.08),.38), 'Vascular':((.72,.69,.44),.53),
 'Shell':((.96,.925,.83),.28), 'Screen':((.013,.026,.028),.16),
 'Scarf':((.14,.39,.045),.86), 'Gold':((.88,.64,.24),.25),
}
for name,(color,rough0) in families.items():
    n=2048 if name in ('Soil','Wood','Stone') else 1024
    v,u=np.mgrid[0:n,0:n]/n
    lo=noise(n,12);fine=noise(n,128);grain=noise(n,256)
    h=.35*lo+.4*fine+.25*grain
    if name in ('Root','Vascular','Wood'):
        fibres=(.5+.5*np.sin(2*np.pi*(u*36+np.sin(v*2*np.pi)*.25)))**9
        rings=(.5+.5*np.cos(v*2*np.pi*9))**18
        h=.45*fibres+.2*lo+.2*fine+.15*rings
    if name=='Grana':h=.25*lo+.5*fine+.25*(.5+.5*np.cos(v*2*np.pi*22))**10
    if name in ('Shell','Screen','Gold'):h=.48+.025*grain+.018*fine
    if name=='Scarf':h=.3*lo+.2*fine+.25*(np.sin(u*2*np.pi*256)*np.sin(v*2*np.pi*256)+1)
    if name=='Soil':h=np.clip(h+(grain>.72)*.22,0,1)
    rgb=np.asarray(color)[None,None,:]*(.74+.36*h[:,:,None])
    if name=='Soil':rgb+=((grain>.75)&(fine>.60))[:,:,None]*np.array([.16,.14,.105])
    rough=np.clip(rough0+(fine-.5)*.14-(h-.5)*.10,.08,.97)
    maps(name,rgb,h,rough, .92 if name=='Gold' else 0.,4 if name=='Soil' else 2)

# Unity exports its exact existing leaf and epidermis functions in native UV space.
for name in ('LeafTop','LeafUnder','Epidermis','EpidermisPatch','LabWood','LabFloor','LabBeam'):
    p=SOURCE/f'{name}_Source.png'
    if not p.exists(): p=OUT/f'{name}_Source.png'
    if not p.exists():continue
    rgb=np.asarray(Image.open(p).convert('RGB'),dtype=np.float32)/255
    bright=rgb[:,:,0]*.23+rgb[:,:,1]*.65+rgb[:,:,2]*.12
    h=np.clip((bright-bright.min())/max(.01,np.ptp(bright)),0,1)
    if name.startswith('Leaf'):
        # Existing gold-green vein pixels carry relief and less wax than the blade.
        veins=np.clip((rgb[:,:,0]-.17)*3,0,1)
        h=.18*h+.65*veins
        rough=np.clip(.32+veins*.19+(1-h)*.06,.22,.67)
    elif name.startswith('Lab'):rough=np.clip((.62 if name=='LabWood' else .42)+(h-.5)*.22,.28,.78)
    else:rough=np.clip(.39+h*.17,.3,.65)
    maps(name,rgb,h,rough,strength=3.,repeat=False)

save('Neutral_Normal',np.full((4,4,3),[.5,.5,1]))
save('Neutral_MetallicSmoothness',np.full((4,4,4),[0,1,0,1]))
save('Neutral_AO',np.ones((4,4,3)))
save('Eye_Emission',np.full((64,64,3),[.08,.82,1.]))
# Source-only channels do not belong in Resources (which packages every file).
import shutil
for p in list(OUT.glob('*.png')):
    source_only=p.stem.endswith(('_Source','_Height','_Roughness')) or p.stem.startswith(('Wood_','Stone_')) or p.stem=='Scarf_Albedo' or (p.stem.startswith('Lab') and p.stem.endswith(('_Albedo','_Normal')))
    if source_only:
        shutil.move(str(p),str(SOURCE/p.name))
        meta=p.with_suffix('.png.meta')
        if meta.exists():shutil.move(str(meta),str(SOURCE/meta.name))
print('PBR assets:',len(list(OUT.glob('*.png'))),'matching maps; normal unit-length checks passed')
