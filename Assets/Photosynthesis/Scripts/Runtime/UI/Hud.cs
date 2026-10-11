using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using static Photosynthesis.Kit;
using static Photosynthesis.Palette;

namespace Photosynthesis
{
    public enum ButtonKind { Primary, Secondary, Gold, Ghost }
    public enum VoiceState { Idle, Listening, Transcribing, Thinking, Speaking }

    /// UI Toolkit implementation of the Figma sheet "Photosynthesis — Hidden Food Factory · UI System"
    /// (file suKcNjnLh8gigaytOygbBs). Layout: TL Current Investigation · R Progress · TR Menu/Reset/Help ·
    /// BL CC · BC Dr. Curio dialogue · BR Ask AI. Centre stays clear. All panels fade/slide/scale in.
    /// Built entirely in code at Awake; motion + interactive states in Resources/UI/Hud.uss.
    /// Partials: Hud.cs (core HUD, dialogue, scanner, callouts, info) · HudLearning.cs (clues, assessment, big moments,
    /// free explore, transitions) · HudSystem.cs (pause/settings/help/reset, Ask AI chat, debug).
    [DefaultExecutionOrder(1000)]   // LateUpdate after CameraDirector → callouts track this frame's camera (no 1-frame lag)
    public partial class Hud : MonoBehaviour
    {
        /// Settings ▸ Accessibility ▸ Reduce motion: no slides/pops/typewriter, short fades; camera: no shake, auto-turntable, ride lens/bank,
        /// speed-FOV or idle drift. Persisted (PlayerPrefs ps_reduce_motion, or the launch flag -reducemotion).
        public static bool ReduceMotion;
        /// Settings ▸ Audio ▸ Microphone (null = system default). Read by the Ask AI voice capture.
        public static string MicDevice;

        UIDocument doc; VisualElement root;
        VisualElement worldLayer, fadeEl, dimEl, spotEl, titleLayer, hudLayer, panelLayer, overlayLayer, chatLayer, loadingLayer, systemLayer, debugLayer;
        SparkleElement sparkles;
        readonly HashSet<VisualElement> shown = new HashSet<VisualElement>();
        Camera cam;
        float fadeAlpha, lineIdle;
        int transientFrame = -1;          // frame of the last HideTransient (cold start / debug jump): no unlock celebrations
        const float MinUiScale = 0.8f; Vector2Int uiScreen;
        const string Kbd = "ps-kbd";      // root class while the learner drives the HUD from the keyboard: shows :focus rings

        // ================================================================== build
        void Awake()
        {
            var go = new GameObject("HUD (UI Toolkit)");
            go.SetActive(false); go.transform.SetParent(transform, false);
            doc = go.AddComponent<UIDocument>();
            doc.panelSettings = Instantiate(PanelAsset()); FitUiScale();   // per-run copy: the UI-scale floor never dirties the asset
            go.SetActive(true);                                   // OnEnable with panelSettings set → rootVisualElement exists
            root = doc.rootVisualElement;
            Abs(root, 0, 0, 0, 0); root.pickingMode = PickingMode.Ignore;
            var uss = Resources.Load<StyleSheet>("UI/Hud");
            if (uss) root.styleSheets.Add(uss); else Debug.LogWarning("[Hud] Resources/UI/Hud.uss missing — interactive states disabled");
            Font(root, false); root.style.color = Text;
            ReduceMotion = PlayerPrefs.GetInt("ps_reduce_motion", 0) == 1 || Array.IndexOf(Environment.GetCommandLineArgs(), "-reducemotion") >= 0;   // shared school PCs: the comfort switch survives a relaunch (IT can force it on)
            root.EnableInClassList("ps-reduce", ReduceMotion);

            worldLayer = Layer(root);
            fadeEl = Layer(root); fadeEl.style.backgroundColor = Color.black; fadeEl.style.opacity = 0; fadeEl.style.display = DisplayStyle.None;
            dimEl = Layer(root); dimEl.style.backgroundColor = new Color(0.01f, 0.03f, 0.02f, 1f); dimEl.style.opacity = 0; dimEl.AddToClassList("ps-dim");   // scene dim (equation, title): HUD + title stay bright
            titleLayer = Layer(root); hudLayer = Layer(root);
            spotEl = Layer(root); spotEl.style.backgroundColor = new Color(0.01f, 0.03f, 0.02f, 1f); spotEl.style.opacity = 0; spotEl.AddToClassList("ps-dim");   // reward/feedback spotlight also recedes the corner HUD; dialogue + cards stay bright
            panelLayer = Layer(root); overlayLayer = Layer(root);
            sparkles = new SparkleElement(); root.Add(sparkles);   // unlock-art pop and progress-row pops stay on top of everything
            chatLayer = Layer(root); loadingLayer = Layer(root); systemLayer = Layer(root); debugLayer = Layer(root);

            BuildPersistent(); BuildDialogue(); BuildScanner(); BuildTitle(); BuildInfo();
            BuildLearning(); BuildSystem(); BuildChat(); BuildDebugPanel();
            root.RegisterCallback<GeometryChangedEvent>(_ => RelayoutBottom());   // the logical canvas changes with the UI-scale floor
            // edge-anchored panels enter from their own edge (Hud.uss .ps-from-*); centred cards keep the rise + 0.97 scale
            foreach (var e in new[] { progressPanel, infoCard }) e.AddToClassList("ps-from-right");
            foreach (var e in new[] { investigation, zoomCtl, qCard, reteachCard, legend, expPanel }) e.AddToClassList("ps-from-left");
            foreach (var e in new[] { topActions, magPill, zoomPathPill, clueRow, cmpPill, scanner }) e.AddToClassList("ps-from-top");
            chatPanel.AddToClassList("ps-from-orb");   // keeps the rise: the panel comes out of the orb, not from the right edge
            ApplySubtitles(); ApplyVoice();
        }

        /// ScaleWithScreenSize 1920x1080 @ match 0.5 gives 0.667 at 720p (Label 12 -> 8 px). Floor the scale at 0.8, but never
        /// let the logical canvas drop below 1600x900 (the layout's minimum) on either axis: 5:4 / 4:3 and very wide screens are capped too.
        void FitUiScale()
        {
            int w = Screen.width, h = Screen.height;
            if (!doc || !doc.panelSettings || (w == uiScreen.x && h == uiScreen.y) || w <= 0 || h <= 0) return;
            uiScreen = new Vector2Int(w, h);
            float s = Mathf.Lerp(w / 1920f, h / 1080f, 0.5f);   // PanelSettings.ResolveScale (match 0.5) lerps the size ratios linearly
            float t = Mathf.Min(Mathf.Max(s, MinUiScale), Mathf.Min(w / 1600f, h / 900f));   // 0.8 floor, but the logical canvas never drops below 1600x900 (5:4 would be 1586 wide, 32:9 would be 720 tall)
            // ref = screen / t makes both size ratios t, so the resolved scale is exactly t
            doc.panelSettings.referenceResolution = Mathf.Abs(t - s) > 0.001f ? new Vector2Int(Mathf.RoundToInt(w / t), Mathf.RoundToInt(h / t)) : new Vector2Int(1920, 1080);
        }

        static PanelSettings PanelAsset()
        {
            var ps = Resources.Load<PanelSettings>("PhotosynthesisPanel");
            if (ps) return ps;
            ps = ScriptableObject.CreateInstance<PanelSettings>();
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize; ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight; ps.match = 0.5f;
            ps.themeStyleSheet = Resources.Load<ThemeStyleSheet>("PhotosynthesisTheme");
            return ps;
        }

