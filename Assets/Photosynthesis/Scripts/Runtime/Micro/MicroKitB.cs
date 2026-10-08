using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Photosynthesis
{
    /// Shared procedural kit for the tissue / cell / chloroplast worlds: superellipsoid meshes, static batching,
    /// soft glow billboards, light beams, backdrop, mood and camera flights.
    public static class MicroKitB
    {
        // ------------------------------------------------------------------ meshes
        /// Superellipsoid with half extents `half`: a = XZ exponent, b = Y exponent (2,2 sphere · 2,5 disc · 6,6 rounded box).
        /// The UV seam column is duplicated (textures wrap cleanly) and its normals are welded (no lighting seam).
        /// `bump(dir)` displaces radially (blobs).
        public static Mesh Super(Vector3 half, float a = 2f, float b = 2f, int rings = 12, int segs = 18, bool inside = false,
            Func<Vector3, float> bump = null)
        {
            int rc = rings - 1, w = segs + 1, n = rc * w + 2;
            var v = new Vector3[n]; var uv = new Vector2[n];
            Vector3 P(Vector3 d)
            {
                float r = Mathf.Pow(Mathf.Pow(Mathf.Abs(d.x), a) + Mathf.Pow(Mathf.Abs(d.z), a), b / a) + Mathf.Pow(Mathf.Abs(d.y), b);
                var p = d * Mathf.Pow(Mathf.Max(r, 1e-6f), -1f / b);
                if (bump != null) p *= 1f + bump(d);
                return Vector3.Scale(p, half);
            }
            int R(int r, int k) => 1 + (r - 1) * w + k;
            v[0] = P(Vector3.up); uv[0] = new Vector2(0.5f, 1f);
            v[n - 1] = P(Vector3.down); uv[n - 1] = new Vector2(0.5f, 0f);
            for (int r = 1; r < rings; r++)
            {
                float phi = Mathf.PI * r / rings, y = Mathf.Cos(phi), s = Mathf.Sin(phi);
                for (int k = 0; k <= segs; k++)
                {
                    float th = 2f * Mathf.PI * k / segs; int i = R(r, k);
                    v[i] = k == segs ? v[R(r, 0)] : P(new Vector3(s * Mathf.Cos(th), y, s * Mathf.Sin(th)));
                    uv[i] = new Vector2(k / (float)segs, 1f - r / (float)rings);
                }
            }
            var tri = new List<int>(segs * rings * 6);
            void T(int i0, int i1, int i2)
            {
                if (inside) { tri.Add(i0); tri.Add(i2); tri.Add(i1); } else { tri.Add(i0); tri.Add(i1); tri.Add(i2); }
            }
            for (int k = 0; k < segs; k++) T(0, R(1, k + 1), R(1, k));
            for (int r = 1; r < rc; r++)
                for (int k = 0; k < segs; k++) { T(R(r, k), R(r, k + 1), R(r + 1, k)); T(R(r, k + 1), R(r + 1, k + 1), R(r + 1, k)); }
            for (int k = 0; k < segs; k++) T(R(rc, k), R(rc, k + 1), n - 1);
            var m = new Mesh { name = "Super" };
            m.vertices = v; m.uv = uv; m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds(); Fx.FixNormals(m);
            var nr = m.normals;
            for (int r = 1; r < rings; r++) nr[R(r, 0)] = nr[R(r, segs)] = (nr[R(r, 0)] + nr[R(r, segs)]).normalized;
            m.normals = nr;
            return m;
        }

        /// Ellipsoid zone with its pole on +z, between polar angles phi0..phi1 (0 = +z tip, π = −z tip): the cut at
        /// z = e.z·cos(phi) is an exact ring, so cutaways have clean edges. `both` adds the reverse winding (seen from inside too).
        public static Mesh EllipsoidZone(Vector3 e, float phi0, float phi1, bool both, int rings = 28, int segs = 64)
        {
            int w = segs + 1, n = (rings + 1) * w;
            var v = new Vector3[n]; var nr = new Vector3[n]; var uv = new Vector2[n];
            for (int r = 0; r <= rings; r++)
            {
                float phi = Mathf.Lerp(phi0, phi1, r / (float)rings), s = Mathf.Sin(phi), c = Mathf.Cos(phi);
                for (int k = 0; k <= segs; k++)
                {
                    float th = 2f * Mathf.PI * k / segs; int i = r * w + k;
                    var p = new Vector3(e.x * s * Mathf.Cos(th), e.y * s * Mathf.Sin(th), e.z * c);
                    v[i] = p; uv[i] = new Vector2(k / (float)segs, r / (float)rings);
                    nr[i] = new Vector3(p.x / (e.x * e.x), p.y / (e.y * e.y), p.z / (e.z * e.z)).normalized;
                    if (nr[i].sqrMagnitude < 0.5f) nr[i] = c > 0 ? Vector3.forward : Vector3.back;
                }
            }
            var tri = new List<int>(rings * segs * (both ? 12 : 6));
            for (int r = 0; r < rings; r++)
                for (int k = 0; k < segs; k++)
                {
                    int a = r * w + k, b = a + 1, c = a + w, d = c + 1;   // b: +theta, c: +phi
                    tri.Add(a); tri.Add(c); tri.Add(b); tri.Add(b); tri.Add(c); tri.Add(d);   // outward
                }
            var m = new Mesh { name = "EllipsoidZone", indexFormat = IndexFormat.UInt32 };
            if (both)
            {   // second copy of the vertices with flipped normals + reversed winding
                var v2 = new Vector3[n * 2]; var n2 = new Vector3[n * 2]; var uv2 = new Vector2[n * 2];
                for (int i = 0; i < n; i++) { v2[i] = v2[n + i] = v[i]; n2[i] = nr[i]; n2[n + i] = -nr[i]; uv2[i] = uv2[n + i] = uv[i]; }
                int cnt = tri.Count;
                for (int i = 0; i < cnt; i += 3) { tri.Add(n + tri[i]); tri.Add(n + tri[i + 2]); tri.Add(n + tri[i + 1]); }
                v = v2; nr = n2; uv = uv2;
            }
            m.vertices = v; m.normals = nr; m.uv = uv; m.SetTriangles(tri, 0); m.RecalculateBounds();
            return m;
        }

        public static Mesh Sphere(int rings = 8, int segs = 12) => Super(Vector3.one * 0.5f, 2, 2, rings, segs);

        /// Closed tube loop (rings, sieve plates, wheels). Points in local space.
        public static Mesh Loop(IList<Vector3> pts, float r, int sides = 8)
        {
            var p = new List<Vector3>(pts) { pts[0], pts[1] };
            return Fx.Tube(p, r, r, sides);
        }

        public static List<Vector3> Circle(float radius, int n, float y = 0f)
        {
            var o = new List<Vector3>(n);
            for (int i = 0; i < n; i++) { float a = i * Mathf.PI * 2f / n; o.Add(new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius)); }
            return o;
        }

        /// Simplified glucose unit (hexagon ring + atoms) used for the cellulose / starch / energy vignettes.
        static Mesh hex;
        public static Mesh HexUnit
        {
            get
            {
                if (hex) return hex;
                var ring = new List<Vector3>(6);
                for (int i = 0; i < 6; i++) { float a = i * Mathf.PI / 3f; ring.Add(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * 0.5f); }
                var b = new Batch(); var sph = Sphere(6, 10);
                b.Add(Loop(ring, 0.06f, 6), Vector3.zero, Quaternion.identity, Vector3.one);
                foreach (var p in ring) b.Add(sph, p, Quaternion.identity, Vector3.one * 0.22f);
                return hex = b.Mesh("HexUnit");
            }
        }

        /// Merge many transformed copies into one mesh (one draw per material).
        public class Batch
        {
            readonly List<CombineInstance> list = new List<CombineInstance>();
            public int Count => list.Count;
            public void Add(Mesh m, Vector3 p, Quaternion r, Vector3 s) => list.Add(new CombineInstance { mesh = m, transform = Matrix4x4.TRS(p, r, s) });
            public Mesh Mesh(string name)
            {
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(list.ToArray(), true, true); mesh.RecalculateBounds(); list.Clear();
                return mesh;
            }
            public GameObject Make(string name, Transform parent, Material mat) => Fx.MeshObj(name, Mesh(name), mat, parent);
        }

        public static float Noise(Vector3 p) =>
            (Mathf.PerlinNoise(p.x + 3.1f, p.y + 7.7f) + Mathf.PerlinNoise(p.y + 31.7f, p.z + 1.3f) + Mathf.PerlinNoise(p.z + 17.1f, p.x + 5.3f)) / 1.5f - 1f;

        // ------------------------------------------------------------------ textures & glows
        public static Texture2D Tex(int w, int h, Func<float, float, Color> f, TextureWrapMode wrap = TextureWrapMode.Clamp)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = wrap, name = "MicroTex" };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[y * w + x] = f((x + 0.5f) / w, (y + 0.5f) / h);
            t.SetPixels(px); t.Apply(true, true); return t;
        }

        static Texture2D soft, beam;
        /// Premultiplied soft dot (works for SrcAlpha·One and One·One additive).
        public static Texture2D Soft => soft ? soft : soft = Tex(64, 64, (u, v) =>
        {
            float d = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f); d = d * d * (3 - 2 * d); d *= d;
            return new Color(d, d, d, d);
        });
        /// Soft-edged light shaft, bright at v=0 (source), fading at both ends.
        public static Texture2D BeamTex => beam ? beam : beam = Tex(32, 128, (u, v) =>
        {
            float a = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * u)), 2.2f) * Mathf.Lerp(1f, 0.45f, v) * Mathf.Clamp01((1f - v) * 10f) * Mathf.Clamp01(v * 6f);
            return new Color(a, a, a, a);
        });

        public static Material GlowMat(Color c) { var m = Fx.Mat(MatKind.Additive, c); Fx.SetTexture(m, Soft); return m; }

        /// Camera-facing soft glow quad.
        public static GameObject Glow(Transform parent, Vector3 localPos, float size, Material m)
        {
            var g = Fx.Prim(PrimitiveType.Quad, parent, localPos, Vector3.one * size, m, false, "Glow");
            g.AddComponent<MicroFace>(); return g;
        }

        /// Crossed soft quads from→to (local). Scale the root's Y (0..1) to grow it.
        public static GameObject Beam(Transform parent, Vector3 from, Vector3 to, float width, Material m)
        {
            var root = Fx.Node("Beam", parent, from);
            root.transform.localRotation = Quaternion.FromToRotation(Vector3.up, to - from);
            float len = Vector3.Distance(from, to);
            for (int i = 0; i < 2; i++)
            {
                var q = Fx.Prim(PrimitiveType.Quad, root.transform, new Vector3(0, len * 0.5f, 0), new Vector3(width, len, 1), m, false, "Shaft");
                q.transform.localRotation = Quaternion.Euler(0, 90 * i, 0);
            }
            return root;
        }

        public static TrailRenderer Trail(GameObject g, Material m, float width, float time)
        {
            var tr = g.AddComponent<TrailRenderer>();
            tr.sharedMaterial = m; tr.time = time; tr.widthMultiplier = width; tr.widthCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);
            tr.minVertexDistance = 0.03f; tr.numCapVertices = 2; tr.shadowCastingMode = ShadowCastingMode.Off; tr.Clear();
            return tr;
        }

        // ------------------------------------------------------------------ environment
        /// Inside-out gradient sphere so the chamber sky never shows behind a micro world.
        public static GameObject Backdrop(Transform parent, float radius, Color top, Color bottom)
        {
            var tex = Tex(4, 64, (u, v) => Color.Lerp(bottom, top, v * v * (3 - 2 * v)));
            var m = Fx.Mat(MatKind.Unlit, Color.white); Fx.SetTexture(m, tex);
            return Fx.MeshObj("Backdrop", Super(Vector3.one * radius, 2, 2, 24, 32, true), m, parent);
        }

        /// Fog + trilight ambient + the chamber sun re-aimed (the chamber restores all of it when shown).
        /// Colours are sRGB (converted to linear by Unity); below ~0.08 they render as black after ACES.
        public static void Mood(Color fog, float density, Color sky, Color equator, Color ground, Color sunColor, float sunIntensity, Vector3 sunEuler)
        {
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = fog; RenderSettings.fogDensity = density;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = sky; RenderSettings.ambientEquatorColor = equator; RenderSettings.ambientGroundColor = ground;
            var sun = Module.I != null && Module.I.chamber ? Module.I.chamber.sun : RenderSettings.sun;
            if (!sun) return;
            sun.enabled = true; sun.color = sunColor; sun.intensity = sunIntensity; sun.transform.rotation = Quaternion.Euler(sunEuler);
        }

        // ------------------------------------------------------------------ camera
        /// Scripted flight through Catmull-Rom position + look-target curves.
        public static IEnumerator FlyCam(CameraDirector cam, IList<Vector3> pos, IList<Vector3> look, float dur, float fov1, Ease e = Ease.InOut)
        {
            if (!cam) yield break;
            cam.StopOrbit(); var c = cam.Cam; float f0 = c.fieldOfView;
            yield return Fx.Tween(dur, t =>
            {
                Vector3 p = Fx.CatmullRom(pos, t), l = Fx.CatmullRom(look, t);
                if ((l - p).sqrMagnitude > 1e-6f) cam.transform.SetPositionAndRotation(p, Quaternion.LookRotation(l - p));
                c.fieldOfView = Mathf.Lerp(f0, fov1, t);
            }, e);
        }

        /// Damped chase step (call each frame). k = responsiveness.
        public static void Chase(CameraDirector cam, Vector3 pos, Vector3 look, float k)
        {
            if (!cam) return;
            float f = 1f - Mathf.Exp(-k * Time.deltaTime);
            var tr = cam.transform; var p = Vector3.Lerp(tr.position, pos, f);
            var want = Quaternion.LookRotation(look - p);
            tr.SetPositionAndRotation(p, Quaternion.Slerp(tr.rotation, want, Mathf.Min(1f, f * 1.6f)));
        }

        public static Vector3 Bez(Vector3 a, Vector3 b, Vector3 c, float t) => Vector3.Lerp(Vector3.Lerp(a, b, t), Vector3.Lerp(b, c, t), t);
        public static float Smooth01(float t) { t = Mathf.Clamp01(t); return t * t * (3 - 2 * t); }

        public static void Kill(List<GameObject> list)
        {
            foreach (var g in list) if (g) UnityEngine.Object.Destroy(g);
            list.Clear();
        }
    }

    /// Billboard: faces the module camera (cached; no scene searches). Runs after CameraDirector.LateUpdate so glows never lag a frame.
    [DefaultExecutionOrder(1000)]
    public class MicroFace : MonoBehaviour
    {
        static Transform cam;
        void LateUpdate()
        {
            if (!cam) { var c = Module.I != null ? Module.I.cam : null; if (!c) return; cam = c.transform; }
            transform.rotation = cam.rotation;
        }
    }
}
