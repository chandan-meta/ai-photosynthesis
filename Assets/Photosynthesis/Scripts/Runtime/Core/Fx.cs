using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Photosynthesis
{
    public enum MatKind { Lit, LitEmissive, LitTransparent, Glass, Unlit, UnlitTransparent, Additive, Leaf, ParticleAdditive, ParticleAlpha }
    public enum Ease { Linear, In, Out, InOut, Back, Elastic }
    public enum Mol { CO2, H2O, O2, Glucose }

    [Serializable]
    public class GlucoseGeometry
    {
        [Serializable] public class Atom { public string element; public Vector3 position; public float size; }
        [Serializable] public class Bond { public int a, b, order; }
        public Atom[] atoms; public Bond[] bonds; public int[] ring; public string source, name;
        static GlucoseGeometry data;
        public static GlucoseGeometry Data
        {
            get
            {
                if (data != null) return data;
                var json = Resources.Load<TextAsset>("Molecules/GlucoseGeometry");
                if (!json) throw new InvalidOperationException("Verified glucose molecular data is missing");
                return data = JsonUtility.FromJson<GlucoseGeometry>(json.text);
            }
        }
    }

    /// Shared helpers: materials, meshes, molecules, particles, tweens. Everything procedural goes through here.
    public static class Fx
    {
        static MatLib lib;
        public static MatLib Lib => lib ? lib : lib = Resources.Load<MatLib>("PhotosynthesisMaterials");
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor"), EmissionColor = Shader.PropertyToID("_EmissionColor"),
            Smoothness = Shader.PropertyToID("_Smoothness"), Metallic = Shader.PropertyToID("_Metallic"), BaseMap = Shader.PropertyToID("_BaseMap");

        static Material Template(MatKind k)
        {
            var l = Lib;
            if (l == null) return new Material(Shader.Find("Universal Render Pipeline/Lit"));
            return k switch
            {
                MatKind.LitEmissive => l.litEmissive, MatKind.LitTransparent => l.litTransparent, MatKind.Glass => l.glass,
                MatKind.Unlit => l.unlit, MatKind.UnlitTransparent => l.unlitTransparent, MatKind.Additive => l.additive,
                MatKind.Leaf => l.leaf, MatKind.ParticleAdditive => l.particleAdditive, MatKind.ParticleAlpha => l.particleAlpha, _ => l.lit
            };
        }

        /// New material instance from a template. emission is HDR (multiply by intensity for bloom).
        public static Material Mat(MatKind k, Color c, Color? emission = null, float smoothness = -1f, float metallic = -1f)
        {
            var m = new Material(Template(k));
            m.SetColor(BaseColor, c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (emission.HasValue && m.HasProperty(EmissionColor)) m.SetColor(EmissionColor, emission.Value);
            if (smoothness >= 0 && m.HasProperty(Smoothness)) m.SetFloat(Smoothness, smoothness);
            if (metallic >= 0 && m.HasProperty(Metallic)) m.SetFloat(Metallic, metallic);
            if (metallic > 0.5f) SurfaceArt.Apply(m, "Gold", false, 0.2f);
            return m;
        }

        static readonly Dictionary<(MatKind, Color, Color), Material> shared = new Dictionary<(MatKind, Color, Color), Material>();
        /// Cached material — use for many identical objects (atoms, beads). Do not mutate the result.
        public static Material Shared(MatKind k, Color c, Color? emission = null)
        {
            var key = (k, c, emission ?? Color.black);
            if (!shared.TryGetValue(key, out var m) || m == null) shared[key] = m = Mat(k, c, emission);
            return m;
        }

        public static void SetColor(Material m, Color c) { m.SetColor(BaseColor, c); if (m.HasProperty("_Color")) m.SetColor("_Color", c); }

        static readonly Dictionary<Color, Material> rims = new Dictionary<Color, Material>();
        /// Shared fresnel rim (second material on an atom). Null when the scene builder has not made the template yet.
        public static Material Rim(Color c)
        {
            var l = Lib; if (l == null || !l.rim) return null;
            if (!rims.TryGetValue(c, out var m) || !m) { rims[c] = m = new Material(l.rim); m.SetColor("_RimColor", c); }
            return m;
        }
        public static void SetAlpha(Material m, float a) { var c = m.GetColor(BaseColor); c.a = a; SetColor(m, c); }
        public static void SetEmission(Material m, Color c) { if (m.HasProperty(EmissionColor)) m.SetColor(EmissionColor, c); }
        public static void SetTexture(Material m, Texture t) { if (m.HasProperty(BaseMap)) m.SetTexture(BaseMap, t); }

        // ---------------------------------------------------------------- objects & meshes
        public static GameObject Node(string name, Transform parent, Vector3 localPos = default)
        {
            var g = new GameObject(name); g.transform.SetParent(parent, false); g.transform.localPosition = localPos; return g;
        }

        public static GameObject Prim(PrimitiveType t, Transform parent, Vector3 localPos, Vector3 scale, Material m, bool collider = false, string name = null)
        {
            var g = GameObject.CreatePrimitive(t);
            if (name != null) g.name = name;
            if (!collider) UnityEngine.Object.Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false); g.transform.localPosition = localPos; g.transform.localScale = scale;
            var r = g.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return g;
        }

        public static GameObject MeshObj(string name, Mesh mesh, Material m, Transform parent, bool shadows = false)
        {
            if (mesh && mesh.isReadable && mesh.tangents.Length == 0 && mesh.uv.Length == mesh.vertexCount && mesh.normals.Length == mesh.vertexCount) mesh.RecalculateTangents();
            var g = Node(name, parent);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = m;
            r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            return g;
        }

        /// Tube along a polyline (local space). Radius lerps r0→r1. Used for stems, roots, vessels, tunnels.
        public static Mesh Tube(IList<Vector3> pts, float r0, float r1, int sides = 10, bool insideOut = false, float uvRepeat = 1f)
        {
            int n = pts.Count; var v = new Vector3[n * (sides + 1)]; var nr = new Vector3[v.Length]; var uv = new Vector2[v.Length];
            var tri = new int[(n - 1) * sides * 6];
            Vector3 up = Vector3.up, prevT = (pts[1] - pts[0]).normalized;
            if (Mathf.Abs(Vector3.Dot(prevT, up)) > 0.95f) up = Vector3.right;
            Vector3 normal = Vector3.Cross(prevT, up).normalized;
            float len = 0;
            for (int i = 0; i < n; i++)
            {
                Vector3 tan = i < n - 1 ? (pts[i + 1] - pts[i]).normalized : (pts[i] - pts[i - 1]).normalized;
                normal = Vector3.ProjectOnPlane(normal, tan).normalized; // parallel transport keeps the tube from twisting
                if (normal.sqrMagnitude < 1e-4f) normal = Vector3.Cross(tan, Vector3.right).normalized;
                Vector3 bin = Vector3.Cross(tan, normal);
                if (i > 0) len += Vector3.Distance(pts[i], pts[i - 1]);
                float r = Mathf.Lerp(r0, r1, n > 1 ? i / (n - 1f) : 0);
                for (int s = 0; s <= sides; s++)
                {
                    float a = s / (float)sides * Mathf.PI * 2;
                    Vector3 d = Mathf.Cos(a) * normal + Mathf.Sin(a) * bin;
                    int k = i * (sides + 1) + s;
                    v[k] = pts[i] + d * r; nr[k] = insideOut ? -d : d; uv[k] = new Vector2(s / (float)sides, len * uvRepeat);
                }
            }
            int t = 0;
            for (int i = 0; i < n - 1; i++)
                for (int s = 0; s < sides; s++)
                {
                    int a = i * (sides + 1) + s, b = a + sides + 1;
                    if (insideOut) { tri[t++] = a; tri[t++] = b; tri[t++] = a + 1; tri[t++] = a + 1; tri[t++] = b; tri[t++] = b + 1; }
                    else { tri[t++] = a; tri[t++] = a + 1; tri[t++] = b; tri[t++] = a + 1; tri[t++] = b + 1; tri[t++] = b; }
                }
            var m = new Mesh { name = "Tube" };
            if (v.Length > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.vertices = v; m.normals = nr; m.uv = uv; m.triangles = tri; m.RecalculateBounds();
            return m;
        }

        /// Catmull-Rom resample of control points into a smooth polyline with `count` points.
        public static List<Vector3> Smooth(IList<Vector3> c, int count)
        {
            var o = new List<Vector3>(count);
            for (int i = 0; i < count; i++) o.Add(CatmullRom(c, i / (count - 1f)));
            return o;
        }

        public static Vector3 CatmullRom(IList<Vector3> c, float t)
        {
            if (c.Count == 1) return c[0];
            float f = Mathf.Clamp01(t) * (c.Count - 1); int i = Mathf.Min((int)f, c.Count - 2); float u = f - i;
            Vector3 p0 = c[Mathf.Max(i - 1, 0)], p1 = c[i], p2 = c[i + 1], p3 = c[Mathf.Min(i + 2, c.Count - 1)];
            return 0.5f * (2 * p1 + (-p0 + p2) * u + (2 * p0 - 5 * p1 + 4 * p2 - p3) * u * u + (-p0 + 3 * p1 - 3 * p2 + p3) * u * u * u);
        }

        /// Point at normalised arc length along a polyline.
        public static Vector3 Along(IList<Vector3> p, float t)
        {
            if (p.Count == 1) return p[0];
            float total = 0; for (int i = 1; i < p.Count; i++) total += Vector3.Distance(p[i - 1], p[i]);
            float d = Mathf.Clamp01(t) * total;
            for (int i = 1; i < p.Count; i++)
            {
                float s = Vector3.Distance(p[i - 1], p[i]);
                if (d <= s || i == p.Count - 1) return Vector3.Lerp(p[i - 1], p[i], s > 0 ? Mathf.Clamp01(d / s) : 0);
                d -= s;
            }
            return p[p.Count - 1];
        }

        /// Replace NaN / zero-length normals (collapsed leaf tips, sphere poles, degenerate triangles). In the shader
        /// normalize(0) = NaN, and one NaN pixel blooms into a white disc across the frame.
        public static Mesh FixNormals(Mesh m)
        {
            if (!m || !m.isReadable || m.vertexCount == 0) return m;
            var n = m.normals; bool bad = n == null || n.Length != m.vertexCount;
            if (!bad) for (int i = 0; i < n.Length; i++) if (!(n[i].sqrMagnitude > 1e-8f)) { bad = true; break; }   // !(x > e) also catches NaN
            if (!bad) return m;
            var v = m.vertices; var c = m.bounds.center;
            m.RecalculateNormals(); var r = m.normals;
            for (int i = 0; i < r.Length; i++)
            {
                if (n != null && i < n.Length && n[i].sqrMagnitude > 1e-8f) { r[i] = n[i]; continue; }   // keep authored normals that were fine
                if (!(r[i].sqrMagnitude > 1e-8f)) { var d = v[i] - c; r[i] = d.sqrMagnitude > 1e-10f ? d.normalized : Vector3.up; }
            }
            m.normals = r;
            return m;
        }

        static readonly HashSet<int> sanitized = new HashSet<int>();
        /// Safety net: FixNormals on every mesh under root (each mesh once).
        public static void Sanitize(GameObject root)
        {
            if (!root) return;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = mf.sharedMesh; if (!m || !sanitized.Add(m.GetInstanceID())) continue;
                FixNormals(m);
            }
        }

        // ---------------------------------------------------------------- molecules
        /// Ball-and-stick molecule. Root is centred; children "Atom*" / "Bond*". Radius ≈ size.
        public static GameObject Molecule(Mol kind, Transform parent, float size = 1f, float glow = 0.6f)
        {
            var root = Node(kind.ToString(), parent);
            Material C = SurfaceArt.Upgraded("AtomC"), O = SurfaceArt.Upgraded("AtomO"),
                H = SurfaceArt.Upgraded("AtomH"), bond = SurfaceArt.Upgraded("Connector");
            void Atom(Vector3 p, float r, Material m)
            {
                Prim(PrimitiveType.Sphere, root.transform, p * size, Vector3.one * r * size, m, false, "Atom");
            }
            void Bond(Vector3 a, Vector3 b, int order = 1)
            {
                for (int i = 0; i < order; i++)
                {
                    var offset = Vector3.Cross((b - a).normalized, Vector3.forward).normalized * ((i - (order - 1) * .5f) * .14f);
                    var g = Prim(PrimitiveType.Cylinder, root.transform, ((a + b) * .5f + offset) * size, new Vector3(order > 1 ? .08f : .12f, Vector3.Distance(a, b) * .5f, order > 1 ? .08f : .12f) * size, bond, false, "Bond");
                    g.transform.localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
                }
            }
            switch (kind)
            {
                case Mol.CO2:
                    Atom(Vector3.zero, 0.7f, C); Atom(Vector3.left * 0.75f, 0.6f, O); Atom(Vector3.right * 0.75f, 0.6f, O);
                    Bond(Vector3.zero, Vector3.left * 0.75f, 2); Bond(Vector3.zero, Vector3.right * 0.75f, 2); break;
                case Mol.H2O:
                    { Vector3 h1 = Quaternion.Euler(0, 0, 52.25f) * Vector3.up * 0.6f, h2 = Quaternion.Euler(0, 0, -52.25f) * Vector3.up * 0.6f;
                      Atom(Vector3.zero, 0.75f, O); Atom(h1, 0.45f, H); Atom(h2, 0.45f, H); Bond(Vector3.zero, h1); Bond(Vector3.zero, h2); }
                    break;
                case Mol.O2:
                    Atom(Vector3.left * 0.38f, 0.65f, O); Atom(Vector3.right * 0.38f, 0.65f, O);
                    Bond(Vector3.left * .38f, Vector3.right * .38f, 2); break;
                case Mol.Glucose:
                    {
                        var geometry = GlucoseGeometry.Data;
                        foreach (var atom in geometry.atoms) Atom(atom.position, atom.size, atom.element == "C" ? C : atom.element == "O" ? O : H);
                        foreach (var link in geometry.bonds) Bond(geometry.atoms[link.a].position, geometry.atoms[link.b].position);
                        Prim(PrimitiveType.Sphere, root.transform, Vector3.zero, Vector3.one * 3.7f * size, Shared(MatKind.Additive, new Color(Palette.Glucose.r, Palette.Glucose.g, Palette.Glucose.b, 0.045f)), false, "Halo");
                    }
                    break;
            }
            return root;
        }

        // ---------------------------------------------------------------- particles
        static Texture2D dot;
        /// Soft round sprite for particles / glows.
        public static Texture2D SoftDot
        {
            get
            {
                if (dot) return dot;
                dot = new Texture2D(64, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "SoftDot" };
                for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)) / 32f;
                    float a = Mathf.Clamp01(1 - d); a = a * a * (3 - 2 * a);
                    dot.SetPixel(x, y, new Color(1, 1, 1, a));
                }
                dot.Apply(); return dot;
            }
        }

        public class PCfg
        {
            public Color color = Palette.Gold; public Color? color2; public float size = 0.08f, sizeVar = 0.5f, life = 3f, speed = 0.3f, rate = 30f, radius = 1f;
            public int max = 400; public bool additive = true, world = true, loop = true, burst = false; public ParticleSystemShapeType shape = ParticleSystemShapeType.Sphere;
            public float noise = 0.3f, gravity = 0f; public Vector3 velocity;
        }

        public static ParticleSystem Particles(string name, Transform parent, PCfg c)
        {
            var g = Node(name, parent); g.SetActive(false);
            var ps = g.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = c.loop; main.startLifetime = new ParticleSystem.MinMaxCurve(c.life * 0.6f, c.life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(c.speed * 0.4f, c.speed); main.startSize = new ParticleSystem.MinMaxCurve(c.size * (1 - c.sizeVar), c.size);
            main.startColor = c.color2.HasValue ? new ParticleSystem.MinMaxGradient(c.color, c.color2.Value) : new ParticleSystem.MinMaxGradient(c.color);
            main.maxParticles = c.max; main.simulationSpace = c.world ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            main.gravityModifier = c.gravity; main.playOnAwake = true; main.duration = c.burst ? 0.2f : 5f;
            var em = ps.emission; em.rateOverTime = c.burst ? 0 : c.rate;
            if (c.burst) em.SetBursts(new[] { new ParticleSystem.Burst(0, (short)Mathf.Min(c.max, c.rate)) });
            var sh = ps.shape; sh.shapeType = c.shape; sh.radius = c.radius;
            if (c.velocity != Vector3.zero) { var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.World; v.x = c.velocity.x; v.y = c.velocity.y; v.z = c.velocity.z; }
            if (c.noise > 0) { var n = ps.noise; n.enabled = true; n.strength = c.noise; n.frequency = 0.4f; n.scrollSpeed = 0.2f; n.quality = ParticleSystemNoiseQuality.Low; }
            var col = ps.colorOverLifetime; col.enabled = true;
            var gr = new Gradient(); gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.2f), new GradientAlphaKey(1, 0.7f), new GradientAlphaKey(0, 1) });
            col.color = gr;
            var r = g.GetComponent<ParticleSystemRenderer>();
            var m = Mat(c.additive ? MatKind.ParticleAdditive : MatKind.ParticleAlpha, Color.white); SetTexture(m, SoftDot);
            r.sharedMaterial = m; r.renderMode = ParticleSystemRenderMode.Billboard; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            g.SetActive(true);
            return ps;
        }

        /// One-shot sparkle burst (unlock rewards, molecule arrivals). Self-destroys.
        public static void Burst(Vector3 worldPos, Color color, int count = 40, float size = 0.08f, float speed = 2f, float life = 1.2f)
        {
            var ps = Particles("Burst", null, new PCfg { color = color, color2 = Color.white, size = size, speed = speed, life = life, rate = count, burst = true, loop = false, radius = 0.05f, noise = 0.5f });
            ps.transform.position = worldPos; ps.Play();
            UnityEngine.Object.Destroy(ps.GetComponent<ParticleSystemRenderer>().sharedMaterial, life + 0.5f);
            UnityEngine.Object.Destroy(ps.gameObject, life + 0.5f);
        }

        // ---------------------------------------------------------------- tweens
        public static float Eval(float t, Ease e)
        {
            t = Mathf.Clamp01(t);
            switch (e)
            {
                case Ease.In: return t * t * t;
                case Ease.Out: return 1 - Mathf.Pow(1 - t, 3);
                case Ease.InOut: return t < 0.5f ? 4 * t * t * t : 1 - Mathf.Pow(-2 * t + 2, 3) / 2;
                case Ease.Back: { const float c1 = 1.70158f, c3 = c1 + 1; return 1 + c3 * Mathf.Pow(t - 1, 3) + c1 * Mathf.Pow(t - 1, 2); }
                case Ease.Elastic: return t == 0 || t == 1 ? t : Mathf.Pow(2, -10 * t) * Mathf.Sin((t * 10 - 0.75f) * (2 * Mathf.PI / 3)) + 1;
                default: return t;
            }
        }

        /// Drive f(0..1) over dur seconds. Skippable via Module.Skipping (snaps to 1).
        public static IEnumerator Tween(float dur, Action<float> f, Ease e = Ease.InOut, bool unscaled = false)
        {
            if (dur <= 0f) { f(1); yield break; }
            for (float t = 0; t < dur; t += unscaled ? Time.unscaledDeltaTime : Time.deltaTime)
            {
                if (Module.Skipping) break;
                f(Eval(t / dur, e)); yield return null;
            }
            f(1);
        }

        /// Wait that respects skip (fast-forward) mode.
        public static IEnumerator Wait(float s)
        {
            for (float t = 0; t < s && !Module.Skipping; t += Time.deltaTime) yield return null;
        }

        public static IEnumerator All(MonoBehaviour host, params IEnumerator[] routines)
        {
            int left = routines.Length;
            foreach (var r in routines) host.StartCoroutine(Track(r, () => left--));
            while (left > 0) yield return null;
        }
        static IEnumerator Track(IEnumerator r, Action done) { yield return r; done(); }

        public static void SetLayerRecursive(GameObject g, int layer) { foreach (var t in g.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer; }
    }

    /// Moves a set of glowing beads along a polyline forever (xylem/phloem flows, CO₂ streams…).
    /// One instanced draw per stream: no per-bead GameObjects (the working plant has ~53 streams and several hundred beads).
    public class Beads : MonoBehaviour
    {
        public List<Vector3> path = new List<Vector3>(); // local space of this transform
        public float speed = 0.25f;                       // path fraction per second
        public bool fadeEnds = true;
        float[] phase, cum; Matrix4x4[] mats; Vector3 baseScale; Mesh mesh; RenderParams rp;
        static Mesh sphere;

        public static Beads Create(string name, Transform parent, IList<Vector3> localPath, int count, float size, Material m, float speed, Mesh mesh = null)
        {
            var g = Fx.Node(name, parent); var b = g.AddComponent<Beads>();
            b.path.AddRange(localPath); b.speed = speed; b.baseScale = Vector3.one * size;
            if (!mesh && !sphere) { var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere); sphere = tmp.GetComponent<MeshFilter>().sharedMesh; Destroy(tmp); }
            b.mesh = mesh ? mesh : sphere;
            if (!m.enableInstancing) m.enableInstancing = true;   // templates already have it; Fx.Mat copies it
            b.rp = new RenderParams(m)
            {
                shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = true,
                reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.BlendProbes   // the chamber has a realtime probe
            };
            b.phase = new float[count]; b.mats = new Matrix4x4[count];
            for (int i = 0; i < count; i++) b.phase[i] = i / (float)count;
            b.cum = new float[b.path.Count];
            for (int i = 1; i < b.path.Count; i++) b.cum[i] = b.cum[i - 1] + Vector3.Distance(b.path[i - 1], b.path[i]);
            return b;
        }

        // LateUpdate: after HeroPlant sway/spin and the micro-world spinners have posed the parents this frame.
        // Inactive streams (SetActive(false)) do not run this, so they draw nothing.
        void LateUpdate()
        {
            int n = path.Count; if (n < 2 || mats == null || cum.Length != n) return;
            float total = cum[n - 1]; if (total <= 0f) return;
            Matrix4x4 l2w = transform.localToWorldMatrix;
            Vector3 lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), hi = -lo;
            float tt = Time.time * speed; int live = 0;
            for (int i = 0; i < mats.Length; i++)
            {
                float t = Mathf.Repeat(phase[i] + tt, 1f), s = fadeEnds ? Mathf.Clamp01(Mathf.Min(t, 1f - t) * 10f) : 1f;
                if (s < 0.01f) continue;   // invisible at the ends anyway; a zero-scale matrix has no inverse (NaN normals → bloom disc)
                float d = t * total; int k = System.Array.BinarySearch(cum, d); if (k < 0) k = ~k; k = Mathf.Clamp(k, 1, n - 1);
                Vector3 p = Vector3.Lerp(path[k - 1], path[k], Mathf.InverseLerp(cum[k - 1], cum[k], d));   // = Fx.Along (arc length clamped to the path)
                var w = l2w * Matrix4x4.TRS(p, Quaternion.identity, baseScale * s);
                mats[live++] = w;
                var wp = new Vector3(w.m03, w.m13, w.m23); lo = Vector3.Min(lo, wp); hi = Vector3.Max(hi, wp);
            }
            if (live == 0) return;
            Vector3 ls = transform.lossyScale;
            float r = baseScale.x * Mathf.Max(Mathf.Abs(ls.x), Mathf.Max(Mathf.Abs(ls.y), Mathf.Abs(ls.z)));
            rp.worldBounds = new Bounds((lo + hi) * 0.5f, hi - lo + Vector3.one * 2f * r);
            rp.layer = gameObject.layer;
            Graphics.RenderMeshInstanced(rp, mesh, 0, mats, live);
        }
    }
}
