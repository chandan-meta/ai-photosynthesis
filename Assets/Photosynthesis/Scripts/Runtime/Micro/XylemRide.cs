using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using K = Photosynthesis.MicroKitA;
using Object = UnityEngine.Object;

namespace Photosynthesis
{
    /// Clue 02 ride: the learner travels WITH the water up a translucent xylem vessel ROOT → STEM → BRANCH → LEAF.
    /// ≈285 m spline climbing at ≤60° (keeps the ride camera's LookRotation stable), a branch junction where the main
    /// stem vessel carries on upward, then a flat midrib that opens into a glowing vein network under a ceiling of
    /// palisade cells. Inside-out translucent vessel with a double lignin spiral + perforation rims; tissue cells,
    /// streaks and parallel vessels slide past; fog/ambient/sun/wall colour change per section; FOV and shake follow speed.
    public class XylemRide : MicroWorld
    {
        static readonly Vector3[] Ctrl =
        {
            new Vector3(0f, 0f, 0f), new Vector3(1f, 1.5f, 16f), new Vector3(3f, 7f, 30f), new Vector3(6f, 19f, 41f),           // root      C0–C3
            new Vector3(11f, 40f, 54f), new Vector3(8f, 66f, 69f), new Vector3(-2f, 92f, 80f), new Vector3(-10f, 116f, 93f),    // stem
            new Vector3(-12f, 136f, 108f),                                                                                  // node: branch junction (C8)
            new Vector3(-6f, 150f, 126f), new Vector3(4f, 158f, 144f), new Vector3(16f, 162f, 158f),                        // branch → leaf entry (C11)
            new Vector3(26f, 163.5f, 172f), new Vector3(33f, 164f, 184f)                                                     // midrib
        };
        const int PerSeg = 30;
        const float RideTime = 14f, ArriveTime = 2.2f, LeafY = 163f, StopShort = 4f;   // stop short of the vein openings + molecule clamp at the vessel end
        static readonly string[] Stages = { "ROOT", "STEM", "BRANCH", "LEAF" };
        static readonly int[] StageCtrl = { 0, 3, 8, 11 };
        static readonly float[] StageTime = { 0f, 0.235f, 0.585f, 0.78f };   // slow root, fast stem, easing into the leaf
        static readonly float[] NeighbourAng = { 0.6f, 2.7f, 4.6f };

        // per-section looks: root · stem · branch · leaf
        static readonly Color[] FogC = { new Color(0.24f, 0.15f, 0.08f), new Color(0.1f, 0.3f, 0.24f), new Color(0.18f, 0.4f, 0.3f), new Color(0.4f, 0.58f, 0.33f) };
        static readonly float[] FogD = { 0.03f, 0.022f, 0.02f, 0.012f };
        static readonly Color[] SkyC = { new Color(0.55f, 0.4f, 0.28f), new Color(0.35f, 0.6f, 0.5f), new Color(0.45f, 0.7f, 0.5f), new Color(0.6f, 0.74f, 0.47f) };
        static readonly Color[] SunC = { new Color(1f, 0.75f, 0.5f), new Color(0.85f, 1f, 0.8f), new Color(0.95f, 1f, 0.8f), new Color(1f, 0.93f, 0.72f) };
        static readonly float[] SunI = { 0.5f, 1f, 1.4f, 1.6f };
        static readonly Color[] WallC = { new Color(0.62f, 0.44f, 0.26f), new Color(0.3f, 0.66f, 0.46f), new Color(0.32f, 0.72f, 0.5f), new Color(0.5f, 0.9f, 0.45f) };
        static readonly Color[] LampC = { new Color(1f, 0.8f, 0.6f), new Color(0.7f, 1f, 0.85f), new Color(0.75f, 1f, 0.85f), new Color(1f, 1f, 0.85f) };
        static readonly float[] Pitch = { 1.6f, 2.8f, 2.3f, 1.8f };

        readonly List<Object> owned = new List<Object>();
        readonly float[] stageS = new float[4];
        bool ready, arriving; int rideGen;
        float progress, arriveT0, L, rideL;
        K.Path path; List<Vector3> world, ride; AnimationCurve curve;
        Vector3 leafFwd, leafSide, arrivePos, arriveLook;
        Material wallMat; Light lamp; GameObject sunGlow; Transform camT;
        Transform[] mols; float[] md, mr, mth, mw, mf, mph; Vector3[] from, to;
        K.SunOverride sun;

        T Own<T>(T o) where T : Object { owned.Add(o); return o; }
        void OnDestroy() { foreach (var o in owned) if (o) Destroy(o); }
        void EnsureBuilt() { if (!ready) Build(); }

