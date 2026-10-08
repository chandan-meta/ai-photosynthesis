using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Photosynthesis
{
    /// Chamber construction: floor + engraving, investigation platform, planting beds and pots, research stations,
    /// banners, scenery outside the glass, underground cross-section, vines, suspended leaves, light shafts, sun beam, crown
    /// skylight, formation particles and lights. Static decor is runtime static-batched; anything animated stays separate.
    public partial class Chamber
    {
        /// A material whose colour (or emission) is driven by a level: value = c × k (alpha untouched).
        sealed class Glow
        {
            readonly Material m; readonly Color c; readonly bool emission;
            public Glow(Material m, Color c, bool emission) { this.m = m; this.c = c; this.emission = emission; }
            public void Set(float k)
            {
                var v = new Color(c.r * k, c.g * k, c.b * k, c.a);
                if (emission) Fx.SetEmission(m, v); else Fx.SetColor(m, v);
            }
        }
        struct Item { public Transform t; public Vector3 scale; public Quaternion rot; public float delay, sway; }
        struct Piece { public Renderer r; public Transform t; public Vector3 center, off, scale; public float delay, pop; }
        struct Fade { public Renderer r; public Material m; public float min; }
        struct Scan { public Transform t; public float y0, y1; }
        struct Occluder { public Vector2 c; public float rad2, y0, y1; public Renderer[] r; public bool hidden; }

        static readonly Color HoloCyan = new Color(0.62f, 2.18f, 2.37f, 1f);   // HDR, alpha 1 (Color * k scales alpha too)
        static readonly Color GoldGlow = new Color(1f, 0.72f, 0.3f);
        static readonly string[][] BannerWords =
        {
            new[] { "THE HIDDEN", "FOOD", "FACTORY", "AWAITS" }, new[] { "SMALL", "PROCESSES", "BIG IMPACT" },   // never name the clues: banner 0 is readable in the Clue 3 shot
            new[] { "OBSERVE", "LEARN", "EXPLORE" }, new[] { "PLANTS", "POWER", "OUR PLANET" },
        };

        Transform staticRoot, dynRoot;
        Texture2D floorTex, engraveTex, soilTex, leafTex, holoLeafTex, beamTex, shaftTex;
        Material soilMat, goldTrim;
        Glow[][] ringStages; float[] ringK; Glow[] engrave, bannerGlows; Glow[][] stationGlows; float[] stationLevel; Transform[] stationDyn;
        readonly List<Glow> daylit = new List<Glow>();   // scale with daylight but are not lit by it (scenery, metal glints)
        Glow shaftGlow, shaftGlowSoft; ParticleSystem shaftDust;
        Item[] veg; Piece[] pieces; Occluder[] occ;
        Transform[] vines, floaters, spinners; Vector3[] floaterPos; float[] floaterYaw; Scan[] scans;
        readonly List<Renderer> fadeSolid = new List<Renderer>(); readonly List<Fade> fadeGhost = new List<Fade>();
        Material glassFloorMat; Renderer glassFloor; GameObject underground;
        ParticleSystem sparks; Light formLight, fillLight; const float FillBase = 13f;

        // sun beam (runs from the skylight to the target)
        const float BeamLen = 15f, BeamHit = 0.32f, BeamWhoom = 1.2f, FlareRest = 0.4f;   // FlareRest: steady hotspot small enough that the lit leaf and the EnergyWave read around it
        GameObject beamRig; Transform beamShaft, beamFlare, beamHalo, impactRing; Glow[] beamGlows; Glow flareGlow, ringGlow;
        Light beamSpot; ParticleSystem beamDust; bool beamSteady; float ringFx, beamLen = BeamLen;

        // skylight: bronze iris at the dome crown, inside the dome's ceiling-lamp ring (inner radius ≈ 3.47 m, lamp disc at y ≈ 10.94 m,
        // which hides anything above it from the floor)
        const float SkyY = 10.5f, SkyAperture = 1.68f, SkyHousing = 3.38f, SkyOpenTime = 0.9f, SkyRimRest = 1.3f;
        Transform skylight; Transform[] skyBlades; SphereCollider skyCol; Glow skyRim, skyHalo, skyGlow; float skyOpen; bool skyReady;

        GameObject Obj(string name, Mesh mesh, Material m, Transform parent = null, bool shadows = false) =>
            Fx.MeshObj(name, mesh, m, parent ? parent : staticRoot, shadows);

        GameObject Box(Transform parent, string name, Vector3 pos, Vector3 size, Material m, bool shadows = false)
        {
            var g = Fx.Prim(PrimitiveType.Cube, parent, pos, size, m, false, name);
            if (shadows) g.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.On;
            return g;
        }

        void Build()
        {
            var art = Fx.Node("Procedural", transform).transform;
            staticRoot = Fx.Node("Static Decor", art).transform;
            dynRoot = Fx.Node("Animated", art).transform;
            // ponytail: textures are generated at startup (~0.3 s, mostly the 1024² floor). Bake them to assets in
            // ChamberBuilder if load time ever matters.
            ChamberArt.Floor(1024, out floorTex, out engraveTex);
            soilTex = ChamberArt.Soil(); leafTex = ChamberArt.LeafTexture(false); holoLeafTex = ChamberArt.LeafTexture(true);
            beamTex = ChamberArt.Shaft(true); shaftTex = ChamberArt.Shaft(false);
            soilMat = Fx.Mat(MatKind.Lit, Color.white, null, 0.12f, 0f); Fx.SetTexture(soilMat, soilTex);
            goldTrim = Fx.Mat(MatKind.LitEmissive, new Color(0.85f, 0.64f, 0.3f), new Color(0.55f, 0.36f, 0.1f), 0.8f, 1f);

            BuildFloor(); BuildPlatform(); BuildBeds(); BuildPots(); BuildStations(); BuildBanners();
            BuildOutside(); BuildUnderground(); BuildVines(); BuildFloaters(); BuildShafts(); BuildBeam(); BuildSkylight(); BuildFormationFx();
            InitPieces(); InitVegetation(); InitOccluders();
            StaticBatchingUtility.Combine(staticRoot.gameObject);
        }

        // ================================================================== floor
        void BuildFloor()
        {
            var floor = Fx.Mat(MatKind.Lit, Color.white, null, 0.86f, 0f); Fx.SetTexture(floor, floorTex);
            var inner = Obj("Floor Inner", ChamberArt.Arc(PlatformRadius - 0.03f, 6.36f, 0, 360, 0f, 160, ChamberArt.FloorR), floor);
            Obj("Floor Outer", ChamberArt.Arc(6.36f, ChamberArt.FloorR, 0, 360, 0f, 192, ChamberArt.FloorR), floor);
            var ghost = Fx.Mat(MatKind.LitTransparent, Color.white, null, 0.9f, 0f); Fx.SetTexture(ghost, floorTex);
            AddFade(inner.GetComponent<Renderer>(), ghost, 0.3f);

            engrave = new Glow[12];
            for (int i = 0; i < engrave.Length; i++)
            {
                float r0 = 3.5f + i * 0.74f;
                var m = Fx.Mat(MatKind.Additive, GoldGlow); Fx.SetTexture(m, engraveTex);
                Obj("Engraving " + i, ChamberArt.Arc(r0, r0 + 0.74f, 0, 360, 0.004f, 128, ChamberArt.FloorR), m);
                engrave[i] = new Glow(m, GoldGlow * 0.85f, false);
            }
        }

        /// Opaque renderer + transparent twin used while the floor is see-through (no keyword switching at runtime).
        void AddFade(Renderer solid, Material ghost, float min)
        {
            var g = Fx.MeshObj(solid.name + " (glass)", solid.GetComponent<MeshFilter>().sharedMesh, ghost, solid.transform.parent);
            g.transform.SetLocalPositionAndRotation(solid.transform.localPosition, solid.transform.localRotation);
            g.transform.localScale = solid.transform.localScale;
            var r = g.GetComponent<Renderer>(); r.enabled = false;
            fadeSolid.Add(solid); fadeGhost.Add(new Fade { r = r, m = ghost, min = min });
        }

        void ApplyFloor(float t)
        {
            floorT = t; bool ghost = t > 0.001f;
            for (int i = 0; i < fadeSolid.Count; i++) fadeSolid[i].enabled = !ghost;
            for (int i = 0; i < fadeGhost.Count; i++)
            {
                var f = fadeGhost[i]; f.r.enabled = ghost;
                if (ghost) Fx.SetAlpha(f.m, Mathf.Lerp(1f, f.min, t));
            }
            glassFloor.enabled = ghost; if (ghost) Fx.SetAlpha(glassFloorMat, 0.16f * t);
            if (underground.activeSelf != ghost) underground.SetActive(ghost);
        }

        // ================================================================== platform
        void BuildPlatform()
        {
            const float h0 = 0.14f, h1 = PlatformHeight - 0.02f, rT = 2.95f, rS = 2.62f;
            Color darkC = new Color(0.06f, 0.075f, 0.07f), sideC = new Color(0.80f, 0.77f, 0.70f);
            var darkM = Fx.Mat(MatKind.Lit, darkC, null, 0.88f, 0.15f);
            var stone = Fx.Mat(MatKind.Lit, Color.white, null, 0.72f, 0f); Fx.SetTexture(stone, floorTex);
            var side = Fx.Mat(MatKind.Lit, sideC, null, 0.6f, 0f);
            var root = Fx.Node("Investigation Platform", staticRoot).transform;

            void Fadeable(GameObject g, Color ghostC, Texture tex, float min)
            {
                var gm = Fx.Mat(MatKind.LitTransparent, ghostC, null, 0.9f, 0f); if (tex) Fx.SetTexture(gm, tex);
                AddFade(g.GetComponent<Renderer>(), gm, min);
            }
            Fadeable(Obj("Plinth Side", ChamberArt.Wall(PlatformRadius, PlatformRadius, 0, 360, 0f, h0, 128), darkM, root, true), darkC, null, 0.25f);
            Fadeable(Obj("Plinth Top", ChamberArt.Arc(rT, PlatformRadius, 0, 360, h0, 128), darkM, root), darkC, null, 0.25f);
            Fadeable(Obj("Tier Side", ChamberArt.Wall(rT, rT, 0, 360, h0, h1, 128), side, root, true), sideC, null, 0.15f);
            Fadeable(Obj("Tier Top", ChamberArt.Arc(rS, rT, 0, 360, h1, 128, ChamberArt.FloorR), stone, root), Color.white, floorTex, 0.18f);
            Fadeable(Obj("Soil Rim", ChamberArt.Wall(rS, rS, 0, 360, h1 - 0.05f, h1, 96, false), darkM, root), darkC, null, 0f);
            Fadeable(Obj("Soil Bed", ChamberArt.Arc(0f, rS, 0, 360, h1 - 0.03f, 96, 0.5f), soilMat, root), Color.white, soilTex, 0f);
            // gentle mound so the plant base (anchor at PlatformHeight) sits in soil
            Fadeable(Fx.Prim(PrimitiveType.Sphere, root, new Vector3(0f, h1 - 0.03f, 0f), new Vector3(2.6f, 0.13f, 2.6f), soilMat, false, "Soil Mound"), Color.white, soilTex, 0f);
            Obj("Gold Trim", ChamberArt.Wall(PlatformRadius + 0.006f, PlatformRadius + 0.006f, 0, 360, h0 - 0.035f, h0 + 0.004f, 128), goldTrim, root);

            glassFloorMat = Fx.Mat(MatKind.Glass, new Color(0.75f, 0.95f, 0.92f, 0f));
            glassFloor = Obj("Glass Floor", ChamberArt.Arc(0f, rT, 0, 360, h1 + 0.003f, 96), glassFloorMat, root).GetComponent<Renderer>();
            glassFloor.enabled = false;

            // glowing rings: platform cyan → floor gold → outer cyan line; additive halos fake the light pooling on stone
            var halo = ChamberArt.Halo();
            Glow Ring(string n, float r0, float r1, float y, Color baseC, Color emis)
            {
                var m = Fx.Mat(MatKind.LitEmissive, baseC, Color.black, 0.8f, 0.3f);
                Obj(n, ChamberArt.Arc(r0, r1, 0, 360, y, 160), m, root);
                return new Glow(m, emis, true);
            }
            Glow Halo(string n, float r0, float r1, float y, Color c)
            {
                var m = Fx.Mat(MatKind.Additive, c); Fx.SetTexture(m, halo);
                Obj(n, ChamberArt.Arc(r0, r1, 0, 360, y, 160), m, root);
                return new Glow(m, c, false);
            }
            Color cyan = Palette.Cyan, gold = Palette.Gold;
            ringStages = new[]
            {
                // a dim teal inlay and a warm brass accent, not a neon teleporter pad (the ignition in EvaluateForm still flashes to ~3x)
                new[] { Ring("Cyan Ring", rS + 0.012f, rS + 0.085f, h1 + 0.004f, cyan * 0.3f, cyan * 1.5f), Halo("Cyan Halo", rS - 0.3f, rS + 0.36f, h1 + 0.005f, new Color(cyan.r, cyan.g, cyan.b, 0.2f)) },
                new[] { Ring("Gold Ring", 3.40f, 3.50f, 0.003f, gold * 0.4f, gold * 2.0f), Halo("Gold Halo", 3.25f, 3.78f, 0.0045f, new Color(gold.r, gold.g, gold.b, 0.28f)),
                        new Glow(goldTrim, new Color(0.55f, 0.36f, 0.1f) * 1.6f, true) },
                new[] { Ring("Outer Cyan Line", 6.345f, 6.378f, 0.004f, cyan * 0.3f, cyan * 1.3f), Halo("Outer Cyan Halo", 6.2f, 6.52f, 0.005f, new Color(cyan.r, cyan.g, cyan.b, 0.1f)) },
            };
            ringK = new float[ringStages.Length];
        }

        // ================================================================== planting beds + pots
        void BuildBeds()
        {
            var curb = Fx.Mat(MatKind.Lit, new Color(0.78f, 0.75f, 0.69f), null, 0.5f, 0f);
            Bed("Platform Bed", EdgeBedIn, EdgeBedOut, Entrance + EdgeBedGap, Entrance + 360f - EdgeBedGap, 0.22f, EdgeBedSoil, curb);
            Bed("Perimeter Bed", PerimeterIn, PerimeterOut, Entrance + PerimeterGap, Entrance + 360f - PerimeterGap, 0.2f, PerimeterSoil, curb);
        }

        void Bed(string n, float r0, float r1, float a0, float a1, float h, float soilY, Material curb)
        {
            const float lip = 0.07f; int segs = Mathf.CeilToInt((a1 - a0) / 2f);
            var root = Fx.Node(n, staticRoot).transform;
            Obj("Inner Wall", ChamberArt.Wall(r0, r0, a0, a1, 0f, h, segs, false), curb, root, true);
            Obj("Outer Wall", ChamberArt.Wall(r1, r1, a0, a1, 0f, h, segs, true), curb, root);
            Obj("Lip In", ChamberArt.Arc(r0, r0 + lip, a0, a1, h, segs), curb, root);
            Obj("Lip Out", ChamberArt.Arc(r1 - lip, r1, a0, a1, h, segs), curb, root);
            Obj("Cap A", ChamberArt.EndCap(r0, r1, a0, 0f, h, false), curb, root);
            Obj("Cap B", ChamberArt.EndCap(r0, r1, a1, 0f, h, true), curb, root);
            Obj("Soil", ChamberArt.Arc(r0 + lip, r1 - lip, a0, a1, soilY, segs, 0.5f), soilMat, root);
            Obj("Gold Line", ChamberArt.Wall(r0 - 0.004f, r0 - 0.004f, a0, a1, h - 0.055f, h - 0.035f, segs, false), goldTrim, root);
        }

        void BuildPots()
        {
            var stone = Fx.Mat(MatKind.Lit, new Color(0.74f, 0.68f, 0.58f), null, 0.4f, 0f);
            var dark = Fx.Mat(MatKind.Lit, new Color(0.07f, 0.08f, 0.08f), null, 0.7f, 0.2f);
            foreach (var p in Pots)
            {
                var root = Fx.Node("Planter", staticRoot, Polar(p.x, p.y)).transform;
                Obj("Plinth", ChamberArt.Wall(0.64f, 0.64f, 0, 360, 0f, 0.05f, 48), dark, root);
                Obj("Wall", ChamberArt.Wall(0.66f, PotRadius, 0, 360, 0.05f, 0.55f, 48), stone, root, true);
                Obj("Lip", ChamberArt.Arc(0.7f, PotRadius, 0, 360, 0.55f, 48), stone, root);
                Obj("Inner", ChamberArt.Wall(0.7f, 0.7f, 0, 360, PotSoil - 0.03f, 0.55f, 48, false), dark, root);
                Obj("Soil", ChamberArt.Arc(0f, 0.7f, 0, 360, PotSoil, 48, 0.5f), soilMat, root);
                Obj("Gold Band", ChamberArt.Wall(0.774f, 0.779f, 0, 360, 0.43f, 0.46f, 48), goldTrim, root);
            }
        }

        // ================================================================== research stations
        void BuildStations()
        {
            var body = Fx.Mat(MatKind.Lit, new Color(0.86f, 0.85f, 0.81f), null, 0.55f, 0f);
            var dark = Fx.Mat(MatKind.Lit, new Color(0.05f, 0.06f, 0.06f), null, 0.6f, 0.5f);
            var wood = Fx.Mat(MatKind.Lit, Color.white, null, 0.5f, 0f); Fx.SetTexture(wood, ChamberArt.Wood());
            var screen = Fx.Mat(MatKind.Lit, new Color(0.01f, 0.025f, 0.03f), null, 0.95f, 0.2f);
            var glass = Fx.Mat(MatKind.Glass, new Color(0.85f, 0.95f, 0.95f, 0.22f));
            var holo = new[] { ChamberArt.Hologram(ChamberArt.Holo.Leaf), ChamberArt.Hologram(ChamberArt.Holo.Cell), ChamberArt.Hologram(ChamberArt.Holo.Graph) };
            var leafMesh = ChamberArt.Leaf(0.72f, 0.42f, 0.1f, 0.1f, 10, 3);
            int n = Stations.Length;
            stationGlows = new Glow[n][]; stationLevel = new float[n]; stationDyn = new Transform[n];
            var spin = new List<Transform>(); var scanList = new List<Scan>();
            Color cyan = Palette.Cyan;

            for (int i = 0; i < n; i++)
            {
                Vector3 pos = Polar(Stations[i].x, Stations[i].y), look = Polar(Entrance, 9f) - pos; look.y = 0f;   // face the entrance / establishing camera
                var rot = Quaternion.LookRotation(look);
                var root = Fx.Node("Research Station " + (i + 1), staticRoot, pos).transform; root.rotation = rot;
                var dyn = Fx.Node("Research Station " + (i + 1) + " Holo", dynRoot, pos).transform; dyn.rotation = rot; stationDyn[i] = dyn;
                var glows = new List<Glow>();

                Box(root, "Body", new Vector3(0f, 0.47f, 0f), new Vector3(2.0f, 0.78f, 0.72f), body, true);
                Box(root, "Kick", new Vector3(0f, 0.04f, -0.03f), new Vector3(1.9f, 0.08f, 0.6f), dark);
                Box(root, "Top", new Vector3(0f, 0.89f, 0.02f), new Vector3(2.12f, 0.06f, 0.82f), wood, true);
                var led = Fx.Mat(MatKind.LitEmissive, cyan * 0.3f, Color.black);
                Box(root, "LED Strip", new Vector3(0f, 0.845f, 0.372f), new Vector3(2.0f, 0.016f, 0.012f), led);
                glows.Add(new Glow(led, cyan * 3f, true));
                var projM = Fx.Mat(MatKind.LitEmissive, cyan * 0.4f, Color.black);
                glows.Add(new Glow(projM, cyan * 4f, true));

                // tilted touch screen with dashboard UI
                var sRot = Quaternion.Euler(22f, 0f, 0f); var sPos = new Vector3(0.4f, 1.04f, 0.12f);
                Box(root, "Screen", sPos, new Vector3(0.95f, 0.035f, 0.6f), screen).transform.localRotation = sRot;
                glows.Add(Holo(root, "Screen UI", sPos + sRot * new Vector3(0f, 0.02f, 0f), sRot * Quaternion.LookRotation(Vector3.down, Vector3.back), new Vector2(0.86f, 0.54f), holo[(i + 2) % 3], 0.8f));

                bool panel = i < 2;
                float px = panel ? -0.3f : -0.5f, py = panel ? 1.78f : 1.62f, pz = -0.22f;
                Fx.Prim(PrimitiveType.Cylinder, root, new Vector3(px, 0.93f, pz), new Vector3(0.18f, 0.012f, 0.18f), projM, false, "Projector");
                var cone = Fx.Mat(MatKind.Additive, HoloCyan * 0.3f); Fx.SetTexture(cone, beamTex);
                float top = panel ? py - 0.41f : py - 0.02f;
                var coneGo = Obj("Projection", ChamberArt.Crossed(panel ? 1.05f : 0.5f, 0.08f, top - 0.94f), cone, root);
                coneGo.transform.SetLocalPositionAndRotation(new Vector3(px, top, pz), Quaternion.Euler(90f, 0f, 0f));
                glows.Add(new Glow(cone, HoloCyan * 0.3f, false));
                if (panel)
                {   // floating holographic panel + scan line
                    glows.Add(Holo(root, "Holo Panel", new Vector3(px, py, pz), Quaternion.Euler(0f, 180f, 0f), new Vector2(1.25f, 0.82f), holo[i], 1f));
                    var scanM = Fx.Mat(MatKind.Additive, HoloCyan * 1.4f);
                    var scan = Fx.Prim(PrimitiveType.Quad, dyn, new Vector3(px, py, pz + 0.01f), new Vector3(1.2f, 0.012f, 1f), scanM, false, "Scan Line");
                    scanList.Add(new Scan { t = scan.transform, y0 = py - 0.38f, y1 = py + 0.38f });
                    glows.Add(new Glow(scanM, HoloCyan * 1.4f, false));
                }
                else
                {   // spinning holographic leaf + side panel + specimen beakers
                    var holoLeaf = Fx.Mat(MatKind.Additive, HoloCyan); Fx.SetTexture(holoLeaf, holoLeafTex);
                    var sp = Fx.Node("Holo Leaf", dyn, new Vector3(px, py - 0.02f, pz)).transform;
                    Fx.MeshObj("Blade", leafMesh, holoLeaf, sp).transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                    spin.Add(sp); glows.Add(new Glow(holoLeaf, HoloCyan, false));
                    glows.Add(Holo(root, "Side Panel", new Vector3(0.45f, 1.66f, -0.28f), Quaternion.Euler(0f, 180f, 0f), new Vector2(0.82f, 0.56f), holo[(i + 1) % 3], 0.9f));
                    var liquid = Fx.Mat(MatKind.LitEmissive, new Color(0.3f, 0.8f, 0.35f), Color.black);
                    for (int b = 0; b < 2; b++)
                    {
                        var bp = new Vector3(0.78f + b * 0.15f, 0.92f, 0.05f + b * 0.14f);
                        Fx.Prim(PrimitiveType.Cylinder, root, bp + Vector3.up * 0.06f, new Vector3(0.065f, 0.055f, 0.065f), liquid, false, "Specimen");
                        Fx.Prim(PrimitiveType.Cylinder, root, bp + Vector3.up * 0.1f, new Vector3(0.08f, 0.1f, 0.08f), glass, false, "Beaker");
                    }
                    glows.Add(new Glow(liquid, new Color(0.3f, 0.95f, 0.4f) * 1.6f, true));
                }
                stationGlows[i] = glows.ToArray();
            }
            spinners = spin.ToArray(); scans = scanList.ToArray();
        }

        Glow Holo(Transform parent, string n, Vector3 pos, Quaternion rot, Vector2 size, Texture tex, float k)
        {
            var m = Fx.Mat(MatKind.Additive, HoloCyan * k); Fx.SetTexture(m, tex);
            var q = Fx.Prim(PrimitiveType.Quad, parent, pos, new Vector3(size.x, size.y, 1f), m, false, n);
            q.transform.localRotation = rot;
            return new Glow(m, HoloCyan * k, false);
        }

        // ================================================================== banners
        void BuildBanners()
        {
            var frame = Fx.Mat(MatKind.LitEmissive, new Color(0.8f, 0.62f, 0.3f), new Color(0.3f, 0.19f, 0.05f), 0.75f, 1f);
            daylit.Add(new Glow(frame, new Color(0.3f, 0.19f, 0.05f), true));   // no gold frames glowing in the pre-formation dark
            var foot = Fx.Mat(MatKind.Lit, new Color(0.08f, 0.09f, 0.09f), null, 0.7f, 0.3f);
            var panel = Fx.Mat(MatKind.LitTransparent, new Color(0.02f, 0.09f, 0.075f, 0.82f), null, 0.92f, 0f);
            Color glow = new Color(0.38f, 1f, 0.72f) * 1.5f;
            bannerGlows = new Glow[Banners.Length];
            for (int i = 0; i < Banners.Length; i++)
            {
                Vector3 pos = Polar(Banners[i], BannerRadius);
                var root = Fx.Node("Banner " + (i + 1), staticRoot, pos).transform; root.rotation = Quaternion.LookRotation(-pos);
                Box(root, "Footing", new Vector3(0f, 0.15f, 0f), new Vector3(1.9f, 0.3f, 0.45f), foot, true);
                Box(root, "Panel", new Vector3(0f, 2.75f, 0f), new Vector3(1.6f, 4.6f, 0.04f), panel);
                Box(root, "Post L", new Vector3(-0.83f, 2.65f, 0f), new Vector3(0.06f, 4.9f, 0.08f), frame, true);
                Box(root, "Post R", new Vector3(0.83f, 2.65f, 0f), new Vector3(0.06f, 4.9f, 0.08f), frame, true);
                Box(root, "Rail Top", new Vector3(0f, 5.08f, 0f), new Vector3(1.72f, 0.06f, 0.08f), frame);
                Box(root, "Rail Bottom", new Vector3(0f, 0.42f, 0f), new Vector3(1.72f, 0.06f, 0.08f), frame);
                var art = Fx.Mat(MatKind.Additive, glow); Fx.SetTexture(art, ChamberArt.Banner(BannerWords[i % BannerWords.Length]));
                Fx.Prim(PrimitiveType.Quad, root, new Vector3(0f, 2.75f, 0.03f), new Vector3(1.56f, 4.5f, 1f), art, false, "Banner Art").transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                bannerGlows[i] = new Glow(art, glow, false);
            }
        }

        // ================================================================== outside the glass
        void BuildOutside()
        {
            Obj("Outside Ground", ChamberArt.Arc(13.2f, 420f, 0, 360, -0.03f, 96), Fx.Mat(MatKind.Lit, new Color(0.19f, 0.27f, 0.13f), null, 0.08f, 0f));
            Hills("Mountains", 210f, -2f, 75f, 3, 0.3f, 0.55f, 0f, 0, new Color(0.62f, 0.72f, 0.78f), new Color(0.46f, 0.58f, 0.66f), 2900);
            Hills("Hills", 95f, -1f, 30f, 5, 0.22f, 0.45f, 0.04f, 160, new Color(0.42f, 0.55f, 0.5f), new Color(0.24f, 0.37f, 0.29f), 2910);
            Hills("Forest", 36f, -0.5f, 12f, 9, 0.38f, 0.22f, 0.22f, 46, new Color(0.13f, 0.22f, 0.13f), new Color(0.07f, 0.15f, 0.07f), 2920);
        }

        void Hills(string n, float r, float y0, float h, int seed, float baseH, float amp, float trees, int crowns, Color low, Color high, int queue)
        {
            var m = Fx.Mat(MatKind.UnlitTransparent, Color.white); Fx.SetTexture(m, ChamberArt.Hills(seed, baseH, amp, trees, crowns, low, high));
            m.renderQueue = queue;   // far → near, before the dome glass
            Obj(n, ChamberArt.Wall(r, r, 0, 360, y0, y0 + h, 128, false), m);
            daylit.Add(new Glow(m, Color.white, false));
        }

        // ================================================================== underground (visible through the transparent floor)
        void BuildUnderground()
        {
            underground = Fx.Node("Underground", transform);
            var u = underground.transform;
            // near (outward) face stays thin so the roots read through it; the far (inward) face gives the soil context
            var strataTex = ChamberArt.Strata();
            var strataNear = Fx.Mat(MatKind.LitTransparent, new Color(1f, 1f, 1f, 0.38f), null, 0.2f, 0f); Fx.SetTexture(strataNear, strataTex);
            var strataFar = Fx.Mat(MatKind.LitTransparent, Color.white, null, 0.2f, 0f); Fx.SetTexture(strataFar, strataTex);
            Obj("Soil Section", ChamberArt.Wall(3.05f, 3.05f, 0, 360, -2.8f, -0.01f, 96, true), strataNear, u);
            Obj("Soil Section Back", ChamberArt.Wall(3.04f, 3.04f, 0, 360, -2.8f, -0.01f, 96, false), strataFar, u);
            Obj("Section Floor", ChamberArt.Arc(0f, 3.05f, 0, 360, -2.79f, 64, 0.5f), soilMat, u);
            var earth = Fx.Mat(MatKind.Unlit, new Color(0.045f, 0.032f, 0.022f));
            Obj("Earth Wall", ChamberArt.Wall(12.9f, 12.9f, 0, 360, -4.5f, -0.02f, 96, false), earth, u);
            Obj("Earth Floor", ChamberArt.Arc(0f, 12.9f, 0, 360, -4.5f, 96), earth, u);
            var pebble = Fx.Mat(MatKind.Lit, new Color(0.42f, 0.38f, 0.33f), null, 0.3f, 0f);
            var rnd = new System.Random(5);
            for (int i = 0; i < 14; i++)
            {
                float a = (float)rnd.NextDouble() * 360f, r = 0.8f + (float)rnd.NextDouble() * 2.1f, s = 0.08f + (float)rnd.NextDouble() * 0.16f;
                var g = Fx.Prim(PrimitiveType.Sphere, u, Polar(a, r, -0.3f - (float)rnd.NextDouble() * 2.3f), new Vector3(s * 1.4f, s * 0.8f, s), pebble, false, "Pebble");
                g.transform.localRotation = Quaternion.Euler(0f, a, 15f);
            }
            underground.SetActive(false);
        }

        // ================================================================== vines + suspended leaves
        const float DomeInner = 12.55f;
        static float DomeHeightAt(float r) => Mathf.Sqrt(Mathf.Max(0f, DomeInner * DomeInner - r * r));

        void BuildVines()
        {
            var stemM = Fx.Mat(MatKind.Leaf, new Color(0.2f, 0.25f, 0.1f));
            var leafA = Fx.Mat(MatKind.Leaf, new Color(0.85f, 1f, 0.8f)); Fx.SetTexture(leafA, leafTex);
            var leafB = Fx.Mat(MatKind.Leaf, new Color(0.62f, 0.8f, 0.5f)); Fx.SetTexture(leafB, leafTex);
            var unitLeaf = ChamberArt.Leaf(1f, 0.55f, 0.12f, 0.15f, 4, 1);
            float[] clusters = { -45f, 0f, 30f, 60f, 120f, 150f, 180f, 225f };
            var rnd = new System.Random(11);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            vines = new Transform[clusters.Length];
            var stems = new List<CombineInstance>(); var la = new List<CombineInstance>(); var lb = new List<CombineInstance>();
            var ctrl = new List<Vector3>();
            for (int c = 0; c < clusters.Length; c++)
            {
                Vector3 pivot = Polar(clusters[c], 9.6f, DomeHeightAt(9.6f) - 0.3f);
                int count = 3 + rnd.Next(2);
                for (int v = 0; v < count; v++)
                {
                    float h = R(6.8f, 10.2f), a = clusters[c] + R(-9f, 9f);
                    Vector3 top = Polar(a, DomeHeightAt(h) - 0.35f, h) - pivot;   // anchor just inside the ribs
                    float len = R(2.4f, Mathf.Min(5.2f, h - 2.6f)), ph = R(0f, 6.28f);
                    ctrl.Clear();
                    for (int k = 0; k <= 5; k++)
                    {
                        float f = k / 5f;
                        ctrl.Add(top + new Vector3(Mathf.Sin(ph + k * 1.3f) * 0.14f * f, -len * f, Mathf.Cos(ph + k * 0.9f) * 0.12f * f));
                    }
                    var pts = Fx.Smooth(ctrl, 20);
                    stems.Add(new CombineInstance { mesh = Fx.Tube(pts, 0.016f, 0.007f, 5), transform = Matrix4x4.identity });
                    int leaves = Mathf.RoundToInt(len / 0.17f);
                    for (int l = 1; l < leaves; l++)
                    {
                        float f = l / (float)leaves; Vector3 p = Fx.Along(pts, f);
                        float side = (l % 2 == 0 ? 1f : -1f) * R(0.6f, 1.2f);
                        Vector3 outDir = Quaternion.Euler(0f, a + side * 70f, 0f) * Vector3.right;
                        var rot = Quaternion.LookRotation(outDir * 0.8f + Vector3.down * R(0.3f, 0.8f), Vector3.up) * Quaternion.Euler(0f, 0f, R(-25f, 25f));
                        float s = R(0.15f, 0.26f) * Mathf.Lerp(1f, 0.7f, f);
                        (l % 3 == 0 ? lb : la).Add(new CombineInstance { mesh = unitLeaf, transform = Matrix4x4.TRS(p, rot, Vector3.one * s) });
                    }
                }
                // one renderer per cluster: stems | leaves A | leaves B
                var stemMesh = ChamberArt.Combine("Vine Stems", stems);
                var aMesh = new Mesh { indexFormat = IndexFormat.UInt32 }; aMesh.CombineMeshes(la.ToArray(), true, true); la.Clear();
                var bMesh = new Mesh { indexFormat = IndexFormat.UInt32 }; bMesh.CombineMeshes(lb.ToArray(), true, true); lb.Clear();
                var parts = new List<CombineInstance>
                {
                    new CombineInstance { mesh = stemMesh, transform = Matrix4x4.identity },
                    new CombineInstance { mesh = aMesh, transform = Matrix4x4.identity },
                    new CombineInstance { mesh = bMesh, transform = Matrix4x4.identity },
                };
                var mesh = ChamberArt.Combine("Vine Cluster", parts, false);
                var node = Fx.Node("Vines " + (c + 1), dynRoot, pivot).transform;
                var go = Fx.MeshObj("Vines", mesh, stemM, node, true);
                go.GetComponent<Renderer>().sharedMaterials = new[] { stemM, leafA, leafB };
                vines[c] = node;
            }
            Destroy(unitLeaf);
        }

        void BuildFloaters()
        {
            var mat = Fx.Mat(MatKind.Leaf, Color.white, new Color(0.015f, 0.04f, 0.01f)); Fx.SetTexture(mat, leafTex);
            var cable = Fx.Mat(MatKind.Lit, new Color(0.15f, 0.15f, 0.15f), null, 0.6f, 0.8f);
            Vector4[] spots = { new(-25f, 7.6f, 6.3f, 1.7f), new(35f, 8.2f, 7.3f, 2.1f), new(75f, 7.2f, 6.8f, 1.5f), new(125f, 8.0f, 7.6f, 1.9f), new(165f, 7.4f, 6.2f, 1.6f), new(-155f, 8.4f, 7.1f, 2.0f) };
            floaters = new Transform[spots.Length]; floaterPos = new Vector3[spots.Length]; floaterYaw = new float[spots.Length];
            for (int i = 0; i < spots.Length; i++)
            {
                var s = spots[i]; Vector3 pos = Polar(s.x, s.y, s.z);
                var node = Fx.Node("Suspended Leaf " + (i + 1), dynRoot, pos).transform;
                Fx.MeshObj("Blade", ChamberArt.Leaf(s.w, s.w * 0.56f, 0.2f, 0.18f, 12, 3), mat, node, true).transform.localRotation = Quaternion.Euler(68f, 0f, 0f);
                float len = DomeHeightAt(s.y) - s.z;
                Fx.Prim(PrimitiveType.Cylinder, node, Vector3.up * (len * 0.5f), new Vector3(0.012f, len * 0.5f, 0.012f), cable, false, "Cable");
                Fx.Prim(PrimitiveType.Sphere, node, Vector3.zero, Vector3.one * 0.06f, goldTrim, false, "Clip");
                floaters[i] = node; floaterPos[i] = node.localPosition; floaterYaw[i] = s.x + 90f;
            }
        }

        // ================================================================== light shafts + dust
        // ponytail: fake volumetrics (crossed additive quads) aimed along the sun direction captured at startup; a URP
        // volumetric-light renderer feature is the upgrade if the sun ever moves.
        void BuildShafts()
        {
            Vector3 toSun = -(sunRot * Vector3.forward); if (toSun.y < 0.25f) toSun = (toSun + Vector3.up).normalized;
            Vector4[] hits = { new(2.6f, 4.4f, 1.3f, 0), new(-3.8f, 1.4f, 0.9f, 1), new(-1.4f, 7.4f, 1.6f, 0), new(5.6f, -0.8f, 1.0f, 1),
                               new(-6.6f, -2.4f, 1.4f, 0), new(1.2f, 9.6f, 1.1f, 1), new(-8.0f, 5.4f, 1.5f, 1), new(7.6f, 4.2f, 0.9f, 0) };
            var strong = new List<CombineInstance>(); var soft = new List<CombineInstance>();
            const float R = 12.4f;
            foreach (var h in hits)
            {
                Vector3 p = new Vector3(h.x, 0.02f, h.y);
                float b = Vector3.Dot(p, toSun), len = -b + Mathf.Sqrt(b * b - (p.sqrMagnitude - R * R));   // to the glass
                (h.w == 0 ? strong : soft).Add(new CombineInstance { mesh = ChamberArt.Crossed(h.z, h.z * 1.25f, len), transform = Matrix4x4.TRS(p, Quaternion.LookRotation(toSun), Vector3.one) });
            }
            Color warm = new Color(1f, 0.85f, 0.6f);
            var mS = Fx.Mat(MatKind.Additive, new Color(warm.r, warm.g, warm.b, 0.15f)); Fx.SetTexture(mS, shaftTex);
            var mW = Fx.Mat(MatKind.Additive, new Color(warm.r, warm.g, warm.b, 0.085f)); Fx.SetTexture(mW, shaftTex);
            var strongMesh = ChamberArt.Combine("Sun Shafts", strong);
            Obj("Sun Shafts", strongMesh, mS); Obj("Sun Shafts Soft", ChamberArt.Combine("Sun Shafts Soft", soft), mW);
            shaftGlow = new Glow(mS, new Color(warm.r, warm.g, warm.b, 0.09f), false);   // softer: shafts must never wash out the plant
            shaftGlowSoft = new Glow(mW, new Color(warm.r, warm.g, warm.b, 0.05f), false);

            shaftDust = Fx.Particles("Shaft Dust", transform, new Fx.PCfg { color = new Color(1f, 0.93f, 0.78f, 0.75f), size = 0.035f, sizeVar = 0.6f, life = 7f, speed = 0.02f, rate = 16f, max = 280, noise = 0.05f, shape = ParticleSystemShapeType.Mesh });
            var sh = shaftDust.shape; sh.shapeType = ParticleSystemShapeType.Mesh; sh.mesh = strongMesh; sh.meshShapeType = ParticleSystemMeshShapeType.Triangle;
        }

        /// Everything that scales with daylight but is not lit by it (additive shafts, unlit scenery, fill light).
        void ApplyDaylightFx(float day)
        {
            float shafts = day * day;
            shaftGlow.Set(shafts); shaftGlowSoft.Set(shafts);
            bool dust = shafts > 0.25f;
            if (dust != shaftDust.isEmitting) { if (dust) shaftDust.Play(); else shaftDust.Stop(true, ParticleSystemStopBehavior.StopEmitting); }
            for (int i = 0; i < daylit.Count; i++) daylit[i].Set(0.03f + 0.97f * day);
            fillLight.intensity = FillBase * day; fillLight.enabled = day > 0.01f;
        }

        // ================================================================== sun beam
        void BuildBeam()
        {
            beamRig = Fx.Node("Sun Beam", transform);
            var rig = beamRig.transform;
            beamShaft = Fx.Node("Shaft", rig, new Vector3(0f, 0f, BeamLen)).transform;
            beamShaft.localRotation = Quaternion.Euler(180f, 0f, 0f);   // geometry runs from the sky (z 0) down to the target
            var tex = ChamberArt.Shaft(true, 0.25f);   // short fade-in: the shaft reads as coming out of the open skylight
            Glow Layer(string n, float wSky, float wTarget, Color c)
            {
                var m = Fx.Mat(MatKind.Additive, c); Fx.SetTexture(m, tex);
                Fx.MeshObj(n, ChamberArt.Crossed(wSky, wTarget, BeamLen), m, beamShaft);
                return new Glow(m, c, false);
            }
            beamGlows = new[]
            {
                Layer("Core", 0.8f, 0.45f, new Color(1.5f, 1.2f, 0.75f, 0.55f)),   // warm gold, not white-hot (peak ≈0.8): the EnergyWave (2.2 HDR) reads over it
                Layer("Glow", 2.6f, 1.4f, new Color(1.5f, 1.1f, 0.62f, 0.3f)),
                Layer("Haze", 5f, 2.6f, new Color(1f, 0.78f, 0.48f, 0.2f)),       // ≈1.45 m visible column: light pouring out of the 3.4 m oculus, not a pencil
            };
            var spotGo = Fx.Node("Beam Light", rig, new Vector3(0f, 0f, 5.5f));
            spotGo.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);
            beamSpot = spotGo.AddComponent<Light>();
            beamSpot.type = LightType.Spot; beamSpot.spotAngle = 22f; beamSpot.innerSpotAngle = 8f; beamSpot.range = 9f;
            beamSpot.color = new Color(1f, 0.9f, 0.7f); beamSpot.shadows = LightShadows.Soft; beamSpot.intensity = 0f;

            var flareM = Fx.Mat(MatKind.Additive, new Color(3f, 2.2f, 1.05f, 1f)); Fx.SetTexture(flareM, Fx.SoftDot);
            var haloM = Fx.Mat(MatKind.Additive, new Color(1.4f, 1f, 0.45f, 0.35f)); Fx.SetTexture(haloM, Fx.SoftDot);
            beamFlare = Fx.Prim(PrimitiveType.Quad, rig, Vector3.zero, Vector3.one, flareM, false, "Flare").transform;
            beamHalo = Fx.Prim(PrimitiveType.Quad, rig, Vector3.zero, Vector3.one, haloM, false, "Flare Halo").transform;
            flareGlow = new Glow(flareM, new Color(3f, 2.2f, 1.05f, 1f), false);
            var ringM = Fx.Mat(MatKind.Additive, new Color(2.2f, 1.7f, 0.8f, 1f)); Fx.SetTexture(ringM, RingSprite());
            impactRing = Fx.Prim(PrimitiveType.Quad, rig, Vector3.zero, Vector3.zero, ringM, false, "Impact Ring").transform;
            ringGlow = new Glow(ringM, new Color(2.2f, 1.7f, 0.8f, 1f), false);

            beamDust = Fx.Particles("Beam Dust", rig, new Fx.PCfg { color = new Color(1f, 0.92f, 0.7f, 0.9f), size = 0.035f, life = 3.2f, speed = 0.05f, rate = 28f, max = 200, noise = 0.15f, shape = ParticleSystemShapeType.Box });
            beamDust.transform.localPosition = new Vector3(0f, 0f, BeamLen * 0.45f);
            var sh = beamDust.shape; sh.scale = new Vector3(1.0f, 1.0f, BeamLen * 0.9f);   // motes fill the wider shaft (AimBeam matches)
            var bm = beamDust.main; bm.playOnAwake = false;
            beamDust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            beamRig.SetActive(false);
        }

        static Texture2D ringSprite;
        static Texture2D RingSprite()
        {
            if (ringSprite) return ringSprite;
            var c = new ChamberArt.Canvas(128, 128);
            c.Ellipse(new Vector2(64, 64), 54, 54, 7f, 1f); c.Ellipse(new Vector2(64, 64), 54, 54, 16f, 0.35f);
            return ringSprite = c.ToAlpha("Impact Ring");
        }

        void AimBeam(Vector3 target)
        {
            Vector3 to = skylight.position - target; beamLen = Mathf.Max(1f, to.magnitude);
            beamRig.transform.SetPositionAndRotation(target, Quaternion.LookRotation(to));
            beamShaft.localPosition = new Vector3(0f, 0f, beamLen);
            beamDust.transform.localPosition = new Vector3(0f, 0f, beamLen * 0.45f);
            var sh = beamDust.shape; sh.scale = new Vector3(1.0f, 1.0f, beamLen * 0.9f);
            if (!beamRig.activeSelf) beamRig.SetActive(true);
        }

        /// t 0 → BeamHit: the shaft shoots down from the sky; impact; swell + flare settle by BeamWhoom.
        void PoseBeam(float t)
        {
            float grow = Fx.Eval(Mathf.Clamp01(t / BeamHit), Ease.In), hit = Mathf.Clamp01((t - BeamHit) / (BeamWhoom - BeamHit));
            bool after = t >= BeamHit;
            float swell = after ? 1f + 0.6f * Mathf.Sin(Mathf.PI * Mathf.Min(1f, hit * 1.6f)) * (1f - hit) : 0.45f;
            beamShaft.localScale = new Vector3(swell, swell, Mathf.Max(0.001f, grow) * beamLen / BeamLen);
            float k = after ? 1f + 1.6f * (1f - hit) * (1f - hit) : 0.6f + 0.6f * grow;
            foreach (var g in beamGlows) g.Set(k);
            beamSpot.intensity = after ? Mathf.Lerp(220f, 80f, hit) : 0f;
            float flare = after ? FlareRest + (2.4f - FlareRest) * (1f - hit) * (1f - hit) : 0f;
            beamFlare.localScale = Vector3.one * (0.9f * flare); beamHalo.localScale = Vector3.one * (3.2f * flare);
            flareGlow.Set(Mathf.Max(1f, flare));
            beamSteady = t >= BeamWhoom;
        }

        void BeamImpact()
        {
            Vector3 p = beamRig.transform.position;
            Fx.Burst(p, Palette.Gold, 70, 0.07f, 3.2f, 1.3f);
            ringFx = 1f; PoseImpactRing();
            if (!beamDust.isPlaying) beamDust.Play();
        }

        void PoseImpactRing()
        {
            float u = 1f - Mathf.Clamp01(ringFx);
            impactRing.localScale = ringFx > 0f ? Vector3.one * Mathf.Lerp(0.3f, 4.2f, Fx.Eval(u, Ease.Out)) : Vector3.zero;
            ringGlow.Set(Mathf.Clamp01(ringFx) * Mathf.Clamp01(ringFx));
        }

        void BeamOff()
        {
            beamSteady = false; ringFx = 0f;
            if (impactRing) PoseImpactRing();   // a beam cut mid-impact must not re-appear with a stale ring
            if (beamDust) beamDust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (beamRig) beamRig.SetActive(false);
        }

        // ================================================================== skylight
        // Local +Y faces the floor. Six blades are circular segments whose straight edges form a hexagonal aperture; each is tilted
        // along its edge so the over/under stacking reads as an iris pinwheel. Fully open, every blade lies under the housing.
        void BuildSkylight()
        {
            skylight = Fx.Node("Skylight", dynRoot, new Vector3(0f, SkyY, 0f)).transform;
            skylight.localRotation = Quaternion.Euler(180f, 0f, 0f);
            var housing = Fx.Mat(MatKind.Lit, new Color(0.34f, 0.24f, 0.15f), null, 0.6f, 0.85f);
            var bronze = Fx.Mat(MatKind.Lit, new Color(0.72f, 0.5f, 0.27f), null, 0.62f, 0.9f);
            Obj("Housing", ChamberArt.Arc(SkyAperture, SkyHousing, 0, 360, 0f, 128), housing, skylight);
            Obj("Collar", ChamberArt.Wall(SkyHousing, SkyHousing, 0, 360, -0.2f, 0f, 128), housing, skylight);
            Obj("Throat", ChamberArt.Wall(SkyAperture, SkyAperture, 0, 360, -0.28f, 0f, 96, false), housing, skylight);
            Obj("Trim", ChamberArt.Arc(SkyHousing - 0.08f, SkyHousing, 0, 360, 0.004f, 128), goldTrim, skylight);
            Obj("Frosted Glass", ChamberArt.Arc(0f, SkyAperture + 0.05f, 0, 360, -0.27f, 96), Fx.Mat(MatKind.Glass, new Color(0.9f, 0.94f, 0.88f, 0.55f), null, 0.2f, 0f), skylight);
            var glowM = Fx.Mat(MatKind.Additive, Color.black); Fx.SetTexture(glowM, Fx.SoftDot);
            Obj("Sky Glow", ChamberArt.Arc(0f, SkyAperture, 0, 360, -0.25f, 96, SkyAperture), glowM, skylight);
            skyGlow = new Glow(glowM, new Color(2.2f, 1.9f, 1.4f, 1f), false);
            var rimM = Fx.Mat(MatKind.LitEmissive, new Color(0.85f, 0.64f, 0.3f), Color.black, 0.7f, 1f);
            Obj("Gold Rim", ChamberArt.Arc(SkyAperture - 0.02f, SkyAperture + 0.12f, 0, 360, 0.006f, 128), rimM, skylight);
            skyRim = new Glow(rimM, GoldGlow, true);
            var haloM = Fx.Mat(MatKind.Additive, Color.black); Fx.SetTexture(haloM, ChamberArt.Halo());
            Obj("Rim Halo", ChamberArt.Arc(SkyAperture - 0.4f, SkyAperture + 0.5f, 0, 360, 0.012f, 128), haloM, skylight);
            skyHalo = new Glow(haloM, new Color(1f, 0.72f, 0.3f, 0.6f), false);

            // blade: straight edge on local X, curved edge on the housing radius once retracted (a1); hb/SkyAperture ≈ 2 keeps every
            // partly-open pose free of gaps outside the hexagon
            const float a1 = SkyAperture + 0.04f, hb = SkyHousing - 0.04f, tilt = 0.035f; const int segs = 24;
            float c = Mathf.Sqrt(hb * hb - a1 * a1), b0 = Mathf.Asin(c / hb);
            Vector3 nrm = new Vector3(tilt, 1f, 0f).normalized;
            var v = new Vector3[segs + 2]; var nv = new Vector3[v.Length]; var t = new int[segs * 3];
            nv[0] = nrm;
            for (int k = 0; k <= segs; k++)
            {
                float b = Mathf.Lerp(-b0, b0, k / (float)segs), x = hb * Mathf.Sin(b);
                v[k + 1] = new Vector3(x, -tilt * x, hb * Mathf.Cos(b) - a1); nv[k + 1] = nrm;
            }
            for (int k = 0; k < segs; k++) { t[k * 3] = k + 2; t[k * 3 + 1] = 0; t[k * 3 + 2] = k + 1; }
            float e = c - 0.08f;
            var ev = new[] { new Vector3(-e, tilt * e + 0.005f, 0f), new Vector3(e, -tilt * e + 0.005f, 0f), new Vector3(-e, tilt * e + 0.005f, 0.05f), new Vector3(e, -tilt * e + 0.005f, 0.05f) };
            var parts = new List<CombineInstance>
            {
                new CombineInstance { mesh = ChamberArt.Build("Blade", v, nv, new Vector2[v.Length], t), transform = Matrix4x4.identity },
                new CombineInstance { mesh = ChamberArt.Build("Blade Edge", ev, new[] { nrm, nrm, nrm, nrm }, new Vector2[4], new[] { 0, 2, 1, 1, 2, 3 }), transform = Matrix4x4.identity },
            };
            var blade = ChamberArt.Combine("Iris Blade", parts, false);
            skyBlades = new Transform[6];
            for (int i = 0; i < skyBlades.Length; i++)
            {
                var g = Fx.MeshObj("Blade " + (i + 1), blade, bronze, skylight);
                g.GetComponent<Renderer>().sharedMaterials = new[] { bronze, goldTrim };
                skyBlades[i] = g.transform;
            }
            skyCol = skylight.gameObject.AddComponent<SphereCollider>(); skyCol.radius = 2.6f; skyCol.enabled = false;   // most of the visible disc, not just the aperture
            PoseSkylight(0f, SkyRimRest);
        }

        /// open 0 closed → 1 retracted under the housing; rim = gold rim emission level.
        void PoseSkylight(float open, float rim)
        {
            skyOpen = open;
            float a = Mathf.Lerp(-0.13f, SkyAperture + 0.04f, open);
            for (int i = 0; i < skyBlades.Length; i++)
            {
                var q = Quaternion.Euler(0f, i * 60f + 40f * open, 0f);
                skyBlades[i].SetLocalPositionAndRotation(q * new Vector3(0f, -0.12f, a), q);
            }
            skyRim.Set(rim); skyHalo.Set(0.3f * rim); skyGlow.Set(open);
        }

        // ================================================================== formation particles + lights
        void BuildFormationFx()
        {
            swirlPs = Fx.Particles("Formation Swirl", transform, new Fx.PCfg { color = Color.white, rate = 0f, max = SwirlCount, life = 100f, size = 0.06f, sizeVar = 0f, speed = 0f, noise = 0f });
            var col = swirlPs.colorOverLifetime; col.enabled = false;
            var swr = swirlPs.GetComponent<ParticleSystemRenderer>(); swr.maxParticleSize = 0.05f;   // motes skim the lens at the start
            Fx.SetColor(swr.sharedMaterial, new Color(2.4f, 1.9f, 1.15f, 1f));
            swirlPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            swirlBuf = new ParticleSystem.Particle[SwirlCount];
            swR0 = new float[SwirlCount]; swH0 = new float[SwirlCount]; swTh = new float[SwirlCount]; swOm = new float[SwirlCount]; swDel = new float[SwirlCount];
            swRT = new float[SwirlCount]; swHT = new float[SwirlCount]; swR = new float[SwirlCount]; swH = new float[SwirlCount]; swSize = new float[SwirlCount];
            swPh = new float[SwirlCount]; swMote = new bool[SwirlCount];

            sparks = Fx.Particles("Assembly Sparks", transform, new Fx.PCfg { rate = 0f, max = 1400, life = 1.3f, size = 0.05f, speed = 0.9f, radius = 0.7f, noise = 0.6f, gravity = -0.05f, color = Palette.Gold, color2 = new Color(1f, 0.95f, 0.8f) });
            Fx.SetColor(sparks.GetComponent<ParticleSystemRenderer>().sharedMaterial, new Color(2f, 1.6f, 1f, 1f));

            formLight = NewLight("Formation Light", new Vector3(0f, 1.6f, 0f), 10f, new Color(1f, 0.78f, 0.42f));
            fillLight = NewLight("Hero Fill Light", new Vector3(0f, 6.5f, -2.8f), 13f, new Color(1f, 0.94f, 0.84f));
        }

        Light NewLight(string n, Vector3 pos, float range, Color c)
        {
            var l = Fx.Node(n, transform, pos).AddComponent<Light>();
            l.type = LightType.Point; l.range = range; l.color = c; l.shadows = LightShadows.None; l.intensity = 0f;
            return l;
        }

        // ================================================================== editor-placed content
        void InitPieces()
        {
            var src = domePieces != null && domePieces.Length > 0 ? domePieces : dome ? dome.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            var list = new List<Piece>(src.Length);
            foreach (var r in src)
            {
                if (!r) continue;
                var b = r.bounds; Vector3 c = transform.InverseTransformPoint(b.center);
                float back = Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(c.z, c.x) * Mathf.Rad2Deg, 90f)) / 180f, h = Mathf.Clamp01(c.y / 12.5f);
                // rings around the axis (floor band, rib sweep, lantern) only rise: a scale-pop would shrink them through the beds
                bool ringLike = new Vector2(c.x, c.z).magnitude < Mathf.Max(b.extents.x, b.extents.z) * 0.5f;
                list.Add(new Piece { r = r, t = r.transform, center = b.center, off = r.transform.position - b.center, scale = r.transform.localScale,
                                     pop = ringLike ? 0f : 0.28f, delay = 6.4f + h * 2.3f + back * 1.3f + ChamberArt.Hash01(list.Count) * 0.15f });
            }
            pieces = list.ToArray();
        }

        void InitVegetation()
        {
            int n = vegetation ? vegetation.childCount : 0;
            veg = new Item[n];
            for (int i = 0; i < n; i++)
            {
                var t = vegetation.GetChild(i); Vector3 p = t.localPosition;
                float r = new Vector2(p.x, p.z).magnitude, back = Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(p.z, p.x) * Mathf.Rad2Deg, 90f)) / 180f;
                veg[i] = new Item { t = t, scale = t.localScale, rot = t.localRotation, sway = 0.75f + 0.5f * ChamberArt.Hash01(i + 600),   // rest pose + per-plant rocking speed
                                    delay = 6.0f + Mathf.Clamp01(r / 12.5f) * 1.9f + back * 0.7f + ChamberArt.Hash01(i + 300) * 0.35f };
            }
        }

        void InitOccluders()
        {
            var list = new List<Occluder>();
            void Add(Transform t, float shrink)
            {
                var rs = t.GetComponentsInChildren<Renderer>(true); if (rs.Length == 0) return;
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                float rad = Mathf.Max(b.extents.x, b.extents.z) * shrink;
                if (rad < 0.5f) return;
                list.Add(new Occluder { c = new Vector2(b.center.x, b.center.z), rad2 = rad * rad, y0 = b.min.y - 0.3f, y1 = b.max.y + 0.3f, r = rs });
            }
            foreach (var v in veg) if (v.t) Add(v.t, 0.8f);
            foreach (var v in vines) Add(v, 0.9f);
            foreach (var f in floaters) Add(f, 1.3f);   // a high-pitch orbit (y ≈ 6–7 m) passes through the suspended leaves
            for (int i = 0; i < staticRoot.childCount; i++) { var t = staticRoot.GetChild(i); if (t.name.StartsWith("Banner")) Add(t, 0.9f); }
            occ = list.ToArray();
        }
    }
}
