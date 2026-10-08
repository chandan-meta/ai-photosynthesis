using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Photosynthesis
{
    /// The hidden world inside the leaf for the Scene 3 prologue: the client's leaf cross-section model (G7 ZIP → Resources/Models/LeafInside)
    /// re-materialled — translucent cell layers, softly lit chloroplasts, xylem / phloem pipes — with sun shafts, air motes and a camera glide.
    /// ponytail: a vista, not a lesson set. Story cells and routes stay in LeafTissueWorld; this world only has to look like a living place.
    public class LeafInteriorWorld : MicroWorld
    {
        const float Length = 34f;   // long axis of the block in metres: the camera flies between the cells
        bool ready; Bounds b; Material chlMat; readonly Color chlE = new Color(0.06f, 0.42f, 0.1f); float pulse, pulseGoal;

        protected override void Build()
        {
            var prefab = Resources.Load<GameObject>("Models/LeafInside");
            MicroKitB.Backdrop(transform, 56f, new Color(0.24f, 0.44f, 0.33f), new Color(0.12f, 0.25f, 0.2f));
            if (!prefab) { Debug.LogWarning("[Photosynthesis] Resources/Models/LeafInside missing — the prologue interior is empty."); b = new Bounds(Origin, Vector3.one * 20f); ready = true; return; }
            var model = Instantiate(prefab, transform).transform; model.name = "LeafInside";
            foreach (var a in model.GetComponentsInChildren<Animation>(true)) Destroy(a);
            foreach (var a in model.GetComponentsInChildren<Animator>(true)) Destroy(a);
            var rs = model.GetComponentsInChildren<Renderer>(true);
            // the block itself (cells, layers, vein); the two "_Render" planes are decorative sheets that stick out of it and would stretch the glide
            var block = System.Array.FindAll(rs, r => !r.name.Contains("Render")); if (block.Length == 0) block = rs;
            Bounds All() { var r0 = block[0].bounds; foreach (var r in block) r0.Encapsulate(r.bounds); return r0; }
            // normalise: long axis along x, ~Length m, centred on the origin (the file is a centimetre-scale Maya block)
            b = All();
            if (b.size.z >= b.size.x && b.size.z >= b.size.y) model.localRotation = Quaternion.Euler(0f, 90f, 0f);
            model.localScale = Vector3.one * (Length / Mathf.Max(0.01f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z))));
            b = All(); model.position += transform.position - b.center; b = All();

            // materials by part name: the file's own textures are flat noise, so the look comes from translucency, emission and the light
            var epi = Fx.Mat(MatKind.Glass, new Color(0.9f, 1f, 0.78f, 0.3f), new Color(0.04f, 0.06f, 0.02f), 0.96f);
            var pal = Fx.Mat(MatKind.LitTransparent, new Color(0.42f, 0.86f, 0.4f, 0.42f), new Color(0.02f, 0.09f, 0.02f), 0.72f);
            var spongy = Fx.Mat(MatKind.LitTransparent, new Color(0.58f, 0.88f, 0.48f, 0.38f), new Color(0.02f, 0.07f, 0.02f), 0.65f);
            var sheath = Fx.Mat(MatKind.LitTransparent, new Color(0.84f, 0.9f, 0.5f, 0.4f), new Color(0.05f, 0.06f, 0.01f), 0.6f);
            var xylem = Fx.Mat(MatKind.LitTransparent, new Color(Palette.Xylem.r, Palette.Xylem.g, Palette.Xylem.b, 0.5f), Palette.Xylem * 0.25f, 0.8f);
            var phloem = Fx.Mat(MatKind.LitTransparent, new Color(1f, 0.64f, 0.28f, 0.5f), Palette.Phloem * 0.25f, 0.8f);
            var core = Fx.Mat(MatKind.LitTransparent, new Color(0.7f, 0.55f, 0.9f, 0.45f), new Color(0.12f, 0.06f, 0.2f), 0.7f);
            var guard = Fx.Mat(MatKind.LitTransparent, new Color(0.4f, 0.82f, 0.36f, 0.6f), new Color(0.03f, 0.12f, 0.03f), 0.7f);
            var water = Fx.Shared(MatKind.LitEmissive, Palette.Water, Palette.Water * 1.6f);
            var palisade = new List<Renderer>();
            foreach (var r in rs)
            {
                string n = r.name.ToLowerInvariant(); Material m = spongy;
                if (n.Contains("upper_layer") || n.Contains("polysurface")) m = epi;
                else if (n.Contains("green_brick")) { m = pal; palisade.Add(r); }
                else if (n.Contains("box_pipe")) m = xylem;
                else if (n.Contains("red_pipe")) m = phloem;
                else if (n.Contains("yellow_rod")) m = sheath;
                else if (n.Contains("center_purple")) m = core;
                else if (n.Contains("stomata")) m = guard;
                else if (Regex.IsMatch(r.name, @"^(A|B|C|D|AA)_\d")) m = water;
                r.sharedMaterials = System.Array.ConvertAll(r.sharedMaterials, _ => m);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            }
            // chloroplasts: softly lit lentils scattered through the palisade cells (one instanced batch); the whole block if the names changed
            chlMat = Fx.Mat(MatKind.LitEmissive, new Color(0.2f, 0.62f, 0.22f), chlE, 0.6f);
            var chl = new MicroKitB.Batch(); var lentil = MicroKitB.Sphere(5, 9); var rnd = new System.Random(7);
            float R(float a, float c) => a + (float)rnd.NextDouble() * (c - a);
            var cells = palisade.Count > 0 ? palisade : new List<Renderer>(rs);
            for (int i = 0; i < 560; i++)
            {
                var cb = cells[rnd.Next(cells.Count)].bounds;
                var p = new Vector3(R(cb.min.x, cb.max.x), R(cb.min.y, cb.max.y), R(cb.min.z, cb.max.z));
                chl.Add(lentil, transform.InverseTransformPoint(p), Quaternion.Euler(R(0, 360), R(0, 360), R(0, 360)), new Vector3(0.34f, 0.52f, 0.2f) * R(0.8f, 1.2f));
            }
            chl.Make("Chloroplasts", transform, chlMat);
            // atmosphere: sun shafts from above, motes drifting in the air spaces
            var shaft = Fx.Mat(MatKind.Additive, new Color(1f, 0.86f, 0.5f, 0.16f)); Fx.SetTexture(shaft, MicroKitB.BeamTex);
            for (int i = 0; i < 5; i++)
            {
                float x = Mathf.Lerp(b.min.x, b.max.x, (i + 0.5f) / 5f) + R(-1.5f, 1.5f);
                MicroKitB.Beam(transform, transform.InverseTransformPoint(new Vector3(x, b.max.y + 14f, R(-2f, 2f))), transform.InverseTransformPoint(new Vector3(x + 2.5f, b.min.y, R(-1f, 1f))), R(2.5f, 4f), shaft);
            }
            var motes = Fx.Particles("AirMotes", transform, new Fx.PCfg
            {
                color = new Color(0.75f, 0.95f, 1f, 0.35f), color2 = new Color(1f, 0.95f, 0.75f, 0.25f), size = 0.06f, life = 7f, speed = 0.05f,
                rate = 30f, max = 300, shape = ParticleSystemShapeType.Box, noise = 0.12f
            });
            var sh = motes.shape; sh.scale = b.size * 1.1f; motes.transform.position = b.center;
            ready = true;
        }

        /// SHOT 05: over the upper surface at one end, then down past the cut face and along it toward the vein. The lens stays outside
        /// the slab (z ≤ -1.5 hz): inside the translucent layers everything reads as one green blur.
        public IEnumerator Glide(CameraDirector cam)
        {
            if (!ready || !cam) yield break;
            Vector3 c = b.center; float hx = b.extents.x, hy = b.extents.y, hz = b.extents.z;
            var pos = new List<Vector3>
            {
                c + new Vector3(-hx * 0.6f, hy + 7f, -hz * 2.8f), c + new Vector3(-hx * 0.3f, hy + 2.2f, -hz * 2.1f), c + new Vector3(hx * 0.05f, hy * 0.5f, -hz * 1.75f),
                c + new Vector3(hx * 0.35f, -hy * 0.05f, -hz * 1.9f), c + new Vector3(hx * 0.5f, -hy * 0.1f, -hz * 2.1f)
            };
            var look = new List<Vector3>
            {
                c + new Vector3(-hx * 0.35f, hy * 0.5f, 0f), c + new Vector3(-hx * 0.05f, hy * 0.2f, 0f), c + new Vector3(hx * 0.25f, 0f, hz * 0.1f),
                c + new Vector3(hx * 0.6f, -hy * 0.1f, hz * 0.2f), c + new Vector3(hx * 0.8f, -hy * 0.15f, hz * 0.3f)
            };
            yield return MicroKitB.FlyCam(cam, pos, look, 7.5f, 44f);
        }

        /// "…food is being made.": the chloroplasts light up.
        public void Pulse() => pulseGoal = 1f;

        public override (Vector3 pos, Vector3 look) Pose() =>
            (b.center + new Vector3(-b.extents.x * 0.6f, b.extents.y + 7f, -b.extents.z * 2.8f), b.center + new Vector3(-b.extents.x * 0.35f, b.extents.y * 0.5f, 0f));

        public override void ResetWorld() { pulse = pulseGoal = 0f; if (chlMat) Fx.SetEmission(chlMat, chlE); }

        protected override void ApplyMood() =>
            MicroKitB.Mood(new Color(0.16f, 0.33f, 0.26f), 0.014f, new Color(0.55f, 0.72f, 0.56f), new Color(0.22f, 0.36f, 0.28f), new Color(0.05f, 0.1f, 0.08f),
                new Color(1f, 0.92f, 0.74f), 1.8f, new Vector3(62f, -28f, 0f));

        void Update()
        {
            if (!ready || !chlMat) return;
            pulse = Mathf.MoveTowards(pulse, pulseGoal, Time.deltaTime * 0.9f);
            Fx.SetEmission(chlMat, Color.Lerp(chlE, new Color(0.25f, 1.7f, 0.35f), pulse) * (0.85f + 0.15f * Mathf.Sin(Time.time * 1.6f)));
        }
    }
}