        void Start()
        {
            if (Module.I) Module.I.BeatChanged += OnBeat;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            Application.wantsToQuit += PrepareQuit;
#endif
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        bool quitPending, quitReady;
        bool PrepareQuit()
        {
            if (quitReady) return true;
            if (!quitPending) { quitPending = true; OnApplicationQuit(); StartCoroutine(FinishQuit()); }
            return false;
        }
        IEnumerator FinishQuit()
        {
            // Let Windows process provider-removal events before Unity destroys its native host window.
            yield return new WaitForSecondsRealtime(.1f);
            quitReady = true; Application.Quit();
        }
#endif

        void OnApplicationQuit()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            // Tear down the host provider while its window is still alive (Unity 6.3 Windows quit race).
            UnityEngine.Accessibility.AssistiveSupport.screenReaderStatusOverride = UnityEngine.Accessibility.AssistiveSupport.ScreenReaderStatusOverride.ForceDisabled;
            UnityEngine.Accessibility.AssistiveSupport.activeHierarchy = null;
#endif
            if (doc) doc.enabled = false;
        }

        void OnDestroy()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            Application.wantsToQuit -= PrepareQuit;
#endif
            if (Module.I) Module.I.BeatChanged -= OnBeat;
            if (sysStack.Count > 0) { Time.timeScale = savedTimeScale; AudioListener.pause = false; }
            if (doc && doc.panelSettings) Destroy(doc.panelSettings);    // the per-run copy made in Awake
        }

        void OnBeat(Beat b) { for (int i = 0; i < 3; i++) StyleClue(i); HighlightBeat(b); }

        // ================================================================== show / hide (USS .ps-anim + .ps-off)
        void Show(VisualElement e, bool on)
        {
            if (on)
            {
                if (!shown.Add(e)) return;
                e.style.display = DisplayStyle.Flex;
                e.schedule.Execute(() => { if (shown.Contains(e)) e.RemoveFromClassList(Off); }).StartingIn(16);
            }
            else
            {
                if (!shown.Remove(e)) return;
                e.AddToClassList(Off);
                e.schedule.Execute(() => { if (!shown.Contains(e)) e.style.display = DisplayStyle.None; }).StartingIn(ReduceMotion ? 120 : 320);
            }
        }

        bool IsShown(VisualElement e) => shown.Contains(e);

        enum DimSrc { Unlock, Feedback, Equation, Complete, Title }
        readonly float[] dims = new float[5];
        /// Figma dim alphas; GA maps them for linear blending (GA(0) = 0). Equation + title dim only the 3D (dimEl, under the
        /// title and HUD); unlock / feedback / complete are a spotlight that also recedes the corner HUD (spotEl, under the cards).
        void Dim(DimSrc s, float a)
        {
            dims[(int)s] = a;
            float scene = Mathf.Max(dims[(int)DimSrc.Equation], dims[(int)DimSrc.Title]);
            float spot = Mathf.Max(dims[(int)DimSrc.Unlock], Mathf.Max(dims[(int)DimSrc.Feedback], dims[(int)DimSrc.Complete]));
            dimEl.style.opacity = GA(scene); spotEl.style.opacity = GA(spot);
        }

        /// Focus once the element is displayed (Show removes .ps-off after 16 ms); never steals focus from the chat field.
        void FocusSoon(VisualElement e, long ms = 30) { if (e != null) e.schedule.Execute(() => { if (e == chatField ? chatOpen : !TypingInChat) e.Focus(); }).StartingIn(ms); }

