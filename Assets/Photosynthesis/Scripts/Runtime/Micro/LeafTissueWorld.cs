using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Photosynthesis
{
    /// Leaf interior cross-section (upper epidermis, palisade, spongy mesophyll + air spaces, lower epidermis with stoma).
    /// Used to enter the cell and for the oxygen / food journeys.
    /// Local layout: block x ∈ [-9,9], z ∈ [-3,3] (front cut face at z = -3); upper cuticle y ≈ 4.1, lower cuticle y ≈ -3.25.
    /// The vein runs along z (seen in section on the cut face); its main phloem tube extends far behind the block for the food ride.
    /// ponytail: cells on the cut face are whole translucent cells, not sliced geometry; a clip-plane shader would give true cut walls.
    public class LeafTissueWorld : MicroWorld
    {
        const float PalY = 1.95f, PalH = 1.18f, PalR = 0.36f, FrontZ = -2.6f;
        static readonly Vector2 Bundle = new Vector2(-4.6f, -0.95f);
        static readonly Vector3 StomaPos = new Vector3(3.5f, -2.85f, -2.0f);
        static readonly Vector2 PhloemMain = Bundle + new Vector2(0.08f, -0.58f);
        const float PhloemMainR = 0.19f;

        bool ready;
        Material chlMat, targetChlMat, targetCellMat, glowO2, glowSugar, trailO2, trailSugar, shaftMat;
        Color chlEmission, targetCellBase;
        GameObject phloemExt, targetGlow;
        float targetLit, targetLitGoal;
        Vector3 diveCell, o2Cell, foodCell;          // local centres of the three story cells
        readonly List<GameObject> transient = new List<GameObject>();

        // ================================================================== build
        protected override void Build()
        {
            var rnd = new System.Random(11);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            var root = transform;

            // --- materials
            var cuticle = Fx.Mat(MatKind.Glass, new Color(0.92f, 1f, 0.7f, 0.32f), new Color(0.05f, 0.07f, 0.02f), 0.97f);
            var epi = Fx.Mat(MatKind.LitTransparent, new Color(0.78f, 0.96f, 0.86f, 0.2f), new Color(0.02f, 0.05f, 0.04f), 0.85f);
            var pal = Fx.Mat(MatKind.LitTransparent, new Color(0.34f, 0.67f, 0.25f, 0.36f), new Color(0.015f, 0.045f, 0.01f), 0.7f);
            var spongy = Fx.Mat(MatKind.LitTransparent, new Color(0.46f, 0.66f, 0.29f, 0.3f), new Color(0.015f, 0.04f, 0.01f), 0.65f);
            var sheath = Fx.Mat(MatKind.LitTransparent, new Color(0.8f, 0.86f, 0.5f, 0.34f), new Color(0.04f, 0.05f, 0.01f), 0.6f);
            chlEmission = new Color(0.05f, 0.32f, 0.08f);
            chlMat = Fx.Mat(MatKind.LitEmissive, new Color(0.18f, 0.6f, 0.2f), chlEmission, 0.6f);
            targetChlMat = Fx.Mat(MatKind.LitEmissive, new Color(0.2f, 0.66f, 0.22f), chlEmission, 0.65f);
            targetCellBase = new Color(0.5f, 0.9f, 0.46f, 0.3f);
            targetCellMat = Fx.Mat(MatKind.LitTransparent, targetCellBase, new Color(0.02f, 0.07f, 0.02f), 0.75f);
            var guard = Fx.Mat(MatKind.LitTransparent, new Color(0.4f, 0.82f, 0.36f, 0.55f), new Color(0.03f, 0.12f, 0.03f), 0.7f);
            var xylem = Fx.Mat(MatKind.LitTransparent, Palette.Xylem * new Color(1, 1, 1, 0.42f), Palette.Xylem * 0.18f, 0.8f);
            var xylemRing = Fx.Mat(MatKind.Lit, new Color(0.62f, 0.8f, 1f), null, 0.5f);
            var phloem = Fx.Mat(MatKind.LitTransparent, new Color(1f, 0.64f, 0.28f, 0.42f), Palette.Phloem * 0.22f, 0.8f);
            var plate = Fx.Mat(MatKind.LitTransparent, new Color(1f, 0.75f, 0.45f, 0.16f), Palette.Phloem * 0.1f, 0.6f);
            glowO2 = MicroKitB.GlowMat(new Color(Palette.Oxygen.r, Palette.Oxygen.g, Palette.Oxygen.b, 0.55f) * 1.6f);
            glowSugar = MicroKitB.GlowMat(new Color(Palette.Glucose.r, Palette.Glucose.g, Palette.Glucose.b, 0.6f) * 1.8f);
            trailO2 = Fx.Mat(MatKind.Additive, new Color(Palette.Oxygen.r, Palette.Oxygen.g, Palette.Oxygen.b, 0.45f));
            trailSugar = Fx.Mat(MatKind.Additive, new Color(Palette.Glucose.r * 1.5f, Palette.Glucose.g * 1.5f, Palette.Glucose.b * 1.5f, 0.5f));
            shaftMat = Fx.Mat(MatKind.Additive, new Color(1f, 0.86f, 0.5f, 0.16f)); Fx.SetTexture(shaftMat, MicroKitB.BeamTex);

            var chlMesh = MicroKitB.Sphere(7, 12);   // the dive, O₂ and food shots pass ~1 m from these: no visible facets
            var chl = new MicroKitB.Batch();
            void Chloroplast(Vector3 c, Vector3 outward, Vector3 up, float s) =>
                chl.Add(chlMesh, c, Quaternion.LookRotation(outward, up), new Vector3(0.2f, 0.34f, 0.12f) * s);

            // --- cuticles + epidermis (flat clear cells)
            Fx.MeshObj("UpperCuticle", MicroKitB.Super(new Vector3(9.15f, 0.09f, 3.05f), 10, 8, 14, 40), cuticle, root).transform.localPosition = new Vector3(0, 4.06f, 0);
            Fx.MeshObj("LowerCuticle", PoreSheet(new Vector3(9.15f, 0.06f, 3.05f), StomaPos, new Vector2(0.3f, 0.8f)), cuticle, root).transform.localPosition = new Vector3(0, -3.24f, 0);
            var epiMesh = MicroKitB.Super(Vector3.one * 0.5f, 4, 3, 8, 16);
            var epiB = new MicroKitB.Batch(); var epiLow = new MicroKitB.Batch();   // upper / lower apart: correct transparent sorting
            for (int ix = 0; ix < 12; ix++)
                for (int iz = 0; iz < 4; iz++)
                {
                    float x = -8.25f + ix * 1.5f + R(-0.05f, 0.05f), z = -2.25f + iz * 1.5f;
                    epiB.Add(epiMesh, new Vector3(x, 3.6f, z), Quaternion.identity, new Vector3(1.46f * R(0.95f, 1.03f), 0.68f, 1.44f));
                    bool stoma = Mathf.Abs(x - StomaPos.x) < 1.0f && Mathf.Abs(z - StomaPos.z) < 1.3f;
                    if (!stoma) epiLow.Add(epiMesh, new Vector3(x, -2.85f, z), Quaternion.identity, new Vector3(1.44f, 0.56f, 1.44f));
                }
            epiB.Make("UpperEpidermis", root, epi);
            epiLow.Make("LowerEpidermis", root, epi);

            // --- palisade mesophyll: tall columns lined with chloroplasts
            var palMesh = MicroKitB.Super(new Vector3(PalR, PalH, PalR), 2, 3.5f, 14, 20);
            var palB = new MicroKitB.Batch();
            float Col(float x) => Mathf.Round((x + 8.6f) / 0.8f) * 0.8f - 8.6f;
            diveCell = new Vector3(Col(0.4f), PalY, FrontZ); o2Cell = new Vector3(Col(2.6f), PalY, FrontZ); foodCell = new Vector3(Col(-3.4f), PalY, FrontZ);
            for (int ix = 0; ix < 22; ix++)
                for (int iz = 0; iz < 7; iz++)
                {
                    var c = new Vector3(-8.6f + ix * 0.8f, PalY + R(-0.05f, 0.05f), FrontZ + iz * 0.8f);
                    bool target = iz == 0 && Mathf.Abs(c.x - diveCell.x) < 0.01f;
                    if (target) c.y = PalY;
                    var sc = new Vector3(R(0.95f, 1.04f), R(0.97f, 1.03f), R(0.95f, 1.04f));
                    MicroKitB.Batch into = chl;
                    if (target)
                    {
                        Fx.MeshObj("DiveCell", palMesh, targetCellMat, root).transform.localPosition = c;
                        into = new MicroKitB.Batch();
                    }
                    else palB.Add(palMesh, c, Quaternion.identity, sc);
                    for (int j = 0; j < 13; j++)
                    {
                        float a = j * 2.39996f + ix, h = -0.98f + 1.96f * (j + 0.5f) / 13f;
                        var o = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                        into.Add(chlMesh, c + o * (0.27f * Mathf.Sqrt(1f - Mathf.Pow(Mathf.Abs(h * 0.86f), 3.5f))) + Vector3.up * h * PalH * 0.86f, Quaternion.LookRotation(o, Vector3.up), new Vector3(0.2f, 0.34f, 0.12f) * R(0.85f, 1.05f));
                    }
                    if (target)
                    {
                        into.Make("DiveChloroplasts", root, targetChlMat);
                        targetGlow = MicroKitB.Glow(root, c, 2.6f, MicroKitB.GlowMat(new Color(0.4f, 1f, 0.45f, 0.12f)));   // faint: the close-up stays crisp
                        targetGlow.SetActive(false);
                    }
                }
            palB.Make("Palisade", root, pal);

            // --- O₂ escape route + food route (kept clear of spongy cells)
            var o2Path = O2Path(o2Cell);

            // --- vein bundle (runs along z): xylem above, phloem below, bundle sheath around
            var vein = Fx.Node("Vein", root).transform;
            var sheathB = new MicroKitB.Batch();
            var blobs = new Mesh[5];
            for (int i = 0; i < blobs.Length; i++)
            {
                var seed = new Vector3(i * 3.7f, i * 1.3f, i * 5.1f);
                blobs[i] = MicroKitB.Super(Vector3.one * 0.5f, 2, 2, 9, 14, false, d => MicroKitB.Noise(d * 1.6f + seed) * 0.22f);
            }
            for (float z = -2.55f; z < 3f; z += 0.85f)
                for (int k = 0; k < 11; k++)
                {
                    float a = k * Mathf.PI * 2f / 11f + z;
                    var p = new Vector3(Bundle.x + Mathf.Cos(a) * 1.08f, Bundle.y + Mathf.Sin(a) * 0.98f, z);
                    sheathB.Add(blobs[k % 5], p, Quaternion.Euler(R(0, 360), R(0, 360), 0), new Vector3(0.62f, 0.56f, 0.8f));
                    if (k % 2 == 0) Chloroplast(p + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * 0.16f, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0), Vector3.forward, 0.6f);
                }
            sheathB.Make("BundleSheath", vein, sheath);
            var ringB = new MicroKitB.Batch();
            var xy = new[] { (new Vector2(-0.42f, 0.32f), 0.21f), (new Vector2(0.06f, 0.5f), 0.25f), (new Vector2(0.5f, 0.27f), 0.2f), (new Vector2(0.02f, 0.02f), 0.15f) };
            foreach (var (off, r) in xy)
            {
                var c = Bundle + off;
                var xs = new[] { new Vector3(c.x, c.y, -3f), new Vector3(c.x, c.y, 3f) };
                Fx.MeshObj("Xylem", Fx.Tube(xs, r, r, 14), xylem, vein);
                Fx.MeshObj("XylemInner", Fx.Tube(xs, r * 0.98f, r * 0.98f, 14, true), xylem, vein);   // open vessel reads hollow from the cut face
                var ring = MicroKitB.Loop(MicroKitB.Circle(r * 0.97f, 14), 0.025f, 5);
                for (float z = -2.85f; z < 3f; z += 0.32f) ringB.Add(ring, new Vector3(c.x, c.y, z), Quaternion.Euler(90, 0, 0), Vector3.one);
                Beads.Create("Water", vein, new[] { new Vector3(c.x, c.y, -3f), new Vector3(c.x, c.y, 3f) }, 5, r * 0.7f,
                    Fx.Shared(MatKind.LitEmissive, Palette.Water, Palette.Water * 2.2f), R(0.08f, 0.12f));
            }
            ringB.Make("XylemThickenings", vein, xylemRing);
            var ph = new[] { (new Vector2(-0.38f, -0.42f), 0.16f), (PhloemMain - Bundle, PhloemMainR), (new Vector2(0.52f, -0.34f), 0.15f) };
            var sugar = Fx.Shared(MatKind.LitEmissive, Palette.Phloem, Palette.Phloem * 2.6f);
            foreach (var (off, r) in ph)
            {
                var c = Bundle + off;
                var seg = new[] { new Vector3(c.x, c.y, -3f), new Vector3(c.x, c.y, 3f) };
                Fx.MeshObj("Phloem", Fx.Tube(seg, r, r, 14), phloem, vein);
                Fx.MeshObj("PhloemInner", Fx.Tube(seg, r * 0.98f, r * 0.98f, 14, true), phloem, vein);
                Beads.Create("Sugar", vein, new[] { seg[0] + Vector3.down * r * 0.5f, seg[1] + Vector3.down * r * 0.5f }, 4, r * 0.4f, sugar, R(0.1f, 0.14f));
            }
            // main phloem extension (enabled only for the food ride)
            phloemExt = Fx.Node("PhloemRide", vein).gameObject;
            var ext = new[] { new Vector3(PhloemMain.x, PhloemMain.y, 3f), new Vector3(PhloemMain.x, PhloemMain.y, 46f) };
            Fx.MeshObj("Tube", Fx.Tube(ext, PhloemMainR, PhloemMainR, 16, false, 0.2f), phloem, phloemExt.transform);
            Fx.MeshObj("TubeInner", Fx.Tube(ext, PhloemMainR * 0.98f, PhloemMainR * 0.98f, 16, true, 0.2f), phloem, phloemExt.transform);
            var plates = new MicroKitB.Batch(); var plateRing = MicroKitB.Loop(MicroKitB.Circle(PhloemMainR * 0.95f, 16), 0.018f, 5);
            var disc = MicroKitB.Super(new Vector3(PhloemMainR * 0.95f, 0.006f, PhloemMainR * 0.95f), 2, 4, 6, 16);
            for (float z = -1.4f; z < 46f; z += 1.7f)
            {
                plates.Add(plateRing, new Vector3(PhloemMain.x, PhloemMain.y, z), Quaternion.Euler(90, 0, 0), Vector3.one);
                plates.Add(disc, new Vector3(PhloemMain.x, PhloemMain.y, z), Quaternion.Euler(90, 0, 0), Vector3.one);
            }
            plates.Make("SievePlates", phloemExt.transform, plate);
            Beads.Create("RideSugar", phloemExt.transform, new[] { new Vector3(PhloemMain.x + 0.04f, PhloemMain.y - 0.1f, -3f), new Vector3(PhloemMain.x - 0.04f, PhloemMain.y - 0.1f, 46f) },
                26, 0.06f, sugar, 0.045f);
            phloemExt.SetActive(false);

            // --- spongy mesophyll: irregular cells, generous air spaces, clear channel to the stoma
            var spB = new MicroKitB.Batch();
            float DistPath(Vector3 p)
            {
                float d = float.MaxValue;
                for (int i = 1; i < o2Path.Count; i++)
                {
                    Vector3 a = o2Path[i - 1], ab = o2Path[i] - a;
                    float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
                    d = Mathf.Min(d, Vector3.Distance(p, a + ab * t));
                }
                return d;
            }
            for (int iy = 0; iy < 3; iy++)
                for (int iz = 0; iz < 5; iz++)
                    for (float x = -8.5f + (iy + iz) % 2 * 0.55f; x < 8.6f; x += 1.12f)
                    {
                        var p = new Vector3(x + R(-0.2f, 0.2f), -1.85f + iy * 1.0f + R(-0.18f, 0.18f), -2.35f + iz * 1.25f + R(-0.15f, 0.15f));
                        if (new Vector2(p.x - Bundle.x, p.y - Bundle.y).magnitude < 1.75f) continue;
                        if (DistPath(p) < 0.9f) continue;
                        if (Mathf.Abs(p.x - StomaPos.x) < 1.0f && p.y < -1.2f && p.z < -0.7f) continue;     // substomatal cavity
                        if (Mathf.Abs(p.x - foodCell.x - 0.3f) < 0.7f && p.z < -1.8f && p.y > -1.3f) continue;   // food route on the cut face
                        if (rnd.NextDouble() < 0.14) continue;                                                // extra air spaces
                        float s = R(0.8f, 1.1f);
                        var sc = new Vector3(0.95f, 0.75f, 0.9f) * s;
                        var rot = Quaternion.Euler(R(0, 360), R(0, 360), R(0, 360));
                        spB.Add(blobs[rnd.Next(5)], p, rot, sc);
                        for (int j = 0; j < 6; j++)
                        {
                            var d = new Vector3(R(-1, 1), R(-1, 1), R(-1, 1)).normalized;
                            Chloroplast(p + Vector3.Scale(d, sc) * 0.4f, d, Vector3.up, 0.85f);
                        }
                    }
            spB.Make("Spongy", root, spongy);
            chl.Make("Chloroplasts", root, chlMat);

            // --- stoma: two bean-shaped guard cells around the pore
            var gB = new MicroKitB.Batch(); var gChl = new MicroKitB.Batch(); var cap = MicroKitB.Sphere(8, 12);
            for (int side = -1; side <= 1; side += 2)
            {
                var pts = new List<Vector3>();
                for (int i = 0; i <= 12; i++)
                {
                    float th = Mathf.Lerp(-Mathf.PI / 2f, Mathf.PI / 2f, i / 12f);
                    pts.Add(new Vector3(side * (0.2f + 0.22f * Mathf.Cos(th)), 0, 0.86f * Mathf.Sin(th)));
                }
                gB.Add(Fx.Tube(pts, 0.21f, 0.21f, 14), StomaPos, Quaternion.identity, Vector3.one);
                gB.Add(cap, StomaPos + pts[0], Quaternion.identity, Vector3.one * 0.42f);
                gB.Add(cap, StomaPos + pts[12], Quaternion.identity, Vector3.one * 0.42f);
                for (int i = 2; i <= 10; i += 2) gChl.Add(chlMesh, StomaPos + pts[i] + Vector3.up * 0.05f, Quaternion.Euler(R(0, 90), R(0, 180), 0), new Vector3(0.16f, 0.1f, 0.11f));
            }
            gB.Make("GuardCells", root, guard);
            gChl.Make("GuardChloroplasts", root, chlMat);
            MicroKitB.Glow(root, StomaPos + Vector3.down * 0.15f, 1.1f, MicroKitB.GlowMat(new Color(0.6f, 0.95f, 1f, 0.25f)));

            // --- atmosphere: sunlight from above, motes in the air spaces, backdrop
            for (int i = 0; i < 4; i++)
            {
                var top = new Vector3(-6f + i * 4.2f + R(-0.6f, 0.6f), 15f, R(-1.5f, 1.5f));
                MicroKitB.Beam(root, top, new Vector3(top.x + 2.2f, 4.1f, top.z * 0.5f), R(2.2f, 3.4f), shaftMat);
            }
            var motes = Fx.Particles("AirMotes", root, new Fx.PCfg
            {
                color = new Color(0.75f, 0.95f, 1f, 0.35f), color2 = new Color(1f, 0.95f, 0.75f, 0.25f), size = 0.035f, life = 7f, speed = 0.04f,
                rate = 26f, max = 260, shape = ParticleSystemShapeType.Box, noise = 0.12f
            });
            var sh = motes.shape; sh.scale = new Vector3(17.5f, 2.9f, 5.8f);
            motes.transform.localPosition = new Vector3(0, -1.0f, 0);
            var below = Fx.Particles("OutsideAir", root, new Fx.PCfg
            {
                color = new Color(0.7f, 0.9f, 1f, 0.2f), size = 0.05f, life = 8f, speed = 0.05f, rate = 14f, max = 140, shape = ParticleSystemShapeType.Box, noise = 0.2f
            });
            sh = below.shape; sh.scale = new Vector3(20f, 4f, 10f);
            below.transform.localPosition = new Vector3(0, -6f, 0);
            MicroKitB.Backdrop(root, 52f, new Color(0.25f, 0.45f, 0.34f), new Color(0.13f, 0.26f, 0.21f));   // sRGB: see Mood
            ready = true;
        }

        /// Thin waxy sheet (top + bottom faces, local y = ±half.y) with an elliptical pore left open over the stoma, so the O₂
        /// really exits to the air. Grid corners inside the pore are pushed radially onto its rim; cells fully inside are dropped.
        static Mesh PoreSheet(Vector3 half, Vector3 pore, Vector2 poreR, int nx = 72, int nz = 24)
        {
            int w = nx + 1, per = w * (nz + 1);
            var v = new Vector3[per * 2]; var nr = new Vector3[per * 2]; var inside = new bool[per]; var tri = new List<int>(nx * nz * 12);
            for (int iz = 0; iz <= nz; iz++)
                for (int ix = 0; ix <= nx; ix++)
                {
                    int i = iz * w + ix;
                    var p = new Vector3(Mathf.Lerp(-half.x, half.x, ix / (float)nx), 0f, Mathf.Lerp(-half.z, half.z, iz / (float)nz));
                    var e = new Vector2((p.x - pore.x) / poreR.x, (p.z - pore.z) / poreR.y);
                    if (inside[i] = e.sqrMagnitude < 1f)
                    {
                        e = e.sqrMagnitude > 1e-6f ? e.normalized : Vector2.right;
                        p.x = pore.x + e.x * poreR.x; p.z = pore.z + e.y * poreR.y;
                    }
                    v[i] = p + Vector3.up * half.y; v[per + i] = p - Vector3.up * half.y; nr[i] = Vector3.up; nr[per + i] = Vector3.down;
                }
            for (int iz = 0; iz < nz; iz++)
                for (int ix = 0; ix < nx; ix++)
                {
                    int a = iz * w + ix, b = a + 1, c = a + w, d = c + 1;
                    if (inside[a] && inside[b] && inside[c] && inside[d]) continue;
                    tri.Add(a); tri.Add(c); tri.Add(d); tri.Add(a); tri.Add(d); tri.Add(b);                                  // top (+y)
                    tri.Add(per + a); tri.Add(per + d); tri.Add(per + c); tri.Add(per + a); tri.Add(per + b); tri.Add(per + d); // bottom (−y)
                }
            var m = new Mesh { name = "PoreSheet", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.vertices = v; m.normals = nr; m.SetTriangles(tri, 0); m.RecalculateBounds();
            return m;
        }

        static List<Vector3> O2Path(Vector3 cell) => new List<Vector3>
        {
            new Vector3(cell.x, 1.0f, -2.62f), new Vector3(cell.x + 0.15f, 0.45f, -2.75f), new Vector3(cell.x + 0.42f, -0.45f, -2.55f),
            new Vector3(3.35f, -1.35f, -2.2f), new Vector3(StomaPos.x, -2.1f, StomaPos.z), new Vector3(StomaPos.x, StomaPos.y, StomaPos.z),
            new Vector3(StomaPos.x + 0.05f, -3.9f, StomaPos.z - 0.05f), new Vector3(StomaPos.x + 0.35f, -5.8f, StomaPos.z - 0.4f)
        };

        protected override void ApplyMood() =>
            MicroKitB.Mood(new Color(0.15f, 0.31f, 0.25f), 0.018f, new Color(0.5f, 0.66f, 0.52f), new Color(0.2f, 0.32f, 0.26f), new Color(0.05f, 0.09f, 0.08f),
                new Color(1f, 0.92f, 0.74f), 1.7f, new Vector3(66f, -24f, 0f));

        public override (Vector3 pos, Vector3 look) Pose() => (Origin + new Vector3(5.5f, 6.8f, -13.5f), Origin + new Vector3(0.5f, 0.2f, 0f));

        public override void ResetWorld()
        {
            if (!ready) return;
            MicroKitB.Kill(transient);
            phloemExt.SetActive(false);
            targetLit = targetLitGoal = 0f; ApplyTarget();
        }

        void Update()
        {
            if (!ready) return;
            float t = Time.time;
            Fx.SetEmission(chlMat, chlEmission * (0.85f + 0.15f * Mathf.Sin(t * 1.3f)));
            if (!Mathf.Approximately(targetLit, targetLitGoal)) { targetLit = Mathf.MoveTowards(targetLit, targetLitGoal, Time.deltaTime * 0.8f); ApplyTarget(); }
            else if (targetLit > 0) Fx.SetEmission(targetChlMat, Color.Lerp(chlEmission, new Color(0.2f, 1.6f, 0.3f), targetLit) * (0.8f + 0.2f * Mathf.Sin(t * 3f)));
        }

        void ApplyTarget()
        {
            Fx.SetEmission(targetChlMat, Color.Lerp(chlEmission, new Color(0.2f, 1.6f, 0.3f), targetLit));
            Fx.SetColor(targetCellMat, Color.Lerp(targetCellBase, new Color(0.62f, 1f, 0.55f, 0.36f), targetLit));
            targetGlow.SetActive(targetLit > 0.01f);
            targetGlow.transform.localScale = Vector3.one * (1.6f + targetLit * 1.4f);
        }

        Vector3 W(Vector3 local) => transform.TransformPoint(local);

        GameObject Spawn(Mol kind, Vector3 local, float size, Material glow, float glowSize, Material trail, float trailWidth)
        {
            var g = Fx.Molecule(kind, transform, size, 1f);
            g.transform.localPosition = local; transient.Add(g);
            if (glow) MicroKitB.Glow(g.transform, Vector3.zero, glowSize, glow);
            if (trail) MicroKitB.Trail(g, trail, trailWidth, 0.7f);
            return g;
        }

        // ================================================================== journeys
        /// Fly from the leaf surface down into the tissue and up to one palisade cell.
        public IEnumerator DiveToCell(CameraDirector cam)
        {
            if (!ready || !cam) yield break;
            var c = diveCell; var tr = cam.transform;
            var pos = new List<Vector3> { tr.position, W(new Vector3(c.x + 3.6f, 9.6f, -5.6f)), W(new Vector3(c.x + 0.9f, 5.5f, -4.3f)),
                W(new Vector3(c.x + 0.25f, 3.4f, -4.6f)), W(new Vector3(c.x, 2.4f, -4.2f)), W(new Vector3(c.x, 2.1f, -3.72f)) };
            var look = new List<Vector3> { tr.position + tr.forward * 14f, W(new Vector3(c.x, 4.1f, 0.6f)), W(new Vector3(c.x, 3.7f, -1.6f)),
                W(new Vector3(c.x, 2.7f, -2.6f)), W(c + Vector3.up * 0.1f), W(c + Vector3.up * 0.12f) };
            targetLitGoal = 1f;
            yield return MicroKitB.FlyCam(cam, pos, look, 5.2f, 40f);
            targetLit = targetLitGoal; ApplyTarget();
        }

        /// O₂ from the cell through air spaces to the stoma (camera follows).
        public IEnumerator FollowO2(CameraDirector cam)
        {
            if (!ready) yield break;
            MicroKitB.Kill(transient);
            var hero = O2Path(o2Cell);
            var heroGo = Spawn(Mol.O2, hero[0], 0.3f, glowO2, 1.1f, trailO2, 0.12f);
            // companions from neighbouring palisade cells merge into the same route
            var others = new List<(GameObject g, List<Vector3> p, float delay)>();
            float[] xs = { -0.8f, 0.8f, 0f, -0.8f, 0.8f };
            for (int i = 0; i < xs.Length; i++)
            {
                var p = O2Path(o2Cell + new Vector3(xs[i], 0, 0));
                p[0] = new Vector3(p[0].x, 0.85f + 0.3f * (i % 3), p[0].z);
                p[2] = Vector3.Lerp(p[2], hero[2], 0.6f) + new Vector3(0.08f * (i - 2), 0, 0);
                p[3] += new Vector3(0.1f * (i - 2), 0.06f * i, 0.05f * (i - 2));
                others.Add((Spawn(Mol.O2, p[0], 0.2f, glowO2, 0.7f, trailO2, 0.07f), p, 0.25f + i * 0.3f));
            }
            const float dur = 5.2f; bool burst = false;
            if (cam) cam.StopOrbit();
            yield return Fx.Tween(dur, t =>
            {
                float u = Fx.Eval(t, Ease.InOut);
                var hp = Fx.CatmullRom(hero, u);
                heroGo.transform.localPosition = hp + new Vector3(Mathf.Sin(t * 19f), Mathf.Cos(t * 23f), 0) * 0.03f;
                heroGo.transform.localRotation = Quaternion.Euler(t * 160f, t * 220f, 0);
                foreach (var (g, p, d) in others)
                {
                    float v = Mathf.Clamp01((t * dur - d) / (dur - d - 0.2f));
                    g.transform.localPosition = Fx.CatmullRom(p, Fx.Eval(v, Ease.InOut)) + new Vector3(Mathf.Sin(t * 13f + d * 7f), 0, Mathf.Cos(t * 11f + d)) * 0.05f;
                    g.transform.localRotation = Quaternion.Euler(t * 200f + d * 90f, t * 140f, 0);
                }
                if (!burst && hp.y < StomaPos.y) { burst = true; Fx.Burst(W(hp), Palette.Oxygen, 30, 0.06f, 1.2f, 1f); }
                // once the hero drops into the substomatal cavity, settle below the leaf looking up at the pore: cuticle, guard cells
                // and the molecules streaming out stay in frame (matches the LeafSurface stoma shot that follows)
                float w = MicroKitB.Smooth01((StomaPos.y + 0.4f - hp.y) / 2f);   // 0 in the cavity → 1 about 2 m below it: blended hand-over, no jolt
                Vector3 want = Vector3.Lerp(W(hp + new Vector3(-0.65f, 0.6f, -2.35f)), W(StomaPos + new Vector3(-2f, -2.2f, -3.6f)), w);
                Vector3 look = Vector3.Lerp(W(hp), W(StomaPos + Vector3.down * 1.6f), w);
                MicroKitB.Chase(cam, want, look, Mathf.Lerp(Mathf.Lerp(2f, 6f, t * 3f), 3f, w));
            }, Ease.Linear);
        }

        /// Glucose leaves the cell into a vein's phloem and away ("case closed? of course not").
        public IEnumerator FoodToPhloem(CameraDirector cam)
        {
            if (!ready) yield break;
            MicroKitB.Kill(transient);
            phloemExt.SetActive(true);
            var f = foodCell; Vector3 tube = new Vector3(PhloemMain.x, PhloemMain.y, 0);
            var path = new List<Vector3> { new Vector3(f.x, 1.25f, -2.62f), new Vector3(f.x + 0.12f, 0.55f, -3.15f), new Vector3(-3.15f, -0.3f, -3.35f),
                new Vector3(-3.6f, -1.35f, -3.45f), new Vector3(tube.x, tube.y, -3.5f), new Vector3(tube.x, tube.y, -2.6f) };
            var g = Spawn(Mol.Glucose, path[0], 0.08f, glowSugar, 0.5f, trailSugar, 0.06f);
            var gt = g.transform;
            if (cam) cam.StopOrbit();
            // 1 · out of the cell, along the cut face to the phloem (camera alongside)
            yield return Fx.Tween(2.4f, t =>
            {
                var p = Fx.CatmullRom(path, Fx.Eval(t, Ease.InOut));
                gt.localPosition = p; gt.localScale = Vector3.one * Mathf.Lerp(1.8f, 1.1f, MicroKitB.Smooth01((t - 0.6f) / 0.4f));
                gt.localRotation = Quaternion.Euler(0, t * 90f, t * 120f);
                MicroKitB.Chase(cam, W(p + new Vector3(0.6f, 0.35f, -2.0f)), W(p), Mathf.Lerp(2.5f, 6f, t * 2f));
            }, Ease.Linear);
            // 2 · camera slides in behind it, onto the tube axis
            Vector3 c0 = cam ? cam.transform.position : Vector3.zero; Quaternion r0 = cam ? cam.transform.rotation : Quaternion.identity;
            yield return Fx.Tween(0.8f, t =>
            {
                float gz = Mathf.Lerp(-2.6f, -2.2f, t);
                gt.localPosition = new Vector3(tube.x, tube.y, gz); gt.localScale = Vector3.one * Mathf.Lerp(1.1f, 1f, t);
                if (!cam) return;
                // beside the translucent phloem (not inside it: stacked transparent walls read as a flat orange screen)
                var want = W(new Vector3(tube.x + 0.85f, tube.y + 0.42f, gz - 1.25f));
                cam.transform.SetPositionAndRotation(Vector3.Lerp(c0, want, t), Quaternion.Slerp(r0, Quaternion.LookRotation(gt.position - want, transform.up), t));
            }, Ease.InOut);
            // 3 · it speeds away along the phloem; the camera stays by the tissue and pans after it (riding along
            //     only shows the bare tube against the void — the leaf must stay in frame as the point of departure)
            Vector3 c2 = cam ? cam.transform.position : Vector3.zero;
            yield return Fx.Tween(2.6f, t =>
            {
                float gz = Mathf.Lerp(-2.2f, 6f, Fx.Eval(t, Ease.InOut));   // it leaves along the phloem and stays in frame
                gt.localPosition = new Vector3(tube.x + Mathf.Sin(t * 9f) * 0.02f, tube.y + Mathf.Cos(t * 7f) * 0.02f, gz);
                gt.localRotation = Quaternion.Euler(t * 140f, 0, t * 300f);
                if (cam)
                {
                    var cp = Vector3.Lerp(c2, W(new Vector3(tube.x + 2.5f, tube.y + 0.6f, -5f + 1.5f * Fx.Eval(t, Ease.InOut))), MicroKitB.Smooth01(t / 0.35f));   // eases out to beside the cut face: no translucent cells in front of the lens
                    var look = Vector3.Lerp(gt.position, W(new Vector3(tube.x, tube.y, 6f)), Mathf.Clamp01((t - 0.55f) / 0.45f));
                    cam.transform.SetPositionAndRotation(cp, Quaternion.LookRotation(look - cp, transform.up));
                }
            }, Ease.Linear);
        }
    }
}
