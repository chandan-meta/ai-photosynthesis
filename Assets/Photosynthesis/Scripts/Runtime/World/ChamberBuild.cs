using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Photosynthesis
{
    /// Chamber construction around the editor-placed environment: the hero plant's soil bed and ring in the centre planter,
    /// scenery outside, underground cross-section, light shafts, sun beam, crown skylight, formation particles and lights.
    /// Static decor is runtime static-batched; anything animated stays separate.
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
        struct Piece { public Renderer r; public Transform t; public Vector3 center, off, scale; public float delay, pop; }
        struct Fade { public Renderer r; public Material m; public float min; }
        struct Occluder { public Bounds b; public Renderer r; public bool hidden; }
        const float OccMargin = 0.6f;   // lens closer than this to a see-through renderer's bounds hides it

        static readonly Color GoldGlow = new Color(1f, 0.72f, 0.3f);

        Transform staticRoot, dynRoot;
        Texture2D soilTex, shaftTex;
        Material soilMat, goldTrim;
        Glow[][] ringStages; float[] ringK;
        readonly List<Glow> daylit = new List<Glow>();   // scale with daylight but are not lit by it (scenery, the lab's lamps and screens)
        Glow shaftGlow, shaftGlowSoft; ParticleSystem shaftDust;
        Piece[] pieces; Occluder[] occ;
        readonly List<Renderer> fadeSolid = new List<Renderer>(); readonly List<Fade> fadeGhost = new List<Fade>();
        Material glassFloorMat; Renderer glassFloor; GameObject underground;
        ParticleSystem sparks; Light formLight, fillLight; const float FillBase = 13f;

        // sun beam (runs from the skylight to the target)
        const float BeamLen = 15f, BeamHit = 0.32f, BeamWhoom = 1.2f, FlareRest = 0.4f;   // FlareRest: steady hotspot small enough that the lit leaf and the EnergyWave read around it
        GameObject beamRig; Transform beamShaft, beamFlare, beamHalo, impactRing; Glow[] beamGlows; Glow flareGlow, ringGlow;
        Light beamSpot; ParticleSystem beamDust; bool beamSteady; float ringFx, beamLen = BeamLen;

        // skylight: bronze iris at the roof crown; flipped, its throat and glass rise 0.28 m, so the stack stays under the crown hub
        // (underside y ≈ 12.24 inside the aperture) and the collar reaches up to the ribs (≈ 12.2 at r 3.4)
        const float SkyY = 11.9f, SkyAperture = 1.68f, SkyHousing = 3.38f, SkyOpenTime = 0.9f, SkyRimRest = 1.3f;
        Transform skylight; Transform[] skyBlades; SphereCollider skyCol; Glow skyRim, skyHalo, skyGlow; float skyOpen; bool skyReady;

        GameObject Obj(string name, Mesh mesh, Material m, Transform parent = null) => Fx.MeshObj(name, mesh, m, parent ? parent : staticRoot);

        void Build()
        {
            var art = Fx.Node("Procedural", transform).transform;
            staticRoot = Fx.Node("Static Decor", art).transform;
            dynRoot = Fx.Node("Animated", art).transform;
            soilTex = ChamberArt.Soil(); shaftTex = ChamberArt.Shaft(false);
            soilMat = Fx.Mat(MatKind.Lit, Color.white, null, 0.12f, 0f); Fx.SetTexture(soilMat, soilTex);
            SurfaceArt.Apply(soilMat, "Soil", true, 1f);
            goldTrim = Fx.Mat(MatKind.LitEmissive, new Color(0.85f, 0.64f, 0.3f), new Color(0.55f, 0.36f, 0.1f), 0.8f, 1f);

            BuildPlatform(); BuildUnderground(); BuildShafts(); BuildBeam(); BuildSkylight(); BuildFormationFx();
            InitPieces(); InitEnvironment();
            if (!ModelExport.Requested) StaticBatchingUtility.Combine(staticRoot.gameObject);
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

        // ================================================================== platform (inside the environment's centre planter)
        void BuildPlatform()
        {
            const float h1 = PlatformHeight - 0.02f, rS = 2.95f;   // soil reaches under the planter's inner wall (r ≈ 2.9)
            var root = Fx.Node("Investigation Platform", staticRoot).transform;

            void Fadeable(GameObject g)
            {
                var gm = Fx.Mat(MatKind.LitTransparent, Color.white, null, 0.9f, 0f); Fx.SetTexture(gm, soilTex);
                AddFade(g.GetComponent<Renderer>(), gm, 0f);
            }
            Fadeable(Obj("Soil Bed", ChamberArt.Arc(0f, rS, 0, 360, h1 - 0.03f, 96, 0.5f), soilMat, root));
            // gentle mound so the plant base (anchor at PlatformHeight) sits in soil
            Fadeable(Fx.Prim(PrimitiveType.Sphere, root, new Vector3(0f, h1 - 0.03f, 0f), new Vector3(2.6f, 0.13f, 2.6f), soilMat, false, "Soil Mound"));
            // Real small clods and organic debris give the planter contact depth without adding individual draws.
            var clods = new MicroKitB.Batch(); var chip = MicroKitB.Super(Vector3.one * .5f, 2f, 2f, 5, 7, false, d => MicroKitB.Noise(d * 3f) * .18f);
            var earthRandom = new System.Random(4209);
            for (int i = 0; i < 150; i++)
            {
                float angle = (float)earthRandom.NextDouble() * 360f;
                float radius = Mathf.Lerp(.55f, 2.75f, Mathf.Sqrt((float)earthRandom.NextDouble()));
                float size = Mathf.Lerp(.045f, .13f, (float)earthRandom.NextDouble());
                clods.Add(chip, Polar(angle, radius, h1 - .025f), Quaternion.Euler(12f, angle, 7f), new Vector3(size, size * .45f, size * 1.4f));
            }
            var debris = clods.Make("Granular planter soil", root, soilMat); debris.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            Fadeable(debris); Destroy(chip);

            glassFloorMat = Fx.Mat(MatKind.Glass, new Color(0.75f, 0.95f, 0.92f, 0f));
            glassFloor = Obj("Glass Floor", ChamberArt.Arc(0f, rS, 0, 360, h1 + 0.003f, 96), glassFloorMat, root).GetComponent<Renderer>();
            glassFloor.enabled = false;

            // a dim teal inlay on the planter rim, not a neon teleporter pad (the ignition in EvaluateForm still flashes it to ~3x);
            // the additive halo fakes the light pooling on the stone
            Color cyan = Palette.Cyan;
            var ringM = Fx.Mat(MatKind.LitEmissive, cyan * 0.3f, Color.black, 0.8f, 0.3f);
            Obj("Cyan Ring", ChamberArt.Arc(3.06f, 3.13f, 0, 360, RimTop + 0.003f, 160), ringM, root);
            var haloM = Fx.Mat(MatKind.Additive, new Color(cyan.r, cyan.g, cyan.b, 0.2f)); Fx.SetTexture(haloM, ChamberArt.Halo());
            Obj("Cyan Halo", ChamberArt.Arc(2.93f, 3.29f, 0, 360, RimTop + 0.004f, 160), haloM, root);
            ringStages = new[] { new[] { new Glow(ringM, cyan * 1.5f, true), new Glow(haloM, new Color(cyan.r, cyan.g, cyan.b, 0.2f), false) } };
            ringK = new float[ringStages.Length];
        }

        // ================================================================== underground (visible through the transparent floor)
        void BuildUnderground()
        {
            underground = Fx.Node("Underground", transform);
            var u = underground.transform;
            // near (outward) face stays thin so the roots read through it; the far (inward) face gives the soil context
            var strataTex = ChamberArt.Strata();
            var strataNear = Fx.Mat(MatKind.LitTransparent, new Color(1f, 1f, 1f, 0.38f), null, 0.2f, 0f); Fx.SetTexture(strataNear, strataTex);
            SurfaceArt.Apply(strataNear, "Soil", false, 1f);
            var strataFar = Fx.Mat(MatKind.LitTransparent, Color.white, null, 0.2f, 0f); Fx.SetTexture(strataFar, strataTex);
            SurfaceArt.Apply(strataFar, "Soil", false, 1f);
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

        // ================================================================== light shafts + dust
        // ponytail: fake volumetrics (crossed additive quads) aimed along the sun direction captured at startup; a URP
        // volumetric-light renderer feature is the upgrade if the sun ever moves.
        void BuildShafts()
        {
            Vector3 toSun = -(sunRot * Vector3.forward); if (toSun.y < 0.25f) toSun = (toSun + Vector3.up).normalized;
            Vector4[] hits = { new(2.6f, 4.4f, 1.3f, 0), new(-3.8f, 1.4f, 0.9f, 1), new(-1.4f, 7.4f, 1.6f, 0), new(5.6f, -0.8f, 1.0f, 1),   // feet on open floor, clear of the lab tables
                               new(-6.6f, -2.4f, 1.4f, 0), new(1.2f, 7.4f, 1.1f, 1), new(-6.0f, 3.0f, 1.5f, 1), new(5.2f, 2.6f, 0.9f, 0) };
            var strong = new List<CombineInstance>(); var soft = new List<CombineInstance>();
            const float R = 10.8f, Cy = 2.2f;   // the roof cap: a sphere through its base ring (r 10.2, y 5.8) and crown (y 13)
            foreach (var h in hits)
            {
                Vector3 p = new Vector3(h.x, 0.02f, h.y), q = p - Vector3.up * Cy;
                float b = Vector3.Dot(q, toSun), len = -b + Mathf.Sqrt(b * b - (q.sqrMagnitude - R * R));   // to the roof
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
            c.Circle(new Vector2(64, 64), 54, 7f); c.Circle(new Vector2(64, 64), 54, 16f, 0.35f);
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
            Obj("Collar", ChamberArt.Wall(SkyHousing, SkyHousing, 0, 360, -0.32f, 0f, 128), housing, skylight);
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
                // rings around the axis (roof bands, the crown) only rise: a scale-pop would shrink them through the posts
                bool ringLike = new Vector2(c.x, c.z).magnitude < Mathf.Max(b.extents.x, b.extents.z) * 0.5f;
                list.Add(new Piece { r = r, t = r.transform, center = b.center, off = r.transform.position - b.center, scale = r.transform.localScale,
                                     pop = ringLike ? 0f : 0.28f, delay = 6.4f + h * 2.3f + back * 1.3f + ChamberArt.Hash01(list.Count) * 0.15f });
            }
            pieces = list.ToArray();
        }

        /// The environment beyond the formation: the inner floor and the planter ring go see-through for the roots x-ray,
        /// lamps and screens follow the daylight and the mood (runtime copies, the assets stay untouched), and foliage /
        /// hologram screens (anything not opaque) are culled when the lens is inside them.
        void InitEnvironment()
        {
            var list = new List<Occluder>(); var copies = new Dictionary<Material, Material>();
            var rs = dome ? dome.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            foreach (var r in rs)
            {
                string n = r.name;
                if (n == "Floor Inner" || n.StartsWith("Console"))
                {
                    var src = r.sharedMaterial;
                    // same gloss as the solid, so the swap at the start of the x-ray does not pop (its normal map would need a keyword: dropped)
                    var gm = Fx.Mat(MatKind.LitTransparent, Color.white, null, src ? src.GetFloat("_Smoothness") : 0.5f, src ? src.GetFloat("_Metallic") : 0f);
                    if (src) Fx.SetTexture(gm, src.GetTexture("_BaseMap"));
                    AddFade(r, gm, n == "Floor Inner" ? 0.3f : 0.2f);
                    continue;
                }
                var ms = r.sharedMaterials; bool clear = false;
                for (int i = 0; i < ms.Length; i++)
                {
                    var m = ms[i]; if (!m) continue;
                    clear |= m.renderQueue >= (int)RenderQueue.AlphaTest;
                    if (!m.IsKeywordEnabled("_EMISSION")) continue;
                    if (!copies.TryGetValue(m, out var c)) { copies[m] = c = new Material(m); daylit.Add(new Glow(c, m.GetColor("_EmissionColor"), true)); }
                    ms[i] = c;
                }
                r.sharedMaterials = ms;
                if (!clear) continue;
                list.Add(new Occluder { b = r.bounds, r = r });   // bounds, not a circle: thin ivy strands and the ends of the curved screens count
            }
            occ = list.ToArray();
        }
    }
}
