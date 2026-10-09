using System.Collections.Generic;
using UnityEngine;

namespace Photosynthesis
{
    /// Hero plant construction: stem, leaves, corm, roots, soil, shoot, flower, fruit, vessel networks, materials, textures.
    /// Plant-local space = the spin pivot; y = 0 is the soil surface. All lengths in metres.
    public partial class HeroPlant
    {
        struct LeafDef
        {
            public float yaw, a, h, d, L, p0, p1, roll;   // azimuth · attach height · petiole rise/reach · blade length · midrib pitch base/tip · roll
            public LeafDef(float yaw, float a, float h, float d, float L, float p0, float p1, float roll) { this.yaw = yaw; this.a = a; this.h = h; this.d = d; this.L = L; this.p0 = p0; this.p1 = p1; this.roll = roll; }
        }

        // yaw 180 = toward the learner (−Z). Two interleaved tiers, 80–106° apart within a tier: a lower ring of big
        // outward-drooping leaves and an upper ring of upright ones; the rolled shoot crowns the centre. Blade-to-blade
        // clearance ≥ 18 cm at rest and ≥ 9 cm at the worst idle-sway extremes (dormant droop only opens the gaps).
        // Leaf 0 is the focus leaf: low, front-left, blade tilted toward the camera (centre ≈ (−0.42, 2.02, −1.35)).
        static readonly LeafDef[] Defs =
        {
            new LeafDef(195f, 0.55f, 1.85f, 0.85f, 1.35f,  30f, 80f,  0f),
            new LeafDef(110f, 0.68f, 1.95f, 1.05f, 1.50f,  12f, 62f, -6f),
            new LeafDef(285f, 0.62f, 1.95f, 1.05f, 1.50f,  15f, 65f,  5f),
            new LeafDef( 22f, 0.78f, 2.00f, 1.00f, 1.55f,   8f, 58f, -4f),
            new LeafDef(155f, 1.05f, 2.30f, 0.80f, 1.40f, -50f,  5f, -6f),
            new LeafDef(235f, 1.00f, 2.35f, 0.85f, 1.45f, -50f,  5f,  6f),
            new LeafDef(322f, 1.12f, 2.45f, 0.85f, 1.60f, -50f, 10f,  5f),
            new LeafDef( 68f, 1.15f, 2.45f, 0.85f, 1.60f, -50f, 10f, -5f),
        };
        /// Stem heights where the flower peduncle and the fruit stalk leave the stem (flower at yaw 135°, fruit at 70°:
        /// both clear every petiole by ≥ 9 cm and every blade by ≥ 0.6 m).
        const float FlowerY = 0.6f, FruitY = 0.68f;

        class LeafPart
        {
            public int index; public LeafDef def; public PlantLeafShape shape;
            public Transform root, blade;                 // root = petiole base (sways), blade = frame at the petiole tip
            public Quaternion rootRest, bladeRest; public Vector3 attach, bladePos;
            public List<Vector3> petiole;                 // root-local
            public Mesh mesh, stub; public MatPair top, under;
            public GameObject rim; public Wave wave;
            public Vector3 center, centerN, underside;    // blade-local
            public Vector3[] topP, topN, undP, undN;      // blade-local surface samples (particles, molecules)
            public float swayF, swayPh, growGlow, flash, lg; public bool hl;
        }

        class MatPair { public Material o, t; public Color c, e, lastE; public bool leafy; }
        class Look { public Renderer r; public Material[] o, t; public bool shadows; }

        bool built;
        Transform pivot, stemNode, underground, soilNode, shootNode, flowerNode, fruitNode;
        readonly List<LeafPart> leaves = new List<LeafPart>();
        int[] growOrder = new int[0];
        List<Vector3> stemAxis, shootPts, flowerPed, fruitPed;   // plant-local
        readonly List<List<Vector3>> mainRoots = new List<List<Vector3>>();
        Vector3 rootTip, shootTip, flowerPos, fruitPos, cormCenter = new Vector3(0f, -0.28f, 0f);
        float topHeight = 4.2f;

        readonly List<Object> owned = new List<Object>();
        readonly List<MatPair> pairs = new List<MatPair>();
        readonly List<Look> looks = new List<Look>();
        MatPair mStem, mPetiole, mRoot, mHair, mCorm;
        Material rimMat, mXylem, mPhloem;
        Material[] soilSolid, soilGhost; MeshRenderer soilR;
        Texture2D texTop, texUnder, texPetiole;
        readonly List<GameObject> xylemObjs = new List<GameObject>(), phloemObjs = new List<GameObject>();
        Mesh beadMesh, hexMesh, molSphere;

        float growth, transp, dormantW, spinSpeed, spinAngle, spinVel;
        int growTok, transpTok;
        bool transMode, rootsShown, vesX, vesP, xylemVis, phloemVis, hlStem, hlRoots, hlXylem, hlPhloem, dormant;

