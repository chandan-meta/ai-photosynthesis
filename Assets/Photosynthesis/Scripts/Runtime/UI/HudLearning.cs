using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using static Photosynthesis.Kit;
using static Photosynthesis.Palette;

namespace Photosynthesis
{
    /// Clue tracker + reward, glucose uses, in/out badges, MCQ/feedback/reteach, equation, xylem-vs-phloem,
    /// case solved, Free Explore, fade + loading.
    public partial class Hud
    {
        void BuildLearning()
        {
            BuildClues(); BuildMagnification(); BuildGlucoseAndInOut(); BuildQuestion(); BuildFeedback(); BuildReteach();
            BuildEquation(); BuildComparison(); BuildExperiment(); BuildComplete(); BuildEndMenu(); BuildExplore(); BuildLoading();
        }

        void UpdateLearning(float t)
        {
            UpdateCluePick();
            if (IsShown(clueRow))
                for (int i = 0; i < 3; i++)
                    if (clueState[i] == ClueState.Available)
                    {
                        // the pulse runs on the glow and the '?' medallion only, so .ps-clue--available:hover keeps its 1.04 scale
                        float w = 0.5f + 0.5f * Mathf.Sin(t * 3f + i);
                        cards[i].glow.style.opacity = 0.55f + 0.45f * w;          // effective alpha 0.41–0.75, always above Discovered's 0.3
                        cards[i].mystery.style.opacity = 0.75f + 0.25f * w;
                        if (!ReduceMotion) cards[i].mystery.style.scale = new Scale(Vector2.one * (1f + 0.06f * w));
                    }
            if (IsShown(reteachCard)) UpdateReteach();
            if (IsShown(expPanel)) UpdateExperiment();
            if (IsShown(eqLayer) && eqArrowDone && !ReduceMotion)
            {
                float k = Mathf.Repeat(t * 0.6f, 1f);
                eqPulse.style.translate = new Translate(k * 214f, 0); eqPulse.style.opacity = Mathf.Sin(k * Mathf.PI);
            }
            if (IsShown(inOutRow) && !ReduceMotion)
            {
                float b = Mathf.Sin(t * 3f) * 6f;
                inArrow.style.translate = new Translate(0, b); outArrow.style.translate = new Translate(0, -b);
            }
            if (IsShown(completeCard) && !ReduceMotion) completeSun.style.rotate = new Rotate(Angle.Degrees(t * 10f));
        }

        // ================================================================== clues
        class ProgItem { public VisualElement root, ring; public IconElement icon, trail; public Label label; public GlowElement glow; }
        class ClueCard { public VisualElement root, pillHolder, mystery; public ArtElement art; public IconElement mysteryIcon; public Label num, name; public GlowElement glow; }
        readonly ProgItem[] items = new ProgItem[3];
        readonly ClueCard[] cards = new ClueCard[3];
        readonly ClueState[] clueState = new ClueState[3];
        static readonly string[] ProgClasses = { "ps-prog--locked", "ps-prog--available", "ps-prog--active", "ps-prog--done" };
        VisualElement clueRow;
        bool cluePick;   // the hub waits for a clue pick: only then do the progress rows and clue cards take clicks
        public event Action<ClueId> ClueClicked;

        static string ClueLabel(ClueId id) => id == ClueId.CO2 ? "Carbon dioxide" : id == ClueId.Water ? "Water" : "Sunlight";
        static IconId ClueIcon(ClueId id) => id == ClueId.CO2 ? IconId.CO2 : id == ClueId.Water ? IconId.Drop : IconId.Sun;
        static ArtId ClueArt(ClueId id) => id == ClueId.CO2 ? ArtId.ClueCO2 : id == ClueId.Water ? ArtId.ClueWater : ArtId.ClueSun;
        static string ClueFact(ClueId id) => id == ClueId.CO2 ? "Carbon dioxide from the air enters the leaf through tiny pores called stomata."
            : id == ClueId.Water ? "Water is absorbed from the soil by the roots and carried up through the xylem."
            : "Light provides the energy that drives photosynthesis.";

        static bool Investigating(ClueId id)
        {
            var m = Module.I; if (!m) return false; var b = m.Beat;
            return id == ClueId.CO2 ? b >= Beat.Clue1 && b <= Beat.CO2 : id == ClueId.Water ? b >= Beat.Clue2 && b <= Beat.XylemRide : b >= Beat.Clue3 && b <= Beat.Sunlight;
        }

        void BuildClues()
        {
            clueRow = Hidden(new FlexBox(true, 24) { pickingMode = PickingMode.Ignore });
            clueRow.style.alignItems = Align.Stretch;
            for (int i = 0; i < 3; i++) { cards[i] = MakeCard((ClueId)i); ((FlexBox)clueRow).Put(cards[i].root); StyleClue(i); }
            CenterRow(panelLayer, 140).Add(clueRow);

            unlockCard = Hidden(Panel(20, 0.92f, Water, 2)); Size(unlockCard, 460); Pad(unlockCard, 32, 40, 32, 40);
            unlockGlow = Glow(unlockCard, Water, 20, 32, 0.6f);
            var col = new FlexBox(false, 14); col.style.alignItems = Align.Center; unlockCard.Add(col);
            unlockHalo = col.Put(Circle(170, A(Water, 0.08f), Color.clear, 0));
            unlockHaloGlow = Glow(unlockHalo, Water, 85, 40, 0.3f);
            unlockHalo.Add(unlockArt = new ArtElement(ArtId.ClueWater, 150));
            unlockReadout = col.Put(Txt("", TS.Readout, Water));
            unlockTitle = col.Put(Txt("", TS.H1, Text));
            unlockBody = col.Put(Txt("", TS.Body, TextSecondary, 380)); unlockBody.style.unityTextAlign = TextAnchor.UpperCenter;
            unlockBtn = col.Put(Btn("Continue investigation", ButtonKind.Gold, IconId.ChevronRight, () => unlockDismiss = true));
            CenterRow(overlayLayer, 200).Add(unlockCard);
        }

        ProgItem MakeProg(ClueId id, bool clickable)
        {
            var it = new ProgItem { root = new VisualElement() };
            it.root.AddToClassList("ps-prog");
            it.glow = Glow(it.root, GreenBright, 12, 16, 0.5f);
            it.ring = Circle(32, A(GreenBright, 0.16f), A(GreenBright, 0.7f), 1.5f, it.icon = Icon(ClueIcon(id), 18, Text));
            it.ring.style.marginRight = 12; it.root.Add(it.ring);
            it.label = Txt(ClueLabel(id), TS.BodyStrong, Text); it.label.style.flexGrow = 1; it.root.Add(it.label);
            it.root.Add(it.trail = Icon(IconId.Lock, 18, TextMuted));
            if (clickable) Click(it.root, () => CardClicked(id, it.root));
            else { it.root.pickingMode = PickingMode.Ignore; IgnoreChildren(it.root); }
            StyleProg(it, ClueState.Locked, false);
            return it;
        }

        void UpdateCluePick()
        {
            var m = Module.I; bool on = m && m.HubOpen && IsShown(clueRow);   // Hub(): HubOpen + cards; Ask() clears HubOpen; FreeExplore hides the cards
            if (on == cluePick) return;
            cluePick = on; for (int i = 0; i < 3; i++) StyleClue(i);
        }

