using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using K = Photosynthesis.MicroKitA;
using Object = UnityEngine.Object;

namespace Photosynthesis
{
    /// Clue 01 & oxygen exit: leaf underside at 10× (epidermal cell mosaic with many stomata) and 100× (one stoma:
    /// two kidney-shaped guard cells around a pore, leaf interior visible through it).
    /// One continuous world, so 10× → 100× is a real camera zoom: the hero stoma's pore is at the local origin, air is +y,
    /// the leaf interior (substomatal cavity + spongy mesophyll) is −y and only visible through the pore.
    /// Epidermis = jigsaw Voronoi heightfield (domain-warped) with a matching procedural texture; a dense patch around
    /// the hero stoma keeps 100× crisp. The epidermis casts sun shadows, so light reaches the interior only through the pore.
    /// Molecule paths below the surface stay inside the slanted window the 100× camera sees through the pore
    /// (checked by ray-marching the guard-cell tubes), so CO₂ visibly sinks into — and O₂ rises out of — the interior.
    [DefaultExecutionOrder(1000)]   // LateUpdate after CameraDirector: the freeze tint never lags the camera
    public class LeafSurfaceWorld : MicroWorld
    {
        const float CellSize = 2.4f, Extent = 22f, PatchHalf = 4.5f, Coarse = 0.25f, StomaGrid = 5.2f;
        const float GA = 1.2f, GB = 0.66f, Collar = 0.07f, GuardY = 0.17f;   // guard-cell centre-line ellipse; heights above stoma base
        const int Sites = 32, SiteOff = 16;
        static readonly Vector3 L1Pos = new Vector3(2.5f, 17f, -10f), L1Look = new Vector3(0f, 0f, 2f);
        static readonly Vector3 L2Pos = new Vector3(0f, 4.3f, -2.4f), L2Look = new Vector3(0f, -0.35f, 0.05f);
        static readonly Vector3 BowlC = new Vector3(0f, -2.75f, 0f), BowlR = new Vector3(4.4f, 2.6f, 3.8f);
        static readonly Color Fog = new Color(0.35f, 0.49f, 0.36f), GuardBase = new Color(0.015f, 0.055f, 0.008f), GuardHot = new Color(0.09f, 0.3f, 0.07f);
        // starts in the upper-left of the 100× frame, threads the pore, settles in the cavity in view of the camera
        static readonly Vector3[] CO2Path =
        {
            new Vector3(-1.45f, 1.35f, 0.3f), new Vector3(-1.1f, 0.85f, 0.18f), new Vector3(-0.22f, 0.3f, 0f),
            new Vector3(-0.05f, -0.75f, 0.5f), new Vector3(0.3f, -1.75f, 1.1f)
        };

        public Transform Stoma => stoma ? stoma : transform;
        public Transform GuardCells => guards ? guards : transform;
        public Transform HighlightedMolecule => hero ? hero : transform;

        readonly List<Object> owned = new List<Object>();
        bool ready, ambientOn, flowsOn, frozen, guardHL, heroResting, dimmed;
        int level = 1, co2Run, o2Run, beepRun;
        float clock, flowT, frostA, ambK;

        float[,] siteX, siteZ, siteT;
        float[] stX, stZ, stA, stY;          // stomata: position, yaw (rad), base height (index 0 = hero at origin)
        int[,] stGrid;

        Transform stoma, guards, hero, molRoot, pingRoot;
        Transform[] pings, amb, o2, flowIn, flowOut;
        Vector3[] ambHome; Vector3[][] o2Paths, inPaths, outPaths;
        Renderer[] ambR; Material[][] ambMats;   // full material sets: atoms carry a second (rim) material
        Material guardMat, rimMat, pingMat, poreGlowMat, frostMat, dimMat;
        GameObject rim, poreGlow, frost, heroHalo;
        TrailRenderer trail;
        readonly List<ParticleSystem> particles = new List<ParticleSystem>();
        K.SunOverride sun;
        CameraDirector camDir;

        T Own<T>(T o) where T : Object { owned.Add(o); return o; }
        void OnDestroy() { foreach (var o in owned) if (o) Destroy(o); }
        void EnsureBuilt() { if (!ready) Build(); }

        // ================================================================== contract
        protected override void Build()
        {
            if (ready) return;
            ready = true;
            sun = gameObject.AddComponent<K.SunOverride>();
            MakeSites(); PlaceStomata(); MakePaths();
            BuildEpidermis(); BuildStomata(); BuildInterior(); BuildAir(); BuildMolecules();
            ResetWorld();
        }

        protected override void ApplyMood()
        {
            K.Mood(Fog, 0.022f, new Color(0.62f, 0.74f, 0.72f), new Color(0.4f, 0.52f, 0.47f), new Color(0.15f, 0.2f, 0.16f));
            sun.Apply(1.45f, new Color(1f, 0.95f, 0.86f), new Vector3(58f, -60f, 0f));
        }

        public override (Vector3 pos, Vector3 look) Pose() => Pose(level);

        /// 1 = 10× overview, 2 = 100× single stoma.
        public void SetMagnification(int level) => this.level = Mathf.Clamp(level, 1, 2);
        public (Vector3 pos, Vector3 look) Pose(int level) =>
            level >= 2 ? (Origin + L2Pos, Origin + L2Look) : (Origin + L1Pos, Origin + L1Look);

        public override void ResetWorld()
        {
            if (!ready) return;
            co2Run++; o2Run++; beepRun++;
            level = 1; frozen = false; frostA = 0f; frost.SetActive(false);
            foreach (var ps in particles) ps.Play();
            AmbientCO2(false); BothFlows(false); SetGuardHighlight(false); SetDim(false);
            trail.emitting = false; heroResting = false;
            hero.localPosition = CO2Path[0]; hero.localScale = Vector3.one; trail.Clear(); hero.gameObject.SetActive(false);
            foreach (var o in o2) o.gameObject.SetActive(false);
            poreGlow.SetActive(false); pingRoot.gameObject.SetActive(false);
        }