        // ================================================================== contract
        protected override void Build()
        {
            if (ready) return;
            ready = true;
            sun = gameObject.AddComponent<K.SunOverride>();
            var pts = Fx.Smooth(Ctrl, (Ctrl.Length - 1) * PerSeg + 1);
            path = new K.Path(pts); L = path.Length;
            for (int i = 0; i < 4; i++) stageS[i] = path.S(StageCtrl[i] * PerSeg);
            world = new List<Vector3>(pts.Count);
            foreach (var p in pts) world.Add(transform.TransformPoint(p));
            int n = world.Count; while (n > 2 && path.S(n - 1) > L - StopShort) n--;
            ride = world.GetRange(0, n); rideL = path.S(n - 1);   // the camera's path ends ≈ L−4.0…4.5 (≈0.46 m per point here); molecules + arrival keep the full vessel
            leafFwd = Vector3.ProjectOnPlane(Ctrl[13] - Ctrl[11], Vector3.up).normalized; leafSide = Vector3.Cross(Vector3.up, leafFwd);
            var end = pts[pts.Count - 1];
            arrivePos = end - leafFwd * 3.5f + Vector3.up * 4.6f; arriveLook = end + leafFwd * 12f + Vector3.down * 2.2f;

            BuildCurve(); BuildVessel(); BuildTissue(); BuildNeighbours(); BuildLeaf(); BuildFlow();
            lamp = Fx.Node("Ride Light", transform).AddComponent<Light>();
            lamp.type = LightType.Point; lamp.range = 11f; lamp.intensity = 2.4f; lamp.shadows = LightShadows.None;
            var dome = Fx.MeshObj("Backdrop", Own(K.Sphere(24, 16, true)), Own(Fx.Mat(MatKind.Unlit, Color.black)), transform);
            dome.transform.localPosition = new Vector3(10f, 90f, 95f); dome.transform.localScale = Vector3.one * 420f;
            ResetWorld();
        }

        protected override void ApplyMood()
        {
            float yaw = Mathf.Atan2(leafFwd.x, leafFwd.z) * Mathf.Rad2Deg;
            sun.Apply(SunI[0], SunC[0], new Vector3(62f, yaw + 25f, 0f));
            ApplySection(arriving ? L : progress * L);
        }

        public override (Vector3 pos, Vector3 look) Pose() => ready ? (world[0], transform.TransformPoint(path.At(9f))) : base.Pose();

        public override void ResetWorld()
        {
            if (!ready) return;
            progress = 0f; arriving = false;
            Fx.SetAlpha(wallMat, 1f); sunGlow.SetActive(false);
            foreach (var m in mols) m.localScale = Vector3.one;
            Flow(0f, 0f);
        }

        /// Drives the camera through the vessel. `stage` fires "ROOT","STEM","BRANCH","LEAF" as each is reached.
        public IEnumerator Ride(CameraDirector cam, Action<string> stage)
        {
            EnsureBuilt();
            if (!cam) yield break;
            ResetWorld(); camT = cam.transform; int my = ++rideGen;   // a superseded ride touches nothing
            int fired = 0; float lastU = 0f, speed = 0f, fov = cam.Cam.fieldOfView, t0 = Time.time;
            void OnProgress(float u)
            {
                if (!isActiveAndEnabled || my != rideGen) return;   // world hidden mid-ride (GoTo elsewhere): no FOV/shake/stage callbacks toward a hidden world
                float s = u * rideL; progress = s / L;   // u runs along the shorter camera path; s is the true arc length on the vessel
                while (fired < 4 && s >= stageS[fired] - 0.01f)
                {
                    if (fired == 3) sunGlow.SetActive(true);
                    stage?.Invoke(Stages[fired++]);
                }
                float dt = Mathf.Max(Time.deltaTime, 1e-4f);
                speed = Mathf.Lerp(speed, Mathf.Abs(u - lastU) * rideL / dt, Mathf.Clamp01(dt * 4f)); lastU = u;
                float k = Mathf.Clamp01(speed / 32f);
                fov = Mathf.Lerp(fov, 60f + 16f * k, Mathf.Clamp01(dt * 2.5f)); if (!Hud.ReduceMotion) cam.Cam.fieldOfView = fov;   // speed widens the lens (Reduce motion: Ride's steady 66°)
                cam.Shake(0.006f + 0.03f * k, 0.12f);
                Flow(s, Time.time - t0);
            }
            yield return cam.Ride(ride, RideTime, 6f, curve, 0.03f, OnProgress, 66f);
            if (!isActiveAndEnabled || my != rideGen) yield break;   // ride was cut short by a world change: never blend toward the hidden world

            // arrival: the vessel walls dissolve, water spreads into the leaf, the camera rises to reveal the vein network
            cam.Shake(0f, 0f);
            for (int i = 0; i < mols.Length; i++) from[i] = mols[i].localPosition;
            arriving = true; arriveT0 = Time.time;
            yield return cam.Blend(transform.TransformPoint(arrivePos), transform.TransformPoint(arriveLook), ArriveTime, 58f);
            arriveT0 = Time.time - ArriveTime;   // snap (skip-safe)
            yield return Fx.Wait(Module.I && Module.I.story.AutoPlay ? 0.3f : 0.8f);   // the settled vista holds a beat before the card (AutoPlay keeps 0.3 s)
        }

