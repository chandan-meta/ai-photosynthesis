using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using K = Photosynthesis.MicroKitA;
using Object = UnityEngine.Object;

namespace Photosynthesis
{
    /// Clue 02 underground: enlarged soil particles, root surface with root hairs, water films, water molecules.
    /// A translucent young root (cell-patterned epidermis, visible vascular core) crosses the frame from its tip at the
    /// lower left up and out of the top; root hairs weave between noisy mineral grains; wet grains carry blue water films
    /// holding pickable H₂O. Absorb → hair → root core; PathwayGlow lights the core (xylem) upward.
    public class SoilWorld : MicroWorld
    {
        static readonly Vector3 CamPos = new Vector3(-2.5f, -1.2f, -8.5f), CamLook = new Vector3(0f, 0.3f, 2.6f), TiltPos = new Vector3(-0.9f, 1.6f, -8.2f);
        static readonly Vector3[] AxisCtrl =
        {
            new Vector3(-15f, -10f, 7f), new Vector3(-8f, -5.5f, 5f), new Vector3(-1f, -1f, 3.2f),
            new Vector3(5f, 4f, 2.6f), new Vector3(9f, 10f, 3.5f), new Vector3(11f, 18f, 5f)
        };
        static readonly Color Fog = new Color(0.16f, 0.11f, 0.075f), SteleBase = new Color(0.18f, 0.16f, 0.07f);
        static readonly Color WaterHdr = new Color(Palette.Water.r * 3f, Palette.Water.g * 3f, Palette.Water.b * 3f, 1f);

        public Transform Root => rootMark ? rootMark : transform;

        struct Rock { public Vector3 c, scale; public Quaternion q; public float r; public int seed, tint; }

        readonly List<Object> owned = new List<Object>();
        readonly List<Rock> rocks = new List<Rock>();
        readonly List<Vector3[]> hairs = new List<Vector3[]>();   // [0] = hero hair (callout), base on the root → tip
        K.Path axis; Vector3 tiltLook;
        bool ready, highlight, glowing;
        int pickResult = -1, hovered = -1, absorbed = -1;
        float glowT0, rippleT = -1f;
        Vector3 absBase, absCore; float absS;

        Transform rootMark, molRoot;
        Transform[] mols; GameObject[] halos; Vector3[] home, nrm; bool[] taken, near;
        Light[] lamps; Vector3[] lampHome; float[] lampI;
        Transform[] ripples; Material[] rippleMats; GameObject head;
        Material steleMat, glowInner, glowOuter, beadMat;
        GameObject pathInner, pathOuter, beads; Mesh pathMeshA, pathMeshB; int[] trisA, trisB; K.Path glowPath;
        K.SunOverride sun;
        CameraDirector clickCam;

        T Own<T>(T o) where T : Object { owned.Add(o); return o; }
        void OnDisable() => Unsubscribe();   // a debug jump can stop the Story mid-wait
        void OnDestroy() { Unsubscribe(); ClearPath(); foreach (var o in owned) if (o) Destroy(o); }
        void EnsureBuilt() { if (!ready) Build(); }

        // ================================================================== contract
        protected override void Build()
        {
            if (ready) return;
            ready = true;
            sun = gameObject.AddComponent<K.SunOverride>();
            BuildRoot(); PlaceRocks(); BuildHairs(); BuildRocks(); BuildAtmosphere(); BuildFx();
            ResetWorld();
        }

        protected override void ApplyMood()
        {
            K.Mood(Fog, 0.038f, new Color(0.5f, 0.42f, 0.33f), new Color(0.28f, 0.2f, 0.14f), new Color(0.1f, 0.07f, 0.05f));
            sun.Apply(0.85f, new Color(1f, 0.86f, 0.66f), new Vector3(62f, 25f, 0f));
        }

        public override (Vector3 pos, Vector3 look) Pose() => (Origin + CamPos, Origin + CamLook);

        public override void ResetWorld()
        {
            if (!ready) return;
            Unsubscribe();
            pickResult = -1; hovered = -1; absorbed = -1; highlight = false; glowing = false; rippleT = -1f;
            for (int i = 0; i < mols.Length; i++)
            {
                taken[i] = false; mols[i].localPosition = home[i]; mols[i].localScale = Vector3.one; halos[i].SetActive(false);
                mols[i].gameObject.SetActive(true);
            }
            foreach (var r in ripples) r.gameObject.SetActive(false);
            head.SetActive(false);
            Fx.SetEmission(steleMat, SteleBase);
            ClearPath();
        }

        public void HighlightWater(bool on) { if (ready) highlight = on; }

        /// The molecule a forced pick would take (nearest the hero root hair): the idle pick hint has Curio point at it.
        /// Null before the world is built or when none is left.
        public Transform SuggestedMolecule { get { if (!ready) return null; int i = NearestToHair(); return i >= 0 ? mols[i] : null; } }