        /// Scanner pings the pores three times (BEEP BEEP BEEP), pores pulse.
        public IEnumerator Beep()
        {
            EnsureBuilt();
            int run = ++beepRun;
            poreGlow.SetActive(true); pingRoot.gameObject.SetActive(true);
            var au = Module.I ? Module.I.audioDirector : null;
            const float period = 0.8f; int pinged = -1;
            for (float t = 0; t < period * 3; t += Time.deltaTime)
            {
                if (run != beepRun) yield break;
                if (Module.Skipping) break;
                int k = Mathf.Min(2, (int)(t / period));
                if (k != pinged) { pinged = k; if (au) au.Play(Sfx.Beep, 0.8f, 1f + 0.06f * k); }
                Ping(t / period - k);
                yield return null;
            }
            if (run != beepRun) yield break;
            poreGlow.SetActive(false); pingRoot.gameObject.SetActive(false);
        }

        public void SetGuardHighlight(bool on)
        {
            if (!ready) return;
            guardHL = on; rim.SetActive(on);
            if (!on) Fx.SetEmission(guardMat, GuardBase);
        }

        public void AmbientCO2(bool on)
        {
            if (!ready) return;
            if (on && ambientOn) return;
            ambientOn = on; ambK = 0f;   // fades in from nothing (Update) instead of popping
            foreach (var a in amb) { a.localScale = Vector3.zero; a.gameObject.SetActive(on); }
        }

        /// One highlighted CO₂ travels through the stoma into the leaf interior; other detail fades.
        public IEnumerator CO2Enter()
        {
            EnsureBuilt();
            int run = ++co2Run;
            heroResting = false; trail.emitting = false;
            hero.localPosition = CO2Path[0]; trail.Clear(); hero.gameObject.SetActive(true);
            SetDim(true);
            const float dur = 4f;
            for (float t = 0; t < dur; t += frozen ? 0f : Time.deltaTime)
            {
                if (run != co2Run) yield break;
                if (Module.Skipping) break;
                PlaceHero(Fx.Eval(t / dur, Ease.InOut));
                trail.emitting = t > 0.05f;
                yield return null;
            }
            if (run != co2Run) yield break;
            PlaceHero(1f); trail.emitting = false; heroResting = true; SetDim(false);
        }

        /// O₂ leaves through the same stoma into the atmosphere.
        public IEnumerator O2Exit()
        {
            EnsureBuilt();
            int run = ++o2Run;
            const float each = 2.3f, gap = 0.32f; float total = each + gap * (o2.Length - 1);
            for (float t = 0; t < total; t += frozen ? 0f : Time.deltaTime)
            {
                if (run != o2Run) yield break;
                if (Module.Skipping) break;
                for (int i = 0; i < o2.Length; i++) Travel(o2[i], o2Paths[i], (t - gap * i) / each);
                yield return null;
            }
            if (run != o2Run) yield break;
            foreach (var o in o2) o.gameObject.SetActive(false);
        }

        /// Continuous CO₂ in + O₂ out together ("same doorway, different journey").
        public void BothFlows(bool on)
        {
            if (!ready) return;
            flowsOn = on;
            foreach (var m in flowIn) m.gameObject.SetActive(on);
            foreach (var m in flowOut) m.gameObject.SetActive(on);
        }

        /// "The environment freezes": motion pauses, view tints cool and desaturates.
        public void Freeze(bool on)
        {
            if (!ready) return;
            frozen = on;
            foreach (var ps in particles) { if (on) ps.Pause(); else ps.Play(); }
        }

        // ================================================================== per frame
        void Update()
        {
            float dt = frozen ? 0f : Time.deltaTime;
            clock += dt;
            if (ambientOn)
            {
                ambK = Mathf.MoveTowards(ambK, 1f, dt * 1.4f);
                Vector3 grow = Vector3.one * K.Smooth01(ambK);
                var lens = Module.I && Module.I.cam ? Module.I.cam.transform.position : Vector3.zero;   // during CO2Enter the ambient CO₂ near the lens shrinks away from the hero
                for (int i = 0; i < amb.Length; i++)
                {
                    float s = i * 7.31f;
                    amb[i].localPosition = ambHome[i] + new Vector3(Mathf.PerlinNoise(clock * 0.11f, s) - 0.5f,
                        (Mathf.PerlinNoise(s, clock * 0.09f) - 0.5f) * 0.6f, Mathf.PerlinNoise(clock * 0.1f + s, s * 0.5f) - 0.5f) * 1.5f;
                    amb[i].localRotation = Quaternion.Euler(clock * (20 + i * 3 % 25), clock * (30 + i * 7 % 40), i * 40f);
                    float near = dimmed ? Mathf.Clamp01((Vector3.Distance(amb[i].position, lens) - 1.2f) / 1.4f) : 1f;
                    amb[i].localScale = grow * near;
                }
            }
            if (flowsOn)
            {
                flowT += dt;
                for (int i = 0; i < flowIn.Length; i++) Travel(flowIn[i], inPaths[i], Mathf.Repeat(flowT / 3.6f + i / (float)flowIn.Length, 1f));
                for (int i = 0; i < flowOut.Length; i++) Travel(flowOut[i], outPaths[i], Mathf.Repeat(flowT / 3.6f + (i + 0.5f) / flowOut.Length, 1f));
            }
            if (heroResting) hero.localPosition = CO2Path[4] + new Vector3(Mathf.Sin(clock * 0.7f), Mathf.Sin(clock * 0.9f + 1f) * 0.6f, Mathf.Cos(clock * 0.6f)) * 0.12f;
            if (hero.gameObject.activeSelf) heroHalo.transform.localScale = Vector3.one * (1.45f + 0.12f * Mathf.Sin(clock * 5f));
            if (guardHL)
            {
                float p = 1f + 0.3f * Mathf.Sin(clock * 3.2f);   // clock: the pulse holds still while frozen
                Fx.SetEmission(guardMat, GuardHot * p);
                Fx.SetColor(rimMat, new Color(Palette.Cyan.r * 1.4f * p, Palette.Cyan.g * 1.4f * p, Palette.Cyan.b * 1.4f * p, 1f));
            }
            frostA = Mathf.MoveTowards(frostA, frozen ? 0.22f : 0f, Time.deltaTime * 0.7f);
            if (frost.activeSelf != frostA > 0.001f) frost.SetActive(frostA > 0.001f);
            if (frostA > 0.001f) Fx.SetAlpha(frostMat, frostA);
        }