        // ================================================================== per frame
        void LateUpdate()
        {
            if (!camT) { var cd = Module.I ? Module.I.cam : null; if (cd) camT = cd.transform; }
            if (arriving) Arrive(Mathf.Clamp01((Time.time - arriveT0) / ArriveTime));
            ApplySection(arriving ? L : progress * L);
            if (camT) lamp.transform.position = camT.position + camT.forward * 1.2f;
        }

        void ApplySection(float s)
        {
            var sky = Sect(SkyC, s);
            K.Mood(Sect(FogC, s), Sect(FogD, s), sky, sky * 0.6f, sky * 0.25f);
            sun.Set(Sect(SunI, s), Sect(SunC, s));
            lamp.color = Sect(LampC, s); lamp.intensity = Mathf.Lerp(2.4f, 1.2f, K.Smooth01((s - stageS[3] + 8f) / 16f));
        }

        /// Water molecules ride with the camera: each keeps a drifting lead distance and swirls around the vessel axis.
        /// Past the vessel end they run on down the midrib, always ahead of the lens (no pile-up for the arriving camera to fly into).
        void Flow(float sCam, float t)
        {
            for (int i = 0; i < mols.Length; i++)
            {
                float s = sCam + md[i] + 1.6f * Mathf.Sin(t * mf[i] + mph[i]);
                path.Frame(s, out var p, out var tan, out var n, out var b);   // Seg() clamps s to L: past the end this is the last point + last-segment tangent
                p += tan * Mathf.Max(0f, s - L);   // the path's own end tangent: no kink at s = L
                float a = mth[i] + t * mw[i], r = mr[i] * Mathf.Min(1f, R(s) / 2.2f);
                mols[i].localPosition = p + (Mathf.Cos(a) * n + Mathf.Sin(a) * b) * r;
                mols[i].localRotation = Quaternion.Euler(t * 60f + i * 30f, t * 45f + i * 50f, 0f);
            }
        }

        void Arrive(float k)
        {
            float e = Fx.Eval(k, Ease.Out);
            for (int i = 0; i < mols.Length; i++) { mols[i].localPosition = Vector3.Lerp(from[i], to[i], e); mols[i].localScale = Vector3.one * Mathf.Lerp(1f, 0.7f, e); }
            Fx.SetAlpha(wallMat, Mathf.Lerp(1f, 0.25f, e));
        }

        // ================================================================== sections
        void Weights(float s, out float stem, out float branch, out float leaf)
        {
            stem = K.Smooth01((s - stageS[1] + 10f) / 20f);
            branch = K.Smooth01((s - stageS[2] + 8f) / 16f);
            leaf = K.Smooth01((s - stageS[3] + 8f) / 16f);
        }
        Color Sect(Color[] k, float s) { Weights(s, out var a, out var b, out var c); return Color.Lerp(Color.Lerp(Color.Lerp(k[0], k[1], a), k[2], b), k[3], c); }
        float Sect(float[] k, float s) { Weights(s, out var a, out var b, out var c); return Mathf.Lerp(Mathf.Lerp(Mathf.Lerp(k[0], k[1], a), k[2], b), k[3], c); }
        int Section(float s) => s < stageS[1] ? 0 : s < stageS[2] ? 1 : s < stageS[3] ? 2 : 3;

        /// Vessel radius: root 2.0 → stem 2.4 → branch 2.0 → leaf 1.7 tapering to 1.25 at the vein ending.
        float R(float s)
        {
            Weights(s, out var a, out var b, out var c);
            return Mathf.Lerp(Mathf.Lerp(Mathf.Lerp(2f, 2.4f, a), 2f, b), 1.7f, c) - 0.45f * Mathf.Clamp01((s - stageS[3]) / Mathf.Max(1f, L - stageS[3]));
        }

