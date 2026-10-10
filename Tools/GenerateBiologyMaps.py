"""UV-aligned root tissue and restrained molecule surfaces; no reference-board pixels.
Root U is circumference/18, V is root length/3m (two staggered 1.5m cells).
AI source contributes subtle fiber variation; cell relief/masks are deterministic.
"""
from pathlib import Path
import argparse,shutil,json
import numpy as np
from PIL import Image,ImageFilter

root=Path(__file__).resolve().parents[1]/'Assets/Photosynthesis/Art/Upgraded'
dest=root/'Textures';dest.mkdir(parents=True,exist_ok=True)
source=root/'Sources';source.mkdir(parents=True,exist_ok=True)

def save(name,data):
    Image.fromarray(np.uint8(np.clip(data,0,1)*255)).save(dest/(name+'.png'))

def maps(name,rgb,h,rough,alpha=None,thickness=None):
    dy=(np.roll(h,-1,0)-np.roll(h,1,0))*1.8
    dx=(np.roll(h,-1,1)-np.roll(h,1,1))*1.8
    n=np.stack((-dx,dy,np.ones_like(h)),-1);n/=np.linalg.norm(n,axis=-1,keepdims=True)
    local=np.asarray(Image.fromarray(np.uint8(h*255)).filter(ImageFilter.GaussianBlur(4)))/255
    ao=np.clip(1-np.maximum(local-h,0)*.5,.84,1)
    save(name+'_Albedo',rgb if alpha is None else np.dstack((rgb,alpha)))
    save(name+'_Normal',n*.5+.5);save(name+'_Roughness',rough);save(name+'_Height',h)
    save(name+'_AO',np.repeat(ao[:,:,None],3,axis=2))
    save(name+'_MetallicSmoothness',np.stack((np.zeros_like(h),ao,np.zeros_like(h),1-rough),-1))
    if thickness is not None:save(name+'_Thickness',thickness)
    assert np.isfinite(n).all() and np.max(abs(np.linalg.norm(n,axis=-1)-1))<1e-5
    assert np.ptp(rgb)>.01 and np.ptp(rough)>.001

def generate(ai_path):
    target=source/'RootEpidermis_AI.png'
    if ai_path:shutil.copy2(ai_path,target)
    assert target.exists(),'Pass --source with the generated root albedo source'
    n=2048;v,u=np.mgrid[0:n,0:n]/(n-1)
    # Edge-matched AI detail preserves seamless sampling without hand painting.
    a=np.asarray(Image.open(target).convert('RGB').resize((n,n)),dtype=np.float32)/255
    edge=np.sin(np.pi*u)**2*np.sin(np.pi*v)**2
    detail=(a.mean(2)-a.mean())*edge
    uu=np.where(v<.5,u,np.mod(u+.5,1));vv=np.mod(v*2,1)
    distance=np.minimum(np.minimum(uu,1-uu)*.42,np.minimum(vv,1-vv)*1.5)
    wall=1-np.clip((distance-.005)/.016,0,1)
    fiber=(np.sin(2*np.pi*u*96+.12*np.sin(v*2*np.pi))*.5+.5)**6
    h=.46-.08*wall+.025*fiber+.08*detail
    rgb=np.array([.92,.87,.76])[None,None,:]*(1-.14*wall[:,:,None])+detail[:,:,None]*.4
    maps('RootEpidermis',rgb,h,np.clip(.69+.05*wall+.05*detail,0,1),.82+.1*wall,.73-.28*wall)
    n=512;v,u=np.mgrid[0:n,0:n]/(n-1)
    f=np.sin(2*np.pi*u*12+.08*np.sin(2*np.pi*v))*.5+.5
    maps('RootHair',np.array([.92,.89,.81])[None,None,:]*(.97+.03*f[:,:,None]),.5+.008*f,.48+.025*f,thickness=.82+.05*f)
    # Elongated vessel walls, restrained banding; distinct from epidermis/hairs.
    n=1024;v,u=np.mgrid[0:n,0:n]/(n-1)
    bands=(.5+.5*np.sin(v*2*np.pi*8))**10;fibers=np.sin(u*2*np.pi*32)*.5+.5
    h=.4+.045*bands+.018*fibers
    maps('VesselWall',np.array([.72,.68,.47])[None,None,:]*(.91+.09*bands[:,:,None]),h,.57+.08*bands)
    # Very low-amplitude shared ceramic-like atom finish; no rocky/bubble pattern.
    n=256;v,u=np.mgrid[0:n,0:n]/(n-1);micro=np.sin(u*2*np.pi*41)*np.sin(v*2*np.pi*37)
    for name,color,rough in [('AtomO',(.78,.055,.045),.23),('AtomH',(.94,.95,.97),.29),('AtomC',(.10,.12,.14),.27),('Connector',(.68,.71,.74),.35)]:
        maps(name,np.array(color)[None,None,:]*(1+micro[:,:,None]*.007),.5+micro*.0005,rough+micro*.009)
    flow=.5+.5*np.sin(v*2*np.pi*5+.12*np.sin(u*2*np.pi*3))
    maps('WaterFlow',np.array([.18,.47,.57])[None,None,:]*(.94+.06*flow[:,:,None]),.5+.025*flow,.10+.02*flow)
    manifest={'method':'AI root source + procedural UV-aligned derivatives; other families procedural','source':str(target.relative_to(root)),'files':[str(p.relative_to(root)) for p in dest.glob('*.png')]}
    (root/'texture_manifest.json').write_text(json.dumps(manifest,indent=2))
    print(len(manifest['files']),'maps saved; normal unit-length and variation checks passed')

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--source');args=parser.parse_args();generate(args.source)
