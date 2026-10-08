using System.Collections.Generic;
using UnityEngine;

namespace Photosynthesis
{
    /// Alocasia / elephant-ear blade: sagittate outline with two rounded basal lobes, a drooping arc-length midline and a
    /// tented, slightly wavy cross-section. Normalised coordinates: s along the midrib (−Lb … 1; 0 = petiole junction at
    /// the sinus), x lateral (|x| ≤ ~0.47). The midline is arc-length parameterised, so midrib distance = s · L metres.
    /// The vein texture and the vein geometry (vessels, energy wave, glucose paths) share the same vein formulas.
    public class PlantLeafShape
    {
        public const float W = 0.46f, Lb = 0.30f;
        public const float VeinS0 = 0.07f, VeinDS = 0.105f, VeinA = 0.45f, VeinB = 0.9f;
        public const int Veins = 8;          // pinnate lateral pairs; ids 8 = basal vein, 9–10 = basal secondaries
        public const int VeinIds = 11;

        public readonly float L;
        readonly float p0, p1;
        const int N = 160;
        readonly Vector2[] mid = new Vector2[N + 1]; // (y, z) of the normalised midline

        /// phi = downward pitch of the midrib (deg): phi0 at the junction, phi1 at the tip (negative = pointing up).
        public PlantLeafShape(float length, float phi0Deg, float phi1Deg)
        {
            L = length; p0 = phi0Deg * Mathf.Deg2Rad; p1 = phi1Deg * Mathf.Deg2Rad;
            float ds = (1 + Lb) / N; var c = Vector2.zero; mid[0] = c;
            for (int i = 0; i < N; i++) { float f = Phi(-Lb + (i + 0.5f) * ds); c += new Vector2(-Mathf.Sin(f), Mathf.Cos(f)) * ds; mid[i + 1] = c; }
            var zero = MidRaw(0); for (int i = 0; i <= N; i++) mid[i] -= zero;
        }

        float Phi(float s) => s >= 0 ? p0 + (p1 - p0) * Mathf.Pow(Mathf.Min(s, 1f), 1.2f)
                                     : Mathf.Max(p0 - 50f * Mathf.Deg2Rad * Mathf.Pow(Mathf.Min(-s / Lb, 1f), 1.1f), -95f * Mathf.Deg2Rad);
        Vector2 MidRaw(float s) { float f = Mathf.Clamp((s + Lb) / (1 + Lb) * N, 0, N); int i = Mathf.Min((int)f, N - 1); return Vector2.Lerp(mid[i], mid[i + 1], f - i); }
        Vector3 Nrm0(float s) { float f = Phi(s); return new Vector3(0, Mathf.Cos(f), Mathf.Sin(f)); }

        public static float Xout(float s) { if (s >= 0) { float t = Mathf.Min(s, 1f); return W * Mathf.Pow(1 - t, 0.8f) * (1 + 0.55f * t); } float q = Mathf.Min(-s / Lb, 1f); return Cx(q) + Hw(q); }
        public static float Xin(float s) { if (s >= 0) return 0; float q = Mathf.Min(-s / Lb, 1f); return Cx(q) - Hw(q); }
        static float Cx(float q) => Mathf.Lerp(0.5f, 0.62f, q) * W;
        static float Hw(float q) => 0.5f * W * Mathf.Sqrt(Mathf.Max(0, 1 - Mathf.Pow(q, 2.5f)));

        /// Blade-local position in metres (x signed, normalised).
        public Vector3 Pos(float s, float x)
        {
            float ax = Mathf.Abs(x), r = ax / W;
            float h = -0.17f * W * Mathf.Pow(r, 1.7f) + 0.010f * Mathf.Sin(s * 19f + ax * 11f) * r * r; // tented + wavy margin
            var m = MidRaw(s);
            return (new Vector3(x, m.x, m.y) + Nrm0(s) * h) * L;
        }

        /// Upper-surface normal (blade-local).
        public Vector3 Normal(float s, float x)
        {
            const float e = 0.003f;
            return Vector3.Cross(Pos(s + e, x) - Pos(s - e, x), Pos(s, x + e) - Pos(s, x - e)).normalized;
        }