        /// Waits for the learner to click a water molecule near the root (raycast via Module.I.cam.Clicked).
        /// `autoPick` forces a pick (AI action / debug). Returns the molecule through `picked`.
        public IEnumerator WaitForPick(Func<bool> autoPick, Action<Transform> picked)
        {
            EnsureBuilt();
            Unsubscribe(); pickResult = -1;
            var cd = Module.I ? Module.I.cam : null;
            if (cd) { clickCam = cd; cd.Clicked += OnClick; }
            while (pickResult < 0)
            {
                if (autoPick != null && autoPick()) { pickResult = NearestToHair(); break; }
                var mouse = Mouse.current;
                if (mouse != null && cd)
                {
                    var mp = mouse.position.ReadValue();
                    hovered = cd.UiBlocks != null && cd.UiBlocks(mp) ? -1 : PickAt(mp, false);
                }
                yield return null;
            }
            Unsubscribe(); hovered = -1;
            picked?.Invoke(mols[Mathf.Max(pickResult, 0)]);
        }

        /// Molecule moves into a root hair; ripple; absorbed (≈2.5 s).
        public IEnumerator Absorb(Transform molecule)
        {
            EnsureBuilt();
            int i = Array.IndexOf(mols, molecule);
            if (i < 0 || taken[i]) i = NearestToHair();
            if (i < 0) yield break;
            taken[i] = true; halos[i].SetActive(true);
            var m = mols[i];
            FindHair(m.localPosition, out int h, out int k);
            var hp = hairs[h];
            absS = axis.Nearest(hp[0]); var ax = axis.At(absS);
            absBase = hp[0]; absCore = ax + (hp[0] - ax).normalized * 0.45f;   // beside the vascular core, inside the root
            Vector3 p0 = m.localPosition, pk = hp[k], ctrl = (p0 + pk) * 0.5f + Vector3.up * 0.35f;
            const float A = 0.9f, B = 1.0f, C = 0.6f;
            bool rippled = false;
            for (float t = 0f; ; t += Time.deltaTime)
            {
                if (Module.Skipping) t = A + B + C;
                t = Mathf.Min(t, A + B + C);
                if (t < A)
                {
                    float u = Fx.Eval(t / A, Ease.InOut);
                    m.localPosition = Vector3.Lerp(Vector3.Lerp(p0, ctrl, u), Vector3.Lerp(ctrl, pk, u), u);
                }
                else if (t < A + B)
                {
                    float f = (1f - Fx.Eval((t - A) / B, Ease.InOut)) * k; int a = Mathf.Min(Mathf.FloorToInt(f), hp.Length - 2);
                    m.localPosition = Vector3.Lerp(hp[a], hp[a + 1], f - a);
                    m.localScale = Vector3.one * 0.85f;
                }
                else
                {
                    if (!rippled) { rippled = true; Ripple(hp[0], (hp[0] - ax).normalized); }
                    m.localPosition = Vector3.Lerp(hp[0], absCore, Fx.Eval((t - A - B) / C, Ease.Out));
                    m.localScale = Vector3.one * Mathf.Lerp(0.85f, 0.75f, (t - A - B) / C);
                }
                m.localRotation = Quaternion.Euler(t * 140f, t * 90f, 0f);
                if (t >= A + B + C) break;
                yield return null;
            }
            absorbed = i;
        }

        /// "A pathway illuminates upward": from the root hair into the vascular core and up out of frame; camera tilts up.
        public IEnumerator PathwayGlow()
        {
            EnsureBuilt();
            if (absorbed < 0) { var hp = hairs[0]; absBase = hp[0]; absS = axis.Nearest(hp[0]); var ax = axis.At(absS); absCore = ax + (hp[0] - ax).normalized * 0.45f; }
            BuildPath();
            glowT0 = Time.time; glowing = true; head.SetActive(true);
            var cd = Module.I ? Module.I.cam : null;
            if (cd) yield return cd.Blend(Origin + TiltPos, Origin + tiltLook, 2.2f, 52f);
            else yield return Fx.Wait(2f);
            glowing = false; Reveal(1f); head.SetActive(false);
            Fx.SetEmission(steleMat, new Color(Palette.Water.r, Palette.Water.g, Palette.Water.b) * 0.9f);
            beads = Beads.Create("Upflow", transform, glowPath.pts, 14, 0.16f, beadMat, 0.28f).gameObject;
        }