        void Build()
        {
            built = true;
            var rnd = new System.Random(4207);
            pivot = Fx.Node("Plant", transform).transform;
            beadMesh = Own(PlantMesh.Sphere(8, 6)); molSphere = Own(PlantMesh.Sphere(12, 8)); hexMesh = Own(PlantMesh.Hexagon());

            texTop = SurfaceArt.Map("LeafTop", "Albedo"); texUnder = SurfaceArt.Map("LeafUnder", "Albedo");
            if (!texTop) texTop = Own(PlantLeafShape.BladeTexture(512, true));
            if (!texUnder) texUnder = Own(PlantLeafShape.BladeTexture(256, false));
            texPetiole = Own(PlantMesh.Tex(32, 128, (u, v) => Fibre(new Color(0.50f, 0.66f, 0.31f), u, v), "Petiole"));
            var texShoot = Own(PlantMesh.Tex(32, 128, (u, v) => Fibre(new Color(0.68f, 0.84f, 0.36f), u, v), "Shoot"));

            mStem = Pair(MatKind.LitEmissive, Color.white, Own(PlantMesh.Tex(32, 256, StemPx, "Stem")), 0.3f, true);
            mPetiole = Pair(MatKind.LitEmissive, Color.white, texPetiole, 0.45f, true);
            mRoot = Pair(MatKind.LitEmissive, new Color(0.86f, 0.78f, 0.62f), null, 0.35f, false);
            mHair = Pair(MatKind.LitEmissive, new Color(0.97f, 0.95f, 0.88f), null, 0.2f, false);
            SurfaceArt.Apply(mRoot.o, "Root"); SurfaceArt.Apply(mRoot.t, "Root");
            SurfaceArt.Apply(mHair.o, "Root"); SurfaceArt.Apply(mHair.t, "Root");
            mCorm = Pair(MatKind.LitEmissive, Color.white, Own(PlantMesh.Tex(32, 128, CormPx, "Corm")), 0.3f, false);
            var mShoot = Pair(MatKind.Leaf, Color.white, texShoot, 0.5f, true);
            var mSpathe = Pair(MatKind.Leaf, new Color(0.96f, 0.94f, 0.83f), null, 0.5f, false);
            var mSpadix = Pair(MatKind.LitEmissive, new Color(0.98f, 0.86f, 0.46f), null, 0.3f, false, K(Palette.Gold, 0.25f));
            var mBerry = Pair(MatKind.LitEmissive, new Color(0.80f, 0.09f, 0.06f), null, 0.85f, false, new Color(0.30f, 0.02f, 0.01f));
            mXylem = Own(Fx.Mat(MatKind.LitEmissive, K(Palette.Xylem, 0.5f), K(Palette.Xylem, 1.1f)));     // emission kept near 1: blue stays blue under bloom
            mPhloem = Own(Fx.Mat(MatKind.LitEmissive, K(Palette.Phloem, 0.5f), K(Palette.Phloem, 1.1f)));   // and orange stays orange (2.4 clipped both to white-gold)
            rimMat = Own(Fx.Mat(MatKind.Additive, K(RimColor, 2.4f)));

            BuildStem();
            BuildUnderground(rnd);
            BuildSoil(rnd);
            for (int i = 0; i < Defs.Length; i++) leaves.Add(BuildLeaf(i, Defs[i], rnd));
            BuildShoot(mShoot); BuildFlower(mSpathe, mSpadix); BuildFruit(mBerry, rnd);
            BuildStemVessels(true); BuildStemVessels(false);

            growOrder = new int[leaves.Count];
            for (int i = 0; i < growOrder.Length; i++) growOrder[i] = i;
            System.Array.Sort(growOrder, (x, y) => leaves[x].def.a.CompareTo(leaves[y].def.a));

            topHeight = shootTip.y;
            foreach (var lf in leaves)
                foreach (var q in PlantLeafShape.Outline())
                    topHeight = Mathf.Max(topHeight, (lf.attach + lf.rootRest * (lf.bladePos + lf.bladeRest * lf.shape.Pos(q.x, q.y))).y);

            ApplyLook(); ApplyGrowth(1f); ShowRoots(false);
        }

        // ---------------------------------------------------------------- stem
        static float StemRt(float t) => Mathf.Lerp(0.19f, 0.10f, Mathf.Pow(t, 0.7f)) * (1f + 0.28f * Mathf.Exp(-t * 14f)) * (t > 0.86f ? Mathf.Lerp(1f, 0.4f, (t - 0.86f) / 0.14f) : 1f);
        static float StemR(float y) => StemRt(Mathf.Clamp01((y + 0.14f) / 1.56f));
        static float PetioleR(float t) => Mathf.Lerp(0.058f, 0.03f, PlantMesh.Sstep(0f, 0.22f, t)) - 0.008f * t;