        // ---------------------------------------------------------------- mesh
        /// Blade: sub 0 upper surface, sub 1 lower surface (5 mm apart, so both sides light correctly and survive
        /// back-face culling on the transparent material), sub 2 midrib.
        public Mesh Build()
        {
            var b = new PlantMesh();
            for (int side = -1; side <= 1; side += 2)
            {
                int sd = side;
                float S(float u) => -Lb + (1 + Lb) * u;
                float X(float u, float w) { float s = S(u); return sd * Mathf.Lerp(Xin(s), Xout(s), w); }
                b.Surface((u, w) => Pos(S(u), X(u, w)), (u, w) => Normal(S(u), X(u, w)),
                          (u, w) => new Vector2(0.5f + X(u, w), u), 34, 10, 0, 0.005f, 1);
            }
            var mr = new List<Vector3>(16);
            for (int i = 0; i < 16; i++) { float s = Mathf.Lerp(-0.02f, 0.93f, i / 15f); mr.Add(Pos(s, 0) - Normal(s, 0) * 0.002f); }
            float l = L;
            b.Tube(mr, t => l * (0.012f * Mathf.Pow(1 - t, 1.2f) + 0.0025f), 6, 2, 2f);
            return b.Build("Blade");
        }

        // ---------------------------------------------------------------- veins
        static List<Vector2>[] basal;    // (ax, s) polylines
        static List<Vector2>[] Basal3()
        {
            if (basal != null) return basal;
            basal = new List<Vector2>[3];
            var main = new List<Vector2>();
            for (int i = 0; i <= 10; i++) { float q = i / 10f * 0.92f; main.Add(new Vector2(Cx(1f) * 0.95f * q + 0.04f * W * Mathf.Sin(Mathf.PI * q / 0.92f), -Lb * q)); }
            basal[0] = main;
            for (int j = 0; j < 2; j++)
            {
                var p = Along2(main, j == 0 ? 0.35f : 0.68f); var line = new List<Vector2> { p };
                var dir = new Vector2(1f, 0.25f).normalized;
                for (int k = 0; k < 200; k++) { p += dir * 0.004f; if (p.x >= 0.86f * Xout(p.y)) break; if (k % 6 == 5) line.Add(p); }
                line.Add(p); basal[j + 1] = line;
            }
            return basal;
        }

        static Vector2 Along2(List<Vector2> p, float t)
        {
            float total = 0; for (int i = 1; i < p.Count; i++) total += Vector2.Distance(p[i - 1], p[i]);
            float d = Mathf.Clamp01(t) * total;
            for (int i = 1; i < p.Count; i++) { float s = Vector2.Distance(p[i - 1], p[i]); if (d <= s || i == p.Count - 1) return Vector2.Lerp(p[i - 1], p[i], s > 0 ? Mathf.Clamp01(d / s) : 0); d -= s; }
            return p[p.Count - 1];
        }

        /// Vein `id` (0–7 pinnate laterals, 8 basal, 9–10 basal secondaries) on side ±1 as normalised (s, x) points,
        /// starting where it leaves its parent vein.
        public static List<Vector2> Vein(int id, int side, int count = 9)
        {
            var pts = new List<Vector2>(count);
            if (id < Veins)
            {
                float sk = VeinS0 + id * VeinDS, end = 0;
                for (float ax = 0; ax < 0.6f; ax += 0.004f) { float s = sk + VeinA * ax + VeinB * ax * ax; if (s >= 0.99f || ax >= 0.86f * Xout(s)) break; end = ax; }
                for (int i = 0; i < count; i++) { float ax = end * i / (count - 1f); pts.Add(new Vector2(sk + VeinA * ax + VeinB * ax * ax, ax * side)); }
            }
            else
            {
                var line = Basal3()[id - Veins];
                for (int i = 0; i < count; i++) { var p = Along2(line, i / (count - 1f)); pts.Add(new Vector2(p.y, p.x * side)); }
            }
            return pts;
        }

        /// Normalised network distance (× L = metres from the junction along the midrib/basal vein) where vein `id` starts.
        public static float VeinStart(int id)
        {
            if (id < Veins) return VeinS0 + id * VeinDS;
            if (id == Veins) return 0;
            var m = Basal3()[0]; float total = 0; for (int i = 1; i < m.Count; i++) total += Vector2.Distance(m[i - 1], m[i]);
            return total * (id == Veins + 1 ? 0.35f : 0.68f);
        }

        /// Blade margin as a closed loop of normalised (s, x) points.
        public static List<Vector2> Outline()
        {
            var o = new List<Vector2>();
            const int A = 48, B = 12;
            for (int i = 0; i <= A; i++) { float s = 1f - (1 + Lb) * Mathf.Sin(i / (float)A * Mathf.PI * 0.5f); o.Add(new Vector2(s, Xout(s))); }
            for (int i = 1; i <= B; i++) { float s = -Lb + Lb * (1 - Mathf.Cos(i / (float)B * Mathf.PI * 0.5f)); o.Add(new Vector2(s, Xin(s))); }
            for (int i = o.Count - 2; i >= 0; i--) o.Add(new Vector2(o[i].x, -o[i].y));
            return o;
        }

