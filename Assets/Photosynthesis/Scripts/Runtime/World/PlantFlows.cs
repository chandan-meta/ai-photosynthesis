using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Photosynthesis
{
    /// Hero plant processes: continuous flows (bead streams, molecules, light motes, chloroplast sparkles), the energy
    /// wave, the scanner signal, idle ingredients and the floating leaf clone. Everything is built lazily on first use.
    public partial class HeroPlant
    {
        const int FlowCount = 7;
        readonly bool[] flowReq = new bool[FlowCount], flowLive = new bool[FlowCount], flowBuilt = new bool[FlowCount];
        readonly List<GameObject>[] flowObjs = { new List<GameObject>(), new List<GameObject>(), new List<GameObject>(), new List<GameObject>(), new List<GameObject>(), new List<GameObject>(), new List<GameObject>() };

        class Mover { public Transform t; public int leaf, sample; public float age, dur; public Vector3 off, spin; public Quaternion rot0; }
        readonly List<Mover> co2 = new List<Mover>(), o2 = new List<Mover>(), ingr = new List<Mover>();
        Transform molRoot; Material haloMat;
        bool ingrOn; float ingrW;

        ParticleSystem sunPS, chloroPS, glintPS;
        ParticleSystem.EmitParams ep;
        float sunAcc, chloroAcc; Vector3 sunDir = new Vector3(0.3f, 1f, -0.25f).normalized;
        readonly float[] gT = new float[96]; readonly Vector3[] gP = new Vector3[96]; int gNext;   // pending light-impact glints

        /// Continuous flows for demos / final working plant. Suppressed while dormant or not fully grown.
        public void SetFlow(Flow f, bool on) { EnsureBuilt(); flowReq[(int)f] = on; RefreshFlows(); }

        void RefreshFlows()
        {
            for (int i = 0; i < FlowCount; i++)
            {
                bool live = flowReq[i] && !dormant && growth >= 1f;
                if (live && !flowBuilt[i]) { flowBuilt[i] = true; BuildFlow((Flow)i); }
                if (live == flowLive[i]) continue;
                flowLive[i] = live;
                foreach (var g in flowObjs[i]) if (g) g.SetActive(live);
                if (!live && i == (int)Flow.Sunlight) for (int k = 0; k < gT.Length; k++) gT[k] = 0f;
                if (live && i == (int)Flow.Sunlight) sunDir = SunDir();
                if (live && (i == (int)Flow.CO2 || i == (int)Flow.O2)) foreach (var m in i == (int)Flow.CO2 ? co2 : o2) { Respawn(m, i == (int)Flow.CO2); m.age = Random.value * m.dur; }
            }
        }

        void BuildFlow(Flow f)
        {
            switch (f)
            {
                case Flow.Water: BuildWater(); break;
                case Flow.Phloem: BuildPhloemFlow(); break;
                case Flow.Glucose: BuildGlucose(); break;
                case Flow.CO2: BuildMovers(Flow.CO2); break;
                case Flow.O2: BuildMovers(Flow.O2); break;
                case Flow.Sunlight:
                    sunPS = MakePS("Sun Motes", 2.2f);
                    var r = sunPS.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.06f; r.lengthScale = 1.5f;
                    flowObjs[(int)f].Add(sunPS.gameObject); break;
                case Flow.Chloroplast: chloroPS = MakePS("Chloroplast Sparkles", 2.4f); flowObjs[(int)f].Add(chloroPS.gameObject); break;
            }
            foreach (var g in flowObjs[(int)f]) g.SetActive(false);
        }

        // ---------------------------------------------------------------- bead streams
        void Stream(Flow f, string name, Transform parent, List<Vector3> path, float spacing, float size, Material m, float mps, Mesh mesh)
        {
            if (f == Flow.Water || f == Flow.Phloem) size *= 1.3f;   // colour carries by area once the bloom halo shrinks (same bead count)
            float len = 0f; for (int i = 1; i < path.Count; i++) len += Vector3.Distance(path[i - 1], path[i]);
            if (len < 0.05f) return;
            var b = Beads.Create(name, parent, path, Mathf.Max(2, Mathf.RoundToInt(len / spacing)), size, m, mps / len, mesh);
            flowObjs[(int)f].Add(b.gameObject);
        }

        /// Water: roots → corm → three xylem strands up the stem → every petiole → midrib.
        void BuildWater()
        {
            var m = Fx.Shared(MatKind.LitEmissive, K(Palette.Water, 0.6f), K(Palette.Water, 1.05f));   // xylem water reads blue, not white
            foreach (var root in mainRoots) { var p = new List<Vector3>(root); p.Reverse(); p.Add(cormCenter); Stream(Flow.Water, "Water · Root", underground, p, 0.17f, 0.03f, m, 0.42f, beadMesh); }
            for (int k = 0; k < 3; k++)
            {
                var p = new List<Vector3>(12); for (int i = 0; i <= 11; i++) p.Add(Strand(k, true, Mathf.Lerp(-0.28f, 1.3f, i / 11f)));
                Stream(Flow.Water, "Water · Stem", pivot, p, 0.13f, 0.032f, m, 0.42f, beadMesh);
            }
            foreach (var lf in leaves) Stream(Flow.Water, "Water · Leaf", lf.root, LeafVesselPath(lf, -1, 0.85f), 0.16f, 0.03f, m, 0.42f, beadMesh);
        }

        /// Phloem: every leaf → stem, then stem → growing shoot, flower, fruit, storage corm and roots.
        void BuildPhloemFlow()
        {
            var m = Fx.Shared(MatKind.LitEmissive, K(Palette.Phloem, 0.6f), K(Palette.Phloem, 1.05f));   // phloem food reads amber, not gold
            foreach (var lf in leaves) { var p = LeafVesselPath(lf, 1, 0.7f); p.Reverse(); Stream(Flow.Phloem, "Phloem · Leaf", lf.root, p, 0.18f, 0.034f, m, 0.36f, beadMesh); }
            Vector3 S(float y) => Strand(1, false, y);
            var shoot = new List<Vector3> { S(0.95f), S(1.2f) }; for (int i = 0; i < shootPts.Count * 9 / 10; i++) shoot.Add(shootPts[i]);
            Stream(Flow.Phloem, "Phloem · Shoot", pivot, shoot, 0.16f, 0.034f, m, 0.36f, beadMesh);
            // flower and fruit sit below most leaf attachments, so their food comes down the stem
            var fl = new List<Vector3> { S(FlowerY + 0.3f), S(FlowerY) }; fl.AddRange(flowerPed); fl.Add(flowerPos);
            Stream(Flow.Phloem, "Phloem · Flower", pivot, fl, 0.16f, 0.034f, m, 0.36f, beadMesh);
            var fr = new List<Vector3> { Strand(2, false, FruitY + 0.3f), Strand(2, false, FruitY) }; fr.AddRange(fruitPed); fr.Add(fruitPos);
            Stream(Flow.Phloem, "Phloem · Fruit", pivot, fr, 0.16f, 0.034f, m, 0.36f, beadMesh);
            Stream(Flow.Phloem, "Phloem · Storage", underground, new List<Vector3> { Strand(0, false, 0.8f), Strand(0, false, 0.3f), Strand(0, false, -0.1f), cormCenter }, 0.15f, 0.036f, m, 0.36f, beadMesh);
            for (int r = 0; r < mainRoots.Count; r += 2)
            {
                var p = new List<Vector3> { Strand(r % 3, false, 0.6f), Strand(r % 3, false, 0.1f), cormCenter }; p.AddRange(mainRoots[r]);
                Stream(Flow.Phloem, "Phloem · Root", underground, p, 0.2f, 0.03f, m, 0.36f, beadMesh);
            }
        }

        /// Glucose: golden hexagons gathering along two veins and the midrib, leaving the blade into the petiole.
        void BuildGlucose()
        {
            var m = Fx.Shared(MatKind.LitEmissive, Palette.Glucose, K(Palette.Glucose, 2.4f));
            foreach (var lf in leaves)
                for (int path = 0; path < 2; path++)
                {
                    var vein = PlantLeafShape.Vein(path == 0 ? 2 : 5, path == 0 ? 1 : -1, 8);
                    var pts = new List<Vector3>(32);
                    for (int k = vein.Count - 1; k >= 0; k--) pts.Add(Surf(lf, vein[k].x, vein[k].y, 0.022f));
                    for (int k = 1; k <= 8; k++) pts.Add(Surf(lf, Mathf.Lerp(vein[0].x, 0f, k / 8f), 0f, 0.03f));
                    for (int k = lf.petiole.Count - 2; k >= lf.petiole.Count * 2 / 3; k--) pts.Add(lf.petiole[k]);
                    Stream(Flow.Glucose, "Glucose", lf.root, pts, 0.24f, 0.055f, m, 0.26f, hexMesh);
                }
        }

        static Vector3 Surf(LeafPart lf, float s, float x, float lift) => B2R(lf, lf.shape.Pos(s, x) + lf.shape.Normal(s, x) * lift);

        // ---------------------------------------------------------------- molecules (CO₂ in, O₂ out through the stomata)
        Transform MolRoot => molRoot ? molRoot : molRoot = Fx.Node("Molecules", transform).transform;

        GameObject Molecule(Mol kind, float size, bool halo)
        {
            var g = Fx.Molecule(kind, MolRoot, size, 0.8f); g.transform.localScale = Vector3.one * 0.001f;   // grows in on first update
            foreach (var mf in g.GetComponentsInChildren<MeshFilter>()) if (mf.name == "Atom") mf.sharedMesh = molSphere;   // ~4× fewer triangles than the primitive
            if (halo)
            {
                if (!haloMat) haloMat = Own(Fx.Mat(MatKind.Additive, new Color(Palette.Oxygen.r, Palette.Oxygen.g, Palette.Oxygen.b, 0.22f)));
                Fx.MeshObj("Halo", molSphere, haloMat, g.transform).transform.localScale = Vector3.one * size * 2.6f;
            }
            return g;
        }

        void BuildMovers(Flow f)
        {
            bool c = f == Flow.CO2; var list = c ? co2 : o2;
            for (int i = 0; i < leaves.Count; i++)
                for (int k = 0; k < 2; k++)
                {
                    var g = Molecule(c ? Mol.CO2 : Mol.O2, 0.06f, !c);
                    var mv = new Mover { t = g.transform, leaf = i }; Respawn(mv, c); mv.age = Random.value * mv.dur;
                    list.Add(mv); flowObjs[(int)f].Add(g);
                }
        }

        void Respawn(Mover m, bool co2Kind)
        {
            m.sample = Random.Range(0, leaves[m.leaf].undP.Length); m.age = 0f;
            m.dur = co2Kind ? Random.Range(3.2f, 4.6f) : Random.Range(3.6f, 5f);
            m.off = Random.insideUnitSphere * 0.35f; m.spin = Random.insideUnitSphere * 90f; m.rot0 = Random.rotation;
        }

        void UpdateMovers(List<Mover> list, bool co2Kind, float dt, float time)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var m = list[i]; m.age += dt;
                if (m.age >= m.dur) { if (co2Kind) Glint(m.t.position, Palette.CO2, 0.07f); Respawn(m, co2Kind); }
                var lf = leaves[m.leaf];
                Vector3 tp = lf.blade.TransformPoint(lf.undP[m.sample]), n = lf.blade.TransformDirection(lf.undN[m.sample]);
                float k = m.age / m.dur, fade; Vector3 p;
                if (co2Kind)
                {   // drifts in from below and is drawn into the underside
                    float e = 1f - (1f - k) * (1f - k);
                    p = Vector3.Lerp(tp + n * 0.95f + m.off, tp, e) + Vector3.up * (Mathf.Sin(time * 1.7f + i) * 0.03f * (1f - k));
                    fade = PlantMesh.Sstep(0f, 0.12f, k) * (1f - PlantMesh.Sstep(0.86f, 1f, k));
                }
                else
                {   // pops out of the underside, then rises away
                    p = tp + n * (0.45f * Mathf.Sqrt(k)) + Vector3.up * (1.5f * k * k) + m.off * k;
                    fade = PlantMesh.Sstep(0f, 0.15f, k) * (1f - PlantMesh.Sstep(0.7f, 1f, k));
                }
                m.t.SetPositionAndRotation(p, m.rot0 * Quaternion.Euler(m.spin * m.age));
                m.t.localScale = Vector3.one * Mathf.Max(0.001f, fade);
            }
        }

        /// CO₂ + H₂O molecules idling around the focus leaf (factory won't start).
        public void SetIngredientsIdle(bool on)
        {
            EnsureBuilt();
            if (on && ingr.Count == 0)
            {
                Vector3[] offs = { new Vector3(-0.15f, 0.18f, 0f), new Vector3(0.18f, -0.08f, 0.08f), new Vector3(-0.05f, -0.22f, -0.1f) };
                for (int i = 0; i < 6; i++)
                {
                    var g = Molecule(i < 3 ? Mol.CO2 : Mol.H2O, 0.1f, false);   // readable from the 4.5 m Clue 3 framing
                    var o = offs[i % 3]; if (i >= 3) o.x = -o.x;
                    ingr.Add(new Mover { t = g.transform, off = o, spin = Random.insideUnitSphere * 25f, rot0 = Random.rotation, age = i * 1.7f });
                }
            }
            if (on != ingrOn) ingrW = 0f;
            ingrOn = on;
            foreach (var m in ingr) m.t.gameObject.SetActive(on);
        }

        void UpdateIngredients(float dt, float t)
        {
            if (!ingrOn) return;
            ingrW = Mathf.MoveTowards(ingrW, 1f, dt / 0.8f);
            int f = FocusLeaf; Vector3 c = LeafCenter(f) + LeafNormal(f) * 0.3f;
            float s = Mathf.Max(0.001f, Fx.Eval(ingrW, Ease.Back));
            for (int i = 0; i < ingr.Count; i++)
            {
                var m = ingr[i];
                Vector3 p = c + (i < 3 ? Vector3.left : Vector3.right) * 0.6f + m.off
                          + new Vector3(Mathf.Sin(t * 0.5f + m.age) * 0.04f, Mathf.Sin(t * 0.8f + m.age * 1.3f) * 0.06f, Mathf.Cos(t * 0.6f + m.age) * 0.04f);
                m.t.SetPositionAndRotation(p, m.rot0 * Quaternion.Euler(m.spin * t));
                m.t.localScale = Vector3.one * s;
            }
        }

        // ---------------------------------------------------------------- particles (light motes, chloroplast sparkles, glints)
        ParticleSystem MakePS(string name, float hdr)
        {
            var ps = Fx.Particles(name, transform, new Fx.PCfg { rate = 0f, max = 900, noise = 0f, life = 1f });
            var mat = ps.GetComponent<ParticleSystemRenderer>().sharedMaterial; Own(mat);
            Fx.SetColor(mat, new Color(hdr, hdr, hdr, 1f));
            return ps;
        }

        ParticleSystem Glints => glintPS ? glintPS : glintPS = MakePS("Plant Glints", 2.8f);

        void Emit(ParticleSystem ps, Vector3 pos, Vector3 vel, float size, float life, Color c)
        {
            ep.position = pos; ep.velocity = vel; ep.startSize = size; ep.startLifetime = life; ep.startColor = c;
            ps.Emit(ep, 1);
        }

        void Glint(Vector3 pos, Color c, float size) => Emit(Glints, pos, Vector3.up * 0.05f, size, 0.45f, c);

        Vector3 SunDir()
        {
            var ch = Module.I ? Module.I.chamber : null;
            if (ch && ch.sun) { var d = -ch.sun.transform.forward; if (d.y > 0.25f) return d.normalized; }
            return new Vector3(0.3f, 1f, -0.25f).normalized;
        }

        void UpdateFlows(float dt, float t)
        {
            if (flowLive[(int)Flow.CO2]) UpdateMovers(co2, true, dt, t);
            if (flowLive[(int)Flow.O2]) UpdateMovers(o2, false, dt, t);
            if (flowLive[(int)Flow.Sunlight])
                for (sunAcc += dt * 75f; sunAcc >= 1f; sunAcc -= 1f)
                {   // golden light streaks fall onto the blades and flash where they land
                    var lf = leaves[Random.Range(0, leaves.Count)]; int k = Random.Range(0, lf.topP.Length);
                    Vector3 p = lf.blade.TransformPoint(lf.topP[k]);
                    float dist = Random.Range(2f, 3.8f), speed = 3.4f;
                    Emit(sunPS, p + sunDir * dist, -sunDir * speed, Random.Range(0.025f, 0.05f), dist / speed, Color.Lerp(Palette.Light, Color.white, Random.value * 0.5f));
                    gT[gNext] = Time.time + dist / speed; gP[gNext] = p + lf.blade.TransformDirection(lf.topN[k]) * 0.01f; gNext = (gNext + 1) % gT.Length;
                }
            if (flowLive[(int)Flow.Chloroplast])
                for (chloroAcc += dt * 70f; chloroAcc >= 1f; chloroAcc -= 1f)
                {   // green sparkles twinkling in the blade tissue (both faces)
                    var lf = leaves[Random.Range(0, leaves.Count)]; int k = Random.Range(0, lf.topP.Length);
                    Vector3 n = lf.blade.TransformDirection(lf.topN[k]);
                    Emit(chloroPS, lf.blade.TransformPoint(lf.topP[k]) + n * (Random.value < 0.5f ? 0.008f : -0.008f), Random.insideUnitSphere * 0.03f,
                         Random.Range(0.02f, 0.05f), Random.Range(0.7f, 1.5f), Color.Lerp(Palette.GreenBright, new Color(0.75f, 1f, 0.55f), Random.value));
                }
            float now = Time.time;
            for (int i = 0; i < gT.Length; i++) if (gT[i] > 0f && now >= gT[i]) { gT[i] = 0f; Glint(gP[i], Palette.Light, 0.1f); }
        }

        // ---------------------------------------------------------------- energy wave
        class Wave { public GameObject go; public Mesh mesh; public float[] dist; public Color32[] cols; public Vector3[] verts; public float petLen, maxD; }
        int waveLeaf = -1; float waveStart; Material waveMat;
        const float WaveRun = 1.45f, WaveFade = 0.9f;

        /// Golden energy racing from the petiole base out through the midrib and every vein (sunlight arrival).
        public IEnumerator EnergyWave(int leaf)
        {
            var lf = Lf(leaf);
            StopWave();
            if (lf.wave == null) lf.wave = BuildWave(lf);
            lf.wave.go.SetActive(true); waveLeaf = lf.index; waveStart = Time.time;
            UpdateWave();   // overwrite vertex alphas left over from an interrupted wave before this frame renders
            for (int i = 0; i < 16; i++) Glint(lf.root.position + Random.insideUnitSphere * 0.08f, Palette.Gold, 0.08f);
            yield return Fx.Wait(WaveRun);
            if (Module.Skipping && waveLeaf == lf.index) waveStart = Mathf.Min(waveStart, Time.time - WaveRun);
        }

        Wave BuildWave(LeafPart lf)
        {
            var b = new PlantMesh { tint = new Color32(255, 236, 170, 0) }; var sh = lf.shape; float L = sh.L;
            float petLen = 0f; for (int i = 1; i < lf.petiole.Count; i++) petLen += Vector3.Distance(lf.petiole[i - 1], lf.petiole[i]);
            b.Tube(lf.petiole, t => PetioleR(t) * 1.25f + 0.004f, 6, 0, 1f, 0f);
            var mid = new List<Vector3>(17); for (int k = 0; k <= 16; k++) mid.Add(B2R(lf, sh.Pos(0.95f * k / 16f, 0f)));
            b.Tube(mid, t => L * (0.012f * (1f - t) + 0.003f) * 1.5f, 6, 0, 1f, petLen);
            float maxD = petLen + 0.95f * L;
            var pts = new List<Vector3>(9); var ups = new List<Vector3>(9);
            for (int id = 0; id < PlantLeafShape.VeinIds; id++)
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    pts.Clear(); ups.Clear();
                    foreach (var q in PlantLeafShape.Vein(id, sd, 9)) { pts.Add(B2R(lf, sh.Pos(q.x, q.y))); ups.Add(lf.bladeRest * sh.Normal(q.x, q.y)); }
                    float d0 = petLen + PlantLeafShape.VeinStart(id) * L, w0 = id < PlantLeafShape.Veins ? 0.016f : 0.02f;
                    b.Ribbon(pts, ups, w0, 0.005f, 0.004f, 0, d0); b.Ribbon(pts, ups, w0, 0.005f, -0.004f, 0, d0);
                    float len = 0f; for (int i = 1; i < pts.Count; i++) len += Vector3.Distance(pts[i - 1], pts[i]);
                    maxD = Mathf.Max(maxD, d0 + len);
                }
            var mesh = Own(b.Build("EnergyWave")); mesh.MarkDynamic();
            if (!waveMat) waveMat = Own(Fx.Mat(MatKind.ParticleAdditive, K(Palette.Gold, 2.2f)));
            var go = Fx.MeshObj("Energy Wave", mesh, waveMat, lf.root); go.SetActive(false);
            return new Wave { go = go, mesh = mesh, dist = b.aux.ToArray(), cols = b.col.ToArray(), verts = b.v.ToArray(), petLen = petLen, maxD = maxD };
        }

        void UpdateWave()
        {
            if (waveLeaf < 0) return;
            var lf = leaves[waveLeaf]; var w = lf.wave; float el = Time.time - waveStart;
            if (el > WaveRun + WaveFade) { StopWave(); return; }
            float k = Mathf.Clamp01(el / WaveRun), p = Mathf.Lerp(-0.3f, w.maxD + 0.35f, k * (0.65f + 0.35f * k));   // accelerates outward
            float fade = el <= WaveRun ? 1f : 1f - (el - WaveRun) / WaveFade;
            for (int i = 0; i < w.dist.Length; i++)
            {
                float d = w.dist[i], x = (d - p) / 0.22f;
                float a = Mathf.Exp(-x * x) + (d < p ? 0.4f * Mathf.Exp(-(p - d) / 0.9f) : 0f);   // bright front + warm trail
                w.cols[i].a = (byte)(Mathf.Clamp01(a * fade) * 255f);
            }
            w.mesh.colors32 = w.cols;
            lf.flash = Mathf.Clamp01((p - w.petLen) / Mathf.Max(0.01f, w.maxD - w.petLen)) * fade;
            for (int j = 0; j < 4; j++) { int i = Random.Range(0, w.dist.Length); if (Mathf.Abs(w.dist[i] - p) < 0.15f) Glint(lf.root.TransformPoint(w.verts[i]), Palette.Gold, 0.07f); }
        }

        void StopWave()
        {
            if (waveLeaf < 0) return;
            var lf = leaves[waveLeaf]; lf.flash = 0f; if (lf.wave != null) lf.wave.go.SetActive(false);
            waveLeaf = -1;
        }

        // ---------------------------------------------------------------- scanner signal
        GameObject sigGo; Transform sigCore, sigHalo; readonly Transform[] sigRings = new Transform[2]; readonly Material[] sigRingM = new Material[2];
        Material sigCoreM, sigHaloM; Color sigColor; bool sigOn;

        /// Pulsing scanner signal sphere + expanding floor rings at a world point.
        public void Signal(Vector3 at, Color color, bool on)
        {
            EnsureBuilt(); sigOn = on;
            if (!on) { if (sigGo) sigGo.SetActive(false); return; }
            if (!sigGo)
            {
                sigGo = Fx.Node("Signal", transform);
                sigCoreM = Own(Fx.Mat(MatKind.Additive, color)); sigHaloM = Own(Fx.Mat(MatKind.Additive, color));
                sigCore = Fx.MeshObj("Core", molSphere, sigCoreM, sigGo.transform).transform;
                sigHalo = Fx.MeshObj("Halo", molSphere, sigHaloM, sigGo.transform).transform;
                var ring = Own(PlantMesh.Ring(64));
                for (int i = 0; i < 2; i++)
                {
                    sigRingM[i] = Own(Fx.Mat(MatKind.ParticleAdditive, color));
                    sigRings[i] = Fx.MeshObj("Ring", ring, sigRingM[i], sigGo.transform).transform; sigRings[i].localPosition = Vector3.up * 0.06f;
                }
            }
            sigColor = color; sigGo.transform.position = at; sigGo.SetActive(true);
            var c = K(color, 2.6f); c.a = 0.7f; Fx.SetColor(sigCoreM, c);
            c = K(color, 1.3f); c.a = 0.2f; Fx.SetColor(sigHaloM, c);
        }

        void UpdateSignal(float t)
        {
            if (!sigOn) return;
            // sized to swell out of the stem base (stem radius ≈ 0.18 m there), where the story places the signal
            float pulse = 0.5f + 0.5f * Mathf.Sin(t * 5f);
            sigCore.localScale = Vector3.one * (0.44f + 0.08f * pulse);
            sigHalo.localScale = Vector3.one * (0.95f + 0.3f * pulse);
            for (int i = 0; i < 2; i++)
            {
                float ph = Mathf.Repeat(t / 1.6f + i * 0.5f, 1f);
                sigRings[i].localScale = Vector3.one * Mathf.Lerp(0.3f, 3.0f, ph);
                var c = K(sigColor, 2.2f); c.a = Mathf.Pow(1f - ph, 1.5f); Fx.SetColor(sigRingM[i], c);
            }
        }

        // ---------------------------------------------------------------- floating copy
        Material[] cloneMats;
        readonly List<GameObject> clones = new List<GameObject>();

        /// Destroys clones the caller never cleaned up (e.g. a debug jump interrupted the beat that owned one).
        void DestroyClones()
        {
            foreach (var c in clones) if (c) Destroy(c);
            clones.Clear();
        }

        /// Stand-alone copy of a leaf (world space, no colliders). Pivot = leaf centre, +Z = midrib direction, +Y = upper surface.
        public GameObject CloneLeaf(int i)
        {
            clones.RemoveAll(c => !c);
            var lf = Lf(i); var sh = lf.shape;
            if (cloneMats == null)
            {
                var top = Own(Fx.Mat(MatKind.Leaf, Color.white, Color.black, 0.62f)); Fx.SetTexture(top, texTop);
                var und = Own(Fx.Mat(MatKind.Leaf, Color.white, Color.black, 0.3f)); Fx.SetTexture(und, texUnder);
                var pet = Own(Fx.Mat(MatKind.LitEmissive, Color.white, Color.black, 0.45f)); Fx.SetTexture(pet, texPetiole);
                cloneMats = new[] { top, und, pet };
            }
            if (!lf.stub)
            {   // last ~30 cm of the petiole, in blade space
                var pts = new List<Vector3>(); float acc = 0f; var inv = Quaternion.Inverse(lf.bladeRest);
                for (int k = lf.petiole.Count - 1; k >= 0 && acc < 0.3f; k--)
                {
                    if (k < lf.petiole.Count - 1) acc += Vector3.Distance(lf.petiole[k], lf.petiole[k + 1]);
                    pts.Insert(0, inv * (lf.petiole[k] - lf.bladePos));
                }
                var b = new PlantMesh(); b.Tube(pts, t => PetioleR(1f) + 0.004f * (1f - t), 8, 0);
                lf.stub = Own(b.Build("PetioleStub"));
            }
            Vector3 fwd = lf.blade.TransformDirection(sh.Pos(0.47f, 0.05f) - sh.Pos(0.43f, 0.05f)).normalized;
            var g = new GameObject("Leaf Clone");
            g.transform.SetPositionAndRotation(LeafCenter(i), Quaternion.LookRotation(fwd, LeafNormal(i)));
            var blade = Fx.MeshObj("Blade", lf.mesh, null, g.transform, true);
            blade.GetComponent<MeshRenderer>().sharedMaterials = cloneMats;
            blade.transform.SetPositionAndRotation(lf.blade.position, lf.blade.rotation); blade.transform.localScale = lf.blade.lossyScale;
            Fx.MeshObj("Petiole", lf.stub, cloneMats[2], blade.transform, true);
            clones.Add(g);
            return g;
        }
    }
}
