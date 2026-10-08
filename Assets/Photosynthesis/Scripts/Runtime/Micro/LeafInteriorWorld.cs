using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Photosynthesis
{
    /// The hidden world inside the leaf for the Scene 3 prologue: the client's leaf cross-section model (G7 ZIP → Resources/Models/LeafInside)
    /// re-materialled — translucent cell layers, softly lit chloroplasts inside the cells, xylem / phloem pipes — repeated either side so the
    /// leaf runs on into green fog, with sun shafts, air motes and a camera glide.
    /// ponytail: a vista, not a lesson set. Story cells and routes stay in LeafTissueWorld; this world only has to look like a living place.
    public class LeafInteriorWorld : MicroWorld
    {
        const float Length = 34f;   // long axis of one block in metres: the camera flies along the cut face
        bool ready; Bounds b; Material chlMat; readonly Color chlE = new Color(0.06f, 0.42f, 0.1f); float pulse, pulseGoal;

        protected override void Build()
        {
            var prefab = Resources.Load<GameObject>("Models/LeafInside");
            MicroKitB.Backdrop(transform, 70f, new Color(0.24f, 0.44f, 0.33f), new Color(0.12f, 0.25f, 0.2f));
            if (!prefab) { Debug.LogWarning("[Photosynthesis] Resources/Models/LeafInside missing — the prologue interior is empty."); b = new Bounds(Origin, Vector3.one * 20f); ready = true; return; }
            var slab = Fx.Node("Slab", transform).transform;
            var model = Instantiate(prefab, slab).transform; model.name = "LeafInside";
            foreach (var a in model.GetComponentsInChildren<Animation>(true)) Destroy(a);
            foreach (var a in model.GetComponentsInChildren<Animator>(true)) Destroy(a);
            var rs = model.GetComponentsInChildren<Renderer>(true);
            // the block itself (cells, layers, vein); the two "_Render" planes are decorative sheets that stick out of it and would stretch the glide
            var core = System.Array.FindAll(rs, r => !r.name.Contains("Render")); if (core.Length == 0) core = rs;
            Bounds All() { var r0 = core[0].bounds; foreach (var r in core) r0.Encapsulate(r.bounds); return r0; }
            // normalise: long axis along x, ~Length m, centred on the origin (the file is a centimetre-scale Maya block)
            b = All();
            if (b.size.z >= b.size.x && b.size.z >= b.size.y) model.localRotation = Quaternion.Euler(0f, 90f, 0f);
            model.localScale = Vector3.one * (Length / Mathf.Max(0.01f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z))));
            b = All(); model.position += transform.position - b.center; b = All();

            // materials by part name: the file's own textures are flat noise, so the look comes from translucency, emission and the light
            var epi = Fx.Mat(MatKind.Glass, new Color(0.84f, 1f, 0.76f, 0.18f), new Color(0.04f, 0.07f, 0.02f), 0.96f);   // thin, green-tinted cuticle: not frosted plastic
            var pal = Fx.Mat(MatKind.LitTransparent, new Color(0.42f, 0.86f, 0.4f, 0.42f), new Color(0.02f, 0.09f, 0.02f), 0.72f);
            var spongy = Fx.Mat(MatKind.LitTransparent, new Color(0.58f, 0.88f, 0.48f, 0.38f), new Color(0.02f, 0.07f, 0.02f), 0.65f);
            var sheath = Fx.Mat(MatKind.LitTransparent, new Color(0.84f, 0.9f, 0.5f, 0.4f), new Color(0.05f, 0.06f, 0.01f), 0.6f);
            var xylem = Fx.Mat(MatKind.LitTransparent, new Color(Palette.Xylem.r, Palette.Xylem.g, Palette.Xylem.b, 0.5f), Palette.Xylem * 0.25f, 0.8f);
            var phloem = Fx.Mat(MatKind.LitTransparent, new Color(1f, 0.64f, 0.28f, 0.5f), Palette.Phloem * 0.25f, 0.8f);
            var vein = Fx.Mat(MatKind.LitTransparent, new Color(0.7f, 0.55f, 0.9f, 0.45f), new Color(0.12f, 0.06f, 0.2f), 0.7f);
            var guard = Fx.Mat(MatKind.LitTransparent, new Color(0.4f, 0.82f, 0.36f, 0.6f), new Color(0.03f, 0.12f, 0.03f), 0.7f);
            var water = Fx.Shared(MatKind.LitEmissive, Palette.Water, Palette.Water * 1.6f);
            var palHosts = new List<Renderer>(); var spongyHosts = new List<Renderer>();
            foreach (var r in rs)
            {
                string n = r.name.ToLowerInvariant(); Material m = spongy;
                if (n.Contains("upper_layer") || n.Contains("polysurface")) m = epi;
                else if (n.Contains("green_brick")) { m = pal; palHosts.Add(r); }
                else if (n.Contains("box_pipe")) m = xylem;
                else if (n.Contains("red_pipe")) m = phloem;
                else if (n.Contains("yellow_rod")) m = sheath;
                else if (n.Contains("center_purple")) m = vein;
                else if (n.Contains("stomata")) m = guard;
                else if (Regex.IsMatch(r.name, @"^(A|B|C|D|AA)_\d")) m = water;
                else if (n.Contains("loosely_arranged_cell") && !n.Contains("render")) spongyHosts.Add(r);
                r.sharedMaterials = System.Array.ConvertAll(r.sharedMaterials, _ => m);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            }

            // chloroplasts sit inside the cells: a random point of a palisade (70 %) or spongy cell surface, pushed inward along its normal
            chlMat = Fx.Mat(MatKind.LitEmissive, new Color(0.2f, 0.62f, 0.22f), chlE, 0.6f);
            var chl = new MicroKitB.Batch(); var lentil = MicroKitB.Sphere(6, 10); var rnd = new System.Random(7);
            float R(float a, float c) => a + (float)rnd.NextDouble() * (c - a);
            var surf = new Dictionary<Renderer, (Vector3[] v, Vector3[] n)>();
            (Vector3[] v, Vector3[] n) Surface(Renderer r)
            {
                if (surf.TryGetValue(r, out var s)) return s;
                var mf = r.GetComponent<MeshFilter>(); var mesh = mf ? mf.sharedMesh : null;
                s = mesh && mesh.isReadable && mesh.vertexCount > 0 ? (mesh.vertices, mesh.normals) : (null, null);
                surf[r] = s; return s;
            }
            for (int i = 0; i < 560; i++)
            {
                var hosts = rnd.NextDouble() < 0.7 && palHosts.Count > 0 ? palHosts : spongyHosts.Count > 0 ? spongyHosts : palHosts;
                Vector3 p;
                var host = hosts.Count > 0 ? hosts[rnd.Next(hosts.Count)] : null;
                var (v, nrm) = host ? Surface(host) : (null, null);
                if (v != null)
                {
                    int k = rnd.Next(v.Length); var t = host.transform;
                    Vector3 inward = nrm != null && nrm.Length == v.Length ? -t.TransformDirection(nrm[k]).normalized : Vector3.zero;
                    p = t.TransformPoint(v[k]) + inward * R(0.12f, 0.35f);
                }
                else
                {   // mesh not readable (import settings): fall back to the cell bounds
                    var cb = host ? host.bounds : b;
                    p = new Vector3(R(cb.min.x, cb.max.x), R(cb.min.y, cb.max.y), R(cb.min.z, cb.max.z));
                }
                chl.Add(lentil, slab.InverseTransformPoint(p), Quaternion.Euler(R(0, 360), R(0, 360), R(0, 360)), new Vector3(0.3f, 0.46f, 0.18f) * R(0.8f, 1.2f));
            }
            chl.Make("Chloroplasts", slab, chlMat);

            // the leaf runs on: a copy either side, dissolving into the fog, so the block never reads as a specimen in a void
            foreach (float side in new[] { -1f, 1f })
                Instantiate(slab.gameObject, slab.position + Vector3.right * side * b.size.x * 0.995f, slab.rotation, transform).name = "Slab (continues)";

            // atmosphere: sun shafts from above, motes drifting in the air spaces along the whole run
            var shaft = Fx.Mat(MatKind.Additive, new Color(1f, 0.86f, 0.5f, 0.16f)); Fx.SetTexture(shaft, MicroKitB.BeamTex);
            for (int i = 0; i < 7; i++)
            {
                float x = Mathf.Lerp(b.min.x - b.size.x * 0.4f, b.max.x + b.size.x * 0.4f, (i + 0.5f) / 7f) + R(-1.5f, 1.5f);
                MicroKitB.Beam(transform, transform.InverseTransformPoint(new Vector3(x, b.max.y + 14f, R(-2f, 2f))), transform.InverseTransformPoint(new Vector3(x + 2.5f, b.min.y, R(-1f, 1f))), R(2.5f, 4f), shaft);
            }
            var motes = Fx.Particles("AirMotes", transform, new Fx.PCfg
            {
                color = new Color(0.75f, 0.95f, 1f, 0.35f), color2 = new Color(1f, 0.95f, 0.75f, 0.25f), size = 0.06f, life = 7f, speed = 0.05f,
                rate = 50f, max = 500, shape = ParticleSystemShapeType.Box, noise = 0.12f
            });
            var sh = motes.shape; sh.scale = new Vector3(b.size.x * 2.2f, b.size.y * 1.1f, b.size.z * 1.8f); motes.transform.position = b.center;
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

        protected override void ApplyMood() =>   // denser fog than the other worlds: the repeated slabs fade out instead of ending
            MicroKitB.Mood(new Color(0.16f, 0.33f, 0.26f), 0.026f, new Color(0.55f, 0.72f, 0.56f), new Color(0.22f, 0.36f, 0.28f), new Color(0.05f, 0.1f, 0.08f),
                new Color(1f, 0.92f, 0.74f), 1.8f, new Vector3(62f, -28f, 0f));

        void Update()
        {
            if (!ready || !chlMat) return;
            pulse = Mathf.MoveTowards(pulse, pulseGoal, Time.deltaTime * 0.9f);
            Fx.SetEmission(chlMat, Color.Lerp(chlE, new Color(0.25f, 1.7f, 0.35f), pulse) * (0.85f + 0.15f * Mathf.Sin(Time.time * 1.6f)));
        }
    }
}