        /// Point on the stem axis at height y (extrapolates straight down into the corm).
        Vector3 StemAt(float y)
        {
            for (int i = 1; i < stemAxis.Count; i++)
                if (stemAxis[i].y >= y || i == stemAxis.Count - 1)
                {
                    var p = Vector3.Lerp(stemAxis[i - 1], stemAxis[i], Mathf.InverseLerp(stemAxis[i - 1].y, stemAxis[i].y, y));
                    p.y = y; return p;
                }
            return stemAxis[0];
        }

        /// Vascular strand k (3 xylem + 3 phloem alternate around the stem axis).
        Vector3 Strand(int k, bool xylem, float y)
        {
            float a = (k * 120f + (xylem ? 0f : 60f)) * Mathf.Deg2Rad, r = 0.05f * (y < 0f ? Mathf.Lerp(0.25f, 1f, Mathf.Clamp01(1f + y / 0.28f)) : 1f);
            return StemAt(y) + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
        }

        void BuildStem()
        {
            stemAxis = Fx.Smooth(new[] { new Vector3(0f, -0.14f, 0f), new Vector3(0.01f, 0.40f, 0f), new Vector3(0.03f, 0.92f, 0.02f), new Vector3(0.02f, 1.42f, 0.01f) }, 14);
            stemNode = Fx.Node("Stem", pivot).transform;
            var b = new PlantMesh(); b.Tube(stemAxis, StemRt, 14, 0, 1f);
            var g = Fx.MeshObj("Stem Mesh", Own(b.Build("Stem")), null, stemNode, true); Reg(g, true, mStem);
            var cap = g.AddComponent<CapsuleCollider>(); cap.center = new Vector3(0.015f, 0.62f, 0.01f); cap.height = 1.6f; cap.radius = 0.17f; cap.direction = 1;
            g.AddComponent<PlantPartTag>().part = PlantPart.Stem;
        }

        void BuildStemVessels(bool xylem)
        {
            var b = new PlantMesh();
            for (int k = 0; k < 3; k++)
            {
                var pts = new List<Vector3>(13);
                for (int i = 0; i <= 12; i++) pts.Add(Strand(k, xylem, Mathf.Lerp(-0.28f, 1.34f, i / 12f)));
                b.Tube(pts, t => 0.0085f, 6, 0);
            }
            Vessel(xylem ? "Stem Xylem" : "Stem Phloem", b, xylem, pivot);
        }

        void Vessel(string name, PlantMesh b, bool xylem, Transform parent)
        {
            var go = Fx.MeshObj(name, Own(b.Build(name)), xylem ? mXylem : mPhloem, parent);
            go.SetActive(false); (xylem ? xylemObjs : phloemObjs).Add(go);
        }

        // ---------------------------------------------------------------- leaves
        LeafPart BuildLeaf(int i, LeafDef d, System.Random rnd)
        {
            var sh = new PlantLeafShape(d.L, d.p0, d.p1);
            var lf = new LeafPart { index = i, def = d, shape = sh, swayF = Mathf.Lerp(0.7f, 1.1f, R(rnd)), swayPh = R(rnd) * 6.28f };
            Vector3 dir = Quaternion.Euler(0f, d.yaw, 0f) * Vector3.forward;
            lf.attach = StemAt(d.a) + dir * StemR(d.a) * 0.75f;
            lf.rootRest = Quaternion.Euler(0f, d.yaw, 0f);
            lf.root = Fx.Node("Leaf " + i, pivot, lf.attach).transform; lf.root.localRotation = lf.rootRest;

            // petiole: steep from the stem, arching outward (local +Z = outward)
            lf.petiole = Fx.Smooth(new[] { Vector3.zero, new Vector3(0f, 0.30f * d.h, 0.06f * d.d), new Vector3(0f, 0.70f * d.h, 0.35f * d.d), new Vector3(0f, d.h, d.d) }, 16);
            var pb = new PlantMesh(); pb.Tube(lf.petiole, PetioleR, 8, 0, 1.2f);
            Reg(Fx.MeshObj("Petiole", Own(pb.Build("Petiole")), null, lf.root, true), true, mPetiole);

            // blade hangs from the petiole tip at the sinus
            lf.bladePos = new Vector3(0f, d.h, d.d); lf.bladeRest = Quaternion.Euler(0f, 0f, d.roll);
            lf.blade = Fx.Node("Blade", lf.root, lf.bladePos).transform; lf.blade.localRotation = lf.bladeRest;
            lf.mesh = Own(sh.Build());
            lf.top = Pair(MatKind.Leaf, Color.white, texTop, 0.62f, true);
            lf.under = Pair(MatKind.Leaf, Color.white, texUnder, 0.3f, true);
            SurfaceArt.Apply(lf.top.o, "LeafTop"); SurfaceArt.Apply(lf.top.t, "LeafTop");
            SurfaceArt.Apply(lf.under.o, "LeafUnder"); SurfaceArt.Apply(lf.under.t, "LeafUnder");
            var bl = Fx.MeshObj("Blade Mesh", lf.mesh, null, lf.blade, true);
            Reg(bl, true, lf.top, lf.under, mPetiole);
            bl.AddComponent<MeshCollider>().sharedMesh = lf.mesh;
            var tag = bl.AddComponent<PlantPartTag>(); tag.part = PlantPart.Leaf; tag.leaf = i;

            lf.centerN = sh.Normal(0.45f, 0.05f);
            lf.center = sh.Pos(0.45f, 0.05f) + lf.centerN * 0.004f;
            lf.underside = sh.Pos(0.15f, 0.10f) - sh.Normal(0.15f, 0.10f) * 0.06f;

            lf.topP = new Vector3[40]; lf.topN = new Vector3[40];
            for (int k = 0; k < lf.topP.Length; k++)
            {
                float s = Mathf.Lerp(-0.2f, 0.9f, R(rnd)), x = (R(rnd) < 0.5f ? -1f : 1f) * Mathf.Lerp(PlantLeafShape.Xin(s), PlantLeafShape.Xout(s), Mathf.Lerp(0.05f, 0.9f, R(rnd)));
                lf.topN[k] = sh.Normal(s, x); lf.topP[k] = sh.Pos(s, x);
            }
            lf.undP = new Vector3[12]; lf.undN = new Vector3[12];
            for (int k = 0; k < lf.undP.Length; k++)
            {
                float s = Mathf.Lerp(0.08f, 0.75f, R(rnd)), x = (R(rnd) < 0.5f ? -1f : 1f) * PlantLeafShape.Xout(s) * Mathf.Lerp(0.15f, 0.8f, R(rnd));
                var n = sh.Normal(s, x); lf.undN[k] = -n; lf.undP[k] = sh.Pos(s, x) - n * 0.004f;
            }

            BuildLeafVessels(lf, true); BuildLeafVessels(lf, false);
            return lf;
        }