        // ================================================================== input routing
        public bool PointerOverUI(Vector2 screenPos)
        {
            var panel = root?.panel; if (panel == null) return false;
            var p = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPos.x, Screen.height - screenPos.y));
            var picked = panel.Pick(p);
            for (var e = picked; e != null; e = e.parent)
            {
                if (e == root) return picked != root;                     // only our own panels count
                if (e.ClassListContains(Off)) return false;               // fading out: no longer blocks the 3D view
                if (cinematic && e.ClassListContains(CineHide)) return false;   // hidden for a cinematic shot
            }
            return false;
        }

        /// True while the learner is typing in the Ask Dr. Curio field (keyboard shortcuts such as Space should be ignored).
        public bool TypingInChat
        {
            get
            {
                var f = chatField?.panel?.focusController?.focusedElement as VisualElement;
                return f != null && (f == chatField || chatField.Contains(f));
            }
        }

        void Update()
        {
            var k = Keyboard.current; if (k == null) return;
            bool typing = TypingInChat;
            var mouse = Mouse.current;   // focus rings only while the keyboard drives (.ps-kbd)
            if (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)) root.RemoveFromClassList(Kbd);
            if (k.tabKey.wasPressedThisFrame || k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame || k.escapeKey.wasPressedThisFrame || k.upArrowKey.wasPressedThisFrame || k.downArrowKey.wasPressedThisFrame || k.leftArrowKey.wasPressedThisFrame || k.rightArrowKey.wasPressedThisFrame) root.AddToClassList(Kbd);
            if (k.escapeKey.wasPressedThisFrame)
            {
                if (typing) chatField.Blur();
                else if (popupOpen) ClosePopup();
                else if (sysStack.Count > 0) CloseSystem();
                else if (chatOpen) CloseChat();
                else TogglePause();
            }
            if (k.mKey.wasReleasedThisFrame) StopTalk();                 // always: M may be released after a menu opened
            if (typing || sysStack.Count > 0) return;
            if (k.cKey.wasPressedThisFrame) { Subtitles = !Subtitles; Play(Sfx.UiSelect, 0.6f); }
            if (k.mKey.wasPressedThisFrame && askOn) { OpenChat(false); StartTalk(); }
            if (chatOpen) return;
            // the lettered answer badges are real shortcuts: 1–4 pick A–D (Enter then submits from the focused Submit button)
            if (IsShown(qCard) && !qLocked)
                for (int i = 0; i < qOpts.Count && i < 4; i++)
                    if ((k[Key.Digit1 + i].wasPressedThisFrame || k[Key.Numpad1 + i].wasPressedThisFrame) && qOpts[i].pickingMode == PickingMode.Position)
                    { root.AddToClassList(Kbd); Play(Sfx.UiSelect, 0.8f); SelectOption(i); }
            if (IsShown(zoomCtl))
            {
                if (k.equalsKey.wasPressedThisFrame || k.numpadPlusKey.wasPressedThisFrame) ZoomButton?.Invoke(1f);
                if (k.minusKey.wasPressedThisFrame || k.numpadMinusKey.wasPressedThisFrame) ZoomButton?.Invoke(-1f);
            }
        }

        void LateUpdate()
        {
            FitUiScale();                                                // no-op unless the window size changed
            float t = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            UpdateCallouts(t);
            UpdateTypewriter(Time.deltaTime);                           // scaled: the line pauses with the game (and its voice)
            UpdateScanner(t, dt);
            UpdatePersistent(t);
            UpdateLearning(t);
            UpdateChat(t);
            sparkles.Tick(dt); fbFx.Tick(dt); completeFx.Tick(dt);
        }

        Camera Cam()
        {
            if (cam) return cam;
            var m = Module.I;
            cam = m && m.cam ? m.cam.Cam : null;
            if (!cam) cam = Camera.main;                                 // only while nothing is cached
            return cam;
        }

        // ================================================================== persistent HUD
        VisualElement investigation, invRing, topActions, progressPanel, ccToggle, ccBox, zoomCtl, orbCluster, orb, orbTag;
        IconElement invIcon, ccIcon, orbTagIcon; Label invTitle, invSub, ccLabel, orbTagLabel; GlowElement orbGlow, invGlow;
        ArtElement orbAvatar, dlgAvatar;
        bool hudOn, progressOn, askOn, zoomOn, orbHover, orbPointer;
        float invFlash = -10f;

        void BuildPersistent()
        {
            // TL — Current investigation
            investigation = Hidden(Abs(Panel(), 40, 36));
            Pad(investigation, 14, 22, 14, 14); investigation.style.maxWidth = 600;
            var row = new FlexBox(true, 14); investigation.Add(row);
            invRing = row.Put(Circle(48, A(GreenBright, 0.16f), A(GreenBright, 0.8f), 1.5f, invIcon = Icon(IconId.Sparkle, 24, GreenBright)));
            invGlow = Glow(invRing, GreenBright, 24, 16, 0.7f); invGlow.style.opacity = 0;
            var col = row.Put(new FlexBox(false, 2)); col.style.flexShrink = 1;
            col.Put(Txt("Current investigation", TS.Label, TextSecondary));
            invTitle = col.Put(Txt("", TS.H3, Text));
            invSub = col.Put(WrapText(Txt("", TS.Small, TextSecondary))); invSub.style.maxWidth = 460;
            hudLayer.Add(investigation);

            // TR — Menu · Reset · Help
            topActions = Hidden(Abs(new FlexBox(true, 8) { pickingMode = PickingMode.Ignore }, float.NaN, 32, 40));
            ((FlexBox)topActions).Put(Tile(IconId.Menu, "Menu", TogglePause));
            ((FlexBox)topActions).Put(Tile(IconId.Reset, "Reset", () => OpenSystem(resetPanel)));
            ((FlexBox)topActions).Put(Tile(IconId.Help, "Help", () => OpenSystem(helpPanel)));
            hudLayer.Add(topActions);

            // R — Investigation progress
            progressPanel = Hidden(Abs(Panel(), float.NaN, 140, 40));
            Pad(progressPanel, 16, 16, 16, 16);
            var pc = new FlexBox(false, 8); progressPanel.Add(pc);
            pc.Put(Txt("Investigation progress", TS.Label, TextSecondary));
            for (int i = 0; i < 3; i++) { items[i] = MakeProg((ClueId)i, true); pc.Put(items[i].root); }
            hudLayer.Add(progressPanel);

            // BL — CC toggle
            ccToggle = Hidden(Abs(new VisualElement(), 40, float.NaN, float.NaN, 40));
            ccToggle.AddToClassList("ps-cc");   // fill + stroke in Hud.uss (glass @80 → GA 0.97), so :hover / :active / :focus apply
            ccToggle.style.flexDirection = FlexDirection.Row; ccToggle.style.alignItems = Align.Center;
            Radius(ccToggle, 12); Pad(ccToggle, 8, 12, 8, 8);
            Glow(ccToggle, Cyan, 12, 14, 0.5f).AddToClassList("ps-hoverglow");
            ccBox = Circle(32, A(Green, 0.9f), Color.clear, 0, ccIcon = Icon(IconId.CC, 20, Text)); Radius(ccBox, 8);
            ccToggle.Add(ccBox);
            ccLabel = Txt("Subtitles On", TS.Caption, Text); ccLabel.style.marginLeft = 10; ccToggle.Add(ccLabel);
            Click(ccToggle, () => Subtitles = !Subtitles, Sfx.UiSelect);
            hudLayer.Add(ccToggle);

            // L — zoom control
            zoomCtl = Hidden(Abs(Panel(16), 40, 420));
            Pad(zoomCtl, 12, 10, 12, 10);
            var zc = new FlexBox(false, 10); zc.style.alignItems = Align.Center; zoomCtl.Add(zc);
            zc.Put(Icon(IconId.Zoom, 22, TextSecondary));
            zc.Put(Round(48, IconId.Plus, 22, Text, () => ZoomButton?.Invoke(1f)));   // ≥ 44 px: the only zoom on a tablet (no pinch)
            zc.Put(Round(48, IconId.Minus, 22, Text, () => ZoomButton?.Invoke(-1f)));
            zc.Put(Txt("Zoom", TS.Caption, TextMuted));
            hudLayer.Add(zoomCtl);
            foreach (var e in new[] { investigation, progressPanel, ccToggle, zoomCtl }) e.AddToClassList(CineHide);
            topActions.AddToClassList(CineDim);

            // BR — Ask AI orb (Dr. Curio avatar, state ring, tag)
            orbCluster = Hidden(Abs(new VisualElement(), float.NaN, float.NaN, 40, 40));
            orbCluster.style.alignItems = Align.Center;
            orb = Circle(88, AG(Glass, 0.92f), Cyan, 3);
            orbGlow = Glow(orb, Cyan, 44, 22, 0.6f);
            orb.Add(orbAvatar = new ArtElement(ArtId.Curio, 70));
            orbCluster.Add(orb);
            orbTag = new VisualElement(); orbTag.style.flexDirection = FlexDirection.Row; orbTag.style.alignItems = Align.Center; orbTag.style.marginTop = -14;
            Pad(orbTag, 6, 14); Radius(orbTag, 999); Bg(orbTag, A(GreenDeep, 0.95f)); Border(orbTag, A(Cyan, 0.9f), 1.5f);
            orbTag.Add(orbTagIcon = Icon(IconId.Mic, 14, Cyan));
            orbTag.Add(orbTagLabel = Txt("Ask AI", TS.Label, Text));
            orbCluster.Add(orbTag);
            Click(orbCluster, () => { if (chatOpen) CloseChat(); else OpenChat(); }, Sfx.UiSelect);
            orbCluster.RegisterCallback<PointerEnterEvent>(_ => { orbPointer = orbHover = true; ApplyVoice(); });
            orbCluster.RegisterCallback<PointerLeaveEvent>(_ => { orbPointer = orbHover = false; ApplyVoice(); });
            orbCluster.RegisterCallback<FocusInEvent>(_ => { orbHover = orbPointer || root.ClassListContains(Kbd); ApplyVoice(); });   // keyboard focus ring = hover ring
            orbCluster.RegisterCallback<FocusOutEvent>(_ => { orbHover = orbPointer; ApplyVoice(); });
            orbCluster.AddToClassList(CineDim);
            orbCluster.AddToClassList("ps-orb");   // press scale on the cluster: `orb` gets an inline breathing scale every frame
            hudLayer.Add(orbCluster);
        }

        void RefreshPersistent()
        {
            Show(investigation, hudOn); Show(topActions, hudOn); Show(ccToggle, hudOn);
            // an equation shifted beside the reteach card (or the 984-wide row on a narrow canvas) needs the right side
            Show(progressPanel, progressOn && !chatOpen && !qSpoils && !infoInSlot && !(IsShown(eqLayer) && (eqBeside || root.layout.width < 1700f)));   // qSpoils: the tracker row would answer the open card; infoInSlot: an info card stands in its place
            // a card in the tracker's slot steps aside for the chat as the tracker does (shifted beside the chat it would cover the stoma's
            // labels); other cards move left of the chat. Set here, in the same refresh, so a card never slides while it fades
            if (infoInSlot) Show(infoCard, !chatOpen);
            infoWrap.style.right = chatOpen && !infoInSlot ? 40 + 460 + 20 : 40;
            Show(orbCluster, askOn);
            RefreshZoom();
            PlaceScanner();
        }

        // + / − only move an orbiting, learner-driven camera: hidden during cuts, blends, rides and locked framings.
        // Exception: the 1× "zoom into the leaf" prompt, where + is an answer even on a held camera.
        static bool CamLive() { var cd = Module.I ? Module.I.cam : null; return cd && cd.Orbiting && cd.UserControl; }
        bool ZoomAsked() => (magPrompt && IsShown(magPill)) || (IsShown(instrPanel) && instrIcon.Id == IconId.Zoom);   // any "scroll or press +" prompt
        void RefreshZoom() => Show(zoomCtl, zoomOn && (CamLive() || ZoomAsked()) && !IsShown(legend) && !IsShown(reteachCard) && !IsShown(expPanel) && !IsShown(qCard));

        const string CineHide = "ps-cine-hide", CineDim = "ps-cine-dim";
        bool cinematic; int cineGen;
        public bool Cinematic => cinematic;

        /// Cinematic shot: investigation, progress, zoom, CC and the magnification / zoom-path pills fade out (0.4 s) and stop
        /// taking clicks; Menu/Reset/Help and ASK AI recede to 80% / 0.92 scale, full on hover or while Ask AI is live, and stay
        /// clickable. Dialogue, instruction, buttons, callouts, scanner and the question/reteach/feedback cards are untouched. Last call wins.
        public void SetCinematic(bool on)
        {
            if (cinematic == on) return;
            cinematic = on; int my = ++cineGen;
            root.EnableInClassList("ps-cine", on);
            PlaceScanner();
            if (!on) { root.Query(className: CineHide).ForEach(e => e.style.visibility = StyleKeyword.Null); SchedulePop(); }
            else root.schedule.Execute(() => { if (my == cineGen) root.Query(className: CineHide).ForEach(e => e.style.visibility = Visibility.Hidden); }).StartingIn(400);
        }

        public void SetHudVisible(bool on) { hudOn = on; RefreshPersistent(); }
        public void SetProgressVisible(bool on) { progressOn = on; RefreshPersistent(); }
        public void SetAskAIVisible(bool on) { askOn = on; if (!on && chatOpen) CloseChat(); RefreshPersistent(); }
        public void SetZoomControlVisible(bool on) { zoomOn = on; RefreshPersistent(); }
        public event Action<float> ZoomButton;   // +1 / -1

        string invTitleSrc, scanDetailSrc;   // raw text last set (the labels hold uppercased / Chem-tagged copies)

        public void SetInvestigation(string title, string subtitle, IconId icon)
        {
            if (invTitleSrc != title) { invTitleSrc = title; invFlash = Time.unscaledTime; }
            Set(invTitle, title); Set(invSub, subtitle); invSub.style.display = string.IsNullOrEmpty(subtitle) ? DisplayStyle.None : DisplayStyle.Flex;
            invIcon.Id = icon;
        }

        void UpdatePersistent(float t)
        {
            RefreshZoom();                                               // follows the camera; Show() is a no-op when unchanged
            // Ask AI orb: idle breathing ring; voice states pulse faster
            if (IsShown(orbCluster))
            {
                orbCluster.EnableInClassList("ps-cine-awake", chatOpen || voice != VoiceState.Idle);   // a live Ask AI never sits recessed
                bool still = ReduceMotion || (cinematic && voice == VoiceState.Idle);                      // calm, steady orb under a cinematic
                float speed = voice == VoiceState.Idle ? 1.6f : 4.5f, b = 0.5f + 0.5f * Mathf.Sin(t * speed);
                orbGlow.style.opacity = still ? 0.8f : 0.45f + 0.55f * b;
                orb.style.scale = new Scale(Vector2.one * (still ? 1f : 1f + 0.025f * b));
            }
            bool blink = Mathf.Repeat(t, 4.3f) < 0.13f;
            orbAvatar.Blink = blink; dlgAvatar.Blink = blink; chatAvatar.Blink = blink;
            // new investigation title → ring flash
            float f = Mathf.Clamp01(1f - (t - invFlash) / 1.2f);
            invGlow.style.opacity = f;
            // Dr. Curio's dialogue avatar glows with his voice
            if (IsShown(dlgPanel))
            {
                var m = Module.I; float lvl = m && m.audioDirector ? m.audioDirector.VoiceLevel : 0f;
                dlgAvatarGlow.style.opacity = 0.25f + Mathf.Clamp01(lvl * 1.5f) * 0.75f;
                // A finished line lingers for reading, then clears so stale subtitles never sit under rides / questions.
                bool speaking = m && m.dialogue && m.dialogue.Speaking;
                // Speaking: the chevron skips. 0.5 s after the line ends it turns into Replay, so a double-click skip never re-says the line.
                bool replay = !speaking && lineIdle > 0.5f;
                if (dlgNext.enabledSelf != (speaking || replay)) dlgNext.SetEnabled(speaking || replay);
                dlgNextIcon.Id = replay ? IconId.Replay : IconId.ChevronRight;   // the Id setter does nothing if unchanged
                lineIdle = speaking ? 0f : lineIdle + Time.deltaTime;
                if (lineIdle > (IsShown(exploreBar) ? 2.5f : LineLinger)) HideLine();   // Free Explore: the line clears quickly so the bar settles to its Figma-20 slot (bottom 110)
            }
            if (IsShown(instrPanel) && !ReduceMotion) instrRing.style.scale = new Scale(Vector2.one * (1f + 0.05f * Mathf.Sin(t * 3.2f)));
            if (btnPulse != null) btnPulse.style.opacity = 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Sin(t * 3f));
        }

        // ================================================================== dialogue / instructions
        VisualElement dlgPanel, dlgNext, instrPanel, instrRing, instrRow, btnRow, btnHolder, exploreRow;
        Label dlgSpeaker, dlgLine, dlgSubsOff, instrText; IconElement instrIcon, dlgNextIcon; GlowElement dlgAvatarGlow, btnPulse;
        string lineFull = ""; int lineShown; float lineAcc; bool lineTyping, subtitles = true, largeSubs;
        string instrRaw, btnLabel;   // what the instruction pill / call-to-action ask for, as given (CurrentTask)
        const string Invisible = "<alpha=#00>";
        const float LineLinger = 6f;

        void BuildDialogue()
        {
            dlgPanel = Hidden(Panel(20)); Size(dlgPanel, 780); Pad(dlgPanel, 14, 18, 14, 14);
            var row = new FlexBox(true, 16); dlgPanel.Add(row);
            var av = Circle(68, Raised, A(Cyan, 0.9f), 2, dlgAvatar = new ArtElement(ArtId.Curio, 60));
            dlgAvatarGlow = Glow(av, Cyan, 34, 16, 0.6f);
            row.Put(av);
            var col = row.Put(new FlexBox(false, 4)); col.style.flexGrow = 1; col.style.flexShrink = 1;
            dlgSpeaker = col.Put(Txt("Dr. Curio", TS.H3, Cyan));
            dlgLine = col.Put(Txt("", TS.Body, Text, 580));
            dlgSubsOff = col.Put(Txt("Subtitles off · tap CC to show them", TS.Small, TextMuted));
            dlgNext = row.Put(Round(48, IconId.ChevronRight, 22, Cyan, () => { if (dlgNextIcon.Id == IconId.Replay) ReplayClicked?.Invoke(); else NextClicked?.Invoke(); }, "ps-round--cyan"));
            dlgNextIcon = dlgNext.Q<IconElement>();
            CenterRow(panelLayer, float.NaN, 40).Add(dlgPanel);
            dlgPanel.RegisterCallback<GeometryChangedEvent>(_ => RelayoutBottom());

            instrPanel = Hidden(Panel(16, 0.84f, A(Cyan, 0.45f))); Size(instrPanel, 560); Pad(instrPanel, 12, 16, 12, 12);
            var ir = new FlexBox(true, 14); instrPanel.Add(ir);
            instrRing = ir.Put(Circle(44, A(Cyan, 0.16f), A(Cyan, 0.9f), 1.5f, instrIcon = Icon(IconId.Target, 22, Cyan)));
            instrText = ir.Put(Txt("", TS.BodyStrong, Text, 456));                       // no trailing chevron: a prompt, not a button
            instrPanel.pickingMode = PickingMode.Ignore; IgnoreChildren(instrPanel);   // clicks/drags fall through to the 3D task it describes
            instrRow = CenterRow(panelLayer, float.NaN, 40); instrRow.AddToClassList("ps-slot"); instrRow.Add(instrPanel);
            instrPanel.RegisterCallback<GeometryChangedEvent>(_ => RelayoutBottom());

            btnHolder = Hidden(new VisualElement { pickingMode = PickingMode.Ignore });
            btnRow = CenterRow(panelLayer, float.NaN, 40); btnRow.AddToClassList("ps-slot"); btnRow.Add(btnHolder);
            btnHolder.RegisterCallback<GeometryChangedEvent>(_ => RelayoutBottom());

            exploreRow = CenterRow(panelLayer, float.NaN, 110); exploreRow.AddToClassList("ps-slot");
        }

        /// Bottom-centre stack: dialogue at 40 px, then instruction, call-to-action and Free Explore bar above it.
        void RelayoutBottom()
        {
            float y = 40;
            if (IsShown(dlgPanel)) y += H(dlgPanel, 96) + 16;
            instrRow.style.bottom = y; if (IsShown(instrPanel)) y += H(instrPanel, 68) + 16;
            btnRow.style.bottom = y; if (IsShown(btnHolder)) y += H(btnHolder, 48) + 16;
            if (glucoseSlot != null) glucoseSlot.style.bottom = y;          // FOOD MADE pill + uses cards ride on top of the stack
            exploreRow.style.bottom = Mathf.Max(y, 110);
            bool echoed = subtitles && IsShown(dlgPanel) && lineFull == Prose(InOutCap);   // s11_doorway's subtitle already shows these words: never caption + subtitle at once
            Show(inOutCaption, inOutOn && inOutCapOn && !echoed && !IsShown(instrPanel) && !IsShown(btnHolder));   // the caption never stacks with a prompt (its VO line already says it)
            float free = root.layout.height - y;                             // y = top of the bottom stack + 16
            if (inOutCapRow != null && !float.IsNaN(free)) inOutCapRow.style.top = Mathf.Min(786f, free - H(inOutCapRow, 64));   // a 900-high canvas lifts it off the dialogue
        }
        static float H(VisualElement e, float fallback) { float h = e.layout.height; return float.IsNaN(h) || h < 1 ? fallback : h; }

        public void ShowLine(string speaker, string text)
        {
            Set(dlgSpeaker, string.IsNullOrEmpty(speaker) ? "Dr. Curio" : speaker);
            lineFull = Prose(text ?? "");                                  // raw (Chem tags are added on display), so the '<' guard still holds
            lineTyping = !ReduceMotion && lineFull.Length > 0 && lineFull.IndexOf('<') < 0;
            lineShown = 0; lineAcc = 0; lineIdle = 0;
            dlgLine.text = lineTyping ? Invisible + Chem(lineFull) : Chem(lineFull);   // hidden tail keeps the final layout from frame one
            Show(dlgPanel, true); RelayoutBottom();
        }

        public void HideLine() { Show(dlgPanel, false); lineTyping = false; RelayoutBottom(); }
        /// Cut / beat change: a finished line clears in this frame's LateUpdate unless a new ShowLine replaces it first (ShowLine zeroes lineIdle).
        /// A line still being spoken is untouched: UpdatePersistent resets lineIdle while dialogue.Speaking, so its normal linger applies.
        public void ExpireLine() => lineIdle = 1e6f;
        public event Action NextClicked, ReplayClicked;

        void UpdateTypewriter(float dt)
        {
            if (!lineTyping) return;
            lineAcc += dt * 60f;                                         // ≈60 characters per second
            int n = Mathf.Min(lineFull.Length, (int)lineAcc);
            if (n == lineShown) return;
            lineShown = n;
            if (n >= lineFull.Length) { dlgLine.text = Chem(lineFull); lineTyping = false; }
            // ponytail: one string per revealed character while a line types (~2 s); a TextCore visible-character API would remove it.
            // Split the raw text first, then tag each half, so a Chem tag is never cut in half.
            else dlgLine.text = Chem(lineFull.Substring(0, n)) + Invisible + Chem(lineFull.Substring(n));
        }

        public bool Subtitles { get => subtitles; set { subtitles = value; ApplySubtitles(); } }

        void ApplySubtitles()
        {
            if (dlgLine == null) return;
            dlgLine.style.display = subtitles ? DisplayStyle.Flex : DisplayStyle.None;
            dlgSubsOff.style.display = subtitles ? DisplayStyle.None : DisplayStyle.Flex;
            dlgLine.style.fontSize = largeSubs ? 22 : 18;                 // subtitles are the main teaching channel: ≈69 chars/line at 18
            ccBox.style.backgroundColor = subtitles ? A(Green, 0.9f) : A(Palette.Locked, 0.4f);
            ccIcon.Color = subtitles ? Text : TextMuted;
            ccLabel.text = subtitles ? "Subtitles On" : "Subtitles Off"; ccLabel.style.color = subtitles ? Text : TextMuted;
            if (subsToggle != null) subsToggle.Value = subtitles;
        }

        public void ShowInstruction(string text, IconId icon = IconId.Target)
        {
            instrRaw = text;
            Set(instrText, Prose(text)); instrIcon.Id = icon;
            Show(instrPanel, true); RelayoutBottom();
        }

        public void HideInstruction() { Show(instrPanel, false); instrRing.style.scale = StyleKeyword.Null; RelayoutBottom(); }

        // ================================================================== scanner
        VisualElement scanner, scanRing, scanTrack, scanFill, scanShimmer, scanHome, scanDock; IconElement scanIcon; Label scanReadout, scanDetail; GlowElement scanGlow;
        float scanTarget, scanCur; ScanState scanState; string scanText;

        void BuildScanner()
        {
            scanner = Hidden(new VisualElement()); Size(scanner, 460); Pad(scanner, 14, 18, 14, 14);
            Bg(scanner, AG(Sunken, 0.88f)); Radius(scanner, 16); Border(scanner, A(Cyan, 0.8f), 1.5f);
            scanGlow = Glow(scanner, Cyan, 16, 20, 0.5f);
            var row = new FlexBox(true, 14); scanner.Add(row);
            scanRing = row.Put(Circle(48, A(Cyan, 0.14f), A(Cyan, 0.9f), 1.5f, scanIcon = Icon(IconId.Scan, 24, Cyan)));
            var col = row.Put(new FlexBox(false, 6)); col.style.flexGrow = 1; col.style.flexShrink = 1;
            col.Put(Txt("Dr. Curio's scanner", TS.Label, TextSecondary));
            scanReadout = col.Put(Txt("", TS.Readout, Cyan, 360));
            scanDetail = col.Put(Txt("", TS.Small, TextSecondary, 360));
            scanTrack = col.Put(Bar(360, Cyan, out scanFill));
            scanShimmer = Abs(new VisualElement { pickingMode = PickingMode.Ignore }, 0, 0); Size(scanShimmer, 56, 6); Radius(scanShimmer, 3);
            Bg(scanShimmer, new Color(1, 1, 1, 0.55f)); scanTrack.Add(scanShimmer);
            scanHome = CenterRow(panelLayer, 150);
            scanDock = Abs(new VisualElement { pickingMode = PickingMode.Ignore }, float.NaN, 140, 40); panelLayer.Add(scanDock);
            scanHome.Add(scanner);
        }

        /// Right column (under progress, or in its slot when progress is hidden), as in Figma 03/06, so the readout never sits on the
        /// plant or beam it reports. Falls back to top-centre when the right column is taken (Ask AI chat, or an info card under visible progress).
        void PlaceScanner()
        {
            if (scanner == null) return;
            bool prog = IsShown(progressPanel) && !cinematic;
            float y = 140f;
            if (prog) { float b = progressPanel.layout.yMax; y = (float.IsNaN(b) || b < 200f ? 362f : b) + 16f; }   // ≈378
            else if (IsShown(exitExploreBtn)) y = 184f;
            bool dock = !chatOpen && !eqBeside && !(prog && IsShown(infoCard));
            var host = dock ? scanDock : scanHome;
            if (dock) scanDock.style.top = y;
            if (scanner.parent != host) host.Add(scanner);   // re-parent: no cross-screen translate glide; .ps-off slide-in kept
            // centre fallback stays clear of the question card (x 120–740): 150 at 1920, 186 on a 1600 canvas
            float w = root.layout.width; if (float.IsNaN(w)) w = 1920f;
            if (!dock && IsShown(qCard)) scanner.style.translate = new Translate(Mathf.Max(150f, 756f - (w * 0.5f - 230f)), 0);
            else scanner.style.translate = StyleKeyword.Null;
        }

        public void ShowScanner(ScanState state, string readout, string detail, float progress = -1f)
        {
            Color c = state == ScanState.Scanning ? Cyan : state == ScanState.Detected ? Gold : Correct;
            if (state != scanState || !IsShown(scanner))
            {
                BorderColor(scanner, A(c, 0.8f)); scanGlow.Color = A(c, 0.5f);
                scanRing.style.backgroundColor = A(c, 0.14f); BorderColor(scanRing, A(c, 0.9f)); scanIcon.Color = c;
                scanReadout.style.color = c; scanFill.style.backgroundColor = c;
            }
            if (scanText != readout) { scanText = readout; Set(scanReadout, readout); }
            if (scanDetailSrc != detail) { scanDetailSrc = detail; Set(scanDetail, detail ?? ""); }
            scanDetail.style.display = string.IsNullOrEmpty(detail) ? DisplayStyle.None : DisplayStyle.Flex;
            scanTrack.style.display = progress < 0 ? DisplayStyle.None : DisplayStyle.Flex;
            scanTarget = Mathf.Clamp01(progress);
            if (!IsShown(scanner)) { scanCur = scanTarget; scanFill.style.width = 360 * scanCur; }
            scanState = state;
            if (!IsShown(scanner)) PlaceScanner();
            Show(scanner, true);
        }

        public void HideScanner() { Show(scanner, false); }

        void UpdateScanner(float t, float dt)
        {
            if (!IsShown(scanner)) return;
            if (Mathf.Abs(scanCur - scanTarget) > 0.001f)
            {
                scanCur = Mathf.MoveTowards(scanCur, scanTarget, dt * 1.5f);
                scanFill.style.width = 360 * scanCur;
            }
            bool sweep = scanState == ScanState.Scanning && !ReduceMotion;
            scanShimmer.style.display = sweep ? DisplayStyle.Flex : DisplayStyle.None;
            if (sweep) scanShimmer.style.translate = new Translate(Mathf.Repeat(t * 0.7f, 1f) * 416f - 56f, 0);
        }

        // ================================================================== magnification
        VisualElement magPill; readonly VisualElement[] magSegs = new VisualElement[3]; readonly Label[] magLabels = new Label[3]; bool magPrompt;

        /// 0 = 1×, 1 = 10×, 2 = 100×
        public void ShowMagnification(int level)
        {
            magPrompt = level == 0;
            for (int i = 0; i < 3; i++)
            {
                magSegs[i].EnableInClassList("ps-seg--on", i == level);
                magLabels[i].style.color = i == level ? Cyan : TextSecondary;
                magSegs[i].pickingMode = level == 0 && i > 0 ? PickingMode.Position : PickingMode.Ignore;   // 10×/100× zoom in only at the 1× prompt
            }
            PlaceMag(); Show(magPill, true);
        }

        public void HideMagnification() { Show(magPill, false); }

        // ================================================================== world callouts
        class CalloutUi { public string key; public VisualElement root, dot, ring, line, tag; public Label title, sub; public GlowElement glow; public Func<Vector3> anchor; public bool flip; public bool? side; public float born; }
        readonly List<CalloutUi> callouts = new List<CalloutUi>();
        readonly Stack<CalloutUi> calloutPool = new Stack<CalloutUi>();

        CalloutUi MakeCallout()
        {
            var c = new CalloutUi();
            c.root = Abs(new VisualElement(), 0, 0); c.root.style.flexDirection = FlexDirection.Row; c.root.style.alignItems = Align.Center;
            c.root.AddToClassList("ps-fade"); c.root.AddToClassList(Off); c.root.style.display = DisplayStyle.None;
            c.dot = Circle(12, Cyan, Cyan, 0);
            c.ring = Abs(new VisualElement(), -6, -6); Size(c.ring, 24, 24); Radius(c.ring, 12); Border(c.ring, Cyan, 1.5f); c.dot.Add(c.ring);
            c.line = new VisualElement(); Size(c.line, 48, 2); c.line.AddToClassList("ps-leader");   // the leader draws out from the dot (USS width transition)
            c.tag = new VisualElement(); Pad(c.tag, 8, 14); Radius(c.tag, 12); Bg(c.tag, AG(Sunken, 0.85f)); Border(c.tag, Cyan, 1.5f);
            c.glow = Glow(c.tag, Cyan, 12, 14, 0.35f);
            c.tag.Add(c.title = Txt("", TS.Readout, Cyan));
            c.tag.Add(c.sub = Txt("", TS.Small, TextSecondary)); c.sub.style.marginTop = 2;
            c.root.Add(c.dot); c.root.Add(c.line); c.root.Add(c.tag);
            c.root.pickingMode = PickingMode.Ignore; IgnoreChildren(c.root);
            worldLayer.Add(c.root);
            return c;
        }

        /// Screen-space label anchored to a moving world point (leader line + tag). Key replaces existing.
        /// left: force the tag to the anchor's left (true) or right (false); null = the usual edge-aware choice.
        public void Callout(string key, string title, string sub, Tone tone, Func<Vector3> worldAnchor, bool? left = null)
        {
            CalloutUi c = null;
            foreach (var x in callouts) if (x.key == key) { c = x; break; }
            if (c == null) { c = calloutPool.Count > 0 ? calloutPool.Pop() : MakeCallout(); c.key = key; callouts.Add(c); c.born = Time.unscaledTime; c.line.style.width = 0f; }   // new tag: the leader draws out once displayed
            c.line.EnableInClassList("ps-leader", key != "glu");   // the glucose leader is re-measured every frame: no easing there
            var col = Palette.Of(tone);
            c.anchor = worldAnchor; c.side = left;
            Set(c.title, title); c.title.style.color = col;
            Set(c.sub, sub); c.sub.style.display = string.IsNullOrEmpty(sub) ? DisplayStyle.None : DisplayStyle.Flex;
            c.dot.style.backgroundColor = col; BorderColor(c.ring, col); c.line.style.backgroundColor = A(col, 0.8f);
            BorderColor(c.tag, A(col, 0.9f)); c.glow.Color = A(col, 0.35f);
            c.root.style.visibility = Visibility.Hidden;                 // revealed once positioned
            Show(c.root, true);
        }

        public void HideCallout(string key)
        {
            for (int i = callouts.Count - 1; i >= 0; i--) if (callouts[i].key == key) RemoveCallout(i);
        }

        public void ClearCallouts() { for (int i = callouts.Count - 1; i >= 0; i--) RemoveCallout(i); }

        void RemoveCallout(int i)
        {
            var c = callouts[i]; callouts.RemoveAt(i);
            c.anchor = null; c.key = null; Show(c.root, false); calloutPool.Push(c);
        }

        void UpdateCallouts(float t)
        {
            if (callouts.Count == 0) return;
            var camera = Cam(); var panel = root.panel;
            if (!camera || panel == null) return;
            float w = root.layout.width, h = root.layout.height;
            for (int i = callouts.Count - 1; i >= 0; i--)
            {
                var c = callouts[i]; Vector3 world;
                try { world = c.anchor != null ? c.anchor() : Vector3.zero; }
                catch (Exception) { RemoveCallout(i); continue; }          // anchor object destroyed
                var sp = camera.WorldToScreenPoint(world);
                bool vis = sp.z > 0.05f;
                Vector2 p = default;
                if (vis)
                {
                    p = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(sp.x, Screen.height - sp.y));
                    vis = p.x > -60 && p.x < w + 60 && p.y > -60 && p.y < h + 60;
                }
                c.root.style.visibility = vis ? Visibility.Visible : Visibility.Hidden;
                if (!vis) continue;
                if (c.key == "glu")
                {
                    // Keep the formula outside the ball-and-stick model during the glucose close-up.
                    var edge = camera.WorldToScreenPoint(world + camera.transform.right * 1.5f);
                    var ep = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(edge.x, Screen.height - edge.y));
                    c.line.style.width = Mathf.Clamp(Vector2.Distance(p, ep) + 16f, 48f, 330f);
                }
                else if (t - c.born > 0.05f) c.line.style.width = 48f;   // a few frames after display, so the .ps-leader transition draws it from 0
                bool flip = c.side ?? (c.flip ? p.x > w * 0.56f : p.x > w * 0.64f);   // tag points inward near the right edge, unless a side was asked for
                if (flip != c.flip) { c.flip = flip; c.root.style.flexDirection = flip ? FlexDirection.RowReverse : FlexDirection.Row; }
                float cw = c.root.layout.width, ch = c.root.layout.height;
                if (float.IsNaN(cw)) { cw = 0; ch = 0; }
                c.root.style.translate = new Translate(flip ? p.x - cw + 6 : p.x - 6, p.y - ch * 0.5f);
                if (!ReduceMotion)
                {
                    float f = Mathf.Repeat(t * 0.8f + i * 0.31f, 1f);
                    c.ring.style.scale = new Scale(Vector2.one * (0.6f + f)); c.ring.style.opacity = 1f - f;
                }
            }
        }

        // ================================================================== title
        VisualElement titleBox, titleRule; Label titleBig, titleSmall; int titleGen;

        void BuildTitle()
        {
            titleBox = new FlexBox(false, 14) { pickingMode = PickingMode.Ignore };
            titleBox.style.alignItems = Align.Center; titleBox.style.opacity = 0; titleBox.style.display = DisplayStyle.None;
            // known (b): a dark plate makes the hero type part of the glass system, not a decal on the scene. minWidth keeps it put
            // while the letter-spacing tween runs; the glow starts at the plate edge at the fill alpha, so it reads as a soft vignette.
            Pad(titleBox, 22, 72); Radius(titleBox, 48); titleBox.style.minWidth = 880;   // ≥ 'PHOTOSYNTHESIS' at the 18 px start of the letter-spacing tween: the plate never shrinks
            Bg(titleBox, A(Scene, 0.85f));                       // linear blending: 0.72 still lets the FinalPlant flare through
            Glow(titleBox, Scene, 48, 96, 0.85f);                // fades with titleBox.opacity
            titleBig = ((FlexBox)titleBox).Put(Txt("", TS.Display, Gold));
            titleBig.style.textShadow = new TextShadow { offset = new Vector2(0, 2), blurRadius = 14, color = new Color(0, 0, 0, 0.7f) };   // serif on a plate: no gold halo
            titleRule = ((FlexBox)titleBox).Put(new VisualElement { pickingMode = PickingMode.Ignore }); Size(titleRule, 160, 1); Bg(titleRule, A(Gold, 0.6f));
            titleSmall = ((FlexBox)titleBox).Put(Txt("", TS.Body, TextSecondary));
            titleSmall.style.textShadow = new TextShadow { offset = new Vector2(0, 1), blurRadius = 6, color = new Color(0, 0, 0, 0.7f) };
            CenterRow(titleLayer, 388).Add(titleBox);           // 410 − 22 padding keeps the glyphs where they were
        }

        /// Centred display title (e.g. "A New World", "Photosynthesis"). Fades in, holds, fades out.
        public IEnumerator Title(string big, string small, float hold, float top = 388f, float dim = 0.3f)
        {
            int my = ++titleGen;
            Set(titleBig, big); titleSmall.text = small;
            titleSmall.style.display = string.IsNullOrEmpty(small) ? DisplayStyle.None : DisplayStyle.Flex;
            titleBox.style.display = DisplayStyle.Flex;
            titleBox.parent.style.top = top;                             // centre (388) by default; the finale puts it in the upper third above the crown
            bool finale = top < 200f;                                     // the finale's summary is the module's last statement: it reads at 22 px, full white
            titleSmall.style.fontSize = finale ? 22f : 15f; titleSmall.style.color = finale ? Text : TextSecondary;
            Dim(DimSrc.Title, dim);                                      // the 3D recedes a little so the plate edge blends in
            yield return Fx.Tween(ReduceMotion ? 0.3f : 1.2f, k =>
            {
                if (my != titleGen) return;
                titleBox.style.opacity = k;
                titleBig.style.letterSpacing = ReduceMotion ? DisplaySpacing : Mathf.Lerp(18f, DisplaySpacing, k);
                titleRule.style.width = 160 * k;
            }, Ease.Out);
            yield return Fx.Wait(hold);
            if (my == titleGen) Dim(DimSrc.Title, 0);
            yield return Fx.Tween(0.8f, k => { if (my == titleGen) titleBox.style.opacity = 1 - k; });
            if (my == titleGen) titleBox.style.display = DisplayStyle.None;
        }

        void HideTitle() { titleGen++; titleBox.style.opacity = 0; titleBox.style.display = DisplayStyle.None; Dim(DimSrc.Title, 0); }

        // ================================================================== information
        VisualElement infoWrap, infoCard, tooltip; Label infoTitle, infoBody, tipTitle, tipBody, tipFoot; IconElement infoIcon, tipIcon;

        void BuildInfo()
        {
            infoWrap = Abs(new VisualElement { pickingMode = PickingMode.Ignore }, float.NaN, 380, 40); infoWrap.AddToClassList("ps-shift");
            infoCard = Hidden(Panel(20, 1f, A(GlassStroke, 0.28f))); Size(infoCard, 380); Pad(infoCard, 20, 20, 20, 20);   // opaque: no blur in UI Toolkit, so bright signs behind would ghost through the text
            var col = new FlexBox(false, 14); infoCard.Add(col);
            var head = col.Put(new FlexBox(true, 10));
            infoTitle = head.Put(WrapText(Txt("", TS.H2, Text))); infoTitle.style.flexGrow = 1; infoTitle.style.flexShrink = 1;
            head.Put(Round(44, IconId.Close, 18, TextSecondary, HideInfo, "ps-round--ghost"));
            var vis = col.Put(new VisualElement()); Size(vis, 340, 150); Radius(vis, 14); Bg(vis, A(GreenDeep, 0.9f)); Border(vis, A(GreenBright, 0.25f), 1);
            vis.style.alignItems = Align.Center; vis.style.justifyContent = Justify.Center;
            var halo = Circle(120, A(GreenBright, 0.07f), Color.clear, 0); Glow(halo, GreenBright, 60, 30, 0.25f);
            vis.Add(halo); halo.Add(infoIcon = Icon(IconId.Leaf, 96, GreenBright));
            infoBody = col.Put(Txt("", TS.Body, TextSecondary, 340));
            infoWrap.Add(infoCard); panelLayer.Add(infoWrap);

            tooltip = Hidden(Abs(Panel(16, 0.92f, A(GreenBright, 0.45f)), 0, 0)); Size(tooltip, 400); Pad(tooltip, 12, 12, 12, 12);
            var tr = new FlexBox(true, 14); tooltip.Add(tr);
            var thumb = tr.Put(new VisualElement()); Size(thumb, 84, 84); Radius(thumb, 12); Bg(thumb, GreenDeep); thumb.style.flexShrink = 0;
            thumb.style.alignItems = Align.Center; thumb.style.justifyContent = Justify.Center;
            thumb.Add(tipIcon = Icon(IconId.Stomata, 44, GreenBright));
            var tc = tr.Put(new FlexBox(false, 4)); tc.style.flexShrink = 1;
            tipTitle = tc.Put(Txt("", TS.H3, GreenBright));
            tipBody = tc.Put(Txt("", TS.Small, TextSecondary, 270));
            var foot = tc.Put(new FlexBox(true, 6)); foot.Put(Icon(IconId.Info, 14, TextMuted)); tipFoot = foot.Put(Txt("", TS.Caption, TextMuted));
            tooltip.pickingMode = PickingMode.Ignore; IgnoreChildren(tooltip);
            overlayLayer.Add(tooltip);
        }

        bool infoInSlot;   // the info card stands in the progress panel's place (RefreshPersistent keeps the panel hidden meanwhile)

        /// title: Title Case — H2/H3 have no uppercase rule; caps are for Readout/Label only.
        /// slot: take the progress panel's place (top 140) instead of sitting under it (top 380); the panel steps aside until HideInfo,
        /// which keeps a tall card clear of Dr. Curio's companion pose in the micro worlds.
        public void ShowInfo(string title, string body, IconId icon, bool slot = false)
        {
            infoInSlot = slot; infoWrap.style.top = slot ? 140f : 380f;
            infoTitle.text = title; Set(infoBody, Prose(body)); infoIcon.Id = icon;
            Show(infoCard, true); RefreshPersistent();   // ends in PlaceScanner
        }

        public void HideInfo() { Show(infoCard, false); infoInSlot = false; RefreshPersistent(); }   // also the card's × button: the tracker comes straight back

        /// title: Title Case — H2/H3 have no uppercase rule; caps are for Readout/Label only.
        public void ShowTooltip(string title, string body, IconId icon, Vector2 screenPos)
        {
            Set(tipTitle, title); Set(tipBody, Prose(body)); tipIcon.Id = icon;
            tipFoot.text = askOn ? "Ask Dr. Curio to learn more" : "Click another part to inspect it";   // no second tier: point at what really works
            var panel = root.panel;
            if (panel != null)
            {
                var p = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPos.x, Screen.height - screenPos.y));
                float w = root.layout.width, h = root.layout.height, th = H(tooltip, 112);
                float x = p.x + 18, y = p.y + 18;
                if (x + 400 > w - 16) x = p.x - 418;
                if (y + th > h - 16) y = p.y - th - 18;
                tooltip.style.left = Mathf.Max(16, x); tooltip.style.top = Mathf.Max(16, y);
            }
            Show(tooltip, true);
        }

        public void HideTooltip() { Show(tooltip, false); }

        // ================================================================== generic call-to-action
        int btnGen; bool btnClicked;

        /// Generic bottom-centre call-to-action ("ENTER LEAF", "Continue"). Waits until clicked.
        public IEnumerator WaitButton(string label, IconId icon, ButtonKind kind)
        {
            int my = ++btnGen; btnClicked = false;
            btnLabel = label;
            btnHolder.Clear(); btnPulse = null;
            var b = Btn(label, kind, icon, () => { if (my == btnGen) btnClicked = true; });
            b.AddToClassList("ps-btn--big");
            if (kind == ButtonKind.Gold || kind == ButtonKind.Primary)
            {
                btnPulse = new GlowElement(kind == ButtonKind.Gold ? Gold : GreenBright, 999, 26, 0.75f);
                b.Insert(0, btnPulse);
            }
            btnHolder.Add(b);
            Show(btnHolder, true); RelayoutBottom(); FocusSoon(b);
            while (my == btnGen && !btnClicked) yield return null;
            if (my == btnGen) HideButton();
        }

        public void HideButton() { btnGen++; btnPulse = null; Show(btnHolder, false); RelayoutBottom(); }

        /// What the story is waiting on the learner to do right now, read by AskAI for "what should I do next?" / "help"; null when nothing is waiting.
        public string CurrentTask => IsShown(instrPanel) ? instrRaw
            : IsShown(btnHolder) ? $"When you're ready, press “{btnLabel}” at the bottom of the screen."
            : IsShown(expPanel) ? "Use the Factory controls on the left: switch a supply off and watch what happens to the food. When you're done, press “Start the final investigation”."
            : null;

        // ================================================================== waits
        /// Ends any pending Question / Feedback / WaitButton / Complete wait (no callback) and hides those cards.
        /// Hide* methods for those cards must also end their pending wait. Used by debug jumps and autoplay.
        public void CancelWaits() { HideQuestion(); HideFeedback(); HideButton(); HideComplete(); HideEndMenu(); }

        /// Hide every transient panel (callouts, scanner, info, instruction, question, reteach, equation, comparison,
        /// free explore, factory controls, magnification, in/out, glucose uses, button, title), end cinematic mode and clear
        /// the fade. Persistent HUD untouched.
        public void HideTransient()
        {
            pendingPop = -1;                                             // debug jumps / resets / UiShots never pop a tracker row
            SetCinematic(false);
            ClearCallouts(); HideScanner(); HideInfo(); HideTooltip(); HideInstruction();
            HideQuestion(); HideFeedback(); HideReteach(); HideEquation(); HideComparison(); HideFreeExplore(); HideComplete(); HideEndMenu(); HideExperiment();
            HideMagnification(); ShowInOut(false); ShowGlucoseUses(false); HideButton(); ShowClueCards(false);
            HideUnlock(); HideLoading(); HideTitle();
            SetFade(0f); sparkles.Stop(); fbFx.Stop(); completeFx.Stop(); transientFrame = Time.frameCount;
            for (int i = 0; i < dims.Length; i++) dims[i] = 0;
            dimEl.style.opacity = 0; spotEl.style.opacity = 0;
        }
    }
}