        /// pick: the row is a live hub choice (chevron); otherwise it reads as status (Target glyph, the "Available" status icon).
        static void StyleProg(ProgItem it, ClueState s, bool active, bool pick = true)
        {
            int k = s == ClueState.Locked ? 0 : s == ClueState.Discovered ? 3 : active ? 2 : 1;
            for (int i = 0; i < 4; i++) it.root.EnableInClassList(ProgClasses[i], i == k);
            it.ring.style.backgroundColor = k == 0 ? A(Palette.Locked, 0.35f) : A(GreenBright, k == 2 ? 0.25f : 0.16f);
            BorderColor(it.ring, k == 0 ? Color.clear : A(GreenBright, 0.7f));
            it.icon.Color = k == 0 ? TextMuted : Text; it.label.style.color = k == 0 ? TextMuted : Text;
            it.trail.Id = k == 0 ? IconId.Lock : k == 3 ? IconId.Check : pick ? IconId.ChevronRight : IconId.Target;
            it.trail.Color = k == 0 ? TextMuted : k == 3 ? Correct : k == 2 ? Text : Cyan;
            it.glow.style.display = k == 2 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        ClueCard MakeCard(ClueId id)
        {
            var c = new ClueCard { root = new VisualElement() };
            c.root.AddToClassList("ps-clue");
            c.glow = Glow(c.root, Cyan, 18, 22, 0.6f);
            var col = new FlexBox(false, 10); col.style.alignItems = Align.Center; c.root.Add(col);
            // fixed 72×72 slot: the art, or (until discovered) a '?' disc in the progress-row language, so the card never reads blank
            var slot = col.Put(Size(new VisualElement { pickingMode = PickingMode.Ignore }, 72, 72)); slot.style.flexShrink = 0;
            slot.Add(c.art = new ArtElement(ClueArt(id), 72));
            slot.Add(c.mystery = Abs(Circle(64, A(Palette.Locked, 0.3f), A(GlassStroke, 0.14f), 1.5f,
                c.mysteryIcon = Icon(IconId.Help, 36, TextMuted)), 4, 4));
            c.num = col.Put(Txt($"Clue 0{(int)id + 1}", TS.Label, TextSecondary));
            c.name = col.Put(Txt(ClueLabel(id).ToUpperInvariant(), TS.BodyStrong, Text));
            c.pillHolder = col.Put(new VisualElement { pickingMode = PickingMode.Ignore });
            Click(c.root, () => CardClicked(id, c.root));
            return c;
        }

        // Soft tick on every click; the select bell comes from the Hub when the pick is valid, a soft buzz when locked.
        void CardClicked(ClueId id, VisualElement e)
        {
            var s = clueState[(int)id];
            if (s == ClueState.Available) ClueClicked?.Invoke(id);
            else if (s == ClueState.Locked) { Play(Sfx.Wrong, 0.3f); if (!ReduceMotion) StartCoroutine(Shake(e)); }
        }

        static IEnumerator Shake(VisualElement e)
        {
            yield return Fx.Tween(0.35f, k => e.style.translate = new Translate(Mathf.Sin(k * 30f) * 7f * (1 - k), 0), Ease.Linear, true);
            e.style.translate = StyleKeyword.Null;
        }

        void StyleClue(int i)
        {
            var s = clueState[i]; var id = (ClueId)i; var col = Palette.Of(id);
            // while the hub cards are up the row stays the cyan-outline Available (matching the card); green Active after the pick
            StyleProg(items[i], s, s == ClueState.Available && Investigating(id) && !IsShown(clueRow), cluePick);
            items[i].root.pickingMode = cluePick ? PickingMode.Position : PickingMode.Ignore;   // outside the hub: status, not a button
            // A clue stays "???" until discovered: Dr. Curio isn't "giving them away that easily" (storyboard: CLUE 0N — LOCKED).
            bool known = s == ClueState.Discovered, avail = s == ClueState.Available;
            items[i].label.text = known ? ClueLabel(id) : $"Clue 0{i + 1}"; items[i].icon.Id = known ? ClueIcon(id) : IconId.Help;
            var c = cards[i]; if (c == null) return;
            c.root.pickingMode = cluePick ? PickingMode.Position : PickingMode.Ignore;
            c.name.text = known ? ClueLabel(id).ToUpperInvariant() : "???";
            c.art.style.display = known ? DisplayStyle.Flex : DisplayStyle.None;
            c.mystery.style.display = known ? DisplayStyle.None : DisplayStyle.Flex;
            c.mystery.style.backgroundColor = avail ? A(Cyan, 0.12f) : A(Palette.Locked, 0.3f);
            BorderColor(c.mystery, avail ? A(Cyan, 0.75f) : A(GlassStroke, 0.14f));   // matches .ps-clue--available 0.75 / locked glass@14
            c.mysteryIcon.Color = avail ? Cyan : TextMuted;
            c.mystery.style.opacity = 1f; c.mystery.style.scale = StyleKeyword.Null;
            c.root.EnableInClassList("ps-clue--locked", s == ClueState.Locked);
            c.root.EnableInClassList("ps-clue--available", avail);
            c.root.EnableInClassList("ps-clue--found", known);
            BorderColor(c.root, known ? A(col, 0.95f) : (Color?)null);
            c.glow.Color = known ? A(col, 0.3f) : A(Cyan, 0.75f);          // the actionable card out-glows a finished one
            c.glow.style.display = s == ClueState.Locked ? DisplayStyle.None : DisplayStyle.Flex;
            c.glow.style.opacity = 1f;
            c.num.style.color = s == ClueState.Locked ? TextMuted : TextSecondary;
            c.name.style.color = s == ClueState.Locked ? TextMuted : Text;
            c.pillHolder.Clear();
            var pill = s == ClueState.Locked ? Pill("Locked", Palette.Locked, IconId.Lock, 0.35f, TextMuted)
                : avail ? Pill("Available", Cyan, IconId.Target)
                : Pill("Discovered", GreenBright, IconId.Check, 0f, Text, A(Green, 0.95f));
            if (s == ClueState.Locked) BorderColor(pill, Color.clear);   // no rim; the 1px transparent border keeps it the Available pill's size
            c.pillHolder.Add(pill);
        }

        int pendingPop = -1;   // discovered clue whose tracker pop waits for the reward card / cinematic to end

        public void SetClueState(ClueId id, ClueState state, bool celebrate)
        {
            int i = (int)id; var prev = clueState[i]; clueState[i] = state;
            StyleClue(i);
            if (celebrate && state == ClueState.Discovered && prev != ClueState.Discovered && !ReduceMotion && Time.frameCount != transientFrame) { pendingPop = i; SchedulePop(); }
            else if (pendingPop == i) pendingPop = -1;
        }

        /// The tracker pop is a handoff after the reward: it waits for the card fade-out / HUD fade-in (HideUnlock, SetCinematic(false) retry).
        void SchedulePop() { if (pendingPop >= 0) root.schedule.Execute(() => FlushPop()).StartingIn(400); }
        void FlushPop()
        {
            int i = pendingPop;
            if (i < 0 || IsShown(unlockCard) || cinematic) return;   // still busy: the next HideUnlock / SetCinematic(false) retries
            pendingPop = -1;
            if (!IsShown(progressPanel) || Module.Skipping) return;
            StartCoroutine(PopRoutine(items[i].root));
            var wb = items[i].ring.worldBound;
            if (!float.IsNaN(wb.x)) sparkles.Burst(wb.center, Palette.Of((ClueId)i), Gold, 26, 220f, 10f);
        }

        static float PopCurve(float k) => k < 0.55f ? Mathf.Lerp(0.8f, 1.06f, Fx.Eval(k / 0.55f, Ease.Out)) : Mathf.Lerp(1.06f, 1f, Fx.Eval((k - 0.55f) / 0.45f, Ease.InOut));

        static IEnumerator PopRoutine(VisualElement e)
        {
            yield return Fx.Tween(0.5f, k => e.style.scale = new Scale(Vector2.one * PopCurve(k)), Ease.Linear, true);
            e.style.scale = StyleKeyword.Null;
        }

        /// Top-centre three-clue tracker cards. The progress rows restyle with them (cyan Available while the hub is up, green Active after).
        public void ShowClueCards(bool on) { Show(clueRow, on); for (int i = 0; i < 3; i++) StyleClue(i); }

        // ---- reward card
        VisualElement unlockCard, unlockHalo, unlockBtn; ArtElement unlockArt; Label unlockReadout, unlockTitle, unlockBody; GlowElement unlockGlow, unlockHaloGlow;
        bool unlockDismiss; int unlockGen;

        /// Reward card "CLUE 0X UNLOCKED! … ✓" with fanfare, burst + pulse; hides itself after a reading-time hold
        /// (~4–6.5 s) or on Continue.
        public IEnumerator ClueUnlocked(ClueId id)
        {
            int my = ++unlockGen; unlockDismiss = false;
            var col = Palette.Of(id);
            unlockArt.Id = ClueArt(id);
            Set(unlockReadout, $"Clue 0{(int)id + 1} unlocked!"); unlockReadout.style.color = col;
            unlockTitle.text = ClueLabel(id).ToUpperInvariant() + " ✓"; Set(unlockBody, Prose(ClueFact(id)));
            BorderColor(unlockCard, col); unlockGlow.Color = A(col, 0.6f);
            unlockHalo.style.backgroundColor = A(col, 0.08f); unlockHaloGlow.Color = A(col, 0.3f);
            HideScanner(); ClearCallouts();   // the card lands where the scanner sits; world tags would poke out of / show through the glass
            Dim(DimSrc.Unlock, 0.45f); Show(unlockCard, true); FocusSoon(unlockBtn);
            Play(Sfx.Unlock); Play(Sfx.Sparkle, 0.7f);
            bool burst = false; float t = 0, life = 0, hold = Mathf.Max(3f, 2f + 0.3f * ClueFact(id).Split(' ').Length);
            while (my == unlockGen && !unlockDismiss && life < hold && !Module.Skipping)
            {
                t += Time.unscaledDeltaTime; life += Time.deltaTime;
                float k = Mathf.Clamp01(t / 0.6f);
                unlockArt.style.scale = new Scale(Vector2.one * (ReduceMotion ? 1f : PopCurve(k) * (1f + 0.03f * Mathf.Sin(t * 4f))));
                unlockHaloGlow.style.opacity = 0.6f + 0.4f * Mathf.Sin(t * 3f);
                var wb = unlockCard.worldBound;
                if (!burst && wb.width > 1 && !float.IsNaN(wb.x) && unlockCard.resolvedStyle.opacity > 0.85f)
                {   // from behind the card (fbFx): the sparks frame the reward card instead of crossing "CLUE 0N UNLOCKED!"
                    burst = true;
                    FrameBurst(wb, Gold, Color.white, 90, 460f);
                    FrameBurst(wb, col, Gold, 50, 320f);
                }
                yield return null;
            }
            if (my == unlockGen) HideUnlock();
        }

        void HideUnlock() { unlockGen++; Show(unlockCard, false); Dim(DimSrc.Unlock, 0); SchedulePop(); if (!IsShown(fbCard)) fbFx.Stop(); }

        // ================================================================== magnification + zoom path
        VisualElement zoomPathPill, magRow; readonly VisualElement[] pathSegs = new VisualElement[4]; readonly Label[] pathLabels = new Label[4];
        static readonly string[] PathKeys = { "WholePlant", "Leaf", "Cell", "Chloroplast" };

        void BuildMagnification()
        {
            magPill = Hidden(new VisualElement()); magPill.style.flexDirection = FlexDirection.Row;
            Pad(magPill, 4, 4); Radius(magPill, 999); Bg(magPill, AG(Glass, 0.85f)); Border(magPill, A(GlassStroke, 0.25f), 1);
            string[] lv = { "1×", "10×", "100×" };
            for (int i = 0; i < 3; i++)
            {
                var s = magSegs[i] = Stadium(new VisualElement { pickingMode = PickingMode.Ignore }); s.AddToClassList("ps-seg");
                if (i > 0) s.style.marginLeft = 4;
                s.Add(magLabels[i] = Txt(lv[i], TS.Button, TextSecondary)); magPill.Add(s);
                Click(s, () => ZoomButton?.Invoke(3f));                  // a level tap completes the 3-step zoom prompt; pickable only at ShowMagnification(0)
            }
            (magRow = CenterRow(panelLayer, 40)).Add(magPill);
            magPill.AddToClassList(CineHide);

            // same family as the magnification pill stacked under it: 4 px inset, .ps-seg 6×14 padding, Button 14 labels
            zoomPathPill = Hidden(new FlexBox(true, 6)); Pad(zoomPathPill, 4, 4); Radius(zoomPathPill, 999);
            Bg(zoomPathPill, AG(Glass, 0.85f)); Border(zoomPathPill, A(GlassStroke, 0.25f), 1);
            string[] names = { "Whole Plant", "Leaf", "Cell", "Chloroplast" };
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                var s = pathSegs[i] = Stadium(new VisualElement()); s.AddToClassList("ps-seg"); s.AddToClassList("ps-seg--plain");
                s.Add(pathLabels[i] = Txt(names[i], TS.Button, TextSecondary));
                Click(s, () => { SelectTile(Array.IndexOf(XKeys, PathKeys[idx])); explorePick?.Invoke(PathKeys[idx]); });
                ((FlexBox)zoomPathPill).Put(s);
                if (i < 3) ((FlexBox)zoomPathPill).Put(Icon(IconId.ChevronRight, 14, TextMuted));
            }
            CenterRow(panelLayer, 44).Add(zoomPathPill);
            zoomPathPill.AddToClassList(CineHide);
        }