        /// Blade-local (rest pose) → leaf-root-local.
        static Vector3 B2R(LeafPart lf, Vector3 p) => lf.bladePos + lf.bladeRest * p;

        /// Vessel centre line: inside the stem → petiole → midrib up to sEnd (leaf-root space). side −1 xylem, +1 phloem.
        static List<Vector3> LeafVesselPath(LeafPart lf, int side, float sEnd)
        {
            var sh = lf.shape; float off = 0.006f * side;
            var p = new List<Vector3>(32) { new Vector3(off, -0.06f, -0.10f) };
            foreach (var q in lf.petiole) p.Add(q + new Vector3(off, 0f, 0f));
            for (int k = 1; k <= 14; k++) { float s = sEnd * k / 14f; p.Add(B2R(lf, sh.Pos(s, off / sh.L) - sh.Normal(s, 0f) * 0.002f)); }
            return p;
        }

        void BuildLeafVessels(LeafPart lf, bool xylem)
        {
            var b = new PlantMesh(); var sh = lf.shape; int side = xylem ? -1 : 1;
            b.Tube(LeafVesselPath(lf, side, 0.92f), t => Mathf.Lerp(0.0075f, 0.003f, t), 6, 0);
            for (int id = 0; id < PlantLeafShape.VeinIds; id++)
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    var vein = PlantLeafShape.Vein(id, sd, 8); var pts = new List<Vector3>(8);
                    foreach (var q in vein) pts.Add(B2R(lf, sh.Pos(q.x, q.y) + sh.Normal(q.x, q.y) * (0.0012f * side)));
                    b.Tube(pts, t => Mathf.Lerp(0.0036f, 0.0012f, t), 3, 0);
                }
            Vessel(xylem ? "Xylem" : "Phloem", b, xylem, lf.root);
        }

        GameObject Rim(LeafPart lf)
        {
            if (lf.rim) return lf.rim;
            var o = PlantLeafShape.Outline(); var pts = new List<Vector3>(o.Count);
            foreach (var q in o) pts.Add(lf.shape.Pos(q.x, q.y) + lf.shape.Normal(q.x, q.y) * 0.003f);
            var b = new PlantMesh(); b.Tube(pts, t => 0.009f, 4, 0);
            lf.rim = Fx.MeshObj("Highlight Rim", Own(b.Build("Rim")), rimMat, lf.blade); lf.rim.SetActive(false);
            return lf.rim;
        }

