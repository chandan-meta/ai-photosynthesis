using System.Collections.Generic;
using UnityEngine;

namespace Photosynthesis
{
    /// Procedural meshes + textures for the Botanical Investigation Chamber. Everything is generated once at startup
    /// (≈0.6 M texels total) and made non-readable afterwards. Winding convention: Unity front faces = clockwise.
    static class ChamberArt
    {
        // ================================================================== meshes
        /// Flat ring sector in XZ at height y, facing up (a0 < a1, degrees). planarR > 0 → UV = XZ over a disc of that
        /// radius (planar tiling); else UV = (angle fraction, inner 0 → outer 1).
        public static Mesh Arc(float r0, float r1, float a0, float a1, float y, int segs, float planarR = 0f)
        {
            int n = (segs + 1) * 2; var v = new Vector3[n]; var nr = new Vector3[n]; var uv = new Vector2[n]; var t = new int[segs * 6];
            for (int i = 0; i <= segs; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / (float)segs) * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
                for (int k = 0; k < 2; k++)
                {
                    int j = i * 2 + k; float r = k == 0 ? r0 : r1;
                    v[j] = new Vector3(c * r, y, s * r); nr[j] = Vector3.up;
                    uv[j] = planarR > 0 ? new Vector2(v[j].x / (2 * planarR) + 0.5f, v[j].z / (2 * planarR) + 0.5f) : new Vector2(i / (float)segs, k);
                }
            }
            for (int i = 0, q = 0; i < segs; i++)
            {
                int i0 = i * 2, o0 = i0 + 1, i1 = i0 + 2, o1 = i0 + 3;
                t[q++] = i0; t[q++] = i1; t[q++] = o0; t[q++] = o0; t[q++] = i1; t[q++] = o1;
            }
            return Build("Arc", v, nr, uv, t);
        }