        // ================================================================== construction
        void BuildCurve()
        {
            var tt = new[] { 0f, StageTime[1], StageTime[2], StageTime[3], 1f };
            var vv = new[] { 0f, stageS[1] / rideL, stageS[2] / rideL, stageS[3] / rideL, 1f };   // stage timing unchanged on the shorter camera path
            var m = new float[4]; for (int i = 0; i < 4; i++) m[i] = (vv[i + 1] - vv[i]) / (tt[i + 1] - tt[i]);
            var tan = new[] { m[0] * 0.3f, (m[0] + m[1]) * 0.5f, (m[1] + m[2]) * 0.5f, (m[2] + m[3]) * 0.5f, 0f };   // monotone: no backward drift
            var keys = new Keyframe[5]; for (int i = 0; i < 5; i++) keys[i] = new Keyframe(tt[i], vv[i], tan[i], tan[i]);
            curve = new AnimationCurve(keys);
        }

        void BuildVessel()
        {
            var pts = path.pts;
            var wallTex = Own(K.Tex(32, 256, (u, v) =>
            {   // section colour along the length, longitudinal striations around (periodic in u → seamless)
                float a = u * Mathf.PI * 2f, st = 0.82f + 0.1f * Mathf.Sin(a * 7f + Mathf.Sin(a * 3f) * 2f) + 0.08f * Mathf.Sin(a * 17f);
                var c = Sect(WallC, v * L) * st; c.a = 0.3f * st; return c;
            }));
            wallMat = Own(Fx.Mat(MatKind.LitTransparent, Color.white, new Color(1.2f, 1.2f, 1.2f), 0.65f));
            Fx.SetTexture(wallMat, wallTex); wallMat.SetTexture("_EmissionMap", wallTex);
            Fx.MeshObj("Xylem Vessel", Own(K.Tube(pts, i => R(path.S(i)), 24, true, i => path.S(i) / L)), wallMat, transform);

            // lignin: double spiral thickening + perforation-plate rims between vessel elements
            var bandTex = Own(K.Tex(4, 256, (u, v) => Color.Lerp(Sect(WallC, v * L), Color.white, 0.55f)));
            var bandMat = Own(Fx.Mat(MatKind.LitEmissive, Color.white, new Color(0.7f, 0.7f, 0.7f), 0.6f));
            Fx.SetTexture(bandMat, bandTex); bandMat.SetTexture("_EmissionMap", bandTex);
            var parts = new List<CombineInstance>(); var temp = new List<Mesh>();
            for (int h = 0; h < 2; h++)
            {
                var hp = new List<Vector3>(); var hv = new List<float>();
                for (float s = 0.5f, th = h * Mathf.PI; s < L - 0.5f; s += Sect(Pitch, s) / 14f, th += Mathf.PI * 2f / 14f)
                {
                    path.Frame(s, out var p, out _, out var n, out var b);
                    hp.Add(p + (Mathf.Cos(th) * n + Mathf.Sin(th) * b) * (R(s) - 0.1f)); hv.Add(s / L);
                }
                var mesh = K.Tube(hp, _ => 0.085f, 6, false, i => hv[i]); temp.Add(mesh);
                parts.Add(K.Part(mesh, Vector3.zero, Quaternion.identity, Vector3.one));
            }
            Fx.MeshObj("Lignin Spiral", Own(K.Merge(parts, "Lignin Spiral")), bandMat, transform);
            parts.Clear();
            for (float s = 4f; s < L - 2f; s += 8.5f)
            {
                path.Frame(s, out var p, out _, out var n, out var b);
                var circle = new Vector3[29]; float r = R(s) - 0.05f;
                for (int i = 0; i <= 28; i++) { float a = Mathf.PI * 2f * (i % 28) / 28f; circle[i] = p + (Mathf.Cos(a) * n + Mathf.Sin(a) * b) * r; }
                var mesh = K.Tube(circle, _ => 0.15f, 8, false, null, 1f, 1f, null, true); temp.Add(mesh);
                parts.Add(K.Part(mesh, Vector3.zero, Quaternion.identity, Vector3.one));
            }
            Fx.MeshObj("Perforation Rims", Own(K.Merge(parts, "Perforation Rims")),
                Own(Fx.Mat(MatKind.LitEmissive, new Color(0.75f, 0.95f, 1f), Palette.Water * 0.9f, 0.7f)), transform);
            foreach (var m in temp) Destroy(m);

            // light streaks near the wall (static — the camera's speed makes them stream past)
            var rng = new System.Random(5); var v3 = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            void Quad(Vector3 c, Vector3 half, Vector3 side)
            {
                int o = v3.Count;
                v3.Add(c - half - side); v3.Add(c - half + side); v3.Add(c + half - side); v3.Add(c + half + side);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(0, 1)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1));
                tri.Add(o); tri.Add(o + 1); tri.Add(o + 2); tri.Add(o + 1); tri.Add(o + 3); tri.Add(o + 2);
            }
            for (int i = 0; i < 260; i++)
            {
                float s = K.Rand(rng, 2f, L - 2f), a = K.Rand(rng, 0f, Mathf.PI * 2f), len = K.Rand(rng, 1.2f, 3.6f), w = K.Rand(rng, 0.04f, 0.08f);
                path.Frame(s, out var p, out var t, out var n, out var b);
                var d = Mathf.Cos(a) * n + Mathf.Sin(a) * b; var c = p + d * R(s) * K.Rand(rng, 0.45f, 0.9f);
                Quad(c, t * len * 0.5f, Vector3.Cross(t, d) * w); Quad(c, t * len * 0.5f, d * w);
            }
            var nr = new Vector3[v3.Count]; for (int i = 0; i < nr.Length; i++) nr[i] = Vector3.up;
            var streakMat = Own(Fx.Mat(MatKind.Additive, new Color(0.6f * 1.6f, 0.9f * 1.6f, 1.6f, 0.7f))); Fx.SetTexture(streakMat, K.StreakTex);
            Fx.MeshObj("Light Streaks", Own(K.Build("Streaks", v3.ToArray(), nr, uv.ToArray(), tri.ToArray())), streakMat, transform);
        }

        static float SegDist(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a; float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector3.Distance(p, a + ab * t);
        }

        bool NearNeighbour(float s, float a)
        {
            if (s < stageS[1] - 5f || s > stageS[2] - 8f) return false;
            foreach (var na in NeighbourAng) if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, na * Mathf.Rad2Deg)) < 32f) return true;
            return false;
        }

        static void Dots(List<CombineInstance> list, Mesh octa, System.Random r, Vector3 pos, Quaternion rot, Vector3 half, int count)
        {
            for (int k = 0; k < count; k++)
            {
                var u = K.OnSphere(r); var n = (rot * new Vector3(u.x / half.x, u.y / half.y, u.z / half.z)).normalized;
                list.Add(K.Part(octa, pos + rot * Vector3.Scale(half, u) * 0.96f, Quaternion.LookRotation(n), new Vector3(0.24f, 0.18f, 0.1f)));
            }
        }

        // ponytail: tissue cells are opaque + emissive so they always sort correctly behind the translucent vessel wall;
        // upgrade: chunked transparent meshes if true see-through cells are wanted.
        void BuildTissue()
        {
            var r = new System.Random(17);
            var unit = Own(K.Sphere(12, 8)); var octa = Own(K.Octa());
            var lists = new List<CombineInstance>[5]; for (int i = 0; i < 5; i++) lists[i] = new List<CombineInstance>();   // root, stem, branch, sheath, fibre
            var dots = new List<CombineInstance>();
            for (float s = 0.5f; s < L; s += 1.7f)
                for (int k = 0; k < 5; k++)
                {
                    float ss = Mathf.Clamp(s + K.Rand(r, -0.8f, 0.8f), 0f, L), a = K.Rand(r, 0f, Mathf.PI * 2f);
                    int sec = Section(ss + K.Rand(r, -8f, 8f));
                    if (sec == 1 && NearNeighbour(ss, a)) continue;
                    path.Frame(ss, out var p, out var t, out var n, out var b);
                    var d = Mathf.Cos(a) * n + Mathf.Sin(a) * b; Vector3 size; float rad; int list = sec;
                    if (sec == 3) { size = new Vector3(K.Rand(r, 1.1f, 1.5f), K.Rand(r, 0.9f, 1.2f), K.Rand(r, 1.6f, 2.4f)); rad = R(ss) + 0.35f + size.y * 0.5f + K.Rand(r, 0f, 0.4f); }   // bundle sheath
                    else if (sec == 1 && r.NextDouble() < 0.22) { size = new Vector3(K.Rand(r, 0.5f, 0.7f), K.Rand(r, 0.5f, 0.7f), K.Rand(r, 6f, 9f)); rad = R(ss) + 0.35f + size.y * 0.5f + K.Rand(r, 0f, 2.4f); list = 4; }   // fibres
                    else { size = new Vector3(K.Rand(r, 0.9f, 1.7f), K.Rand(r, 0.8f, 1.4f), K.Rand(r, 2.2f, 4.2f)); rad = R(ss) + 0.35f + size.y * 0.5f + K.Rand(r, 0f, 2.6f); }
                    var pos = p + d * rad; var rot = Quaternion.LookRotation(t, d);
                    if (Mathf.Min(SegDist(pos, path.pts[path.pts.Count - 1], arrivePos), SegDist(pos, path.At(rideL), arrivePos)) < Mathf.Max(size.x, Mathf.Max(size.y, size.z)) * 0.5f + 0.8f) continue;   // the arrival rise (from the ride's end) never clips a cell; the old exclusion is kept
                    lists[list].Add(K.Part(unit, pos, rot, size * 0.5f));
                    if (sec == 3) Dots(dots, octa, r, pos, rot, size * 0.5f, 4);
                }
            BuildLeafLayers(r, unit, octa, lists[3], dots);
            var mats = new[]
            {
                Fx.Mat(MatKind.LitEmissive, new Color(0.66f, 0.47f, 0.3f), new Color(0.22f, 0.12f, 0.05f), 0.4f),   // root cortex
                Fx.Mat(MatKind.LitEmissive, new Color(0.32f, 0.66f, 0.42f), new Color(0.06f, 0.2f, 0.1f), 0.45f),   // stem parenchyma
                Fx.Mat(MatKind.LitEmissive, new Color(0.36f, 0.7f, 0.4f), new Color(0.08f, 0.24f, 0.1f), 0.45f),    // branch
                Fx.Mat(MatKind.LitEmissive, new Color(0.42f, 0.8f, 0.38f), new Color(0.1f, 0.3f, 0.08f), 0.45f),    // leaf mesophyll
                Fx.Mat(MatKind.LitEmissive, new Color(0.85f, 0.82f, 0.62f), new Color(0.15f, 0.14f, 0.08f), 0.5f)   // fibres
            };
            var names = new[] { "Root Cortex Cells", "Stem Cells", "Branch Cells", "Leaf Cells", "Fibres" };
            for (int i = 0; i < 5; i++) Fx.MeshObj(names[i], Own(K.Merge(lists[i], names[i])), Own(mats[i]), transform);
            Fx.MeshObj("Chloroplasts", Own(K.Merge(dots, "Chloroplasts")), Own(Fx.Mat(MatKind.LitEmissive, new Color(0.12f, 0.55f, 0.15f), new Color(0.08f, 0.5f, 0.12f))), transform);
        }

        /// Leaf blade around the end of the ride: a ceiling of palisade columns and a floor of spongy cells, both chloroplast-dotted.
        void BuildLeafLayers(System.Random r, Mesh unit, Mesh octa, List<CombineInstance> cells, List<CombineInstance> dots)
        {
            var o = Ctrl[11];
            bool Inside(float a, float c) => (a - 25f) * (a - 25f) / (30f * 30f) + c * c / (26f * 26f) < 1f;   // leaf outline
            for (float a = -2f; a <= 52f; a += 2.9f)
                for (float c = -24f; c <= 24f; c += 2.9f)
                {
                    float ja = a + K.Rand(r, -0.7f, 0.7f), jc = c + K.Rand(r, -0.7f, 0.7f); if (!Inside(ja, jc)) continue;
                    float h = K.Rand(r, 6f, 7.5f), rad = K.Rand(r, 1f, 1.25f);
                    var pos = o + leafFwd * ja + leafSide * jc; pos.y = LeafY + 6.5f + h * 0.5f;
                    var rot = Quaternion.Euler(0f, K.Rand(r, 0f, 360f), 0f); var half = new Vector3(rad, h * 0.5f, rad);
                    cells.Add(K.Part(unit, pos, rot, half)); Dots(dots, octa, r, pos, rot, half, 6);
                }
            for (float a = -2f; a <= 52f; a += 3.6f)
                for (float c = -24f; c <= 24f; c += 3.6f)
                {
                    float ja = a + K.Rand(r, -1.2f, 1.2f), jc = c + K.Rand(r, -1.2f, 1.2f); if (!Inside(ja, jc)) continue;
                    float rad = K.Rand(r, 1f, 1.6f);
                    var pos = o + leafFwd * ja + leafSide * jc; pos.y = LeafY - K.Rand(r, 3.5f, 8f);
                    var rot = Quaternion.Euler(K.Rand(r, 0f, 360f), K.Rand(r, 0f, 360f), 0f); var half = new Vector3(rad, rad * K.Rand(r, 0.7f, 1f), rad * K.Rand(r, 0.8f, 1.2f));
                    cells.Add(K.Part(unit, pos, rot, half)); Dots(dots, octa, r, pos, rot, half, 4);
                }
        }

        void BuildNeighbours()
        {
            var beadMat = Own(Fx.Mat(MatKind.Additive, new Color(Palette.Water.r * 2.6f, Palette.Water.g * 2.6f, Palette.Water.b * 2.6f, 1f)));
            float s0 = stageS[1] - 5f, s1 = stageS[2] - 8f;
            foreach (var a in NeighbourAng)
            {   // parallel vessels of the same vascular bundle, water flowing up inside
                var pts = new List<Vector3>();
                for (float s = s0; s <= s1; s += 2f) { path.Frame(s, out var p, out _, out var n, out var b); pts.Add(p + (Mathf.Cos(a) * n + Mathf.Sin(a) * b) * 6f); }
                int c = pts.Count;
                Fx.MeshObj("Neighbour Vessel", Own(K.Tube(pts, i => 0.05f + 1.7f * K.Smooth01(i / 3f) * K.Smooth01((c - 1 - i) / 3f), 16, false, i => (s0 + 2f * i) / L)), wallMat, transform);
                Beads.Create("Neighbour Flow", transform, pts, 14, 0.45f, beadMat, 0.09f);
            }
            // at the node the main stem vessel carries on upward while the ride turns into the branch
            var tanStem = (Ctrl[8] - Ctrl[7]).normalized; var away = -Vector3.ProjectOnPlane(Ctrl[9] - Ctrl[8], tanStem).normalized;
            var d0 = Ctrl[8] + away * 1.2f + tanStem * 1.5f;
            var cpts = Fx.Smooth(new[] { d0, d0 + tanStem * 14f, d0 + tanStem * 28f + new Vector3(-3f, 4f, 2f), d0 + tanStem * 44f + new Vector3(-5f, 9f, 4f), d0 + tanStem * 62f + new Vector3(-6f, 15f, 6f) }, 40);
            var cpath = new K.Path(cpts); float v = (stageS[2] - 10f) / L;
            Fx.MeshObj("Main Stem Vessel (continues)", Own(K.Tube(cpts, i => Mathf.Lerp(0.5f, 2.3f, K.Smooth01(cpath.S(i) / 10f)), 20, false, _ => v)), wallMat, transform);
            Beads.Create("Main Stem Flow", transform, cpts, 12, 0.45f, beadMat, 0.08f);
        }

        void BuildLeaf()
        {
            var r = new System.Random(29); var o = Ctrl[11]; var end = Ctrl[13];
            // midrib continues past the end of the ride; side veins fan out, tertiary veins close the network
            var veins = new List<(List<Vector3> pts, float r0, float r1)>();
            var rib = new List<Vector3>(); for (int i = 11 * PerSeg; i < path.pts.Count; i += 3) rib.Add(path.pts[i]);
            var cont = Fx.Smooth(new[] { end, end + leafFwd * 10f + leafSide * 0.6f, end + leafFwd * 20f + leafSide * 0.3f, end + leafFwd * 30f }, 16);
            veins.Add((cont, 1.25f, 0.45f)); rib.AddRange(cont);
            Vector3 RibAt(float along) { foreach (var p in rib) if (Vector3.Dot(p - o, leafFwd) >= along) return p; return rib[rib.Count - 1]; }
            var side = new List<Vector3>[8];
            for (int j = 0; j < 8; j++)
            {
                float sign = j % 2 == 0 ? 1f : -1f, len = 26f - 1.4f * j;
                var d0 = (leafFwd * 0.616f + leafSide * sign * 0.788f).normalized;   // 52° off the midrib
                var pts = new List<Vector3> { RibAt(3f + 6.5f * j) };
                for (int k = 1; k <= 10; k++) pts.Add(pts[k - 1] + Vector3.Lerp(d0, leafFwd, k / 10f * 0.45f).normalized * (len / 10f));
                side[j] = pts; veins.Add((pts, 0.85f - 0.03f * j, 0.25f));
            }
            for (int j = 0; j + 2 < 8; j++)
            {
                float sign = j % 2 == 0 ? 1f : -1f;
                Vector3 p = Fx.Along(side[j], 0.6f), q = Fx.Along(side[j + 2], 0.4f), mid = (p + q) * 0.5f + leafSide * sign * 1.5f;
                veins.Add((Fx.Smooth(new[] { p, mid, q }, 8), 0.22f, 0.22f));
                var e = Fx.Along(side[j], 0.8f);
                veins.Add((new List<Vector3> { e, e + (leafSide * sign * 0.7f + leafFwd * 0.7f).normalized * 1.8f, e + (leafSide * sign * 0.8f + leafFwd * 0.5f).normalized * 3.5f }, 0.18f, 0.1f));
            }
            var walls = new List<CombineInstance>(); var cores = new List<CombineInstance>(); var temp = new List<Mesh>();
            var beadMat = Own(Fx.Mat(MatKind.Additive, new Color(Palette.Water.r * 2.6f, Palette.Water.g * 2.6f, Palette.Water.b * 2.6f, 1f)));
            for (int i = 0; i < veins.Count; i++)
            {
                var (pts, r0, r1) = veins[i]; int n = pts.Count;
                var w = K.Tube(pts, k => Mathf.Lerp(r0, r1, k / (n - 1f)), 10); var c = K.Tube(pts, k => Mathf.Lerp(r0, r1, k / (n - 1f)) * 0.35f, 6);
                temp.Add(w); temp.Add(c);
                walls.Add(K.Part(w, Vector3.zero, Quaternion.identity, Vector3.one)); cores.Add(K.Part(c, Vector3.zero, Quaternion.identity, Vector3.one));
                if (i <= 8) Beads.Create("Vein Flow", transform, pts, i == 0 ? 5 : 3, i == 0 ? 0.4f : 0.3f, beadMat, i == 0 ? 0.15f : 0.28f);
            }
            Fx.MeshObj("Leaf Veins", Own(K.Merge(walls, "Leaf Veins")), Own(Fx.Mat(MatKind.LitTransparent, new Color(0.75f, 1f, 0.75f, 0.35f), new Color(0.1f, 0.25f, 0.12f), 0.8f)), transform);
            Fx.MeshObj("Leaf Vein Water", Own(K.Merge(cores, "Leaf Vein Water")), Own(Fx.Mat(MatKind.Additive, new Color(Palette.Water.r * 1.8f, Palette.Water.g * 1.8f, Palette.Water.b * 1.8f, 0.9f))), transform);
            foreach (var m in temp) Destroy(m);

            // sunlight pouring down through the palisade
            float yaw = Mathf.Atan2(leafFwd.x, leafFwd.z) * Mathf.Rad2Deg; var dir = Quaternion.Euler(62f, yaw + 25f, 0f) * Vector3.forward;
            var shaftMat = Own(Fx.Mat(MatKind.Additive, new Color(1f, 0.9f, 0.6f, 0.1f))); Fx.SetTexture(shaftMat, K.ShaftTex);
            for (int k = 0; k < 6; k++)
            {
                var target = o + leafFwd * (6f + 6f * k) + leafSide * ((k % 2 == 0 ? 1f : -1f) * (4f + 1.5f * k)); target.y = LeafY - 2f;
                K.Shaft(transform, target - dir * 20f, dir, 22f, 2.4f, shaftMat);
            }
            sunGlow = K.Glow(transform, end + leafFwd * 70f + Vector3.up * 25f, 60f, K.GlowMat(new Color(Palette.Light.r * 1.3f, Palette.Light.g * 1.3f, Palette.Light.b * 1.3f, 0.8f)));
        }

        void BuildFlow()
        {
            var r = new System.Random(31); int n = 14; var end = Ctrl[13];
            mols = new Transform[n]; md = new float[n]; mr = new float[n]; mth = new float[n]; mw = new float[n]; mf = new float[n]; mph = new float[n];
            from = new Vector3[n]; to = new Vector3[n];
            var halo = K.GlowMat(new Color(Palette.Water.r * 1.6f, Palette.Water.g * 1.6f, Palette.Water.b * 1.6f, 0.8f));
            for (int i = 0; i < n; i++)
            {
                mols[i] = Fx.Molecule(Mol.H2O, transform, 0.34f, 0.8f).transform; mols[i].name = "H2O (riding)";
                K.Glow(mols[i], Vector3.zero, 0.9f, halo);
                md[i] = K.Rand(r, 2.8f, 16f); mr[i] = K.Rand(r, 0.2f, 1.4f); mth[i] = K.Rand(r, 0f, Mathf.PI * 2f);
                mw[i] = K.Rand(r, -0.8f, 0.8f); mf[i] = K.Rand(r, 0.4f, 1.1f); mph[i] = K.Rand(r, 0f, Mathf.PI * 2f);
                to[i] = end + leafFwd * K.Rand(r, 2f, 10f) + leafSide * K.Rand(r, -7f, 7f) + Vector3.up * K.Rand(r, -2.5f, 1.5f);   // spreading into the leaf
            }
        }
    }
}
