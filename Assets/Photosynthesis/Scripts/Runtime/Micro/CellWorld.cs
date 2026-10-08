using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Photosynthesis
{
    /// Inside one plant cell: cell wall, membrane, large vacuole, nucleus, mitochondria, cytoplasm, and chloroplasts
    /// streaming around the periphery (cyclosis). The camera sits inside the cell (rounded box, half extents H).
    /// ponytail: 45 chloroplasts are separate renderers moved on the CPU (cheap at this count); GPU instancing if it grows past a few hundred.
    public class CellWorld : MicroWorld
    {
        static readonly Vector3 H = new Vector3(15f, 10f, 12.5f);           // inner wall half extents (superellipsoid k = 5)
        static readonly Vector3 VacPos = new Vector3(1.5f, -1f, 1.5f), VacR = new Vector3(7.2f, 5.4f, 6.2f);
        static readonly Vector3 NucPos = new Vector3(-8.8f, 3.2f, 7.2f);
        static readonly Vector3 PosePos = new Vector3(3f, 2f, -9.5f), PoseLook = new Vector3(-1.5f, 0f, 4f);
        const float K = 5f;

        class Chl { public Transform t; public Renderer skin, stacks; public Quaternion belt; public float theta, h, amp, phase, speed, scale; }

        bool ready;
        Transform wallAnchor, vacuole, nucleus;
        readonly List<Chl> chl = new List<Chl>();
        readonly List<Transform> chlList = new List<Transform>();
        readonly List<(Material m, Color c, Color e, float s)> dimmable = new List<(Material, Color, Color, float)>();
        static readonly int SmoothId = Shader.PropertyToID("_Smoothness");
        readonly List<GameObject> transient = new List<GameObject>();
        Material skinMat, stackMat, selSkin, selStack, glowO2, trailO2;
        Color skinE, stackE;
        GameObject selGlow;
        int selected = -1; bool driving;
        Vector3 showPos; Quaternion showRot;
        float dim, dimGoal, glow, glowGoal;

        public Transform Wall => ready ? wallAnchor : transform;
        public Transform Vacuole => ready ? vacuole : transform;
        public Transform Nucleus => ready ? nucleus : transform;
        public IReadOnlyList<Transform> Chloroplasts => chlList;

        // ================================================================== build
        protected override void Build()
        {
            var rnd = new System.Random(5);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            var root = transform;

            Material Dim(MatKind k, Color c, Color e, float smooth)
            {
                var m = Fx.Mat(k, c, e, smooth); dimmable.Add((m, c, e, smooth)); return m;
            }

            // --- cell wall (thick translucent cellulose shell) + membrane + neighbouring cells beyond
            var fibre = MicroKitB.Tex(128, 128, (u, v) =>
            {
                float n = Mathf.PerlinNoise(u * 6f + Mathf.PerlinNoise(v * 7f, 3.3f) * 1.2f, v * 46f);
                float g = 0.88f + 0.12f * n;
                return new Color(g, 1f, g, 0.5f + 0.5f * n);
            }, TextureWrapMode.Repeat);
            var wallOut = Dim(MatKind.LitTransparent, new Color(0.5f, 0.86f, 0.5f, 0.2f), new Color(0.02f, 0.07f, 0.03f), 0.75f);
            var wallIn = Dim(MatKind.LitTransparent, new Color(0.55f, 0.9f, 0.52f, 0.26f), new Color(0.03f, 0.09f, 0.04f), 0.7f);
            Fx.SetTexture(wallOut, fibre); Fx.SetTexture(wallIn, fibre);
            wallOut.mainTextureScale = wallIn.mainTextureScale = new Vector2(3f, 2f);
            // Both wall faces point inward (the camera lives inside), so the 0.7-thick wall reads as two layers.
            Fx.MeshObj("WallOuter", MicroKitB.Super(H + Vector3.one * 0.7f, K, K, 30, 48, true), wallOut, root);
            Fx.MeshObj("WallInner", MicroKitB.Super(H, K, K, 30, 48, true), wallIn, root);
            var membrane = Dim(MatKind.LitTransparent, new Color(0.95f, 0.98f, 0.72f, 0.1f), new Color(0.04f, 0.05f, 0.02f), 0.9f);
            Fx.MeshObj("Membrane", MicroKitB.Super(H - Vector3.one * 0.35f, K, K, 30, 48, true), membrane, root);
            var neighbour = Fx.Mat(MatKind.LitTransparent, new Color(0.45f, 0.8f, 0.45f, 0.14f), new Color(0.02f, 0.08f, 0.03f), 0.6f);
            // Seen from inside, every enclosing shell is behind everything in the cell: draw them first, outermost first.
            // (Distance sorting by bounds centre would draw the far wall over the vacuole and chloroplasts.)
            neighbour.renderQueue = 2980; wallOut.renderQueue = 2985; wallIn.renderQueue = 2990; membrane.renderQueue = 2995;
            var nMesh = MicroKitB.Super(H + Vector3.one * 0.7f, K, K, 16, 28);
            foreach (var o in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back, Vector3.up, Vector3.down })
                Fx.MeshObj("NeighbourCell", nMesh, neighbour, root).transform.localPosition = Vector3.Scale(o, (H + Vector3.one * 0.75f) * 2f);
            wallAnchor = Fx.Node("WallAnchor", root, WallHit(PosePos, Quaternion.LookRotation(PoseLook - PosePos) * Quaternion.Euler(-14f, 26f, 0f) * Vector3.forward)).transform;

            // --- vacuole: big translucent sac of watery sap (tonoplast inner + outer surfaces)
            vacuole = Fx.Node("Vacuole", root, VacPos).transform;
            Func<Vector3, float> sag = d => MicroKitB.Noise(d * 1.3f + new Vector3(2.1f, 0.4f, 7.7f)) * 0.06f;
            var vacOut = Dim(MatKind.LitTransparent, new Color(0.46f, 0.72f, 1f, 0.2f), new Color(0.03f, 0.09f, 0.2f), 0.95f);
            var vacIn = Dim(MatKind.LitTransparent, new Color(0.35f, 0.6f, 1f, 0.12f), new Color(0.02f, 0.06f, 0.16f), 0.9f);
            Fx.MeshObj("Tonoplast", MicroKitB.Super(VacR, 2, 2, 28, 40, false, sag), vacOut, vacuole);
            Fx.MeshObj("Sap", MicroKitB.Super(VacR * 0.985f, 2, 2, 28, 40, true, sag), vacIn, vacuole);

            // --- nucleus: envelope with pores, chromatin, glowing nucleolus
            nucleus = Fx.Node("Nucleus", root, NucPos).transform;
            var sph = MicroKitB.Sphere(8, 12);
            Fx.MeshObj("Envelope", MicroKitB.Super(Vector3.one * 2.3f, 2, 2, 22, 30), Dim(MatKind.LitTransparent, new Color(0.62f, 0.42f, 0.95f, 0.5f), new Color(0.12f, 0.05f, 0.22f), 0.8f), nucleus);
            Fx.MeshObj("Nucleolus", MicroKitB.Super(Vector3.one * 0.85f, 2, 2, 14, 20, false, d => MicroKitB.Noise(d * 2f) * 0.1f),
                Dim(MatKind.LitTransparent, new Color(0.38f, 0.16f, 0.6f, 0.92f), new Color(0.45f, 0.12f, 0.8f), 0.6f), nucleus).transform.localPosition = new Vector3(0.5f, 0.3f, -0.4f);
            var pores = new MicroKitB.Batch(); var chromatin = new MicroKitB.Batch();
            for (int i = 0; i < 70; i++)
            {
                var d = new Vector3(R(-1, 1), R(-1, 1), R(-1, 1)).normalized;
                pores.Add(sph, d * 2.28f, Quaternion.LookRotation(d), new Vector3(0.22f, 0.22f, 0.08f));
                if (i < 46) chromatin.Add(sph, d * R(0.6f, 1.9f), Quaternion.identity, Vector3.one * R(0.12f, 0.26f));
            }
            pores.Make("Pores", nucleus, Dim(MatKind.LitTransparent, new Color(0.3f, 0.18f, 0.5f, 0.75f), Color.black, 0.5f));
            chromatin.Make("Chromatin", nucleus, Dim(MatKind.LitTransparent, new Color(0.78f, 0.6f, 1f, 0.55f), new Color(0.18f, 0.1f, 0.3f), 0.5f));

            // --- mitochondria (small, kept subtle)
            var mito = new MicroKitB.Batch(); var mMesh = MicroKitB.Super(new Vector3(0.28f, 0.28f, 0.62f), 2, 2, 8, 12);
            for (int i = 0; i < 12; i++)
            {
                var d = new Vector3(R(-1, 1), R(-0.8f, 0.8f), R(-1, 1)).normalized;
                mito.Add(mMesh, Surf(d, H - Vector3.one * R(1.6f, 3f)), Quaternion.Euler(R(0, 360), R(0, 360), 0), Vector3.one * R(0.8f, 1.2f));
            }
            mito.Make("Mitochondria", root, Dim(MatKind.LitTransparent, new Color(1f, 0.6f, 0.45f, 0.7f), new Color(0.2f, 0.07f, 0.03f), 0.6f));

            // --- chloroplasts: lens + visible grana stacks inside; streaming along four periphery belts
            skinE = new Color(0.02f, 0.13f, 0.03f); stackE = new Color(0.03f, 0.28f, 0.06f);
            skinMat = Fx.Mat(MatKind.LitTransparent, new Color(0.36f, 0.82f, 0.36f, 0.5f), skinE, 0.85f);
            stackMat = Fx.Mat(MatKind.LitEmissive, new Color(0.1f, 0.46f, 0.13f), stackE, 0.7f);
            selSkin = Fx.Mat(MatKind.LitTransparent, new Color(0.4f, 0.9f, 0.4f, 0.55f), skinE, 0.85f);
            selStack = Fx.Mat(MatKind.LitEmissive, new Color(0.12f, 0.52f, 0.15f), stackE, 0.75f);
            var skin = MicroKitB.Super(new Vector3(0.72f, 0.46f, 1.1f), 2, 2.4f, 12, 20);
            var inner = new MicroKitB.Batch(); var disc = MicroKitB.Super(new Vector3(0.17f, 0.028f, 0.17f), 2, 4, 6, 14);
            var grana = new List<Vector3>();
            for (int g = 0; g < 7; g++)
            {
                var c = new Vector3((g % 2 == 0 ? -0.22f : 0.24f) + R(-0.06f, 0.06f), 0, -0.78f + g * 0.26f);
                grana.Add(c);
                for (int s = 0; s < 4; s++) inner.Add(disc, c + Vector3.up * (-0.11f + s * 0.074f), Quaternion.identity, Vector3.one);
            }
            for (int g = 1; g < grana.Count; g++) inner.Add(Fx.Tube(new[] { grana[g - 1], (grana[g - 1] + grana[g]) * 0.5f + Vector3.up * 0.03f, grana[g] }, 0.018f, 0.018f, 5), Vector3.zero, Quaternion.identity, Vector3.one);
            var innerMesh = inner.Mesh("ChloroplastInner");
            var belts = new[] { (Quaternion.identity, -0.34f, 12, 0.016f), (Quaternion.identity, 0.16f, 12, -0.013f), (Quaternion.Euler(0, 0, 90), 0.55f, 11, 0.014f), (Quaternion.Euler(0, 0, 90), -0.45f, 10, -0.012f) };
            foreach (var (rot, h, n, w) in belts)
                for (int i = 0; i < n; i++)
                {
                    var go = Fx.Node("Chloroplast", root);
                    var c = new Chl
                    {
                        t = go.transform, belt = rot, h = h + R(-0.06f, 0.06f), amp = R(0.06f, 0.14f), phase = R(0, 6.28f),
                        theta = i * Mathf.PI * 2f / n + R(-0.12f, 0.12f), speed = w * R(0.85f, 1.15f)
                    };
                    c.skin = Fx.MeshObj("Skin", skin, skinMat, go.transform).GetComponent<Renderer>();
                    c.stacks = Fx.MeshObj("Grana", innerMesh, stackMat, go.transform).GetComponent<Renderer>();
                    c.scale = R(0.9f, 1.1f); go.transform.localScale = Vector3.one * c.scale;
                    chl.Add(c); chlList.Add(go.transform); PlaceOnBelt(c, 0f);
                }
            selGlow = MicroKitB.Glow(root, Vector3.zero, 4f, MicroKitB.GlowMat(new Color(0.45f, 1f, 0.5f, 0.35f)));
            selGlow.SetActive(false);

            // --- cytoplasm motes, O₂ materials, backdrop
            var motes = Fx.Particles("Cytoplasm", root, new Fx.PCfg
            {
                color = new Color(0.8f, 1f, 0.85f, 0.3f), color2 = new Color(0.6f, 0.85f, 1f, 0.2f), size = 0.07f, life = 8f, speed = 0.05f,
                rate = 40f, max = 400, shape = ParticleSystemShapeType.Box, noise = 0.15f
            });
            var sh = motes.shape; sh.scale = H * 1.8f;
            glowO2 = MicroKitB.GlowMat(new Color(Palette.Oxygen.r, Palette.Oxygen.g, Palette.Oxygen.b, 0.55f) * 1.6f);
            trailO2 = Fx.Mat(MatKind.Additive, new Color(Palette.Oxygen.r, Palette.Oxygen.g, Palette.Oxygen.b, 0.4f));
            MicroKitB.Backdrop(root, 75f, new Color(0.2f, 0.38f, 0.28f), new Color(0.1f, 0.2f, 0.17f));   // sRGB: see Mood
            ready = true;
        }

        // superellipsoid helpers (k = 5) — surface point along a direction, outward normal, ray hit
        static Vector3 Surf(Vector3 d, Vector3 half)
        {
            float s = Mathf.Pow(Mathf.Pow(Mathf.Abs(d.x), K) + Mathf.Pow(Mathf.Abs(d.y), K) + Mathf.Pow(Mathf.Abs(d.z), K), -1f / K);
            return Vector3.Scale(d * s, half);
        }
        static Vector3 Normal(Vector3 p, Vector3 half)
        {
            float G(float x, float h) => Mathf.Sign(x) * Mathf.Pow(Mathf.Abs(x / h), K - 1) / h;
            return new Vector3(G(p.x, half.x), G(p.y, half.y), G(p.z, half.z)).normalized;
        }
        static Vector3 WallHit(Vector3 from, Vector3 dir)
        {
            for (float s = 0; s < 60f; s += 0.05f)
            {
                var q = from + dir * s;
                if (Mathf.Pow(Mathf.Abs(q.x / H.x), K) + Mathf.Pow(Mathf.Abs(q.y / H.y), K) + Mathf.Pow(Mathf.Abs(q.z / H.z), K) >= 1f) return q;
            }
            return from + dir * 10f;
        }

        void PlaceOnBelt(Chl c, float t)
        {
            var hin = H - Vector3.one * 0.95f;
            Vector3 Dir(float th) => (c.belt * new Vector3(Mathf.Cos(th), c.h + c.amp * Mathf.Sin(2f * th + c.phase), Mathf.Sin(th))).normalized;
            var p = Surf(Dir(c.theta), hin);
            var tan = (Surf(Dir(c.theta + 0.02f), hin) - p) * Mathf.Sign(c.speed);
            var n = Normal(p, hin);
            c.t.localPosition = p + n * (0.12f * Mathf.Sin(t * 0.6f + c.phase));
            c.t.localRotation = Quaternion.LookRotation(tan, n) * Quaternion.Euler(0, 0, 8f * Mathf.Sin(t * 0.4f + c.phase * 2f));
        }

        protected override void ApplyMood() =>
            MicroKitB.Mood(new Color(0.1f, 0.22f, 0.18f), 0.016f, new Color(0.56f, 0.72f, 0.6f), new Color(0.28f, 0.42f, 0.36f), new Color(0.1f, 0.16f, 0.14f),
                new Color(1f, 0.94f, 0.8f), 1.35f, new Vector3(55f, -30f, 0f));

        public override (Vector3 pos, Vector3 look) Pose() => (transform.TransformPoint(PosePos), transform.TransformPoint(PoseLook));

        /// FOLLOW THE OXYGEN arrival: the default spot lifted 1.5 m (clear sightline over the vacuole dome), aimed where
        /// O2AcrossCell's chase first looks, Lerp(src, exit[3], 0.35), so the cut lands on the O₂'s source chloroplasts and the crane starts with no whip-pan.
        public (Vector3 pos, Vector3 look) O2Pose => (transform.TransformPoint(PosePos + Vector3.up * 1.5f), transform.TransformPoint(new Vector3(3.9f, 5.6f, 4.75f)));

        public override void ResetWorld()
        {
            if (!ready) return;
            MicroKitB.Kill(transient);
            Release();
            dim = dimGoal = 0f; glow = glowGoal = 0f; ApplyDim(); ApplyGlow(0f);
        }

        void Release()
        {
            if (selected >= 0)
            {
                var c = chl[selected];
                c.skin.sharedMaterial = skinMat; c.stacks.sharedMaterial = stackMat;
                c.t.localScale = Vector3.one * c.scale; PlaceOnBelt(c, Time.time);
            }
            selected = -1; driving = false; selGlow.SetActive(false); selGlow.transform.localScale = Vector3.one * 4f;
        }

        // ================================================================== per-frame: streaming, fades, glow
        void Update()
        {
            if (!ready) return;
            float dt = Time.deltaTime, t = Time.time;
            for (int i = 0; i < chl.Count; i++)
            {
                chl[i].theta += chl[i].speed * dt;
                if (i != selected) PlaceOnBelt(chl[i], t);
            }
            if (selected >= 0 && !driving)
            {
                var tr = chl[selected].t; float k = 1f - Mathf.Exp(-2.2f * dt);
                tr.localPosition = Vector3.Lerp(tr.localPosition, showPos, k);
                tr.localRotation = Quaternion.Slerp(tr.localRotation, showRot * Quaternion.Euler(0, 0, 6f * Mathf.Sin(t * 0.8f)), k);
                selGlow.transform.position = tr.position;
            }
            if (!Mathf.Approximately(dim, dimGoal)) { dim = Mathf.MoveTowards(dim, dimGoal, dt * 0.9f); ApplyDim(); }
            if (!Mathf.Approximately(glow, glowGoal)) glow = Mathf.MoveTowards(glow, glowGoal, dt * 1.2f);
            ApplyGlow(t);
        }

        void ApplyDim()
        {
            float a = Mathf.Lerp(1f, 0.16f, MicroKitB.Smooth01(dim)), e = Mathf.Lerp(1f, 0.3f, dim);
            float k = MicroKitB.Smooth01(dim);
            foreach (var (m, c, em, sm) in dimmable)
            {
                Fx.SetColor(m, new Color(c.r, c.g, c.b, c.a * a)); Fx.SetEmission(m, em * e);
                m.SetFloat(SmoothId, Mathf.Lerp(sm, 0.25f, k));   // URP transparent keeps full specular at low alpha
            }
        }

        void ApplyGlow(float t)
        {
            float pulse = glow * (0.75f + 0.25f * Mathf.Sin(t * 3.2f));
            Fx.SetEmission(stackMat, stackE * (1f + 9f * pulse));
            Fx.SetEmission(skinMat, skinE * (1f + 6f * pulse));
            if (selected < 0) return;
            float s = 1f + 9f * pulse + 4f;
            Fx.SetEmission(selStack, stackE * s); Fx.SetEmission(selSkin, skinE * (s * 0.7f));
        }

        // ================================================================== story API
        /// Fade non-chloroplast structures down.
        public void DeEmphasize(bool on) { dimGoal = on ? 1f : 0f; }

        public void IlluminateChloroplasts(bool on) { glowGoal = on ? 1f : 0f; }

        /// Chloroplast nearest the camera centre (selected one). It leaves the stream and glides to a clear spot in front of the camera.
        public Transform PickChloroplast()
        {
            if (!ready) return transform;
            if (selected >= 0) return chl[selected].t;
            var cam = Module.I != null && Module.I.cam ? Module.I.cam.transform : null;
            Vector3 cp = cam ? transform.InverseTransformPoint(cam.position) : PosePos;
            Vector3 f = cam ? transform.InverseTransformDirection(cam.forward) : (PoseLook - PosePos).normalized;
            Vector3 up = cam ? transform.InverseTransformDirection(cam.up) : Vector3.up, right = Vector3.Cross(up, f);
            showPos = cp + f * 4.6f + right * 0.7f - up * 0.25f;
            // keep it in the cytoplasm (outside the vacuole, inside the membrane)
            for (int i = 0; i < 20 && InVacuole(showPos); i++) showPos -= f * 0.25f;
            showRot = Quaternion.LookRotation(right, -f) * Quaternion.Euler(22f, 0, 0);
            float best = float.MaxValue;
            for (int i = 0; i < chl.Count; i++)
            {
                var p = chl[i].t.localPosition;
                if (Vector3.Dot((p - cp).normalized, f) < 0.35f) continue;
                float d = Vector3.Distance(p, showPos);
                if (d < best) { best = d; selected = i; }
            }
            if (selected < 0) selected = 0;
            var c = chl[selected];
            c.skin.sharedMaterial = selSkin; c.stacks.sharedMaterial = selStack;
            selGlow.SetActive(true); selGlow.transform.position = c.t.position;
            return c.t;
        }

        static bool InVacuole(Vector3 p)
        {
            var q = p - VacPos;
            return (q.x * q.x) / (VacR.x * VacR.x) + (q.y * q.y) / (VacR.y * VacR.y) + (q.z * q.z) / (VacR.z * VacR.z) < 1.15f;
        }

        /// Selected chloroplast enlarges while the camera closes in until it fills the view.
        public IEnumerator EnlargeChloroplast(CameraDirector cam, Transform chloroplast)
        {
            if (!ready) yield break;
            if (selected < 0 || chloroplast != chl[selected].t) { Release(); chloroplast = PickChloroplast(); }
            driving = true;
            var tr = chloroplast; glowGoal = Mathf.Max(glowGoal, 0.6f);
            Vector3 p0 = tr.position, s0 = tr.localScale, s1 = Vector3.one * 2.3f; Quaternion q0 = tr.rotation;
            Vector3 c0 = cam ? cam.transform.position : transform.TransformPoint(PosePos);
            Vector3 f = cam ? cam.transform.forward : transform.TransformDirection((PoseLook - PosePos).normalized);
            Vector3 up = cam ? cam.transform.up : transform.up, right = Vector3.Cross(up, f);
            Vector3 p1 = c0 + f * 3.8f, cam1 = p1 - f * 2.6f;
            Quaternion q1 = transform.rotation * Quaternion.LookRotation(transform.InverseTransformDirection(right), transform.InverseTransformDirection(-f)) * Quaternion.Euler(14f, 0, 0);
            float fov0 = cam ? cam.Cam.fieldOfView : 55f;
            Quaternion r0 = cam ? cam.transform.rotation : Quaternion.identity;   // ease from the framing we start in: no first-frame swing at any fps
            if (cam) cam.StopOrbit();
            yield return Fx.Tween(3f, t =>
            {
                float u = Fx.Eval(t, Ease.InOut);
                tr.position = Vector3.Lerp(p0, p1, u); tr.localScale = Vector3.Lerp(s0, s1, u);
                tr.rotation = Quaternion.Slerp(q0, q1, u);
                selGlow.transform.position = tr.position; selGlow.transform.localScale = Vector3.one * Mathf.Lerp(4f, 9f, u);
                if (!cam) return;
                var cp = Vector3.Lerp(c0, cam1, Fx.Eval(t, Ease.In));
                cam.transform.SetPositionAndRotation(cp, Quaternion.Slerp(r0, Quaternion.LookRotation(tr.position - cp, up), u));   // deterministic at 30, 60 or 449 fps
                cam.Cam.fieldOfView = Mathf.Lerp(fov0, 44f, u);
            }, Ease.Linear);
            if (cam) cam.transform.rotation = Quaternion.LookRotation(tr.position - cam.transform.position, up);
        }

        /// O₂ drifts from a chloroplast across the cell (chloroplast → cell leg of the oxygen journey).
        public IEnumerator O2AcrossCell(CameraDirector cam)
        {
            if (!ready) yield break;
            MicroKitB.Kill(transient);
            var src = new Vector3(-2f, 6.5f, 10f);
            var near = new List<Chl>(chl); near.Sort((a, b) => (a.t.localPosition - src).sqrMagnitude.CompareTo((b.t.localPosition - src).sqrMagnitude));
            var exit = new[] { new Vector3(2f, 6.3f, 4f), new Vector3(8f, 5.2f, -1f), new Vector3(13.2f, 4.2f, -4.5f), new Vector3(14.9f, 4f, -5f), new Vector3(18.5f, 3.7f, -6.2f) };
            var movers = new List<(Transform t, List<Vector3> p, float delay)>();
            for (int i = 0; i < 6; i++)
            {
                var from = near[i].t.localPosition;
                var p = new List<Vector3> { from, from + (exit[0] - from) * 0.15f };
                var jitter = i == 0 ? Vector3.zero : new Vector3(Mathf.Sin(i * 2.1f), Mathf.Cos(i * 1.7f), Mathf.Sin(i * 3.3f)) * 0.7f;
                foreach (var e in exit) p.Add(e + jitter * (e.x < 14f ? 1f : 0.4f));
                var g = Fx.Molecule(Mol.O2, transform, i == 0 ? 0.42f : 0.28f, 1f);
                g.transform.localPosition = from; transient.Add(g);
                MicroKitB.Glow(g.transform, Vector3.zero, i == 0 ? 1.5f : 1f, glowO2);
                MicroKitB.Trail(g, trailO2, i == 0 ? 0.16f : 0.1f, 0.6f);
                movers.Add((g.transform, p, i * 0.22f));
            }
            Vector3 c0 = cam ? cam.transform.position : Vector3.zero; bool burst = false;
            if (cam) cam.StopOrbit();
            const float dur = 3.6f;
            yield return Fx.Tween(dur, t =>
            {
                foreach (var (tr, p, d) in movers)
                {
                    float v = Mathf.Clamp01((t * dur - d) / (dur - d));
                    tr.localPosition = Fx.CatmullRom(p, Fx.Eval(v, Ease.InOut)) + new Vector3(Mathf.Sin(t * 17f + d * 9f), Mathf.Cos(t * 13f + d * 5f), 0) * 0.06f;
                    tr.localRotation = Quaternion.Euler(t * 260f + d * 100f, t * 180f, 0);
                }
                var hero = movers[0].t.localPosition;
                if (!burst && hero.x > 14.8f) { burst = true; Fx.Burst(movers[0].t.position, Palette.Oxygen, 36, 0.08f, 1.6f, 1f); }
                if (!cam) return;
                // crane up to a parked 3/4 view inside the cell (clear of the chloroplast belts; the cell is convex, so the lerp
                // stays inside) that pans with the O₂: the +x wall, its corners and the exit stay in frame as it crosses
                var want = transform.TransformPoint(Vector3.Lerp(transform.InverseTransformPoint(c0), new Vector3(2f, 7f, -9f), MicroKitB.Smooth01(t * 1.5f)));
                MicroKitB.Chase(cam, want, Vector3.Lerp(movers[0].t.position, transform.TransformPoint(exit[3]), 0.35f), 3.5f);
            }, Ease.Linear);
        }
    }
}