        /// Vertical (optionally tapered) cylinder band: radius r0 at y0 → r1 at y1. UV = (angle fraction, 0 bottom → 1 top).
        public static Mesh Wall(float r0, float r1, float a0, float a1, float y0, float y1, int segs, bool outward = true)
        {
            int n = (segs + 1) * 2; var v = new Vector3[n]; var nr = new Vector3[n]; var uv = new Vector2[n]; var t = new int[segs * 6];
            for (int i = 0; i <= segs; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / (float)segs) * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
                Vector3 d = new Vector3(c, 0, s) * (outward ? 1 : -1);
                v[i * 2] = new Vector3(c * r0, y0, s * r0); v[i * 2 + 1] = new Vector3(c * r1, y1, s * r1);
                nr[i * 2] = nr[i * 2 + 1] = d;
                uv[i * 2] = new Vector2(i / (float)segs, 0); uv[i * 2 + 1] = new Vector2(i / (float)segs, 1);
            }
            for (int i = 0, q = 0; i < segs; i++)
            {
                int b0 = i * 2, t0 = b0 + 1, b1 = b0 + 2, t1 = b0 + 3;
                if (outward) { t[q++] = b0; t[q++] = t0; t[q++] = b1; t[q++] = b1; t[q++] = t0; t[q++] = t1; }
                else { t[q++] = b0; t[q++] = b1; t[q++] = t0; t[q++] = b1; t[q++] = t1; t[q++] = t0; }
            }
            return Build("Wall", v, nr, uv, t);
        }

        /// `planes` quads crossing on the +Z axis from z=0 (width w0) to z=length (width w1). UV: u across, v along.
        /// Used with the double-sided additive material for light shafts and the sun beam.
        public static Mesh Crossed(float w0, float w1, float length, int planes = 3)
        {
            var v = new Vector3[planes * 4]; var uv = new Vector2[v.Length]; var t = new int[planes * 6];
            for (int p = 0; p < planes; p++)
            {
                float a = p * Mathf.PI / planes; Vector3 ax = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                int b = p * 4;
                v[b] = -ax * w0 * 0.5f; v[b + 1] = ax * w0 * 0.5f;
                v[b + 2] = -ax * w1 * 0.5f + Vector3.forward * length; v[b + 3] = ax * w1 * 0.5f + Vector3.forward * length;
                uv[b] = new Vector2(0, 0); uv[b + 1] = new Vector2(1, 0); uv[b + 2] = new Vector2(0, 1); uv[b + 3] = new Vector2(1, 1);
                int q = p * 6; t[q] = b; t[q + 1] = b + 2; t[q + 2] = b + 1; t[q + 3] = b + 1; t[q + 4] = b + 2; t[q + 5] = b + 3;
            }
            return Build("Crossed", v, null, uv, t);
        }

        public static Mesh Build(string name, Vector3[] v, Vector3[] n, Vector2[] uv, int[] t)
        {
            var m = new Mesh { name = name };
            if (v.Length > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.vertices = v; if (n != null) m.normals = n; m.uv = uv; m.triangles = t;
            if (n == null) m.RecalculateNormals();
            m.RecalculateBounds(); return Fx.FixNormals(m);
        }

        /// Combine parts (each its own submesh unless merge) and destroy the source meshes.
        public static Mesh Combine(string name, List<CombineInstance> parts, bool merge = true)
        {
            var m = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.CombineMeshes(parts.ToArray(), merge, true); m.RecalculateBounds();
            foreach (var p in parts) if (p.mesh) Object.Destroy(p.mesh);
            parts.Clear(); return Fx.FixNormals(m);
        }

        // ================================================================== noise
        static float Hash(int x, int y)
        {
            unchecked { uint h = (uint)(x * 374761393 + y * 668265263); h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16; return (h & 0xFFFFFF) / 16777215f; }
        }
        public static float Hash01(int i) => Hash(i, 7919);
        /// Tileable value noise (period in lattice cells).
        static float VNoise(float x, float y, int period)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y); float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            int ax = Mod(x0, period), bx = Mod(x0 + 1, period), ay = Mod(y0, period), by = Mod(y0 + 1, period);
            return Mathf.Lerp(Mathf.Lerp(Hash(ax, ay), Hash(bx, ay), fx), Mathf.Lerp(Hash(ax, by), Hash(bx, by), fx), fy);
        }
        static int Mod(int a, int m) => ((a % m) + m) % m;
        static float TFbm(float x, float y, int period) => VNoise(x, y, period) * 0.55f + VNoise(x * 2, y * 2, period * 2) * 0.3f + VNoise(x * 4, y * 4, period * 4) * 0.15f;
        static byte B(float f) => (byte)(Mathf.Clamp01(f) * 255f + 0.5f);

        static Texture2D Tex(int w, int h, Color32[] px, string name, TextureWrapMode wrap = TextureWrapMode.Clamp)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = name, wrapMode = wrap, filterMode = FilterMode.Trilinear, anisoLevel = 2 };
            t.SetPixels32(px); t.Apply(true, true); return t;
        }

        // ================================================================== canvas (vector strokes → alpha)
        public sealed class Canvas
        {
            public readonly int W, H; public readonly float[] A;
            public Canvas(int w, int h) { W = w; H = h; A = new float[w * h]; }

            /// Anti-aliased circle outline (radius r, stroke width w) at alpha a.
            public void Circle(Vector2 c, float r, float w, float a = 1f)
            {
                float m = w + 2;
                int x0 = Mathf.Max(0, (int)(c.x - r - m)), x1 = Mathf.Min(W - 1, (int)(c.x + r + m));
                int y0 = Mathf.Max(0, (int)(c.y - r - m)), y1 = Mathf.Min(H - 1, (int)(c.y + r + m));
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float d = new Vector2(x + 0.5f - c.x, y + 0.5f - c.y).magnitude - r;
                        float cov = Mathf.Clamp01(w * 0.5f - Mathf.Abs(d) + 0.5f) * a;
                        int i = y * W + x; if (cov > A[i]) A[i] = cov;
                    }
            }

            /// White texture, alpha = coverage (for additive / transparent glows).
            public Texture2D ToAlpha(string name)
            {
                var px = new Color32[W * H];
                for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, B(A[i]));
                return Tex(W, H, px, name);
            }
        }

        // ================================================================== small textures
        /// Tileable soil: dark loam with granules.
        public static Texture2D Soil()
        {
            const int N = 256; var p = new Color32[N * N];
            Color lo = new Color(0.09f, 0.06f, 0.04f), hi = new Color(0.26f, 0.18f, 0.11f), moss = new Color(0.16f, 0.22f, 0.09f);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float n = TFbm(x / 32f, y / 32f, 8), grain = Hash(x, y) > 0.9f ? 0.35f : 0f, m = Mathf.Clamp01((TFbm(x / 64f + 3, y / 64f, 4) - 0.55f) * 3f);
                    var c = Color.Lerp(Color.Lerp(lo, hi, n * 0.9f + grain), moss, m * 0.6f);
                    p[y * N + x] = new Color32(B(c.r), B(c.g), B(c.b), 255);
                }
            return Tex(N, N, p, "Chamber Soil", TextureWrapMode.Repeat);
        }

        /// Underground cross-section: soil horizons (u around, v 0 bottom → 1 top), alpha for the see-through volume.
        public static Texture2D Strata()
        {
            const int W = 256, H = 256; var p = new Color32[W * H];
            Color top = new Color(0.16f, 0.10f, 0.06f), loam = new Color(0.34f, 0.22f, 0.12f), clay = new Color(0.46f, 0.28f, 0.15f), rock = new Color(0.36f, 0.33f, 0.30f);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float v = y / (H - 1f) + (TFbm(x / 32f, y / 64f, 8) - 0.5f) * 0.08f;
                    Color c = v > 0.82f ? top : v > 0.5f ? Color.Lerp(loam, top, (v - 0.5f) / 0.32f * 0.4f) : v > 0.22f ? clay : rock;
                    float pebble = Hash(x / 6, y / 6) > 0.93f ? 0.25f : 0f;
                    c = c * (0.8f + 0.4f * TFbm(x / 16f, y / 16f, 16)) + new Color(pebble, pebble, pebble) * 0.5f;
                    p[y * W + x] = new Color32(B(c.r), B(c.g), B(c.b), B(0.55f + 0.25f * (1 - v)));
                }
            return Tex(W, H, p, "Chamber Strata", TextureWrapMode.Repeat);
        }

        /// Light shaft profile: soft across (u), fades in above the floor and out toward the roof (v).
        /// beam: fades in over the first `fadeIn` of its length (v 0 = sky end).
        public static Texture2D Shaft(bool beam, float fadeIn = 0f)
        {
            const int W = 64, H = 128; var p = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = (x + 0.5f) / W - 0.5f, v = (y + 0.5f) / H;
                    float across = Mathf.Exp(-u * u / (beam ? 0.03f : 0.045f));
                    float along = beam ? Mathf.SmoothStep(0f, 1f, v / fadeIn) * Mathf.SmoothStep(1f, 0.6f, (v - 0.94f) / 0.06f)
                                       : Mathf.SmoothStep(0f, 1f, v / 0.25f) * Mathf.SmoothStep(1f, 0f, (v - 0.7f) / 0.3f);
                    p[y * W + x] = new Color32(255, 255, 255, B(across * along));
                }
            return Tex(W, H, p, beam ? "Beam" : "Shaft");
        }

        /// Soft band across a ring (v inner → outer) for glow halos on the floor.
        public static Texture2D Halo()
        {
            const int W = 4, H = 64; var p = new Color32[W * H];
            for (int y = 0; y < H; y++)
            {
                float v = (y + 0.5f) / H - 0.5f; byte al = B(Mathf.Exp(-v * v / 0.03f));
                for (int x = 0; x < W; x++) p[y * W + x] = new Color32(255, 255, 255, al);
            }
            return Tex(W, H, p, "Halo");
        }

        /// Silhouette ring for the scenery outside the chamber (u around 0..1 periodic, v up). rgb = haze gradient.
        public static Texture2D Hills(int seed, float baseH, float amp, float trees, int crowns, Color low, Color high)
        {
            const int W = 1024, H = 128; var p = new Color32[W * H];
            for (int x = 0; x < W; x++)
            {
                float u = x / (float)W;
                float ridge = baseH + amp * (VNoise(u * 6f, seed, 6) * 0.6f + VNoise(u * 17f, seed + 3, 17) * 0.3f + VNoise(u * 53f, seed + 7, 53) * 0.1f);
                if (trees > 0) ridge += trees * Mathf.Pow(Mathf.Abs(Mathf.Sin(u * Mathf.PI * crowns + VNoise(u * 40f, seed + 11, 40) * 3f)), 0.6f) * (0.6f + 0.4f * VNoise(u * 90f, seed + 5, 90));
                for (int y = 0; y < H; y++)
                {
                    float v = y / (H - 1f), al = Mathf.Clamp01((ridge - v) * H * 0.8f + 0.5f);
                    var c = Color.Lerp(low, high, Mathf.Pow(v / Mathf.Max(ridge, 0.01f), 1.5f));
                    p[y * W + x] = new Color32(B(c.r), B(c.g), B(c.b), B(al));
                }
            }
            var t = Tex(W, H, p, "Hills"); t.wrapModeU = TextureWrapMode.Repeat; return t;
        }
    }
}
