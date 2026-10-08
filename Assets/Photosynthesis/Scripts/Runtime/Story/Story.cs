using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Photosynthesis
{
    /// The module's script: Scenes 2–15 as restartable beats. Every beat can cold-start (debug jumps) because
    /// Prepare() + Ensure*() rebuild the state it needs. Progression waits on dialogue, learner input and world
    /// coroutines — never on chains of fixed sleeps.
    public class Story : MonoBehaviour
    {
        enum Place { None, Chamber, LeafSurface, Soil, Xylem, Tissue, Cell, Chloroplast, Interior }

        /// Test mode (-autoplay): auto-selects clues, answers questions, clicks buttons. Used for end-to-end runs.
        public bool AutoPlay { get; set; }
        /// AutoPlay answers incorrectly once per question (exercises the reteach branch).
        public bool AutoPlayWrongFirst { get; set; }
        public string ContextTopic { get; private set; } = "the Botanical Investigation Chamber";
        public bool Running => runner != null;
        /// The question card currently open (null otherwise). AskAI answers in hint-only mode while it is set.
        public Question OpenQuestion { get; private set; }
        /// AskAI calls this when the learner asks Dr. Curio anything while a card is open: a correct answer then earns no credit.
        public void NoteHelp() { if (OpenQuestion != null) helped = true; }

        Module m; Hud hud; CameraDirector cam; AudioDirector au; Dialogue dlg; HeroPlant plant; Chamber ch; Curio curio;
        Coroutine runner; Place place = Place.None; ClueId? forcedClue; bool forcedPick; string lastLineId;
        bool chloroplastOpen, glucoseFormed, helped, pickPending, enterLeafWaiting;
        bool skylightWaiting, skylightHit, skyMiss;   // Scene 8: a click on the skylight (InspectPart) or an AI "show me sunlight" (WorldAction) sets skylightHit; skyMiss: a click that missed it brings the hint forward
        bool feedWaiting, feedAssist;   // Scene 10: the SEND THEM IN button or an AI "show me CO2/water" sets feedAssist (sends the rest in)
        // Handlers subscribe once in Start and route through these fields: a coroutine killed by Run() can leave nothing behind.
        ClueId? hubWant; bool hubGo; float userZoom; int inspectGen;
        string pick; int exploreGen;   // Free Explore destination (tiles + AI); the newest generation wins
        Coroutine demo;                // the one AI demo allowed at a time (Hub waits for it before moving on)
        bool fadeIn;                   // Finale → Replay / Review: Play lifts the black under the first beat

        bool inited;
        void Start()
        {
            if (inited) return; inited = true;
            m = Module.I; hud = m.hud; cam = m.cam; au = m.audioDirector; dlg = m.dialogue; plant = m.plant; ch = m.chamber; curio = m.curio;
            m.clues.Changed += (id, s) => hud.SetClueState(id, s, s == ClueState.Discovered);
            hud.ResetRequested += () => { m.assessment.Reset(); Run(Beat.Scene2Start); };
            hud.ExitRequested += Quit;
            hud.ZoomButton += d => cam.Zoom(d);
            hud.ClueClicked += id => { if (hubWant == null) return; if (id == hubWant) hubGo = true; else au.Play(Sfx.Wrong, 0.4f); };
            if (cam) { cam.UiBlocks = hud.PointerOverUI; cam.UserZoomed += d => { if (d > 0) userZoom += d; }; cam.Clicked += InspectPart;
                cam.BoundCentre = ch.transform.position; cam.BoundExtents = new Vector3(12f, 10.3f, 12f); }   // orbit lens stays inside the rib fins (~12.35 m) and under the skylight/lamp ring (y 10.5–10.9); micro worlds are 2 km away, so it never applies there
        }

        // ================================================================== control
        public void Run(Beat from)
        {
            Start();
            StopAllCoroutines();
            runner = StartCoroutine(Play(from));
        }

        IEnumerator Play(Beat from)
        {
            Prepare(from);
            bool lift = fadeIn; fadeIn = false;
            if (lift) StartCoroutine(hud.Fade(1f, 0f, Color.black));   // from the end menu: black on this same frame (Prepare cleared the fade)
            yield return null;
            yield return m.PrebuildWorlds();   // launch only: one micro world per frame on the dark, unformed opening; no-op once built
            if (lift) Parallel(hud.Fade(0f, 1.6f));   // the picture comes back under the first beat
            // the lesson ends on "This is Photosynthesis." (FinalPlant → Finale). Assessment / Complete / Free Explore stay reachable from the
            // end menu (Review) and the debug panel, so a jump past FinalPlant still runs on through to Free Explore.
            var last = from > Beat.FinalPlant ? Beat.FreeExplore : Beat.FinalPlant;
            for (var b = from; b <= last; b++)
            {
                m.SetBeat(b);
                hud.ExpireLine();   // a finished line never carries into the next beat; the beat's opening Say (same frame) replaces it without a hide/show blink
                yield return Step(b);
            }
            runner = null;
        }

        IEnumerator Step(Beat b)
        {
            switch (b)
            {
                case Beat.Scene2Start: return Scene2();
                case Beat.Chamber: return ChamberLook();
                case Beat.CurioIntro: return CurioIntro();
                case Beat.Clue1: return Clue1();
                case Beat.Stomata: return Stomata();
                case Beat.CO2: return CO2();
                case Beat.Clue2: return Clue2();
                case Beat.Roots: return Roots();
                case Beat.Water: return Water();
                case Beat.XylemRide: return XylemRide();
                case Beat.Clue3: return Clue3();
                case Beat.Sunlight: return Sunlight();
                case Beat.EnterLeaf: return EnterLeaf();
                case Beat.PlantCell: return PlantCell();
                case Beat.Chloroplast: return ChloroplastBeat();
                case Beat.Reaction: return Reaction();
                case Beat.Glucose: return Glucose();
                case Beat.Oxygen: return Oxygen();
                case Beat.Phloem: return Phloem();
                case Beat.Equation: return Equation();
                case Beat.FinalPlant: return FinalPlant();
                case Beat.Assessment: return FinalAssessment();
                case Beat.Complete: return Complete();
                default: return FreeExplore();
            }
        }

        /// Rebuild the world state a beat expects (cold start / reset / debug jump).
        void Prepare(Beat b)
        {
            Time.timeScale = 1f;
            hud.CancelWaits(); hud.HideTransient(); hud.HideLine();
            au.StopVoice(); dlg.Cancel(); lastLineId = null;
            m.clues.ResetFor(b);
            chloroplastOpen = glucoseFormed = false; forcedClue = null; forcedPick = false;
            OpenQuestion = null; helped = false; pickPending = enterLeafWaiting = skylightWaiting = skylightHit = skyMiss = feedWaiting = feedAssist = false; hubWant = null; pick = null; demo = null;
            plant.ResetVisuals(); ch.ResetVisuals();
            foreach (var w in Worlds()) w.Show(false);
            ch.SetVisible(true); place = Place.Chamber;
            bool formed = b > Beat.Scene2Start;
            ch.SetFormed(formed); plant.SetGrown(formed);
            if (b > Beat.Sunlight) ch.SetSkylightOpen();   // Scene 8 left it open
            curio.SetVisible(b > Beat.CurioIntro); curio.Companion(false); curio.Talking = false;
            if (b > Beat.CurioIntro) curio.Place(CurioSpot, PlantFocus);
            hud.SetHudVisible(b > Beat.CurioIntro); hud.SetAskAIVisible(b > Beat.CurioIntro);
            hud.SetProgressVisible(b > Beat.CurioIntro && b < Beat.FreeExplore);
            hud.SetZoomControlVisible(b > Beat.CurioIntro); hud.SetCinematic(false);
            hud.ShowZoomPath(b == Beat.PlantCell ? 2 : b >= Beat.Chloroplast && b <= Beat.Oxygen ? 3 : b == Beat.Phloem ? 1 : -1);   // debug jumps land on the story's breadcrumb level
            m.HubOpen = false; cam.AutoSpin = 0f;
            if (formed) { cam.Cut(Establish, PlantFocus, 50f); cam.Orbit(PlantFocus, OrbitDist, 0f, 10f, false, 3f, 11.5f); }   // no eased flight in from a micro world
            au.Ambience(formed ? "chamber" : "none");
        }

        IEnumerable<MicroWorld> Worlds() { yield return m.leafSurface; yield return m.soil; yield return m.xylem; yield return m.tissue; yield return m.cell; yield return m.chloroplast; yield return m.interior; }

        void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ================================================================== helpers
        Vector3 PlantFocus => plant.Base + Vector3.up * ((plant.Top.y - plant.Base.y) * 0.5f);
        Vector3 CurioSpot => plant.Base + new Vector3(3.4f, 1.7f, -1.9f);
        Vector3 Establish => plant.Base + new Vector3(0f, 2.9f, -10.2f);
        const float OrbitDist = 8.8f;   // inside the palm ring (crowns reach r ≈ 8.5–14)
        const float WholeFov = 56f;   // whole plant, crown to roots, inside the HUD band; widen the lens, not the orbit (palm/banana trunks stand at r ≈ 10–10.9)

        IEnumerator Say(string id) { lastLineId = id; yield return dlg.Say(id); }
        IEnumerator SayAll(params string[] ids) { foreach (var id in ids) yield return Say(id); }
        Coroutine Parallel(IEnumerator r) => StartCoroutine(r);

        IEnumerator GoTo(Place p, bool flash = false, (Vector3 pos, Vector3 look)? at = null, int leafLevel = 1)
        {
            if (place == p) yield break;
            // the warm whiteout marks a dive to a smaller scale; surfacing back out (and Reduce motion) dips to the scene ink at the same pace
            bool warm = flash && Rank(p) >= Rank(place) && !Hud.ReduceMotion;
            var tint = warm ? new Color(1f, 0.95f, 0.75f) : flash ? Palette.Scene : Color.black;
            yield return hud.Fade(1f, flash ? 0.22f : 0.4f, tint);
            hud.ClearCallouts();
            if (place == Place.Chamber) { ch.SetSunBeam(false, Vector3.zero); ch.SetVisible(false); } else World(place)?.Show(false);
            if (p == Place.Chamber)
            {
                ch.SetVisible(true); curio.Companion(false); curio.Place(CurioSpot, PlantFocus);
                // Leaving a world that lives in the focus leaf (stoma, xylem's leaf end, tissue, cell, chloroplast): open 1.6 m off that leaf
                // on the Establish line; Orbit's eased takeover (settle 1.2 s) then pulls out leaf → whole plant under the fade-in (storyboard Sc 13).
                var lc = plant.LeafCenter(plant.FocusLeaf);
                if (World(place) != null && place != Place.Soil && !Hud.ReduceMotion) cam.Cut(lc + (Establish - lc).normalized * 1.6f, lc, 50f);
                else cam.Cut(Establish, PlantFocus, 50f);   // soil sits at the roots; Reduce Motion keeps the plain cut
                cam.Orbit(PlantFocus, OrbitDist, 0f, 10f, false, 3f, 11.5f); au.Ambience("chamber");
            }
            else
            {
                var w = World(p); w.Show(true); w.ResetWorld(); curio.Companion(true);
                if (p == Place.LeafSurface) m.leafSurface.SetMagnification(leafLevel);   // land on the requested lens: the flash is the only cut
                (Vector3 pos, Vector3 look) pose = at ?? w.Pose(); cam.Cut(pose.pos, pose.look, p == Place.LeafSurface ? 50f : 55f);
                au.Ambience(p == Place.Soil ? "soil" : p == Place.Xylem ? "xylem" : p == Place.Cell || p == Place.Chloroplast ? "cell" : "micro");
            }
            place = p;
            hud.ExpireLine();   // the old world's finished line clears under the dip; the bar's 0.32 s .ps-off fade plays as the fade lifts
            yield return hud.Fade(0f, flash ? 0.45f : 0.6f);
        }

        /// Zoom depth for GoTo's transition: chamber 0 · leaf surface / soil / xylem 1 · tissue 2 · cell 3 · chloroplast 4.
        static int Rank(Place q) => q switch { Place.Chamber => 0, Place.Tissue or Place.Interior => 2, Place.Cell => 3, Place.Chloroplast => 4, _ => 1 };

        MicroWorld World(Place p) => p switch
        {
            Place.LeafSurface => m.leafSurface, Place.Soil => m.soil, Place.Xylem => m.xylem, Place.Tissue => m.tissue,
            Place.Cell => m.cell, Place.Chloroplast => m.chloroplast, Place.Interior => m.interior, _ => null
        };

        /// Waits for the learner to pick `want` from the clue tracker (or an AI world-action / autoplay to choose it).
        IEnumerator Hub(ClueId want)
        {
            if (forcedClue == want) { forcedClue = null; hud.ShowClueCards(false); au.Play(Sfx.UiSelect); yield break; }   // promised by Navigate
            yield return GoTo(Place.Chamber);
            m.HubOpen = true; hud.ShowClueCards(true); cam.Orbit(PlantFocus, OrbitDist, cam.transform.eulerAngles.y, 10f, true, 3f, 11.5f);
            hud.ShowInstruction($"Tap CLUE 0{(int)want + 1} to continue — or ask Dr. Curio.", IconId.Target);
            hubWant = want; hubGo = false;
            if (want != ClueId.CO2) Parallel(Say("hub_next"));   // hubs 2 and 3 open with a spoken cue (hub 1 opens under s3_askme)
            float t0 = Time.time;   // Discovered: debug "Unlock all clues" while waiting
            bool hinted = false, offered = false;
            IEnumerator offer() { yield return hud.WaitButton($"OPEN CLUE 0{(int)want + 1}", IconId.Target, ButtonKind.Gold); if (hubWant == want) hubGo = true; }   // a big tap target for anyone who has not found the card
            while (!hubGo && forcedClue != want && m.clues[want] != ClueState.Discovered && !(AutoPlay && Time.time - t0 > 1.5f && !dlg.Speaking))   // autoplay lets the opening line (s3_askme) finish
            {
                float el = Time.time - t0; bool quiet = !dlg.Speaking && !hud.TypingInChat && cam.IdleSeconds > 3f;   // never over Curio, the chat or a learner busy rotating
                if (!hinted && el > 12f && quiet) { hinted = true; Parallel(Say("hub_hint")); curio.Emote(CurioEmote.Point); }
                if (!offered && el > 22f && !dlg.Speaking) { offered = true; Parallel(offer()); }
                yield return null;
            }
            hubWant = null; forcedClue = null; m.HubOpen = false;
            if (offered) hud.HideButton();
            hud.HideInstruction(); hud.ShowClueCards(false); au.Play(Sfx.UiSelect);
            while (demo != null) yield return null;   // let an AI demo put its visuals back before the next beat frames the camera
        }

        IEnumerator Button(string label, IconId icon, ButtonKind kind = ButtonKind.Gold)
        {
            if (AutoPlay) { Parallel(hud.WaitButton(label, icon, kind)); yield return Fx.Wait(1f); hud.HideButton(); yield break; }
            yield return hud.WaitButton(label, icon, kind);
        }

        /// Free-look window: the learner may rotate/zoom; continues after `seconds`, once input has paused (≤ 5 s extra).
        IEnumerator LookAround(string instruction, float seconds)
        {
            cam.Orbit(PlantFocus, cam.Distance > 0 ? Mathf.Min(cam.Distance, OrbitDist) : OrbitDist, cam.transform.eulerAngles.y, 10f, true, 3f, 11.5f);
            hud.ShowInstruction(instruction, IconId.Eye);
            yield return Fx.Wait(AutoPlay ? 1f : seconds);
            for (float t = 0; !AutoPlay && !Module.Skipping && cam.IdleSeconds < 1.2f && t < 5f; t += Time.deltaTime) yield return null;   // never take the camera mid-drag
            hud.HideInstruction();
        }

        // ================================================================== assessment
        /// Ask a bank question with adaptive gating. Wrong answers trigger 3D reteaching, then a retry.
        IEnumerator Ask(string id, Func<IEnumerator> reteach = null, bool force = false, int number = 0, int total = 0)
        {
            var q = m.assessment.Get(id);
            if (q == null || (!force && !m.assessment.ShouldAsk(id))) yield break;
            bool hub = m.HubOpen; m.HubOpen = false; int n = number > 0 ? number : m.assessment.Asked + 1;   // Asked counts finished questions
            string tag = $"{q.type.ToString().ToUpperInvariant()} · ADAPTIVE";
            if (!string.IsNullOrEmpty(q.lead)) { yield return Say($"q_{id}_lead"); hud.HideLine(); }   // the card's lead row carries it from here (Figma 16: no dialogue bar under the MCQ)
            bool firstTry = true; int attempts = 0, lastWrong = -1;   // lastWrong: shown red and not clickable on the retry
            OpenQuestion = q; helped = false;
            inspectGen++; hud.HideTooltip(); plant.Highlight(PlantPart.WholePlant, false); plant.Highlight(PlantPart.Roots, false);   // an open inspect tooltip could answer the card
            while (true)
            {
                int chosen = -1;
                if (AutoPlay)
                {
                    Parallel(hud.Question(q, n, total, tag, c => { }, lastWrong));
                    yield return Fx.Wait(1.2f);
                    chosen = AutoPlayWrongFirst && attempts == 0 ? (q.correct + 1) % q.options.Length : q.correct;
                }
                else yield return hud.Question(q, n, total, tag, c => chosen = c, lastWrong);
                bool ok = chosen == q.correct;
                if (ok) OpenQuestion = null;   // questions to Curio after a correct answer are not 'help'
                hud.MarkAnswer(chosen, ok);
                if (ok)
                {
                    au.Play(Sfx.Success); curio.Emote(firstTry && !q.prediction ? CurioEmote.Celebrate : CurioEmote.Nod);
                    if (total > 0 && !q.prediction)
                    {   // final set: the card's explanation adds to the line, so it lands under Curio's voice
                        Parallel(Say($"q_{id}_ok")); yield return Fx.Wait(AutoPlay ? 0.3f : 0.8f);
                        hud.HideQuestion(); yield return Fx.Wait(0.3f);   // .ps-anim opacity fade is 0.25 s: no question/feedback double exposure
                        int r = 0;
                        if (AutoPlay) { Parallel(hud.Feedback(true, "Correct!", q.correctFeedback, "Continue", reteach != null ? "See it again" : null, x => { }, q.concept)); yield return Fx.Wait(1f); while (dlg.Speaking) yield return null; hud.CancelWaits(); }
                        else yield return hud.Feedback(true, "Correct!", q.correctFeedback, "Continue", reteach != null ? "See it again" : null, x => r = x, q.concept);
                        if (r == 1 && reteach != null) { yield return Say("gen_lookagain"); yield return reteach(); }
                    }
                    else
                    {   // in the journey (and for predictions) Curio's line is the feedback: no card
                        yield return Say($"q_{id}_ok");
                        hud.HideQuestion();
                        if (!AutoPlay) yield return Fx.Wait(0.5f);
                    }
                    break;
                }
                au.Play(Sfx.Wrong); firstTry = false; attempts++; curio.Emote(CurioEmote.Think);
                if (q.prediction)
                {   // predictions never "fail": the journey reveals the answer
                    yield return Say($"q_{id}_no"); hud.HideQuestion(); break;
                }
                if (attempts >= 2)
                {   // second miss: show the answer on its own question, then a card the learner closes. The reteach is offered, not forced.
                    OpenQuestion = null;
                    yield return Fx.Wait(AutoPlay ? 0.2f : 0.9f);                 // let the red mark read first
                    hud.MarkAnswer(q.correct, true); curio.Emote(CurioEmote.Nod);   // the right option, shown in context
                    yield return Fx.Wait(AutoPlay ? 0.3f : 1.4f);
                    hud.HideQuestion(); yield return Fx.Wait(0.3f);                // .ps-anim fade: no question/feedback double exposure
                    string title = $"The answer is {(char)('A' + q.correct)}", sub = "That one's tricky — here's how it works.";
                    string more = reteach != null ? "See it again" : null; int r = 0;
                    if (AutoPlay) { Parallel(hud.Feedback(false, title, q.options[q.correct], "Continue", more, x => { }, q.concept, sub)); yield return Fx.Wait(1f); hud.CancelWaits(); }
                    else yield return hud.Feedback(false, title, q.options[q.correct], "Continue", more, x => r = x, q.concept, sub);
                    if (r == 1 && reteach != null) { yield return Say("gen_lookagain"); yield return reteach(); }
                    break;
                }
                lastWrong = chosen; yield return Fx.Wait(AutoPlay ? 0.2f : 0.9f);   // let the red mark read before the reteach card replaces it
                hud.HideQuestion();
                var steps = ReteachSteps(q.concept, place);
                hud.ShowReteach("Not quite — let's look again", q.wrongFeedback, steps);
                yield return Say($"q_{id}_no");
                if (reteach != null) { hud.RestartReteachClock(); yield return reteach(); hud.SetReteachStep(steps.Length); yield return Fx.Wait(0.6f); } else yield return Fx.Wait(1.5f);   // the card completes when the replay does
                hud.HideReteach();
                yield return Say("gen_tryagain");
            }
            OpenQuestion = null;
            m.assessment.Record(q, firstTry, helped);
            m.HubOpen = hub;
        }

        /// Step pills for the reteach card: micro-world replays show something different from the chamber demos.
        static string[] ReteachSteps(Concept c, Place at) => (c, at) switch
        {
            (Concept.Stomata, Place.LeafSurface) => new[] { "CO₂ arrives", "CO₂ enters" },
            (Concept.Oxygen, Place.LeafSurface) => new[] { "Stoma", "O₂ out" },
            (Concept.Chloroplast, Place.Cell) => new[] { "Chloroplasts", "Light absorbed" },
            (Concept.Chlorophyll, Place.Chloroplast) => new[] { "Light", "Chlorophyll", "Energy captured" },
            (Concept.Stomata, _) => new[] { "Zoom to stoma", "CO₂ arrives", "CO₂ enters" },
            (Concept.Water or Concept.Xylem, _) => new[] { "Plant transparent", "Root", "Xylem", "Leaf" },
            (Concept.Sunlight, _) => new[] { "Leaf", "Sunbeam", "Energy captured" },
            (Concept.Chloroplast or Concept.Chlorophyll, _) => new[] { "Leaf", "Cell", "Chloroplast", "Light absorbed" },
            (Concept.Glucose, _) => new[] { "Inputs", "Light energy", "Glucose forms" },
            (Concept.Oxygen, _) => new[] { "Chloroplast", "Stoma", "O₂ out" },
            (Concept.Phloem, _) => new[] { "Glucose", "Phloem", "Roots · shoots · fruits" },
            _ => new[] { "CO₂ + H₂O", "Light energy", "Glucose + O₂" }
        };

        /// Reteach using the chamber plant (works for any concept from the chamber).
        IEnumerator DemoInChamber(Concept c)
        {
            bool uc = cam.UserControl; var beat0 = m.Beat;   // give back the rotation the caller had; never re-frame a later beat
            yield return GoTo(Place.Chamber);
            int leaf = plant.FocusLeaf;
            if (c < Concept.Phloem && Mathf.Abs(Mathf.DeltaAngle(cam.transform.eulerAngles.y, 0f)) > 50f)   // a hub demo can start from any orbit: every Focus below is framed from the front, and its straight chord from behind or Curio's side cuts through the stem or him
            { if (Hud.ReduceMotion) cam.Cut(Establish, PlantFocus, 50f); else yield return cam.Arc(Establish, PlantFocus, 1.8f, 50f); }
            switch (c)
            {
                case Concept.Stomata:
                case Concept.Oxygen:
                    yield return cam.Focus(plant.LeafUnderside(leaf), new Vector3(0.3f, 1f, 0.6f), 2.2f, 1.5f);
                    plant.SetFlow(Flow.CO2, true); plant.SetFlow(Flow.O2, c == Concept.Oxygen);
                    hud.Callout("demo", c == Concept.Oxygen ? "O₂ OUT" : "CO₂ IN", "through the stomata", Tone.Cyan, () => plant.LeafUnderside(leaf));
                    yield return Fx.Wait(5f); plant.SetFlow(Flow.CO2, false); plant.SetFlow(Flow.O2, false); break;
                case Concept.Water:
                case Concept.Xylem:
                    ch.SetFloorTransparent(true, 1f); plant.ShowRoots(true);
                    yield return plant.SetTransparency(0.7f, 1f); plant.SetVessels(true, false); plant.SetFlow(Flow.Water, true);
                    var path = plant.XylemPath(leaf);
                    hud.Callout("demo", "XYLEM", "Water travels up from the roots", Tone.Blue, () => Fx.Along(path, 0.45f));
                    yield return cam.Focus(Fx.Along(path, 0.2f), new Vector3(0, 0.2f, 1), 6f, 1.5f);
                    yield return cam.Focus(Fx.Along(path, 0.8f), new Vector3(0, -0.2f, 1), 5f, 4f);
                    plant.SetFlow(Flow.Water, false); plant.SetVessels(false, false); yield return plant.SetTransparency(0, 0.8f);
                    ch.SetFloorTransparent(false, 1f); plant.ShowRoots(false); break;
                case Concept.Sunlight:
                    yield return cam.Focus(plant.LeafCenter(leaf), new Vector3(0, 0.3f, 1), 4f, 1.2f);
                    yield return ch.SunBeam(plant.LeafCenter(leaf), () => au.Play(Sfx.SunImpact, 0.6f)); yield return plant.EnergyWave(leaf);
                    hud.Callout("demo", "LIGHT ENERGY", "drives photosynthesis", Tone.Gold, () => plant.LeafCenter(leaf)); yield return Fx.Wait(2f);
                    if (m.Beat != Beat.Sunlight) ch.SetSunBeam(false, Vector3.zero); break;   // Scene 8 keeps its own beam (Q6 reteach)
                case Concept.Chloroplast:
                case Concept.Chlorophyll:
                case Concept.Glucose:
                    yield return cam.Focus(plant.LeafCenter(leaf), new Vector3(0, 0.3f, 1), 3.5f, 1.2f);
                    plant.SetFlow(Flow.Sunlight, true); plant.SetFlow(Flow.Chloroplast, true); plant.SetFlow(Flow.Glucose, c == Concept.Glucose);
                    hud.Callout("demo", c == Concept.Glucose ? "GLUCOSE" : "CHLOROPLASTS", c == Concept.Glucose ? "Made from CO₂ and water with light energy" : "Chlorophyll absorbs light energy", c == Concept.Glucose ? Tone.Gold : Tone.Green, () => plant.LeafCenter(leaf));
                    yield return Fx.Wait(5f); plant.SetFlow(Flow.Sunlight, false); plant.SetFlow(Flow.Chloroplast, false); plant.SetFlow(Flow.Glucose, false); break;
                case Concept.Phloem:
                    yield return plant.SetTransparency(0.7f, 1f); plant.SetVessels(false, true); plant.SetFlow(Flow.Phloem, true);
                    hud.Callout("demo", "PHLOEM", "Food from the leaves to the rest of the plant", Tone.Orange, () => plant.Base + Vector3.up * 1.2f);
                    cam.Orbit(PlantFocus, OrbitDist, cam.transform.eulerAngles.y, 8f, false); cam.AutoSpin = 10f; yield return Fx.Wait(6f); cam.AutoSpin = 0;   // Scene 12's pace: a 60° turn, so Q18's 1.5 s Arc back to the card swings 60°, not 150°
                    plant.SetFlow(Flow.Phloem, false); plant.SetVessels(false, false); yield return plant.SetTransparency(0, 0.8f); break;
                default:
                    yield return hud.Equation(); yield return Fx.Wait(3f); hud.HideEquation(); break;
            }
            hud.HideCallout("demo");
            if (place == Place.Chamber && m.Beat == beat0) cam.Orbit(PlantFocus, OrbitDist, cam.transform.eulerAngles.y, 10f, uc || m.HubOpen, 3f, 11.5f);
            if (place == Place.Chamber && m.Beat == Beat.FinalPlant && beat0 == Beat.FinalPlant) restoreFinal?.Invoke();   // Scene 14: give back the working plant (and the experiment's switches)
        }

        /// Q11/Q12 reteach: the demo resets the plant, so re-stage Scene 12's ghosted phloem view for the retry and the comparison.
        IEnumerator PhloemReteach(Concept c)
        {
            yield return DemoInChamber(c);
            ch.SetFloorTransparent(true, 0.5f); plant.ShowRoots(true);
            yield return plant.SetTransparency(0.72f, 0.6f);
            plant.SetVessels(false, true); plant.SetFlow(Flow.Phloem, true); plant.SetFlow(Flow.Glucose, true);
            cam.Orbit(PlantFocus + Vector3.down * 0.5f, OrbitDist + 0.4f, cam.transform.eulerAngles.y, 10f, true, 3f, 11.5f, WholeFov);   // matches Phloem's framing; also undoes the demo's 45° Focus lens
        }

        // AI demos: one at a time, tracked so Hub can wait for it (demos restore their own visuals; stopping them would not).
        IEnumerator Tracked(IEnumerator r) { yield return r; demo = null; }
        bool StartDemo(IEnumerator r) { if (demo != null) return false; exploreGen++; demo = StartCoroutine(Tracked(r)); return true; }   // gen: a Free Explore child yields to the demo

        // ================================================================== inspect (click a plant part)
        /// Click-to-select a leaf, the stem or the roots while the learner controls the camera in the chamber.
        void InspectPart(Vector2 p)
        {
            if (skylightWaiting)
            {   // Scene 8: any click whose ray passes through the skylight counts (a leaf in front does not block it)
                bool hit = false;
                foreach (var sh in Physics.RaycastAll(cam.Cam.ScreenPointToRay(p), 60f)) if (ch.IsSkylight(sh.collider)) hit = true;
                if (hit) skylightHit = true;
                else { au.Play(Sfx.Wrong, 0.25f); skyMiss = true; }   // "not there": answer the click, and bring the hint forward
                return;
            }
            if (place != Place.Chamber || !cam.Orbiting || !cam.UserControl || OpenQuestion != null) return;   // never during scripted shots or a question
            inspectGen++; hud.HideTooltip(); plant.Highlight(PlantPart.WholePlant, false); plant.Highlight(PlantPart.Roots, false);
            Physics.SyncTransforms();   // leaves sway in Update; same as SoilWorld.OnClick
            if (!Physics.Raycast(cam.Cam.ScreenPointToRay(p), out var h, 60f)) return;
            var t = h.collider.GetComponentInParent<PlantPartTag>(); if (!t) return;
            var (title, body, icon) = t.part switch
            {
                PlantPart.Stem => ("Stem", "Holds the leaves up to the light. Xylem carries water up inside it; phloem carries food.", IconId.Stem),
                PlantPart.Roots => ("Roots", "Absorb water and minerals from the soil; the corm stores food.", IconId.Root),
                _ => ("Leaf", m.clues[ClueId.Sunlight] == ClueState.Discovered   // never hand out an undiscovered clue (Q5 follows Scene 8's inspect window)
                        ? "The food factory: chloroplasts in its cells use light energy to make glucose. CO₂ enters through stomata underneath."
                        : m.Beat == Beat.Clue3 ? "The food factory: CO₂ ✓ and water ✓ are both inside, yet nothing is being made. What else could it need?"
                        : "The plant's food factory: this is where its food gets made. How it works is what we're here to find out.", IconId.Leaf)
            };
            if (t.leaf >= 0) plant.HighlightLeaf(t.leaf, true); else plant.Highlight(t.part, true);
            curio.Point(h.point);
            hud.ShowTooltip(title, body, icon, p); au.Play(Sfx.UiSelect, 0.5f);
            StartCoroutine(InspectHide(inspectGen));
        }

        /// Tooltip + highlight go away after ~6 s, or as soon as the camera moves (drag / zoom / a scripted shot). A new click supersedes.
        IEnumerator InspectHide(int gen)
        {
            Vector3 p0 = cam.transform.position;
            for (float t = 0; t < 6f && gen == inspectGen && (cam.transform.position - p0).sqrMagnitude < 0.04f; t += Time.deltaTime) yield return null;
            if (gen != inspectGen) yield break;
            hud.HideTooltip(); plant.Highlight(PlantPart.WholePlant, false); plant.Highlight(PlantPart.Roots, false);
        }

        // ================================================================== Scene 2 — A New World
        IEnumerator Scene2()
        {
            ContextTopic = "the Botanical Investigation Chamber forming around you";
            au.Music(MusicState.NewWorld); hud.SetHudVisible(false); curio.SetVisible(false);
            ch.SetFormed(false); plant.SetGrown(false);
            // world-forming reveal: crane-orbit ~48° round the plant onto the establishing pose, so palms, ribs and signs slide past
            // each other while they form (yaw is spent 6–11 s, under the vegetation/glass/sunrise). Reduce motion keeps the near-axial pull-back.
            bool calm = Hud.ReduceMotion;
            cam.Cut(plant.Base + (calm ? new Vector3(0.6f, 1.7f, -4.2f) : new Vector3(-3.2f, 1.5f, -2.9f)), plant.Base + Vector3.up * 1.6f, 62f);
            Parallel(calm ? cam.Blend(Establish, PlantFocus + Vector3.up * 0.3f, 16f, 50f, Ease.InOut)
                          : cam.Arc(Establish, PlantFocus + Vector3.up * 0.3f, 16f, 50f, Ease.InOut));
            var form = Parallel(ch.Form(plant));
            yield return Fx.Wait(2.5f);   // Figma 01: title over the dark chamber; fully in by 3.7 s, gone by 7.1 s, before the sunrise
            yield return hud.Title("A New World", "Botanical Investigation Chamber", 2.6f);
            au.Ambience("chamber");   // ~7.1 s: room tone, leaf rustle and birds fade in (1.6 s) as the glass closes and the sun rises
            yield return form;
        }

        IEnumerator ChamberLook()
        {
            ContextTopic = "the Botanical Investigation Chamber and its enormous central plant";
            yield return LookAround("Look around — drag to rotate, zoom with + / −, tap the plant to inspect it.", 3f);   // short: Curio arrives (and talks) sooner
        }

        // ================================================================== Scene 3 — Meet Dr. Curio
        IEnumerator CurioIntro()
        {
            ContextTopic = "Dr. Curio, your investigation partner";
            cam.StopOrbit(); hud.SetCinematic(true);
            var shot = plant.Base + new Vector3(3.3f, 1.9f, -5.5f);
            // character introduction: push in to a medium shot — Curio on the right third, the plant behind him
            Parallel(cam.Arc(shot, Vector3.Lerp(PlantFocus, CurioSpot, 0.78f), 2.4f, 40f));   // round the plant from wherever ChamberLook left the camera: never through the canopy or the materialising Curio (Arc's default Ease.InOut)
            yield return curio.Materialize(CurioSpot, shot);   // face the final shot, not wherever the learner had orbited
            curio.LookAtCamera();
            Parallel(curio.Scan(cam.transform.position, 1.6f));
            // the scan gag is voiced: he reads his scanner aloud, so "Good." lands with its context and the opening is never silent for long
            hud.ShowScanner(ScanState.Scanning, "SCAN…", "Unknown visitor detected", 0.2f); au.Play(Sfx.Scan);
            yield return Say("s3_scan1");
            hud.ShowScanner(ScanState.Scanning, "SPECIES: HUMAN", "Biometric scan complete", 1f); au.Play(Sfx.TargetLock);
            yield return Say("s3_scan2");
            Parallel(curio.Scan(cam.transform.position, 1.2f)); au.Play(Sfx.Beep);
            hud.ShowScanner(ScanState.Detected, "CURIOSITY LEVEL: SUSPICIOUSLY HIGH", "Recommendation: recruit immediately", 1f);
            yield return Say("s3_scan3");
            curio.Emote(CurioEmote.Nod);
            hud.SetCinematic(false);
            if (!Hud.ReduceMotion) Parallel(cam.Push(0.9f, 9f));   // slow push under his self-introduction (s3_good + s3_intro ≈ 9.35 s): medium → medium-close, Curio stays on the right third; the gaze Blend below supersedes it
            yield return Say("s3_good"); hud.HideScanner();
            curio.Emote(CurioEmote.Celebrate);
            yield return Say("s3_intro");
            // ---- cinematic prologue (one shot per storyboard line): hero plant → sunlight → through the leaf → the hidden world → back out
            int leaf = plant.FocusLeaf; bool calm = Hud.ReduceMotion;
            // SHOT 02 — hero reveal: he turns to the plant; the camera swings low and wide so the crown stands against the glass
            curio.LookAt(plant.Top);
            var hero = plant.Base + new Vector3(-3.4f, 0.7f, -8.6f); var heroLook = PlantFocus + Vector3.up * 0.8f;   // low three-quarter: crown to pot against the glass
            Coroutine heroMove = null;
            if (calm) cam.Cut(hero, heroLook, 50f); else heroMove = Parallel(cam.Arc(hero, heroLook, 2.6f, 50f));
            Parallel(curio.Scan(plant.Top, 2.5f)); hud.ShowScanner(ScanState.Scanning, "BIOLOGICAL ACTIVITY DETECTED", "Analysing the plant…", 0.6f);   // the readout never says the next line
            yield return Say("s3_peaceful");
            if (heroMove != null) yield return heroMove;   // the reveal lands before the light does
            // SHOT 03 — sunlight enters the frame and lands on the focus leaf; the camera pushes toward the leaf under the line
            hud.HideScanner();
            Parallel(ch.SunBeam(plant.LeafCenter(leaf), () => au.Play(Sfx.SunImpact, 0.45f)));   // the beam stays on through the push-in; it fades as we dive
            plant.SetFlow(Flow.Sunlight, true); plant.SetFlow(Flow.Chloroplast, true);
            var leafDir = new Vector3(0.1f, -0.45f, 1f).normalized;   // from above: the lit upper surface, the beam entering at the top of frame
            if (calm) cam.Cut(plant.LeafCenter(leaf) - leafDir * 3.2f, plant.LeafCenter(leaf), 42f);
            else Parallel(cam.Focus(plant.LeafCenter(leaf), leafDir, 3.2f, 4.2f, 42f));
            yield return Say("s3_processes");
            // SHOT 04 — through the leaf surface (warm flash at the blade) · SHOT 05 — the hidden world inside the leaf (the client's cross-section model)
            hud.SetCinematic(true); au.Play(Sfx.Whoosh, 0.6f);
            Parallel(Say("s3_somewhere"));
            Parallel(ch.FadeSunBeam(1.2f));
            Parallel(plant.SetTransparency(0.55f, 1.2f));   // the leaf turns glassy as we close in: we pass through the surface into the green inside
            if (!calm) yield return cam.Focus(plant.LeafCenter(leaf), plant.LeafCenter(leaf) - cam.transform.position, 0.25f, 1.3f, 28f);
            au.Music(MusicState.Microscopic, 1.5f);
            curio.SetVisible(false);   // the vista plays alone (hidden before the flash, so he never shows while it fades); back at his spot for the briefing
            yield return GoTo(Place.Interior, true);
            plant.SetFlow(Flow.Sunlight, false); plant.SetFlow(Flow.Chloroplast, false); Parallel(plant.SetTransparency(0f, 0.05f));   // reset while the chamber is hidden
            var glide = Parallel(m.interior.Glide(cam));
            while (dlg.Speaking) yield return null;
            yield return Fx.Wait(AutoPlay ? 0.3f : 1.4f);
            m.interior.Pulse(); au.Play(Sfx.ChloroplastActivate, 0.55f);   // "…food is being made." — the chloroplasts light up around the camera
            Parallel(Say("s3_food"));
            yield return glide;
            while (dlg.Speaking) yield return null;
            // back out to the chamber (leaf → whole plant under the flash): the clue cards appear around the plant
            au.Play(Sfx.Whoosh, 0.5f); au.Music(MusicState.NewWorld, 2f);
            yield return GoTo(Place.Chamber, true);
            ch.ResetSkylight();   // the beam opened it for the reveal; Scene 8 needs it closed for the learner
            curio.SetVisible(true); curio.Place(CurioSpot, PlantFocus); curio.LookAtCamera(); hud.SetCinematic(false);
            plant.HighlightLeaf(leaf, true); curio.Point(plant.LeafCenter(leaf));
            hud.ShowClueCards(true);
            IEnumerator twoShot()
            {   // two-shot: Curio briefs the learner beside the plant, then a slow push under the ~8 s briefing (unless the hub already took the camera)
                yield return cam.Blend(plant.Base + new Vector3(2.4f, 2.1f, -6.6f), Vector3.Lerp(PlantFocus, CurioSpot, 0.5f), 2.6f, 46f);
                if (!cam.Orbiting) yield return cam.Push(0.6f, 8f);
            }
            Parallel(twoShot());
            yield return Say("s3_three");
            curio.Emote(CurioEmote.Think);
            yield return Say("s3_tease");
            yield return Say("s3_find");
            m.clues.Set(ClueId.CO2, ClueState.Available);
            hud.SetHudVisible(true); hud.SetProgressVisible(true); hud.SetAskAIVisible(true); hud.SetZoomControlVisible(true);
            hud.SetInvestigation("THREE CLUES", "Find what the plant needs to make food.", IconId.Sparkle);
            plant.HighlightLeaf(plant.FocusLeaf, false); curio.Emote(CurioEmote.Idle);
            Parallel(Say("s3_askme"));   // the hub opens under his last line: the card is clickable the moment it lights up
        }

        // ================================================================== Scene 4 — Something from the air
        IEnumerator Clue1()
        {
            yield return Hub(ClueId.CO2);
            cam.Orbit(PlantFocus, OrbitDist, cam.transform.eulerAngles.y, 10f, false, 3f, 11.5f);   // stage the leaf from a known pose (keeps the learner's yaw, drops pan/zoom)
            ContextTopic = "Clue 01 — something from the air entering the leaf";
            hud.SetInvestigation("CLUE 01 — ???", "Something from the air…", IconId.Target);
            au.Music(MusicState.Investigation);
            yield return Say("s4_first");
            Parallel(curio.Scan(plant.LeafCenter(plant.FocusLeaf) + Vector3.up, 1.5f));
            yield return Say("s4_around");
            // A giant leaf floats in front of the learner
            var leaf = plant.CloneLeaf(plant.FocusLeaf);
            cam.StopOrbit();   // parked camera: a drag can't lose the leaf; scroll and + still raise UserZoomed
            Vector3 from = leaf.transform.position; Quaternion r0 = leaf.transform.rotation;
            Vector3 front = cam.transform.position + cam.transform.forward * 3.2f - Vector3.up * 0.2f;
            Quaternion r1 = Quaternion.LookRotation(cam.transform.position - front) * Quaternion.Euler(-70f, 0, 0);
            au.Play(Sfx.Whoosh, 0.6f);
            Parallel(Fx.Tween(2.2f, t => { if (leaf) leaf.transform.SetPositionAndRotation(Vector3.Lerp(from, front, t), Quaternion.Slerp(r0, r1, t)); }, Ease.InOut));
            yield return Say("s4_closer");
            hud.ShowMagnification(0);
            hud.ShowInstruction("Let's zoom into the leaf — press + (or scroll).", IconId.Zoom);
            userZoom = 0;
            Parallel(Say("s4_zoom"));
            float t0 = Time.time;
            // the lens follows the learner: three notches / presses narrow the FOV, each with a rising tick
            const float Need = 3f; float f0 = cam.Cam.fieldOfView, shown = 0f; int ticks = 0;
            while (shown < 0.97f && !Module.Skipping)
            {
                if (AutoPlay) userZoom += 2.5f * Time.deltaTime;
                else if (Time.time - t0 > 25f) userZoom = Mathf.Max(userZoom, Need);   // idle fallback
                shown = Mathf.Lerp(shown, Mathf.Clamp01(userZoom / Need), 1f - Mathf.Exp(-8f * Time.deltaTime));   // each notch glides
                cam.Cam.fieldOfView = Mathf.Lerp(f0, f0 * 0.6f, shown);
                int n = Mathf.Min(Mathf.FloorToInt(userZoom), 3);
                if (n > ticks) { ticks = n; au.Play(Sfx.UiClick, 0.5f, 1f + 0.12f * n); }
                yield return null;
            }
            hud.HideInstruction(); hud.SetCinematic(true);
            au.Play(Sfx.Whoosh);
            yield return cam.Blend(front - (front - cam.transform.position).normalized * 0.6f, front, 1.4f, f0 * 0.45f, Ease.In);   // keep closing the lens
            hud.ShowMagnification(1);
            if (leaf) Destroy(leaf);
            yield return EnsureLeafSurface(1);
            hud.SetCinematic(false);
        }

        IEnumerator EnsureLeafSurface(int level)
        {
            if (place != Place.LeafSurface) { yield return GoTo(Place.LeafSurface, true, leafLevel: level); }   // lands on the requested lens: the flash is the only cut
            m.leafSurface.SetMagnification(level); hud.ShowMagnification(level);
            var p = m.leafSurface.Pose(level); cam.Cut(p.pos, p.look, 50f);
        }

        IEnumerator Stomata()
        {
            ContextTopic = "the underside of a leaf under the microscope — stomata";
            hud.SetInvestigation("CLUE 01 — ???", "Find where it gets into the leaf.", IconId.Target);
            if (place != Place.LeafSurface) yield return EnsureLeafSurface(1);
            au.Music(MusicState.Microscopic);
            var p1 = m.leafSurface.Pose(1); var p = m.leafSurface.Pose(2);
            // storyboard: the zoom drifts on over the 10× pore field, BEEP ×3 interrupts it, "Stop.", then the learner zooms into ONE pore
            Parallel(cam.Blend(Vector3.Lerp(p1.pos, p.pos, 0.12f), Vector3.Lerp(p1.look, p.look, 0.12f), 1.4f, 49f, Ease.Out));
            yield return Fx.Wait(0.9f);
            hud.ShowScanner(ScanState.Detected, "BEEP · BEEP · BEEP", "Pores detected on the lower leaf surface", 0.7f);
            yield return m.leafSurface.Beep();   // pings + pore glow on every stoma in the 10× field
            curio.Emote(CurioEmote.Surprise);
            yield return Say("s4_stop");
            m.leafSurface.SetGuardHighlight(true);   // only the hero stoma pulses: the one to zoom into
            hud.ShowInstruction("Zoom into one pore — press + (or scroll).", IconId.Zoom);
            userZoom = 0; float t0 = Time.time;
            while (userZoom < 1f && !Module.Skipping && Time.time - t0 < (AutoPlay ? 1.5f : 20f)) yield return null;
            hud.HideInstruction(); hud.HideScanner();   // the scanner card would cover the GUARD CELLS callout at 100×
            m.leafSurface.SetMagnification(2); au.Play(Sfx.Whoosh, 0.5f);
            yield return cam.Blend(p.pos, p.look, 2.4f, 45f);
            hud.ShowMagnification(2);   // the pill flips when 100× actually lands
            yield return Say("s4_found");
            hud.Callout("stoma", "STOMA", "Plural: STOMATA", Tone.Green, () => m.leafSurface.Stoma.position);
            hud.Callout("guard", "GUARD CELLS", "Open and close the pore", Tone.Cyan, () => m.leafSurface.GuardCells.position);
            yield return Say("s4_stomata");
        }

        IEnumerator CO2()
        {
            ContextTopic = "carbon dioxide entering the leaf through a stoma";
            if (place != Place.LeafSurface) yield return EnsureLeafSurface(2);
            m.leafSurface.AmbientCO2(true);
            hud.HideCallout("guard"); hud.HideCallout("stoma");   // the cyan guard rim still marks the pore; the CO₂ tag would sit on the STOMA tag
            var enter = Parallel(m.leafSurface.CO2Enter());
            if (!Hud.ReduceMotion) Parallel(cam.Push(0.7f, 4f));   // travel IN with the CO₂ (4 s = CO2Enter; Scene 11 pulls OUT with the O₂). 0.7 m keeps foreground ambient CO₂ ≥ ~1.4 m from the lens through the Q1/Q2 hold
            hud.Callout("co2", "CARBON DIOXIDE — CO₂", "From the air", Tone.Cyan, () => m.leafSurface.HighlightedMolecule.position);
            hud.SetInvestigation("CLUE 01 — CARBON DIOXIDE", "CO₂ enters through the stomata.", IconId.CO2);
            yield return Say("s4_entering");   // the hero is highlighted and drifting in as Curio says "look"
            yield return Say("s4_co2");
            yield return enter;
            yield return Say("s4_ingredient");
            m.leafSurface.Freeze(true); curio.Emote(CurioEmote.Celebrate);
            m.clues.Set(ClueId.CO2, ClueState.Discovered);
            yield return hud.ClueUnlocked(ClueId.CO2);
            m.leafSurface.Freeze(false); hud.ClearCallouts();
            hud.SetInvestigation("CLUE 01 — FOUND", "Quick check before we leave the leaf.", IconId.Target);   // the CO₂ title would answer Q1/Q2
            IEnumerator replay() { hud.Callout("stoma", "STOMA", "CO₂ comes in here", Tone.Green, () => m.leafSurface.Stoma.position); yield return m.leafSurface.CO2Enter(); hud.HideCallout("stoma"); }
            yield return Ask("Q1", replay, force: true);
            yield return Ask("Q2", replay);
            hud.SetInvestigation("CLUE 01 — CARBON DIOXIDE", "CO₂ enters through the stomata.", IconId.CO2);   // the Clue 2 hub waits under this title
            m.leafSurface.AmbientCO2(false); hud.HideMagnification();
            yield return GoTo(Place.Chamber, true);   // same quick dip as every other micro-world exit (GoTo's Rank tints an ascent with the scene ink)
        }

        // ================================================================== Scene 5–7 — Water
        IEnumerator Clue2()
        {
            yield return Hub(ClueId.Water);
            ContextTopic = "Clue 02 — the water trail";
            hud.SetInvestigation("CLUE 02 — ???", "Follow the signal to the second ingredient.", IconId.Target);
            au.Music(MusicState.Investigation);
            Parallel(cam.Arc(Establish, PlantFocus, 1.8f, 52f));   // round the plant from wherever the hub left the camera
            yield return Say("s5_second");
            hud.ShowScanner(ScanState.Scanning, "SCANNING…", "Top of plant → leaves → stem → bottom", 0.1f);
            var sweep = Parallel(curio.Sweep(plant.Top + Vector3.up * 0.3f, plant.Base + Vector3.up * 0.1f, 4.5f));
            var tw = Parallel(Fx.Tween(4.5f, t => hud.ShowScanner(ScanState.Scanning, t < 0.85f ? "SCANNING…  NOTHING" : "SCANNING…", "Top of plant → leaves → stem → bottom", t)));
            yield return Say("s5_nothing");
            yield return sweep;
            yield return tw;   // Fx.Tween ends with f(1): its last tick must land before DETECTED
            au.Play(Sfx.Beep, 1f, 0.8f);
            plant.Signal(plant.Base + Vector3.up * 0.05f, Palette.Water, true);
            hud.ShowScanner(ScanState.Detected, "SIGNAL DETECTED BELOW", "Source: beneath the plant", 1f);
            yield return Ask("Q3", null, force: true);
            hud.HideScanner();   // the readout has done its job; docked in the right column it sits over Curio's body and pointing arm
            Parallel(cam.Blend(plant.Base + new Vector3(3.3f, 1.9f, -5.5f), Vector3.Lerp(plant.Base + Vector3.up * 0.4f, CurioSpot, 0.6f), 1.4f, 44f));   // push in on Curio's reaction from Scene 3's medium-shot spot: Curio right third, blue signal lower left, his point the line between
            yield return Say("s5_huh");
            curio.Point(plant.Base - Vector3.up); curio.Emote(CurioEmote.Point);
            yield return Say("s5_notleaf");
        }

        IEnumerator Roots()
        {
            ContextTopic = "the roots hidden under the floor";
            if (place != Place.Chamber) yield return GoTo(Place.Chamber);
            plant.Signal(plant.Base, Palette.Water, true);
            ch.SetFloorTransparent(true, 2f); plant.ShowRoots(true);
            yield return cam.Blend(plant.Base + new Vector3(2.5f, -0.4f, -5.5f), plant.RootTip + Vector3.up * 0.6f, 2.5f, 50f);
            hud.Callout("roots", "ROOTS", "Hidden beneath the floor", Tone.Blue, () => plant.RootTip + Vector3.up * 0.4f);   // after the reveal: the label lands on its subject, clear of the dialogue
            yield return Say("s5_follow");
            plant.Signal(plant.Base, Palette.Water, false);
            au.Play(Sfx.Whoosh); hud.SetCinematic(true);
            yield return cam.Blend(plant.RootTip + new Vector3(0.2f, 0.3f, -0.6f), plant.RootTip, 1.2f, Hud.ReduceMotion ? 50f : 70f, Ease.In);   // Reduce motion: no lens widening during the dive
            hud.HideCallout("roots");
            yield return GoTo(Place.Soil, true);
            hud.SetCinematic(false);
        }

        IEnumerator Water()
        {
            ContextTopic = "the soil around the roots — water molecules";
            if (place != Place.Soil) yield return GoTo(Place.Soil, true);
            au.Music(MusicState.WaterJourney);
            hud.SetInvestigation("CLUE 02 — WATER", "Where does the plant's water come from?", IconId.Drop);
            pickPending = true;   // "Show me water" from here on queues a forced pick for WaitForPick
            yield return Say("s6_recognise");
            m.soil.HighlightWater(true);
            hud.Callout("root", "ROOT HAIR", "Tiny extensions of the root", Tone.Green, () => m.soil.Root.position);
            yield return Say("s6_poured");
            hud.ShowInstruction("Select a water molecule near the root.", IconId.Drop);
            Parallel(Say("s6_select"));
            Transform picked = null;
            bool pickHinted = false, pickOffered = false; float pick0 = Time.time;
            IEnumerator pickForMe() { yield return hud.WaitButton("PICK ONE FOR ME", IconId.Drop, ButtonKind.Gold); if (pickPending) forcedPick = true; }   // a wait cancelled by the learner's own pick changes nothing
            yield return m.soil.WaitForPick(() =>
            {   // idle help like the skylight: ~12 s Curio points at a molecule and repeats the ask, ~22 s a button picks one
                float el = Time.time - pick0;
                if (!AutoPlay && !pickHinted && el > 12f && !dlg.Speaking && !hud.TypingInChat) { pickHinted = true; var mol = m.soil.SuggestedMolecule; if (mol) curio.Point(mol.position); Parallel(Say("s6_select")); }
                if (!AutoPlay && !pickOffered && el > 22f) { pickOffered = true; Parallel(pickForMe()); }
                return forcedPick || AutoPlay;
            }, t => picked = t);
            if (pickOffered) hud.HideButton();
            bool mine = !forcedPick;   // praise only the learner's own pick, not an AI "show me water" or the button
            pickPending = forcedPick = false; hud.HideInstruction();
            au.Play(Sfx.Pop, 0.8f, 1.25f);   // a tactile world pick, not the menu chime; SoilWorld plays WaterAbsorb on the ripple
            if (picked) Fx.Burst(picked.position, Palette.Water, 14, 0.04f, 0.7f, 0.5f);
            if (mine) { curio.Emote(CurioEmote.Nod); Parallel(Say("gen_wellspotted")); }
            hud.HideCallout("root");
            hud.Callout("h2o", "WATER — H₂O", "Absorbed by the root", Tone.Blue, () => picked ? picked.position : m.soil.Root.position);
            yield return m.soil.Absorb(picked);
            yield return Say("s6_absorbed");
            hud.ClearCallouts();
            yield return Say("s6_andnow");
            yield return m.soil.PathwayGlow();
            yield return Say("s6_ride");
        }

        IEnumerator XylemRide()
        {
            ContextTopic = "riding the water up the xylem";
            hud.SetCinematic(true);
            yield return GoTo(Place.Xylem, true);
            au.Music(MusicState.WaterJourney); au.Play(Sfx.WaterRush);
            bool spoke = false;
            yield return m.xylem.Ride(cam, stage =>
            {
                au.Play(Sfx.Whoosh, 0.5f, 1.2f);
                hud.ShowScanner(ScanState.Scanning, stage, "ROOT → STEM → BRANCH → LEAF", stage == "ROOT" ? 0.25f : stage == "STEM" ? 0.5f : stage == "BRANCH" ? 0.75f : 1f);
                if (stage == "STEM" && !spoke) { spoke = true; hud.ShowInfo("Xylem", "Carries water and minerals upward from the roots.", IconId.Xylem); Parallel(Say("s7_xylem")); }
            });
            hud.HideInfo();   // the scanner stays on LEAF: ClueUnlocked swaps it for the card in place
            curio.Emote(CurioEmote.Celebrate);
            m.clues.Set(ClueId.Water, ClueState.Discovered);
            yield return hud.ClueUnlocked(ClueId.Water);   // storyboard: water reaches the leaf → CLUE 02 UNLOCKED → back to the chamber
            yield return GoTo(Place.Chamber, true);
            hud.SetCinematic(false);   // the HUD returns with the chamber, not over the transition dip
            ch.SetFloorTransparent(false, 1f); plant.ShowRoots(false);   // close Roots()' floor whether or not the Q4 reteach runs
            yield return Ask("Q4", () => DemoInChamber(Concept.Xylem));
            curio.LookAtCamera();
            var twoPos = plant.Base + new Vector3(2.4f, 2.1f, -6.6f); var twoLook = Vector3.Lerp(PlantFocus, CurioSpot, 0.5f);   // Scene 3's two-shot: the "Where's the food?" button lands on Curio, not the house wide; Clue 3's hub orbit eases back out
            if (Hud.ReduceMotion) cam.Cut(twoPos, twoLook, 46f); else Parallel(cam.Blend(twoPos, twoLook, 2f, 46f));
            yield return Say("s7_two"); curio.Point(plant.LeafCenter(plant.FocusLeaf)); yield return Say("s7_so");   // storyboard: "Two ingredients." — he looks at the leaf — "So…"
            yield return Say("s7_food");
            yield return Fx.Wait(1.2f);
            yield return Say("s7_exactly");
        }

        // ================================================================== Scene 8 — The factory won't start / Sunlight
        IEnumerator Clue3()
        {
            yield return Hub(ClueId.Sunlight);
            ContextTopic = "a leaf with carbon dioxide and water but no food being made";
            hud.SetInvestigation("CLUE 03 — ???", "Something is still missing…", IconId.Target);
            int leaf = plant.FocusLeaf;
            Vector3 lc = plant.LeafCenter(leaf);
            yield return cam.Arc(lc - new Vector3(0.2f, 0.25f, 1f).normalized * 4.5f, lc, 1.8f, 45f);   // the old Focus pose, reached round the plant from the hub orbit instead of through the leaf
            ch.SetDarkness(0.85f, 4f); au.Ambience("dark");   // lights go out before the zero reading: FOOD PRODUCTION: 0 and "absolutely nothing" land in shadow; room tone and birdsong go with the light (1.6 s bus fade inside the 4 s darkening)
            Parallel(cam.Push(0.6f, 8f));   // slow push under the ~7 s "food production: 0" narration
            plant.SetIngredientsIdle(true); plant.SetDormant(true);
            hud.Callout("co2", "CO₂", "Carbon dioxide ✓", Tone.Cyan, () => plant.LeafCenter(leaf) + Vector3.left * 0.6f);
            yield return Say("s8_co2");
            hud.Callout("h2o", "H₂O", "Water ✓", Tone.Blue, () => plant.LeafCenter(leaf) + Vector3.right * 0.6f);
            yield return Say("s8_water");
            hud.ShowScanner(ScanState.Detected, "FOOD PRODUCTION: 0", "CO₂ ✓ · H₂O ✓ · ??? — not detected", 0f);
            au.Music(MusicState.None, 3f);   // the water loop drains away under "doing… absolutely nothing"
            curio.Emote(CurioEmote.Think);
            yield return SayAll("s8_doing", "s8_nothing");
            yield return Say("s8_missing");
            hud.ClearCallouts();
            Parallel(Say("s8_inspect"));
            yield return LookAround("Tap the plant to inspect it — what could be missing?", 7f);
            yield return Ask("Q5", null, force: true);
        }

        IEnumerator Sunlight()
        {
            ContextTopic = "sunlight reaching the leaf";
            if (place != Place.Chamber) yield return GoTo(Place.Chamber);
            int leaf = plant.FocusLeaf; Vector3 lc = plant.LeafCenter(leaf);
            ch.SetDarkness(0.85f, 0f); plant.SetDormant(true); plant.SetIngredientsIdle(true);
            hud.SetInvestigation("CLUE 03 — ???", "Let the light in — wake the food factory.", IconId.Target);   // name stays hidden until the beam reveals it
            au.Music(MusicState.None, 1f); au.Ambience("dark"); hud.HideScanner();   // silent dark room until the beam lands (no-ops in the normal flow; covers debug jumps and a chamber demo during Clue3); the FOOD PRODUCTION card would sit over the skylight
            // low and looking up: the dark leaf above the bottom HUD, the closed skylight high in frame (aim by angle — the skylight is ~10 m above the leaf)
            Vector3 low = plant.Base + new Vector3(-1.5f, 0.8f, -9f), toLeaf = (lc - low).normalized, toSky = (ch.SkylightPos - low).normalized;
            Parallel(cam.Arc(low, low + Vector3.Slerp(toLeaf, toSky, 0.36f) * 8f, 2.5f, Mathf.Clamp(Vector3.Angle(toLeaf, toSky) * 1.7f, 55f, 66f)));
            ch.SetSkylightReady(true);
            hud.ShowInstruction("Open the skylight — tap it to let the light in.", IconId.Sun);
            Parallel(Say("s8_open"));
            skylightHit = skyMiss = false; skylightWaiting = true;   // the learner opens the sky
            bool pressed = false, hinted = false, offered = false; float t0 = Time.time;
            IEnumerator fallback() { yield return hud.WaitButton("LET THE LIGHT IN", IconId.Sun, ButtonKind.Gold); pressed = true; }
            while (!skylightHit && !pressed && m.clues[ClueId.Sunlight] != ClueState.Discovered && !Module.Skipping && !(AutoPlay && Time.time - t0 > 1f))
            {
                float el = Time.time - t0;
                if (!hinted && (el > 12f || (skyMiss && el > 4f)) && !dlg.Speaking && !hud.TypingInChat) { hinted = true; Parallel(Say("s8_hint")); curio.Point(ch.SkylightPos); }   // a missed click brings the hint forward
                if (!offered && el > 20f) { offered = true; Parallel(fallback()); }   // a button that also counts, for anyone still stuck
                yield return null;
            }
            skylightWaiting = false; if (offered) hud.HideButton();
            if (dlg.Speaking) { au.StopVoice(); dlg.Cancel(); hud.HideLine(); curio.Talking = false; }   // the hint never talks over the skylight opening and the beam
            hud.HideInstruction(); ch.SetSkylightReady(false); au.Play(Sfx.UiSelect);
            yield return ch.OpenSkylight();
            au.Play(Sfx.EnergyBuild, 1f, 1.8f);   // the charge-up starts on the learner's action and peaks as the beam hits
            curio.Point(lc);
            yield return Fx.Wait(0.95f);   // the "Then—" beat
            hud.SetCinematic(true);
            yield return ch.SunBeam(lc, () => { au.Play(Sfx.SunImpact); au.Music(MusicState.SunlightReveal, 0.25f); au.Ambience("chamber"); if (!Hud.ReduceMotion) cam.Shake(0.06f, 0.8f); });   // fall + impact in the locked low shot; the reveal music and the room's life land on the hit
            Vector3 aim = lc + Vector3.down * 0.2f;   // blade centre: LeafCenter sits in the blade's upper third, the tip must clear the subtitle panel
            cam.Cut(aim - new Vector3(0.2f, 0.25f, 1f).normalized * 3.6f, aim, 42f);   // cut in on the wave from Clue 3's leaf angle, flare already at FlareRest
            hud.ShowScanner(ScanState.Detected, "ENERGY RISING…", "Scanner overloading", 0.8f);
            if (!Hud.ReduceMotion) Parallel(cam.Push(0.3f, 1.45f));   // creep with the wave and land at rest before the pull-back
            yield return plant.EnergyWave(leaf);
            plant.SetDormant(false); ch.SetDarkness(0.15f, 3f);
            hud.ShowScanner(ScanState.Complete, "ENERGY DETECTED", "Source: SUNLIGHT · readings off the scale", 1f); au.Play(Sfx.AnalysisComplete);
            yield return cam.Blend(plant.Base + new Vector3(-1.5f, 2.2f, -7.5f), lc + Vector3.up * 0.8f, 1.4f, 50f);   // pull back as the chamber wakes (same end pose as before); Curio in frame for his reaction
            curio.Emote(CurioEmote.Surprise);
            yield return Say("s8_thereyou");
            hud.Callout("sun", "SUNLIGHT", "Light energy", Tone.Gold, () => plant.LeafCenter(leaf) + Vector3.up * 0.5f);
            yield return Say("s8_light");
            m.clues.Set(ClueId.Sunlight, ClueState.Discovered);
            hud.HideScanner();
            yield return hud.ClueUnlocked(ClueId.Sunlight);
            hud.SetCinematic(false);
            plant.SetIngredientsIdle(false); hud.ClearCallouts();   // the SUNLIGHT · Light energy tag would answer Q6
            hud.SetInvestigation("CLUE 03 — FOUND", "All three ingredients found.", IconId.Target);
            yield return Ask("Q6", () => DemoInChamber(Concept.Sunlight));
            hud.ShowClueCards(true); curio.Emote(CurioEmote.Celebrate);   // all-three recap, and the hook leads straight into ENTER LEAF
            yield return SayAll("s8_everything", "s8_notseen");
            hud.ShowClueCards(false);
        }

        // ================================================================== Scene 9 — Deeper than before
        IEnumerator EnterLeaf()
        {
            ContextTopic = "going inside the leaf";
            if (place != Place.Chamber) yield return GoTo(Place.Chamber);
            int leaf = plant.FocusLeaf;
            Parallel(ch.FadeSunBeam(1.2f));   // the clue is solved: the shaft eases off so the leaf (not a flare) fills the close-up
            yield return cam.Focus(plant.LeafCenter(leaf), new Vector3(0.1f, 0.6f, 1f), 3.5f, 1.2f);
            hud.SetInvestigation("ENTER THE LEAF", "Leaf → leaf tissue → plant cell", IconId.Leaf); hud.ShowZoomPath(0);   // the breadcrumb previews the descent
            enterLeafWaiting = true;   // "take me inside the leaf" presses the button (WorldAction → hud.HideButton)
            yield return Button("ENTER LEAF", IconId.Leaf, ButtonKind.Gold);
            ch.SetDarkness(0f, 1.5f); hud.ShowZoomPath(1); hud.SetCinematic(true);
            enterLeafWaiting = false;
            Parallel(Say("s8_deeper"));
            au.Play(Sfx.Whoosh); au.Music(MusicState.Microscopic);
            yield return cam.Focus(plant.LeafCenter(leaf), plant.LeafCenter(leaf) - cam.transform.position, 0.25f, 1.6f, 28f);
            yield return GoTo(Place.Tissue, true);
            hud.Callout("tissue", "LEAF TISSUE", "Layers of living cells", Tone.Green, () => m.tissue.Origin + Vector3.up * 1.5f);
            yield return m.tissue.DiveToCell(cam);
            hud.ClearCallouts(); hud.ShowZoomPath(2);
            yield return GoTo(Place.Cell, true);
            hud.SetCinematic(false);
        }

        IEnumerator PlantCell()
        {
            ContextTopic = "inside a plant cell — cell wall, vacuole, nucleus and chloroplasts";
            if (place != Place.Cell) yield return GoTo(Place.Cell, true);
            hud.SetInvestigation("INSIDE A PLANT CELL", "Where is the food factory?", IconId.Cell);
            var c = m.cell;
            hud.Callout("wall", "CELL WALL", "Strong outer support", Tone.Green, () => c.Wall.position);
            hud.Callout("vac", "VACUOLE", "Large store of water", Tone.Blue, () => c.Vacuole.position);
            hud.Callout("nuc", "NUCLEUS", "Control centre", Tone.Cyan, () => c.Nucleus.position);
            Parallel(curio.Scan(c.Vacuole.position, 2f));
            yield return Say("s9_noteverywhere");
            c.DeEmphasize(true); c.IlluminateChloroplasts(true); au.Play(Sfx.ChloroplastActivate, 0.6f);   // the labelled structures dim as the green ones light
            yield return Fx.Wait(1f);
            bool q7 = m.assessment.ShouldAsk("Q7");
            hud.ClearCallouts(); if (q7) hud.ShowZoomPath(-1);   // gone before Q7: the labels would rule out its options; the breadcrumb's next stop names the answer
            yield return Ask("Q7", () => { c.IlluminateChloroplasts(false); return Pulse(() => c.IlluminateChloroplasts(true)); });
            if (q7) hud.ShowZoomPath(2);
            var target = c.PickChloroplast();   // one chloroplast leaves the stream under "inside these" (1.6 s VO + 0.2 s hold ≈ 98 % of the glide), so the tag lands on a settled subject
            yield return Say("s9_inside");
            hud.Callout("chl", "CHLOROPLAST", "Main site of photosynthesis", Tone.Green, () => target.position);   // rides the push-in; GoTo clears it
            yield return Say("s9_chloroplast");
            hud.SetCinematic(true);
            yield return c.EnlargeChloroplast(cam, target);
            hud.ShowZoomPath(3);
            yield return GoTo(Place.Chloroplast, true);
            hud.SetCinematic(false);
        }

        IEnumerator Pulse(Action after) { yield return Fx.Wait(0.6f); after(); yield return Fx.Wait(1.2f); }

        /// Scene 10 reveal: punch in so the finished glucose sits just left of centre (x ≈ 0.46, mid-height), left of the callout flip
        /// line (0.64·w), so the GLUCOSE tag and the scanner card (x ≥ 0.74) stay clear of it. Then the narration push.
        IEnumerator GlucoseHero(Vector3 g)
        {
            Vector3 p = g + new Vector3(-0.6f, 1.6f, -5.8f), l = g + new Vector3(0.35f, -0.05f, 0f);   // ~6 m out, pitch ≈ 16°, yaw ≈ 9° right; local z ≈ -8.65, outside the opened membrane
            if (Hud.ReduceMotion) { cam.Cut(p, l, 45f); yield break; }   // reduced motion: a cut, no dolly
            yield return cam.Blend(p, l, 1.4f, 45f, Ease.Out);   // starts on the GlucoseDiscover burst; "There it is." lands with it
            yield return cam.Push(0.35f, 5.5f);   // 1.4 + 5.5 ≈ 6.9 s, inside thereitis+kabir+glucose, so `yield return push` adds no wait
        }

        IEnumerator EnsureChloroplast()
        {
            if (place != Place.Chloroplast) yield return GoTo(Place.Chloroplast, true);
            if (!chloroplastOpen) { chloroplastOpen = true; yield return m.chloroplast.Open(); }
        }

        IEnumerator ChloroplastBeat()
        {
            ContextTopic = "inside a chloroplast — chlorophyll capturing light";
            yield return EnsureChloroplast();
            hud.SetInvestigation("THE CHLOROPLAST", "The food factory itself", IconId.Chloroplast);
            var cw = m.chloroplast;
            var push = Parallel(cam.Push(0.5f, 8f));   // slow push under the s9 hold; waited for below so it never fights the reaction's re-frame
            cw.HighlightChlorophyll(true);
            hud.Callout("chlorophyll", "CHLOROPHYLL", "Green pigment in the thylakoids", Tone.Green, () => cw.Grana.position);
            au.Play(Sfx.EnergyBuild, 0.7f);
            var light = Parallel(cw.LightStrikes());
            yield return Say("s9_absorbs");
            yield return light;
            yield return Ask("Q8", () => cw.LightStrikes());
            hud.ClearCallouts();
            yield return Say("s9_andnow");
            yield return cw.InputsArrive();
            hud.Callout("h2o", "H₂O", "Water", Tone.Blue, () => cw.Grana.position); hud.Callout("co2", "CO₂", "Carbon dioxide", Tone.Cyan, () => cw.Stroma.position);
            au.Play(Sfx.ChloroplastActivate);
            yield return Say("s9_run");
            yield return push;
        }

        // ================================================================== Scene 10 — The reaction
        IEnumerator Reaction()
        {
            ContextTopic = "the photosynthesis reaction inside the chloroplast";
            bool cold = !chloroplastOpen;   // debug jump: the inputs never flew in
            yield return EnsureChloroplast();
            var cw = m.chloroplast; bool unknown = false, proceed = false;
            if (cold) yield return cw.InputsArrive();
            hud.SetInvestigation("THE REACTION", "Inputs + light energy → ?", IconId.Energy);
            // the three elements of the storyboard: CO₂ · H₂O · LIGHT ENERGY (Callout replaces by key, so a jump straight here is covered)
            hud.Callout("h2o", "H₂O", "Water", Tone.Blue, () => cw.Grana.position); hud.Callout("co2", "CO₂", "Carbon dioxide", Tone.Cyan, () => cw.Stroma.position);
            // the learner feeds the factory: each group is clicked in; one spoken nudge if they stall
            hud.ShowInstruction("Send in the ingredients — tap the CO₂ and the water.", IconId.Energy);
            Parallel(Say("s10_feed"));
            bool fed = false; float lastSend = Time.time; feedWaiting = true; feedAssist = false;
            IEnumerator hint()
            {
                while (!fed && (Time.time - lastSend < 12f || dlg.Speaking || hud.TypingInChat)) yield return null;
                if (fed) yield break;
                curio.Point(cw.Stroma.position); yield return Say("s10_hint");
                while (!fed && Time.time - lastSend < 20f) yield return null;
                if (!fed) { yield return hud.WaitButton("SEND THEM IN", IconId.Energy, ButtonKind.Gold); feedAssist = true; }   // a button that also counts, for anyone still stuck
            }
            Parallel(hint());
            yield return cw.FeedIngredients(cam, () => AutoPlay || feedAssist, isCo2 => { lastSend = Time.time; Parallel(Say(isCo2 ? "s10_inco2" : "s10_inh2o")); au.Play(Sfx.UiSelect); });
            fed = true; feedWaiting = false; hud.HideButton(); hud.HideInstruction();
            au.Music(MusicState.Reaction, 0.5f);   // ignition: the last ingredient throws the switch
            au.Play(Sfx.ChloroplastActivate, 0.8f, 1.15f); au.Play(Sfx.EnergyBuild, 0.6f);
            curio.Point(cw.Stroma.position);
            hud.Callout("light", "LIGHT ENERGY", "captured by chlorophyll", Tone.Gold, () => cw.Grana.position + Vector3.up * 1.2f);
            var form = Parallel(cw.FormGlucose(() =>
            {
                unknown = true; au.Play(Sfx.Beep); hud.HideCallout("h2o"); hud.HideCallout("co2"); hud.HideCallout("light");
                hud.ShowScanner(ScanState.Detected, "UNKNOWN MOLECULE DETECTED", "Analysing structure…", 0.6f);
            }, () => proceed));
            while (!unknown) yield return null;
            if (!Hud.ReduceMotion) Parallel(cam.Push(1.2f, 10f));   // the payoff builds: slow creep on the forming molecule through UNKNOWN MOLECULE + Q9; FormGlucose's 3 s Frame glide has finished by onUnknown (≥4.3 s in) and GlucoseHero's Blend supersedes it
            yield return Ask("Q9", null, force: true);
            proceed = true;
            yield return form;
            glucoseFormed = true;
            au.Play(Sfx.GlucoseDiscover); Fx.Burst(cw.Glucose.position, Palette.Glucose, 60, 0.12f, 3f);
            hud.ShowScanner(ScanState.Complete, "GLUCOSE · C₆H₁₂O₆", "Scan complete · an energy-rich carbohydrate", 1f);
            hud.Callout("glu", "GLUCOSE · C₆H₁₂O₆", "Energy-rich sugar", Tone.Gold, () => cw.Glucose.position);   // the formula rides the 18 px Readout line
        }

        IEnumerator Glucose()
        {
            ContextTopic = "glucose — the food the plant made";
            yield return EnsureChloroplast();
            var cw = m.chloroplast;
            if (!glucoseFormed) { glucoseFormed = true; yield return cw.FormGlucose(null, () => true); hud.Callout("glu", "GLUCOSE · C₆H₁₂O₆", "Energy-rich sugar", Tone.Gold, () => cw.Glucose.position); }
            hud.SetInvestigation("GLUCOSE", "The food the plant makes", IconId.Glucose);
            var push = Parallel(GlucoseHero(cw.Glucose.position));   // reveal punch-in on the new sugar, then the slow push under the narration; done before GlucoseUses re-frames
            yield return Say("s10_thereitis");
            curio.Emote(CurioEmote.Celebrate); au.Music(MusicState.Resolution, 3f);   // the answer lands: the ignition loop resolves under "The answer to Kabir's question"
            yield return SayAll("s10_kabir", "s10_glucose");
            hud.HideScanner();
            yield return push;
            var uses = Parallel(cw.GlucoseUses()); hud.ShowGlucoseUses(true);
            yield return Say("s10_uses");
            yield return uses;
            hud.ShowScanner(ScanState.Complete, "GLUCOSE PUT TO WORK", "Energy · growth · storage", 1f); au.Play(Sfx.Success);   // the FOOD MADE pill over the cards carries that cue
            yield return SayAll("s10_summary", "s10_ownfood");
            hud.ShowGlucoseUses(false); hud.HideScanner();
        }

        // ================================================================== Scene 11 — Oxygen
        bool o2Waiting;   // FOLLOW THE OXYGEN is up: "follow the oxygen" to Dr. Curio presses it (WorldAction)

        IEnumerator Oxygen()
        {
            ContextTopic = "oxygen leaving the leaf through the stomata";
            yield return EnsureChloroplast();
            var cw = m.chloroplast;
            au.Play(Sfx.Beep); curio.Emote(CurioEmote.Surprise); au.Music(MusicState.Investigation, 1.5f);   // "Hold on." — a new clue, the hubs' cue
            yield return Say("s11_hold");
            var rel = Parallel(cw.ReleaseO2()); au.Play(Sfx.OxygenRelease);
            yield return Say("s11_else");
            hud.ShowScanner(ScanState.Complete, "O₂ · OXYGEN", "Another product detected", 1f);
            if (cw.HeroO2) hud.Callout("o2", "OXYGEN — O₂", "Leaving the chloroplast", Tone.Cyan, () => cw.HeroO2.position);   // throws once the hero is destroyed or nulled, so Hud.UpdateCallouts drops it
            hud.SetInvestigation("OXYGEN", "Follow the oxygen out of the leaf.", IconId.O2);
            yield return Say("s11_oxygen");
            yield return rel;
            hud.HideScanner(); hud.ClearCallouts();
            o2Waiting = true;
            Parallel(Say("s11_follow"));   // storyboard "Prompt and VO": the line and the call-to-action land together
            yield return Button("FOLLOW THE OXYGEN", IconId.ArrowUp, ButtonKind.Gold);
            o2Waiting = false; au.Play(Sfx.Whoosh); hud.SetCinematic(true); au.Music(MusicState.WaterJourney, 1f);   // the second ride echoes the xylem ride
            hud.ShowZoomPath(2);
            if (!Hud.ReduceMotion) { Parallel(cam.Push(-3f, 0.7f)); yield return Fx.Wait(0.3f); }   // rise out after the O₂ that just left; the quick dip (0.3–0.52 s) brackets the whoosh peak and cuts mid-move — GoTo's Cut supersedes the Push
            yield return GoTo(Place.Cell, true, m.cell.O2Pose);   // land aimed at the O₂'s source chloroplasts: the chase starts with no whip-pan
            yield return m.cell.O2AcrossCell(cam);
            hud.ShowZoomPath(1);
            yield return GoTo(Place.Tissue, true);
            yield return m.tissue.FollowO2(cam);
            yield return EnsureLeafSurface(2);
            hud.SetCinematic(false); au.Music(MusicState.Microscopic, 2f);   // back at the same stoma: the Stomata beat's cue, and quiet under Q10
            hud.Callout("stoma", "SAME STOMA", "Now in reverse", Tone.Green, () => m.leafSurface.Stoma.position);
            if (!Hud.ReduceMotion) Parallel(cam.Push(-0.8f, 3.5f));   // pull OUT with the O₂ (the reverse of Scene 4's push-in); the s11_doorway Orbit below supersedes it with its eased hand-off
            yield return m.leafSurface.O2Exit();
            hud.ShowInOut(true, false);                    // badges now; the caption waits for its own line
            Parallel(m.leafSurface.CO2Enter());
            yield return Say("s11_entered");
            var o2Out = Parallel(m.leafSurface.O2Exit());  // storyboard: "Oxygen leaves." -> O₂ exits, together
            yield return Say("s11_leaves");
            yield return o2Out;                            // bounded 3.6 s; O2Exit breaks on Module.Skipping / a newer run
            m.leafSurface.BothFlows(true);
            var st = m.leafSurface.Stoma.position;
            cam.Orbit(st, Vector3.Distance(cam.transform.position, st), cam.transform.eulerAngles.y, 25f, true, 2.5f, 10f, pitchMin: 15f, pitchMax: 70f);   // pitch 15–70 keeps the look down into the pore; 2.5–10 m stays over the flat slab
            hud.ShowInOut(true);                           // caption shows only when subtitles are off (Hud hides it while the same subtitle is up)
            yield return Say("s11_doorway");
            hud.ShowInstruction("Rotate the leaf and watch both journeys.", IconId.Replay);   // the caption yields to the prompt (Hud.RelayoutBottom)
            yield return Fx.Wait(AutoPlay ? 0.5f : 4f);
            bool q10 = m.assessment.ShouldAsk("Q10");
            hud.ShowInOut(false); hud.HideInstruction(); hud.HideCallout("stoma");   // the badges, "Now in reverse" and the OXYGEN title spell out Q10's answer
            if (q10) hud.SetInvestigation("TWO JOURNEYS", "Quick check at the stoma.", IconId.Stomata);
            yield return Ask("Q10", () => m.leafSurface.O2Exit());
            if (q10) hud.SetInvestigation("OXYGEN", "Follow the oxygen out of the leaf.", IconId.O2);   // as CO2() restores Clue 1's title
            m.leafSurface.BothFlows(false); hud.ClearCallouts(); hud.HideMagnification();
        }

        // ================================================================== Scene 12 — Case closed? Phloem
        IEnumerator Phloem()
        {
            ContextTopic = "phloem carrying food from the leaves to the rest of the plant";
            hud.SetInvestigation("CASE CLOSED?", "The plant made its own food.", IconId.Glucose);   // the scene's name; OXYGEN would sit stale over the punchline
            hud.ShowScanner(ScanState.Complete, "FOOD MADE ✓", "Glucose · C₆H₁₂O₆", 1f);        // the punchline's subject: the stoma shot holds no glucose
            yield return Say("s12_foodmade");   // where the learner already is: the stoma the O₂ just left
            var shut = Parallel(curio.ShutScanner());
            yield return Fx.Wait(0.72f);          // ShutScanner's iris snap: the readout closes with the lens
            hud.HideScanner();
            yield return shut;
            yield return Say("s12_closed");
            yield return Fx.Wait(0.3f);
            curio.Emote(CurioEmote.Surprise);     // the double-take (1.4 s): jolts back on a still camera, then the dive cuts on it
            yield return Fx.Wait(0.35f);
            hud.SetCinematic(true);
            Coroutine dive = null;
            if (place == Place.LeafSurface)   // a cold start arrives in the chamber: no push
            {
                var st = m.leafSurface.Stoma.position;
                au.Play(Sfx.Whoosh, 0.6f);
                dive = Parallel(cam.Focus(st, st - cam.transform.position, 0.6f, 1.3f, 30f));   // back in through the same doorway; Curio's double-take motivates the cut
            }
            yield return Say("s12_ofcourse");
            if (dive != null) yield return dive;
            yield return GoTo(Place.Tissue, true);
            var food = Parallel(m.tissue.FoodToPhloem(cam));
            yield return Say("s12_leaves");
            yield return food;
            hud.ShowZoomPath(0);
            yield return GoTo(Place.Chamber, true);
            hud.SetCinematic(false);
            hud.SetInvestigation("PHLOEM", "Food travels from the leaves to the rest of the plant.", IconId.Phloem); au.Music(MusicState.Investigation, 2f);
            yield return plant.SetTransparency(0.72f, 1.5f);
            ch.SetFloorTransparent(true, 1f); plant.ShowRoots(true);
            plant.SetVessels(false, true); plant.SetFlow(Flow.Phloem, true); plant.SetFlow(Flow.Glucose, true);
            cam.Orbit(PlantFocus + Vector3.down * 0.5f, OrbitDist + 0.4f, cam.transform.eulerAngles.y, 10f, false, 3f, 11.5f, WholeFov); cam.AutoSpin = 10f;   // crown to roots in frame (PhloemReteach matches)
            yield return Say("s12_rest");
            hud.Callout("phloem", "PHLOEM", "Food from the leaves", Tone.Orange, () => plant.Base + Vector3.up * 1.3f);
            hud.Callout("shoot", "GROWING SHOOT", "", Tone.Orange, () => plant.Shoot);
            hud.Callout("rootsF", "ROOTS", "", Tone.Orange, () => plant.RootTip + Vector3.up * 1.5f);   // on the main root, clear of the dialogue panel
            hud.Callout("flower", "FLOWERS / FRUITS", "", Tone.Orange, () => plant.Flower);
            hud.Callout("store", "STORAGE", "", Tone.Orange, () => plant.Storage);
            yield return Say("s12_phloem");
            cam.AutoSpin = 0; cam.UserControl = true;
            hud.HideCallout("phloem"); hud.SetInvestigation("THE SECOND HIGHWAY", "Where does the food go?", IconId.Phloem);   // no answer on screen for Q11
            yield return Ask("Q11", () => PhloemReteach(Concept.Phloem));
            yield return Ask("Q12", () => PhloemReteach(Concept.Xylem));
            hud.SetInvestigation("PHLOEM", "Food travels from the leaves to the rest of the plant.", IconId.Phloem); hud.ClearCallouts();
            Action<int> apply = mode =>
            {
                plant.SetVessels(mode != 1, mode != 0);
                plant.SetFlow(Flow.Water, mode != 1); plant.SetFlow(Flow.Phloem, mode != 0); plant.SetFlow(Flow.Glucose, mode != 0);
            };
            hud.ShowComparison(x => { apply(x); au.Play(Sfx.UiSelect); }); apply(2);   // 3D matches the pre-selected "Show Both"
            yield return Say("s12_compare");
            yield return Button("Continue", IconId.ChevronRight, ButtonKind.Primary);
            hud.HideComparison(); hud.ShowZoomPath(-1); hud.HideLine();   // 'Try the switches' must not linger over the reset plant
            plant.AllFlows(false); plant.SetVessels(false, false); ch.SetFloorTransparent(false, 1f); plant.ShowRoots(false);
            yield return plant.SetTransparency(0f, 1f);
        }

        // ================================================================== Scene 13 — The equation
        IEnumerator Equation()
        {
            ContextTopic = "the photosynthesis equation";
            if (place != Place.Chamber) yield return GoTo(Place.Chamber, true);
            au.Music(MusicState.Resolution);
            hud.SetInvestigation("THE EQUATION", "Now it all makes sense.", IconId.Sparkle);
            curio.Place(CurioSpot, PlantFocus); curio.LookAtCamera();
            yield return cam.Arc(Establish, PlantFocus + Vector3.up * 0.4f, 2.5f, 52f);   // round the plant from the comparison orbit, never through it
            Parallel(cam.Push(0.5f, 10f));   // slow push under the equation lines; Scene 14's orbit supersedes it
            yield return Ask("Q13", () => hud.Equation());   // storyboard: Q13 comes before the equation is revealed; its lead opens the beat over the Arc
            var eq = Parallel(hud.Equation());               // "Remember this?" lands as the PHOTOSYNTHESIS title and 6CO₂ fly in (EqStart[0] = 0.5 s)
            yield return Say("s13_remember");
            yield return eq;                                 // let the rest of the 4.4 s build finish before "That's photosynthesis."
            yield return Say("s13_thats");
            // the equation lifts to a compact token row while the two highways light under a light dim
            Parallel(plant.SetTransparency(0.6f, 1.2f));
            yield return Say("s13_highways");
            hud.EquationCompact();        // the equation steps aside as the highways light, each on its own line
            plant.SetVessels(true, false); plant.SetFlow(Flow.Water, true);
            curio.Point(plant.Base + Vector3.up * 1.2f);
            yield return Say("s13_xylem");
            plant.SetVessels(true, true); plant.SetFlow(Flow.Phloem, true);
            yield return Say("s13_phloem");
            yield return Fx.Wait(AutoPlay ? 0.5f : 1.5f);
            hud.HideEquation();
            plant.SetFlow(Flow.Water, false); plant.SetFlow(Flow.Phloem, false); plant.SetVessels(false, false);
            yield return plant.SetTransparency(0f, 0.8f);   // Scene 14 opens on the opaque "doing nothing" plant
        }

        // ================================================================== Scene 14 — The whole working plant
        Action restoreFinal;   // Scene 14: puts the working plant (and the experiment's switches) back after an AI demo

        IEnumerator FinalPlant()
        {
            ContextTopic = "the whole plant with every process running together";
            if (place != Place.Chamber) yield return GoTo(Place.Chamber, true);
            restoreFinal = null;
            hud.SetInvestigation("THE WORKING PLANT", "Every process, all at once.", IconId.Plant);
            curio.LookAtCamera();
            var med = plant.Base + new Vector3(3.3f, 1.9f, -5.5f); var medLook = Vector3.Lerp(PlantFocus, CurioSpot, 0.78f);   // "At the beginning…": Scene 3's first medium on Curio as the callback; working()'s orbit pulls back out as the plant ghosts
            if (Hud.ReduceMotion) cam.Cut(med, medLook, 40f); else Parallel(cam.Blend(med, medLook, 2.4f, 40f));
            yield return Say("s14_beginning");
            yield return Say("s14_better");
            ch.SetFloorTransparent(true, 1.5f); plant.ShowRoots(true);
            yield return plant.SetTransparency(0.6f, 2f);
            void working()
            {   // sunlight reads through the golden light flow; a beam flare would hide the processes
                ch.SetFloorTransparent(true, 1f); plant.ShowRoots(true); Parallel(plant.SetTransparency(0.6f, 1f)); plant.SetVessels(true, true); plant.AllFlows(true);
                cam.Orbit(PlantFocus + Vector3.down * 0.5f, OrbitDist + 0.4f, cam.transform.eulerAngles.y, 10f, true, 3f, 11.5f, WholeFov); cam.AutoSpin = 8f;   // crown clear of the top HUD, roots above the bottom panels; spins only when idle
            }
            working(); restoreFinal = working;
            hud.ShowInstruction("Rotate the plant — every process is running at once.", IconId.Replay);   // stays up through the lines
            yield return Fx.Wait(AutoPlay ? 0.5f : 4f);
            hud.HideInstruction();   // the roots and the water uptake sit under it: the lines play over a clear plant
            yield return SayAll("s14_mouth", "s14_plate", "s14_kitchen", "s14_tiny", "s14_look", "s14_make");
            yield return Finale();
        }

        /// The final educational reveal. A slow crane rises over the working plant while the whole relationship is labelled on it in 3D
        /// (water, carbon dioxide and light go in; glucose and oxygen come out), then "This is photosynthesis!" is spoken and shown, the
        /// processes settle, the plant turns solid, the music resolves on a cadence and the picture fades to black under the title.
        /// Nothing mandatory follows: the end menu offers Replay · Review (Free Explore, with the factory controls) · Exit.
        IEnumerator Finale()
        {
            ContextTopic = "the end of the investigation";
            hud.HideInstruction(); hud.HideLine(); hud.SetCinematic(true); hud.SetAskAIVisible(false);
            restoreFinal = null; cam.AutoSpin = 0f; curio.LookAt(plant.Top);
            // crane from the working-plant orbit up over the crown, looking down across it to the glass. The end pose stays inside the dome:
            // (height / 10.3)² + (distance / 12)² < 1 from the chamber centre, or the lens enters the rib fins.
            var top = plant.Base + new Vector3(0f, 6.2f, -8.4f); var topLook = PlantFocus + Vector3.up * 0.3f;
            if (Hud.ReduceMotion) cam.Cut(top, topLook, 56f); else Parallel(cam.Arc(top, topLook, 15f, 56f));
            int leaf = plant.FocusLeaf;
            // the relationship on the plant itself: three inputs arrive, two products leave
            var tags = new (string key, string title, string sub, Tone tone, Func<Vector3> at, float t)[]
            {
                ("fw", "WATER", "up from the roots", Tone.Blue, () => plant.RootTip, 0.8f),
                ("fc", "CARBON DIOXIDE", "in from the air", Tone.Cyan, () => plant.LeafUnderside(leaf), 1.6f),
                ("fs", "SUNLIGHT", "light energy", Tone.Gold, () => plant.Top + Vector3.up * 0.2f, 2.4f),
                ("fg", "GLUCOSE", "food, made in the leaves", Tone.Orange, () => plant.Base + Vector3.up * 1.2f, 3.8f),
                ("fo", "OXYGEN", "out to the air", Tone.Green, () => plant.LeafCenter(leaf) + Vector3.up * 0.7f, 4.6f),
            };
            float t0 = Time.time;
            foreach (var g in tags)
            {
                while (Time.time - t0 < g.t && !Module.Skipping) yield return null;
                hud.Callout(g.key, g.title, g.sub, g.tone, g.at); au.Play(g.t < 3f ? Sfx.Pop : Sfx.Sparkle, 0.55f);
            }
            yield return Fx.Wait(1.2f);
            au.Play(Sfx.EnergyBuild, 0.5f);   // the one riser, timed to peak as the title lands (~2.3 s)
            yield return Fx.Wait(1.2f);
            foreach (var g in tags) hud.HideCallout(g.key);
            yield return Fx.Wait(1.1f);
            // the reveal, spoken and shown together; the title sits in the upper third above the crown and is the caption
            au.Play(Sfx.GlucoseDiscover, 0.8f); curio.Emote(CurioEmote.Celebrate);
            var title = Parallel(hud.Title("This is Photosynthesis", "Light energy turns water and carbon dioxide into glucose and oxygen", 11f, 80f, 0.1f));
            Parallel(Say("s14_this")); hud.HideLine();
            yield return Fx.Wait(2.2f);
            // the processes settle one by one under the title; the plant turns solid again and the floor closes
            foreach (var f in new[] { Flow.Sunlight, Flow.CO2, Flow.Water, Flow.Chloroplast, Flow.Glucose, Flow.O2, Flow.Phloem }) { plant.SetFlow(f, false); yield return Fx.Wait(0.35f); }
            plant.SetVessels(false, false); Parallel(plant.SetTransparency(0f, 2.5f)); ch.SetFloorTransparent(false, 2.5f);
            yield return Fx.Wait(2.6f);
            plant.ShowRoots(false);
            // the music resolves on a cadence and the picture fades to black while the title is still up: the module ends on the statement
            au.Play(Sfx.FinaleCadence, 0.9f); au.Music(MusicState.None, 1.2f); au.Ambience("none");
            hud.SetHudVisible(false); hud.SetZoomControlVisible(false);   // Menu / Reset / Help and zoom leave with the picture; Prepare brings them back for Replay and Review
            yield return hud.Fade(1f, 3.2f, Color.black);
            yield return title;          // the title holds alone on black, then fades
            yield return Fx.Wait(AutoPlay ? 0.2f : 1f);
            int choice = -1;
            if (AutoPlay) { Parallel(hud.EndMenu(r => { })); yield return Fx.Wait(1.5f); hud.CancelWaits(); choice = 1; }   // tests go on to Review (Free Explore + factory controls)
            else yield return hud.EndMenu(r => choice = r);
            fadeIn = choice != 2;
            switch (choice)
            {
                case 0: m.assessment.Reset(); Run(Beat.Scene2Start); break;   // Replay (Run stops this coroutine; nothing may follow)
                case 1: hud.SetAskAIVisible(true); Run(Beat.FreeExplore); break;   // Review: the whole plant, every process, the factory controls
                default: Quit(); break;
            }
        }

        /// "Break the factory": the learner switches supplies off and watches FOOD MADE / FOOD DELIVERED, then leaves from the panel when ready.
        IEnumerator Experiment(int gen = -1)   // gen ≥ 0: Free Explore's Factory controls; a newer pick ends it
        {
            hud.HideInstruction();
            Parallel(Say("exp_intro"));
            var on = new[] { true, true, true, true }; var seen = new bool[4]; int tried = 0; bool go = false, praised = false;   // 0 sunlight · 1 CO₂ · 2 water · 3 phloem
            string pending = null; bool live = true;   // a switch line waits for the one playing instead of cutting it off (the newest wins)
            IEnumerator voice() { while (live) { if (pending != null && !dlg.Speaking) { var id = pending; pending = null; yield return Say(id); } else yield return null; } }
            Parallel(voice());
            void apply()
            {   // a dormant plant suppresses every flow; with only the phloem cut, glucose still shows in the leaves
                bool made = on[0] && on[1] && on[2];
                plant.SetFlow(Flow.Sunlight, on[0]); plant.SetFlow(Flow.Chloroplast, on[0]); plant.SetFlow(Flow.CO2, on[1]);
                plant.SetFlow(Flow.Water, on[2]); plant.SetFlow(Flow.Phloem, on[3]); plant.SetFlow(Flow.Glucose, made);
                plant.SetVessels(on[2], on[3]); plant.SetDormant(!made);
                hud.SetFoodMeters(made ? 1f : 0f, made && on[3] ? 1f : 0f);
            }
            void flip(int i, bool v)
            {
                if (on[i] == v) return;
                on[i] = v; apply();
                if (i == 0) ch.SetDarkness(v ? 0f : 0.8f, 1.2f);
                if (!v && !seen[i]) { seen[i] = true; tried++; curio.Emote(CurioEmote.Think); pending = i switch { 0 => "exp_light", 1 => "exp_co2", 2 => "exp_water", _ => "exp_phloem" }; }
                else if (v && !praised && tried >= 2 && on[0] && on[1] && on[2] && on[3]) { praised = true; curio.Emote(CurioEmote.Celebrate); pending = "exp_all"; }
            }
            hud.ShowExperiment(flip, () => go = true);
            var plain = restoreFinal; restoreFinal = () => { plain?.Invoke(); apply(); };
            if (AutoPlay)
            {
                for (int k = 0; k < 4; k++)
                    foreach (var to in new[] { false, true }) { yield return Fx.Wait(0.8f); hud.SetExperimentToggle(k, to); flip(k, to); }
                yield return Fx.Wait(0.8f);
            }
            else while (!go && !AutoPlay && !Module.Skipping && (gen < 0 || gen == exploreGen)) yield return null;
            live = false;
            hud.HideExperiment();
            au.StopVoice(); dlg.Cancel(); hud.HideLine(); curio.Talking = false;   // an exp_* line must not carry over the FINAL INVESTIGATION move
            if (!on[0]) ch.SetDarkness(0f, 1f);
            if (gen >= 0 && gen != exploreGen) { plant.SetDormant(false); yield break; }   // the new destination sets its own flows
            for (int k = 0; k < 4; k++) on[k] = true;
            apply();   // everything back on, not dormant
        }

        // ================================================================== Final adaptive assessment
        IEnumerator FinalAssessment()
        {
            ContextTopic = "the final investigation questions";
            au.Music(MusicState.Investigation, 3f);   // a test under the clue cue; Complete()'s Resolution now actually arrives
            if (place != Place.Chamber) yield return GoTo(Place.Chamber, true);
            plant.AllFlows(false); plant.SetVessels(false, false); ch.SetSunBeam(false, Vector3.zero);
            ch.SetFloorTransparent(false, 1f); plant.ShowRoots(false);
            yield return plant.SetTransparency(0f, 1f);
            hud.SetInvestigation("FINAL INVESTIGATION", "Show what you can explain.", IconId.Target);
            // the question card takes the left 40 %: look past the plant's left so it sits in the right two-thirds. The yaw is pinned
            // (the old Establish+(-0.6,0.2,0) heading) so Curio's spot stays in the empty lower right after every reteach.
            const float qYaw = -6f;
            var qFocus = PlantFocus + new Vector3(-1.6f, 0.3f, 0f); var qPos = qFocus + Quaternion.Euler(10f, qYaw, 0f) * new Vector3(0f, 0f, -OrbitDist);
            Parallel(curio.Materialize(plant.Base + new Vector3(1.2f, 2.4f, -3.9f), Establish));   // the interviewer beams in beside the plant while the camera swings round (hides the jump from CurioSpot)
            yield return cam.Arc(qPos, qFocus, 2.6f, 48f);   // lands on the orbit pose: one move, from wherever the turntable stopped
            cam.Orbit(qFocus, OrbitDist, qYaw, 10f, false, 3f, 11.5f);
            curio.LookAtCamera();
            yield return Say("s15_final");
            var set = m.assessment.FinalSet(5);
            IEnumerator reteach(Concept k) { yield return DemoInChamber(k); yield return cam.Arc(qPos, qFocus, 1.5f, 48f); cam.Orbit(qFocus, OrbitDist, qYaw, 10f, false, 3f, 11.5f); }   // same card framing and Curio spot
            for (int i = 0; i < set.Count; i++)
            {
                var concept = set[i].concept;   // Assessment may re-plan later slots as the learner answers
                bool dark = concept == Concept.Sunlight;   // stage only the premise ("a plant kept in darkness"), never the answer
                if (dark) ch.SetDarkness(0.6f, 1.5f);
                if (concept == Concept.Stomata) { plant.HighlightLeaf(plant.FocusLeaf, true); curio.Point(plant.LeafCenter(plant.FocusLeaf)); }   // "Here's a leaf…": Ask() clears the rim when the card opens
                yield return Ask(set[i].id, () => reteach(concept), true, i + 1, set.Count);
                if (dark) { ch.SetDarkness(0f, 1.5f); yield return plant.EnergyWave(plant.FocusLeaf); }   // lights back: the leaf lights up
            }
        }

        IEnumerator Complete()
        {
            ContextTopic = "the end of the investigation";
            if (place != Place.Chamber) yield return GoTo(Place.Chamber, true);
            au.Music(MusicState.Resolution);
            hud.SetInvestigation("CASE SOLVED", "Three clues. One leaf. Food made.", IconId.Sparkle);
            Parallel(curio.Materialize(CurioSpot, PlantFocus)); curio.LookAtCamera();   // back from the interviewer spot to where he set the case
            Parallel(cam.Blend(plant.Base + new Vector3(2.4f, 2.1f, -6.6f), Vector3.Lerp(PlantFocus, CurioSpot, 0.5f), 2.6f, 46f));   // Scene 3's two-shot
            ch.SetFloorTransparent(true, 1.5f); plant.ShowRoots(true);
            yield return plant.SetTransparency(0.6f, 2.5f);
            plant.SetVessels(true, true); plant.AllFlows(true);   // the factory running again
            curio.Emote(CurioEmote.Celebrate);
            yield return Say("s15_solved");
            cam.Orbit(PlantFocus, OrbitDist, cam.transform.eulerAngles.y, 12f, false, 3f, 11.5f); cam.AutoSpin = 6f;   // slow turntable behind the card
            au.Play(Sfx.Success);
            int choice = 0;
            if (AutoPlay) { Parallel(hud.Complete(m.assessment.ScoredCorrect, m.assessment.Scored, m.assessment.Level, r => { })); yield return Fx.Wait(1.5f); hud.CancelWaits(); }
            else yield return hud.Complete(m.assessment.ScoredCorrect, m.assessment.Scored, m.assessment.Level, r => choice = r);
            cam.AutoSpin = 0;
            if (choice == 1) { Quit(); yield break; }
        }

        // ================================================================== Scene 15 — Free Explore
        IEnumerator FreeExplore()
        {
            ContextTopic = "free exploration of the whole plant";
            if (place != Place.Chamber) yield return GoTo(Place.Chamber, true);
            au.Music(MusicState.FreeExplore);
            hud.SetProgressVisible(false); hud.SetInvestigation("FREE EXPLORE", "Rotate, zoom and follow any process.", IconId.Eye);
            m.HubOpen = true;
            pick = null;
            hud.ShowFreeExplore(k => pick = k, () => pick = "Exit");
            hud.SetZoomPath(0);
            StartCoroutine(Explore("WholePlant", ++exploreGen));   // home: the working plant on an idle turntable (synchronous in the chamber)
            yield return Say("s15_explore");
            if (AutoPlay) { foreach (var k in new[] { "Stomata", "Xylem", "Cell", "Experiment", "WholePlant" }) { yield return Explore(k, ++exploreGen); } yield break; }
            while (true)
            {
                while (pick == null) yield return null;
                var k = pick; pick = null;
                if (k == "Exit") { hud.HideFreeExplore(); Quit(); yield break; }
                if (k == "FollowWater" && place == Place.Xylem) continue;   // already riding; that child ends with WholePlant
                StartCoroutine(Explore(k, ++exploreGen));   // tiles and AI stay responsive mid-ride; the older destination yields out
            }
        }

        static (string, string, IconId) ExploreInfo(string k) => k switch
        {
            "Roots" => ("ROOTS", "Absorb water and minerals from the soil.", IconId.Root),
            "Stem" => ("STEM", "Xylem and phloem run inside it.", IconId.Stem),
            "Leaf" => ("LEAF", "The food factory: light energy makes glucose here.", IconId.Leaf),
            "Stomata" => ("STOMATA", "Tiny pores: CO₂ in, O₂ out.", IconId.Stomata),
            "FollowCO2" => ("FOLLOW CO₂", "Carbon dioxide enters through a stoma.", IconId.CO2),
            "FollowO2" => ("FOLLOW OXYGEN", "Oxygen leaves through the same stoma.", IconId.O2),
            "Xylem" => ("XYLEM", "Water travels up from the roots.", IconId.Xylem),
            "Phloem" => ("PHLOEM", "Food travels from the leaves to the rest of the plant.", IconId.Phloem),
            "FollowFood" => ("FOLLOW FOOD", "Glucose travels down the phloem to the rest of the plant.", IconId.Glucose),
            "Cell" => ("PLANT CELL", "Nucleus, vacuole and chloroplasts.", IconId.Cell),
            "Chloroplast" => ("CHLOROPLAST", "Chlorophyll absorbs light in the grana.", IconId.Chloroplast),
            "FollowWater" => ("FOLLOW WATER", "Up the xylem from root to leaf.", IconId.Drop),
            "Experiment" => ("FACTORY CONTROLS", "Switch a supply off and watch the meters.", IconId.Energy),
            _ => ("FREE EXPLORE", "Rotate, zoom and follow any process.", IconId.Eye)   // WholePlant: the home view keeps the mode name
        };

        /// One Free Explore destination. `gen` is checked after every yield so a newer pick wins instead of racing this one.
        IEnumerator Explore(string key, int gen)
        {
            m.HubOpen = true; au.Play(Sfx.UiSelect);
            var (xt, xs, xi) = ExploreInfo(key); hud.SetInvestigation(xt, xs, xi); hud.SelectExplore(key);   // AI / autoplay picks light their tile too
            plant.AllFlows(false); plant.SetVessels(false, false); hud.ClearCallouts(); hud.HideInfo(); ch.SetSunBeam(false, Vector3.zero); cam.AutoSpin = 0f;
            m.leafSurface.BothFlows(false);
            int leaf = plant.FocusLeaf;
            switch (key)
            {
                case "Stomata":
                case "FollowCO2":
                case "FollowO2":
                    hud.SetZoomPath(1);
                    yield return EnsureLeafSurface(2); if (gen != exploreGen) yield break;
                    var st = m.leafSurface.Stoma.position;
                    cam.Orbit(st, Vector3.Distance(cam.transform.position, st), cam.transform.eulerAngles.y, 25f, true, 2.5f, 10f, pitchMin: 15f, pitchMax: 70f);   // looks down into the pore, stays over the flat slab
                    hud.Callout("stoma", "STOMA", "CO₂ in · O₂ out", Tone.Green, () => m.leafSurface.Stoma.position);
                    if (key == "FollowCO2") yield return m.leafSurface.CO2Enter();
                    else if (key == "FollowO2") yield return m.leafSurface.O2Exit();
                    if (gen == exploreGen) m.leafSurface.BothFlows(true);   // the doorway keeps breathing after a one-shot follow
                    yield break;
                case "Cell":
                    hud.SetZoomPath(2);
                    yield return GoTo(Place.Cell, true); if (gen != exploreGen) yield break;
                    var c = m.cell; c.DeEmphasize(false); c.IlluminateChloroplasts(true);
                    cam.Orbit(c.Origin, 9f, 0, 10f, true, 2f, 11f);
                    hud.Callout("nuc", "NUCLEUS", "", Tone.Cyan, () => c.Nucleus.position); hud.Callout("vac", "VACUOLE", "", Tone.Blue, () => c.Vacuole.position);
                    yield break;
                case "Chloroplast":
                    hud.SetZoomPath(3);
                    yield return EnsureChloroplast(); if (gen != exploreGen) yield break;
                    m.chloroplast.HighlightChlorophyll(true); cam.Orbit(m.chloroplast.Origin, 9f, 0, 10f, true, 2f, 11f);
                    hud.Callout("gr", "GRANA", "Chlorophyll absorbs light here", Tone.Green, () => m.chloroplast.Grana.position);
                    yield break;
                case "Experiment":
                {   // "break the factory": the working plant with its switches, then back to the tour
                    hud.SetZoomPath(0);
                    if (place != Place.Chamber) { yield return GoTo(Place.Chamber, true); if (gen != exploreGen) yield break; }
                    ch.SetFloorTransparent(true, 1f); plant.ShowRoots(true); Parallel(plant.SetTransparency(0.6f, 1f)); plant.SetVessels(true, true); plant.AllFlows(true);
                    cam.Orbit(PlantFocus + Vector3.down * 0.5f, OrbitDist + 0.4f, cam.transform.eulerAngles.y, 10f, true, 3f, 11.5f, WholeFov);
                    hud.HideFreeExplore();
                    yield return Experiment(gen);
                    hud.ShowFreeExplore(k => pick = k, () => pick = "Exit");   // always back: a newer pick must never leave the learner without tiles or Exit
                    if (gen != exploreGen) yield break;
                    hud.SetZoomPath(0); hud.SelectExplore("WholePlant");
                    yield break;
                }
                case "FollowWater":
                    yield return GoTo(Place.Xylem, true); if (gen != exploreGen) yield break;
                    yield return m.xylem.Ride(cam, s => hud.ShowScanner(ScanState.Scanning, s, "Following water up the xylem", -1));
                    hud.HideScanner();
                    if (gen != exploreGen || place != Place.Xylem) yield break;   // a destination chosen mid-ride must not be reverted
                    yield return Explore("WholePlant", gen); yield break;
            }
            // chamber destinations
            hud.SetZoomPath(key == "Leaf" ? 1 : 0);
            if (place != Place.Chamber) { yield return GoTo(Place.Chamber, true); if (gen != exploreGen) yield break; }
            ch.SetFloorTransparent(key == "Roots" || key == "Xylem" || key == "Phloem" || key == "FollowFood", 1f);
            plant.ShowRoots(key == "Roots" || key == "Xylem" || key == "Phloem" || key == "FollowFood");
            bool ghost = key == "Xylem" || key == "Phloem" || key == "FollowFood" || key == "Stem" || key == "WholePlant";
            Parallel(plant.SetTransparency(ghost ? 0.7f : 0f, 1f));
            switch (key)
            {
                case "Roots": cam.Orbit(plant.RootTip + Vector3.up * 0.8f, 6f, cam.transform.eulerAngles.y, 5f, true, 2f, 11f, 50f); hud.Callout("r", "ROOTS", "Absorb water and minerals", Tone.Blue, () => plant.RootTip); break;
                case "Stem": cam.Orbit(plant.Base + Vector3.up * 1.2f, 6f, cam.transform.eulerAngles.y, 8f, true, 2f, 11f, 50f); plant.SetVessels(true, true); hud.Callout("s", "STEM", "Xylem + phloem run inside", Tone.Green, () => plant.Base + Vector3.up * 1.2f); break;
                case "Leaf": cam.Orbit(plant.LeafCenter(leaf), 3.5f, cam.transform.eulerAngles.y, 20f, true, 1.2f, 10f, 50f); plant.HighlightLeaf(leaf, true); plant.SetFlow(Flow.Sunlight, true); plant.SetFlow(Flow.Chloroplast, true); break;
                case "Xylem": plant.SetVessels(true, false); plant.SetFlow(Flow.Water, true); hud.Callout("x", "XYLEM", "Water ↑ from the roots", Tone.Blue, () => plant.Base + Vector3.up * 1f); cam.Orbit(PlantFocus + Vector3.down * 0.5f, OrbitDist + 0.4f, cam.transform.eulerAngles.y, 10f, true, 2f, 11.5f, WholeFov); break;
                case "Phloem":
                case "FollowFood": plant.SetVessels(false, true); plant.SetFlow(Flow.Phloem, true); plant.SetFlow(Flow.Glucose, true); hud.Callout("p", "PHLOEM", "Food from the leaves", Tone.Orange, () => plant.Base + Vector3.up * 1f); cam.Orbit(PlantFocus + Vector3.down * 0.5f, OrbitDist + 0.4f, cam.transform.eulerAngles.y, 10f, true, 2f, 11.5f, WholeFov); break;   // Phloem beat's whole-plant framing
                default: plant.HighlightLeaf(leaf, false); plant.SetVessels(true, true); plant.AllFlows(true); cam.Orbit(PlantFocus, OrbitDist, cam.transform.eulerAngles.y, 10f, true, 2f, 11.5f, 50f); cam.AutoSpin = 6f; break;   // the working plant; turns only when idle
            }
        }

        IEnumerator BriefLeafHighlight() { plant.HighlightLeaf(plant.FocusLeaf, true); yield return Fx.Wait(4f); plant.HighlightLeaf(plant.FocusLeaf, false); }

        // ================================================================== AI world actions
        /// Map an understood intent to a safe, predefined world action. Returns an extra line for Dr. Curio
        /// (e.g. why navigation must wait) or null. Never breaks clue order.
        public string WorldAction(Intent intent)
        {
            var c = m.clues; bool hub = m.HubOpen; var beat = m.Beat;
            // Every non-launch reply keeps "finish"/"first"/"later"/"soon": AskAI.Describe reads those to hide the WORLD ACTION tag.
            string Navigate(ClueId id, string concept)
            {
                if (c[id] == ClueState.Locked) return $"{concept} comes later — let's crack the clue we're on first!";
                if (c[id] == ClueState.Available && beat < Beat.FreeExplore)
                {
                    var hubBeat = id == ClueId.CO2 ? Beat.Clue1 : id == ClueId.Water ? Beat.Clue2 : Beat.Clue3;
                    if (hub) { forcedClue = id; return "Follow me!"; }
                    if (beat < hubBeat) { forcedClue = id; return "Let's finish what we're looking at — then I'll take you straight there."; }   // Hub's pre-check honours it
                    return "We're on that trail right now — let's finish it first!";
                }
                if (!hub) return "Let's finish what we're looking at first — then ask me again.";
                return null;
            }
            string Demo(Concept k, string reply = null) => StartDemo(DemoInChamber(k)) ? reply : "Watch this one first!";
            if (feedWaiting && (intent == Intent.ShowCO2 || intent == Intent.ShowWater)) { feedAssist = true; return "In they go — watch!"; }   // Scene 10: sends the ingredients the learner couldn't hit (no finish/first/later/soon, so the WORLD ACTION tag shows)
            switch (intent)
            {
                case Intent.ShowCO2:
                case Intent.ShowStomata:
                    if (beat == Beat.Stomata || beat == Beat.CO2) { hud.Callout("stoma", "STOMA", "CO₂ enters here", Tone.Green, () => m.leafSurface.Stoma.position); return null; }
                    { var r = Navigate(ClueId.CO2, "Carbon dioxide"); if (r != null) return r; }
                    if (beat >= Beat.FreeExplore) { pick = "Stomata"; return "Here are the stomata."; }
                    return Demo(Concept.Stomata, "Watch the underside of this leaf.");
                case Intent.ShowWater:
                case Intent.ShowRoots:
                case Intent.ShowXylem:
                    if (beat == Beat.Water && intent != Intent.ShowXylem) { if (pickPending) { forcedPick = true; return null; } return "That's our molecule — watch where it goes!"; }
                    { var r = Navigate(ClueId.Water, "Water"); if (r != null) return r; }
                    if (beat >= Beat.FreeExplore) { pick = intent == Intent.ShowRoots ? "Roots" : "Xylem"; return null; }
                    return Demo(Concept.Xylem, "Follow the blue path up from the roots.");
                case Intent.ShowSunlight:
                    if (skylightWaiting) { skylightHit = true; return "Follow me!"; }   // Scene 8: counts as the skylight click
                    { var r = Navigate(ClueId.Sunlight, "Sunlight"); if (r != null) return r; }
                    return hub ? Demo(Concept.Sunlight) : null;
                case Intent.WherePhotosynthesis:
                case Intent.ShowChloroplast:
                case Intent.ShowChlorophyll:
                case Intent.ShowLeaf:
                case Intent.ShowCell:
                case Intent.ShowStem:
                    if (beat >= Beat.FreeExplore) { pick = intent == Intent.ShowCell ? "Cell" : intent == Intent.ShowStem ? "Stem" : intent == Intent.ShowChloroplast || intent == Intent.ShowChlorophyll ? "Chloroplast" : "Leaf"; return null; }
                    if (enterLeafWaiting && intent != Intent.ShowStem) { hud.HideButton(); return "Follow me!"; }   // presses ENTER LEAF
                    if (beat < Beat.EnterLeaf && intent != Intent.ShowCell && intent != Intent.ShowStem) StartCoroutine(BriefLeafHighlight());   // a tease, never the chloroplast reveal
                    return null;
                case Intent.ShowOxygen:
                    if (beat < Beat.Oxygen) return "Oxygen? Oh, that's a twist we'll reach soon. Keep going!";
                    if (o2Waiting && beat == Beat.Oxygen) { hud.HideButton(); return "Follow me!"; }   // presses FOLLOW THE OXYGEN
                    if (beat >= Beat.FreeExplore) { pick = "FollowO2"; return null; }
                    return hub ? Demo(Concept.Oxygen) : null;
                case Intent.ShowPhloem:
                case Intent.ShowGlucose:
                    if (beat < Beat.Glucose) return "Food comes at the end of the case — let's find all three clues first!";
                    if (beat >= Beat.FreeExplore) { pick = intent == Intent.ShowGlucose ? "FollowFood" : "Phloem"; return null; }
                    return hub ? Demo(Concept.Phloem) : null;
                case Intent.ShowEquation:
                    if (beat < Beat.Equation) return "The equation is our grand finale — we'll build it from everything we discover.";
                    return hub ? Demo(Concept.Equation) : null;
                case Intent.ExplainAgain:
                    if (!string.IsNullOrEmpty(lastLineId) && !dlg.Speaking) StartCoroutine(dlg.Say(lastLineId));
                    return null;
            }
            return null;
        }
    }
}
