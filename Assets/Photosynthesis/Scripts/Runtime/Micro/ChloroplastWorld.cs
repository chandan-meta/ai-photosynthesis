using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Photosynthesis
{
    /// Inside one chloroplast: double membrane, grana (thylakoid stacks, chlorophyll-rich), lamellae, stroma.
    /// Hosts the photosynthesis reaction abstraction and glucose / oxygen products.
    /// Local layout: lens along x (outer half extents E0); the front (−z, camera side) is cut away at z = ZC by Open().
    /// Reaction abstraction: light at the thylakoids splits water (O₂ bubbles) and sends golden carriers to a carbon-fixing
    /// cycle wheel in the stroma (Site), which takes in CO₂ and assembles the new sugar ring beside it (GluPos).
    /// ponytail: the cutaway is a pre-split mesh (cap + shell) peeled away, not a shader clip plane; add one if free slicing is needed.
    public class ChloroplastWorld : MicroWorld
    {
        static readonly Vector3 E0 = new Vector3(7.8f, 3.0f, 4.6f), E1 = new Vector3(7.4f, 2.68f, 4.25f);
        const float ZC = -1.2f, GluSize = 0.75f;
        static readonly Vector3 Site = new Vector3(1.2f, 0.15f, -2.55f), GluPos = new Vector3(3.1f, 1.05f, -2.85f), StarchPos = new Vector3(-4.6f, -0.7f, 1.6f);
        static readonly Vector3 LightFrom = new Vector3(-6.5f, 7.5f, -4.5f);
        static readonly Quaternion Facing = Quaternion.Euler(14f, 0f, 0f);   // tilt flat props toward the default camera

        class Granum { public Vector3 c; public float r, hh; public Transform glow; }
        class Floater { public Transform t; public Vector3 rest; public float ph; }

        bool ready, opened;
        Coroutine camMove;
        Transform granaAnchor, stromaAnchor, gluAnchor, capOuter, capInner;
        GameObject rim, energy, glucose, cycle;
        Material capOuterMat, capInnerMat, rimMat, granaMat, lamMat, beamMat, shaftMat, goldMat, ghostMat, ghostBondMat, unitMat,
            greenGlow, goldGlow, waterGlow, co2Glow, o2Glow, goldTrail, o2Trail, bubbleMat, vigCellMat, vigWallMat, starchMat,
            bandMat, cycleMat, nodeMat, cycleGlow, outlineMat, fibreMat, co2Trail, waterTrail, feedGlowC, feedGlowW;
        Color capOuterC, capInnerC, rimC, granaE, lamE;
        float hl, hlGoal, flash;
        Mesh sphere, starchMesh, outlineMesh, ringMesh;   // ring meshes cached: FormGlucose may run many times
        readonly List<Granum> grana = new List<Granum>();
        readonly List<List<Vector3>> lamellae = new List<List<Vector3>>();
        readonly List<Floater> water = new List<Floater>(), co2 = new List<Floater>(), bubbles = new List<Floater>();
        readonly List<GameObject> transient = new List<GameObject>();
        CameraDirector clickCam; List<Floater>[] feed; bool[] feedSent; int feedClick = -1;   // FeedIngredients

        public Transform Grana => ready ? granaAnchor : transform;
        public Transform Stroma => ready ? stromaAnchor : transform;
        public Transform Glucose => glucose ? glucose.transform : ready ? gluAnchor : transform;

        // ================================================================== build
        protected override void Build()
        {
            var rnd = new System.Random(23);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            var root = transform;
            sphere = MicroKitB.Sphere(8, 12);

            // --- materials
            var outerMat = Fx.Mat(MatKind.LitTransparent, capOuterC = new Color(0.46f, 0.72f, 0.26f, 0.16f), new Color(0.015f, 0.04f, 0.01f), 0.7f);
            var innerMat = Fx.Mat(MatKind.LitTransparent, capInnerC = new Color(0.62f, 0.82f, 0.42f, 0.11f), new Color(0.015f, 0.045f, 0.01f), 0.66f);
            capOuterMat = new Material(outerMat); capInnerMat = new Material(innerMat);
            rimC = new Color(0.75f, 1f, 0.7f, 0.85f);
            rimMat = Fx.Mat(MatKind.Glass, rimC, new Color(0.15f, 0.6f, 0.2f), 0.8f);
            granaE = new Color(0.01f, 0.085f, 0.015f); lamE = new Color(0.01f, 0.06f, 0.012f);
            granaMat = Fx.Mat(MatKind.LitEmissive, new Color(0.11f, 0.56f, 0.19f), granaE, 0.88f);
            lamMat = Fx.Mat(MatKind.LitEmissive, new Color(0.22f, 0.64f, 0.28f), lamE, 0.75f);
            beamMat = Fx.Mat(MatKind.Additive, new Color(2.2f, 1.8f, 0.9f, 0.6f)); Fx.SetTexture(beamMat, MicroKitB.BeamTex);
            shaftMat = Fx.Mat(MatKind.Additive, new Color(1.6f, 1.35f, 0.7f, 0.22f)); Fx.SetTexture(shaftMat, MicroKitB.BeamTex);
            goldMat = Fx.Shared(MatKind.LitEmissive, Palette.Gold, Palette.Gold * 4f);
            ghostMat = Fx.Mat(MatKind.LitTransparent, new Color(1f, 0.82f, 0.5f, 0.4f), Palette.Glucose * 0.9f, 0.8f);
            ghostBondMat = Fx.Mat(MatKind.LitTransparent, new Color(1f, 1f, 1f, 0.3f), new Color(0.3f, 0.25f, 0.15f), 0.6f);
            unitMat = Fx.Mat(MatKind.LitEmissive, Palette.Glucose, Palette.Glucose * 1.6f, 0.7f);
            greenGlow = MicroKitB.GlowMat(new Color(0.3f, 1.2f, 0.4f, 0.32f));
            goldGlow = MicroKitB.GlowMat(new Color(2f, 1.6f, 0.6f, 0.7f));
            waterGlow = MicroKitB.GlowMat(new Color(Palette.Water.r, Palette.Water.g, Palette.Water.b, 0.45f) * 1.4f);
            co2Glow = MicroKitB.GlowMat(new Color(Palette.CO2.r, Palette.CO2.g, Palette.CO2.b, 0.35f) * 1.2f);
            o2Glow = MicroKitB.GlowMat(new Color(Palette.Oxygen.r, Palette.Oxygen.g, Palette.Oxygen.b, 0.5f) * 1.5f);
            goldTrail = Fx.Mat(MatKind.Additive, new Color(2f, 1.5f, 0.5f, 0.55f));
            o2Trail = Fx.Mat(MatKind.Additive, new Color(Palette.Oxygen.r, Palette.Oxygen.g, Palette.Oxygen.b, 0.4f));
            co2Trail = Fx.Mat(MatKind.Additive, new Color(Palette.CO2.r, Palette.CO2.g, Palette.CO2.b, 0.4f) * 1.5f);
            waterTrail = Fx.Mat(MatKind.Additive, new Color(Palette.Water.r, Palette.Water.g, Palette.Water.b, 0.4f) * 1.5f);
            feedGlowC = MicroKitB.GlowMat(new Color(Palette.CO2.r, Palette.CO2.g, Palette.CO2.b, 0.5f) * 1.6f);
            feedGlowW = MicroKitB.GlowMat(new Color(Palette.Water.r, Palette.Water.g, Palette.Water.b, 0.5f) * 1.6f);
            bubbleMat = Fx.Mat(MatKind.LitTransparent, new Color(0.7f, 0.95f, 1f, 0.2f), new Color(0.1f, 0.25f, 0.3f), 0.97f);
            vigCellMat = Fx.Mat(MatKind.LitTransparent, new Color(0.5f, 0.9f, 0.5f, 0.45f), new Color(0.03f, 0.12f, 0.04f), 0.85f);
            vigWallMat = Fx.Mat(MatKind.LitTransparent, new Color(0.7f, 1f, 0.6f, 0f), new Color(0.015f, 0.05f, 0.015f), 0.6f);
            starchMat = Fx.Mat(MatKind.LitEmissive, new Color(0.96f, 0.93f, 0.84f), new Color(0.1f, 0.09f, 0.07f), 0.55f);
            bandMat = Fx.Mat(MatKind.Glass, new Color(0.8f, 1f, 0.7f, 0.18f), new Color(0.05f, 0.2f, 0.06f), 0.8f);
            cycleMat = Fx.Mat(MatKind.LitEmissive, new Color(0.45f, 0.95f, 0.7f), new Color(0.25f, 1.3f, 0.65f), 0.8f);
            nodeMat = Fx.Shared(MatKind.LitEmissive, Palette.GreenBright, Palette.GreenBright * 2.5f);
            cycleGlow = MicroKitB.GlowMat(new Color(0.5f, 1.3f, 0.8f, 0.3f));
            outlineMat = Fx.Mat(MatKind.Additive, new Color(2f, 1.5f, 0.6f, 0f));
            fibreMat = Fx.Mat(MatKind.LitEmissive, new Color(0.75f, 1f, 0.65f), new Color(0.2f, 0.45f, 0.15f), 0.6f);

            // --- double membrane (outer + inner envelope), each an ellipsoid split exactly at the cut plane z = ZC:
            //     back shell (double-sided: the open bowl from the front, the whole lens from behind) + front cap peeled by Open().
            //     Back shells render first so every stroma content draws over the envelope interior (transparent sort fix).
            float pc0 = Mathf.Acos(ZC / E0.z), pc1 = Mathf.Acos(ZC / E1.z);
            outerMat.renderQueue = 2985; innerMat.renderQueue = 2990;
            Fx.MeshObj("OuterMembrane", MicroKitB.EllipsoidZone(E0, 0f, pc0, true), outerMat, root);
            Fx.MeshObj("InnerMembrane", MicroKitB.EllipsoidZone(E1, 0f, pc1, true), innerMat, root);
            capOuter = Fx.MeshObj("OuterCap", MicroKitB.EllipsoidZone(E0, pc0, Mathf.PI, false, 20), capOuterMat, root).transform;
            capInner = Fx.MeshObj("InnerCap", MicroKitB.EllipsoidZone(E1, pc1, Mathf.PI, false, 20), capInnerMat, root).transform;
            rim = Fx.Node("CutRim", root).gameObject;
            float k0 = Mathf.Sin(pc0), k1 = Mathf.Sin(pc1);
            List<Vector3> Ellipse(Vector3 e, float k)
            {
                var l = new List<Vector3>(72);
                for (int i = 0; i < 72; i++) { float a = i * Mathf.PI * 2f / 72f; l.Add(new Vector3(Mathf.Cos(a) * e.x * k, Mathf.Sin(a) * e.y * k, ZC)); }
                return l;
            }
            List<Vector3> eo = Ellipse(E0, k0), ei = Ellipse(E1, k1);
            Fx.MeshObj("Outer", MicroKitB.Loop(eo, 0.05f, 6), rimMat, rim.transform);
            Fx.MeshObj("Inner", MicroKitB.Loop(ei, 0.04f, 6), rimMat, rim.transform);
            var band = new Mesh { name = "Band" }; var bv = new Vector3[144]; var bn = new Vector3[144]; var bt = new int[72 * 6];
            for (int i = 0; i < 72; i++)
            {
                bv[i] = eo[i]; bv[72 + i] = ei[i]; bn[i] = bn[72 + i] = Vector3.back;
                int j = (i + 1) % 72;
                bt[i * 6] = i; bt[i * 6 + 1] = 72 + i; bt[i * 6 + 2] = j; bt[i * 6 + 3] = 72 + i; bt[i * 6 + 4] = 72 + j; bt[i * 6 + 5] = j;
            }
            band.vertices = bv; band.normals = bn; band.triangles = bt; band.RecalculateBounds();
            Fx.MeshObj("IntermembraneSpace", band, bandMat, rim.transform);
            rim.SetActive(false);

            // --- grana: stacks of glossy thylakoid discs, perpendicular to the lens plane
            float Sq(float x) => x * x;
            for (int tries = 0; tries < 900 && grana.Count < 16; tries++)
            {
                var c = new Vector3(R(-6.2f, 6.2f), R(-0.3f, 0.3f), R(-3.3f, 3.3f));
                int n = rnd.Next(6, 11); float r = R(0.58f, 0.74f), hh = n * 0.065f;
                if (Sq((Mathf.Abs(c.x) + r + 0.15f) / E1.x) + Sq((Mathf.Abs(c.y) + hh + 0.2f) / E1.y) + Sq((Mathf.Abs(c.z) + r + 0.15f) / E1.z) > 1f) continue;
                if (Vector3.Distance(c, Site) < 2.6f || Vector3.Distance(c, GluPos) < 2.1f || Vector3.Distance(c, StarchPos) < 1.9f) continue;
                bool ok = true;
                foreach (var g in grana) if (new Vector2(c.x - g.c.x, c.z - g.c.z).magnitude < 1.85f) { ok = false; break; }
                if (ok) grana.Add(new Granum { c = c, r = r, hh = hh });
            }
            var disc = MicroKitB.Super(new Vector3(1f, 0.052f, 1f), 2, 5, 6, 22);
            var discs = new MicroKitB.Batch();
            float best = float.MaxValue; granaAnchor = Fx.Node("GranaAnchor", root).transform;
            foreach (var g in grana)
            {
                var tilt = Quaternion.Euler(R(-6, 6), R(0, 360), R(-6, 6));
                int n = Mathf.RoundToInt(g.hh / 0.065f);
                for (int s = 0; s < n; s++)
                    discs.Add(disc, g.c + tilt * new Vector3(0, -g.hh + 0.065f + s * 0.13f, 0), tilt, new Vector3(g.r * R(0.96f, 1.04f), 1f, g.r * R(0.96f, 1.04f)));
                g.glow = MicroKitB.Glow(root, g.c, 2.6f, greenGlow).transform; g.glow.gameObject.SetActive(false);
                float d = Vector3.Distance(g.c, new Vector3(-1.5f, 0.5f, -1.6f));
                if (d < best) { best = d; granaAnchor.localPosition = g.c + Vector3.up * g.hh; }
            }
            discs.Make("Thylakoids", root, granaMat);

            // --- lamellae: stroma thylakoids linking each granum to its nearest neighbours
            var lam = new MicroKitB.Batch(); var linked = new HashSet<int>();
            for (int i = 0; i < grana.Count; i++)
            {
                var order = new List<int>();
                for (int j = 0; j < grana.Count; j++) if (j != i) order.Add(j);
                order.Sort((x, y) => Vector3.Distance(grana[i].c, grana[x].c).CompareTo(Vector3.Distance(grana[i].c, grana[y].c)));
                for (int q = 0; q < Mathf.Min(2, order.Count); q++)
                {
                    int j = order[q], key = Mathf.Min(i, j) * 100 + Mathf.Max(i, j);
                    if (!linked.Add(key)) continue;
                    Granum a = grana[i], b = grana[j];
                    var dir = new Vector3(b.c.x - a.c.x, 0, b.c.z - a.c.z).normalized; var side = Vector3.Cross(dir, Vector3.up);
                    foreach (float f in new[] { 0.3f, 0.72f })
                    {
                        var A = a.c + dir * a.r * 0.85f + Vector3.up * (-a.hh + 2f * a.hh * f);
                        var B = b.c - dir * b.r * 0.85f + Vector3.up * (-b.hh + 2f * b.hh * f);
                        var M = (A + B) * 0.5f + Vector3.up * R(-0.18f, 0.18f) + side * R(-0.35f, 0.35f);
                        var path = Fx.Smooth(new[] { A, M, B }, 9);
                        lamellae.Add(path);
                        lam.Add(Fx.Tube(path, 0.045f, 0.045f, 6), Vector3.zero, Quaternion.identity, Vector3.one);
                    }
                }
            }
            lam.Make("Lamellae", root, lamMat);

            // --- stroma contents: ribosomes, plastoglobuli, cpDNA, starch grain, drifting fluid particles
            var ribo = new MicroKitB.Batch(); var globules = new MicroKitB.Batch();
            for (int i = 0, made = 0; i < 2000 && made < 190; i++)
            {
                var p = Vector3.Scale(new Vector3(R(-1, 1), R(-1, 1), R(-1, 1)), E1 * 0.88f);
                if (Sq(p.x / E1.x) + Sq(p.y / E1.y) + Sq(p.z / E1.z) > 0.78f) continue;
                if (Vector3.Distance(p, Site) < 2.5f || Vector3.Distance(p, GluPos) < 2.5f) continue;   // no pepper on the cycle wheel or the hero sugar
                made++;
                if (made % 19 == 0) globules.Add(sphere, p, Quaternion.identity, Vector3.one * R(0.18f, 0.28f));
                else ribo.Add(sphere, p, Quaternion.identity, Vector3.one * R(0.06f, 0.09f));
            }
            ribo.Make("Ribosomes", root, Fx.Mat(MatKind.LitEmissive, new Color(0.45f, 0.62f, 0.4f), new Color(0.04f, 0.08f, 0.04f), 0.4f));
            globules.Make("Plastoglobuli", root, Fx.Mat(MatKind.Lit, new Color(0.78f, 0.7f, 0.4f), null, 0.7f));   // no glow: gold here means light or glucose
            var dna = new List<Vector3>();
            for (int i = 0; i < 26; i++)
            {
                float a = i * Mathf.PI * 2f / 26f;
                dna.Add(new Vector3(-2.4f + Mathf.Cos(a) * 0.95f + R(-0.2f, 0.2f), -0.9f + Mathf.Sin(2 * a) * 0.25f + R(-0.1f, 0.1f), 2.3f + Mathf.Sin(a) * 0.7f + R(-0.2f, 0.2f)));
            }
            dna.Add(dna[0]);
            Fx.MeshObj("cpDNA", Fx.Tube(Fx.Smooth(dna, 90), 0.026f, 0.026f, 5), Fx.Mat(MatKind.LitEmissive, new Color(0.6f, 0.85f, 1f), new Color(0.15f, 0.3f, 0.45f), 0.6f), root);
            starchMesh = MicroKitB.Super(new Vector3(0.95f, 0.62f, 0.75f), 2, 2, 14, 20, false, d => MicroKitB.Noise(d * 2.2f) * 0.05f);
            var starch = Fx.MeshObj("StarchGrain", starchMesh, starchMat, root);
            starch.transform.localPosition = StarchPos;
            Fx.MeshObj("StarchShell", MicroKitB.Super(new Vector3(1.08f, 0.72f, 0.86f), 2, 2, 14, 20), Fx.Mat(MatKind.LitTransparent, new Color(1f, 0.97f, 0.88f, 0.22f), new Color(0.06f, 0.06f, 0.05f), 0.9f), starch.transform);
            var fluid = Fx.Particles("StromaFluid", root, new Fx.PCfg
            {
                color = new Color(0.8f, 1f, 0.82f, 0.35f), color2 = new Color(1f, 1f, 0.8f, 0.2f), size = 0.05f, life = 7f, speed = 0.05f,
                rate = 60f, max = 520, shape = ParticleSystemShapeType.Sphere, radius = 1f, noise = 0.12f
            });
            var sh = fluid.shape; sh.scale = E1 * 0.9f;

            stromaAnchor = Fx.Node("Stroma", root, Site + new Vector3(-0.8f, -0.9f, 0.4f)).transform;
            gluAnchor = Fx.Node("GlucoseSite", root, GluPos).transform;
            MicroKitB.Backdrop(root, 60f, new Color(0.24f, 0.44f, 0.34f), new Color(0.13f, 0.26f, 0.21f));   // sRGB: see Mood
            ready = true;
        }

        protected override void ApplyMood() =>
            MicroKitB.Mood(new Color(0.15f, 0.3f, 0.25f), 0.017f, new Color(0.55f, 0.72f, 0.58f), new Color(0.26f, 0.4f, 0.32f), new Color(0.08f, 0.14f, 0.11f),
                new Color(1f, 0.93f, 0.76f), 1.5f, Quaternion.LookRotation(-LightFrom).eulerAngles);

        public override (Vector3 pos, Vector3 look) Pose() => (Origin + new Vector3(1.5f, 3.4f, -12.8f), Origin + new Vector3(0.4f, -0.3f, -0.4f));

        /// Molecules home, highlights off. The membrane stays as it is (Story only calls Open() once per run); Open() itself always replays from closed.
        public override void ResetWorld()
        {
            if (!ready) return;
            if (camMove != null) { StopCoroutine(camMove); camMove = null; }
            FeedUnsubscribe(); feed = null;
            MicroKitB.Kill(transient);
            water.Clear(); co2.Clear(); bubbles.Clear();
            if (glucose) Destroy(glucose); glucose = null;
            if (cycle) Destroy(cycle); cycle = null;
            if (energy) Destroy(energy); energy = null;
            hl = hlGoal = 0f; flash = 0f; ApplyGlow(0f);
            Fx.SetColor(beamMat, new Color(2.2f, 1.8f, 0.9f, 0.6f)); Fx.SetColor(shaftMat, new Color(1.6f, 1.35f, 0.7f, 0.22f));
        }

        // ================================================================== per frame
        void Update()
        {
            if (!ready) return;
            float t = Time.time, dt = Time.deltaTime;
            if (!Mathf.Approximately(hl, hlGoal)) hl = Mathf.MoveTowards(hl, hlGoal, dt * 1.1f);
            if (flash > 0f) flash = Mathf.MoveTowards(flash, 0f, dt * 0.7f);
            ApplyGlow(t);
            Bob(water, t, 0.07f); Bob(co2, t, 0.1f); Bob(bubbles, t, 0.12f);
            if (cycle) cycle.transform.GetChild(0).localRotation = Quaternion.Euler(0, 0, t * 40f);
            if (glucose) glucose.transform.localRotation = Facing * Quaternion.Euler(0, 12f * Mathf.Sin(t * 0.4f), 5f * Mathf.Sin(t * 0.2f));
        }

        static void Bob(List<Floater> l, float t, float amp)
        {
            foreach (var f in l)
            {
                if (!f.t) continue;
                f.t.localPosition = f.rest + new Vector3(Mathf.Sin(t * 0.9f + f.ph) * amp, Mathf.Sin(t * 1.3f + f.ph * 1.7f) * amp, Mathf.Cos(t * 0.7f + f.ph) * amp * 0.6f);
                f.t.localRotation = Quaternion.Euler(t * 25f + f.ph * 40f, t * 18f + f.ph * 60f, 0);
            }
        }

        void ApplyGlow(float t)
        {
            float on = MicroKitB.Smooth01(hl);
            float pulse = on * (0.65f + 0.35f * Mathf.Sin(t * 2.6f));
            var gold = new Color(0.32f, 0.21f, 0.04f) * flash;
            Fx.SetEmission(granaMat, granaE + new Color(0.035f, 0.25f, 0.055f) * pulse + gold);
            Fx.SetEmission(lamMat, lamE + new Color(0.025f, 0.19f, 0.04f) * pulse + gold * 0.6f);
            foreach (var g in grana)
            {
                bool vis = on > 0.01f;
                if (g.glow.gameObject.activeSelf != vis) g.glow.gameObject.SetActive(vis);
                if (vis) g.glow.localScale = Vector3.one * (2.3f * on * (0.75f + 0.25f * Mathf.Sin(t * 3f - g.c.x * 0.7f)));
            }
        }

        // ------------------------------------------------------------------ helpers
        Vector3 W(Vector3 local) => transform.TransformPoint(local);

        GameObject Mol3(Mol kind, Vector3 local, float size, Material glow, float glowSize, Material trail = null, float trailW = 0f)
        {
            var g = Fx.Molecule(kind, transform, size, 0.9f);
            g.transform.localPosition = local; transient.Add(g);
            if (glow) MicroKitB.Glow(g.transform, Vector3.zero, glowSize, glow);
            if (trail) MicroKitB.Trail(g, trail, trailW, 0.6f);
            return g;
        }

        GameObject Orb(Vector3 local, float size, Material m, Material glow, Material trail)
        {
            var g = Fx.Prim(PrimitiveType.Sphere, transform, local, Vector3.one * size, m, false, "Orb");
            transient.Add(g);
            if (glow) MicroKitB.Glow(g.transform, Vector3.zero, 4f, glow);
            if (trail) MicroKitB.Trail(g, trail, size * 0.8f, 0.45f);
            return g;
        }

        /// Gentle story re-framing (local pose) so the payoff is not one static wide shot. Runs on this (visible) world, so it
        /// dies with it; never touches CameraDirector's scripted counter and never fights the learner's orbit.
        void Frame(Vector3 pos, Vector3 look, float dur, float fov)
        {
            var cd = Module.I != null ? Module.I.cam : null;
            if (!cd || cd.Orbiting || !isActiveAndEnabled) return;
            if (camMove != null) StopCoroutine(camMove);
            camMove = StartCoroutine(Glide(cd, W(pos), W(look), dur, fov));
        }

        IEnumerator Glide(CameraDirector cd, Vector3 p1, Vector3 look, float dur, float fov)
        {   // flat loop (no nested tween) so StopCoroutine always stops it cleanly
            var t = cd.transform; Vector3 p0 = t.position; Quaternion r0 = t.rotation, r1 = Quaternion.LookRotation(look - p1); float f0 = cd.Cam.fieldOfView;
            for (float k = 0f; k < dur && !Module.Skipping; k += Time.deltaTime)
            {
                if (cd.Orbiting) { camMove = null; yield break; }
                float u = Fx.Eval(k / dur, Ease.InOut);
                t.SetPositionAndRotation(Vector3.Lerp(p0, p1, u), Quaternion.Slerp(r0, r1, u)); cd.Cam.fieldOfView = Mathf.Lerp(f0, fov, u);
                yield return null;
            }
            if (!cd.Orbiting) { t.SetPositionAndRotation(p1, r1); cd.Cam.fieldOfView = fov; }
            camMove = null;
        }

        void EnsureGlucose()
        {
            if (glucose) return;
            glucose = Fx.Molecule(Mol.Glucose, transform, GluSize, 0.8f);
            glucose.transform.localPosition = GluPos;
        }

        List<Granum> FrontGrana(int n)
        {
            var l = new List<Granum>(grana);
            l.Sort((a, b) => (a.c.z + Mathf.Abs(a.c.x) * 0.15f).CompareTo(b.c.z + Mathf.Abs(b.c.x) * 0.15f));
            if (l.Count > n) l.RemoveRange(n, l.Count - n);
            return l;
        }

        // ================================================================== story API
        /// Outer membrane becomes a cutaway revealing the internal structure (double membrane peels away, cut rim glows).
        public IEnumerator Open()
        {
            if (!ready) yield break;
            foreach (var cap in new[] { capOuter, capInner })
            {
                cap.gameObject.SetActive(true);
                cap.localPosition = Vector3.zero; cap.localRotation = Quaternion.identity; cap.localScale = Vector3.one;
            }
            Fx.SetColor(capOuterMat, capOuterC); Fx.SetColor(capInnerMat, capInnerC);
            rim.SetActive(true); SetRim(0f);
            yield return Fx.Tween(2.5f, t =>
            {
                Peel(capOuter, capOuterMat, capOuterC, MicroKitB.Smooth01(t / 0.8f), 1f);
                Peel(capInner, capInnerMat, capInnerC, MicroKitB.Smooth01((t - 0.18f) / 0.8f), 0.75f);
                SetRim(MicroKitB.Smooth01((t - 0.2f) / 0.6f));
            }, Ease.Linear);
            capOuter.gameObject.SetActive(false); capInner.gameObject.SetActive(false);
            opened = true;
        }

        static void Peel(Transform cap, Material m, Color c, float u, float k)
        {
            cap.localPosition = new Vector3(0f, 1.0f * u * k, -2.6f * u * k);
            cap.localRotation = Quaternion.Euler(22f * u * k, 0f, 0f);
            cap.localScale = Vector3.one * (1f + 0.06f * u);
            Fx.SetColor(m, new Color(c.r, c.g, c.b, c.a * (1f - u)));
        }

        void SetRim(float v)
        {
            Fx.SetColor(rimMat, new Color(rimC.r, rimC.g, rimC.b, rimC.a * v)); Fx.SetEmission(rimMat, new Color(0.2f, 0.9f, 0.3f) * v);
            Fx.SetColor(bandMat, new Color(0.8f, 1f, 0.7f, 0.18f * v));
        }

        void EnsureOpen()
        {
            if (!opened) { rim.SetActive(true); SetRim(1f); capOuter.gameObject.SetActive(false); capInner.gameObject.SetActive(false); opened = true; }
        }

        public void HighlightChlorophyll(bool on) { hlGoal = on ? 1f : 0f; }

        /// Sunlight strikes the thylakoids; golden energy captured and flowing along the lamellae.
        public IEnumerator LightStrikes()
        {
            if (!ready) yield break;
            EnsureOpen();
            var targets = FrontGrana(4);
            var fx = new List<GameObject>(); var beams = new List<Transform>(); var hit = new bool[targets.Count];
            var shaft = MicroKitB.Beam(transform, LightFrom * 1.9f, new Vector3(0f, -0.6f, 0f), 6.5f, shaftMat); fx.Add(shaft);
            for (int i = 0; i < targets.Count; i++)
            {
                var g = targets[i]; var to = g.c + Vector3.up * g.hh; var from = to + LightFrom + new Vector3(i * 0.5f - 0.8f, 0, i * 0.3f);
                var b = MicroKitB.Beam(transform, from, to, 0.85f, beamMat); b.transform.localScale = new Vector3(1, 0, 1);
                beams.Add(b.transform); fx.Add(b);
                fx.Add(Beads.Create("Photons", transform, new[] { from, to }, 4, 0.13f, goldMat, 0.75f).gameObject);
            }
            transient.AddRange(fx);   // a debug jump / reset mid-strike must not leave beams behind
            if (!energy)
            {
                energy = Fx.Node("EnergyFlow", transform).gameObject;
                int k = 0;
                foreach (var path in lamellae) Beads.Create("Spark", energy.transform, path, 2, 0.09f, goldMat, 0.28f + 0.04f * (k++ % 4));
                energy.SetActive(false);
            }
            yield return Fx.Tween(3f, u =>
            {
                float clock = u * 3f, a = MicroKitB.Smooth01(clock / 0.5f) * (1f - MicroKitB.Smooth01((clock - 2.4f) / 0.6f));
                Fx.SetColor(shaftMat, new Color(1.6f, 1.35f, 0.7f, 0.22f * a)); Fx.SetColor(beamMat, new Color(2.2f, 1.8f, 0.9f, 0.6f * a));
                for (int i = 0; i < beams.Count; i++)
                {
                    float s = MicroKitB.Smooth01((clock - 0.2f - 0.18f * i) / 0.45f);
                    beams[i].localScale = new Vector3(1f, s, 1f);
                    if (s >= 1f && !hit[i])
                    {
                        hit[i] = true; flash = 1f; energy.SetActive(true);
                        Fx.Burst(W(targets[i].c + Vector3.up * targets[i].hh), Palette.Gold, 28, 0.07f, 2.2f, 0.9f);
                    }
                }
            }, Ease.Linear);
            foreach (var g in fx) Destroy(g);
            Fx.SetColor(beamMat, new Color(2.2f, 1.8f, 0.9f, 0.6f)); Fx.SetColor(shaftMat, new Color(1.6f, 1.35f, 0.7f, 0.22f));
        }

        /// Water arrives at the thylakoids, CO₂ arrives into the stroma around the cycle site.
        public IEnumerator InputsArrive()
        {
            if (!ready) yield break;
            var ins = MakeInputs();
            var arrived = new bool[ins.Count];
            yield return Fx.Tween(3f, u =>
            {
                float clock = u * 3f;
                for (int i = 0; i < ins.Count; i++)
                {
                    var (f, from, ctrl, isWater) = ins[i];
                    float s = Mathf.Clamp01((clock - 0.14f * i) / 1.9f);
                    f.t.localPosition = MicroKitB.Bez(from, ctrl, f.rest, Fx.Eval(s, Ease.InOut));
                    f.t.localRotation = Quaternion.Euler(clock * 70f + i * 30f, clock * 50f, 0f);
                    if (s >= 1f && !arrived[i]) { arrived[i] = true; Fx.Burst(f.t.position, isWater ? Palette.Water : Palette.CO2, 14, 0.05f, 1f, 0.8f); }
                }
            }, Ease.Linear);
            foreach (var x in ins) (x.isWater ? water : co2).Add(x.f);
        }

        /// Six H₂O placed in front of the front grana, six CO₂ on an arc around the cycle site (their rest positions).
        List<(Floater f, Vector3 from, Vector3 ctrl, bool isWater)> MakeInputs()
        {
            foreach (var l in new[] { water, co2 }) { foreach (var f in l) if (f.t) Destroy(f.t.gameObject); l.Clear(); }
            var o = new List<(Floater, Vector3, Vector3, bool)>(12);
            var tg = FrontGrana(6);
            for (int i = 0; i < 6; i++)
            {
                var g = tg[i % tg.Count];
                var rest = g.c + new Vector3(Mathf.Sin(i * 1.7f) * 0.35f, Mathf.Cos(i * 2.3f) * 0.3f, -(g.r + 0.38f));
                var from = new Vector3(-11f + i * 0.6f, -4.5f + (i % 3) * 1.2f, -6f - (i % 2));
                var m = Mol3(Mol.H2O, from, 0.4f, waterGlow, 1.4f);
                o.Add((new Floater { t = m.transform, rest = rest, ph = i * 1.3f }, from, (from + rest) * 0.5f + new Vector3(0, 2.2f, -1.5f), true));
            }
            for (int i = 0; i < 6; i++)
            {
                float a = (110f + i * 42f) * Mathf.Deg2Rad;
                var rest = Site + new Vector3(Mathf.Cos(a) * 2.0f, Mathf.Sin(a) * 1.55f, Mathf.Sin(i * 2.3f) * 0.35f);
                var from = new Vector3(11f - i * 0.5f, 4.5f + (i % 3) * 0.8f, -6f - (i % 2) * 1.2f);
                var m = Mol3(Mol.CO2, from, 0.38f, co2Glow, 1.4f);
                o.Add((new Floater { t = m.transform, rest = rest, ph = 7f + i * 1.1f }, from, (from + rest) * 0.5f + new Vector3(0, 1.8f, -1.2f), false));
            }
            return o;
        }

        /// Cold start (debug jump): the inputs appear at their rest positions.
        IEnumerator EnsureInputs()
        {
            if (water.Count > 0 && co2.Count > 0) yield break;
            var ins = MakeInputs();
            foreach (var x in ins) { x.f.t.localPosition = x.f.rest; x.f.t.localScale = Vector3.zero; (x.isWater ? water : co2).Add(x.f); }
            yield return Fx.Tween(0.6f, u => { foreach (var x in ins) x.f.t.localScale = Vector3.one * u; }, Ease.Out);
        }

        /// The learner sends the ingredients in. The carbon-fixing ring rises at the reaction site; every CO₂ and H₂O gets a
        /// collider and a pulsing halo (its group swells on hover). Clicking any molecule of a group flies the whole group in
        /// with trails: CO₂ docks on the ring, H₂O presses into the thylakoid stacks beside it (water is split there, so
        /// FormGlucose's O₂ bubbles and carriers start next to the ring). sent(true) CO₂ / sent(false) water fires on the click;
        /// returns once both groups have landed. auto() true sends the next group every 1.2 s. Works without InputsArrive.
        public IEnumerator FeedIngredients(CameraDirector cam, Func<bool> auto, Action<bool> sent)
        {
            if (!ready) yield break;
            if (!cam) cam = Module.I != null ? Module.I.cam : null;
            var au = Module.I != null ? Module.I.audioDirector : null;
            EnsureOpen();
            yield return EnsureInputs();
            bool newRing = !cycle;
            if (newRing) { CreateCycle(); Fx.Burst(W(Site), new Color(0.6f, 1f, 0.8f), 24, 0.06f, 1.4f, 0.8f); }
            var nearG = new List<Granum>(grana);
            nearG.Sort((a, b) => Vector3.Distance(a.c, Site).CompareTo(Vector3.Distance(b.c, Site)));
            int nn = Mathf.Min(3, nearG.Count);
            Vector3 Dock(int g, int i)
            {
                if (g == 0) return Site + Facing * (new Vector3(Mathf.Cos(2f + i * 1.05f), Mathf.Sin(2f + i * 1.05f), 0f) * 1.5f + Vector3.back * 0.3f);   // FormGlucose pulls each in from here
                var gr = nearG[i % nn]; int k = (i / nn) % 2;
                var d = (new Vector3(Site.x - gr.c.x, 0f, Site.z - gr.c.z).normalized + Vector3.back).normalized;   // the stack's face toward the ring and the camera
                return gr.c + Quaternion.Euler(0f, k == 0 ? -30f : 30f, 0f) * d * (gr.r + 0.1f) + Vector3.up * (gr.hh * (k == 0 ? 0.35f : -0.35f));
            }

            feed = new[] { new List<Floater>(co2), new List<Floater>(water) }; feedSent = new bool[2];   // 0 CO₂ · 1 H₂O
            var halo = new List<Transform>[2];
            for (int g = 0; g < 2; g++)
            {
                halo[g] = new List<Transform>();
                foreach (var f in feed[g])
                {
                    if (!f.t) { halo[g].Add(null); continue; }
                    f.t.gameObject.AddComponent<SphereCollider>().radius = 0.55f;
                    halo[g].Add(MicroKitB.Glow(f.t, Vector3.zero, 1.9f, g == 0 ? feedGlowC : feedGlowW).transform);
                }
            }
            FeedUnsubscribe(); feedClick = -1;
            if (cam) { clickCam = cam; cam.Clicked += OnFeedClick; }

            const float Fly = 1.1f, Gap = 0.09f;
            var fly = new List<(Floater f, Vector3 a, Vector3 c, float t0, int g)>();
            var launch = new[] { -1f, -1f }; var landed = new bool[2];
            float clk = 0f, autoT = 1.2f;   // the first auto send fires the moment auto() turns true (assist button / AI), no 1.2 s dead air
            void Launch(int g)
            {
                feedSent[g] = true; launch[g] = clk;
                sent?.Invoke(g == 0);
                if (au) au.Play(Sfx.Whoosh, 0.6f, g == 0 ? 1.15f : 1f);
                for (int i = 0; i < feed[g].Count; i++)
                {
                    var f = feed[g][i]; if (!f.t) continue;
                    var col = f.t.GetComponent<Collider>(); if (col) Destroy(col);   // never block later clicks
                    if (halo[g][i]) Destroy(halo[g][i].gameObject);
                    var tr = f.t.GetComponent<TrailRenderer>();
                    if (!tr) tr = MicroKitB.Trail(f.t.gameObject, g == 0 ? co2Trail : waterTrail, 0.12f, 0.5f);
                    tr.Clear(); tr.emitting = true;
                    var a = f.t.localPosition; f.rest = Dock(g, i); f.t.localScale = Vector3.one;
                    fly.Add((f, a, (a + f.rest) * 0.5f + new Vector3(0f, 1.4f, -1.6f), clk + i * Gap, g));
                }
            }

            while (!(landed[0] && landed[1]))
            {
                float prev = clk; clk += Module.Skipping ? 5f : Time.deltaTime;
                if (newRing && cycle) cycle.transform.localScale = Vector3.one * Fx.Eval(Mathf.Clamp01(clk / 0.7f), Ease.Back);
                int hov = -1; var mouse = Mouse.current;
                if (mouse != null && cam)
                {
                    var mp = mouse.position.ReadValue();
                    if (cam.UiBlocks == null || !cam.UiBlocks(mp)) hov = FeedPick(cam, mp, false);
                }
                if (auto != null && auto() && (autoT += Time.deltaTime) >= 1.2f) { autoT = 0f; if (feedClick < 0) feedClick = feedSent[0] ? 1 : 0; }
                if (feedClick >= 0 && !feedSent[feedClick]) Launch(feedClick);
                feedClick = -1;

                for (int g = 0; g < 2; g++)
                {
                    if (feedSent[g]) continue;
                    for (int i = 0; i < feed[g].Count; i++)
                    {
                        var f = feed[g][i]; if (!f.t) continue;
                        f.t.localScale = Vector3.Lerp(f.t.localScale, Vector3.one * (g == hov ? 1.25f : 1f), Time.deltaTime * 10f);
                        if (halo[g][i]) halo[g][i].localScale = Vector3.one * ((g == hov ? 1.35f : 1f) * (1.9f + 0.4f * Mathf.Sin(Time.time * 4f + f.ph)));
                    }
                }
                // Bob (Update) already moved each flyer to rest = its dock this frame: ending the curve there hands over without a jump
                foreach (var (f, a, c, t0, g) in fly)
                {
                    if (!f.t) continue;
                    float s = Mathf.Clamp01((clk - t0) / Fly), s0 = Mathf.Clamp01((prev - t0) / Fly);
                    if (s < 1f) f.t.localPosition = MicroKitB.Bez(a, c, f.t.localPosition, Fx.Eval(s, Ease.InOut));
                    else if (s0 < 1f)
                    {
                        Fx.Burst(f.t.position, g == 0 ? Palette.CO2 : Palette.Water, 10, 0.045f, 0.9f, 0.6f);
                        var tr = f.t.GetComponent<TrailRenderer>(); if (tr) tr.emitting = false;
                    }
                }
                for (int g = 0; g < 2; g++)
                {
                    if (landed[g] || launch[g] < 0f || clk < launch[g] + Gap * Mathf.Max(0, feed[g].Count - 1) + Fly) continue;
                    landed[g] = true;
                    if (au) au.Play(g == 0 ? Sfx.Pop : Sfx.WaterAbsorb, 0.85f);
                    Fx.Burst(g == 0 ? W(Site) : W(nearG[0].c), g == 0 ? Palette.CO2 : Palette.Water, 30, 0.07f, 1.8f, 0.9f);
                }
                if (!(landed[0] && landed[1])) yield return null;
            }
            if (newRing && cycle) cycle.transform.localScale = Vector3.one;
            FeedUnsubscribe(); feed = null;
        }

        void OnDisable() => FeedUnsubscribe();   // a debug jump can stop the Story mid-wait
        void FeedUnsubscribe() { if (clickCam) clickCam.Clicked -= OnFeedClick; clickCam = null; }
        // colliders follow the bobbing molecules only at the physics step; sync so the click ray hits what is drawn
        void OnFeedClick(Vector2 p)
        {
            if (feedClick >= 0 || !clickCam) return;
            Physics.SyncTransforms(); feedClick = FeedPick(clickCam, p, true);
            if (feedClick < 0 && Module.I && Module.I.audioDirector) Module.I.audioDirector.Play(Sfx.Wrong, 0.25f);   // a miss answers the click: soft "not there"
        }

        /// Group (0 CO₂ · 1 H₂O) of the unsent molecule under `screen`; `loose` also accepts the nearest one within ~70 px.
        int FeedPick(CameraDirector cd, Vector2 screen, bool loose)
        {
            if (feed == null) return -1;
            var cam = cd.Cam;
            if (Physics.Raycast(cam.ScreenPointToRay(screen), out var hit, 300f))
                for (int g = 0; g < 2; g++) if (!feedSent[g]) foreach (var f in feed[g]) if (f.t && hit.transform == f.t) return g;
            if (!loose) return -1;
            int best = -1; float bd = 70f * Screen.height / 1080f; bd *= bd;
            for (int g = 0; g < 2; g++)
            {
                if (feedSent[g]) continue;
                foreach (var f in feed[g])
                {
                    if (!f.t) continue;
                    Vector3 sp = cam.WorldToScreenPoint(f.t.position); if (sp.z <= 0f) continue;
                    float d = ((Vector2)sp - screen).sqrMagnitude; if (d < bd) { bd = d; best = g; }
                }
            }
            return best;
        }

        // ------------------------------------------------------------------ the reaction
        /// Light-energy activation → reaction process → new energy-rich molecule forming. Calls onUnknown when the
        /// unfinished molecule is first visible (scanner: UNKNOWN MOLECULE DETECTED); waits for `proceed()` before completing.
        /// Abstraction: light splits water at the thylakoids (O₂ bubbles) and sends golden carriers to the stroma, where a
        /// turning carbon-fixing cycle takes in CO₂ and assembles a new ring molecule that completes into glucose.
        public IEnumerator FormGlucose(Action onUnknown, Func<bool> proceed)
        {
            if (!ready) { onUnknown?.Invoke(); yield break; }
            bool fast = onUnknown == null || proceed == null;
            if (glucose) { Destroy(glucose); glucose = null; }
            EnsureOpen();
            Fx.SetColor(ghostMat, new Color(1f, 0.82f, 0.5f, 0.4f)); Fx.SetColor(outlineMat, new Color(2f, 1.5f, 0.6f, 0f));
            hlGoal = Mathf.Max(hlGoal, 0.5f);

            // 0 · inputs present (cold start: they appear at rest; after FeedIngredients: docked at the ring and the stacks beside it)
            yield return EnsureInputs();
            var ws = new List<Floater>(water); var cs = new List<Floater>(co2); water.Clear(); co2.Clear();

            // 1 · light activation: water split at the thylakoids (O₂ bubbles), golden carriers travel to the stroma cycle
            bool newRing = !cycle; if (newRing) CreateCycle();   // FeedIngredients may already have raised the ring
            Frame(new Vector3(1.4f, 2.9f, -10.8f), new Vector3(1.4f, 0.3f, -2.4f), 3f, 50f);   // push in on the reaction
            var shaft = MicroKitB.Beam(transform, LightFrom * 1.9f, new Vector3(0f, -0.6f, 0f), 6.5f, shaftMat); transient.Add(shaft);
            var carriers = new List<(Transform t, Vector3 a, Vector3 c, Vector3 b, float d)>();
            var fresh = new List<(Floater f, Vector3 from, float t0)>();
            var split = new bool[ws.Count]; var landed = new bool[ws.Count];
            flash = 1f;
            const float dur1 = 2.5f;
            yield return Fx.Tween(dur1, u =>
            {
                float clock = u * dur1;
                Fx.SetColor(shaftMat, new Color(1.6f, 1.35f, 0.7f, 0.22f * MicroKitB.Smooth01(clock / 0.4f) * (1f - MicroKitB.Smooth01((clock - dur1 + 0.5f) / 0.5f))));
                if (newRing) cycle.transform.localScale = Vector3.one * Fx.Eval(Mathf.Clamp01((clock - 0.3f) / 0.7f), Ease.Back);
                for (int i = 0; i < ws.Count; i++)
                {
                    var w = ws[i]; float s = Mathf.Clamp01((clock - 0.2f - i * 0.16f) / 0.3f);
                    if (w.t) w.t.localScale = Vector3.one * (1f - s);
                    if (s < 1f || split[i]) continue;
                    split[i] = true;
                    Fx.Burst(W(w.rest), new Color(0.75f, 0.95f, 1f), 18, 0.05f, 1.4f, 0.8f);
                    var b = Mol3(Mol.O2, w.rest, 0.24f, o2Glow, 0.9f);
                    Fx.MeshObj("Bubble", sphere, bubbleMat, b.transform).transform.localScale = Vector3.one * 0.62f;
                    fresh.Add((new Floater { t = b.transform, rest = w.rest + new Vector3(0.2f, 0.75f, -0.25f), ph = i * 2.1f }, w.rest, clock));
                    var ringPt = Site + Facing * new Vector3(Mathf.Cos(i * 1.05f), Mathf.Sin(i * 1.05f), 0f) * 1.25f;
                    carriers.Add((Orb(w.rest, 0.17f, goldMat, goldGlow, goldTrail).transform, w.rest, (w.rest + ringPt) * 0.5f + new Vector3(0f, 1.3f, -0.8f), ringPt, clock));
                }
                foreach (var (f, from, t0) in fresh) f.t.localPosition = Vector3.Lerp(from, f.rest, MicroKitB.Smooth01((clock - t0) / 1f));
                for (int i = 0; i < carriers.Count; i++)
                {
                    var (t, a, c, b, d) = carriers[i]; float s = Mathf.Clamp01((clock - d) / 0.9f);
                    t.localPosition = MicroKitB.Bez(a, c, b, Fx.Eval(s, Ease.InOut));
                    if (s >= 1f && !landed[i]) { landed[i] = true; t.localScale = Vector3.zero; Fx.Burst(W(b), Palette.Gold, 16, 0.06f, 1.4f, 0.7f); }
                }
            }, Ease.Linear);
            foreach (var (f, _, _) in fresh) { f.t.localPosition = f.rest; bubbles.Add(f); }
            foreach (var c in carriers) if (c.t) Destroy(c.t.gameObject);
            foreach (var w in ws) if (w.t) Destroy(w.t.gameObject);
            Destroy(shaft); Fx.SetColor(shaftMat, new Color(1.6f, 1.35f, 0.7f, 0.22f));

            // 2 · the cycle takes in CO₂ from the stroma
            var cFrom = new Vector3[cs.Count]; var absorbed = new bool[cs.Count];
            for (int i = 0; i < cs.Count; i++) cFrom[i] = cs[i].t ? cs[i].t.localPosition : cs[i].rest;
            yield return Fx.Tween(1.8f, u =>
            {
                float clock = u * 1.8f;
                for (int i = 0; i < cs.Count; i++)
                {
                    if (!cs[i].t) continue;
                    var ringPt = Site + Facing * new Vector3(Mathf.Cos(2f + i * 1.05f), Mathf.Sin(2f + i * 1.05f), 0f) * 1.25f;
                    float s = Mathf.Clamp01((clock - i * 0.16f) / 0.7f);
                    cs[i].t.localPosition = MicroKitB.Bez(cFrom[i], (cFrom[i] + ringPt) * 0.5f + Facing * Vector3.back * 0.6f, ringPt, Fx.Eval(s, Ease.InOut));
                    cs[i].t.localScale = Vector3.one * (1f - MicroKitB.Smooth01((s - 0.8f) / 0.2f));
                    if (s >= 1f && !absorbed[i]) { absorbed[i] = true; Fx.Burst(W(ringPt), new Color(0.8f, 1f, 0.9f), 16, 0.05f, 1.2f, 0.7f); }
                }
            }, Ease.Linear);
            foreach (var c in cs) if (c.t) Destroy(c.t.gameObject);

            // 3 · a new ring molecule assembles beside the cycle — semi-transparent, unfinished, until the learner proceeds
            var ghost = Fx.Node("FormingMolecule", transform, GluPos).transform; ghost.localRotation = Facing; transient.Add(ghost.gameObject);
            var geometry = GlucoseGeometry.Data; int totalAtoms = geometry.atoms.Length;
            var pos = new Vector3[totalAtoms]; var rad = new float[totalAtoms]; var outline = new List<Vector3>(6);
            for (int i = 0; i < totalAtoms; i++) { pos[i] = geometry.atoms[i].position; rad[i] = geometry.atoms[i].size; }
            foreach (int i in geometry.ring) outline.Add(pos[i] * GluSize);
            var bonds = new List<(int a, int b)>();
            foreach (var link in geometry.bonds) bonds.Add((link.a, link.b));
            Fx.MeshObj("Outline", outlineMesh ? outlineMesh : outlineMesh = MicroKitB.Loop(outline, 0.03f, 6), outlineMat, ghost);
            var src = ghost.InverseTransformPoint(W(Site));
            var atoms = new Transform[totalAtoms]; var launch = new float[totalAtoms];
            for (int i = 0; i < totalAtoms; i++) { atoms[i] = Fx.Prim(PrimitiveType.Sphere, ghost, src, Vector3.zero, ghostMat, false, "Atom").transform; launch[i] = -1f; }
            var bondT = new Transform[bonds.Count]; var grow = new float[bonds.Count];
            for (int i = 0; i < bonds.Count; i++)
            {
                Vector3 a = pos[bonds[i].a] * GluSize, b = pos[bonds[i].b] * GluSize;
                bondT[i] = Fx.Prim(PrimitiveType.Cylinder, ghost, (a + b) * 0.5f, Vector3.zero, ghostBondMat, false, "Bond").transform;
                bondT[i].localRotation = Quaternion.FromToRotation(Vector3.up, b - a); grow[i] = -1f;
            }
            float clk = 0f, nextAt = 0.35f, ghostA = 0.35f; int next = 0; bool told = false;
            while (true)
            {
                clk += Module.Skipping ? 5f : Time.deltaTime;
                bool go = fast || (told && proceed());
                int limit = !told ? 3 : go ? totalAtoms : 5;
                if (next < limit && clk >= nextAt) { launch[next++] = clk; nextAt = clk + (go ? 1.44f / totalAtoms : told ? 1.3f : 0.4f); }
                bool settled = true;
                for (int i = 0; i < totalAtoms; i++)
                {
                    if (launch[i] < 0f) continue;
                    float s = Mathf.Clamp01((clk - launch[i]) / 0.45f); if (s < 1f) settled = false;
                    var target = pos[i] * GluSize;
                    atoms[i].localPosition = MicroKitB.Bez(src, (src + target) * 0.5f + new Vector3(0f, 0.9f, -0.4f), target, Fx.Eval(s, Ease.Out));
                    atoms[i].localScale = Vector3.one * (rad[i] * GluSize * Mathf.Lerp(0.4f, 1f, s));
                }
                for (int i = 0; i < bonds.Count; i++)
                {
                    var (a, b) = bonds[i];
                    if (grow[i] < 0f && launch[a] >= 0f && launch[b] >= 0f && clk - Mathf.Max(launch[a], launch[b]) > 0.45f) grow[i] = clk;
                    if (grow[i] < 0f) continue;
                    float s = Mathf.Clamp01((clk - grow[i]) / 0.3f); if (s < 1f) settled = false;
                    bondT[i].localScale = new Vector3(0.12f * GluSize, Vector3.Distance(pos[a], pos[b]) * GluSize * 0.5f * s, 0.12f * GluSize);
                }
                ghostA = go && next >= totalAtoms ? Mathf.MoveTowards(ghostA, 0.9f, Time.deltaTime * 1.5f) : 0.3f + 0.1f * Mathf.Sin(clk * 6f);
                Fx.SetColor(ghostMat, new Color(1f, 0.82f, 0.5f, ghostA));
                Fx.SetColor(outlineMat, new Color(2f, 1.5f, 0.6f, Mathf.Min(1f, clk * 2f) * (0.4f + 0.15f * Mathf.Sin(clk * 4f))));
                ghost.localRotation = Facing * Quaternion.Euler(0f, 18f * Mathf.Sin(clk * 0.8f), 0f);
                if (!told && next >= 3 && settled) { told = true; onUnknown?.Invoke(); }
                if (next >= totalAtoms && settled) break;
                yield return null;
            }

            // 4 · completes into glucose with a flash; the cycle winds down
            Fx.Burst(W(GluPos), Palette.Gold, 40, 0.1f, 2.5f, 1.1f);
            var flashGo = MicroKitB.Glow(transform, GluPos, 0.1f, goldGlow); transient.Add(flashGo);
            EnsureGlucose(); var gt = glucose.transform; gt.localScale = Vector3.zero;
            float a0 = ghostA;
            yield return Fx.Tween(0.9f, u =>
            {
                flashGo.transform.localScale = Vector3.one * (7f * Mathf.Sin(u * Mathf.PI));
                gt.localScale = Vector3.one * Fx.Eval(Mathf.Clamp01(u / 0.7f), Ease.Back);
                ghost.localScale = Vector3.one * (1f + 0.3f * u);
                Fx.SetColor(ghostMat, new Color(1f, 0.82f, 0.5f, a0 * (1f - u)));
                Fx.SetColor(outlineMat, new Color(2f, 1.5f, 0.6f, 0.5f * (1f - u)));
                if (cycle) cycle.transform.localScale = Vector3.one * (1f - MicroKitB.Smooth01(u));
            }, Ease.Linear);
            Destroy(ghost.gameObject); Destroy(flashGo);
            if (cycle) { Destroy(cycle); cycle = null; }
            gt.localScale = Vector3.one;
        }

        void CreateCycle()
        {
            if (cycle) Destroy(cycle);
            cycle = Fx.Node("CarbonFixingCycle", transform, Site).gameObject;
            cycle.transform.localRotation = Facing; cycle.transform.localScale = Vector3.zero;
            var spin = Fx.Node("Spin", cycle.transform).transform;           // child 0 — turned in Update
            var ring = new List<Vector3>(48);
            for (int i = 0; i < 48; i++) { float a = i * Mathf.PI * 2f / 48f; ring.Add(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 1.25f); }
            Fx.MeshObj("Ring", ringMesh ? ringMesh : ringMesh = MicroKitB.Loop(ring, 0.07f, 8), cycleMat, spin);
            for (int i = 0; i < 6; i++) Fx.Prim(PrimitiveType.Sphere, spin, ring[i * 8], Vector3.one * 0.26f, nodeMat, false, "Stage");
            Beads.Create("Carbon", spin, new List<Vector3>(ring) { ring[0] }, 7, 0.11f, goldMat, 0.32f);
            MicroKitB.Glow(cycle.transform, Vector3.zero, 3.6f, cycleGlow);
        }

        /// Energy · growth · storage vignettes around the glucose.
        public IEnumerator GlucoseUses()
        {
            if (!ready) yield break;
            EnsureGlucose();
            Frame(new Vector3(3f, 3.9f, -11.2f), new Vector3(3f, 2f, -3f), 1.2f, 50f);   // frame the three vignettes
            var vig = new List<GameObject>();
            Vector3 g = GluPos, eP = g + new Vector3(-3.4f, 1.5f, -0.3f), grP = g + new Vector3(-0.2f, 2.5f, -0.2f), stP = g + new Vector3(3.3f, 0.4f, -0.4f);
            Transform Keep(GameObject o) { vig.Add(o); transient.Add(o); return o.transform; }
            Transform Unit() { var u = Keep(Fx.MeshObj("GlucoseUnit", MicroKitB.HexUnit, unitMat, transform)); u.localPosition = g; u.localRotation = Facing; u.localScale = Vector3.zero; return u; }

            // ENERGY — glucose is broken down and its energy powers a cell
            var cell = Keep(Fx.MeshObj("Cell", sphere, vigCellMat, transform)); cell.localPosition = eP; cell.localScale = Vector3.zero;
            Fx.Prim(PrimitiveType.Sphere, cell, new Vector3(0.14f, 0.06f, -0.05f), Vector3.one * 0.3f, Fx.Shared(MatKind.LitEmissive, new Color(0.6f, 0.4f, 0.9f), new Color(0.2f, 0.1f, 0.35f)));
            var fuel = Unit(); var sparks = new Transform[8];
            for (int i = 0; i < sparks.Length; i++) sparks[i] = Keep(Fx.Prim(PrimitiveType.Sphere, transform, eP, Vector3.zero, goldMat));
            bool boom = false;
            yield return Fx.Tween(1.7f, u =>
            {
                float c = u * 1.7f, s = Mathf.Clamp01(c / 0.6f), e = c < 0.6f ? 0f : Mathf.Exp(-(c - 0.6f) * 1.6f);
                cell.localScale = Vector3.one * (1.5f * Fx.Eval(Mathf.Clamp01(c / 0.4f), Ease.Back));
                fuel.localPosition = MicroKitB.Bez(g, (g + eP) * 0.5f + Vector3.up * 1.2f, eP, Fx.Eval(s, Ease.InOut));
                fuel.localScale = Vector3.one * (0.5f * (1f - MicroKitB.Smooth01((s - 0.75f) / 0.25f)));
                if (s >= 1f && !boom) { boom = true; Fx.Burst(W(eP), Palette.Gold, 60, 0.09f, 2.6f, 1f); }
                Fx.SetEmission(vigCellMat, new Color(0.03f, 0.12f, 0.04f) + new Color(2.2f, 1.6f, 0.4f) * e);
                for (int i = 0; i < sparks.Length; i++)
                {
                    float a = i * Mathf.PI / 4f + c * 4f, r = 0.95f + 0.1f * Mathf.Sin(c * 7f + i);
                    sparks[i].localPosition = eP + new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a * 1.3f) * 0.35f, Mathf.Sin(a) * r);
                    sparks[i].localScale = Vector3.one * (c < 0.6f ? 0f : 0.12f * (0.4f + 0.6f * e));
                }
            }, Ease.Linear);

            // GROWTH — glucose units link into a cellulose chain; fibrils lay down a new wall
            var chain = new Transform[6]; var links = new Transform[5]; var fibres = new Transform[2];
            for (int i = 0; i < 6; i++) chain[i] = Unit();
            for (int i = 0; i < 5; i++) { links[i] = Keep(Fx.Prim(PrimitiveType.Cylinder, transform, grP + new Vector3((i - 2f) * 0.5f, 0f, 0f), Vector3.zero, fibreMat)); links[i].localRotation = Quaternion.Euler(0, 0, 90); }
            for (int k = 0; k < 2; k++) { fibres[k] = Keep(Fx.Prim(PrimitiveType.Cylinder, transform, grP + Vector3.up * (k == 0 ? 0.42f : -0.42f), Vector3.zero, fibreMat)); fibres[k].localRotation = Quaternion.Euler(0, 0, 90); }
            var wall = Keep(Fx.Prim(PrimitiveType.Quad, transform, grP + new Vector3(0f, 0f, 0.3f), new Vector3(2.6f, 1.1f, 1f), vigWallMat)); wall.localRotation = Facing; wall.gameObject.SetActive(false);   // the fibres carry 'new wall'; the quad read as a dark box on the dark stroma
            yield return Fx.Tween(1.7f, u =>
            {
                float c = u * 1.7f, lay = MicroKitB.Smooth01((c - 1.0f) / 0.5f);
                for (int i = 0; i < 6; i++)
                {
                    var target = grP + new Vector3((i - 2.5f) * 0.5f, 0f, 0f);
                    float s = Mathf.Clamp01((c - i * 0.1f) / 0.55f);
                    chain[i].localPosition = MicroKitB.Bez(g, (g + target) * 0.5f + Vector3.up * 0.8f, target, Fx.Eval(s, Ease.InOut));
                    chain[i].localScale = Vector3.one * (0.38f * Mathf.Min(1f, s * 3f));
                }
                for (int i = 0; i < 5; i++) links[i].localScale = new Vector3(0.05f, 0.11f * MicroKitB.Smooth01((c - 0.65f - i * 0.1f) / 0.2f), 0.05f);
                for (int k = 0; k < 2; k++) fibres[k].localScale = new Vector3(0.06f, 1.6f * lay, 0.06f);
                Fx.SetColor(vigWallMat, new Color(0.7f, 1f, 0.6f, 0.14f * lay));
            }, Ease.Linear);

            // STORAGE — units coil (starch is helical) and pack into a starch granule
            var coil = new Transform[8];
            for (int i = 0; i < 8; i++) coil[i] = Unit();
            var granule = Keep(Fx.MeshObj("Starch", starchMesh, starchMat, transform)); granule.localPosition = stP; granule.localScale = Vector3.zero;
            yield return Fx.Tween(1.8f, u =>
            {
                float c = u * 1.8f, pack = MicroKitB.Smooth01((c - 1.05f) / 0.6f);
                for (int i = 0; i < 8; i++)
                {
                    float a = i * 0.9f + c * 2.2f, s = Mathf.Clamp01((c - i * 0.08f) / 0.5f);
                    var target = stP + new Vector3(Mathf.Cos(a) * 0.45f, (i - 3.5f) * 0.17f, Mathf.Sin(a) * 0.45f) * (1f - pack * 0.8f);
                    coil[i].localPosition = MicroKitB.Bez(g, (g + stP) * 0.5f + Vector3.up * 0.9f, target, Fx.Eval(s, Ease.InOut));
                    coil[i].localScale = Vector3.one * (0.3f * Mathf.Min(1f, s * 3f) * (1f - pack));
                    coil[i].localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f) * Facing;
                }
                granule.localScale = Vector3.one * (0.8f * Fx.Eval(pack, Ease.Back));
            }, Ease.Linear);

            // clear the vignettes — the glucose stays
            var scales = new Vector3[vig.Count];
            for (int i = 0; i < vig.Count; i++) scales[i] = vig[i] ? vig[i].transform.localScale : Vector3.zero;
            yield return Fx.Tween(0.6f, u => { for (int i = 0; i < vig.Count; i++) if (vig[i]) vig[i].transform.localScale = scales[i] * (1f - u); }, Ease.In);
            foreach (var o in vig) if (o) Destroy(o);
            Fx.SetEmission(vigCellMat, new Color(0.03f, 0.12f, 0.04f)); Fx.SetColor(vigWallMat, new Color(0.7f, 1f, 0.6f, 0f));
        }

        /// The O₂ that Scene 11's scanner names. It is null or destroyed outside ReleaseO2, so anchor callouts on it directly and they drop with it.
        public Transform HeroO2 { get; private set; }

        /// O₂ molecules move away from the thylakoids and out of the chloroplast (the water-splitting bubbles join them).
        /// The 4.6 s length covers s11_else + s11_oxygen, so the stream is still leaving while it is named. Routes end inside the pull-back frame.
        public IEnumerator ReleaseO2()
        {
            if (!ready) yield break;
            EnsureGlucose();
            Frame(new Vector3(1.5f, 3.6f, -13.2f), new Vector3(0.4f, 0.9f, -0.4f), 1.6f, 55f);   // pull back: O₂ leaves the whole chloroplast
            var movers = new List<(Transform t, Vector3 a, Vector3 c, Vector3 b, float d, bool grow)>();
            (Transform, Vector3, Vector3, Vector3, float, bool) Route(Transform t, Vector3 start, int i, bool grow)
            {
                var dir = new Vector3(start.x / E0.x * 1.4f + Mathf.Sin(i * 1.9f) * 0.4f, 0.9f, -0.6f).normalized;
                return (t, start, start + dir * 2.4f + Vector3.up * 0.5f, start + dir * (4.5f + (i % 3) * 0.8f), 0.06f * i, grow);   // was 7 + (i%3)*1.2, which flew out the top of the frame
            }
            int n = 0;
            foreach (var f in bubbles) if (f.t) movers.Add(Route(f.t, f.t.localPosition, n++, false));
            bubbles.Clear();
            for (int i = 0; i < 12 && grana.Count > 0; i++)
            {
                var g = grana[(i * 5) % grana.Count]; float a = i * 2.4f;
                var start = g.c + new Vector3(Mathf.Cos(a) * (g.r + 0.2f), Mathf.Sin(i * 1.3f) * g.hh, Mathf.Sin(a) * (g.r + 0.2f));
                bool hero = i == 0;
                var m = Mol3(Mol.O2, start, hero ? 0.42f : 0.3f, o2Glow, hero ? 1.3f : 1f, o2Trail, 0.1f); m.transform.localScale = Vector3.zero;
                var r = Route(m.transform, start, n++, true);
                if (hero) { HeroO2 = m.transform; r.Item5 = 1.2f; }   // the scanned one appears as the scanner reads it (about 1.7 s in Story) and leaves last
                movers.Add(r);
            }
            const float Dur = 4.6f, Travel = 3.4f;   // latest delay (hero, 1.2) + Travel = Dur
            yield return Fx.Tween(Dur, u =>
            {
                float c = u * Dur;
                foreach (var (t, a, ctl, b, d, grow) in movers)
                {
                    if (!t) continue;
                    float s = Mathf.Clamp01((c - d) / Travel);
                    t.localPosition = MicroKitB.Bez(a, ctl, b, Fx.Eval(s, Ease.InOut));
                    t.localScale = Vector3.one * ((grow ? MicroKitB.Smooth01(s * 6f) : 1f) * (1f - MicroKitB.Smooth01((s - 0.82f) / 0.18f)));
                    t.localRotation = Quaternion.Euler(c * 200f + d * 900f, c * 150f, 0f);
                }
            }, Ease.Linear);
            foreach (var mv in movers) if (mv.t) Destroy(mv.t.gameObject);
            HeroO2 = null;
        }
    }
}
