"""Export verified PubChem CID 64689 3D data to Unity's ball-and-stick graph.
Source SDF is retained. All bonds and stereochemical coordinates come from PubChem,
not a hand-drawn hexagon. No chemistry package or Unity runtime dependency needed.
"""
from pathlib import Path
import json, math
from collections import Counter

root=Path(__file__).resolve().parents[2]
lines=(Path(__file__).with_name('BetaDGlucose_64689.sdf')).read_text().splitlines()
n,nb=int(lines[3][:3]),int(lines[3][3:6])
atoms=[(l[31:34].strip(),[float(l[k:k+10]) for k in (0,10,20)]) for l in lines[4:4+n]]
bonds=[(int(l[:3])-1,int(l[3:6])-1,int(l[6:9])) for l in lines[4+n:4+n+nb]]
assert Counter(e for e,p in atoms)=={'C':6,'H':12,'O':6}
degree=[0]*n
for a,b,order in bonds:degree[a]+=order;degree[b]+=order
assert all(d=={'C':4,'O':2,'H':1}[e] for d,(e,p) in zip(degree,atoms))
# Ring first for the existing staged reveal; orientation matches the supplied cyclic illustration.
ring=[0,10,9,7,6,8]
order=ring+[11]+[i for i in range(n) if i not in ring and i!=11]
remap={old:new for new,old in enumerate(order)}
theta=math.radians(25);c,s=math.cos(theta),math.sin(theta)
# Convert the source's right-handed frame to Unity's left-handed frame.
rot=[[c*p[0]-s*p[1],s*p[0]+c*p[1],-p[2]] for e,p in atoms]
center=[(min(p[k] for p in rot)+max(p[k] for p in rot))/2 for k in range(3)]
sizes={'C':.46,'O':.42,'H':.24}
result={'source':'https://pubchem.ncbi.nlm.nih.gov/compound/64689','name':'beta-D-glucopyranose','atoms':[
    {'element':atoms[i][0],'position':{k:round((rot[i][j]-center[j])*.45,6) for j,k in enumerate(['x','y','z'])},'size':sizes[atoms[i][0]]} for i in order
], 'bonds':[{'a':remap[a],'b':remap[b],'order':o} for a,b,o in bonds], 'ring':list(range(6))}
out=root/'Assets/Photosynthesis/Resources/Molecules/GlucoseGeometry.json';out.parent.mkdir(parents=True,exist_ok=True)
out.write_text(json.dumps(result,indent=2))
print('PASS: C6H12O6; C valence4, O valence2, H valence1; 24 atoms/24 bonds; 3D conformer preserved')
