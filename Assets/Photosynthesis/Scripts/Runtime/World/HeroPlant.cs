using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Photosynthesis
{
    /// Tag on every selectable plant collider (leaves, stem, roots…).
    public class PlantPartTag : MonoBehaviour { public PlantPart part; public int leaf = -1; }

    /// The enormous central plant (Alocasia-like: big heart-shaped leaves on long petioles from a central stem,
    /// corm/storage tuber + roots below, one flower spathe and a berry cluster). Fully procedural so every
    /// pathway (xylem, phloem, stomata, roots) is known exactly.
    /// Files: HeroPlant.cs (API + per-frame animation), PlantBuild.cs (geometry/materials), PlantFlows.cs (flows,
    /// vessels' beads, molecules, particles, energy wave, signal), PlantLeafShape.cs (blade maths + vein texture), PlantMesh.cs.
    /// Hierarchy: Hero Plant (anchor, y = soil) → Plant (spin pivot) → Stem · Soil · Underground · Leaf i (petiole base, sways) → Blade.
    public partial class HeroPlant : MonoBehaviour
    {
        public static HeroPlant Create(Transform anchor)
        {
            var g = new GameObject("Hero Plant"); g.transform.SetParent(anchor, false);
            var p = g.AddComponent<HeroPlant>(); p.EnsureBuilt(); return p;
        }

        // ================================================================ anatomy queries
        public int LeafCount { get { EnsureBuilt(); return leaves.Count; } }
        /// Leaf used by the investigation (low, front-facing, near the learner).
        public int FocusLeaf => 0;
        public Transform Leaf(int i) => Lf(i).blade;
        public Vector3 LeafCenter(int i) { var l = Lf(i); return l.blade.TransformPoint(l.center); }
        /// World point just under the blade where stomata are (camera target for "underside").
        public Vector3 LeafUnderside(int i) { var l = Lf(i); return l.blade.TransformPoint(l.underside); }
        public Vector3 LeafNormal(int i) { var l = Lf(i); return l.blade.TransformDirection(l.centerN).normalized; }
        public Vector3 Base => transform.position;
        public Vector3 Top { get { EnsureBuilt(); return transform.TransformPoint(0, topHeight, 0); } }
        public Vector3 RootTip => P(rootTip);
        public Vector3 Shoot => P(shootTip);
        public Vector3 Flower => P(flowerPos);
        public Vector3 Fruit => P(fruitPos);
        public Vector3 Storage => P(cormCenter);

        /// World polyline root tip → corm → stem → petiole → leaf midrib (follows the live geometry).
        public List<Vector3> XylemPath(int leaf)
        {
            var lf = Lf(leaf); var o = new List<Vector3>(64);
            var r = mainRoots[0]; for (int i = r.Count - 1; i >= 0; i--) o.Add(pivot.TransformPoint(r[i]));
            o.Add(pivot.TransformPoint(cormCenter));
            AddStem(o, 0f, lf.def.a);
            foreach (var p in lf.petiole) o.Add(lf.root.TransformPoint(p));
            for (int k = 1; k <= 10; k++) o.Add(lf.blade.TransformPoint(lf.shape.Pos(0.75f * k / 10f, 0)));
            return o;
        }

        /// World polyline leaf blade → target ("Roots","Shoot","Flower","Fruit","Storage").
        public List<Vector3> PhloemPath(int leaf, string target)
        {
            var lf = Lf(leaf); var o = new List<Vector3>(64);
            for (int k = 10; k >= 0; k--) o.Add(lf.blade.TransformPoint(lf.shape.Pos(0.45f * k / 10f, 0)));
            for (int i = lf.petiole.Count - 1; i >= 0; i--) o.Add(lf.root.TransformPoint(lf.petiole[i]));
            float a = lf.def.a;
            switch (target)
            {
                case "Roots": AddStem(o, a, 0f); o.Add(Storage); foreach (var p in mainRoots[0]) o.Add(pivot.TransformPoint(p)); break;
                case "Shoot": AddStem(o, a, 1.3f); foreach (var p in shootPts) o.Add(pivot.TransformPoint(p)); break;
                case "Flower": AddStem(o, a, FlowerY); foreach (var p in flowerPed) o.Add(pivot.TransformPoint(p)); o.Add(Flower); break;
                case "Fruit": AddStem(o, a, FruitY); foreach (var p in fruitPed) o.Add(pivot.TransformPoint(p)); o.Add(Fruit); break;
                case "Storage": AddStem(o, a, 0f); o.Add(Storage); break;
                default: AddStem(o, a, 0f); break;
            }
            return o;
        }

        void AddStem(List<Vector3> o, float y0, float y1)
        {
            int n = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(y1 - y0) / 0.15f));
            for (int i = 0; i <= n; i++) o.Add(pivot.TransformPoint(StemAt(Mathf.Lerp(y0, y1, i / (float)n))));
        }

        // ================================================================ growth
        /// Formation reveal: stem rises from the platform, petioles extend, leaves unfurl one after another.
        public IEnumerator Grow(float duration)
        {
            EnsureBuilt(); int tok = ++growTok;
            ApplyGrowth(0.0001f);
            for (int i = 0; i < 36; i++) Emit(Glints, transform.position + Random.insideUnitSphere * 0.25f + Vector3.up * 0.1f, Random.insideUnitSphere * 0.6f + Vector3.up * 0.9f, Random.Range(0.04f, 0.09f), Random.Range(0.6f, 1.2f), Color.Lerp(Palette.Gold, Palette.GreenBright, Random.value * 0.5f));
            yield return Fx.Tween(duration, t => { if (tok == growTok) { ApplyGrowth(t); GrowSparkles(); } }, Ease.Linear);
            if (tok == growTok) ApplyGrowth(1f);
        }

        public void SetGrown(bool grown) { EnsureBuilt(); growTok++; ApplyGrowth(grown ? 1f : 0f); }

        void ApplyGrowth(float g)
        {
            bool wasFull = growth >= 1f; growth = g;
            bool vis = g > 0f; if (pivot.gameObject.activeSelf != vis) pivot.gameObject.SetActive(vis);
            if (vis)
            {
                float sg = Fx.Eval(Mathf.Clamp01(g / 0.32f), Ease.Out), sxz = Mathf.Lerp(0.35f, 1f, sg), sy = Mathf.Max(0.002f, sg);
                stemNode.localScale = new Vector3(sxz, sy, sxz);
                underground.localScale = new Vector3(sxz, sy, sxz);
                soilNode.localScale = Vector3.one * Mathf.Lerp(0.4f, 1f, Fx.Eval(Mathf.Clamp01(g / 0.15f), Ease.Out));
                for (int k = 0; k < growOrder.Length; k++)
                {
                    var lf = leaves[growOrder[k]];
                    float w0 = 0.14f + 0.52f * k / Mathf.Max(1, growOrder.Length - 1);
                    float lg = Mathf.Clamp01((g - w0) / 0.32f); lf.lg = lg;
                    bool act = lg > 0f; if (lf.root.gameObject.activeSelf != act) lf.root.gameObject.SetActive(act);
                    if (!act) continue;
                    lf.root.localPosition = new Vector3(lf.attach.x * sxz, lf.attach.y * sy, lf.attach.z * sxz);
                    lf.root.localScale = Vector3.one * Mathf.Max(0.002f, Fx.Eval(Mathf.Clamp01(lg / 0.55f), Ease.Out));
                    float uf = Mathf.Clamp01((lg - 0.3f) / 0.7f), ue = Fx.Eval(uf, Ease.Out), ub = Fx.Eval(uf, Ease.Back);
                    lf.blade.localScale = new Vector3(Mathf.LerpUnclamped(0.07f, 1f, ub), 1f, Mathf.Lerp(0.55f, 1f, ue));   // rolled spike → open blade
                    lf.blade.localRotation = lf.bladeRest * Quaternion.Euler(Mathf.Lerp(-80f, 0f, ue), 0, 0);              // points up, then drops into place
                    lf.growGlow = lg < 1f ? Mathf.Sin(Mathf.PI * lg) * 0.8f : 0f;
                }
                GrowPart(shootNode, g, 0.62f); GrowPart(flowerNode, g, 0.76f); GrowPart(fruitNode, g, 0.82f);
            }
            if ((g >= 1f) != wasFull) RefreshFlows();
        }

        static void GrowPart(Transform t, float g, float start)
        {
            float e = Fx.Eval(Mathf.Clamp01((g - start) / (1f - start)), Ease.Out);
            bool a = e > 0f; if (t.gameObject.activeSelf != a) t.gameObject.SetActive(a);
            t.localScale = new Vector3(Mathf.Lerp(0.4f, 1f, e), Mathf.Max(0.002f, e), Mathf.Lerp(0.4f, 1f, e));
        }

        void GrowSparkles()
        {
            foreach (var lf in leaves)
                if (lf.lg > 0f && lf.lg < 1f && Random.value < 0.5f)
                    Emit(Glints, lf.blade.position + Random.insideUnitSphere * 0.25f, Vector3.up * Random.Range(0.1f, 0.4f), Random.Range(0.03f, 0.07f), Random.Range(0.5f, 0.9f), Color.Lerp(Palette.Gold, Palette.GreenBright, Random.value));
        }

        // ================================================================ look
        /// 0 solid → 1 ghost. Swaps to transparent materials while > 0.
        public IEnumerator SetTransparency(float amount, float duration)
        {
            EnsureBuilt(); int tok = ++transpTok; float from = transp; amount = Mathf.Clamp01(amount);
            if (Mathf.Abs(from - amount) < 0.001f) { SetTransp(amount); yield break; }
            yield return Fx.Tween(duration, t => { if (tok == transpTok) SetTransp(Mathf.Lerp(from, amount, t)); });
        }

        void SetTransp(float a)
        {
            transp = a; bool tr = a > 0.001f;
            if (tr != transMode)
            {
                transMode = tr;
                foreach (var l in looks)
                {
                    l.r.sharedMaterials = tr ? l.t : l.o;
                    l.r.shadowCastingMode = !tr && l.shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
            ApplyLook();
        }

        /// Colours + alpha of every plant material (dormant tint, ghost alpha).
        void ApplyLook()
        {
            float alpha = Mathf.Lerp(1f, 0.1f, transp);
            // Base colour can only multiply the texture, so desaturate by lifting R/B against G (a pure darkening tint
            // keeps the green just as saturated): deep green → dull olive, petioles → straw.
            Color dull = Color.Lerp(Color.white, new Color(1.25f, 0.85f, 0.95f), dormantW);
            foreach (var p in pairs)
            {
                Color c = p.leafy ? p.c * dull : p.c; c.a = 1f; Fx.SetColor(p.o, c);
                c.a = alpha; Fx.SetColor(p.t, c);
            }
        }

        public void ShowRoots(bool on)
        {
            EnsureBuilt(); rootsShown = on;
            underground.gameObject.SetActive(on);
            soilR.sharedMaterials = on ? soilGhost : soilSolid;
        }

        public void AllFlows(bool on) { foreach (Flow f in System.Enum.GetValues(typeof(Flow))) SetFlow(f, on); }

        /// Static glowing vessel networks (blue xylem, orange phloem) in roots, stem, petioles, midribs and veins.
        public void SetVessels(bool xylem, bool phloem) { EnsureBuilt(); vesX = xylem; vesP = phloem; RefreshVessels(); }

        void RefreshVessels()
        {
            bool x = vesX || hlXylem, p = vesP || hlPhloem;
            if (x == xylemVis && p == phloemVis) return;
            xylemVis = x; phloemVis = p;
            foreach (var g in xylemObjs) g.SetActive(x);
            foreach (var g in phloemObjs) g.SetActive(p);
        }

        public void HighlightLeaf(int i, bool on)
        {
            var lf = Lf(i); lf.hl = on;
            if (on) Rim(lf).SetActive(true); else if (lf.rim) lf.rim.SetActive(false);
        }

        public void Highlight(PlantPart p, bool on)
        {
            EnsureBuilt();
            switch (p)
            {
                case PlantPart.WholePlant: for (int i = 0; i < leaves.Count; i++) HighlightLeaf(i, on); hlStem = on; break;
                case PlantPart.Roots: hlRoots = on; break;
                case PlantPart.Stem: hlStem = on; break;
                case PlantPart.Xylem: hlXylem = on; RefreshVessels(); break;
                case PlantPart.Phloem: hlPhloem = on; RefreshVessels(); break;
                default: HighlightLeaf(FocusLeaf, on); break; // Leaf, Stomata, Cell, Chloroplast all live in the focus leaf
            }
        }

        /// Desaturated, slightly drooping, flows suppressed (factory won't start).
        public void SetDormant(bool on) { EnsureBuilt(); dormant = on; RefreshFlows(); }

        /// Gentle turntable rotation (deg/s). 0 eases the plant back to its home orientation (focus leaf facing the learner).
        public void Spin(float degPerSec)
        {
            EnsureBuilt();
            if (degPerSec == 0f && spinSpeed != 0f) spinVel = spinSpeed;
            spinSpeed = degPerSec;
        }

        public void ResetVisuals()
        {
            EnsureBuilt();
            for (int i = 0; i < flowReq.Length; i++) flowReq[i] = false;
            dormant = false; dormantW = 0f; RefreshFlows();
            vesX = vesP = hlStem = hlRoots = hlXylem = hlPhloem = false; RefreshVessels();
            foreach (var lf in leaves) { lf.hl = false; lf.flash = 0f; if (lf.rim) lf.rim.SetActive(false); }
            SetIngredientsIdle(false); Signal(Vector3.zero, Color.white, false); StopWave(); DestroyClones();
            for (int i = 0; i < gT.Length; i++) gT[i] = 0f;
            if (glintPS) glintPS.Clear();
            spinSpeed = spinAngle = spinVel = 0f; pivot.localRotation = Quaternion.identity;
            transpTok++; SetTransp(0f);
            ShowRoots(false);
        }

        // ================================================================ per frame
        void Update()
        {
            if (!built) return;
            float dt = Time.deltaTime, t = Time.time;
            UpdateSpin(dt);
            if (dormantW != (dormant ? 1f : 0f)) { dormantW = Mathf.MoveTowards(dormantW, dormant ? 1f : 0f, dt / (dormant ? 0.8f : 2f)); ApplyLook(); }
            if (pivot.gameObject.activeSelf) UpdateSway(t);
            UpdateEmission(t);
            UpdateFlows(dt, t);
            UpdateWave();
            UpdateSignal(t);
            UpdateIngredients(dt, t);
        }

        void UpdateSpin(float dt)
        {
            if (spinSpeed != 0f) spinAngle = Mathf.Repeat(spinAngle + spinSpeed * dt, 360f);
            else if (spinAngle != 0f || spinVel != 0f)
            {
                spinAngle = Mathf.SmoothDampAngle(spinAngle, 0f, ref spinVel, 0.9f, 90f, dt);
                if (Mathf.Abs(Mathf.DeltaAngle(spinAngle, 0f)) < 0.02f && Mathf.Abs(spinVel) < 0.05f) spinAngle = spinVel = 0f;
            }
            else return;
            pivot.localRotation = Quaternion.Euler(0f, spinAngle, 0f);
        }

        /// Idle breathing sway around each petiole base (vessels/beads are children, so they stay aligned) + dormant droop.
        void UpdateSway(float t)
        {
            float droop = dormantW * 9f;
            for (int i = 0; i < leaves.Count; i++)
            {
                var lf = leaves[i];
                float sx = Mathf.Sin(t * lf.swayF + lf.swayPh) * 1.1f + Mathf.Sin(t * lf.swayF * 2.3f + lf.swayPh * 1.7f) * 0.35f;
                float sz = Mathf.Sin(t * lf.swayF * 0.8f + lf.swayPh * 2.1f) * 0.8f;
                lf.root.localRotation = lf.rootRest * Quaternion.Euler(sx + droop, 0f, sz);
            }
        }

        static readonly Color RimColor = new Color(0.45f, 1f, 0.78f);

        void UpdateEmission(float t)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(t * 4.2f);
            Color glow = Color.black;
            if (flowLive[(int)Flow.Sunlight]) glow += Palette.Light * 0.10f;
            if (flowLive[(int)Flow.Chloroplast]) glow += Palette.GreenBright * (0.09f + 0.04f * Mathf.Sin(t * 2.3f));
            bool anyRim = false;
            for (int i = 0; i < leaves.Count; i++)
            {
                var lf = leaves[i];
                Color e = glow + Palette.GreenBright * (0.55f * lf.growGlow) + Palette.Gold * (0.9f * lf.flash);
                if (lf.hl) { e += RimColor * (0.08f + 0.20f * pulse); anyRim = true; }
                SetE(lf.top, e); SetE(lf.under, e * 0.6f);
            }
            if (anyRim) { var rc = RimColor * 2.4f; rc.a = 0.35f + 0.65f * pulse; Fx.SetColor(rimMat, rc); }
            Color hs = hlStem ? RimColor * (0.10f + 0.22f * pulse) : Color.black;
            SetE(mStem, hs); SetE(mPetiole, hs);
            Color hr = hlRoots ? RimColor * (0.15f + 0.3f * pulse) : Color.black;
            SetE(mRoot, hr); SetE(mHair, hr); SetE(mCorm, hr);
            float breathe = 0.15f * Mathf.Sin(t * 2.2f);   // brightness pulse only (0.9–1.2, highlight 1.15–1.45): the two highways keep their blue / amber under bloom
            if (xylemVis) Fx.SetEmission(mXylem, K(Palette.Xylem, (hlXylem ? 1.3f : 1.05f) + breathe));
            if (phloemVis) Fx.SetEmission(mPhloem, K(Palette.Phloem, (hlPhloem ? 1.3f : 1.05f) + breathe));
        }

        static void SetE(MatPair p, Color e)
        {
            e += p.e; e.a = 1f;
            if (e == p.lastE) return;
            p.lastE = e; Fx.SetEmission(p.o, e); Fx.SetEmission(p.t, e);
        }

        void Awake() => EnsureBuilt();

        void EnsureBuilt() { if (!built) Build(); }

        LeafPart Lf(int i) { EnsureBuilt(); return leaves[Mathf.Clamp(i, 0, leaves.Count - 1)]; }
        Vector3 P(Vector3 local) { EnsureBuilt(); return pivot.TransformPoint(local); }

        void OnDestroy() { DestroyClones(); foreach (var o in owned) if (o) Destroy(o); }
    }
}