        // ================================================================== per frame
        void Update()
        {
            float t = Time.time;
            for (int i = 0; i < mols.Length; i++)
            {
                if (taken[i]) continue;
                float ph = i * 1.37f;
                var p = home[i] + new Vector3(Mathf.Sin(t * 0.5f + ph), Mathf.Sin(t * 0.43f + ph * 2f), Mathf.Cos(t * 0.37f + ph)) * 0.06f;
                bool glow = highlight && near[i] || i == hovered;
                if (highlight && near[i]) p += nrm[i] * (0.1f * Mathf.Sin(t * 2.4f + ph));
                mols[i].localPosition = p;
                mols[i].localRotation = Quaternion.Euler(t * 25f + ph * 40f, t * 18f + ph * 20f, 0f);
                mols[i].localScale = Vector3.Lerp(mols[i].localScale, Vector3.one * (i == hovered ? 1.35f : 1f), Time.deltaTime * 10f);
                if (halos[i].activeSelf != glow) halos[i].SetActive(glow);
                if (glow) halos[i].transform.localScale = Vector3.one * (1f + 0.15f * Mathf.Sin(t * 4f + ph));
            }
            for (int i = 0; i < lamps.Length; i++)
            {
                lamps[i].transform.localPosition = lampHome[i] + new Vector3(Mathf.Sin(t * 0.3f + i), Mathf.Sin(t * 0.23f + i * 2f), Mathf.Cos(t * 0.27f + i)) * 1.2f;
                lamps[i].intensity = lampI[i] * (0.82f + 0.12f * Mathf.Sin(t * 1.7f + i * 2.1f) + 0.06f * Mathf.Sin(t * 4.3f + i));   // caustic-like shimmer
            }
            if (rippleT >= 0f)
            {
                float k = t - rippleT;
                for (int j = 0; j < ripples.Length; j++)
                {
                    float u = (k - j * 0.22f) / 0.9f; bool on = u > 0f && u < 1f;
                    if (ripples[j].gameObject.activeSelf != on) ripples[j].gameObject.SetActive(on);
                    if (!on) continue;
                    ripples[j].localScale = Vector3.one * Mathf.Lerp(0.25f, 1.9f, Fx.Eval(u, Ease.Out));
                    Fx.SetColor(rippleMats[j], new Color(WaterHdr.r, WaterHdr.g, WaterHdr.b, Mathf.Pow(1f - u, 1.4f)));
                }
                if (k > 1.5f) rippleT = -1f;
            }
            if (glowing) Reveal(Mathf.Clamp01((t - glowT0) / 2f));
        }

        // ================================================================== picking
        // colliders follow their bobbing molecules only at the physics step; sync so the click ray hits what is drawn
        void OnClick(Vector2 p)
        {
            if (pickResult >= 0) return;
            Physics.SyncTransforms(); pickResult = PickAt(p, true);
            if (pickResult < 0 && Module.I && Module.I.audioDirector) Module.I.audioDirector.Play(Sfx.Wrong, 0.25f);   // a quartz grain / empty soil: soft "not that"
        }
        void Unsubscribe() { if (clickCam) clickCam.Clicked -= OnClick; clickCam = null; }

