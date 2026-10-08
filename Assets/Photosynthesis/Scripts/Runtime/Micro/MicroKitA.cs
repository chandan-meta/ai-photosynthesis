using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Photosynthesis
{
    /// Toolkit shared by LeafSurfaceWorld, SoilWorld and XylemRide: correctly wound procedural meshes, procedural
    /// textures, world mood (fog / ambient / sun), billboard glows, light shafts and an arc-length path.
    /// Winding rule (Unity): a triangle's front face normal is Cross(v1 - v0, v2 - v0).
    public static class MicroKitA
    {
        // ================================================================ mood
        public static void Mood(Color fog, float density, Color sky, Color equator, Color ground)
        {
            if (!RenderSettings.fog) RenderSettings.fog = true;
            if (RenderSettings.fogMode != FogMode.ExponentialSquared) RenderSettings.fogMode = FogMode.ExponentialSquared;
            if (RenderSettings.ambientMode != AmbientMode.Trilight) RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.fogColor = fog; RenderSettings.fogDensity = density;
            RenderSettings.ambientSkyColor = sky; RenderSettings.ambientEquatorColor = equator; RenderSettings.ambientGroundColor = ground;
        }

        /// Re-aims / recolours the scene sun while its world is visible and puts it back when the world hides (OnDisable),
        /// so the chamber (and any other world) always gets the sun it left.
        public class SunOverride : MonoBehaviour
        {
            Light sun; float intensity; Color color; Quaternion rotation; bool wasOn, saved;

            public void Apply(float i, Color c, Vector3 euler)
            {
                if (!saved)
                {
                    sun = RenderSettings.sun ? RenderSettings.sun : (Module.I && Module.I.chamber ? Module.I.chamber.sun : null);
                    if (!sun) return;
                    intensity = sun.intensity; color = sun.color; rotation = sun.transform.rotation; wasOn = sun.enabled; saved = true;
                }
                sun.enabled = true; sun.intensity = i; sun.color = c; sun.transform.rotation = Quaternion.Euler(euler);
            }

            public void Set(float i, Color c) { if (saved && sun) { sun.intensity = i; sun.color = c; } }

            void OnDisable()
            {
                if (saved && sun) { sun.intensity = intensity; sun.color = color; sun.transform.rotation = rotation; sun.enabled = wasOn; }
                saved = false;
            }
        }

        /// Keeps a quad facing the scripted camera (soft glows, halos).
        public class Billboard : MonoBehaviour
        {
            static Transform cam;
            void LateUpdate()
            {
                if (!cam) { if (Module.I && Module.I.cam) cam = Module.I.cam.transform; else return; }
                transform.rotation = cam.rotation;
            }
        }

        // ================================================================ path
        /// Polyline with arc-length lookup and parallel-transport frames. No allocation per query.
        public class Path
        {
            public readonly List<Vector3> pts;
            readonly float[] cum; readonly Vector3[] nrm;
            public float Length => cum[cum.Length - 1];
            public float S(int index) => cum[Mathf.Clamp(index, 0, cum.Length - 1)];

            public Path(List<Vector3> p)
            {
                pts = p; cum = new float[p.Count]; nrm = new Vector3[p.Count];
                for (int i = 1; i < p.Count; i++) cum[i] = cum[i - 1] + Vector3.Distance(p[i - 1], p[i]);
                Vector3 t0 = Tan(0), n = Vector3.Cross(t0, Mathf.Abs(t0.y) > 0.95f ? Vector3.right : Vector3.up).normalized;
                for (int i = 0; i < p.Count; i++) { n = Vector3.ProjectOnPlane(n, Tan(i)).normalized; nrm[i] = n; }
            }

            Vector3 Tan(int i) => (pts[Mathf.Min(i + 1, pts.Count - 1)] - pts[Mathf.Max(i - 1, 0)]).normalized;

            int Seg(float s, out float f)
            {
                s = Mathf.Clamp(s, 0, Length);
                int lo = 0, hi = cum.Length - 1;
                while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (cum[mid] <= s) lo = mid; else hi = mid; }
                float len = cum[hi] - cum[lo]; f = len > 0 ? (s - cum[lo]) / len : 0; return lo;
            }

            public Vector3 At(float s) { int i = Seg(s, out float f); return Vector3.LerpUnclamped(pts[i], pts[i + 1], f); }

            public void Frame(float s, out Vector3 pos, out Vector3 tan, out Vector3 nor, out Vector3 bin)
            {
                int i = Seg(s, out float f);
                pos = Vector3.LerpUnclamped(pts[i], pts[i + 1], f);
                tan = (pts[i + 1] - pts[i]).normalized;
                nor = Vector3.ProjectOnPlane(Vector3.Lerp(nrm[i], nrm[i + 1], f), tan).normalized;
                bin = Vector3.Cross(tan, nor);
            }

            /// Arc length of the point nearest to p (brute force — build time only).
            public float Nearest(Vector3 p)
            {
                int best = 0; float bd = float.MaxValue;
                for (int i = 0; i < pts.Count; i++) { float d = (pts[i] - p).sqrMagnitude; if (d < bd) { bd = d; best = i; } }
                return cum[best];
            }
        }

        // ================================================================ meshes
        /// Tube along pts. Per-point radius, elliptical cross-section (sx along the frame normal, sy along the binormal),
        /// optional explicit v coordinate (default: arc length). insideOut = visible from inside. closed = pts[0]==pts[last] loop.
        public static Mesh Tube(IList<Vector3> pts, Func<int, float> radius, int sides, bool insideOut = false, Func<int, float> v = null,
            float sx = 1f, float sy = 1f, Vector3? firstNormal = null, bool closed = false)
        {
            int n = pts.Count, ring = sides + 1;
            var vert = new Vector3[n * ring]; var nr = new Vector3[vert.Length]; var uv = new Vector2[vert.Length]; var tri = new int[(n - 1) * sides * 6];
            Vector3 T(int i)
            {
                if (closed && (i == 0 || i == n - 1)) return (pts[1] - pts[n - 2]).normalized;
                return (pts[Mathf.Min(i + 1, n - 1)] - pts[Mathf.Max(i - 1, 0)]).normalized;
            }
            Vector3 t0 = T(0), nor = firstNormal ?? Vector3.Cross(t0, Mathf.Abs(t0.y) > 0.95f ? Vector3.right : Vector3.up);
            float len = 0;
            for (int i = 0; i < n; i++)
            {
                Vector3 t = T(i);
                nor = Vector3.ProjectOnPlane(nor, t).normalized; Vector3 bin = Vector3.Cross(t, nor);
                if (i > 0) len += Vector3.Distance(pts[i], pts[i - 1]);
                float r = radius(i), vv = v != null ? v(i) : len;
                for (int k = 0; k <= sides; k++)
                {
                    float a = k / (float)sides * Mathf.PI * 2, c = Mathf.Cos(a), s = Mathf.Sin(a);
                    Vector3 nn = (c / sx * nor + s / sy * bin).normalized;
                    int idx = i * ring + k;
                    vert[idx] = pts[i] + (c * sx * nor + s * sy * bin) * r; nr[idx] = insideOut ? -nn : nn; uv[idx] = new Vector2(k / (float)sides, vv);
                }
            }
            int q = 0;
            for (int i = 0; i < n - 1; i++)
                for (int k = 0; k < sides; k++)
                {
                    int a = i * ring + k, b = a + ring;
                    if (!insideOut) { tri[q++] = a; tri[q++] = a + 1; tri[q++] = b; tri[q++] = a + 1; tri[q++] = b + 1; tri[q++] = b; }
                    else { tri[q++] = a; tri[q++] = b; tri[q++] = a + 1; tri[q++] = a + 1; tri[q++] = b; tri[q++] = b + 1; }
                }
            return Build("Tube", vert, nr, uv, tri);
        }

        /// UV sphere, radius 1. inward = visible from inside (domes, cavities).
        public static Mesh Sphere(int lon, int lat, bool inward = false)
        {
            var v = new Vector3[(lon + 1) * (lat + 1)]; var nr = new Vector3[v.Length]; var uv = new Vector2[v.Length]; var tri = new int[lon * lat * 6];
            for (int j = 0; j <= lat; j++)
                for (int i = 0; i <= lon; i++)
                {
                    float th = Mathf.PI * j / lat, ph = 2 * Mathf.PI * (i % lon) / lon; int k = j * (lon + 1) + i;
                    v[k] = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    nr[k] = inward ? -v[k] : v[k]; uv[k] = new Vector2(i / (float)lon, 1 - j / (float)lat);
                }
            int t = 0;
            for (int j = 0; j < lat; j++)
                for (int i = 0; i < lon; i++)
                {
                    int a = j * (lon + 1) + i, b = a + 1, c = a + lon + 1, d = c + 1;
                    if (!inward) { tri[t++] = a; tri[t++] = b; tri[t++] = c; tri[t++] = b; tri[t++] = d; tri[t++] = c; }
                    else { tri[t++] = a; tri[t++] = c; tri[t++] = b; tri[t++] = b; tri[t++] = c; tri[t++] = d; }
                }
            return Build("Sphere", v, nr, uv, tri);
        }

        /// Copy of a mesh with every vertex moved by f, smooth normals welded across UV seams (rocks, blobs).
        public static Mesh Deform(Mesh src, Func<Vector3, Vector3> f)
        {
            var v = src.vertices;
            for (int i = 0; i < v.Length; i++) v[i] = f(v[i]);
            var m = new Mesh { name = "Deformed", vertices = v, uv = src.uv, triangles = src.triangles };
            m.RecalculateNormals();
            var nr = m.normals; var sum = new Dictionary<Vector3Int, Vector3>();
            Vector3Int Key(Vector3 p) => new Vector3Int(Mathf.RoundToInt(p.x * 2000), Mathf.RoundToInt(p.y * 2000), Mathf.RoundToInt(p.z * 2000));
            for (int i = 0; i < v.Length; i++) { var k = Key(v[i]); sum.TryGetValue(k, out var s); sum[k] = s + nr[i]; }
            for (int i = 0; i < v.Length; i++) nr[i] = sum[Key(v[i])].normalized;
            m.normals = nr; m.RecalculateBounds();
            return m;
        }

        /// Flat disc, radius 1, in XZ facing +y, radial UV (centre 0.5,0.5) — pairs with Fx.SoftDot for soft ellipses.
        public static Mesh Disc(int seg)
        {
            var v = new Vector3[seg + 2]; var nr = new Vector3[v.Length]; var uv = new Vector2[v.Length]; var tri = new int[seg * 3];
            uv[0] = new Vector2(0.5f, 0.5f);
            for (int i = 0; i <= seg; i++)
            {
                float a = 2 * Mathf.PI * i / seg, c = Mathf.Cos(a), s = Mathf.Sin(a);
                v[i + 1] = new Vector3(c, 0, s); uv[i + 1] = new Vector2(0.5f + 0.5f * c, 0.5f + 0.5f * s);
            }
            for (int i = 0; i < v.Length; i++) nr[i] = Vector3.up;
            for (int i = 0; i < seg; i++) { tri[i * 3] = 0; tri[i * 3 + 1] = i + 2; tri[i * 3 + 2] = i + 1; }
            return Build("Disc", v, nr, uv, tri);
        }

        /// Flat annulus, outer radius 1, in XZ facing +y. v runs inner (0) → outer (1) — pairs with LineTex for a soft ring.
        public static Mesh Ring(int seg, float inner)
        {
            var v = new Vector3[(seg + 1) * 2]; var nr = new Vector3[v.Length]; var uv = new Vector2[v.Length]; var tri = new int[seg * 6];
            for (int i = 0; i <= seg; i++)
            {
                float a = 2 * Mathf.PI * i / seg; var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v[2 * i] = d * inner; v[2 * i + 1] = d; uv[2 * i] = new Vector2(i / (float)seg, 0); uv[2 * i + 1] = new Vector2(i / (float)seg, 1);
                nr[2 * i] = nr[2 * i + 1] = Vector3.up;
            }
            for (int i = 0; i < seg; i++)
            {
                int a = 2 * i, b = a + 2, o = a + 1, ob = a + 3, t = i * 6;
                tri[t] = a; tri[t + 1] = b; tri[t + 2] = o; tri[t + 3] = b; tri[t + 4] = ob; tri[t + 5] = o;
            }
            return Build("Ring", v, nr, uv, tri);
        }

        /// Octahedron, radius 1, smooth normals — the cheapest "dot" (chloroplasts, grains).
        public static Mesh Octa()
        {
            var v = new[] { Vector3.up, Vector3.forward, Vector3.right, Vector3.back, Vector3.left, Vector3.down };
            var tri = new[] { 0, 1, 2, 0, 2, 3, 0, 3, 4, 0, 4, 1, 5, 2, 1, 5, 3, 2, 5, 4, 3, 5, 1, 4 };
            return Build("Octa", v, (Vector3[])v.Clone(), new Vector2[6], tri);
        }

        /// Two crossed quads hanging down from the origin along −y (length 1, width 1); v = 1 at the top. Light shafts.
        public static Mesh Shaft()
        {
            var v = new[] { new Vector3(-0.5f, 0, 0), new Vector3(0.5f, 0, 0), new Vector3(-0.5f, -1, 0), new Vector3(0.5f, -1, 0),
                            new Vector3(0, 0, -0.5f), new Vector3(0, 0, 0.5f), new Vector3(0, -1, -0.5f), new Vector3(0, -1, 0.5f) };
            var uv = new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 0), new Vector2(1, 0) };
            var nr = new Vector3[8]; for (int i = 0; i < 8; i++) nr[i] = i < 4 ? Vector3.back : Vector3.right;
            return Build("Shaft", v, nr, uv, new[] { 0, 1, 2, 1, 3, 2, 4, 5, 6, 5, 7, 6 });
        }

        public static Mesh Build(string name, Vector3[] v, Vector3[] nr, Vector2[] uv, int[] tri)
        {
            var m = new Mesh { name = name };
            if (v.Length > 65000) m.indexFormat = IndexFormat.UInt32;
            m.vertices = v; m.normals = nr; m.uv = uv; m.triangles = tri; m.RecalculateBounds();
            return Fx.FixNormals(m);
        }

        public static CombineInstance Part(Mesh mesh, Vector3 pos, Quaternion rot, Vector3 scale) =>
            new CombineInstance { mesh = mesh, transform = Matrix4x4.TRS(pos, rot, scale) };

        public static Mesh Merge(List<CombineInstance> parts, string name)
        {
            var m = new Mesh { name = name }; int count = 0;
            foreach (var p in parts) count += p.mesh.vertexCount;
            if (count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.CombineMeshes(parts.ToArray(), true, true); m.RecalculateBounds();
            return Fx.FixNormals(m);
        }

        // ================================================================ objects
        /// Shared soft additive sprite material keyed by colour (do not mutate).
        static readonly Dictionary<Color, Material> glowMats = new Dictionary<Color, Material>();
        public static Material GlowMat(Color hdr)
        {
            if (glowMats.TryGetValue(hdr, out var m) && m) return m;
            return glowMats[hdr] = NewGlowMat(hdr);
        }
        /// Own (animatable) soft additive sprite material.
        public static Material NewGlowMat(Color hdr) { var m = Fx.Mat(MatKind.Additive, hdr); Fx.SetTexture(m, Fx.SoftDot); return m; }

        /// Camera-facing soft glow quad.
        public static GameObject Glow(Transform parent, Vector3 localPos, float size, Material m)
        {
            var g = Fx.Prim(PrimitiveType.Quad, parent, localPos, Vector3.one * size, m, false, "Glow");
            g.AddComponent<Billboard>(); return g;
        }

        static Mesh shaftMesh;
        /// Additive light shaft (crossed quads) from `top` along `dir`.
        public static GameObject Shaft(Transform parent, Vector3 top, Vector3 dir, float length, float width, Material m)
        {
            if (!shaftMesh) shaftMesh = Shaft();
            var g = Fx.MeshObj("Shaft", shaftMesh, m, parent);
            g.transform.localPosition = top; g.transform.localRotation = Quaternion.FromToRotation(Vector3.down, dir.normalized);
            g.transform.localScale = new Vector3(width, length, width);
            return g;
        }

        // ================================================================ textures
        public static Texture2D Tex(int w, int h, Func<float, float, Color> f, bool repeat = false, bool mips = true)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, mips) { wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp, anisoLevel = 4, name = "MicroKitA" };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[y * w + x] = f((x + 0.5f) / w, (y + 0.5f) / h);
            t.SetPixels32(px); t.Apply(mips, true);
            return t;
        }

        static Texture2D line, shaftTex, streak;
        /// White, alpha a soft bump across v (constant along u) — trails, rings.
        public static Texture2D LineTex => line ? line : line = Tex(8, 32, (u, v) => new Color(1, 1, 1, Mathf.Pow(Mathf.Max(0f, Mathf.Sin(v * Mathf.PI)), 2f)));
        /// White, soft across u and fading toward the bottom (v = 0) — light shafts.
        public static Texture2D ShaftTex => shaftTex ? shaftTex : shaftTex = Tex(32, 64, (u, v) => new Color(1, 1, 1, Mathf.Pow(Mathf.Max(0f, Mathf.Sin(u * Mathf.PI)), 2.5f) * Mathf.Pow(v, 0.8f) * Mathf.Clamp01((1 - v) * 8f)));
        /// White capsule soft on all sides — speed streaks.
        public static Texture2D StreakTex => streak ? streak : streak = Tex(64, 16, (u, v) => new Color(1, 1, 1, Mathf.Pow(Mathf.Max(0f, Mathf.Sin(v * Mathf.PI)), 2f) * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(u * Mathf.PI)), 0.7f)));

        // ================================================================ noise
        public static float Hash(int x, int y, int s = 0)
        {
            unchecked
            {
                uint h = (uint)x * 0x8da6b343u ^ (uint)y * 0xd8163841u ^ (uint)s * 0xcb1ab31fu;
                h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
                return (h & 0xffffff) / 16777216f;
            }
        }

        /// Cheap 3D noise in ~[0,1] (three Perlin planes).
        public static float Noise3(Vector3 p) =>
            (Mathf.PerlinNoise(p.x, p.y) + Mathf.PerlinNoise(p.y + 31.7f, p.z) + Mathf.PerlinNoise(p.z + 57.1f, p.x + 11.3f)) / 3f;

        /// Tileable value noise (period cells across the 0..1 square).
        public static float TileNoise(float u, float v, int period, int seed)
        {
            float x = u * period, y = v * period; int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy; fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            float H(int a, int b) => Hash(((a % period) + period) % period, ((b % period) + period) % period, seed);
            return Mathf.Lerp(Mathf.Lerp(H(ix, iy), H(ix + 1, iy), fx), Mathf.Lerp(H(ix, iy + 1), H(ix + 1, iy + 1), fx), fy);
        }

        public static float Rand(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);
        public static Vector3 OnSphere(System.Random r)
        {
            float z = Rand(r, -1, 1), a = Rand(r, 0, Mathf.PI * 2), s = Mathf.Sqrt(1 - z * z);
            return new Vector3(s * Mathf.Cos(a), z, s * Mathf.Sin(a));
        }
        public static float Smooth01(float x) { x = Mathf.Clamp01(x); return x * x * (3 - 2 * x); }
    }
}
