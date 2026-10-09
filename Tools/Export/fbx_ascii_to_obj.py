# Maya ASCII FBX -> OBJ + MTL (+ textures/ copied) in world space, cm -> m. Geometry, UVs, per-polygon materials, diffuse/normal maps.
import re, sys, os, math, glob, shutil
import numpy as np

src, out_obj, tex_root = sys.argv[1], sys.argv[2], sys.argv[3]
SCALE = 0.01
lines = open(src, encoding="utf-8", errors="ignore").read().split("\n")

nodes, sections = [], {}
depth = 0; section = None; stack = []; sec_start = 0
for i, ln in enumerate(lines):
    s = ln.strip()
    if s.endswith("{"):
        if depth == 0: section = s.split(":")[0]; sec_start = i
        if depth == 1 and section == "Objects": stack.append((s, i))
        depth += 1
    elif s == "}":
        depth -= 1
        if depth == 1 and stack: h, st = stack.pop(); nodes.append((h, st, i))
        if depth == 0: sections[section] = (sec_start, i); section = None

def arr(txt, key):
    m = re.search(r"(?m)^\s*" + key + r": \*\d+ \{\s*a: (.*?)\n\s*\}", txt, re.S)
    return np.array([float(x) for x in m.group(1).replace("\n", "").split(",") if x.strip()]) if m else None
def prop(txt, name):
    m = re.search(r'P: "' + re.escape(name) + r'",[^\n]*', txt)
    if not m: return None
    parts = m.group(0).split(",")
    try: return [float(x) for x in parts[-3:]]
    except ValueError: return [float(parts[-1])]
def strprop(txt, key):
    m = re.search(key + r': "(.*?)"', txt); return m.group(1) if m else None

geoms, models, mats, texs = {}, {}, {}, {}
for h, st, en in nodes:
    m = re.match(r'(\w+): (\d+), "(\w+)::(.*?)"', h)
    if not m: continue
    kind, oid, name = m.group(1), int(m.group(2)), m.group(4); txt = "\n".join(lines[st:en + 1])
    if kind == "Geometry":
        v = arr(txt, "Vertices"); idx = arr(txt, "PolygonVertexIndex")
        if v is None or idx is None: continue
        v = v.reshape(-1, 3) * SCALE; idx = idx.astype(int)
        polys, poly = [], []
        for k in idx:
            if k < 0: poly.append(~k); polys.append(poly); poly = []
            else: poly.append(k)
        uvm = re.search(r"LayerElementUV: 0 \{(.*?)\n\t\t\}", txt, re.S)
        uv, uvi = (arr(uvm.group(1), "UV"), arr(uvm.group(1), "UVIndex")) if uvm else (None, None)
        if uv is not None: uv = uv.reshape(-1, 2)
        mm = re.search(r"LayerElementMaterial: 0 \{(.*?)\n\t\t\}", txt, re.S)
        pm = arr(mm.group(1), "Materials") if mm else None
        geoms[oid] = (name, v, polys, uv, None if uvi is None else uvi.astype(int), None if pm is None else pm.astype(int))
    elif kind == "Model":
        p = lambda n, d: np.array(prop(txt, n) or d, float)
        ro = prop(txt, "RotationOrder"); ro = int(ro[0]) if ro else 0
        models[oid] = dict(name=name, t=p("Lcl Translation", [0, 0, 0]), r=p("Lcl Rotation", [0, 0, 0]), s=p("Lcl Scaling", [1, 1, 1]),
                           pre=p("PreRotation", [0, 0, 0]), rp=p("RotationPivot", [0, 0, 0]), sp=p("ScalingPivot", [0, 0, 0]),
                           gt=p("GeometricTranslation", [0, 0, 0]), gr=p("GeometricRotation", [0, 0, 0]), gs=p("GeometricScaling", [1, 1, 1]),
                           ro=ro, parent=0, geo=None, mats=[])
    elif kind == "Material": mats[oid] = dict(name=name, diffuse=None, normal=None)
    elif kind == "Texture": texs[oid] = strprop(txt, "RelativeFilename") or strprop(txt, "FileName") or ""
cs, ce = sections["Connections"]
for ln in lines[cs:ce + 1]:
    m = re.match(r'\s*C: "(O[OP])",(\d+),(\d+)(?:,\s*"(\w+)")?', ln)
    if not m: continue
    kind, a, b, p = m.group(1), int(m.group(2)), int(m.group(3)), m.group(4)
    if kind == "OO":
        if a in geoms and b in models: models[b]["geo"] = a
        elif a in mats and b in models: models[b]["mats"].append(a)
        elif a in models and (b in models or b == 0): models[a]["parent"] = b
    elif a in texs and b in mats:
        if p == "DiffuseColor": mats[b]["diffuse"] = texs[a]
        elif p in ("NormalMap", "Bump"): mats[b]["normal"] = texs[a]