        /// 0 Whole Plant · 1 Leaf · 2 Cell · 3 Chloroplast
        public void SetZoomPath(int level)
        {
            HideMagnification();   // a new Free Explore destination; the stomata destination re-shows its 100× pill right after
            StylePath(level);
        }

        void StylePath(int level)
        {
            for (int i = 0; i < 4; i++)
            {
                pathSegs[i].EnableInClassList("ps-seg--on", i == level);
                pathLabels[i].style.color = i == level ? Text : TextSecondary;
                pathSegs[i].pickingMode = explorePick != null ? PickingMode.Position : PickingMode.Ignore;   // navigates only in Free Explore
            }
        }

        /// Story descent/ascent: the Free Explore breadcrumb as a read-only "you are here" (magnification pill untouched). -1 hides it.
        public void ShowZoomPath(int level) { if (level >= 0) StylePath(level); Show(zoomPathPill, level >= 0); PlaceMag(); }

        // ================================================================== glucose uses + in/out
        const string InOutCap = "Same doorway. Different journey.";   // = s11_doorway: RelayoutBottom hides it while that subtitle is up
        VisualElement glucosePill, glucoseSlot, inOutRow, inOutCaption, inOutCapRow; bool inOutOn, inOutCapOn = true; readonly VisualElement[] glucoseCards = new VisualElement[3]; IconElement inArrow, outArrow;

        void BuildGlucoseAndInOut()
        {
            // one bottom-centre column (pill 12 px above the cards) that rides on top of the dialogue stack (RelayoutBottom),
            // clear of the molecule and its GLUCOSE callout
            var gcol = new FlexBox(false, 12) { pickingMode = PickingMode.Ignore };
            gcol.style.alignItems = Align.Center;
            glucosePill = Hidden(Pill("Food made", Gold, IconId.Check, 0f, Gold, AG(Sunken, 0.95f)));   // one tick (the icon); ≈11:1 on any background
            glucosePill.style.alignSelf = Align.Center;                                                  // Kit.Pill pins FlexStart
            Glow(glucosePill, Gold, 999, 14, 0.4f);
            gcol.Put(glucosePill);
            var row = gcol.Put(new FlexBox(true, 18) { pickingMode = PickingMode.Ignore });
            (IconId, string, string)[] uses = { (IconId.Energy, "Energy", "Fuel for every living cell"), (IconId.Plant, "Growth", "Builds new cells, shoots & roots"), (IconId.Glucose, "Storage", "Stored as starch for later") };
            for (int i = 0; i < 3; i++)
            {
                var c = glucoseCards[i] = Hidden(Panel(18, 0.86f, A(Palette.Glucose, 0.5f)));
                Pad(c, 14, 18, 14, 14);
                c.style.transitionDelay = new List<TimeValue> { new TimeValue(i * 0.12f) };
                var r = new FlexBox(true, 12); c.Add(r);
                r.Put(Circle(40, A(Palette.Glucose, 0.16f), A(Palette.Glucose, 0.6f), 1, Icon(uses[i].Item1, 20, Palette.Glucose)));
                var cc = r.Put(new FlexBox(false, 2)); cc.Put(Txt(uses[i].Item2, TS.H3, Text)); cc.Put(Txt(uses[i].Item3, TS.Small, TextSecondary));
                row.Put(c);
            }
            glucoseSlot = CenterRow(panelLayer, float.NaN, 40); glucoseSlot.AddToClassList("ps-slot"); glucoseSlot.Add(gcol);

            inOutRow = Hidden(new FlexBox(true, 0) { pickingMode = PickingMode.Ignore });
            inOutRow.style.alignItems = Align.FlexStart;
            ((FlexBox)inOutRow).Put(Badge(ArtId.CO2, IconId.ArrowDown, "CO₂ → IN", Cyan, out inArrow));
            ((FlexBox)inOutRow).Put(new VisualElement { pickingMode = PickingMode.Ignore }).style.width = 700;
            ((FlexBox)inOutRow).Put(Badge(ArtId.O2, IconId.ArrowUp, "O₂ → OUT", Oxygen, out outArrow));
            CenterRow(panelLayer, 380).Add(inOutRow);
            // known (b): a feathered dark pill behind the caption (the glow draws only outside the border box, never over the text)
            inOutCaption = Hidden(new VisualElement { pickingMode = PickingMode.Ignore });
            Pad(inOutCaption, 14, 36); Radius(inOutCaption, 999); Bg(inOutCaption, A(Sunken, 0.85f));
            inOutCaption.Add(new GlowElement(Sunken, 999, 64, 0.85f));
            var cap = Txt(InOutCap, TS.H1, Text);
            cap.style.textShadow = new TextShadow { offset = new Vector2(0, 2), blurRadius = 12, color = new Color(0, 0, 0, 0.8f) };
            inOutCaption.Add(cap);
            (inOutCapRow = CenterRow(panelLayer, 786)).Add(inOutCaption);   // RelayoutBottom lifts it on a short canvas
        }

        static VisualElement Badge(ArtId art, IconId arrow, string label, Color c, out IconElement arrowEl)
        {
            var b = Panel(18, 0.84f, A(c, 0.9f), 1.5f); Pad(b, 18, 22, 18, 22);
            Glow(b, c, 18, 20, 0.45f);
            var col = new FlexBox(false, 10); col.style.alignItems = Align.Center; b.Add(col);
            col.Put(new ArtElement(art, 84));
            arrowEl = col.Put(Icon(arrow, 36, c));
            col.Put(Txt(label, TS.Readout, c));
            return b;
        }

        /// "FOOD MADE" gold pill + Energy · Growth · Storage cards (bottom-centre column above the dialogue stack).
        public void ShowGlucoseUses(bool on)
        {
            Show(glucosePill, on);
            foreach (var c in glucoseCards) Show(c, on);
            RelayoutBottom();
        }

        /// "CO₂ → IN" / "O₂ → OUT" badges + (when caption) "Same doorway. Different journey." The caption yields to an instruction / call-to-action (RelayoutBottom).
        public void ShowInOut(bool on, bool caption = true) { inOutOn = on; inOutCapOn = caption; Show(inOutRow, on); RelayoutBottom(); }

        // ================================================================== assessment
        VisualElement qCard, qLeadRow, qSubmit, qHint, qTagHolder; FlexBox qOptions; Label qNum, qLead, qText;
        readonly List<VisualElement> qOpts = new List<VisualElement>(); readonly List<IconElement> qMarks = new List<IconElement>();
        int qGen, qSel = -1; bool qSubmitted, qLocked, qSpoils;   // qSpoils: the tracker row would single out this card's answer

        void BuildQuestion()
        {
            qCard = Hidden(Abs(Panel(20, 1f), 120, 170)); Size(qCard, 620); Pad(qCard, 24, 24, 24, 24);   // opaque reading card
            var col = new FlexBox(false, 16); qCard.Add(col);
            var head = col.Put(new FlexBox(true, 10));
            qNum = head.Put(Txt("", TS.Label, TextSecondary)); qNum.style.flexGrow = 1;
            qTagHolder = head.Put(new VisualElement { pickingMode = PickingMode.Ignore });
            qLeadRow = col.Put(new FlexBox(true, 10));
            ((FlexBox)qLeadRow).Put(Circle(32, Raised, A(Cyan, 0.9f), 1.5f, new ArtElement(ArtId.Curio, 28)));
            qLead = ((FlexBox)qLeadRow).Put(Txt("", TS.Small, Cyan, 520));
            qText = col.Put(Txt("", TS.H2, Text, 572));
            qOptions = col.Put(new FlexBox(false, 10));
            var foot = col.Put(new FlexBox(true, 10)); foot.style.justifyContent = Justify.FlexEnd;
            qHint = foot.Put(Btn("Ask Dr. Curio for a hint", ButtonKind.Ghost, null, () => { OpenChat(false); Submit("Can you give me a hint?"); }));
            qSubmit = foot.Put(Btn("Submit answer", ButtonKind.Primary, IconId.ChevronRight, SubmitAnswer));
            panelLayer.Add(qCard);
        }

