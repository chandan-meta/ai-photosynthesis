using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Photosynthesis
{
    /// `-exportmodels <dir>`: dump every runtime-built model (hero plant, Dr. Curio, molecules, the chamber's procedural pieces,
    /// each micro world) as OBJ + MTL + PNG textures, one folder per model, then quit. Blender turns those into FBX with the
    /// textures embedded (Tools/Export). World space with X mirrored (left- to right-handed), each model centred on its root.
    public static class ModelExport
    {
        /// True when the player was started with -exportmodels: the chamber then skips static batching (combined meshes are unreadable).
        public static bool Requested => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-exportmodels") >= 0;

        public static IEnumerator Run(string dir)
        {
            yield return null; yield return null;   // Module.Start has shown the start screen: chamber formed, plant grown, Curio placed
            var m = Module.I; var ch = m.chamber;
            m.plant.ShowRoots(true); m.plant.SetVessels(true, true);
            yield return null;
            Export(dir, "HeroPlant", m.plant.transform);
            Export(dir, "DrCurio", m.curio.transform);
            var under = ch.transform.Find("Underground"); bool wasOn = under && under.gameObject.activeSelf; if (under) under.gameObject.SetActive(true);
            Export(dir, "Chamber_Procedural", ch.transform.Find("Procedural"), under);
            if (under) under.gameObject.SetActive(wasOn);
            var mols = new GameObject("Molecules").transform;
            foreach (Mol k in System.Enum.GetValues(typeof(Mol))) { var g = Fx.Molecule(k, mols); Export(dir, "Molecule_" + k, g.transform); Object.Destroy(g); }
            foreach (var w in new MicroWorld[] { m.leafSurface, m.soil, m.xylem, m.tissue, m.cell, m.chloroplast, m.interior })
            {
                w.Show(true); yield return null; yield return null;
                Export(dir, w.GetType().Name, w.transform);
                w.Show(false);
            }
            Debug.Log("[Export] models complete -> " + dir);
            Application.Quit();
        }

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string F(float v) => v.ToString("0.######", Inv);

        static void Export(string dir, string name, params Transform[] roots)
        {
            string folder = Path.Combine(dir, name), texDir = Path.Combine(folder, "textures"); Directory.CreateDirectory(texDir);
            var obj = new StringBuilder("mtllib " + name + ".mtl\n"); var mtl = new StringBuilder();
            var matIds = new Dictionary<Material, string>(); var texFiles = new Dictionary<Texture, string>();
            Vector3 origin = roots[0] ? roots[0].position : Vector3.zero;
            int vOff = 0, meshes = 0, skipped = 0;
            foreach (var root in roots)
            {
                if (!root) continue;
                foreach (var r in root.GetComponentsInChildren<Renderer>(false))
                {
                    if (!r.enabled || r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
                    Mesh mesh = null; Matrix4x4 l2w = r.localToWorldMatrix; int sub0 = 0;
                    if (r is SkinnedMeshRenderer smr) { mesh = new Mesh(); smr.BakeMesh(mesh); l2w = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one); }
                    else if (r is MeshRenderer mr)
                    {
                        var mf = mr.GetComponent<MeshFilter>(); mesh = mf ? mf.sharedMesh : null;
                        if (mr.isPartOfStaticBatch) { sub0 = mr.subMeshStartIndex; l2w = Matrix4x4.identity; }   // combined mesh is in the batch root's space: the chamber sits at the world origin
                    }
                    if (!mesh || !mesh.isReadable || mesh.vertexCount == 0) { skipped++; continue; }
                    var v = mesh.vertices; var n = mesh.normals; var uv = mesh.uv; var mats = r.sharedMaterials;
                    string path = NodePath(r.transform, root).Replace(' ', '_');
                    obj.Append("g ").Append(path).Append('\n');
                    for (int i = 0; i < v.Length; i++) { var p = l2w.MultiplyPoint3x4(v[i]) - origin; obj.Append("v ").Append(F(-p.x)).Append(' ').Append(F(p.y)).Append(' ').Append(F(p.z)).Append('\n'); }
                    bool hasN = n != null && n.Length == v.Length, hasUv = uv != null && uv.Length == v.Length;
                    if (hasN) for (int i = 0; i < n.Length; i++) { var d = l2w.MultiplyVector(n[i]).normalized; obj.Append("vn ").Append(F(-d.x)).Append(' ').Append(F(d.y)).Append(' ').Append(F(d.z)).Append('\n'); }
                    if (hasUv) for (int i = 0; i < uv.Length; i++) obj.Append("vt ").Append(F(uv[i].x)).Append(' ').Append(F(uv[i].y)).Append('\n');
                    for (int s = 0; s < mats.Length && sub0 + s < mesh.subMeshCount; s++)
                    {
                        var mat = mats[s]; if (!mat) continue;
                        if (!matIds.TryGetValue(mat, out var id)) { id = "m" + matIds.Count + "_" + Safe(mat.name); matIds[mat] = id; mtl.Append(MtlEntry(id, mat, texDir, texFiles)); }
                        obj.Append("usemtl ").Append(id).Append('\n');
                        var t = mesh.GetTriangles(sub0 + s);
                        for (int i = 0; i < t.Length; i += 3)   // reversed winding: the X mirror flips handedness
                        {
                            int a = t[i] + vOff + 1, b = t[i + 2] + vOff + 1, c = t[i + 1] + vOff + 1;
                            obj.Append("f ").Append(Idx(a, hasUv, hasN)).Append(' ').Append(Idx(b, hasUv, hasN)).Append(' ').Append(Idx(c, hasUv, hasN)).Append('\n');
                        }
                    }
                    vOff += v.Length; meshes++;
                    if (r is SkinnedMeshRenderer) Object.Destroy(mesh);
                }
            }
            File.WriteAllText(Path.Combine(folder, name + ".obj"), obj.ToString());
            File.WriteAllText(Path.Combine(folder, name + ".mtl"), mtl.ToString());
            Debug.Log($"[Export] {name}: {meshes} meshes, {matIds.Count} materials, {texFiles.Count} textures, {vOff} verts, {skipped} skipped (unreadable)");
        }

        static string Idx(int i, bool uv, bool n) => uv && n ? $"{i}/{i}/{i}" : uv ? $"{i}/{i}" : n ? $"{i}//{i}" : i.ToString();
        static string Safe(string s) { var sb = new StringBuilder(); foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) ? c : '_'); return sb.ToString(); }
        static string NodePath(Transform t, Transform root) { var s = t.name; for (var p = t.parent; p && p != root; p = p.parent) s = p.name + "/" + s; return s; }

        static string MtlEntry(string id, Material mat, string texDir, Dictionary<Texture, string> texFiles)
        {
            var sb = new StringBuilder("newmtl " + id + "\n");
            Color c = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : Color.white;
            sb.Append("Kd ").Append(F(Mathf.Clamp01(c.r))).Append(' ').Append(F(Mathf.Clamp01(c.g))).Append(' ').Append(F(Mathf.Clamp01(c.b))).Append('\n');
            bool transparent = mat.HasProperty("_Surface") && mat.GetFloat("_Surface") > 0.5f;
            sb.Append("d ").Append(F(transparent ? Mathf.Clamp01(c.a) : 1f)).Append('\n');
            if (mat.IsKeywordEnabled("_EMISSION") && mat.HasProperty("_EmissionColor"))
            {
                var e = mat.GetColor("_EmissionColor"); float k = Mathf.Max(e.r, e.g, e.b);
                if (k > 0.001f) sb.Append("Ke ").Append(F(Mathf.Clamp01(e.r))).Append(' ').Append(F(Mathf.Clamp01(e.g))).Append(' ').Append(F(Mathf.Clamp01(e.b))).Append('\n');
            }
            var tex = mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap") : null;
            if (tex && tex.width > 0)
            {
                if (!texFiles.TryGetValue(tex, out var file))
                {
                    file = Safe(string.IsNullOrEmpty(tex.name) ? "tex" : tex.name) + "_" + texFiles.Count + ".png"; texFiles[tex] = file;
                    File.WriteAllBytes(Path.Combine(texDir, file), Readable(tex).EncodeToPNG());
                }
                sb.Append("map_Kd textures/").Append(file).Append('\n');
            }
            return sb.Append('\n').ToString();
        }

        /// GPU read-back: most runtime textures were made non-readable after upload.
        static Texture2D Readable(Texture t)
        {
            var rt = RenderTexture.GetTemporary(t.width, t.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(t, rt); var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(t.width, t.height, TextureFormat.RGBA32, false); tex.ReadPixels(new Rect(0, 0, t.width, t.height), 0, 0); tex.Apply();
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt); return tex;
        }
    }
}