        // ---------------------------------------------------------------- underground: corm, roots, root hairs
        void BuildUnderground(System.Random rnd)
        {
            underground = Fx.Node("Underground", pivot).transform;

            var cb = new PlantMesh();
            Vector3 Corm(float u, float w)
            {
                float th = -u * Mathf.PI * 2f, y = Mathf.Lerp(-0.56f, 0.03f, w);
                float r = w <= 0.5f ? 0.31f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * w)), 0.6f) : Mathf.Lerp(0.31f, 0.15f, PlantMesh.Sstep(0.5f, 1f, w));
                r *= 1f + 0.035f * Mathf.Sin(w * 55f) + 0.03f * Mathf.Sin(3f * th + w * 5f) + 0.02f * Mathf.Sin(5f * th + 1.3f);
                return new Vector3(Mathf.Cos(th) * r, y, Mathf.Sin(th) * r);
            }
            cb.Surface(Corm, null, (u, w) => new Vector2(u * 3f, w), 22, 14, 0);
            var corm = Fx.MeshObj("Corm (storage)", Own(cb.Build("Corm")), null, underground); Reg(corm, false, mCorm);
            var sc = corm.AddComponent<SphereCollider>(); sc.center = cormCenter; sc.radius = 0.3f;
            corm.AddComponent<PlantPartTag>().part = PlantPart.Roots;

            var rb = new PlantMesh();
            for (int i = 0; i < 9; i++)
            {
                List<Vector3> pts;
                if (i == 0)   // the investigated root: straight down in front, tip ≈ 2 m below the soil
                    pts = Fx.Smooth(new[] { new Vector3(0f, -0.40f, -0.24f), new Vector3(0.05f, -0.80f, -0.36f), new Vector3(0.12f, -1.25f, -0.42f), new Vector3(0.07f, -1.65f, -0.47f), new Vector3(0.12f, -2.0f, -0.45f) }, 20);
                else
                {
                    float ang = (i - 1) * 45f + 22f + Rf(rnd, -10f, 10f), reach = Rf(rnd, 0.55f, 1.15f), depth = Rf(rnd, 1.25f, 1.85f);
                    Vector3 dir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward, side = Vector3.Cross(dir, Vector3.up);
                    Vector3 b0 = cormCenter + dir * 0.24f + Vector3.down * Rf(rnd, 0.02f, 0.18f);
                    var c = new Vector3[6];
                    for (int k = 0; k < 6; k++) { float t = k / 5f; c[k] = b0 + dir * reach * (1f - (1f - t) * (1f - t)) + Vector3.down * depth * Mathf.Pow(t, 1.3f) + side * (Mathf.Sin(t * 5f + i) * 0.07f * t); }
                    pts = Fx.Smooth(c, 18);
                }
                mainRoots.Add(pts);
                rb.Tube(pts, MainRootR, 7, 0);
                Hairs(rb, pts, MainRootR, 46, 0.03f, 0.28f, rnd);
                for (int j = 0; j < 3; j++)
                {
                    float t0 = 0.25f + 0.2f * j + Rf(rnd, -0.05f, 0.05f);
                    Vector3 p0 = Fx.Along(pts, t0), tan = (Fx.Along(pts, t0 + 0.02f) - p0).normalized;
                    Vector3 ldir = (Vector3.ProjectOnPlane(RandDir(rnd), tan).normalized * 0.8f + Vector3.down * 0.35f).normalized;
                    float len = Rf(rnd, 0.22f, 0.45f);
                    var lp = Fx.Smooth(new[] { p0, p0 + ldir * len * 0.35f + tan * 0.03f, p0 + ldir * len * 0.7f + Vector3.down * 0.04f, p0 + ldir * len + Vector3.down * 0.1f }, 8);
                    rb.Tube(lp, LateralRootR, 5, 0);
                    Hairs(rb, lp, LateralRootR, 12, 0.02f, 0.12f, rnd);
                }
            }
            rootTip = mainRoots[0][mainRoots[0].Count - 1];
            var roots = Fx.MeshObj("Roots", Own(rb.Build("Roots")), null, underground); Reg(roots, false, mRoot, mHair);
            var rc = roots.AddComponent<CapsuleCollider>(); rc.center = new Vector3(0f, -1.1f, -0.1f); rc.height = 2.0f; rc.radius = 0.7f; rc.direction = 1;
            roots.AddComponent<PlantPartTag>().part = PlantPart.Roots;

            // root vessels: from the corm centre down every main root (phloem beside the xylem)
            var xb = new PlantMesh(); var fb = new PlantMesh();
            foreach (var r in mainRoots)
            {
                var vp = new List<Vector3>(r.Count + 1) { cormCenter }; vp.AddRange(r);
                xb.Tube(vp, t => Mathf.Lerp(0.011f, 0.0016f, Mathf.Pow(t, 0.8f)), 5, 0);
                Vector3 side = Vector3.Cross((r[r.Count - 1] - r[0]).normalized, Vector3.up); side = side.sqrMagnitude < 1e-4f ? Vector3.right : side.normalized;
                var pp = new List<Vector3>(vp.Count);
                for (int k = 0; k < vp.Count; k++) pp.Add(vp[k] + side * (0.012f * (1f - k / (float)vp.Count)));
                fb.Tube(pp, t => Mathf.Lerp(0.009f, 0.0014f, Mathf.Pow(t, 0.8f)), 5, 0);
            }
            Vessel("Root Xylem", xb, true, underground); Vessel("Root Phloem", fb, false, underground);
        }

        static float MainRootR(float t) => Mathf.Lerp(0.034f, 0.004f, Mathf.Pow(t, 0.8f));
        static float LateralRootR(float t) => Mathf.Lerp(0.012f, 0.0025f, t);

        /// Fine root hairs (tapered 3-sided cones) in the zone `d0…d1` metres behind the tip (sub-mesh 1).
        static void Hairs(PlantMesh b, List<Vector3> root, System.Func<float, float> radius, int count, float d0, float d1, System.Random rnd)
        {
            float total = 0f; for (int i = 1; i < root.Count; i++) total += Vector3.Distance(root[i - 1], root[i]);
            var two = new Vector3[2];
            for (int i = 0; i < count; i++)
            {
                float f = 1f - Rf(rnd, d0, d1) / total; if (f <= 0f) continue;
                Vector3 p = Fx.Along(root, f), tan = (Fx.Along(root, Mathf.Min(1f, f + 0.01f)) - Fx.Along(root, Mathf.Max(0f, f - 0.01f))).normalized;
                Vector3 dir = Vector3.ProjectOnPlane(RandDir(rnd), tan).normalized;
                two[0] = p + dir * radius(f) * 0.8f;
                two[1] = two[0] + (dir * 0.9f + Vector3.down * 0.15f).normalized * Rf(rnd, 0.02f, 0.055f);
                b.Tube(two, t => 0.0017f * (1f - t), 3, 1);
            }
        }

        // ---------------------------------------------------------------- soil bed
        /// Rich soil collar round the stem. It domes over the chamber's soil mound and dips under it at the rim,
        /// so the two meet along a natural intersection instead of a lip.
        static float SoilY(float w) => 0.045f * (1f - w * w) - 0.03f * Mathf.Pow(w, 6f);

        void BuildSoil(System.Random rnd)
        {
            soilNode = Fx.Node("Soil", pivot).transform;
            const float R = 0.95f;
            var b = new PlantMesh();
            b.Surface((u, w) => { float th = u * Mathf.PI * 2f, r = w * R; return new Vector3(Mathf.Cos(th) * r, SoilY(w) + 0.006f * Mathf.Sin(th * 7f + w * 9f) * w * (1f - w), Mathf.Sin(th) * r); },
                      (u, w) => Vector3.up, (u, w) => { float th = u * Mathf.PI * 2f; return new Vector2(Mathf.Cos(th), Mathf.Sin(th)) * (w * R * 1.4f); }, 48, 10, 0);
            var sphere = molSphere;
            for (int i = 0; i < 16; i++)   // moss cushions (sub 1) and pebbles (sub 2)
            {
                bool moss = i < 10; float th = Rf(rnd, 0f, Mathf.PI * 2f), r = Rf(rnd, 0.28f, 0.78f);
                var pos = new Vector3(Mathf.Cos(th) * r, SoilY(r / R), Mathf.Sin(th) * r);
                var sc = moss ? new Vector3(Rf(rnd, 0.12f, 0.24f), Rf(rnd, 0.035f, 0.06f), Rf(rnd, 0.10f, 0.2f)) : Vector3.one * Rf(rnd, 0.035f, 0.07f) + new Vector3(0.02f, -0.01f, 0f);
                b.Merge(sphere, Matrix4x4.TRS(pos, Quaternion.Euler(0f, Rf(rnd, 0f, 360f), 0f), sc), moss ? 1 : 2);
            }
            var soil = Own(Fx.Mat(MatKind.Lit, Color.white, null, 0.12f)); Fx.SetTexture(soil, Own(PlantMesh.Tex(256, 256, SoilPx, "Soil")));
            SurfaceArt.Apply(soil, "Soil", true, 1f);
            var ghost = Own(Fx.Mat(MatKind.LitTransparent, new Color(0.25f, 0.17f, 0.11f, 0.22f), Color.black, 0.1f));
            soilSolid = new[] { soil, Own(Fx.Mat(MatKind.Lit, new Color(0.10f, 0.24f, 0.07f), null, 0.15f)), Own(Fx.Mat(MatKind.Lit, new Color(0.46f, 0.43f, 0.39f), null, 0.3f)) };
            soilGhost = new[] { ghost, ghost, ghost };
            var g = Fx.MeshObj("Soil Bed", Own(b.Build("Soil")), null, soilNode);
            soilR = g.GetComponent<MeshRenderer>(); soilR.sharedMaterials = soilSolid;
        }

        // ---------------------------------------------------------------- shoot, flower, fruit
        void BuildShoot(MatPair m)
        {
            shootNode = Fx.Node("Shoot", pivot, new Vector3(0.02f, 1.30f, 0.01f)).transform;
            var sp = Fx.Smooth(new[] { Vector3.zero, new Vector3(0.02f, 0.5f, -0.01f), new Vector3(0.06f, 1.0f, -0.03f), new Vector3(0.04f, 1.45f, -0.02f) }, 16);
            var b = new PlantMesh(); b.Tube(sp, t => (0.042f + 0.02f * Mathf.Sin(Mathf.PI * t * 0.9f)) * (1f - Mathf.Pow(t, 4f)), 10, 0, 1.5f);
            Reg(Fx.MeshObj("Rolled New Leaf", Own(b.Build("Shoot")), null, shootNode, true), true, m);
            shootPts = new List<Vector3>(sp.Count); foreach (var p in sp) shootPts.Add(shootNode.localPosition + p);
            shootTip = shootPts[shootPts.Count - 1];
        }

        static Vector3 SpatheP(float u, float w)
        {
            float span = Mathf.Lerp(165f, 115f, w) * Mathf.Deg2Rad, th = Mathf.Lerp(-span, span, u), rel = Mathf.Abs(th) / span;
            float r = 0.085f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * (0.12f + 0.88f * w))), 0.75f) * (1f + 0.35f * rel * rel * w);
            return new Vector3(Mathf.Sin(th) * r, w * 0.40f, -Mathf.Cos(th) * r + 0.07f * w * w);
        }

        void BuildFlower(MatPair spathe, MatPair spadix)
        {
            Vector3 dir = Quaternion.Euler(0f, 135f, 0f) * Vector3.forward, pb = StemAt(FlowerY) + dir * 0.08f;
            flowerNode = Fx.Node("Flower", pivot, pb).transform;
            var ped = Fx.Smooth(new[] { Vector3.zero, dir * 0.12f + Vector3.up * 0.35f, dir * 0.32f + Vector3.up * 0.70f, dir * 0.47f + Vector3.up * 0.80f }, 12);
            var M = Matrix4x4.TRS(ped[ped.Count - 1], Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(12f, 0f, 0f), Vector3.one);
            var b = new PlantMesh();
            b.Surface((u, w) => M.MultiplyPoint3x4(SpatheP(u, w)), null, (u, w) => new Vector2(u, w), 16, 12, 0, 0.004f, 0);
            var sx = new List<Vector3>(6); for (int i = 0; i < 6; i++) sx.Add(M.MultiplyPoint3x4(new Vector3(0f, 0.30f * i / 5f, 0.015f + 0.02f * i / 5f)));
            b.Tube(sx, t => 0.022f * (1f - Mathf.Pow(t, 6f)), 10, 1);
            b.Tube(ped, t => Mathf.Lerp(0.02f, 0.014f, t), 7, 2);
            Reg(Fx.MeshObj("Spathe + Spadix", Own(b.Build("Flower")), null, flowerNode, true), true, spathe, spadix, mPetiole);
            flowerPed = new List<Vector3>(ped.Count); foreach (var p in ped) flowerPed.Add(pb + p);
            flowerPos = pb + M.MultiplyPoint3x4(new Vector3(0f, 0.17f, 0.025f));
        }

        void BuildFruit(MatPair berry, System.Random rnd)
        {
            Vector3 dir = Quaternion.Euler(0f, 70f, 0f) * Vector3.forward, fb = StemAt(FruitY) + dir * 0.09f;
            fruitNode = Fx.Node("Fruit", pivot, fb).transform;
            var ped = Fx.Smooth(new[] { Vector3.zero, dir * 0.18f + Vector3.up * 0.32f, dir * 0.40f + Vector3.up * 0.56f, dir * 0.55f + Vector3.up * 0.52f }, 12);
            Vector3 end = ped[ped.Count - 1], axis = (dir * 0.55f + Vector3.down).normalized;
            Vector3 p1 = Vector3.Cross(axis, Vector3.up).normalized, p2 = Vector3.Cross(axis, p1);
            var b = new PlantMesh(); var bm = Own(PlantMesh.Sphere(8, 5));
            for (int i = 0; i < 48; i++)
            {
                float t = i / 47f, rr = 0.040f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * (0.08f + 0.84f * t))), 0.6f) + 0.006f, a = i * 137.508f * Mathf.Deg2Rad;
                var pos = end + axis * (0.02f + t * 0.17f) + (p1 * Mathf.Cos(a) + p2 * Mathf.Sin(a)) * rr;
                b.Merge(bm, Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * 0.046f * Rf(rnd, 0.85f, 1.15f)), 0);
            }
            b.Tube(new List<Vector3> { end, end + axis * 0.19f }, t => 0.012f * (1f - 0.5f * t), 6, 1);
            b.Tube(ped, t => Mathf.Lerp(0.019f, 0.013f, t), 7, 1);
            Reg(Fx.MeshObj("Berry Cluster", Own(b.Build("Fruit")), null, fruitNode, true), true, berry, mPetiole);
            fruitPed = new List<Vector3>(ped.Count); foreach (var p in ped) fruitPed.Add(fb + p);
            fruitPos = fb + end + axis * 0.10f;
        }

        // ---------------------------------------------------------------- helpers
        T Own<T>(T o) where T : Object { owned.Add(o); return o; }

        MatPair Pair(MatKind kind, Color c, Texture tex, float smooth, bool leafy, Color? e = null)
        {
            var p = new MatPair { c = c, e = e ?? Color.black, leafy = leafy, lastE = new Color(-1f, 0f, 0f, 0f) };
            p.o = Own(Fx.Mat(kind, c, p.e, smooth)); p.t = Own(Fx.Mat(MatKind.LitTransparent, c, p.e, smooth));
            if (tex) { Fx.SetTexture(p.o, tex); Fx.SetTexture(p.t, tex); }
            pairs.Add(p); return p;
        }

        /// Registers a renderer for the opaque ⇄ transparent material swap.
        void Reg(GameObject g, bool shadows, params MatPair[] m)
        {
            var look = new Look { r = g.GetComponent<MeshRenderer>(), shadows = shadows, o = new Material[m.Length], t = new Material[m.Length] };
            for (int i = 0; i < m.Length; i++) { look.o[i] = m[i].o; look.t[i] = m[i].t; }
            look.r.sharedMaterials = look.o;
            look.r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            looks.Add(look);
        }

        static Color K(Color c, float k) { c *= k; c.a = 1f; return c; }
        static float R(System.Random r) => (float)r.NextDouble();
        static float Rf(System.Random r, float a, float b) => Mathf.Lerp(a, b, (float)r.NextDouble());
        static Vector3 RandDir(System.Random r) { var v = new Vector3(R(r) - 0.5f, R(r) - 0.5f, R(r) - 0.5f); return v.sqrMagnitude < 1e-4f ? Vector3.right : v.normalized; }

        const float Tau = Mathf.PI * 2f;

        /// Petiole / shoot: fine longitudinal fibres and faint speckles (tiles seamlessly).
        static Color Fibre(Color c, float u, float v)
        {
            float k = 0.90f + 0.07f * Mathf.Sin(u * Tau * 14f) + 0.04f * Mathf.Sin(u * Tau * 37f + Mathf.Sin(v * Tau * 3f) * 2f);
            k -= Mathf.Max(0f, Mathf.Sin(u * Tau * 23f + v * Tau * 7f) * Mathf.Sin(u * Tau * 11f - v * Tau * 13f) - 0.8f) * 0.8f;
            var o = c * k; o.a = 1f; return o;
        }

        /// Stem: leaf-scar rings over olive green with vertical streaks.
        static Color StemPx(float u, float v)
        {
            float f = Mathf.Repeat(v * 6f, 1f), ring = Mathf.Exp(-Mathf.Pow((f - 0.5f) / 0.025f, 2f)), band = Mathf.Exp(-Mathf.Pow((f - 0.56f) / 0.06f, 2f));
            var c = new Color(0.36f, 0.45f, 0.24f) * (0.92f + 0.08f * Mathf.Sin(u * Tau * 9f + Mathf.Sin(v * Tau * 3f) * 1.5f));
            c = Color.Lerp(c, new Color(0.47f, 0.52f, 0.30f), band * 0.5f);
            c = Color.Lerp(c, new Color(0.26f, 0.22f, 0.14f), ring * 0.8f); c.a = 1f; return c;
        }

        static Color CormPx(float u, float v)
        {
            float f = Mathf.Repeat(v * 7f, 1f), ring = Mathf.Exp(-Mathf.Pow((f - 0.5f) / 0.05f, 2f));
            var c = new Color(0.47f, 0.33f, 0.21f) * (0.9f + 0.1f * Mathf.Sin(u * Tau * 5f + v * 20f));
            c = Color.Lerp(c, new Color(0.24f, 0.16f, 0.10f), ring * 0.75f); c.a = 1f; return c;
        }

        /// Rich dark soil with perlite specks and darker clumps (tiles seamlessly).
        static Color SoilPx(float u, float v)
        {
            float n = Mathf.Sin(u * Tau * 3f + 1.3f) * Mathf.Sin(v * Tau * 4f + 0.4f) * 0.5f + Mathf.Sin(u * Tau * 11f + v * Tau * 7f + 2f) * 0.25f
                    + Mathf.Sin(u * Tau * 29f - v * Tau * 23f) * 0.15f + Mathf.Sin(u * Tau * 61f + v * Tau * 53f + 4f) * 0.1f;
            var c = Color.Lerp(new Color(0.10f, 0.065f, 0.04f), new Color(0.25f, 0.17f, 0.10f), 0.5f + n * 0.5f);
            float h = Mathf.Repeat(Mathf.Sin(Mathf.Floor(u * 256f) * 12.9898f + Mathf.Floor(v * 256f) * 78.233f) * 43758.547f, 1f);
            if (h > 0.986f) c = Color.Lerp(c, new Color(0.80f, 0.77f, 0.70f), 0.85f);
            else if (h < 0.03f) c *= 0.5f;
            c.a = 1f; return c;
        }
    }
}
