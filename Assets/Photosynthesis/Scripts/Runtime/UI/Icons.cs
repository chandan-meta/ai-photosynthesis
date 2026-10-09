using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Photosynthesis
{
    /// Tiny SVG geometry reader for the Figma icon/art data: path commands M L H V C S A Z (absolute + relative)
    /// plus shapes "O cx cy r" (circle), "E cx cy rx ry", "R x y w h rx", "D cx cy r" (filled dot). Elements are ';'-separated.
    /// Curves are flattened once and cached; drawing scales the polylines into the element.
    static class Vec
    {
        public struct Sub { public Vector2[] pts; public bool closed, fill; }
        static readonly Dictionary<string, Sub[]> cache = new Dictionary<string, Sub[]>();
        const float K = 0.5522848f;

        public static Sub[] Get(string spec)
        {
            if (cache.TryGetValue(spec, out var s)) return s;
            var list = new List<Sub>();
            foreach (var raw in spec.Split(';'))
            {
                var e = raw.Trim(); if (e.Length == 0) continue;
                if (e[0] == 'M') { Path(e, list); continue; }
                var n = Nums(e.Substring(1));
                switch (e[0])
                {
                    case 'O': list.Add(Ellipse(n[0], n[1], n[2], n[2], false)); break;
                    case 'D': list.Add(Ellipse(n[0], n[1], n[2], n[2], true)); break;
                    case 'E': list.Add(Ellipse(n[0], n[1], n[2], n[3], false)); break;
                    case 'R': list.Add(RRect(n[0], n[1], n[2], n[3], n[4])); break;
                }
            }
            return cache[spec] = list.ToArray();
        }

        static float[] Nums(string s)
        {
            var o = new List<float>(); int i = 0;
            while (true) { Sep(s, ref i); if (i >= s.Length) break; o.Add(Num(s, ref i)); }
            return o.ToArray();
        }

        static Sub Ellipse(float cx, float cy, float rx, float ry, bool fill)
        {
            var p = new List<Vector2>();
            Vector2 c = new Vector2(cx, cy);
            for (int q = 0; q < 4; q++)
            {
                float a0 = q * Mathf.PI / 2, a1 = a0 + Mathf.PI / 2;
                Vector2 d0 = new Vector2(Mathf.Cos(a0) * rx, Mathf.Sin(a0) * ry), d1 = new Vector2(Mathf.Cos(a1) * rx, Mathf.Sin(a1) * ry);
                Vector2 t0 = new Vector2(-Mathf.Sin(a0) * rx, Mathf.Cos(a0) * ry), t1 = new Vector2(-Mathf.Sin(a1) * rx, Mathf.Cos(a1) * ry);
                if (q == 0) p.Add(c + d0);
                Cubic(p, c + d0, c + d0 + t0 * K, c + d1 - t1 * K, c + d1);
            }
            p.RemoveAt(p.Count - 1);
            return new Sub { pts = p.ToArray(), closed = true, fill = fill };
        }

        static Sub RRect(float x, float y, float w, float h, float r)
        {
            r = Mathf.Min(r, w / 2, h / 2);
            var p = new List<Vector2>();
            void Corner(float cx, float cy, float a0)
            {
                for (int i = 0; i <= 6; i++) { float a = (a0 + i * 15f) * Mathf.Deg2Rad; p.Add(new Vector2(cx + Mathf.Cos(a) * r, cy + Mathf.Sin(a) * r)); }
            }
            Corner(x + w - r, y + r, -90); Corner(x + w - r, y + h - r, 0); Corner(x + r, y + h - r, 90); Corner(x + r, y + r, 180);
            return new Sub { pts = p.ToArray(), closed = true };
        }

        static void Cubic(List<Vector2> o, Vector2 p0, Vector2 c1, Vector2 c2, Vector2 p3)
        {
            float len = Vector2.Distance(p0, c1) + Vector2.Distance(c1, c2) + Vector2.Distance(c2, p3);
            int n = Mathf.Clamp(Mathf.CeilToInt(len * 1.2f), 4, 28);
            for (int i = 1; i <= n; i++)
            {
                float t = i / (float)n, u = 1 - t;
                o.Add(u * u * u * p0 + 3 * u * u * t * c1 + 3 * u * t * t * c2 + t * t * t * p3);
            }
        }

        // SVG endpoint arc (x-axis rotation is always 0 in our data) → polyline.
        static void Arc(List<Vector2> o, Vector2 a, float rx, float ry, bool large, bool sweep, Vector2 b)
        {
            if (rx <= 0 || ry <= 0 || a == b) { o.Add(b); return; }
            Vector2 h = (a - b) * 0.5f;
            float lam = h.x * h.x / (rx * rx) + h.y * h.y / (ry * ry);
            if (lam > 1) { float k = Mathf.Sqrt(lam); rx *= k; ry *= k; }
            float num = rx * rx * ry * ry - rx * rx * h.y * h.y - ry * ry * h.x * h.x, den = rx * rx * h.y * h.y + ry * ry * h.x * h.x;
            float coef = Mathf.Sqrt(Mathf.Max(0, num / Mathf.Max(den, 1e-6f))) * (large == sweep ? -1 : 1);
            Vector2 cp = new Vector2(coef * rx * h.y / ry, -coef * ry * h.x / rx), c = cp + (a + b) * 0.5f;
            float t1 = Mathf.Atan2((h.y - cp.y) / ry, (h.x - cp.x) / rx);
            float dt = Mathf.Atan2((-h.y - cp.y) / ry, (-h.x - cp.x) / rx) - t1;
            if (!sweep && dt > 0) dt -= Mathf.PI * 2; else if (sweep && dt < 0) dt += Mathf.PI * 2;
            int n = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(dt) / (10f * Mathf.Deg2Rad)));
            for (int i = 1; i < n; i++) { float t = t1 + dt * i / n; o.Add(new Vector2(c.x + rx * Mathf.Cos(t), c.y + ry * Mathf.Sin(t))); }
            o.Add(b);
        }

        static void Path(string d, List<Sub> subs)
        {
            var cur = new List<Vector2>(); Vector2 p = default, start = default, lastC = default; char cmd = 'M', prev = ' ';
            int i = 0;
            void Flush(bool closed) { if (cur.Count > 1) subs.Add(new Sub { pts = cur.ToArray(), closed = closed }); cur.Clear(); }
            Vector2 Pt(bool rel) { float x = Num(d, ref i), y = Num(d, ref i); return rel ? p + new Vector2(x, y) : new Vector2(x, y); }
            while (true)
            {
                Sep(d, ref i); if (i >= d.Length) break;
                if (char.IsLetter(d[i])) cmd = d[i++];
                else if (cmd == 'Z' || cmd == 'z') break; // malformed: numbers after Z
                bool rel = char.IsLower(cmd);
                if (cmd != 'M' && cmd != 'm' && cur.Count == 0) cur.Add(p);
                switch (char.ToUpperInvariant(cmd))
                {
                    case 'M': Flush(false); p = start = Pt(rel); cur.Add(p); cmd = rel ? 'l' : 'L'; break;
                    case 'L': p = Pt(rel); cur.Add(p); break;
                    case 'H': { float x = Num(d, ref i); p = new Vector2(rel ? p.x + x : x, p.y); cur.Add(p); break; }
                    case 'V': { float y = Num(d, ref i); p = new Vector2(p.x, rel ? p.y + y : y); cur.Add(p); break; }
                    case 'C': { var c1 = Pt(rel); var c2 = Pt(rel); var e = Pt(rel); Cubic(cur, p, c1, c2, e); lastC = c2; p = e; break; }
                    case 'S':
                    {
                        var c1 = prev == 'C' || prev == 'S' ? 2 * p - lastC : p;
                        var c2 = Pt(rel); var e = Pt(rel); Cubic(cur, p, c1, c2, e); lastC = c2; p = e; break;
                    }
                    case 'A':
                    {
                        float rx = Num(d, ref i), ry = Num(d, ref i); Num(d, ref i);
                        bool large = Flag(d, ref i), sweep = Flag(d, ref i); var e = Pt(rel);
                        Arc(cur, p, rx, ry, large, sweep, e); p = e; break;
                    }
                    case 'Z': Flush(true); p = start; break;
                }
                prev = char.ToUpperInvariant(cmd);
            }
            Flush(false);
        }

        static void Sep(string s, ref int i) { while (i < s.Length && (s[i] == ' ' || s[i] == ',' || s[i] == '\n' || s[i] == '\t')) i++; }
        static bool Flag(string s, ref int i) { Sep(s, ref i); return s[i++] == '1'; }
        static float Num(string s, ref int i)
        {
            Sep(s, ref i);
            int st = i; if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            bool dot = false;
            while (i < s.Length && (char.IsDigit(s[i]) || (s[i] == '.' && !dot))) { if (s[i] == '.') dot = true; i++; }
            return float.Parse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------ drawing
        static Vector2 X(Vector2 v, float s, Vector2 o) => v * s + o;

        public static void Stroke(Painter2D p, string spec, float s, Vector2 o, Color c, float width)
        {
            var subs = Get(spec);
            p.strokeColor = c; p.lineWidth = width; p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
            p.BeginPath();
            foreach (var sub in subs)
            {
                if (sub.fill) continue;
                p.MoveTo(X(sub.pts[0], s, o));
                for (int i = 1; i < sub.pts.Length; i++) p.LineTo(X(sub.pts[i], s, o));
                if (sub.closed) p.ClosePath();
            }
            p.Stroke();
            Fill(p, spec, s, o, c, true);
        }

        /// Fills every sub-path (or only the "D" dots when dotsOnly).
        public static void Fill(Painter2D p, string spec, float s, Vector2 o, Color c, bool dotsOnly = false)
        {
            p.fillColor = c;
            foreach (var sub in Get(spec))
            {
                if (dotsOnly && !sub.fill) continue;
                p.BeginPath(); p.MoveTo(X(sub.pts[0], s, o));
                for (int i = 1; i < sub.pts.Length; i++) p.LineTo(X(sub.pts[i], s, o));
                p.ClosePath(); p.Fill();
            }
        }

        /// Linear gradient fill of a convex shape: a fan from its centroid with per-vertex colours (exact for 2 stops).
        /// Painted over an antialiased base fill and pulled in by half a pixel so the aliased mesh edge never shows.
        public static void LinearFill(MeshGenerationContext mgc, string spec, float s, Vector2 o, Vector2 a, Vector2 b, Color ca, Color cb)
        {
            var pts = Get(spec)[0].pts;
            Fill(mgc.painter2D, spec, s, o, Color.Lerp(ca, cb, 0.5f));
            Vector2 cen = Vector2.zero; foreach (var v in pts) cen += v; cen /= pts.Length;
            Vector2 ab = b - a; float inv = 1f / Mathf.Max(ab.sqrMagnitude, 1e-5f);
            Color Col(Vector2 v) => Color.Lerp(ca, cb, Mathf.Clamp01(Vector2.Dot(v - a, ab) * inv));
            int n = pts.Length;
            var mwd = mgc.Allocate(n + 1, n * 3);
            mwd.SetNextVertex(new Vertex { position = new Vector3(cen.x * s + o.x, cen.y * s + o.y, Vertex.nearZ), tint = Col(cen) });
            float shrink = 0.6f / s;
            Vector2[] screen = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                var v = pts[i]; var dir = cen - v; float l = dir.magnitude; if (l > shrink) v += dir / l * shrink;
                screen[i] = X(v, s, o);
                mwd.SetNextVertex(new Vertex { position = new Vector3(screen[i].x, screen[i].y, Vertex.nearZ), tint = Col(pts[i]) });
            }
            Vector2 c0 = X(cen, s, o);
            for (int i = 0; i < n; i++) Tri(mwd, c0, screen[i], screen[(i + 1) % n], 0, i + 1, (i + 1) % n + 1);
        }

        /// Radial gradient disc (SVG focal gradient look): concentric rings whose centres slide from `focus` to `c`.
        public static void RadialDisc(MeshGenerationContext mgc, Vector2 c, float r, Vector2 focus, float s, Vector2 o, params Color[] stops)
        {
            const int m = 40;
            int rings = stops.Length - 1;
            if (stops[stops.Length - 1].a > 0.01f) { var p = mgc.painter2D; p.fillColor = stops[stops.Length - 1]; p.BeginPath(); p.Arc(X(c, s, o), r * s, Angle.Degrees(0), Angle.Degrees(360)); p.Fill(); }
            var mwd = mgc.Allocate(1 + rings * m, m * 3 + (rings - 1) * m * 6);
            Vector2 P(int ring, int k)
            {
                float f = ring / (float)rings, rr = f * r - (ring == rings ? 0.5f / s : 0);
                Vector2 cc = Vector2.Lerp(focus, c, f); float a = k * Mathf.PI * 2 / m;
                return X(cc + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr, s, o);
            }
            var f0 = X(focus, s, o);
            mwd.SetNextVertex(new Vertex { position = new Vector3(f0.x, f0.y, Vertex.nearZ), tint = stops[0] });
            for (int ring = 1; ring <= rings; ring++)
                for (int k = 0; k < m; k++) { var v = P(ring, k); mwd.SetNextVertex(new Vertex { position = new Vector3(v.x, v.y, Vertex.nearZ), tint = stops[ring] }); }
            for (int k = 0; k < m; k++) Tri(mwd, f0, P(1, k), P(1, (k + 1) % m), 0, 1 + k, 1 + (k + 1) % m);
            for (int ring = 1; ring < rings; ring++)
                for (int k = 0; k < m; k++)
                {
                    int k1 = (k + 1) % m, a = 1 + (ring - 1) * m + k, b = 1 + (ring - 1) * m + k1, c2 = 1 + ring * m + k, d = 1 + ring * m + k1;
                    Tri(mwd, P(ring, k), P(ring + 1, k), P(ring + 1, k1), a, c2, d);
                    Tri(mwd, P(ring, k), P(ring + 1, k1), P(ring, k1), a, d, b);
                }
        }

        /// Emits a triangle with UI Toolkit's expected (clockwise on screen) winding.
        public static void Tri(MeshWriteData mwd, Vector2 a, Vector2 b, Vector2 c, int ia, int ib, int ic)
        {
            float cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            mwd.SetNextIndex((ushort)ia);
            if (cross >= 0) { mwd.SetNextIndex((ushort)ib); mwd.SetNextIndex((ushort)ic); }
            else { mwd.SetNextIndex((ushort)ic); mwd.SetNextIndex((ushort)ib); }
        }
    }

    /// 24×24 stroke icon (Figma "Icons — 24px stroke 1.8"): stroke 1.8/24 of the size, round caps and joins.
    public class IconElement : VisualElement
    {
        IconId id; Color color;

        public IconElement(IconId id, float size, Color color)
        {
            this.id = id; this.color = color;
            style.width = size; style.height = size; style.flexShrink = 0;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        public IconId Id { get => id; set { if (id == value) return; id = value; MarkDirtyRepaint(); } }
        public Color Color { get => color; set { if (color == value) return; color = value; MarkDirtyRepaint(); } }

        void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect; float s = Mathf.Min(r.width, r.height) / 24f; if (s <= 0) return;
            var o = new Vector2(r.x + (r.width - 24 * s) * 0.5f, r.y + (r.height - 24 * s) * 0.5f);
            Vec.Stroke(mgc.painter2D, Data(id), s, o, color, 1.8f * s);
        }

        /// Path data from the Figma icon set (figma_a1 ICONS), circles/rects/ellipses as O/R/E shapes.
        public static string Data(IconId id) => id switch
        {
            IconId.Menu => "M4 7h16M4 12h16M4 17h16",
            IconId.Reset => "M20 12a8 8 0 1 1-2.3-5.7;M20 4v5h-5",
            IconId.Help => "O 12 12 9;M9.6 9.4a2.5 2.5 0 1 1 3.4 2.4c-.6.3-1 .8-1 1.4v.6;D 12 17 1.15",
            IconId.Close => "M6 6l12 12M18 6L6 18",
            IconId.Mic => "R 9 3 6 11 3;M5.5 11a6.5 6.5 0 0 0 13 0M12 17.5V21",
            IconId.Send => "M5 12h14M13 6l6 6-6 6",
            IconId.ChevronRight => "M9 6l6 6-6 6",
            IconId.ChevronLeft => "M15 6l-6 6 6 6",
            IconId.ChevronDown => "M6 9l6 6 6-6",
            IconId.Plus => "M12 5v14M5 12h14",
            IconId.Minus => "M5 12h14",
            IconId.Zoom => "O 11 11 6.5;M20 20l-4.3-4.3",
            IconId.Check => "M5 12.5l4.5 4.5L19 7",
            IconId.Lock => "R 5 11 14 10 2.5;M8 11V8a4 4 0 0 1 8 0v3",
            IconId.Unlock => "R 5 11 14 10 2.5;M8 11V8a4 4 0 0 1 7.7-1.5",
            IconId.Leaf => "M5 19C5 10 11 5 20 4c-1 9-6 15-15 15z;M5 19l8-8",
            IconId.Drop => "M12 3s6 6.6 6 11a6 6 0 0 1-12 0c0-4.4 6-11 6-11z",
            IconId.Sun => "O 12 12 4;M12 2.5v2.2M12 19.3v2.2M2.5 12h2.2M19.3 12h2.2M5.3 5.3l1.6 1.6M17.1 17.1l1.6 1.6M5.3 18.7l1.6-1.6M17.1 6.9l1.6-1.6",
            IconId.CO2 => "O 12 12 3.2;O 4.6 12 2.2;O 19.4 12 2.2;M6.8 11h2M15.2 11h2M6.8 13h2M15.2 13h2",
            IconId.O2 => "O 8.5 12 4;O 15.5 12 4",
            IconId.Glucose => "M12 3l7.8 4.5v9L12 21l-7.8-4.5v-9z;O 12 12 2",
            IconId.Scan => "M4 8V5a1 1 0 0 1 1-1h3M16 4h3a1 1 0 0 1 1 1v3M20 16v3a1 1 0 0 1-1 1h-3M8 20H5a1 1 0 0 1-1-1v-3M4 12h16",
            IconId.Settings => "M4 7h9M17 7h3M4 17h3M11 17h9;O 15 7 2;O 9 17 2",
            IconId.Pause => "M8.5 5v14M15.5 5v14",
            IconId.Play => "M7 5l12 7-12 7z",
            IconId.Replay => "M4 12a8 8 0 1 0 2.3-5.7;M4 4v5h5",
            IconId.Info => "O 12 12 9;M12 11v6;D 12 7.6 1.15",
            IconId.Sparkle => "M12 3l2 6.2 6.2 2-6.2 2L12 19.4l-2-6.2-6.2-2 6.2-2z",
            IconId.CC => "R 3 6 18 12 2.5;M10.5 10.3a2.2 2.2 0 1 0 0 3.4M17 10.3a2.2 2.2 0 1 0 0 3.4",
            IconId.Eye => "M2.5 12S6 5.5 12 5.5 21.5 12 21.5 12 18 18.5 12 18.5 2.5 12 2.5 12z;O 12 12 3",
            IconId.Plant => "M12 21v-9M12 12c0-4 3-7 7-7 0 4-3 7-7 7zM12 15c0-3-2.2-5-6-5 0 3 2.2 5 6 5zM8 21h8",
            IconId.Root => "M4 6h16M12 6v6M12 12l-5 7M12 12l5 7M12 15l-1.5 6M12 15l1.5 6",
            IconId.Stem => "M12 21V3M12 9l4.5-3M12 14l-4.5-3",
            IconId.Xylem => "M8 3v18M16 3v18M12 18V7M9.5 9.5L12 7l2.5 2.5",
            IconId.Phloem => "M8 3v18M16 3v18M12 6v11M9.5 14.5L12 17l2.5-2.5",
            IconId.Stomata => "E 12 12 7.5 9;E 12 12 1.8 5.5",
            IconId.Cell => "R 3 4 18 16 4;O 15.5 10 2.5;E 7.5 15.5 2 1.2;E 8 8.5 1.6 1",
            IconId.Chloroplast => "E 12 12 9.5 6;M6.5 10h3M6.5 14h3M10.5 12h3M14.5 10h3M14.5 14h3",
            IconId.Energy => "M13 2.5L4.5 14H11l-1 7.5L19.5 10H13z",
            IconId.Target => "O 12 12 8;O 12 12 3;M12 2v3M12 19v3M2 12h3M19 12h3",
            IconId.ArrowUp => "M12 19V5M6 11l6-6 6 6",
            IconId.ArrowDown => "M12 5v14M6 13l6 6 6-6",
            IconId.Bug => "R 7 8 10 12 5;M12 8v12M7 13H3M21 13h-4M8 5l2 3M16 5l-2 3",
            IconId.Exit => "M10 4H6a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h4;M15 8l4 4-4 4;M19 12H9",   // arrow leaving through a door frame
            _ => "O 12 12 8"
        };
    }

    public enum ArtId { Curio, ClueCO2, ClueWater, ClueSun, CO2, H2O, O2, Glucose, Light }

    /// Brand art from the Figma "Brand art" components (Dr. Curio, clue art, molecule art) with real gradients.
    public class ArtElement : VisualElement
    {
        ArtId id; bool blink;

        public ArtElement(ArtId id, float size)
        {
            this.id = id; style.width = size; style.height = size; style.flexShrink = 0;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        public ArtId Id { get => id; set { if (id == value) return; id = value; MarkDirtyRepaint(); } }
        /// Dr. Curio blink (eyes squash) — driven by the Hud so every avatar blinks together.
        public bool Blink { get => blink; set { if (blink == value) return; blink = value; MarkDirtyRepaint(); } }

        static Color H(int rgb, float a = 1f) => Palette.Hex(rgb, a);
        static float Box(ArtId id) => id == ArtId.ClueCO2 || id == ArtId.ClueWater || id == ArtId.ClueSun ? 72f : 64f;

        void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect; float vb = Box(id), s = Mathf.Min(r.width, r.height) / vb; if (s <= 0) return;
            var o = new Vector2(r.x + (r.width - vb * s) * 0.5f, r.y + (r.height - vb * s) * 0.5f);
            var p = mgc.painter2D;
            switch (id)
            {
                case ArtId.Curio:
                    Vec.Stroke(p, "M32 13V8", s, o, H(0xBFD3CE), 2.4f * s);
                    Vec.LinearFill(mgc, "M32 9c1-5 6-7 11-6-1 5-6 7-11 6z", s, o, new Vector2(32, 2), new Vector2(43, 10), H(0x8CF5B4), H(0x1F8A55));
                    Vec.LinearFill(mgc, "M32 9c-1-5-6-7-11-6 1 5 6 7 11 6z", s, o, new Vector2(21, 2), new Vector2(32, 10), H(0x92C65C), H(0x347626));
                    Vec.RadialDisc(mgc, new Vector2(32, 36), 22, new Vector2(26.7f, 26.3f), s, o, H(0xFFFCF3), H(0xF5EEDF), H(0xE9DFC6));
                    Vec.Fill(p, "R 14.5 26 35 20 10", s, o, H(0x0A1A1F));
                    Vec.Fill(p, "M23 55L41 55L35 62z", s, o, H(0x347626));
                    Vec.Fill(p, "E 25 36 5.2 6.4;E 39 36 5.2 6.4", s, o, H(0x3DD6E8, 0.22f));
                    if (blink) Vec.Fill(p, "E 25 37 3.6 0.9;E 39 37 3.6 0.9", s, o, H(0x3DD6E8));
                    else
                    {
                        Vec.Fill(p, "E 25 36 3.4 4.6;E 39 36 3.4 4.6", s, o, H(0x3DD6E8));
                        Vec.Fill(p, "O 26 34.4 1.1;O 40 34.4 1.1", s, o, Color.white);
                    }
                    Vec.Fill(p, "O 47 44 6.5", s, o, H(0x3DD6E8, 0.18f));
                    Vec.Stroke(p, "O 47 44 6.5", s, o, H(0xF5C451), 2f * s);
                    break;
                case ArtId.ClueCO2:
                    Vec.LinearFill(mgc, "M12 62C12 32 30 12 64 8c-2 32-20 54-52 54z", s, o, new Vector2(12, 62), new Vector2(64, 8), H(0x0F6B3F), H(0x7CF0A8));
                    Vec.Stroke(p, "M12 62L44 30", s, o, H(0xC9FFE0, 0.6f), 2f * s);
                    Atom(mgc, 38, 40, 5, H(0xE9F2F8), s, o);
                    Atom(mgc, 27, 40, 3.6f, H(0xE9F2F8, 0.9f), s, o);
                    Atom(mgc, 49, 40, 3.6f, H(0xE9F2F8, 0.9f), s, o);
                    break;
                case ArtId.ClueWater:
                    Vec.LinearFill(mgc, "M36 6s20 22 20 38a20 20 0 0 1-40 0C16 28 36 6 36 6z", s, o, new Vector2(36, 6), new Vector2(36, 64), H(0x9AD7FF), H(0x1667C9));
                    Vec.Stroke(p, "M27 44a9 9 0 0 0 9 9", s, o, H(0xE6F6FF), 3f * s);
                    break;
                case ArtId.ClueSun:
                    Vec.Stroke(p, "M36 5v9M36 58v9M5 36h9M58 36h9M14 14l6 6M52 52l6 6M14 58l6-6M52 20l6-6", s, o, H(0xF5C451), 3.2f * s);
                    Vec.RadialDisc(mgc, new Vector2(36, 36), 15, new Vector2(36, 36), s, o, H(0xFFF4C2), H(0xFAD67A), H(0xF5C451), H(0xC98D16));
                    break;
                case ArtId.H2O:
                    Atom(mgc, 32, 34, 13, Palette.OxygenAtom, s, o);
                    Atom(mgc, 16, 22, 8, Palette.Hydrogen, s, o);
                    Atom(mgc, 48, 22, 8, Palette.Hydrogen, s, o);
                    break;
                case ArtId.CO2:
                    Atom(mgc, 12, 32, 9, Palette.OxygenAtom, s, o);
                    Atom(mgc, 52, 32, 9, Palette.OxygenAtom, s, o);
                    Atom(mgc, 32, 32, 11, H(0x4A515B), s, o);
                    break;
                case ArtId.O2:
                    Atom(mgc, 23, 32, 12, Palette.OxygenAtom, s, o);
                    Atom(mgc, 41, 32, 12, H(0xFF7272), s, o);
                    break;
                case ArtId.Glucose:
                    Vec.Fill(p, "M32 8l20 11.5v23L32 54 12 42.5v-23z", s, o, H(0xFFB547, 0.22f));
                    Vec.Stroke(p, "M32 8l20 11.5v23L32 54 12 42.5v-23z", s, o, H(0xFFB547), 3f * s);
                    Atom(mgc, 32, 8, 4.5f, H(0x4A515B), s, o); Atom(mgc, 52, 19.5f, 4.5f, H(0x4A515B), s, o); Atom(mgc, 52, 42.5f, 4.5f, H(0x4A515B), s, o);
                    Atom(mgc, 32, 54, 4.5f, H(0x4A515B), s, o); Atom(mgc, 12, 42.5f, 4.5f, H(0x4A515B), s, o); Atom(mgc, 12, 19.5f, 4.5f, Palette.OxygenAtom, s, o);
                    break;
                case ArtId.Light:
                    Vec.RadialDisc(mgc, new Vector2(32, 32), 30, new Vector2(32, 32), s, o, H(0xFFFBE0), H(0xFFE48A), H(0xFFD866, 0.55f), H(0xFFD866, 0f));
                    Vec.Fill(p, "M34 14L22 35h9l-2 15 13-22h-9z", s, o, H(0xFFF6CC));
                    break;
            }
        }

        /// Glossy ball: focal radial gradient (highlight up-left → base → shaded rim).
        static void Atom(MeshGenerationContext mgc, float x, float y, float r, Color c, float s, Vector2 o)
        {
            Color hi = Color.Lerp(c, Color.white, 0.6f), lo = Color.Lerp(c, Color.black, 0.28f);
            hi.a = lo.a = c.a;
            Vec.RadialDisc(mgc, new Vector2(x, y), r, new Vector2(x - r * 0.38f, y - r * 0.4f), s, o, hi, c, lo);
        }
    }

    /// Soft outer glow / drop shadow (Figma "Glow *" and "Glass Panel" effects; UI Toolkit has no box-shadow).
    /// Absolute child that extends `blur` px around its parent and draws a feathered rounded-rect ring mesh.
    public class GlowElement : VisualElement
    {
        Color color; readonly float radius, blur;
        static readonly float[] Off = { 0f, 0.18f, 0.45f, 1f }, Alpha = { 1f, 0.55f, 0.18f, 0f };

        public GlowElement(Color color, float radius, float blur, float alpha = 1f)
        {
            this.color = color; this.color.a *= alpha; this.radius = radius; this.blur = blur;
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute; style.left = style.top = style.right = style.bottom = -blur;
            generateVisualContent += Draw;
        }

        public Color Color { get => color; set { if (color == value) return; color = value; MarkDirtyRepaint(); } }
        /// Drop shadow of a glass panel (a coloured glow replaces it).
        public bool Shadow { get; set; }

        void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            float bw = parent != null ? parent.resolvedStyle.borderLeftWidth : 0f, inset = blur - bw;
            var inner = new Rect(r.x + inset, r.y + inset, r.width - 2 * inset, r.height - 2 * inset);
            if (inner.width <= 0 || inner.height <= 0) return;
            float rad = Mathf.Min(radius, inner.width * 0.5f, inner.height * 0.5f);
            const int seg = 8, per = 4 * (seg + 1);
            int loops = Off.Length;
            var mwd = mgc.Allocate(per * loops, per * (loops - 1) * 6);
            var pts = new Vector2[per * loops];
            for (int l = 0; l < loops; l++)
            {
                float d = Off[l] * blur, rr = rad + d; var c = color; c.a *= Alpha[l];
                Vector2[] centres = { new Vector2(inner.xMax - rad, inner.yMin + rad), new Vector2(inner.xMax - rad, inner.yMax - rad), new Vector2(inner.xMin + rad, inner.yMax - rad), new Vector2(inner.xMin + rad, inner.yMin + rad) };
                int k = 0;
                for (int q = 0; q < 4; q++)
                    for (int i = 0; i <= seg; i++)
                    {
                        float a = (-90f + q * 90f + i * 90f / seg) * Mathf.Deg2Rad;
                        var v = centres[q] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
                        pts[l * per + k++] = v;
                        mwd.SetNextVertex(new Vertex { position = new Vector3(v.x, v.y, Vertex.nearZ), tint = c });
                    }
            }
            for (int l = 0; l < loops - 1; l++)
                for (int k = 0; k < per; k++)
                {
                    int k1 = (k + 1) % per, a = l * per + k, b = l * per + k1, c = (l + 1) * per + k, d = (l + 1) * per + k1;
                    Vec.Tri(mwd, pts[a], pts[c], pts[d], a, c, d);
                    Vec.Tri(mwd, pts[a], pts[d], pts[b], a, d, b);
                }
        }
    }

    /// Screen-space sparkle bursts (unlock rewards, correct answers, case solved). Fixed pool, no allocations per frame.
    public class SparkleElement : VisualElement
    {
        const int Max = 180;
        readonly Vector2[] pos = new Vector2[Max], vel = new Vector2[Max];
        readonly float[] age = new float[Max], life = new float[Max], size = new float[Max];
        readonly Color[] col = new Color[Max];
        readonly bool[] star = new bool[Max];
        int alive;

        public SparkleElement()
        {
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute; style.left = style.top = style.right = style.bottom = 0;
            generateVisualContent += Draw;
        }

        public void Burst(Vector2 centre, Color a, Color b, int count, float speed = 420f, float spread = 40f)
        {
            for (int n = 0; n < count; n++)
            {
                int i = Free(); if (i < 0) return;
                float ang = UnityEngine.Random.value * Mathf.PI * 2, sp = speed * (0.25f + UnityEngine.Random.value * 0.75f);
                pos[i] = centre + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * UnityEngine.Random.value * spread;
                vel[i] = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * 0.75f) * sp;
                age[i] = 0; life[i] = 0.9f + UnityEngine.Random.value * 0.9f; size[i] = 2.5f + UnityEngine.Random.value * 5f;
                col[i] = Color.Lerp(a, b, UnityEngine.Random.value); star[i] = UnityEngine.Random.value < 0.3f;
                alive++;
            }
        }

        int Free() { for (int i = 0; i < Max; i++) if (life[i] <= 0) return i; return -1; }

        public void Tick(float dt)
        {
            if (alive == 0) return;
            alive = 0;
            for (int i = 0; i < Max; i++)
            {
                if (life[i] <= 0) continue;
                age[i] += dt;
                if (age[i] >= life[i]) { life[i] = 0; continue; }
                alive++;
                vel[i] *= Mathf.Exp(-2.4f * dt); vel[i].y += 60f * dt;
                pos[i] += vel[i] * dt;
            }
            MarkDirtyRepaint();
        }

        public void Stop() { for (int i = 0; i < Max; i++) life[i] = 0; alive = 0; MarkDirtyRepaint(); }

        void Draw(MeshGenerationContext mgc)
        {
            var p = mgc.painter2D;
            for (int i = 0; i < Max; i++)
            {
                if (life[i] <= 0) continue;
                float t = age[i] / life[i], a = t < 0.15f ? t / 0.15f : 1f - (t - 0.15f) / 0.85f;
                var c = col[i]; c.a *= a; float r = size[i] * (1f - 0.4f * t);
                p.fillColor = c; p.BeginPath();
                if (star[i])
                {
                    var q = pos[i]; float l = r * 2.2f, w = r * 0.45f;
                    p.MoveTo(q + new Vector2(0, -l)); p.LineTo(q + new Vector2(w, -w)); p.LineTo(q + new Vector2(l, 0)); p.LineTo(q + new Vector2(w, w));
                    p.LineTo(q + new Vector2(0, l)); p.LineTo(q + new Vector2(-w, w)); p.LineTo(q + new Vector2(-l, 0)); p.LineTo(q + new Vector2(-w, -w)); p.ClosePath();
                }
                else p.Arc(pos[i], r, Angle.Degrees(0), Angle.Degrees(360));
                p.Fill();
            }
        }
    }

    /// Filled vector shape in its own viewBox (equation arrow head, …).
    public class ShapeElement : VisualElement
    {
        readonly string spec; readonly float vb; Color color;
        public ShapeElement(string spec, float viewBox, float size, Color color)
        {
            this.spec = spec; vb = viewBox; this.color = color; style.width = size; style.height = size; style.flexShrink = 0;
            pickingMode = PickingMode.Ignore; generateVisualContent += Draw;
        }
        void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect; float s = Mathf.Min(r.width, r.height) / vb; if (s <= 0) return;
            Vec.Fill(mgc.painter2D, spec, s, new Vector2(r.x + (r.width - vb * s) * 0.5f, r.y + (r.height - vb * s) * 0.5f), color);
        }
    }

    /// Loading / transition ring of drifting gold particles (Figma screen 25).
    public class RingElement : VisualElement
    {
        float phase;
        public RingElement() { pickingMode = PickingMode.Ignore; generateVisualContent += Draw; }
        public float Phase { set { phase = value; MarkDirtyRepaint(); } }

        void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect; var c = r.center; float rad = Mathf.Min(r.width, r.height) * 0.42f;
            var p = mgc.painter2D;
            for (int i = 0; i < 120; i++)
            {
                float h = Mathf.Sin(i * 12.9898f) * 43758.5453f; h -= Mathf.Floor(h);           // stable per-dot hash
                float a = i / 120f * Mathf.PI * 2 + phase * (0.15f + h * 0.1f), rr = rad * (0.9f + h * 0.22f) + Mathf.Sin(phase * 1.3f + i) * 4f;
                float tw = 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Sin(phase * 2.2f + i * 1.7f));
                p.fillColor = new Color(0.96f, 0.77f, 0.32f, tw * (0.3f + 0.7f * h));
                p.BeginPath(); p.Arc(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr, 1.5f + h * 4f, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();
            }
        }
    }
}