        /// Shows the MCQ card; returns the chosen option index through `chosen` after Submit.
        /// `disabled`: the previously wrong option, shown red with ✕ and not selectable on the retry.
        public IEnumerator Question(Question q, int number, int total, string tag, Action<int> chosen, int disabled = -1)
        {
            int my = ++qGen; qSel = -1; qSubmitted = false; qLocked = false;
            if (q == null) yield break;
            Set(qNum, total > 0 ? $"Question {number} of {total}" : $"Question {number}");
            qTagHolder.Clear(); if (!string.IsNullOrEmpty(tag)) qTagHolder.Add(Pill(tag, Gold, IconId.Sparkle, 0.12f));
            bool lead = !string.IsNullOrEmpty(q.lead);
            qLeadRow.style.display = lead ? DisplayStyle.Flex : DisplayStyle.None;
            if (lead) Set(qLead, Prose($"Dr. Curio: “{q.lead}”"));
            qText.text = Prose(q.text);
            qOptions.Reset(); qOpts.Clear(); qMarks.Clear();
            var opts = q.options ?? Array.Empty<string>();
            for (int i = 0; i < opts.Length; i++)
            {
                int idx = i;
                var o = new VisualElement(); o.AddToClassList("ps-opt");
                var badge = new VisualElement(); badge.AddToClassList("ps-opt-badge");
                var letter = Txt(((char)('A' + i)).ToString(), TS.BodyStrong); letter.AddToClassList("ps-opt-letter"); badge.Add(letter);
                o.Add(badge);
                var ans = WrapText(Txt(opts[i], TS.Body, Text)); ans.style.flexGrow = 1; ans.style.flexShrink = 1; o.Add(ans);
                var mark = Icon(IconId.Check, 20, Correct); mark.style.display = DisplayStyle.None; mark.style.marginLeft = 8; o.Add(mark);
                if (i == disabled)
                {
                    o.AddToClassList("ps-opt--off"); o.pickingMode = PickingMode.Ignore; IgnoreChildren(o);   // "already tried": calmer than a fresh wrong answer
                    mark.Id = IconId.Close; mark.Color = Incorrect; mark.style.display = DisplayStyle.Flex;
                }
                else Click(o, () => SelectOption(idx), Sfx.UiSelect);
                qOptions.Put(o); qOpts.Add(o); qMarks.Add(mark);
            }
            qSubmit.SetEnabled(false); qHint.SetEnabled(true);             // the hint re-arms for each new or retried card
            if (IsShown(eqLayer)) HideEquation();                          // a reteach equation must not cover the retry
            qSpoils = TrackerNames(opts, q.correct);
            Show(qCard, true); RefreshPersistent();                        // RefreshPersistent re-places a visible scanner too (docks right, or steps clear of the card)
            FocusSoon(qOpts.Find(o => o.pickingMode == PickingMode.Position));
            while (my == qGen && !qSubmitted) yield return null;
            if (my != qGen) yield break;
            chosen?.Invoke(qSel);
        }