        void LateUpdate()
        {
            if (!frost.activeSelf) return;
            if (!camDir) { camDir = Module.I ? Module.I.cam : null; if (!camDir) return; }
            var c = camDir.Cam; var t = c.transform; float d = c.nearClipPlane + 0.02f;
            float h = 2f * d * Mathf.Tan(c.GetGateFittedFieldOfView() * 0.5f * Mathf.Deg2Rad) * 1.3f;   // gate-fitted: full cover on 4:3 / 5:4 too
            frost.transform.SetPositionAndRotation(t.position + t.forward * d, t.rotation);
            frost.transform.localScale = new Vector3(h * c.aspect, h, 1f);
        }

        void PlaceHero(float u)
        {
            hero.localPosition = Fx.CatmullRom(CO2Path, u);
            var tumble = Quaternion.Euler(u * 420f, u * 260f + 20f, 35f);
            var aligned = Quaternion.Euler(clock * 140f, 0f, 0f);   // long axis along the pore slit
            hero.localRotation = Quaternion.Slerp(tumble, aligned, K.Smooth01((u - 0.3f) / 0.22f));
        }

        /// Moves a pooled molecule along a 5-point path; u outside 0..1 hides it. Fades in/out at the ends.
        void Travel(Transform m, Vector3[] path, float u)
        {
            bool on = u > 0f && u < 1f;
            if (m.gameObject.activeSelf != on) m.gameObject.SetActive(on);
            if (!on) return;
            m.localPosition = Fx.CatmullRom(path, Fx.Eval(u, Ease.InOut) * 0.8f + u * 0.2f);
            m.localScale = Vector3.one * (Mathf.Clamp01(u * 7f) * Mathf.Clamp01((1f - u) * 6f));
            m.localRotation = Quaternion.Euler(clock * 90f + u * 220f, u * 160f, 0f);
        }

        void Ping(float p)
        {
            float flash = Mathf.Exp(-p * 4.5f) * 3.6f + 0.35f;
            Fx.SetColor(poreGlowMat, new Color(Palette.Cyan.r * flash, Palette.Cyan.g * flash, Palette.Cyan.b * flash, 1f));
            float a = Mathf.Pow(1f - p, 1.5f), s = Mathf.Lerp(0.6f, 2.7f, Fx.Eval(p, Ease.Out));
            Fx.SetColor(pingMat, new Color(Palette.Cyan.r * 3f, Palette.Cyan.g * 3f, Palette.Cyan.b * 3f, a));
            for (int i = 0; i < pings.Length; i++) pings[i].localScale = new Vector3(1.35f * s, 1f, 1.1f * s);
        }

        void SetDim(bool on)
        {
            if (dimmed == on) return;
            dimmed = on;
            for (int i = 0; i < ambR.Length; i++) ambR[i].sharedMaterials = on ? new[] { dimMat } : ambMats[i];
        }

        // ================================================================== epidermis
        void MakeSites()
        {
            siteX = new float[Sites, Sites]; siteZ = new float[Sites, Sites]; siteT = new float[Sites, Sites];
            for (int j = 0; j < Sites; j++)
                for (int i = 0; i < Sites; i++)
                {
                    int ix = i - SiteOff, iz = j - SiteOff;
                    siteX[i, j] = ix + 0.12f + 0.76f * K.Hash(ix, iz, 1); siteZ[i, j] = iz + 0.12f + 0.76f * K.Hash(ix, iz, 2); siteT[i, j] = K.Hash(ix, iz, 3);
                }
        }

        /// Distance (world units) from (x,z) to the wall of its jigsaw epidermal cell, plus that cell's tint 0..1.
        void CellAt(float x, float z, out float edge, out float tint)
        {
            float wx = x + 0.3f * Mathf.Sin(z * 2.1f + 1.7f * Mathf.Sin(x * 0.9f)), wz = z + 0.3f * Mathf.Sin(x * 2.3f + 1.5f * Mathf.Cos(z * 1.1f));
            float gx = wx / CellSize, gz = wz / CellSize;
            int cx = Mathf.Clamp(Mathf.FloorToInt(gx) + SiteOff, 2, Sites - 3), cz = Mathf.Clamp(Mathf.FloorToInt(gz) + SiteOff, 2, Sites - 3);
            float best = float.MaxValue, bx = 0, bz = 0; int bi = cx, bj = cz;
            for (int j = cz - 1; j <= cz + 1; j++)
                for (int i = cx - 1; i <= cx + 1; i++)
                {
                    float dx = siteX[i, j] - gx, dz = siteZ[i, j] - gz, d = dx * dx + dz * dz;
                    if (d < best) { best = d; bx = siteX[i, j]; bz = siteZ[i, j]; bi = i; bj = j; }
                }
            bi = Mathf.Clamp(bi, 1, Sites - 2); bj = Mathf.Clamp(bj, 1, Sites - 2);
            edge = float.MaxValue;
            for (int j = bj - 1; j <= bj + 1; j++)
                for (int i = bi - 1; i <= bi + 1; i++)
                {
                    float nx = siteX[i, j] - bx, nz = siteZ[i, j] - bz, len = Mathf.Sqrt(nx * nx + nz * nz);
                    if (len < 1e-5f) continue;
                    float d = (((bx + siteX[i, j]) * 0.5f - gx) * nx + ((bz + siteZ[i, j]) * 0.5f - gz) * nz) / len;
                    if (d < edge) edge = d;
                }
            edge *= CellSize; tint = siteT[bi, bj];
        }