ORDERS = ["XYZ", "XZY", "YZX", "YXZ", "ZXY", "ZYX"]
def rot(deg, order):
    rx, ry, rz = np.radians(deg); c, s = math.cos, math.sin
    R = {"X": np.array([[1, 0, 0], [0, c(rx), -s(rx)], [0, s(rx), c(rx)]]), "Y": np.array([[c(ry), 0, s(ry)], [0, 1, 0], [-s(ry), 0, c(ry)]]), "Z": np.array([[c(rz), -s(rz), 0], [s(rz), c(rz), 0], [0, 0, 1]])}
    M = np.eye(3)
    for ax in order: M = R[ax] @ M
    return M
def A(M3, t): M = np.eye(4); M[:3, :3] = M3; M[:3, 3] = t; return M
def local(m):
    rp, sp = m["rp"] * SCALE, m["sp"] * SCALE
    return A(np.eye(3), m["t"] * SCALE) @ A(np.eye(3), rp) @ A(rot(m["pre"], "XYZ"), 0) @ A(rot(m["r"], ORDERS[m["ro"]]), 0) @ A(np.eye(3), -rp) @ A(np.eye(3), sp) @ A(np.diag(m["s"]), 0) @ A(np.eye(3), -sp)
cache = {}
def world(oid):
    if oid in cache: return cache[oid]
    m = models[oid]; W = (world(m["parent"]) if m["parent"] in models else np.eye(4)) @ local(m); cache[oid] = W; return W

# textures: copy every referenced map next to the OBJ
tex_dir = os.path.join(os.path.dirname(out_obj), "textures"); os.makedirs(tex_dir, exist_ok=True)
files = {os.path.basename(p).lower(): p for p in glob.glob(tex_root + "/**/*.*", recursive=True) if p.lower().endswith((".png", ".jpg", ".jpeg", ".tga", ".tif"))}
def tex_rel(ref):
    if not ref: return None
    p = files.get(os.path.basename(ref).lower())
    if not p: return None
    shutil.copy2(p, os.path.join(tex_dir, os.path.basename(p))); return "textures/" + os.path.basename(p)

mtl_path = os.path.splitext(out_obj)[0] + ".mtl"
used_mats = {}
obj = ["mtllib " + os.path.basename(mtl_path)]
v_off = vt_off = 0; n_faces = 0
for oid, m in models.items():
    if m["geo"] is None: continue
    gname, v, polys, uv, uvi, pm = geoms[m["geo"]]
    if not polys: continue
    W = world(oid) @ A(rot(m["gr"], "XYZ") @ np.diag(m["gs"]), m["gt"] * SCALE)
    vw = (W @ np.c_[v, np.ones(len(v))].T).T[:, :3]
    obj.append(f"o {m['name']}")
    for p_ in vw: obj.append(f"v {p_[0]:.6f} {p_[1]:.6f} {p_[2]:.6f}")
    # per-polygon-vertex uv list
    pv_count = sum(len(p_) for p_ in polys); uvs = None
    if uv is not None:
        uvs = uv[uvi] if uvi is not None and len(uvi) == pv_count else (uv if len(uv) == pv_count else None)
    if uvs is not None:
        for t in uvs: obj.append(f"vt {t[0]:.6f} {t[1]:.6f}")
    slots = [mats[i]["name"] for i in m["mats"]] or ["(none)"]
    cur = None; k = 0
    for pi, poly in enumerate(polys):
        mi = int(pm[pi]) if pm is not None and len(pm) == len(polys) else (int(pm[0]) if pm is not None and len(pm) else 0)
        mname = slots[min(mi, len(slots) - 1)]
        if mname != cur: obj.append("usemtl " + mname); cur = mname; used_mats.setdefault(mname, next((mats[i] for i in m["mats"] if mats[i]["name"] == mname), None))
        if uvs is not None: obj.append("f " + " ".join(f"{v_off + vi + 1}/{vt_off + k + j + 1}" for j, vi in enumerate(poly)))
        else: obj.append("f " + " ".join(f"{v_off + vi + 1}" for vi in poly))
        k += len(poly); n_faces += 1
    v_off += len(vw); vt_off += (pv_count if uvs is not None else 0)
open(out_obj, "w", encoding="utf-8").write("\n".join(obj) + "\n")
mtl = []
for name, md in used_mats.items():
    mtl += [f"newmtl {name}", "Kd 1.000 1.000 1.000", "Ks 0.000 0.000 0.000", "d 1.0"]
    d = tex_rel(md["diffuse"]) if md else None; n = tex_rel(md["normal"]) if md else None
    if d: mtl.append("map_Kd " + d)
    if n: mtl.append("map_Bump " + n)
    mtl.append("")
open(mtl_path, "w", encoding="utf-8").write("\n".join(mtl))
print(f"[obj] {v_off} verts, {n_faces} faces, {len(used_mats)} materials, textures copied: {len(os.listdir(tex_dir))} -> {out_obj}")