        int PickAt(Vector2 screen, bool loose)
        {
            var cd = Module.I ? Module.I.cam : null; if (!cd) return -1;
            var cam = cd.Cam;
            if (Physics.Raycast(cam.ScreenPointToRay(screen), out var hit, 300f))
                for (int i = 0; i < mols.Length; i++) if (!taken[i] && hit.transform == mols[i]) return i;
            if (!loose) return -1;
            int best = -1; float bd = 70f * Screen.height / 1080f; bd *= bd;   // forgiving: nearest molecule within ~70 px
            for (int i = 0; i < mols.Length; i++)
            {
                if (taken[i]) continue;
                Vector3 sp = cam.WorldToScreenPoint(mols[i].position); if (sp.z <= 0f) continue;
                float d = ((Vector2)sp - screen).sqrMagnitude; if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        int NearestToHair()
        {
            int best = -1; float bd = float.MaxValue;
            for (int i = 0; i < mols.Length; i++)
            {
                if (taken[i]) continue;
                foreach (var p in hairs[0]) { float d = (mols[i].localPosition - p).sqrMagnitude; if (d < bd) { bd = d; best = i; } }
            }
            return best;
        }

        void FindHair(Vector3 p, out int h, out int k)
        {
            h = 0; k = 0; float bd = float.MaxValue;
            for (int i = 0; i < hairs.Count; i++)
                for (int j = 1; j < hairs[i].Length; j++) { float d = (hairs[i][j] - p).sqrMagnitude; if (d < bd) { bd = d; h = i; k = j; } }
        }

        // ================================================================== effects
        void Ripple(Vector3 at, Vector3 normal)
        {
            foreach (var r in ripples) { r.localPosition = at + normal * 0.03f; r.localRotation = Quaternion.FromToRotation(Vector3.up, normal); }
            rippleT = Time.time;
            Fx.Burst(transform.TransformPoint(at), Palette.Water, 30, 0.06f, 1.2f, 0.9f);
            var au = Module.I ? Module.I.audioDirector : null; if (au) au.Play(Sfx.WaterAbsorb);   // in sync with the ring at the hair base
        }

        /// Glow tube: hair base → beside the core → up the vascular core → beyond the top of frame. Revealed by index range.
        void BuildPath()
        {
            ClearPath();
            var pts = new List<Vector3> { absBase, absCore };
            float L = axis.Length;
            for (float s = absS + 0.6f; s < L; s += 0.5f) pts.Add(axis.At(s));
            axis.Frame(L, out var end, out var tan, out _, out _);
            for (int i = 1; i <= 5; i++) pts.Add(end + tan * (2f * i));
            glowPath = new K.Path(pts);
            pathMeshA = K.Tube(pts, i => i == 0 ? 0.1f : 0.37f, 12); trisA = pathMeshA.triangles;
            pathMeshB = K.Tube(pts, i => i == 0 ? 0.22f : 0.7f, 12); trisB = pathMeshB.triangles;
            pathInner = Fx.MeshObj("Pathway (core)", pathMeshA, glowInner, transform);
            pathOuter = Fx.MeshObj("Pathway (halo)", pathMeshB, glowOuter, transform);
            Reveal(0f);
        }

        void Reveal(float f)
        {
            if (!pathMeshA) return;
            int segs = glowPath.pts.Count - 1, show = Mathf.Clamp(Mathf.CeilToInt(f * segs), 1, segs) * 12 * 6;
            pathMeshA.SetTriangles(trisA, 0, show, 0); pathMeshB.SetTriangles(trisB, 0, show, 0);
            var p = glowPath.At(f * glowPath.Length);
            head.transform.localPosition = p;
            // the absorbed molecule waits beside the core (where Absorb left it) until the glow front reaches it, then rides it
            if (absorbed >= 0) mols[absorbed].localPosition = glowPath.At(Mathf.Max(f * glowPath.Length, glowPath.S(1)));
        }

        void ClearPath()
        {
            if (pathInner) Destroy(pathInner); if (pathOuter) Destroy(pathOuter); if (beads) Destroy(beads);
            if (pathMeshA) Destroy(pathMeshA); if (pathMeshB) Destroy(pathMeshB);
            pathMeshA = pathMeshB = null;
        }

        // ================================================================== construction
        float RootR(float s) => Mathf.Lerp(0.55f, 1.3f, K.Smooth01(s / 9f));   // thin near the tip

        static float SegDist(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a; float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector3.Distance(p, a + ab * t);
        }

        static float Lump(Vector3 u, int seed) =>
            1f + 0.8f * (K.Noise3(u * 1.3f + new Vector3(seed * 1.7f, seed * 2.3f, seed * 0.9f)) - 0.5f) + 0.25f * (K.Noise3(u * 3.7f + Vector3.one * (seed * 1.1f)) - 0.5f);

        Vector3 Surface(Rock k, Vector3 worldDir, float lift)
        {
            var u = Vector3.Scale(Quaternion.Inverse(k.q) * worldDir, new Vector3(1f / k.scale.x, 1f / k.scale.y, 1f / k.scale.z)).normalized;
            return k.c + k.q * (Vector3.Scale(k.scale, u) * (Lump(u, k.seed) + lift / k.r)) * k.r;
        }

        static Color RootCellPx(float u, float v)
        {   // one texture tile = 1 cell wide × 2 rows (running bond); world cell ≈ 0.42 × 1.5
            float uu = v < 0.5f ? u : Mathf.Repeat(u + 0.5f, 1f), vv = Mathf.Repeat(v * 2f, 1f);
            float d = Mathf.Min(Mathf.Min(uu, 1f - uu) * 0.42f, Mathf.Min(vv, 1f - vv) * 1.5f);
            float wall = 1f - K.Smooth01((d - 0.012f) / 0.04f);
            float nd = Mathf.Sqrt((uu - 0.5f) * 0.42f * ((uu - 0.5f) * 0.42f) + (vv - 0.45f) * 1.5f * ((vv - 0.45f) * 1.5f)), nuc = 1f - K.Smooth01((nd - 0.05f) / 0.03f);
            var c = Color.Lerp(new Color(1f, 0.97f, 0.9f), new Color(0.86f, 0.8f, 0.64f), Mathf.Max(wall, nuc * 0.6f));
            c.a = Mathf.Lerp(0.36f, 0.95f, wall) + nuc * 0.25f;
            return c;
        }

        static Color RockPx(float u, float v)
        {
            float n = 0.55f * K.TileNoise(u, v, 8, 1) + 0.3f * K.TileNoise(u, v, 24, 2) + 0.15f * K.TileNoise(u, v, 64, 3);
            float g = 0.62f + 0.5f * n, fleck = K.Hash((int)(u * 256), (int)(v * 256), 9);
            if (fleck > 0.985f) g *= 1.35f; else if (fleck < 0.02f) g *= 0.55f;   // mineral flecks
            return new Color(g, g * 0.98f, g * 0.95f, 1f);
        }

        void BuildRoot()
        {
            axis = new K.Path(Fx.Smooth(AxisCtrl, 80));
            float L = axis.Length; tiltLook = axis.At(L * 0.86f);
            var tex = Own(K.Tex(64, 128, RootCellPx, true));
            var mat = Own(Fx.Mat(MatKind.LitTransparent, Color.white, new Color(0.2f, 0.16f, 0.1f), 0.55f));
            Fx.SetTexture(mat, tex); mat.SetTexture("_EmissionMap", tex); mat.SetTextureScale("_BaseMap", new Vector2(18f, 1f));
            Fx.MeshObj("Root", Own(K.Tube(axis.pts, i => RootR(axis.S(i)), 30, false, i => axis.S(i) / 3f)), mat, transform);
            steleMat = Own(Fx.Mat(MatKind.LitEmissive, new Color(0.86f, 0.82f, 0.52f), SteleBase, 0.5f));
            var core = axis.pts.GetRange(2, axis.pts.Count - 2);
            Fx.MeshObj("Vascular Core (stele)", Own(K.Tube(core, i => Mathf.Lerp(0.16f, 0.32f, K.Smooth01(axis.S(i + 2) / 8f)), 12)), steleMat, transform);
            axis.Frame(0.3f, out var p, out var t, out _, out _);
            var cap = Fx.Prim(PrimitiveType.Sphere, transform, p - t * 0.15f, new Vector3(1.15f, 1.15f, 1.7f),
                Own(Fx.Mat(MatKind.LitTransparent, new Color(0.9f, 0.78f, 0.6f, 0.75f), new Color(0.12f, 0.08f, 0.04f), 0.5f)), false, "Root Cap");
            cap.transform.localRotation = Quaternion.LookRotation(t);

            // hero root hair: grows toward the camera-left, its tip resting in the film of the hero grain
            float sh = axis.Nearest(new Vector3(-1.2f, -1.2f, 3.1f));
            axis.Frame(sh, out var ap, out var at, out _, out _);
            var want = new Vector3(-0.55f, -0.25f, -0.8f).normalized; var perp = Vector3.ProjectOnPlane(want, at).normalized;
            var hero = new Vector3[19]; var pos = ap + perp * RootR(sh) * 0.96f;
            for (int k = 0; k < hero.Length; k++)
            {
                hero[k] = pos;
                pos += (Vector3.Lerp(perp, want, k / 18f) + Vector3.Cross(want, Vector3.up) * (0.12f * Mathf.Sin(k * 0.6f))).normalized * 0.2f;
            }
            hairs.Add(hero);
            rootMark = Fx.Node("Root Hair (callout)", transform, hero[9]).transform;
        }

        void AddRock(Vector3 c, float rad, System.Random r, int tint)
        {
            rocks.Add(new Rock
            {
                c = c, r = rad, seed = rocks.Count, tint = tint,
                scale = new Vector3(K.Rand(r, 0.8f, 1.15f), K.Rand(r, 0.65f, 1f), K.Rand(r, 0.85f, 1.2f)),
                q = Quaternion.Euler(K.Rand(r, 0, 360), K.Rand(r, 0, 360), K.Rand(r, 0, 360))
            });
        }

        bool Free(Vector3 c, float rad)
        {
            float s = axis.Nearest(c);
            if (Vector3.Distance(axis.At(s), c) < RootR(s) + rad * 0.95f + 0.25f) return false;
            foreach (var p in hairs[0]) if ((p - c).sqrMagnitude < (rad + 0.3f) * (rad + 0.3f)) return false;
            foreach (var k in rocks) if ((k.c - c).magnitude < (k.r + rad) * 0.88f) return false;
            return SegDist(c, CamPos, CamLook) > rad + 1.2f && SegDist(c, CamPos, tiltLook) > rad + 1f && (c - CamPos).magnitude > rad + 2.5f;
        }

        void PlaceRocks()
        {
            var r = new System.Random(21);
            var tip = hairs[0][hairs[0].Length - 1];
            AddRock(tip + new Vector3(-0.3f, -1f, 0.1f).normalized * 1.45f, 1.3f, r, 1);   // hero grain
            var k0 = rocks[0]; var up = (tip - k0.c).normalized;   // its water film ends 0.3 below the hero tip → a visible water bridge
            k0.c = tip - up * ((Surface(k0, up, 0.2f) - k0.c).magnitude + 0.3f); rocks[0] = k0;
            for (int tries = 0; rocks.Count < 95 && tries < 6000; tries++)
            {
                float rad = Mathf.Lerp(1.1f, 3.4f, Mathf.Pow((float)r.NextDouble(), 1.6f));
                var c = new Vector3(K.Rand(r, -20f, 20f), K.Rand(r, -14f, 15f), K.Rand(r, -5f, 18f));
                if (Free(c, rad)) AddRock(c, rad, r, rocks.Count % 4);
            }
        }

        void BuildHairs()
        {
            var r = new System.Random(33); float L = axis.Length;
            for (int tries = 0; hairs.Count < 80 && tries < 500; tries++)
            {
                float s = K.Rand(r, 0.1f, 0.62f) * L, a = K.Rand(r, 0f, Mathf.PI * 2f);
                axis.Frame(s, out var ap, out var at, out var an, out var ab);
                var n = Mathf.Cos(a) * an + Mathf.Sin(a) * ab;
                var dir = (n + at * K.Rand(r, -0.35f, 0.35f)).normalized; var pos = ap + n * RootR(s) * 0.96f;
                var pts = new Vector3[Mathf.RoundToInt(K.Rand(r, 8f, 20f))]; bool ok = true;
                for (int k = 0; k < pts.Length && ok; k++)
                {
                    pts[k] = pos;
                    if (SegDist(pos, CamPos, CamLook) < 0.9f || (pos - CamPos).sqrMagnitude < 9f) ok = false;
                    if (k > 2) foreach (var hp in hairs[0]) if ((hp - pos).sqrMagnitude < 0.12f) ok = false;
                    dir = (dir + new Vector3(Mathf.Sin(k * 0.9f + a * 3f), Mathf.Cos(k * 0.7f + s), Mathf.Sin(k * 0.5f + s * 2f)) * 0.18f + Vector3.down * 0.04f).normalized;
                    foreach (var rock in rocks)
                    {   // slide around grains instead of piercing them
                        var to = pos - rock.c; float lim = rock.r * Mathf.Max(rock.scale.x, Mathf.Max(rock.scale.y, rock.scale.z)) * 1.05f + 0.12f;
                        if (to.sqrMagnitude < lim * lim) dir = (Vector3.ProjectOnPlane(dir, to.normalized) + to.normalized * 0.4f).normalized;
                    }
                    pos += dir * 0.2f;
                }
                if (ok) hairs.Add(pts);
            }
            var mat = Own(Fx.Mat(MatKind.LitTransparent, new Color(0.95f, 0.92f, 0.84f, 0.55f), new Color(0.14f, 0.13f, 0.1f), 0.7f));
            var tipMesh = K.Sphere(8, 6); var temp = new List<Mesh> { tipMesh }; var parts = new List<CombineInstance>();
            for (int h = 1; h < hairs.Count; h++)
            {
                var hp = hairs[h]; int n = hp.Length;
                var tube = K.Tube(hp, i => Mathf.Lerp(0.075f, 0.058f, i / (n - 1f)), 6); temp.Add(tube);
                parts.Add(K.Part(tube, Vector3.zero, Quaternion.identity, Vector3.one));
                parts.Add(K.Part(tipMesh, hp[n - 1], Quaternion.identity, Vector3.one * 0.058f));
            }
            Fx.MeshObj("Root Hairs", Own(K.Merge(parts, "Root Hairs")), mat, transform);
            var heroTube = K.Tube(hairs[0], i => Mathf.Lerp(0.115f, 0.09f, i / 18f), 10); temp.Add(heroTube);
            var heroParts = new List<CombineInstance> { K.Part(heroTube, Vector3.zero, Quaternion.identity, Vector3.one), K.Part(tipMesh, hairs[0][18], Quaternion.identity, Vector3.one * 0.09f) };
            Fx.MeshObj("Root Hair (hero)", Own(K.Merge(heroParts, "Root Hair")), mat, transform);
            foreach (var m in temp) Destroy(m);
        }

        void BuildRocks()
        {
            var r = new System.Random(44);
            var unit = K.Sphere(20, 14); var temp = new List<Mesh> { unit };
            var tex = Own(K.Tex(256, 256, RockPx, true));
            var tints = new[] { new Color(0.5f, 0.34f, 0.22f), new Color(0.62f, 0.45f, 0.27f), new Color(0.34f, 0.24f, 0.17f), new Color(0.56f, 0.33f, 0.22f) };
            var groups = new List<CombineInstance>[4]; for (int i = 0; i < 4; i++) groups[i] = new List<CombineInstance>();
            var films = new List<CombineInstance>();

            // wet grains: the hero grain + the grains closest to hair tips (water films cling where roots forage)
            var wet = new bool[rocks.Count]; var wetTip = new Vector3[rocks.Count]; wet[0] = true; wetTip[0] = hairs[0][18];
            int wetCount = 1;
            for (int h = 1; h < hairs.Count && wetCount < 12; h++)
            {
                var tip = hairs[h][hairs[h].Length - 1]; int best = -1; float bd = 1.4f;
                for (int i = 1; i < rocks.Count; i++) { float d = Vector3.Distance(tip, rocks[i].c) - rocks[i].r; if (d < bd) { bd = d; best = i; } }
                if (best > 0 && !wet[best]) { wet[best] = true; wetTip[best] = tip; wetCount++; }
            }

            var molPos = new List<Vector3>(); var molN = new List<Vector3>();
            for (int i = 0; i < rocks.Count; i++)
            {
                var k = rocks[i];
                var mesh = K.Deform(unit, v => Vector3.Scale(k.scale, v) * Lump(v, k.seed)); temp.Add(mesh);
                var vs = mesh.vertices; var uv = new Vector2[vs.Length];
                for (int j = 0; j < vs.Length; j++) uv[j] = new Vector2(vs[j].x + 0.5f * vs[j].z, vs[j].y + 0.3f * vs[j].z) * (k.r * 0.45f);   // seamless planar mapping
                mesh.uv = uv;
                groups[k.tint].Add(K.Part(mesh, k.c, k.q, Vector3.one * k.r));
                if (!wet[i]) continue;
                var film = K.Deform(unit, v => Vector3.Scale(k.scale, v) * (Lump(v, k.seed) + 0.2f / k.r)); temp.Add(film);
                films.Add(K.Part(film, k.c, k.q, Vector3.one * k.r));
                var toward = (wetTip[i] - k.c).normalized; int want = i == 0 ? 5 : 3;
                for (int m = 0, guard = 0; m < want && guard < 40; guard++)
                {
                    var p = Surface(k, (toward + K.OnSphere(r) * 0.8f).normalized, 0.1f);
                    bool clash = false; foreach (var q in molPos) if ((q - p).sqrMagnitude < 0.25f) clash = true;
                    if (clash) continue;
                    molPos.Add(p); molN.Add((p - k.c).normalized); m++;
                }
            }
            // a water bridge (meniscus) joins the hero hair tip to its grain's film
            var tip0 = hairs[0][18]; var foot = Surface(rocks[0], (tip0 - rocks[0].c).normalized, 0.12f);
            var bridge = K.Tube(new[] { tip0 + (tip0 - foot).normalized * 0.05f, Vector3.Lerp(tip0, foot, 0.5f), foot }, i => 0.17f + 0.13f * i, 10); temp.Add(bridge);
            films.Add(K.Part(bridge, Vector3.zero, Quaternion.identity, Vector3.one));

            for (int i = 0; i < 4; i++)
            {
                var m = Own(Fx.Mat(MatKind.Lit, tints[i], null, 0.22f)); Fx.SetTexture(m, tex);
                Fx.MeshObj("Soil Particles", Own(K.Merge(groups[i], "Soil Particles")), m, transform, true);
            }
            Fx.MeshObj("Water Films", Own(K.Merge(films, "Water Films")),
                Own(Fx.Mat(MatKind.LitTransparent, new Color(0.5f, 0.78f, 1f, 0.3f), Palette.Water * 0.22f, 0.95f)), transform);

            // sand: translucent quartz + dark mineral grains in the gaps
            var quartz = new List<CombineInstance>(); var dark = new List<CombineInstance>(); var grain = K.Sphere(8, 6); temp.Add(grain);
            for (int tries = 0; quartz.Count + dark.Count < 260 && tries < 5000; tries++)
            {
                var c = new Vector3(K.Rand(r, -16f, 16f), K.Rand(r, -11f, 13f), K.Rand(r, -4f, 14f)); float rad = K.Rand(r, 0.18f, 0.55f);
                bool ok = SegDist(c, CamPos, CamLook) > rad + 0.6f && (c - CamPos).magnitude > 3f;
                float s = axis.Nearest(c); ok &= Vector3.Distance(axis.At(s), c) > RootR(s) + rad;
                foreach (var k in rocks) if ((k.c - c).magnitude < k.r * 1.05f + rad) { ok = false; break; }
                if (!ok) continue;
                var part = K.Part(grain, c, Quaternion.Euler(K.Rand(r, 0, 360), K.Rand(r, 0, 360), 0), new Vector3(rad, rad * K.Rand(r, 0.6f, 0.95f), rad * K.Rand(r, 0.8f, 1.2f)));
                (r.NextDouble() < 0.6 ? quartz : dark).Add(part);
            }
            Fx.MeshObj("Quartz Grains", Own(K.Merge(quartz, "Quartz")), Own(Fx.Mat(MatKind.Glass, new Color(0.92f, 0.88f, 0.8f, 0.55f), new Color(0.06f, 0.05f, 0.04f))), transform);
            Fx.MeshObj("Mineral Grains", Own(K.Merge(dark, "Minerals")), Own(Fx.Mat(MatKind.Lit, new Color(0.25f, 0.18f, 0.12f), null, 0.35f)), transform, true);
            foreach (var m in temp) Destroy(m);

            // water molecules held in the films — clickable
            molRoot = Fx.Node("Water Molecules", transform).transform;
            int n = molPos.Count;
            mols = new Transform[n]; halos = new GameObject[n]; home = molPos.ToArray(); nrm = molN.ToArray(); taken = new bool[n]; near = new bool[n];
            var haloMat = K.GlowMat(new Color(Palette.Water.r * 1.15f, Palette.Water.g * 1.15f, Palette.Water.b * 1.15f, 0.9f));   // soft: the picked molecule reads, no bloom disc
            for (int i = 0; i < n; i++)
            {
                var g = Fx.Molecule(Mol.H2O, molRoot, 0.3f, 0.7f); g.name = "H2O";
                g.AddComponent<SphereCollider>().radius = 0.34f;
                mols[i] = g.transform; mols[i].localPosition = home[i];
                halos[i] = K.Glow(mols[i], Vector3.zero, 0.95f, haloMat); halos[i].SetActive(false);
                float bd = float.MaxValue; foreach (var hp in hairs) foreach (var p in hp) bd = Mathf.Min(bd, (p - home[i]).sqrMagnitude);
                near[i] = bd < 2.4f * 2.4f;
            }
        }

        void BuildAtmosphere()
        {
            var dome = Fx.MeshObj("Underground", Own(K.Sphere(32, 16, true)), Own(Fx.Mat(MatKind.Unlit, Fog * 0.8f)), transform);
            dome.transform.localScale = Vector3.one * 110f;
            // ponytail: caustic shimmer = three wandering, flickering point lights; upgrade: animated light cookie (needs URP cookie variants kept in builds).
            lampHome = new[] { new Vector3(-3.5f, -2f, -1.5f), new Vector3(6f, 7f, -1f), new Vector3(-8f, 3f, 2f) };
            var cols = new[] { new Color(0.5f, 0.8f, 1f), new Color(1f, 0.8f, 0.55f), new Color(0.6f, 0.85f, 1f) };
            lampI = new[] { 3f, 2f, 1.5f }; var ranges = new[] { 8f, 12f, 10f };
            lamps = new Light[3];
            for (int i = 0; i < 3; i++)
            {
                lamps[i] = Fx.Node("Caustic Light", transform, lampHome[i]).AddComponent<Light>();
                lamps[i].type = LightType.Point; lamps[i].color = cols[i]; lamps[i].intensity = lampI[i]; lamps[i].range = ranges[i]; lamps[i].shadows = LightShadows.None;
            }
            var shaftMat = Own(Fx.Mat(MatKind.Additive, new Color(1f, 0.9f, 0.72f, 0.06f))); Fx.SetTexture(shaftMat, K.ShaftTex);
            var dir = new Vector3(0.15f, -1f, 0.1f);
            foreach (var top in new[] { new Vector3(-8f, 16f, 6f), new Vector3(2f, 16f, 9f), new Vector3(9f, 16f, 4f), new Vector3(-3f, 16f, 12f) })
                K.Shaft(transform, top, dir, 26f, 4f, shaftMat);
            var silt = Fx.Particles("Silt", transform, new Fx.PCfg { color = new Color(0.75f, 0.6f, 0.45f, 0.6f), size = 0.07f, life = 9f, speed = 0.03f, rate = 12f, radius = 9f, noise = 0.12f, additive = false, max = 150 });
            silt.transform.localPosition = new Vector3(0f, 0f, 2f);
            var sparkle = Fx.Particles("Water Sparkle", transform, new Fx.PCfg { color = Palette.Water, color2 = Color.white, size = 0.05f, life = 3f, speed = 0.05f, rate = 8f, radius = 2.2f, noise = 0.2f, max = 40 });
            sparkle.transform.localPosition = rocks[0].c;
        }

        void BuildFx()
        {
            var ring = Own(K.Ring(64, 0.75f));
            ripples = new Transform[2]; rippleMats = new Material[2];
            for (int i = 0; i < 2; i++)
            {
                rippleMats[i] = Own(Fx.Mat(MatKind.Additive, Color.clear)); Fx.SetTexture(rippleMats[i], K.LineTex);
                ripples[i] = Fx.MeshObj("Ripple", ring, rippleMats[i], transform).transform; ripples[i].gameObject.SetActive(false);
            }
            head = K.Glow(transform, Vector3.zero, 1.3f, K.GlowMat(new Color(WaterHdr.r * 1.4f, WaterHdr.g * 1.4f, WaterHdr.b * 1.4f, 1f)));
            head.SetActive(false);
            glowInner = Own(Fx.Mat(MatKind.Additive, new Color(Palette.Water.r * 3.5f, Palette.Water.g * 3.5f, Palette.Water.b * 3.5f, 0.9f)));
            glowOuter = Own(Fx.Mat(MatKind.Additive, new Color(Palette.Water.r, Palette.Water.g, Palette.Water.b, 0.25f)));
            beadMat = Own(Fx.Mat(MatKind.Additive, new Color(0.8f * 3f, 0.92f * 3f, 3f, 1f)));
        }
    }
}
