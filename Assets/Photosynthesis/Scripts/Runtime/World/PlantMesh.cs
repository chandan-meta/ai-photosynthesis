using System;
using System.Collections.Generic;
using UnityEngine;

namespace Photosynthesis
{
    /// Small procedural mesh builder for the hero plant: tubes, two-sided parametric sheets, flat ribbons, merged parts,
    /// sub-meshes. `aux` carries one scalar per vertex (distance along the vein network, used by the energy wave).
    /// Winding follows Unity's convention: the front face of (a,b,c) faces Cross(b-a, c-a).
    public class PlantMesh
    {
        public readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<Color32> col = new List<Color32>();
        public readonly List<float> aux = new List<float>();
        public Color32 tint = new Color32(255, 255, 255, 255);
        readonly List<List<int>> subs = new List<List<int>>();

        public List<int> Sub(int i) { while (subs.Count <= i) subs.Add(new List<int>()); return subs[i]; }
        public int Add(Vector3 p, Vector3 nr, Vector2 t, float a = 0f) { v.Add(p); n.Add(nr); uv.Add(t); aux.Add(a); col.Add(tint); return v.Count - 1; }

        /// Triangle wound so its front faces `want`.
        public void Tri(int a, int b, int c, Vector3 want, int sub)
        {
            var t = Sub(sub);
            if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), want) >= 0) { t.Add(a); t.Add(b); t.Add(c); }
            else { t.Add(a); t.Add(c); t.Add(b); }
        }

        /// Tube along a polyline (parallel-transport frames, no twist). radius(t), t = normalised arc length. aux = aux0 + arc length.
        public void Tube(IList<Vector3> pts, Func<float, float> radius, int sides, int sub, float uvRepeat = 1f, float aux0 = 0f)
        {
            int cnt = pts.Count; if (cnt < 2) return;
            float total = 0; for (int i = 1; i < cnt; i++) total += Vector3.Distance(pts[i - 1], pts[i]);
            Vector3 t0 = (pts[1] - pts[0]).normalized;
            Vector3 nrm = Vector3.Cross(t0, Mathf.Abs(Vector3.Dot(t0, Vector3.up)) > 0.95f ? Vector3.right : Vector3.up).normalized;
            int start = v.Count; float len = 0;
            for (int i = 0; i < cnt; i++)
            {
                Vector3 tan = i == 0 ? pts[1] - pts[0] : i == cnt - 1 ? pts[i] - pts[i - 1] : pts[i + 1] - pts[i - 1];
                tan.Normalize();
                nrm = Vector3.ProjectOnPlane(nrm, tan).normalized;
                if (nrm.sqrMagnitude < 1e-4f) nrm = Vector3.Cross(tan, Vector3.right).normalized;
                Vector3 bin = Vector3.Cross(tan, nrm);
                if (i > 0) len += Vector3.Distance(pts[i], pts[i - 1]);
                float r = radius(total > 0 ? len / total : 0);
                for (int s = 0; s <= sides; s++)
                {
                    float a = s / (float)sides * Mathf.PI * 2;
                    Vector3 d = Mathf.Cos(a) * nrm + Mathf.Sin(a) * bin;
                    Add(pts[i] + d * r, d, new Vector2(s / (float)sides, len * uvRepeat), aux0 + len);
                }
            }
            var tri = Sub(sub);
            for (int i = 0; i < cnt - 1; i++)
                for (int s = 0; s < sides; s++)
                {
                    int a = start + i * (sides + 1) + s, b = a + sides + 1;
                    tri.Add(a); tri.Add(a + 1); tri.Add(b); tri.Add(a + 1); tri.Add(b + 1); tri.Add(b);
                }
        }

        /// Flat strip along pts lying in the surface whose normals are `up` (lifted along them). aux = aux0 + arc length.
        public void Ribbon(IList<Vector3> pts, IList<Vector3> up, float w0, float w1, float lift, int sub, float aux0 = 0f)
        {
            int cnt = pts.Count; if (cnt < 2) return;
            float total = 0; for (int i = 1; i < cnt; i++) total += Vector3.Distance(pts[i - 1], pts[i]);
            int start = v.Count; float len = 0;
            for (int i = 0; i < cnt; i++)
            {
                Vector3 tan = (i == 0 ? pts[1] - pts[0] : i == cnt - 1 ? pts[i] - pts[i - 1] : pts[i + 1] - pts[i - 1]).normalized;
                if (i > 0) len += Vector3.Distance(pts[i], pts[i - 1]);
                Vector3 side = Vector3.Cross(up[i], tan).normalized * (Mathf.Lerp(w0, w1, total > 0 ? len / total : 0) * 0.5f);
                Vector3 c = pts[i] + up[i] * lift;
                Add(c - side, up[i], new Vector2(0, len), aux0 + len); Add(c + side, up[i], new Vector2(1, len), aux0 + len);
            }
            var tri = Sub(sub);
            for (int i = 0; i < cnt - 1; i++) { int a = start + i * 2; tri.Add(a); tri.Add(a + 2); tri.Add(a + 1); tri.Add(a + 1); tri.Add(a + 2); tri.Add(a + 3); }
        }

        /// Parametric sheet P(u,v) on a (nu × nv) grid. Normals from N (or finite differences of P).
        /// Faces are wound toward the normal. thick > 0 with subBack ≥ 0 adds a reversed back layer (offset by thick).
        public void Surface(Func<float, float, Vector3> P, Func<float, float, Vector3> N, Func<float, float, Vector2> UV, int nu, int nv, int sub, float thick = 0f, int subBack = -1)
        {
            int cols = nv + 1;
            var pos = new Vector3[(nu + 1) * cols]; var nor = new Vector3[pos.Length];
            for (int i = 0; i <= nu; i++)
                for (int j = 0; j <= nv; j++)
                {
                    float u = i / (float)nu, w = j / (float)nv; int k = i * cols + j;
                    pos[k] = P(u, w); nor[k] = N != null ? N(u, w) : FdNormal(P, u, w);
                }
            int layers = thick > 0 && subBack >= 0 ? 2 : 1;
            for (int layer = 0; layer < layers; layer++)
            {
                bool back = layer == 1; float off = layers == 2 ? thick * 0.5f * (back ? -1 : 1) : 0f;
                int start = v.Count;
                for (int i = 0; i <= nu; i++)
                    for (int j = 0; j <= nv; j++) { int k = i * cols + j; Add(pos[k] + nor[k] * off, back ? -nor[k] : nor[k], UV(i / (float)nu, j / (float)nv)); }
                var tri = Sub(back ? subBack : sub);
                for (int i = 0; i < nu; i++)
                    for (int j = 0; j < nv; j++)
                    {
                        int a = i * cols + j, b = a + cols, c = a + 1, d = b + 1;
                        Vector3 face = Vector3.Cross(pos[b] - pos[a], pos[c] - pos[a]) + Vector3.Cross(pos[b] - pos[c], pos[d] - pos[c]);
                        bool flip = Vector3.Dot(face, nor[a] + nor[b] + nor[c] + nor[d]) < 0; if (back) flip = !flip;
                        a += start; b += start; c += start; d += start;
                        if (!flip) { tri.Add(a); tri.Add(b); tri.Add(c); tri.Add(c); tri.Add(b); tri.Add(d); }
                        else { tri.Add(a); tri.Add(c); tri.Add(b); tri.Add(c); tri.Add(d); tri.Add(b); }
                    }
            }
        }

        static Vector3 FdNormal(Func<float, float, Vector3> P, float u, float w)
        {
            const float e = 1e-3f;
            for (int k = 0; k < 4; k++)
            {
                float ww = Mathf.Lerp(w, 0.5f, k * 0.02f);
                var nr = Vector3.Cross(P(u + e, ww) - P(u - e, ww), P(u, ww + e) - P(u, ww - e));
                if (nr.sqrMagnitude > 1e-14f) return nr.normalized;
            }
            return Vector3.up;
        }

        public void Merge(Mesh m, Matrix4x4 mx, int sub)
        {
            int start = v.Count; var mv = m.vertices; var mn = m.normals; var mu = m.uv;
            var nx = mx.inverse.transpose;   // normals need the inverse-transpose under non-uniform scale (flattened moss cushions)
            for (int i = 0; i < mv.Length; i++) Add(mx.MultiplyPoint3x4(mv[i]), nx.MultiplyVector(mn[i]).normalized, mu.Length > i ? mu[i] : Vector2.zero);
            var t = Sub(sub); foreach (var idx in m.triangles) t.Add(start + idx);
        }

        public Mesh Build(string name)
        {
            var m = new Mesh { name = name };
            if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetColors(col);
            m.subMeshCount = Mathf.Max(1, subs.Count);
            for (int i = 0; i < subs.Count; i++) m.SetTriangles(subs[i], i, false);
            m.RecalculateBounds();
            return Fx.FixNormals(m);
        }

        // ---------------------------------------------------------------- stock shapes
        /// UV sphere, radius 0.5 (same size as the Unity primitive) with a fraction of the triangles.
        public static Mesh Sphere(int lon, int lat)
        {
            var b = new PlantMesh();
            Vector3 P(float u, float w) { float th = -u * Mathf.PI * 2, ph = w * Mathf.PI; return new Vector3(Mathf.Sin(ph) * Mathf.Cos(th), -Mathf.Cos(ph), Mathf.Sin(ph) * Mathf.Sin(th)) * 0.5f; }
            b.Surface(P, (u, w) => P(u, w) * 2f, (u, w) => new Vector2(u, w), lon, lat, 0);
            return b.Build("PlantSphere");
        }

        /// Glucose bead: hexagonal prism, radius 0.5, axis Z.
        public static Mesh Hexagon()
        {
            var b = new PlantMesh(); const float hz = 0.22f;
            for (int side = -1; side <= 1; side += 2)
            {
                var nz = new Vector3(0, 0, side); int c = b.Add(nz * hz, nz, new Vector2(0.5f, 0.5f));
                for (int i = 0; i < 6; i++) { float a = i * Mathf.PI / 3; b.Add(new Vector3(Mathf.Cos(a) * 0.5f, Mathf.Sin(a) * 0.5f, hz * side), nz, new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f)); }
                for (int i = 0; i < 6; i++) b.Tri(c, c + 1 + i, c + 1 + (i + 1) % 6, nz, 0);
            }
            for (int i = 0; i < 6; i++)
            {
                float a0 = i * Mathf.PI / 3, a1 = (i + 1) * Mathf.PI / 3, am = (a0 + a1) * 0.5f;
                var nr = new Vector3(Mathf.Cos(am), Mathf.Sin(am), 0);
                Vector3 p0 = new Vector3(Mathf.Cos(a0) * 0.5f, Mathf.Sin(a0) * 0.5f, 0), p1 = new Vector3(Mathf.Cos(a1) * 0.5f, Mathf.Sin(a1) * 0.5f, 0);
                int k = b.Add(p0 + Vector3.forward * hz, nr, Vector2.zero); b.Add(p0 - Vector3.forward * hz, nr, Vector2.zero);
                b.Add(p1 + Vector3.forward * hz, nr, Vector2.zero); b.Add(p1 - Vector3.forward * hz, nr, Vector2.zero);
                b.Tri(k, k + 1, k + 2, nr, 0); b.Tri(k + 2, k + 1, k + 3, nr, 0);
            }
            return b.Build("GlucoseHex");
        }

        /// Soft flat ring in XZ, outer radius 0.5, vertex alpha 0 → 1 → 0 across its width (use with ParticleAdditive).
        public static Mesh Ring(int seg)
        {
            var b = new PlantMesh(); float[] rad = { 0.30f, 0.44f, 0.5f }; byte[] al = { 0, 255, 0 };
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2; var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                for (int r = 0; r < 3; r++) { b.tint = new Color32(255, 255, 255, al[r]); b.Add(d * rad[r], Vector3.up, new Vector2(i / (float)seg, r * 0.5f)); }
            }
            for (int i = 0; i < seg; i++)
                for (int r = 0; r < 2; r++) { int a = i * 3 + r, c = a + 3; b.Tri(a, c, a + 1, Vector3.up, 0); b.Tri(a + 1, c, c + 1, Vector3.up, 0); }
            return b.Build("SignalRing");
        }

        /// Small procedural texture (sRGB, mipmapped, repeat).
        public static Texture2D Tex(int w, int h, Func<float, float, Color> f, string name)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[y * w + x] = f((x + 0.5f) / w, (y + 0.5f) / h);
            t.SetPixels32(px); t.Apply(true);
            return t;
        }

        public static float Sstep(float e0, float e1, float x) { float t = Mathf.Clamp01((x - e0) / (e1 - e0)); return t * t * (3 - 2 * t); }
    }
}
