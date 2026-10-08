using System.Collections.Generic;
using UnityEngine;

namespace Photosynthesis
{
    /// Procedural meshes + textures for the Botanical Investigation Chamber. Everything is generated once at startup
    /// (≈1.5 M texels total) and made non-readable afterwards. Winding convention: Unity front faces = clockwise.
    static class ChamberArt
    {
        public const float FloorR = 13.3f;

        // ================================================================== meshes
        /// Flat ring sector in XZ at height y, facing up (a0 < a1, degrees). planarR > 0 → UV = XZ over a disc of that
        /// radius (shares the floor textures); else UV = (angle fraction, inner 0 → outer 1).
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

        /// Vertical quad closing the end of a ring sector (radial, from r0 to r1, y0 → y1) at angle a.
        public static Mesh EndCap(float r0, float r1, float a, float y0, float y1, bool facingCcw)
        {
            float ra = a * Mathf.Deg2Rad; Vector3 d = new Vector3(Mathf.Cos(ra), 0, Mathf.Sin(ra));
            Vector3 n = new Vector3(-d.z, 0, d.x) * (facingCcw ? 1 : -1);
            var v = new[] { d * r0 + Vector3.up * y0, d * r1 + Vector3.up * y0, d * r0 + Vector3.up * y1, d * r1 + Vector3.up * y1 };
            var uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            var tri = Vector3.Dot(Vector3.Cross(v[2] - v[0], v[1] - v[0]), n) > 0 ? new[] { 0, 2, 1, 1, 2, 3 } : new[] { 0, 1, 2, 1, 3, 2 };
            return Build("Cap", v, new[] { n, n, n, n }, uv, tri);
        }

        /// `planes` quads crossing on the +Z axis from z=0 (width w0) to z=length (width w1). UV: u across, v along.
        /// Used with the double-sided additive material for light shafts, beams and projector cones.
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