        /// Gentle rolling of the leaf surface, flat around the hero stoma, bending away at the far edges (soft horizon).
        static float Undulate(float x, float z)
        {
            float r = Mathf.Sqrt(x * x + z * z), edge = K.Smooth01((Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) - 15f) / 7f);
            return (0.5f * Mathf.Sin(x * 0.16f + 0.7f) * Mathf.Sin(z * 0.13f + 1.9f) + 0.32f * Mathf.Sin(x * 0.07f - z * 0.09f + 0.4f)) * K.Smooth01((r - 3f) / 7f)
                   - 5f * edge * edge;
        }

        float Height(float x, float z)
        {
            CellAt(x, z, out float e, out float tint);
            float b = Mathf.Clamp01((e < 0.2f ? e * e / 0.4f : e - 0.1f) / 0.7f);   // rounded wall groove (C1 at the wall): no zig-zag crease on the grid; same dome from e ≥ 0.8
            float h = Undulate(x, z) + 0.38f * (1 - (1 - b) * (1 - b)) * (0.8f + 0.4f * tint);
            float w = StomaWeight(x, z, out float collar);
            return w > 0f ? Mathf.Lerp(h, collar, w) : h;
        }

        /// 1 inside a stoma's footprint (flat collar the guard cells sit on), easing to 0 just outside.
        float StomaWeight(float x, float z, out float collar)
        {
            collar = 0f; float best = 0f;
            Consider(0, x, z, ref best, ref collar);
            int gx = Mathf.FloorToInt((x + Extent) / StomaGrid), gz = Mathf.FloorToInt((z + Extent) / StomaGrid), n = stGrid.GetLength(0);
            for (int j = gz - 1; j <= gz + 1; j++)
                for (int i = gx - 1; i <= gx + 1; i++)
                    if (i >= 0 && j >= 0 && i < n && j < n && stGrid[i, j] > 0) Consider(stGrid[i, j], x, z, ref best, ref collar);
            return best;
        }

        void Consider(int s, float x, float z, ref float best, ref float collar)
        {
            float dx = x - stX[s], dz = z - stZ[s], c = Mathf.Cos(stA[s]), sn = Mathf.Sin(stA[s]);
            float lx = dx * c - dz * sn, lz = dx * sn + dz * c;     // world → stoma-local (stoma yaw = stA)
            float m = lx * lx / (1.45f * 1.45f) + lz * lz / (1.25f * 1.25f);
            float w = 1f - K.Smooth01((m - 1f) / 0.7f);
            if (w > best) { best = w; collar = stY[s] + Collar; }
        }