        /// A found clue's name in the right option and in no other (Q1/Q2 beside "Carbon dioxide ✓"): the tracker would answer the card.
        bool TrackerNames(string[] opts, int correct)
        {
            if (correct < 0 || correct >= opts.Length) return false;
            for (int c = 0; c < 3; c++)
            {
                if (clueState[c] != ClueState.Discovered) continue;
                string n = ClueLabel((ClueId)c); int hits = 0;
                foreach (var o in opts) if (o.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) hits++;
                if (hits == 1 && opts[correct].IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        void SelectOption(int i)
        {
            if (qLocked) return;
            qSel = i;
            for (int j = 0; j < qOpts.Count; j++) qOpts[j].EnableInClassList("ps-opt--sel", j == i);
            qSubmit.SetEnabled(true); qSubmit.Focus();                      // Enter now submits
        }

        void SubmitAnswer()
        {
            if (qSel < 0 || qLocked) return;
            LockOptions(); qSubmitted = true;
        }

        void LockOptions()
        {
            qLocked = true; qSubmit.SetEnabled(false); qHint.SetEnabled(false);   // judged: no hint after the answer
            foreach (var o in qOpts) o.pickingMode = PickingMode.Ignore;
        }

        public void MarkAnswer(int option, bool correct)
        {
            if (option < 0 || option >= qOpts.Count) return;
            LockOptions();
            var o = qOpts[option];
            o.RemoveFromClassList("ps-opt--sel");
            o.EnableInClassList("ps-opt--ok", correct); o.EnableInClassList("ps-opt--no", !correct);
            var m = qMarks[option]; m.Id = correct ? IconId.Check : IconId.Close; m.Color = correct ? Correct : Incorrect; m.style.display = DisplayStyle.Flex;
        }

        public void HideQuestion() { qGen++; qSpoils = false; Show(qCard, false); RefreshPersistent(); }

        // ---- feedback
        VisualElement fbCard, fbRing, fbVis; FlexBox fbButtons, fbStrip; Label fbTitle, fbSub, fbBody; IconElement fbIcon; GlowElement fbGlow; SparkleElement fbFx;
        int fbGen, fbResult;

        void BuildFeedback()
        {
            fbCard = Hidden(Panel(20, 0.92f, Correct, 2)); Size(fbCard, 580); Pad(fbCard, 28, 28, 28, 28);
            fbGlow = Glow(fbCard, Correct, 20, 30, 0.55f);
            overlayLayer.Insert(0, fbFx = new SparkleElement());   // behind every overlay card: the feedback + clue-unlock bursts frame the card, never cross its text
            var col = new FlexBox(false, 16); fbCard.Add(col);
            var head = col.Put(new FlexBox(true, 14));
            fbRing = head.Put(Circle(56, A(Correct, 0.2f), Correct, 2, fbIcon = Icon(IconId.Check, 28, Correct)));
            var hc = head.Put(new FlexBox(false, 2)); hc.style.flexShrink = 1;
            fbTitle = hc.Put(Txt("", TS.H1, Correct)); fbSub = hc.Put(Txt("", TS.Small, TextSecondary));
            fbBody = col.Put(Txt("", TS.Body, Text, 524));
            // Figma D-17 visual recap: the path the answer describes (shown on Correct when the caller passes the concept)
            fbVis = col.Put(new VisualElement { pickingMode = PickingMode.Ignore });
            Size(fbVis, 524, 128); Radius(fbVis, 14); Bg(fbVis, A(GreenDeep, 0.9f)); Border(fbVis, A(GreenBright, 0.25f), 1);
            fbVis.style.alignItems = Align.Center; fbVis.style.justifyContent = Justify.Center;
            fbVis.Add(fbStrip = new FlexBox(true, 14) { pickingMode = PickingMode.Ignore });
            fbButtons = col.Put(new FlexBox(true, 10)); fbButtons.style.justifyContent = Justify.FlexEnd;
            Abs(fbCard, 120, 170); overlayLayer.Add(fbCard);   // same slot as qCard (x120 y170): in-place swap, plant stays in the final-set framing
        }

        /// Feedback card. result: 0 = primary button, 1 = secondary button. recap: concept whose path the card recaps (optional; the
        /// second-miss answer card passes it with correct = false). sub: replaces the default subtitle line.
        public IEnumerator Feedback(bool correct, string title, string body, string primary, string secondary, Action<int> result, Concept? recap = null, string sub = null)
        {
            int my = ++fbGen; fbResult = -1;
            Color c = correct ? Correct : Incorrect;
            BorderColor(fbCard, c); fbGlow.Color = A(c, 0.55f);
            fbRing.style.backgroundColor = A(c, 0.2f); BorderColor(fbRing, c);
            fbIcon.Id = correct ? IconId.Check : IconId.Close; fbIcon.Color = c;
            fbTitle.text = title; fbTitle.style.color = c;
            Set(fbSub, sub ?? (correct ? "Nice work, investigator." : "Let's look at it again together."));   // neutral: reads right under a prediction, a recall or "Case solved"
            Set(fbBody, Prose(body ?? "")); fbBody.style.display = string.IsNullOrEmpty(body) ? DisplayStyle.None : DisplayStyle.Flex;
            fbStrip.Reset();
            var steps = recap.HasValue ? RecapSteps(recap.Value) : null;
            fbVis.style.display = steps != null ? DisplayStyle.Flex : DisplayStyle.None;
            for (int i = 0; steps != null && i < steps.Length; i++)
            {
                bool last = i == steps.Length - 1; Color tone = last ? Gold : GreenBright;
                if (i > 0) fbStrip.Put(Icon(IconId.ChevronRight, 18, A(GreenBright, 0.55f)));
                var node = fbStrip.Put(new FlexBox(false, 8) { pickingMode = PickingMode.Ignore }); node.style.alignItems = Align.Center; Size(node, 132);
                var ring = node.Put(Circle(56, A(tone, last ? 0.14f : 0.08f), A(tone, 0.6f), 1.5f, Icon(steps[i].icon, 28, tone)));
                if (last) Glow(ring, Gold, 28, 22, 0.35f);
                var cap = node.Put(Txt(steps[i].label, TS.Caption, last ? Gold : TextSecondary, 132)); cap.style.unityTextAlign = TextAnchor.UpperCenter;
            }
            fbButtons.Reset();
            if (!string.IsNullOrEmpty(secondary)) fbButtons.Put(Btn(secondary, ButtonKind.Secondary, IconId.Replay, () => { if (my == fbGen) fbResult = 1; }));
            var primaryBtn = fbButtons.Put(Btn(string.IsNullOrEmpty(primary) ? "Continue" : primary, ButtonKind.Primary, IconId.ChevronRight, () => { if (my == fbGen) fbResult = 0; }));
            Show(fbCard, true); Dim(DimSrc.Feedback, 0.5f); FocusSoon(primaryBtn);
            if (correct) Play(Sfx.Sparkle, 0.6f);
            bool burst = !correct;   // the burst rings the card rim once it has laid out and faded in (ClueUnlocked's deferral pattern)
            while (my == fbGen && fbResult < 0)
            {
                if (!burst) { var wb = fbCard.worldBound; if (wb.width > 1 && !float.IsNaN(wb.x) && fbCard.resolvedStyle.opacity > 0.85f) { burst = true; FrameBurst(wb, Correct, Gold, 80, 380f); } }
                yield return null;
            }
            if (my != fbGen) yield break;
            int r = fbResult; HideFeedback(); result?.Invoke(r);
        }

        /// Mirrors Story.ReteachSteps, so the correct and reteach cards tell the same path.
        static (IconId icon, string label)[] RecapSteps(Concept c) => c switch
        {
            Concept.Stomata => new[] { (IconId.CO2, "CO₂ in the air"), (IconId.Stomata, "Stoma"), (IconId.Leaf, "Into the leaf") },
            Concept.Water or Concept.Xylem => new[] { (IconId.Root, "Roots"), (IconId.Xylem, "Xylem"), (IconId.Leaf, "Leaves") },
            Concept.Sunlight => new[] { (IconId.Sun, "Sunlight"), (IconId.Leaf, "Leaf"), (IconId.Energy, "Energy captured") },
            Concept.Chloroplast or Concept.Chlorophyll => new[] { (IconId.Sun, "Light"), (IconId.Chloroplast, "Chlorophyll"), (IconId.Energy, "Energy captured") },
            Concept.Glucose => new[] { (IconId.CO2, "CO₂ + H₂O"), (IconId.Sun, "Light energy"), (IconId.Glucose, "Glucose") },
            Concept.Oxygen => new[] { (IconId.Chloroplast, "Chloroplast"), (IconId.Stomata, "Stoma"), (IconId.O2, "O₂ out") },
            Concept.Phloem => new[] { (IconId.Leaf, "Leaves"), (IconId.Phloem, "Phloem"), (IconId.Plant, "Roots · shoots · fruits") },
            _ => new[] { (IconId.CO2, "CO₂ + H₂O"), (IconId.Sun, "Light energy"), (IconId.Glucose, "Glucose + O₂") },
        };

        void HideFeedback() { fbGen++; Show(fbCard, false); Dim(DimSrc.Feedback, 0); if (!IsShown(unlockCard)) fbFx.Stop(); }   // hidden sparks must not spill over the scene once the card is gone

        /// Sparks born at random points on a card's rim. fbFx sits behind the overlay cards, so the inward half hides under the
        /// glass and the outward half fans past the edges: a frame of sparks around the card, never over its text.
        void FrameBurst(Rect wb, Color a, Color b, int count, float speed)
        {
            for (int n = 0; n < count; n++)
            {
                float t = UnityEngine.Random.value * 2f * (wb.width + wb.height), x, y;
                if (t < wb.width) { x = wb.x + t; y = wb.y; }
                else if ((t -= wb.width) < wb.height) { x = wb.xMax; y = wb.y + t; }
                else if ((t -= wb.height) < wb.width) { x = wb.xMax - t; y = wb.yMax; }
                else { x = wb.x; y = wb.yMax - (t - wb.width); }
                fbFx.Burst(fbFx.WorldToLocal(new Vector2(x, y)), a, b, 1, speed, 0f);
            }
        }

        // ---- reteach
        VisualElement reteachCard, rtSteps, rtFill; Label rtTitle, rtBody;
        string[] rtNames = Array.Empty<string>(); int rtStep = -1, rtManual = -1; float rtT0;
        const float RtStepSeconds = 2.4f;

        void BuildReteach()
        {
            reteachCard = Hidden(Abs(Panel(20, 1f, Incorrect, 2), 40, 260)); Size(reteachCard, 480); Pad(reteachCard, 24, 24, 24, 24);
            Glow(reteachCard, Incorrect, 20, 26, 0.5f);
            var col = new FlexBox(false, 14); reteachCard.Add(col);
            var head = col.Put(new FlexBox(true, 12));
            head.Put(Circle(52, A(Incorrect, 0.18f), Incorrect, 2, Icon(IconId.Replay, 24, Incorrect)));
            var hc = head.Put(new FlexBox(false, 2)); hc.style.flexShrink = 1;
            rtTitle = hc.Put(Txt("", TS.H2, Incorrect, 360)); rtBody = hc.Put(Txt("", TS.Small, TextSecondary, 360));
            rtSteps = col.Put(new VisualElement { pickingMode = PickingMode.Ignore }); rtSteps.style.flexDirection = FlexDirection.Row; rtSteps.style.flexWrap = Wrap.Wrap;
            rtSteps.style.marginBottom = -6;
            col.Put(Bar(432, Cyan, out rtFill));
            panelLayer.Add(reteachCard);
        }

        /// Reteach card (red) with step pills; SetReteachStep advances; HideReteach closes.
        /// Without SetReteachStep calls the steps advance on their own while the 3D replay plays.
        public void ShowReteach(string title, string body, string[] steps)
        {
            rtTitle.text = title; Set(rtBody, Prose(body ?? "")); rtNames = steps ?? Array.Empty<string>();
            rtManual = -1; rtStep = -1; rtT0 = Time.time;
            ApplyReteachStep(0); rtFill.style.width = 0;
            Show(reteachCard, true); RefreshPersistent();
        }

        /// No-op while the card is hidden, so shared demos can drive it for "See it again" too. index ≥ steps.Length = finished.
        public void SetReteachStep(int index) { if (!IsShown(reteachCard)) return; rtManual = Mathf.Max(0, index); ApplyReteachStep(rtManual); }

        /// Restarts the automatic step clock: the card appears during the "not quite" line, the 3D replay starts later.
        public void RestartReteachClock() { rtT0 = Time.time; rtManual = -1; rtStep = -1; ApplyReteachStep(0); }

        public void HideReteach() { Show(reteachCard, false); RefreshPersistent(); }

        void ApplyReteachStep(int s)
        {
            if (s == rtStep) return;
            rtStep = s; rtSteps.Clear();
            for (int j = 0; j < rtNames.Length; j++)
            {
                var p = j < s ? Pill(rtNames[j], Correct, IconId.Check) : j == s ? Pill(rtNames[j], Cyan, IconId.Play, 0.18f) : Pill(rtNames[j], TextMuted, null, 0.06f);
                p.style.marginRight = 6; p.style.marginBottom = 6; rtSteps.Add(p);
            }
            if (s > 0 && s < rtNames.Length) Play(Sfx.Beep, 0.35f);
        }

        void UpdateReteach()
        {
            int n = Mathf.Max(1, rtNames.Length);
            float prog;
            if (rtManual < 0)
            {
                float el = (Time.time - rtT0) / RtStepSeconds;
                ApplyReteachStep(Mathf.Min(n - 1, (int)el));
                prog = Mathf.Min(el / n, (n - 0.5f) / n);                // the clock never fills the bar: only the replay's end does
            }
            else prog = Mathf.Clamp01((rtManual + 1f) / n);
            rtFill.style.width = 432f * prog;
        }

        // ================================================================== equation
        VisualElement eqLayer, eqArrowLine, eqArrowHead, eqPulse, eqWordBox; Label eqTitle, eqWord, eqNote;
        readonly VisualElement[] eqParts = new VisualElement[7];
        int eqGen; bool eqArrowDone, eqBeside;
        // 6CO₂ · + · 6H₂O · light arrow · C₆H₁₂O₆ · + · 6O₂ — each flies in from where the learner discovered it
        static readonly float[] EqStart = { 0.5f, 0.8f, 0.95f, 1.45f, 2.45f, 2.7f, 2.85f };
        static readonly Vector2[] EqFrom = { new Vector2(-320, 140), new Vector2(0, 30), new Vector2(0, 280), new Vector2(0, -260), new Vector2(0, 60), new Vector2(0, 30), new Vector2(260, -140) };
        const float EqTotal = 4.4f;

        void BuildEquation()
        {
            eqLayer = Hidden(Layer(overlayLayer));
            eqTitle = Txt("Photosynthesis", TS.Display, Gold);
            eqTitle.style.textShadow = new TextShadow { offset = new Vector2(0, 2), blurRadius = 14, color = new Color(0, 0, 0, 0.7f) };
            CenterRow(eqLayer, 190).Add(eqTitle);

            var row = new FlexBox(true, 18) { pickingMode = PickingMode.Ignore };
            eqParts[0] = row.Put(Token(ArtId.CO2, "6CO₂", "Carbon dioxide", Palette.CO2));
            eqParts[1] = row.Put(Txt("+", TS.H1, TextSecondary));
            eqParts[2] = row.Put(Token(ArtId.H2O, "6H₂O", "Water", Water));
            var arrow = new FlexBox(false, 6) { pickingMode = PickingMode.Ignore }; arrow.style.alignItems = Align.Center;
            arrow.Put(new ArtElement(ArtId.Light, 56));
            // small glass plate (the tokens' fill) under the arrow label: still reads once EquationCompact lifts the row over the bright lamp/cable
            var plate = arrow.Put(new FlexBox(false, 2) { pickingMode = PickingMode.Ignore }); plate.style.alignItems = Align.Center;
            Pad(plate, 6, 14); Radius(plate, 14); Bg(plate, AG(Glass, 0.85f)); Border(plate, A(Palette.Light, 0.45f), 1);
            plate.Put(Txt("Light energy", TS.Readout, Palette.Light));
            var absorbed = plate.Put(Txt("absorbed by chlorophyll", TS.Small, TextSecondary));
            absorbed.style.textShadow = new TextShadow { offset = new Vector2(0, 1), blurRadius = 8, color = new Color(0, 0, 0, 0.85f) };
            var track = arrow.Put(new VisualElement { pickingMode = PickingMode.Ignore }); Size(track, 240, 24);
            eqArrowLine = Abs(new VisualElement { pickingMode = PickingMode.Ignore }, 4, 10); Size(eqArrowLine, 0, 4); Radius(eqArrowLine, 2); Bg(eqArrowLine, Palette.Light);
            Glow(eqArrowLine, Palette.Light, 2, 10, 0.5f);
            eqArrowHead = Abs(new ShapeElement("M2 2l16 10-16 10z", 24, 24, Palette.Light), 216, 0);
            eqPulse = Abs(Circle(10, Color.white, Color.clear, 0), 4, 7); eqPulse.pickingMode = PickingMode.Ignore; Glow(eqPulse, Palette.Light, 5, 12, 0.9f);
            track.Add(eqArrowLine); track.Add(eqArrowHead); track.Add(eqPulse);
            eqParts[3] = row.Put(arrow);
            eqParts[4] = row.Put(Token(ArtId.Glucose, "C₆H₁₂O₆", "Glucose", Palette.Glucose));
            eqParts[5] = row.Put(Txt("+", TS.H1, TextSecondary));
            eqParts[6] = row.Put(Token(ArtId.O2, "6O₂", "Oxygen", Oxygen));
            CenterRow(eqLayer, 330).Add(row);

            // known (b): word equation + note on one feathered plate (with the 0.62 dim, ≈5% of the scene shows through)
            var words = new FlexBox(false, 12) { pickingMode = PickingMode.Ignore }; words.style.alignItems = Align.Center;
            Pad(words, 14, 32); Radius(words, 28); Bg(words, A(Scene, 0.6f)); Glow(words, Scene, 28, 80, 0.6f);
            eqWord = words.Put(Txt("Carbon dioxide + Water  →  Glucose + Oxygen", TS.H1, Text));
            eqWord.style.textShadow = new TextShadow { offset = new Vector2(0, 2), blurRadius = 10, color = new Color(0, 0, 0, 0.7f) };
            eqNote = words.Put(Txt("Each piece came from a place you discovered: stomata, roots, sunbeam, chloroplast.", TS.Body, TextSecondary));
            eqNote.style.textShadow = new TextShadow { offset = new Vector2(0, 2), blurRadius = 10, color = new Color(0, 0, 0, 0.7f) };
            eqWordBox = words; CenterRow(eqLayer, 626).Add(words);
        }

        static VisualElement Token(ArtId art, string formula, string name, Color c)
        {
            var t = new FlexBox(false, 6) { pickingMode = PickingMode.Ignore };
            t.style.alignItems = Align.Center; Size(t, 150); Pad(t, 14, 16); Radius(t, 18);
            Bg(t, AG(Glass, 0.85f)); Border(t, A(c, 0.7f), 1.5f);
            Glow(t, c, 18, 18, 0.35f);
            t.Put(new ArtElement(art, 64));
            t.Put(Txt(formula, TS.H2, c));
            t.Put(Txt(name, TS.Label, TextSecondary));
            return t;
        }

        /// Animated equation: tokens fly in, arrow + "LIGHT ENERGY absorbed by chlorophyll", word equation.
        public IEnumerator Equation()
        {
            int my = ++eqGen;
            var played = new bool[eqParts.Length + 2];
            eqBeside = IsShown(reteachCard);
            eqLayer.style.left = eqBeside ? 520 : 0;                       // reteach: centre in the space right of the card
            eqLayer.style.translate = StyleKeyword.Null;                   // undo a previous EquationCompact
            Show(eqLayer, true); Dim(DimSrc.Equation, 0.62f); RefreshPersistent();
            for (float t = 0; ; t += Time.deltaTime)
            {
                if (my != eqGen) yield break;
                if (Module.Skipping || ReduceMotion) t = EqTotal;
                ApplyEquation(t, played);
                if (t >= EqTotal) break;
                yield return null;
            }
        }

        void ApplyEquation(float t, bool[] played)
        {
            float k = Mathf.Clamp01(t / 0.7f);
            eqTitle.style.opacity = k; eqTitle.style.letterSpacing = Mathf.Lerp(16f, DisplaySpacing, Fx.Eval(k, Ease.Out));
            for (int i = 0; i < eqParts.Length; i++)
            {
                float u = Mathf.Clamp01((t - EqStart[i]) / 0.55f), e = Fx.Eval(u, Ease.Back);
                var p = eqParts[i];
                p.style.opacity = Mathf.Clamp01(u * 2.5f);
                p.style.translate = new Translate(EqFrom[i].x * (1 - e), EqFrom[i].y * (1 - e));
                p.style.scale = new Scale(Vector2.one * Mathf.LerpUnclamped(0.6f, 1f, e));
                if (u > 0.35f && !played[i]) { played[i] = true; if (i % 2 == 0 || i == 3) Play(i == 3 ? Sfx.EnergyBuild : Sfx.Pop, 0.6f); }
            }
            float a = Fx.Eval(Mathf.Clamp01((t - 1.7f) / 0.7f), Ease.Out);
            eqArrowLine.style.width = 214f * a; eqArrowHead.style.opacity = a;
            eqArrowDone = a >= 1f; if (!eqArrowDone) eqPulse.style.opacity = 0;
            float w = Mathf.Clamp01((t - 3.4f) / 0.5f), n = Mathf.Clamp01((t - 3.75f) / 0.5f);
            eqWordBox.style.opacity = w; eqWordBox.style.translate = new Translate(0, 12f * (1 - w));   // the plate never hangs empty while the tokens fly in
            eqNote.style.opacity = n;
            if (w > 0 && !played[7]) { played[7] = true; Play(Sfx.Sparkle, 0.6f); }
        }

        public void HideEquation()
        {
            eqGen++; eqArrowDone = eqBeside = false; Show(eqLayer, false); Dim(DimSrc.Equation, 0); RefreshPersistent();
            eqLayer.schedule.Execute(() => { if (!IsShown(eqLayer)) eqLayer.style.translate = StyleKeyword.Null; }).StartingIn(ReduceMotion ? 120 : 320);   // after the fade-out
        }

        /// Scene 13 "two highways": the equation steps aside for the vessels. Title, word line and note fade, the token
        /// row lifts above the leaf crown (eqLayer is .ps-anim, so it slides over 0.3 s) and the dim drops so the
        /// xylem/phloem glow reads. Undone by HideEquation and by the next Equation().
        public void EquationCompact()
        {
            if (!IsShown(eqLayer) || eqBeside) return;
            eqLayer.style.translate = new Translate(0, -150);   // tokens 330-475 → 180-325, clear of TL/R panels
            Dim(DimSrc.Equation, 0.2f);                        // .ps-dim eases 0.62 → 0.2 over 0.45 s
            StartCoroutine(FadeEqText(eqGen));
        }

        IEnumerator FadeEqText(int my)
        {
            for (float t = 0; ; t += Time.deltaTime)
            {
                if (my != eqGen) yield break;
                float a = ReduceMotion ? 0f : 1f - Mathf.Clamp01(t / 0.3f);
                eqTitle.style.opacity = a; eqWordBox.style.opacity = a;
                if (a <= 0f) yield break;
                yield return null;
            }
        }

        // ================================================================== xylem vs phloem
        VisualElement cmpPill, legend; readonly VisualElement[] cmpSegs = new VisualElement[3]; readonly VisualElement[] legendRows = new VisualElement[2];
        Action<int> cmpCb;

        void BuildComparison()
        {
            cmpPill = Hidden(Panel(999, 0.86f)); cmpPill.style.flexDirection = FlexDirection.Row; Pad(cmpPill, 6, 6);
            (string, IconId, Color)[] segs = { ("Show Xylem", IconId.Xylem, Palette.Xylem), ("Show Phloem", IconId.Phloem, Palette.Phloem), ("Show Both", IconId.Eye, GreenBright) };
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                var s = cmpSegs[i] = Stadium(new VisualElement()); s.AddToClassList("ps-seg"); s.AddToClassList("ps-seg--green"); Pad(s, 13, 18);
                if (i > 0) s.style.marginLeft = 6;
                s.Add(Icon(segs[i].Item2, 18, segs[i].Item3));
                var l = Txt(segs[i].Item1, TS.Button, Text); l.style.marginLeft = 8; s.Add(l);
                Click(s, () => { SelectCmp(idx); cmpCb?.Invoke(idx); }, Sfx.UiSelect);
                cmpPill.Add(s);
            }
            CenterRow(panelLayer, 140).Add(cmpPill);

            legend = Hidden(Abs(Panel(18, 1f), 40, 300)); Size(legend, 360); Pad(legend, 20, 20, 20, 20);
            var col = new FlexBox(false, 16); legend.Add(col);
            col.Put(Txt("Transport systems", TS.Label, TextSecondary));
            (string, Color, IconId, string)[] rows = { ("Xylem", Palette.Xylem, IconId.ArrowUp, "Water & minerals · roots → leaves (upward only)"), ("Phloem", Palette.Phloem, IconId.ArrowDown, "Food (glucose) · leaves → roots, shoots, flowers, fruits & storage") };
            for (int i = 0; i < 2; i++)
            {
                var r = legendRows[i] = col.Put(new FlexBox(true, 12));
                r.style.transitionProperty = new List<StylePropertyName> { new StylePropertyName("opacity") };
                r.style.transitionDuration = new List<TimeValue> { new TimeValue(0.25f) };
                ((FlexBox)r).Put(Circle(40, A(rows[i].Item2, 0.18f), rows[i].Item2, 2, Icon(rows[i].Item3, 20, rows[i].Item2)));
                var c2 = ((FlexBox)r).Put(new FlexBox(false, 2));
                c2.Put(Txt(rows[i].Item1, TS.H3, rows[i].Item2)); c2.Put(Txt(rows[i].Item4, TS.Small, TextSecondary, 268));   // 360 − 2×20 − 40 ring − 12 gap
            }
            panelLayer.Add(legend);
        }

        void SelectCmp(int mode)
        {
            for (int i = 0; i < 3; i++) cmpSegs[i].EnableInClassList("ps-seg--on", i == mode);
            legendRows[0].style.opacity = mode == 1 ? 0.35f : 1f;
            legendRows[1].style.opacity = mode == 0 ? 0.35f : 1f;
        }

        /// Xylem vs Phloem toggle: 0 xylem · 1 phloem · 2 both.
        public void ShowComparison(Action<int> onMode)
        {
            cmpCb = onMode; SelectCmp(2);
            Show(cmpPill, true); Show(legend, true); RefreshPersistent();
        }

        public void HideComparison() { cmpCb = null; Show(cmpPill, false); Show(legend, false); RefreshPersistent(); }

        // ================================================================== factory controls (working-plant experiment)
        VisualElement expPanel; readonly PsToggle[] expToggles = new PsToggle[4]; readonly VisualElement[] expRings = new VisualElement[4];
        readonly VisualElement[] expFills = new VisualElement[2]; readonly Label[] expPct = new Label[2];
        readonly float[] expCur = new float[2], expTarget = new float[2]; readonly int[] expShownPct = { -1, -1 };
        Action<int, bool> expCb; Action expContinue;
        const float ExpBarW = 320f;

        void BuildExperiment()
        {
            expPanel = Hidden(Panel(18, 1f)); Size(expPanel, 360); Pad(expPanel, 20, 20, 20, 20);
            var col = new FlexBox(false, 14); expPanel.Add(col);
            var head = col.Put(new FlexBox(true, 12));
            head.Put(Circle(40, A(Gold, 0.16f), A(Gold, 0.8f), 1.5f, Icon(IconId.Settings, 20, Gold)));
            var hc = head.Put(new FlexBox(false, 2)); hc.style.flexShrink = 1;
            hc.Put(Txt("Factory controls", TS.Label, TextSecondary));
            hc.Put(Txt("Switch a supply off.\nWhat happens to the food?", TS.Small, TextSecondary, 256));
            (string, string, IconId, Color)[] rows =
            {
                ("Sunlight", "Energy for the chloroplasts", IconId.Sun, Palette.Light), ("Carbon dioxide", "Enters through the stomata", IconId.CO2, Palette.CO2),
                ("Water (xylem)", "Carried up from the roots", IconId.Xylem, Palette.Xylem), ("Phloem", "Carries food out of the leaves", IconId.Phloem, Palette.Phloem)
            };
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                var r = col.Put(new FlexBox(true, 12));
                expRings[i] = r.Put(Circle(40, A(rows[i].Item4, 0.18f), rows[i].Item4, 2, Icon(rows[i].Item3, 20, rows[i].Item4)));
                var rc = r.Put(new FlexBox(false, 2)); rc.style.flexGrow = 1; rc.style.flexShrink = 1;
                rc.Put(Txt(rows[i].Item1, TS.H3, Text)); rc.Put(WrapText(Txt(rows[i].Item2, TS.Small, TextSecondary)));
                expToggles[i] = r.Put(new PsToggle(true, v => { StyleExpRow(idx, v); expCb?.Invoke(idx, v); }));
                r.style.minHeight = 48; r.style.alignItems = Align.Center; expToggles[i].pickingMode = PickingMode.Ignore; expToggles[i].focusable = false; r.AddToClassList("ps-exprow");   // the whole 320×48 row is the touch target, not the 46×26 switch
                Click(r, () => { var t = expToggles[idx]; t.Value = !t.Value; StyleExpRow(idx, t.Value); expCb?.Invoke(idx, t.Value); }, Sfx.UiSelect);
            }
            var rule = col.Put(new VisualElement { pickingMode = PickingMode.Ignore }); Size(rule, ExpBarW, 1); Bg(rule, A(GlassStroke, 0.18f));
            for (int i = 0; i < 2; i++)
            {
                var mc = col.Put(new FlexBox(false, 8));
                var mh = mc.Put(new FlexBox(true, 8));
                mh.Put(Icon(i == 0 ? IconId.Glucose : IconId.Phloem, 16, Palette.Glucose));
                mh.Put(Txt(i == 0 ? "Food made" : "Food delivered", TS.Label, Text));
                mh.Put(Txt(i == 0 ? "in the leaves" : "via phloem", TS.Caption, TextMuted)).style.flexGrow = 1;
                expPct[i] = mh.Put(Txt("0%", TS.Caption, Gold));
                mc.Put(Bar(ExpBarW, Gold, out expFills[i]));
            }
            col.Put(Wide(Btn("Back to the plant", ButtonKind.Gold, IconId.ChevronRight, () => expContinue?.Invoke())));
            var wrap = Abs(new VisualElement { pickingMode = PickingMode.Ignore }, 40, 0, float.NaN, 0);   // left edge, vertically centred
            wrap.style.justifyContent = Justify.Center; wrap.style.alignItems = Align.FlexStart;
            wrap.Add(expPanel); panelLayer.Add(wrap);
        }

        void StyleExpRow(int i, bool on) => expRings[i].style.opacity = on ? 1f : 0.35f;

        void ApplyMeter(int i)
        {
            expFills[i].style.width = ExpBarW * expCur[i];
            int pct = Mathf.RoundToInt(expCur[i] * 100f);
            if (pct == expShownPct[i]) return;
            expShownPct[i] = pct; expPct[i].text = pct + "%"; expPct[i].style.color = pct < 15 ? Incorrect : Gold;
        }

        void UpdateExperiment()
        {
            float k = 1f - Mathf.Exp(-3f * Time.deltaTime);
            for (int i = 0; i < 2; i++)
            {
                if (expCur[i] == expTarget[i]) continue;
                expCur[i] = Mathf.Abs(expTarget[i] - expCur[i]) < 0.003f ? expTarget[i] : Mathf.Lerp(expCur[i], expTarget[i], k);
                ApplyMeter(i);
            }
        }

        /// "Factory controls" (left, vertically centred): supply toggles 0 Sunlight · 1 Carbon dioxide · 2 Water (xylem) · 3 Phloem,
        /// all on; FOOD MADE / FOOD DELIVERED meters; Gold "Back to the plant" → onContinue. A learner flip → onToggle(index, isOn).
        public void ShowExperiment(Action<int, bool> onToggle, Action onContinue)
        {
            expCb = onToggle; expContinue = onContinue;
            for (int i = 0; i < 4; i++) SetExperimentToggle(i, true);
            for (int i = 0; i < 2; i++) { expCur[i] = 0f; expTarget[i] = 1f; ApplyMeter(i); }   // the factory spins up as the panel opens
            Show(expPanel, true); RefreshPersistent();
        }

        /// Meter targets 0..1; the bars ease toward them.
        public void SetFoodMeters(float made, float delivered) { expTarget[0] = Mathf.Clamp01(made); expTarget[1] = Mathf.Clamp01(delivered); }

        /// Sets a toggle's look without invoking the callback (AutoPlay).
        public void SetExperimentToggle(int index, bool on)
        {
            if (index < 0 || index >= expToggles.Length) return;
            expToggles[index].Value = on; StyleExpRow(index, on);
        }

        public void HideExperiment() { expCb = null; expContinue = null; Show(expPanel, false); RefreshPersistent(); }

        // ================================================================== case solved
        VisualElement completeCard, completeBtn; ArtElement completeSun; Label completeScore; int completeGen, completeChoice; SparkleElement completeFx;

        void BuildComplete()
        {
            completeCard = Hidden(Panel(20, 0.92f, A(Gold, 0.9f), 2)); Size(completeCard, 620); Pad(completeCard, 32, 32, 32, 32);
            Glow(completeCard, Gold, 20, 34, 0.55f);
            completeCard.Add(completeFx = new SparkleElement());   // celebration under the card text, over the glass
            var col = new FlexBox(false, 16); col.style.alignItems = Align.Center; completeCard.Add(col);
            completeSun = col.Put(new ArtElement(ArtId.ClueSun, 96));
            var title = col.Put(Txt("Case solved", TS.Display, Gold));
            title.style.textShadow = new TextShadow { offset = Vector2.zero, blurRadius = 10, color = A(Gold, 0.3f) };   // small glow on the dark card
            var body = col.Put(Txt("Investigation complete! You caught a leaf making its own food.", TS.Body, TextSecondary, 540));
            body.style.unityTextAlign = TextAnchor.UpperCenter;
            var list = col.Put(new FlexBox(false, 8));
            for (int i = 0; i < 3; i++) { var it = MakeProg((ClueId)i, false); StyleProg(it, ClueState.Discovered, false); list.Put(it.root); }
            var chips = col.Put(new VisualElement { pickingMode = PickingMode.Ignore });
            chips.style.flexDirection = FlexDirection.Row; chips.style.flexWrap = Wrap.Wrap; chips.style.justifyContent = Justify.Center; Size(chips, 556);
            foreach (var c in new[] { "Stomata", "Roots", "Xylem", "Light energy", "Chloroplast", "Chlorophyll", "Glucose", "Oxygen", "Phloem" })
            {
                var p = Pill(c, Correct, IconId.Check, 0.12f); p.style.marginLeft = p.style.marginRight = 4; p.style.marginBottom = 8; chips.Add(p);
            }
            completeScore = col.Put(Txt("", TS.Caption, TextMuted));
            var b = col.Put(new FlexBox(true, 10));
            completeBtn = b.Put(Btn("Free Explore", ButtonKind.Gold, IconId.Eye, () => completeChoice = 0));   // one button per outcome
            b.Put(Btn("Exit", ButtonKind.Ghost, null, () => OpenSystem(exitPanel)));   // leaves the module: same confirm as Pause ▸ Exit and the Free Explore Exit
            CenterRow(overlayLayer, 150).Add(completeCard);
        }

        static string LevelName(Level l) => l == Level.High ? "High-flyer" : l == Level.Average ? "On track" : "Building up";

        // ================================================================== end menu (after "This is Photosynthesis.")
        VisualElement endCard, endBtn; int endGen, endChoice;

        /// Figma "End menu" (UI_SPEC): a quiet gold-stroked glass card on the black, no narration. Replay · Review the plant · Exit.
        void BuildEndMenu()
        {
            endCard = Hidden(Panel(20, 0.94f, A(Gold, 0.8f), 1.5f)); Size(endCard, 560); Pad(endCard, 32, 32, 32, 32);
            Glow(endCard, Gold, 20, 34, 0.4f);
            var col = new FlexBox(false, 18); col.style.alignItems = Align.Center; endCard.Add(col);
            col.Put(new ArtElement(ArtId.ClueSun, 80));
            var title = col.Put(Txt("Investigation complete", TS.H1, Gold));
            title.style.textShadow = new TextShadow { offset = Vector2.zero, blurRadius = 10, color = A(Gold, 0.3f) };
            var body = col.Put(Txt("You caught a leaf making its own food. Watch it again, keep exploring the plant, or leave.", TS.Body, TextSecondary, 480));
            body.style.unityTextAlign = TextAnchor.UpperCenter;
            var b = col.Put(new FlexBox(true, 10));
            b.Put(Btn("Replay", ButtonKind.Gold, IconId.Replay, () => endChoice = 0));
            endBtn = b.Put(Btn("Review the plant", ButtonKind.Secondary, IconId.Eye, () => endChoice = 1));   // default focus: Enter never restarts the whole module
            b.Put(Btn("Exit", ButtonKind.Ghost, IconId.Exit, () => OpenSystem(exitPanel)));   // the same confirm as every other Exit (a mis-tap must not close the app)
            CenterRow(overlayLayer, 330).Add(endCard);
        }

        /// result: 0 Replay · 1 Review (Free Explore) · 2 Exit. Waits for the choice (CancelWaits / HideTransient end it).
        public IEnumerator EndMenu(Action<int> result)
        {
            int my = ++endGen; endChoice = -1;
            Show(endCard, true); FocusSoon(endBtn);
            while (my == endGen && endChoice < 0) yield return null;
            if (my != endGen) yield break;
            int c = endChoice; HideEndMenu(); result?.Invoke(c);
        }

        void HideEndMenu() { endGen++; Show(endCard, false); }

        /// Investigation complete. result: 0 Free Explore (Exit opens the exit confirm → ExitRequested → Story.Quit).
        public IEnumerator Complete(int correct, int total, Level level, Action<int> result)
        {
            int my = ++completeGen; completeChoice = -1;
            completeScore.text = total > 0 ? $"Right first time, no hints: {correct} / {total} · Your level: {LevelName(level)}" : $"Your level: {LevelName(level)}";
            HideLine();                                                   // the solved line has finished; it must not linger under the card
            Show(completeCard, true); Dim(DimSrc.Complete, 0.6f); FocusSoon(completeBtn);
            Play(Sfx.Sparkle, 0.8f);
            bool burst = false;   // from the sun once the card has laid out
            while (my == completeGen && completeChoice < 0)
            {
                if (!burst) { var wb = completeSun.worldBound; if (wb.width > 1 && !float.IsNaN(wb.x)) { burst = true; completeFx.Burst(completeFx.WorldToLocal(wb.center), Gold, Color.white, 110, 620f, 80f); } }
                yield return null;
            }
            if (my != completeGen) yield break;
            int c = completeChoice; HideComplete(); result?.Invoke(c);
        }

        void HideComplete() { completeGen++; Show(completeCard, false); Dim(DimSrc.Complete, 0); }

        // ================================================================== free explore
        VisualElement exploreBar, exitExploreBtn; readonly VisualElement[] xTiles = new VisualElement[9]; readonly Label[] xLabels = new Label[9];
        readonly VisualElement[] followBtns = new VisualElement[4];
        static readonly string[] FollowKeys = { "FollowWater", "FollowCO2", "FollowO2", "FollowFood" };
        Action<string> explorePick;
        static readonly string[] XKeys = { "WholePlant", "Roots", "Stem", "Leaf", "Stomata", "Xylem", "Phloem", "Cell", "Chloroplast" };
        static readonly string[] XNames = { "Whole Plant", "Roots", "Stem", "Leaf", "Stomata", "Xylem", "Phloem", "Plant Cell", "Chloroplast" };
        static readonly IconId[] XIcons = { IconId.Plant, IconId.Root, IconId.Stem, IconId.Leaf, IconId.Stomata, IconId.Xylem, IconId.Phloem, IconId.Cell, IconId.Chloroplast };

        void BuildExplore()
        {
            exploreBar = Hidden(Panel(20, 0.88f)); Size(exploreBar, 1240); Pad(exploreBar, 18, 22, 18, 22);
            exploreBar.style.flexShrink = 1;                             // narrow screens / open chat: shrink rather than slide back under the chat
            var col = new FlexBox(false, 14); exploreBar.Add(col);
            var head = col.Put(new FlexBox(true, 12));
            head.Put(Txt("Choose a part of the plant", TS.H3, Text)).style.flexGrow = 1;
            head.Put(Txt("Drag to rotate · + / − to zoom", TS.Small, TextMuted));
            var tiles = col.Put(new VisualElement { pickingMode = PickingMode.Ignore });
            tiles.style.flexDirection = FlexDirection.Row; tiles.style.justifyContent = Justify.SpaceBetween;
            for (int i = 0; i < 9; i++)
            {
                int idx = i;
                var t = xTiles[i] = new VisualElement(); t.AddToClassList("ps-xtile");
                var box = new VisualElement(); box.AddToClassList("ps-xtile-box"); box.Add(Icon(XIcons[i], 30, GreenBright)); t.Add(box);
                t.Add(xLabels[i] = Txt(XNames[i], TS.Caption, TextSecondary)); xLabels[i].style.unityTextAlign = TextAnchor.MiddleCenter;
                Click(t, () => PickTile(idx), Sfx.UiSelect);
                tiles.Add(t);
            }
            var fol = col.Put(new FlexBox(true, 10));
            fol.Put(Txt("Follow", TS.Label, TextSecondary)).style.marginRight = 4;
            (string, IconId)[] follow = { ("Follow water", IconId.Drop), ("Follow CO₂", IconId.CO2), ("Follow oxygen", IconId.O2), ("Follow food", IconId.Glucose) };
            for (int i = 0; i < 4; i++) { var key = FollowKeys[i]; followBtns[i] = fol.Put(Btn(follow[i].Item1, ButtonKind.Secondary, follow[i].Item2, () => { SelectExplore(key); explorePick?.Invoke(key); })); }
            fol.Put(Btn("Factory controls", ButtonKind.Secondary, IconId.Energy, () => explorePick?.Invoke("Experiment"))).style.marginLeft = 8;   // Scene 14's switches, now an optional review activity
            exploreRow.Add(exploreBar);
            exploreBar.RegisterCallback<GeometryChangedEvent>(_ => RelayoutBottom());

            // Exit leaves the module, so it goes through the same confirm as Pause ▸ Exit.
            exitExploreBtn = Hidden(Abs(Btn("Exit", ButtonKind.Ghost, IconId.Exit, () => OpenSystem(exitPanel)), float.NaN, 120, 40));
            Bg(exitExploreBtn, AG(Glass, 0.7f));
            hudLayer.Add(exitExploreBtn);
        }

        void PickTile(int i) { SelectTile(i); explorePick?.Invoke(XKeys[i]); }

        void SelectTile(int i)
        {
            for (int j = 0; j < 9; j++) { xTiles[j].EnableInClassList("ps-xtile--on", j == i); xLabels[j].style.color = j == i ? Text : TextSecondary; }
            SelectFollow(-1);
        }

        void SelectFollow(int f) { for (int j = 0; j < followBtns.Length; j++) followBtns[j]?.EnableInClassList("ps-btn--on", j == f); }

        /// Lights the tile or Follow button for a destination picked here or elsewhere (Ask AI, autoplay).
        public void SelectExplore(string key)
        {
            SelectTile(Array.IndexOf(XKeys, key));        // also clears the Follow row
            int f = Array.IndexOf(FollowKeys, key);
            if (f >= 0) SelectFollow(f);
        }

        /// Free Explore bar: destinations + Follow buttons + zoom path. Keys: "WholePlant","Roots","Stem","Leaf","Stomata","Xylem","Phloem","Cell","Chloroplast","FollowWater","FollowCO2","FollowO2","FollowFood".
        /// The Exit button opens the exit confirm (→ ExitRequested); onExit is kept for source compatibility and not called.
        public void ShowFreeExplore(Action<string> onPick, Action onExit)
        {
            explorePick = onPick; SelectTile(0); StylePath(0);
            Show(exploreBar, true); Show(zoomPathPill, true); Show(exitExploreBtn, true); RelayoutBottom(); PlaceMag(); PlaceScanner();
        }

        public void HideFreeExplore()
        {
            explorePick = null;
            Show(exploreBar, false); Show(zoomPathPill, false); Show(exitExploreBtn, false); RelayoutBottom(); PlaceMag(); PlaceScanner();
        }

        /// The magnification pill and the zoom-path breadcrumb share top-centre: stack the pill under the breadcrumb.
        void PlaceMag() => magRow.style.top = IsShown(zoomPathPill) ? 112 : 40;   // under the 44 px-tall breadcrumb

        // ================================================================== transitions
        VisualElement loadingRoot, loadFill; RingElement loadRing; Label loadLabel, loadTitle, loadFact; int loadGen;

        void SetFade(float a)
        {
            fadeAlpha = a; fadeEl.style.opacity = a;
            fadeEl.style.display = a > 0.001f ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public IEnumerator Fade(float to, float duration, Color? color = null)
        {
            if (color.HasValue) { var c = color.Value; c.a = 1f; fadeEl.style.backgroundColor = c; }
            float from = fadeAlpha;
            yield return Fx.Tween(duration, k => SetFade(Mathf.Lerp(from, to, k)));
        }

        void BuildLoading()
        {
            loadingRoot = Hidden(Abs(new VisualElement(), 0, 0, 0, 0)); loadingRoot.AddToClassList(Scrim);   // blocks input while loading; full-bleed: fade only
            Bg(loadingRoot, AG(Scene, 0.93f));
            loadRing = new RingElement(); Size(loadRing, 640, 640);
            CenterRow(loadingRoot, 470 - 320).Add(loadRing);
            CenterRow(loadingRoot, 398).Add(loadLabel = Txt("", TS.Label, Cyan));
            loadTitle = Txt("", TS.Display, Text);
            loadTitle.style.textShadow = new TextShadow { offset = new Vector2(0, 2), blurRadius = 12, color = new Color(0, 0, 0, 0.6f) };
            CenterRow(loadingRoot, 432).Add(loadTitle);
            CenterRow(loadingRoot, 530).Add(Bar(360, Cyan, out loadFill));
            CenterRow(loadingRoot, 560).Add(loadFact = Txt("", TS.Body, TextSecondary));
            loadingLayer.Add(loadingRoot);
        }

        public IEnumerator Loading(string label, string title, string fact, float duration)
        {
            int my = ++loadGen;
            Set(loadLabel, label); Set(loadTitle, title); Set(loadFact, Prose(fact));
            loadFact.style.display = string.IsNullOrEmpty(fact) ? DisplayStyle.None : DisplayStyle.Flex;
            loadFill.style.width = 0;
            Show(loadingRoot, true);
            for (float t = 0; t < duration && my == loadGen && !Module.Skipping; t += Time.deltaTime)
            {
                loadFill.style.width = 360f * Fx.Eval(t / Mathf.Max(duration, 0.01f), Ease.InOut);
                loadRing.Phase = Time.unscaledTime;
                yield return null;
            }
            if (my == loadGen) { loadFill.style.width = 360f; HideLoading(); }
        }

        void HideLoading() { loadGen++; Show(loadingRoot, false); }
    }
}