        public static float LeafShape(float t) => Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Clamp01(t))), 0.72f) * (1f - 0.28f * t);

        /// Leaf blade along +Z (base at origin, up = +Y). fold lifts the edges (V section), curl droops the tip.
        /// UV: u across (0 edge → 0.5 midrib → 1 edge), v base → tip — matches LeafTexture().
        public static Mesh Leaf(float length, float width, float fold = 0.18f, float curl = 0.25f, int rows = 10, int cols = 3)
        {
            int cw = cols * 2 + 1; var v = new Vector3[(rows + 1) * cw]; var uv = new Vector2[v.Length]; var t = new int[rows * (cw - 1) * 6];
            for (int r = 0; r <= rows; r++)
            {
                float f = r / (float)rows, hw = width * 0.5f * LeafShape(f), z = f * length, y = -curl * length * f * f;
                for (int c = 0; c < cw; c++)
                {
                    float u = (c - cols) / (float)cols;
                    v[r * cw + c] = new Vector3(u * hw, y + fold * hw * Mathf.Abs(u), z);
                    uv[r * cw + c] = new Vector2(u * 0.5f + 0.5f, f);
                }
            }
            for (int r = 0, q = 0; r < rows; r++)
                for (int c = 0; c < cw - 1; c++)
                {
                    int p00 = r * cw + c, p10 = p00 + 1, p01 = p00 + cw, p11 = p01 + 1;
                    t[q++] = p00; t[q++] = p01; t[q++] = p10; t[q++] = p10; t[q++] = p01; t[q++] = p11;
                }
            return Build("Leaf", v, null, uv, t);
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
        static float Fbm(float x, float y) => Mathf.PerlinNoise(x, y) * 0.65f + Mathf.PerlinNoise(x * 2.3f + 17.1f, y * 2.3f + 5.3f) * 0.35f;
        /// Anti-aliased line coverage for distance d to a line of half-width hw, px = texel size (same units).
        static float Cov(float d, float hw, float px) => Mathf.Clamp01((hw - Mathf.Abs(d)) / px + 0.5f);
        static byte B(float f) => (byte)(Mathf.Clamp01(f) * 255f + 0.5f);

        static Texture2D Tex(int w, int h, Color32[] px, string name, TextureWrapMode wrap = TextureWrapMode.Clamp, int aniso = 2)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = name, wrapMode = wrap, filterMode = FilterMode.Trilinear, anisoLevel = aniso };
            t.SetPixels32(px); t.Apply(true, true); return t;
        }

        // ================================================================== canvas (vector strokes → alpha)
        public sealed class Canvas
        {
            public readonly int W, H; public readonly float[] A;
            public Canvas(int w, int h) { W = w; H = h; A = new float[w * h]; }

            public void Line(Vector2 p, Vector2 q, float w, float a = 1f)
            {
                float m = w * 0.5f + 1.5f;
                int x0 = Mathf.Max(0, (int)(Mathf.Min(p.x, q.x) - m)), x1 = Mathf.Min(W - 1, (int)(Mathf.Max(p.x, q.x) + m));
                int y0 = Mathf.Max(0, (int)(Mathf.Min(p.y, q.y) - m)), y1 = Mathf.Min(H - 1, (int)(Mathf.Max(p.y, q.y) + m));
                Vector2 d = q - p; float l2 = Mathf.Max(d.sqrMagnitude, 1e-6f);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        Vector2 c = new Vector2(x + 0.5f, y + 0.5f);
                        float t = Mathf.Clamp01(Vector2.Dot(c - p, d) / l2);
                        float cov = Mathf.Clamp01(w * 0.5f - (c - (p + d * t)).magnitude + 0.5f) * a;
                        int i = y * W + x; if (cov > A[i]) A[i] = cov;
                    }
            }

            public void Poly(IList<Vector2> pts, float w, float a = 1f, bool closed = false)
            {
                for (int i = 0; i < pts.Count - 1; i++) Line(pts[i], pts[i + 1], w, a);
                if (closed) Line(pts[pts.Count - 1], pts[0], w, a);
            }

            /// Ellipse outline (rx, ry) or arc (degrees), optionally filled with `fill` alpha.
            public void Ellipse(Vector2 c, float rx, float ry, float w, float a = 1f, float fill = 0f, float a0 = 0f, float a1 = 360f)
            {
                float m = w + 2;
                int x0 = Mathf.Max(0, (int)(c.x - rx - m)), x1 = Mathf.Min(W - 1, (int)(c.x + rx + m));
                int y0 = Mathf.Max(0, (int)(c.y - ry - m)), y1 = Mathf.Min(H - 1, (int)(c.y + ry + m));
                float rmin = Mathf.Min(rx, ry); bool arc = a1 - a0 < 359.9f;
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = (x + 0.5f - c.x) / rx, dy = (y + 0.5f - c.y) / ry, l = Mathf.Sqrt(dx * dx + dy * dy);
                        if (arc) { float ang = Mathf.Repeat(Mathf.Atan2(dy, dx) * Mathf.Rad2Deg, 360f); if (ang < a0 || ang > a1) continue; }
                        float d = (l - 1f) * rmin;
                        float cov = Mathf.Clamp01(w * 0.5f - Mathf.Abs(d) + 0.5f) * a;
                        if (fill > 0 && d < 0) cov = Mathf.Max(cov, fill * Mathf.Clamp01(-d + 0.5f));
                        int i = y * W + x; if (cov > A[i]) A[i] = cov;
                    }
            }

            public void Grid(float step, float a)
            {
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                        if (x % (int)step == 0 || y % (int)step == 0) { int i = y * W + x; if (a > A[i]) A[i] = a; }
            }

            /// White texture, alpha = coverage (for additive / transparent glows).
            public Texture2D ToAlpha(string name)
            {
                var px = new Color32[W * H];
                for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, B(A[i]));
                return Tex(W, H, px, name);
            }
        }

        // ================================================================== stroke font (capitals) for holographic text
        // ponytail: a 25-glyph vector font instead of TextMeshPro world text — enough for banners, panels and the floor motto.
        // 5×7 grid (x 0..4, y 0..6 up). Points "xy", strokes separated by '|'.
        static readonly Dictionary<char, string> Glyphs = new Dictionary<char, string>
        {
            ['A'] = "00 26 40|13 33", ['B'] = "00 06 36 45 44 33 03|33 42 41 30 00", ['C'] = "45 36 16 05 01 10 30 41",
            ['D'] = "00 06 36 45 41 30 00", ['E'] = "46 06 00 40|03 33", ['F'] = "46 06 00|03 33", ['G'] = "45 36 16 05 01 10 30 41 43 23",
            ['H'] = "00 06|40 46|03 43", ['I'] = "16 36|26 20|10 30", ['K'] = "00 06|46 02|14 40", ['L'] = "06 00 40",
            ['M'] = "00 06 23 46 40", ['N'] = "00 06 40 46", ['O'] = "16 36 45 41 30 10 01 05 16", ['P'] = "00 06 36 45 44 33 03",
            ['R'] = "00 06 36 45 44 33 03|23 40", ['S'] = "45 36 16 05 04 13 33 42 41 30 10 01", ['T'] = "06 46|26 20",
            ['U'] = "06 01 10 30 41 46", ['V'] = "06 20 46", ['W'] = "06 10 23 30 46", ['X'] = "06 40|00 46", ['Y'] = "06 23 46|23 20",
            ['2'] = "05 16 36 45 44 00 40",
        };

        /// Draw `s` with its baseline starting at `o`, glyph height h, advancing along `right`, glyph up = `up`.
        public static void Text(Canvas c, string s, Vector2 o, float h, float w, float a, Vector2 right, Vector2 up)
        {
            float k = h / 6f, adv = h * 0.78f; var pts = new List<Vector2>(12);
            for (int i = 0; i < s.Length; i++)
            {
                if (!Glyphs.TryGetValue(s[i], out var g)) continue;
                Vector2 b = o + right * (adv * i);
                foreach (var stroke in g.Split('|'))
                {
                    pts.Clear();
                    foreach (var p in stroke.Split(' ')) pts.Add(b + right * ((p[0] - '0') * k) + up * ((p[1] - '0') * k));
                    c.Poly(pts, w, a);
                }
            }
        }
        public static float TextWidth(string s, float h) => s.Length * h * 0.78f - h * 0.12f;
        static void TextCentered(Canvas c, string s, float cx, float y, float h, float w, float a) =>
            Text(c, s, new Vector2(cx - TextWidth(s, h) * 0.5f, y), h, w, a, Vector2.right, Vector2.up);

        /// Leaf outline + midrib + paired side veins between base b and tip.
        static void LeafArt(Canvas c, Vector2 b, Vector2 tip, float width, float w, float a, int veins = 7)
        {
            Vector2 ax = tip - b, side = new Vector2(-ax.y, ax.x).normalized;
            var L = new List<Vector2>(); var R = new List<Vector2>();
            for (int i = 0; i <= 48; i++) { float t = i / 48f, hw = width * 0.5f * LeafShape(t); L.Add(b + ax * t + side * hw); R.Add(b + ax * t - side * hw); }
            c.Poly(L, w, a); c.Poly(R, w, a);
            c.Line(b - ax * 0.12f, tip, w * 0.9f, a);
            for (int k = 1; k <= veins; k++)
            {
                float t = k / (veins + 1f) * 0.92f, t2 = Mathf.Min(0.98f, t + 0.15f), hw = width * 0.5f * LeafShape(t2) * 0.9f;
                c.Line(b + ax * t, b + ax * t2 + side * hw, w * 0.6f, a * 0.8f);
                c.Line(b + ax * t, b + ax * t2 - side * hw, w * 0.6f, a * 0.8f);
            }
        }

        // ================================================================== floor
        /// Polished stone floor albedo + matching engraving mask (white, alpha = groove) over the disc r ≤ FloorR (planar UV).
        /// Rings · radial leaf venation · 12 leaf motifs · microscopic cell walls · engraved motto toward the entrance.
        public static void Floor(int N, out Texture2D albedo, out Texture2D engrave)
        {
            float px = 2 * FloorR / N;
            // Motto along an arc in front of the platform (letters' tops point at the centre, readable from the entrance).
            var text = new Canvas(N, N);
            const string motto = "PLANTS MAKE A BRIGHTER TOMORROW";
            float th = 0.25f, rBase = 6.13f, span = TextWidth(motto, th) / rBase * Mathf.Rad2Deg;
            for (int i = 0; i < motto.Length; i++)
            {
                float adv = th * 0.78f, ang = (-90f - span * 0.5f + (i * adv + adv * 0.4f) / rBase * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                Vector2 right = new Vector2(-Mathf.Sin(ang), Mathf.Cos(ang)), up = -new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                Vector2 p = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rBase - right * (adv * 0.4f);
                Text(text, motto[i].ToString(), (p + Vector2.one * FloorR) / px, th / px, 1.7f, 1f, right, up);
            }

            var a = new Color32[N * N]; var e = new Color32[N * N];
            Color dark = new Color(0.085f, 0.11f, 0.10f), marble = new Color(0.82f, 0.80f, 0.75f), warm = new Color(0.86f, 0.81f, 0.72f);
            Color inlay = new Color(0.62f, 0.47f, 0.22f), goldBand = new Color(0.78f, 0.6f, 0.3f);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float wx = (x + 0.5f) * px - FloorR, wz = (y + 0.5f) * px - FloorR;
                    float r = Mathf.Sqrt(wx * wx + wz * wz), ang = Mathf.Atan2(wz, wx) * Mathf.Rad2Deg;
                    int i = y * N + x;
                    float g = Mathf.Max(Engraving(wx, wz, r, ang, px), text.A[i]);
                    float n = Fbm(wx * 0.33f + 40f, wz * 0.33f + 40f);
                    float vein = Mathf.Pow(1f - Mathf.Abs(Mathf.Sin(wx * 0.5f + wz * 0.21f + n * 7.5f)), 22f);
                    Color c;
                    if (r > 3.55f && r < 6.36f) c = dark * (0.85f + 0.3f * n) + new Color(0.05f, 0.06f, 0.055f) * vein;   // dark polished band
                    else if (r >= 3.33f && r <= 3.55f) c = goldBand * (0.9f + 0.15f * n);                                  // inlay under the gold ring
                    else c = Color.Lerp(marble, warm, Mathf.Clamp01((r - 9f) * 0.35f)) * (0.93f + 0.12f * n) - new Color(0.13f, 0.14f, 0.15f) * vein;
                    c *= 1f - 0.4f * Joints(r, ang, px);
                    c = Color.Lerp(c, inlay, 0.75f * g);
                    a[i] = new Color32(B(c.r), B(c.g), B(c.b), 255);
                    e[i] = new Color32(255, 255, 255, B(g));
                }
            albedo = Tex(N, N, a, "Chamber Floor", TextureWrapMode.Clamp, 8);
            engrave = Tex(N, N, e, "Chamber Engraving", TextureWrapMode.Clamp, 8);
        }

        static float Joints(float r, float ang, float px)
        {
            float j = Mathf.Max(Cov(r - 7.72f, 0.004f, px), Mathf.Max(Cov(r - 10.45f, 0.004f, px), Cov(r - 11.85f, 0.004f, px)));
            float step, off;
            if (r > 3.6f && r < 6.3f) { step = 22.5f; off = 11.25f; }
            else if (r > 6.45f && r < 9.0f) { step = 15f; off = 7.5f; }
            else if (r > 9.12f) { step = r > 10.45f ? 7.5f : 10f; off = 0f; }
            else return j;
            float d = (ang - off) - Mathf.Round((ang - off) / step) * step;
            return Mathf.Max(j, Cov(d * Mathf.Deg2Rad * r, 0.004f, px));
        }

        static float Engraving(float x, float z, float r, float ang, float px)
        {
            float g = Mathf.Max(Cov(r - 3.66f, 0.022f, px), Mathf.Max(Cov(r - 6.30f, 0.028f, px), Cov(r - 6.42f, 0.012f, px)));
            g = Mathf.Max(g, Mathf.Max(Cov(r - 9.0f, 0.028f, px), Cov(r - 9.12f, 0.012f, px)));
            if (r > 3.7f && r < 5.75f)
            {   // radial leaf venation: 24 midribs with herringbone side veins
                float k = Mathf.Round(ang / 15f), d = (ang - k * 15f) * Mathf.Deg2Rad;
                float along = r * Mathf.Cos(d), across = r * Mathf.Sin(d), ac = Mathf.Abs(across);
                float fade = Mathf.Clamp01((5.7f - r) * 3f);
                g = Mathf.Max(g, Cov(across, 0.016f, px) * fade);
                if (r < 3.82f) g = Mathf.Max(g, Cov((new Vector2(along - 3.82f, across)).magnitude - 0.06f, 0.01f, px));
                const float tan40 = 0.8391f, sin40 = 0.6428f, sp = 0.4f;
                float s = along - ac / tan40, sj = Mathf.Round((s - 3.95f) / sp) * sp + 3.95f, lim = 0.3f * r / 3.8f;
                if (ac < lim && sj > 3.9f && sj < 5.3f) g = Mathf.Max(g, Cov((s - sj) * sin40, 0.009f, px) * 0.8f * (1f - ac / lim) * fade);
            }
            if (r > 6.5f && r < 9.0f)
            {   // 8 radiating leaf motifs; the one facing the entrance is framed by a circle
                float k = Mathf.Round(ang / 45f), d = (ang - k * 45f) * Mathf.Deg2Rad;
                float along = r * Mathf.Cos(d), across = r * Mathf.Sin(d), ac = Mathf.Abs(across), t = (along - 6.7f) / 2.15f;
                if (t > -0.02f && t < 1.02f)
                {
                    float hw = 0.6f * LeafShape(t);
                    g = Mathf.Max(g, Cov(ac - hw, 0.015f, px) * 0.95f);
                    if (t < 0.96f) g = Mathf.Max(g, Cov(across, 0.01f, px) * 0.8f);
                    const float tan55 = 1.4281f, sin55 = 0.8192f, sp = 0.26f;
                    float s = along - ac / tan55, sj = Mathf.Round((s - 6.85f) / sp) * sp + 6.85f;
                    if (ac < hw * 0.88f && sj > 6.8f && sj < 8.6f) g = Mathf.Max(g, Cov((s - sj) * sin55, 0.008f, px) * 0.65f);
                }
                if (Mathf.Abs(Mathf.DeltaAngle(k * 45f, -90f)) < 1f) g = Mathf.Max(g, Cov(new Vector2(x, z + 7.78f).magnitude - 1.26f, 0.016f, px));
            }
            if (r > 9.2f && r < 12.35f)
            {   // microscopic cell walls (jittered-grid voronoi)
                const float cell = 0.62f;
                float gx = x / cell, gz = z / cell; int ix = Mathf.FloorToInt(gx), iz = Mathf.FloorToInt(gz);
                float d1 = 9f, d2 = 9f;
                for (int j = -1; j <= 1; j++)
                    for (int i = -1; i <= 1; i++)
                    {
                        float fx = ix + i + 0.15f + 0.7f * Hash(ix + i, iz + j), fz = iz + j + 0.15f + 0.7f * Hash(iz + j + 31, ix + i - 17);
                        float dd = (gx - fx) * (gx - fx) + (gz - fz) * (gz - fz);
                        if (dd < d1) { d2 = d1; d1 = dd; } else if (dd < d2) d2 = dd;
                    }
                float edge = (Mathf.Sqrt(d2) - Mathf.Sqrt(d1)) * 0.5f * cell;
                float fade = Mathf.Clamp01((r - 9.2f) * 3f) * Mathf.Clamp01((12.35f - r) * 2f);
                g = Mathf.Max(g, Cov(edge, 0.008f, px) * 0.4f * fade);
            }
            return g;
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

        /// Wood grain (u along the plank).
        public static Texture2D Wood()
        {
            const int W = 256, H = 64; var p = new Color32[W * H];
            Color a = new Color(0.36f, 0.21f, 0.11f), b = new Color(0.55f, 0.36f, 0.2f);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float n = TFbm(x / 64f, y / 8f, 4), ring = Mathf.Abs(Mathf.Sin((y / 4f + n * 6f) * 1.3f));
                    var c = Color.Lerp(a, b, ring * 0.7f + n * 0.3f);
                    p[y * W + x] = new Color32(B(c.r), B(c.g), B(c.b), 255);
                }
            return Tex(W, H, p, "Chamber Wood", TextureWrapMode.Repeat);
        }

        /// Light shaft profile: soft across (u), fades in above the floor and out toward the glass (v).
        /// beam: fades in over the first `fadeIn` of its length (v 0 = sky end).
        public static Texture2D Shaft(bool beam, float fadeIn = 1f / 1.4f)
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

        /// Leaf in leaf-mesh UV space (u across, v base → tip). holo: glowing veins + rim (additive); else green albedo.
        public static Texture2D LeafTexture(bool holo)
        {
            const int N = 256; var c = new Canvas(N, N);
            c.Line(new Vector2(N * 0.5f, 0), new Vector2(N * 0.5f, N), holo ? 3f : 4f, 1f);
            for (int k = 1; k <= 8; k++)
            {
                float y = N * (k / 9.5f);
                c.Line(new Vector2(N * 0.5f, y), new Vector2(N * 0.04f, y + N * 0.16f), 2.2f, 0.85f);
                c.Line(new Vector2(N * 0.5f, y), new Vector2(N * 0.96f, y + N * 0.16f), 2.2f, 0.85f);
            }
            var px = new Color32[N * N];
            Color dark = new Color(0.07f, 0.24f, 0.07f), mid = new Color(0.16f, 0.42f, 0.12f), vein = new Color(0.52f, 0.7f, 0.3f);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int i = y * N + x; float u = Mathf.Abs(x / (N - 1f) - 0.5f) * 2f;
                    if (holo)
                    {
                        float rim = Mathf.Clamp01((u - 0.82f) / 0.18f);
                        px[i] = new Color32(255, 255, 255, B(Mathf.Max(0.12f + 0.1f * (1 - u), Mathf.Max(c.A[i], rim))));
                    }
                    else
                    {
                        var col = Color.Lerp(mid, dark, u * u) * (0.9f + 0.2f * Hash(x / 3, y / 3));
                        col = Color.Lerp(col, vein, c.A[i] * 0.75f);
                        px[i] = new Color32(B(col.r), B(col.g), B(col.b), 255);
                    }
                }
            return Tex(N, N, px, holo ? "Holo Leaf Blade" : "Leaf Blade");
        }

        public enum Holo { Leaf, Cell, Graph }

        /// Cyan hologram panel art (white strokes, alpha = glow). Tinted by the material colour.
        public static Texture2D Hologram(Holo kind)
        {
            const int N = 256; var c = new Canvas(N, N);
            c.Grid(16, 0.07f);
            // corner brackets + frame ticks
            void Bracket(Vector2 p, Vector2 dx, Vector2 dy) { c.Line(p, p + dx * 26, 2.2f); c.Line(p, p + dy * 26, 2.2f); }
            Bracket(new Vector2(6, 6), Vector2.right, Vector2.up); Bracket(new Vector2(250, 6), Vector2.left, Vector2.up);
            Bracket(new Vector2(6, 250), Vector2.right, Vector2.down); Bracket(new Vector2(250, 250), Vector2.left, Vector2.down);
            switch (kind)
            {
                case Holo.Leaf:
                    c.Ellipse(new Vector2(128, 124), 96, 96, 1.4f, 0.55f, 0, 10, 80); c.Ellipse(new Vector2(128, 124), 96, 96, 1.4f, 0.55f, 0, 100, 170);
                    c.Ellipse(new Vector2(128, 124), 96, 96, 1.4f, 0.55f, 0, 190, 350);
                    for (int k = 0; k < 36; k++) { float a = k * 10 * Mathf.Deg2Rad; var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); c.Line(new Vector2(128, 124) + d * 102, new Vector2(128, 124) + d * (k % 3 == 0 ? 112 : 107), 1.3f, 0.7f); }
                    LeafArt(c, new Vector2(128, 40), new Vector2(128, 214), 104, 2.6f, 1f, 7);
                    for (int k = 0; k < 4; k++) c.Line(new Vector2(14, 200 - k * 12), new Vector2(14 + 18 + Hash01(k) * 26, 200 - k * 12), 3f, 0.6f);
                    TextCentered(c, "LEAF", 128, 226, 14, 1.6f, 0.9f);
                    break;
                case Holo.Cell:
                    {
                        var wall = new List<Vector2>(); var mem = new List<Vector2>();
                        for (int i = 0; i <= 64; i++)
                        {   // rounded-rectangle plant cell (superellipse)
                            float a = i / 64f * Mathf.PI * 2, cs = Mathf.Cos(a), sn = Mathf.Sin(a);
                            float ex = Mathf.Sign(cs) * Mathf.Pow(Mathf.Abs(cs), 0.4f), ey = Mathf.Sign(sn) * Mathf.Pow(Mathf.Abs(sn), 0.4f);
                            wall.Add(new Vector2(128 + ex * 100, 124 + ey * 76)); mem.Add(new Vector2(128 + ex * 92, 124 + ey * 68));
                        }
                        c.Poly(wall, 3f); c.Poly(mem, 1.4f, 0.7f);
                        c.Ellipse(new Vector2(110, 118), 46, 30, 1.6f, 0.6f, 0.12f);                          // vacuole
                        c.Ellipse(new Vector2(176, 150), 20, 20, 2.2f, 1f, 0.25f); c.Ellipse(new Vector2(176, 150), 6, 6, 1f, 1f, 0.8f); // nucleus
                        for (int k = 0; k < 7; k++)
                        {
                            float a = k / 7f * Mathf.PI * 2 + 0.3f; var p = new Vector2(128 + Mathf.Cos(a) * 78, 124 + Mathf.Sin(a) * 52);
                            c.Ellipse(p, 11, 6.5f, 1.4f, 1f, 0.35f);                                       // chloroplasts
                            c.Line(p - new Vector2(6, 0), p + new Vector2(6, 0), 1f, 0.8f);
                        }
                        TextCentered(c, "PLANT CELL", 128, 216, 13, 1.5f, 0.9f);
                    }
                    break;
                default:
                    c.Line(new Vector2(30, 40), new Vector2(30, 200), 2f); c.Line(new Vector2(30, 40), new Vector2(226, 40), 2f);
                    var curve = new List<Vector2>();
                    for (int i = 0; i <= 40; i++) { float t = i / 40f; curve.Add(new Vector2(30 + t * 190, 48 + 140 / (1 + Mathf.Exp(-(t - 0.45f) * 9)))); }
                    c.Poly(curve, 2.6f);
                    for (int i = 0; i < 6; i++) { float h = 20 + Hash01(i + 9) * 60; c.Line(new Vector2(48 + i * 30, 42), new Vector2(48 + i * 30, 42 + h), 9f, 0.28f); }
                    for (int i = 0; i <= 40; i += 8) c.Ellipse(curve[i], 4, 4, 1.4f, 1f, 0.8f);
                    TextCentered(c, "O2 OUTPUT", 128, 216, 13, 1.5f, 0.9f);
                    break;
            }
            return c.ToAlpha("Hologram " + kind);
        }

        /// Tall banner art: stacked words at the top, a large glowing leaf below, footer bars.
        public static Texture2D Banner(string[] words)
        {
            const int W = 288, H = 832; var c = new Canvas(W, H);
            float y = H - 70;
            foreach (var w in words) { TextCentered(c, w, W * 0.5f, y, Mathf.Min(30f, (W - 36f) / (w.Length * 0.78f)), 2.6f, 1f); y -= 52; }
            c.Line(new Vector2(40, y + 18), new Vector2(W - 40, y + 18), 1.4f, 0.6f);
            LeafArt(c, new Vector2(W * 0.5f, 90), new Vector2(W * 0.5f, Mathf.Min(y - 20, 560)), 170, 3f, 1f, 8);
            c.Ellipse(new Vector2(W * 0.5f, 90), 10, 10, 2f, 1f, 0.6f);
            for (int k = 0; k < 3; k++) c.Line(new Vector2(60, 34 + k * 10), new Vector2(120 + k * 40, 34 + k * 10), 3f, 0.45f);
            return c.ToAlpha("Banner");
        }

        /// Silhouette ring for the scenery outside the glass (u around 0..1 periodic, v up). rgb = haze gradient.
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