        void PlaceStomata()
        {
            var r = new System.Random(7);
            int n = Mathf.CeilToInt(2 * Extent / StomaGrid);
            stGrid = new int[n, n];
            var xs = new List<float> { 0f }; var zs = new List<float> { 0f }; var ang = new List<float> { 0f };
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    stGrid[i, j] = -1;
                    float x = -Extent + (i + 0.5f) * StomaGrid + K.Rand(r, -1.5f, 1.5f), z = -Extent + (j + 0.5f) * StomaGrid + K.Rand(r, -1.5f, 1.5f);
                    float a = K.Rand(r, -0.6f, 0.6f);   // stomata loosely share an orientation, as on a real leaf
                    if (r.NextDouble() < 0.2 || Mathf.Abs(x) > Extent - 5f || Mathf.Abs(z) > Extent - 5f || x * x + z * z < 4.9f * 4.9f) continue;
                    stGrid[i, j] = xs.Count; xs.Add(x); zs.Add(z); ang.Add(a);
                }
            stX = xs.ToArray(); stZ = zs.ToArray(); stA = ang.ToArray(); stY = new float[stX.Length];
            for (int s = 0; s < stX.Length; s++) stY[s] = Undulate(stX[s], stZ[s]);
        }

        // ponytail: textures are generated on first Show (~0.2–0.5 s, hidden by the GoTo flash); upgrade: bake them in the editor builder or prewarm at load.
        void BuildEpidermis()
        {
            var mainTex = Own(K.Tex(1024, 1024, (u, v) => CellColor(-Extent + u * 2 * Extent, -Extent + v * 2 * Extent)));
            var patchTex = Own(K.Tex(640, 640, (u, v) => CellColor(-PatchHalf + u * 2 * PatchHalf, -PatchHalf + v * 2 * PatchHalf)));
            Fx.MeshObj("Epidermis", Own(Field(-Extent, 2 * Extent, Mathf.RoundToInt(2 * Extent / Coarse), false)), EpiMat(mainTex), transform, true);
            Fx.MeshObj("Epidermis (hero patch)", Own(Field(-PatchHalf, 2 * PatchHalf, Mathf.RoundToInt(2 * PatchHalf / Coarse) * 3, true)), EpiMat(patchTex), transform, true);
        }

        Material EpiMat(Texture2D t)
        {
            // Leaf = double-sided lit + emission: a learner orbiting below the surface (min pitch −10°) sees a solid sheet, not a void
            var m = Own(Fx.Mat(MatKind.Leaf, Color.white, new Color(0.025f, 0.06f, 0.015f), 0.62f));
            Fx.SetTexture(m, t); SurfaceArt.Apply(m, t.width == 640 ? "EpidermisPatch" : "Epidermis", true, .25f); m.SetFloat("_Smoothness", .85f);
            m.SetTexture("_EmissionMap", t);
            return m;
        }

        Color CellColor(float x, float z)
        {
            CellAt(x, z, out float e, out float tint);
            float wall = 1f - K.Smooth01(e / 0.07f), dome = Mathf.Clamp01(e / 0.9f);
            var c = Color.Lerp(new Color(0.23f, 0.40f, 0.13f), new Color(0.42f, 0.59f, 0.21f), tint) * (0.86f + 0.14f * dome);
            c *= 0.97f + 0.03f * Mathf.Sin((x * 1.3f + z * 0.7f) * 12f + Mathf.Sin(z * 3f) * 2f);   // fine cuticle striae
            c = Color.Lerp(c, new Color(0.63f, 0.76f, 0.42f), wall * 0.7f); c.a = 1f;
            return c;
        }

        /// Heightfield (XZ, facing +y). Main field skips the hero patch square; the patch (3× denser) snaps its border to
        /// the coarse edges (watertight) and has an elliptical hole under the hero guard cells — the pore opening.
        Mesh Field(float origin, float size, int res, bool patch)
        {
            int n = res + 1, ratio = patch ? 3 : 1; float step = size / res;
            var v = new Vector3[n * n]; var nr = new Vector3[v.Length]; var uv = new Vector2[v.Length]; var cut = new bool[v.Length];
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    float x = origin + i * step, z = origin + j * step, h;
                    if (patch && (i == 0 || i == res) && j % ratio != 0)
                        h = Mathf.Lerp(Height(x, origin + j / ratio * Coarse), Height(x, origin + (j / ratio + 1) * Coarse), j % ratio / (float)ratio);
                    else if (patch && (j == 0 || j == res) && i % ratio != 0)
                        h = Mathf.Lerp(Height(origin + i / ratio * Coarse, z), Height(origin + (i / ratio + 1) * Coarse, z), i % ratio / (float)ratio);
                    else h = Height(x, z);
                    int k = j * n + i; const float e = 0.06f;
                    float m = x * x / (1.18f * 1.18f) + z * z / (0.98f * 0.98f);
                    cut[k] = patch && m < 1f;
                    // pore vertices snap radially onto the 1.18×0.98 rim (inside the flat collar): the opening is a smooth lens, not a grid staircase
                    if (cut[k] && m > 1e-4f) { float s = 1f / Mathf.Sqrt(m); x *= s; z *= s; h = Height(x, z); }
                    v[k] = new Vector3(x, h, z); uv[k] = new Vector2((x - origin) / size, (z - origin) / size);
                    nr[k] = new Vector3(Height(x - e, z) - Height(x + e, z), 2 * e, Height(x, z - e) - Height(x, z + e)).normalized;
                }
            var tri = new List<int>(res * res * 6);
            for (int j = 0; j < res; j++)
                for (int i = 0; i < res; i++)
                {
                    int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                    if (!patch && Mathf.Abs(origin + (i + 0.5f) * step) < PatchHalf && Mathf.Abs(origin + (j + 0.5f) * step) < PatchHalf) continue;
                    // a triangle with every corner in the pore lies inside the opening → dropped; rim-straddling ones end exactly on the rim
                    // (per triangle, not per quad: no sliver spans the opening and the kept surface stays watertight up to the rim)
                    if (!(cut[a] && cut[c] && cut[b])) { tri.Add(a); tri.Add(c); tri.Add(b); }
                    if (!(cut[b] && cut[c] && cut[d])) { tri.Add(b); tri.Add(c); tri.Add(d); }
                }
            return K.Build(patch ? "Epidermis Patch" : "Epidermis", v, nr, uv, tri.ToArray());
        }

        // ================================================================== stomata
        static float GuardR(float phi) => 0.06f + 0.40f * Mathf.Pow(Mathf.Abs(Mathf.Sin(phi)), 0.6f);   // two plump kidney cells, deeply pinched where the pair meets → lens-shaped pore

        static void GuardFrame(float phi, out Vector3 c, out Vector3 n)
        {
            c = new Vector3(GA * Mathf.Cos(phi), 0f, GB * Mathf.Sin(phi));
            n = new Vector3(Mathf.Cos(phi) / GA, 0f, Mathf.Sin(phi) / GB).normalized;   // in-plane outward
        }

        static Vector3[] Loop(int count, float offset, float sign)
        {
            var p = new Vector3[count + 1];
            for (int i = 0; i <= count; i++)
            {
                float phi = 2 * Mathf.PI * (i % count) / count; GuardFrame(phi, out var c, out var n);
                p[i] = c + n * (GuardR(phi) + offset) * sign + Vector3.up * GuardY;
            }
            return p;
        }

        void BuildStomata()
        {
            const int N = 72;
            var centre = Loop(N, 0f, 0f);   // sign 0 → centre line at GuardY
            var guardMesh = Own(K.Tube(centre, i => GuardR(2 * Mathf.PI * (i % N) / N), 18, false, null, 0.92f, 1f, Vector3.up, true));
            var shellCol = new Color(0.38f, 0.8f, 0.42f, 0.62f);
            var guardMatAll = Own(Fx.Mat(MatKind.LitTransparent, shellCol, GuardBase, 0.78f));
            guardMat = Own(Fx.Mat(MatKind.LitTransparent, shellCol, GuardBase, 0.78f));
            pingMat = Own(Fx.Mat(MatKind.Additive, Color.clear)); Fx.SetTexture(pingMat, K.LineTex);
            poreGlowMat = Own(Fx.Mat(MatKind.Additive, Color.clear)); Fx.SetTexture(poreGlowMat, Fx.SoftDot);
            var octa = Own(K.Octa()); var ball = Own(K.Sphere(10, 7)); var disc = Own(K.Disc(40)); var ringMesh = Own(K.Ring(64, 0.82f));
            var dots = new List<CombineInstance>(); var nuclei = new List<CombineInstance>(); var pores = new List<CombineInstance>(); var glows = new List<CombineInstance>();
            pingRoot = Fx.Node("Scanner Pings", transform).transform; pings = new Transform[stX.Length];
            var r = new System.Random(3);
            for (int s = 0; s < stX.Length; s++)
            {
                var pos = new Vector3(stX[s], stY[s], stZ[s]); var rot = Quaternion.Euler(0f, stA[s] * Mathf.Rad2Deg, 0f);
                var pivot = Fx.Node(s == 0 ? "Hero Stoma" : "Stoma", transform, pos).transform; pivot.localRotation = rot;
                Fx.MeshObj("Guard Cells", guardMesh, s == 0 ? guardMat : guardMatAll, pivot);
                var M = Matrix4x4.TRS(pos, rot, Vector3.one);
                for (int side = 0; side < 2; side++)
                {
                    for (int k = 0; k < 13; k++)
                    {
                        float phi = (side + K.Rand(r, 0.12f, 0.88f)) * Mathf.PI; GuardFrame(phi, out var c, out var nn); float gr = GuardR(phi);
                        var p = c + nn * K.Rand(r, -0.55f, 0.55f) * gr + Vector3.up * (GuardY + K.Rand(r, -0.45f, 0.45f) * gr);
                        dots.Add(new CombineInstance { mesh = ball, transform = M * Matrix4x4.TRS(p, RandRot(r), new Vector3(0.1f, 0.065f, 0.085f) * K.Rand(r, 0.8f, 1.2f)) });
                    }
                    { float phi = (side + 0.5f) * Mathf.PI; GuardFrame(phi, out var c, out var nn);
                      nuclei.Add(new CombineInstance { mesh = ball, transform = M * Matrix4x4.TRS(c + nn * 0.13f + Vector3.up * GuardY, Quaternion.identity, Vector3.one * 0.09f) }); }
                }
                if (s > 0) pores.Add(new CombineInstance { mesh = disc, transform = M * Matrix4x4.TRS(new Vector3(0f, Collar + 0.012f, 0f), Quaternion.identity, new Vector3(0.9f, 1f, 0.34f)) });
                glows.Add(new CombineInstance { mesh = disc, transform = M * Matrix4x4.TRS(new Vector3(0f, GuardY, 0f), Quaternion.identity, new Vector3(1.05f, 1f, 0.42f)) });
                var ping = Fx.MeshObj("Ping", ringMesh, pingMat, pingRoot); ping.transform.localPosition = pos + Vector3.up * GuardY; ping.transform.localRotation = rot;
                pings[s] = ping.transform;
                if (s == 0)
                {
                    stoma = Fx.Node("Stoma (pore)", pivot, new Vector3(0f, Collar + 0.05f, 0f)).transform;
                    guards = Fx.Node("Guard Cells (crest)", pivot, new Vector3(0f, GuardY + GuardR(Mathf.PI / 2) * 0.92f, GB)).transform;
                    rimMat = Own(Fx.Mat(MatKind.Additive, new Color(Palette.Cyan.r * 1.4f, Palette.Cyan.g * 1.4f, Palette.Cyan.b * 1.4f, 1f)));   // alpha 1: additive blend multiplies by it
                    var rimParts = new List<CombineInstance>
                    {
                        K.Part(Own(K.Tube(Loop(96, 0.012f, 1f), _ => 0.022f, 6, closed: true)), Vector3.zero, Quaternion.identity, Vector3.one),
                        K.Part(Own(K.Tube(Loop(96, 0.012f, -1f), _ => 0.022f, 6, closed: true)), Vector3.zero, Quaternion.identity, Vector3.one)
                    };
                    rim = Fx.MeshObj("Guard Rim", Own(K.Merge(rimParts, "Guard Rim")), rimMat, pivot);
                }
            }
            Fx.MeshObj("Guard Chloroplasts", Own(K.Merge(dots, "Guard Chloroplasts")), Own(Fx.Mat(MatKind.LitEmissive, new Color(0.14f, 0.52f, 0.16f), new Color(0.08f, 0.45f, 0.12f))), transform);
            Fx.MeshObj("Guard Nuclei", Own(K.Merge(nuclei, "Guard Nuclei")), Own(Fx.Mat(MatKind.LitEmissive, new Color(0.62f, 0.6f, 0.7f), new Color(0.12f, 0.1f, 0.16f))), transform);
            Fx.MeshObj("Pores", Own(K.Merge(pores, "Pores")), Own(Fx.Mat(MatKind.Unlit, new Color(0.02f, 0.07f, 0.045f))), transform);
            poreGlow = Fx.MeshObj("Pore Glow", Own(K.Merge(glows, "Pore Glow")), poreGlowMat, transform);
        }

        static Quaternion RandRot(System.Random r) => Quaternion.Euler(K.Rand(r, 0, 360), K.Rand(r, 0, 360), K.Rand(r, 0, 360));

        // ================================================================== leaf interior (seen through the pore)
        void BuildInterior()
        {
            var r = new System.Random(5);
            var interior = Fx.Node("Leaf Interior", transform).transform;
            var bowl = Fx.MeshObj("Substomatal Cavity", Own(K.Sphere(40, 24, true)), Own(Fx.Mat(MatKind.LitEmissive, new Color(0.13f, 0.3f, 0.17f), new Color(0.02f, 0.07f, 0.04f), 0.3f)), interior);
            bowl.transform.localPosition = BowlC; bowl.transform.localScale = BowlR;
            var unit = K.Sphere(16, 12); var octa = Own(K.Octa());
            var cells = new List<CombineInstance>(); var dots = new List<CombineInstance>(); var temp = new List<Mesh> { unit };
            for (int tries = 0; cells.Count < 18 && tries < 500; tries++)
            {
                var d = K.OnSphere(r); if (d.y > 0.4f) continue;
                var p = BowlC + Vector3.Scale(d, BowlR) * 0.8f;
                if (new Vector2(p.x, p.z).magnitude < 1.4f && p.y > -3.4f) continue;   // keep the air space under the pore open
                int seed = cells.Count; float rad = K.Rand(r, 0.7f, 1.1f);
                if (PathClash(p, rad * 1.45f + 0.3f)) continue;   // molecules never pass through a cell
                var mesh = K.Deform(unit, v => v * (1f + 0.36f * (K.Noise3(v * 1.7f + Vector3.one * (seed * 3.1f)) - 0.5f))); temp.Add(mesh);
                var rot = RandRot(r); var sc = new Vector3(rad, rad * K.Rand(r, 0.8f, 1.1f), rad * K.Rand(r, 0.9f, 1.25f));
                cells.Add(K.Part(mesh, p, rot, sc));
                for (int k = 0; k < 16; k++)
                {
                    var u = K.OnSphere(r); var nrm = (rot * new Vector3(u.x / sc.x, u.y / sc.y, u.z / sc.z)).normalized;
                    dots.Add(K.Part(octa, p + rot * Vector3.Scale(sc, u) * 0.97f, Quaternion.LookRotation(nrm), new Vector3(0.16f, 0.13f, 0.07f)));
                }
            }
            Fx.MeshObj("Spongy Mesophyll", Own(K.Merge(cells, "Spongy Mesophyll")), Own(Fx.Mat(MatKind.LitEmissive, new Color(0.3f, 0.62f, 0.3f), new Color(0.04f, 0.12f, 0.05f), 0.4f)), interior);
            Fx.MeshObj("Mesophyll Chloroplasts", Own(K.Merge(dots, "Mesophyll Chloroplasts")), Own(Fx.Mat(MatKind.LitEmissive, new Color(0.12f, 0.5f, 0.14f), new Color(0.06f, 0.4f, 0.1f))), interior);
            foreach (var m in temp) Destroy(m);
            var lamp = Fx.Node("Interior Light", interior, new Vector3(0f, -2f, 0.3f)).AddComponent<Light>();
            lamp.type = LightType.Point; lamp.color = new Color(0.62f, 1f, 0.66f); lamp.intensity = 1.6f; lamp.range = 6.5f; lamp.shadows = LightShadows.None;
            var motes = Fx.Particles("Interior Motes", interior, new Fx.PCfg { color = new Color(0.7f, 1f, 0.75f, 0.5f), size = 0.05f, life = 6f, speed = 0.04f, rate = 6f, radius = 1.8f, noise = 0.15f, max = 60 });
            motes.transform.localPosition = new Vector3(0f, -2f, 0f); particles.Add(motes);
        }

        // ================================================================== air, light, molecules
        void BuildAir()
        {
            var sky = Fx.MeshObj("Sky", Own(K.Sphere(32, 16, true)), Own(Fx.Mat(MatKind.Unlit, Fog)), transform);
            sky.transform.localScale = Vector3.one * 140f;
            var motes = Fx.Particles("Air Motes", transform, new Fx.PCfg { color = new Color(0.85f, 1f, 0.95f, 0.6f), size = 0.06f, life = 8f, speed = 0.06f, rate = 10f, radius = 10f, noise = 0.2f, max = 120 });
            motes.transform.localPosition = new Vector3(0f, 3f, 1f); particles.Add(motes);
            var shaftMat = Own(Fx.Mat(MatKind.Additive, new Color(1f, 0.97f, 0.85f, 0.07f))); Fx.SetTexture(shaftMat, K.ShaftTex);
            var dir = Quaternion.Euler(58f, -60f, 0f) * Vector3.forward;
            foreach (var target in new[] { new Vector3(-5f, 0f, 7f), new Vector3(2.5f, 0f, 10f), new Vector3(8f, 0f, 3f) })
                K.Shaft(transform, target - dir * 15f, dir, 16f, 3f, shaftMat);
            frostMat = Own(Fx.Mat(MatKind.UnlitTransparent, new Color(0.62f, 0.8f, 1f, 0f)));
            frost = Fx.Prim(PrimitiveType.Quad, transform, Vector3.zero, Vector3.one, frostMat, false, "Freeze Tint");
        }

        void BuildMolecules()
        {
            var r = new System.Random(9);
            molRoot = Fx.Node("Molecules", transform).transform;

            hero = Fx.Molecule(Mol.CO2, molRoot, 0.38f, 0.9f).transform; hero.name = "CO2 (highlighted)";   // the narrated molecule outranks the ambient ones (0.22)
            heroHalo = K.Glow(hero, Vector3.zero, 1.45f, K.GlowMat(new Color(Palette.Cyan.r * 1.2f, Palette.Cyan.g * 1.2f, Palette.Cyan.b * 1.2f, 0.9f)));   // soft halo: no bloom disc
            trail = hero.gameObject.AddComponent<TrailRenderer>();
            trail.time = 0.9f; trail.minVertexDistance = 0.03f; trail.widthMultiplier = 0.16f; trail.numCapVertices = 4;
            trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = g;
            var tm = Own(Fx.Mat(MatKind.ParticleAdditive, Palette.Cyan * 2.2f)); Fx.SetTexture(tm, K.LineTex); trail.sharedMaterial = tm;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; trail.receiveShadows = false;

            // ambient CO₂ fills the air the 100× camera actually sees (it looks steeply down, so a box of air would be mostly
            // out of frame): sample the L2 view frustum (45°, 16:9) at 0.9–2.5 above the surface
            amb = new Transform[7]; ambHome = new Vector3[amb.Length]; var rl = new List<Renderer>();
            Vector3 vf = (L2Look - L2Pos).normalized, vr = Vector3.Cross(Vector3.up, vf).normalized, vu = Vector3.Cross(vf, vr);
            float tv = Mathf.Tan(22.5f * Mathf.Deg2Rad), th = tv * 16f / 9f;
            for (int i = 0; i < amb.Length; i++)
            {
                amb[i] = Fx.Molecule(Mol.CO2, molRoot, 0.22f).transform;
                for (int guard = 0; guard < 200; guard++)
                {
                    var d = (vf + vr * (K.Rand(r, -1f, 1f) * th) + vu * (K.Rand(r, -0.9f, 0.9f) * tv)).normalized;
                    var p = L2Pos + d * ((L2Pos.y - K.Rand(r, 0.9f, 2.5f)) / -d.y);
                    ambHome[i] = p;
                    bool ok = (p - L2Pos).sqrMagnitude > 1.8f * 1.8f && !(new Vector2(p.x, p.z).sqrMagnitude < 1.3f && p.y < 1.5f)
                              && (p - CO2Path[0]).sqrMagnitude > 0.8f && (p - CO2Path[1]).sqrMagnitude > 0.6f;
                    for (int j = 0; j < i && ok; j++) ok = (ambHome[j] - p).sqrMagnitude > 0.45f;
                    if (ok) break;
                }
                rl.AddRange(amb[i].GetComponentsInChildren<Renderer>());
            }
            ambR = rl.ToArray(); ambMats = new Material[ambR.Length][];
            for (int i = 0; i < ambR.Length; i++) ambMats[i] = ambR[i].sharedMaterials;
            dimMat = Own(Fx.Mat(MatKind.LitTransparent, new Color(0.62f, 0.7f, 0.78f, 0.22f)));

            var o2Halo = K.GlowMat(new Color(Palette.Oxygen.r * 2f, Palette.Oxygen.g * 2f, Palette.Oxygen.b * 2f, 0.85f));
            var co2Halo = K.GlowMat(new Color(0.7f * 1.3f, 0.9f * 1.3f, 1.3f, 0.55f));
            o2 = Pool(Mol.O2, 5, o2Halo, 0.75f); flowIn = Pool(Mol.CO2, 5, co2Halo, 0.7f); flowOut = Pool(Mol.O2, 5, o2Halo, 0.65f);
        }

        /// Molecule lanes. Below the surface every lane stays in the window the 100× camera sees through the pore
        /// (the cavity floor around z ≈ 1.1–1.5), so nothing vanishes behind the near guard cell.
        void MakePaths()
        {
            o2Paths = new Vector3[5][]; inPaths = new Vector3[5][]; outPaths = new Vector3[5][];
            for (int i = 0; i < 5; i++)
            {
                float x = 0.14f + 0.07f * i;   // O₂ uses the +x half of the slit, CO₂ the −x half: same doorway, two lanes
                o2Paths[i] = new[] { new Vector3(-0.6f + 0.3f * i, -2.3f + 0.12f * (i % 2), 1.15f + 0.2f * (i % 3)), new Vector3(x, -0.85f, 0.55f),
                    new Vector3(x, 0.3f, 0f), new Vector3(x * 1.8f + (i - 2) * 0.45f, 1.7f, 0.25f + 0.15f * i), new Vector3((i - 2) * 1.35f + 0.6f, 4.3f, 1.1f + 0.35f * (i % 3)) };
                outPaths[i] = new[] { new Vector3(0.4f + 0.12f * i, -2.3f, 1.2f + 0.25f * (i % 2)), new Vector3(x, -0.85f, 0.55f), new Vector3(x, 0.3f, 0f),
                    new Vector3(x * 1.7f + 0.3f, 1.5f, 0.3f), new Vector3((i - 2) * 1.3f + 1.2f, 3.6f, 1.5f + 0.3f * (i % 2)) };
                inPaths[i] = new[] { new Vector3((i - 2) * 1.3f - 1.4f, 3.3f, 1.4f + 0.3f * (i % 2)), new Vector3(-x * 1.6f - 0.3f, 1.4f, 0.35f), new Vector3(-x, 0.3f, 0f),
                    new Vector3(-x, -0.85f, 0.55f), new Vector3(-x - 0.4f - 0.1f * i, -2.3f, 1.2f + 0.25f * (i % 3)) };
            }
        }

        /// True when a sphere (centre p, radius rad) would cut any below-surface molecule lane (build time only).
        bool PathClash(Vector3 p, float rad)
        {
            bool Hit(Vector3[] path)
            {
                for (int k = 0; k <= 32; k++)
                {
                    var q = Fx.CatmullRom(path, k / 32f);
                    if (q.y < -0.1f && (q - p).sqrMagnitude < rad * rad) return true;
                }
                return false;
            }
            if (Hit(CO2Path)) return true;
            for (int i = 0; i < 5; i++) if (Hit(o2Paths[i]) || Hit(inPaths[i]) || Hit(outPaths[i])) return true;
            return false;
        }

        Transform[] Pool(Mol kind, int count, Material halo, float haloSize)
        {
            var a = new Transform[count];
            for (int i = 0; i < count; i++)
            {
                a[i] = Fx.Molecule(kind, molRoot, 0.22f, 0.9f).transform;
                K.Glow(a[i], Vector3.zero, haloSize, halo);
                a[i].gameObject.SetActive(false);
            }
            return a;
        }
    }
}