        // ---------------------------------------------------------------- texture
        /// Colour + vein map in blade UV space (u = 0.5 + x, v = (s + Lb) / (1 + Lb)): midrib, 8 lateral pairs, basal
        /// veins, intramarginal vein, fine reticulation, mottling and darkened margins. Top = glossy deep green, bottom = pale.
        public static Texture2D BladeTexture(int res, bool top)
        {
            Color dark = top ? new Color(0.030f, 0.150f, 0.055f) : new Color(0.20f, 0.33f, 0.18f);
            Color light = top ? new Color(0.090f, 0.330f, 0.120f) : new Color(0.33f, 0.48f, 0.29f);
            Color vein = top ? new Color(0.30f, 0.56f, 0.22f) : new Color(0.62f, 0.72f, 0.48f);
            Color midc = top ? new Color(0.66f, 0.82f, 0.45f) : new Color(0.80f, 0.86f, 0.64f);
            var px = new Color32[res * res];
            for (int y = 0; y < res; y++)
            {
                float v = (y + 0.5f) / res, s = v * (1 + Lb) - Lb, xo = Xout(s), xi = Xin(s);
                for (int x = 0; x < res; x++)
                {
                    float u = (x + 0.5f) / res, ax = Mathf.Abs(u - 0.5f);
                    float n1 = Mathf.PerlinNoise(u * 6f + 3.1f, v * 6f + 7.7f), n2 = Mathf.PerlinNoise(u * 46f + 1.3f, v * 46f + 9.1f), n3 = Mathf.PerlinNoise(u * 130f, v * 130f);
                    var c = Color.Lerp(dark, light, Mathf.Clamp01(0.42f + (n1 - 0.5f) * 0.8f + 0.30f * Mathf.Exp(-ax / 0.05f) + (n3 - 0.5f) * 0.14f));
                    c = Color.Lerp(c, vein, (1 - PlantMesh.Sstep(0f, 0.045f, Mathf.Abs(n2 - 0.5f))) * (top ? 0.14f : 0.24f)); // reticulate veinlets
                    float veins = Mathf.Max(Lateral(s, ax, xo), BasalMask(s, ax));
                    if (s > 0.02f && s < 0.97f) veins = Mathf.Max(veins, 0.45f * (1 - PlantMesh.Sstep(0.0008f, 0.0024f, Mathf.Abs(ax - 0.93f * xo))));
                    c = Color.Lerp(c, vein, veins * 0.9f);
                    c = Color.Lerp(c, midc, Midrib(s, ax));
                    float ed = xo - ax; if (s < 0) ed = Mathf.Min(ed, ax - xi);
                    c *= Mathf.Lerp(0.55f, 1f, PlantMesh.Sstep(0f, 0.03f, ed));
                    c.a = 1;
                    px[y * res + x] = c;
                }
            }
            var t = new Texture2D(res, res, TextureFormat.RGBA32, true) { name = top ? "LeafTop" : "LeafUnder", wrapMode = TextureWrapMode.Clamp, anisoLevel = 8 };
            t.SetPixels32(px); t.Apply(true);
            return t;
        }

        static float Midrib(float s, float ax)
        {
            if (s < -0.01f) return 0;
            float w = 0.011f * (1 - 0.85f * Mathf.Clamp01(s)) + 0.0015f;
            return (1 - PlantMesh.Sstep(w * 0.55f, w, ax)) * (1 - PlantMesh.Sstep(0.97f, 1f, s));
        }

        static float Lateral(float s, float ax, float xo)
        {
            if (s < 0 || xo <= 1e-3f) return 0;
            float q = s - VeinA * ax - VeinB * ax * ax;
            int k = Mathf.RoundToInt((q - VeinS0) / VeinDS); if (k < 0 || k >= Veins) return 0;
            float slope = VeinA + 2 * VeinB * ax, d = Mathf.Abs(q - (VeinS0 + k * VeinDS)) / Mathf.Sqrt(1 + slope * slope);
            float rel = ax / xo, w = 0.0042f * (1 - 0.55f * Mathf.Min(rel, 1f)) + 0.0010f;
            return (1 - PlantMesh.Sstep(w * 0.4f, w, d)) * (1 - PlantMesh.Sstep(0.80f, 0.93f, rel));
        }

        static float BasalMask(float s, float ax)
        {
            if (s > 0.12f) return 0;
            var p = new Vector2(ax, s); float best = 0; var lines = Basal3();
            for (int l = 0; l < lines.Length; l++)
            {
                var line = lines[l]; float w = l == 0 ? 0.0055f : 0.0035f, d = 9f;
                for (int i = 1; i < line.Count; i++) d = Mathf.Min(d, SegDist(p, line[i - 1], line[i]));
                best = Mathf.Max(best, 1 - PlantMesh.Sstep(w * 0.4f, w, d));
            }
            return best;
        }

        static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-9f));
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
