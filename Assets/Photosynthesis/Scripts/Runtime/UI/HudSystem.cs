using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using static Photosynthesis.Kit;
using static Photosynthesis.Palette;

namespace Photosynthesis
{
    /// System menus (Pause · Settings · Help · Reset confirm), Ask Dr. Curio chat, developer debug panel.
    public partial class Hud
    {
        // ================================================================== system menus
        public event Action ResetRequested;
        public event Action ExitRequested;

        VisualElement sysDim, pausePanel, settingsPanel, helpPanel, resetPanel, exitPanel, creditsPanel, popup, popupAnchor;
        readonly List<VisualElement> sysStack = new List<VisualElement>();
        readonly Dictionary<VisualElement, VisualElement> sysFocus = new Dictionary<VisualElement, VisualElement>();   // panel → default control (Enter confirms)
        /// True while any system panel (pause, settings, help, confirms) is open: time is stopped and shortcuts must not advance the story.
        public bool Paused => sysStack.Count > 0;
        readonly FlexBox[] settingsTabs = new FlexBox[3]; readonly VisualElement[] tabSegs = new VisualElement[3];
        float savedTimeScale = 1f; bool popupOpen;
        PsToggle subsToggle, reduceToggle, fullToggle; PsSlider musicSlider, sfxSlider, voiceSlider;
        Label qualityValue, micValue, subSizeValue;

        static AudioDirector Ad => Module.I ? Module.I.audioDirector : null;

        void BuildSystem()
        {
            sysDim = Hidden(Layer(systemLayer)); sysDim.AddToClassList(Scrim); sysDim.pickingMode = PickingMode.Position;   // modal: blocks HUD + 3D; full-bleed: fade only
            Bg(sysDim, new Color(0f, 0.02f, 0.01f, GA(0.62f)));
            BuildPause(); BuildSettings(); BuildHelp(); BuildReset(); BuildExit(); BuildMusicCredits();

            popup = Abs(new VisualElement(), 0, 0); popup.style.display = DisplayStyle.None;
            Bg(popup, AG(Raised, 0.98f)); Border(popup, A(GlassStroke, 0.35f), 1); Radius(popup, 10); Pad(popup, 4, 4);
            popup.Add(new GlowElement(Color.black, 10, 18, 0.45f));
            systemLayer.Add(popup);
            systemLayer.RegisterCallback<PointerDownEvent>(e =>
            {
                if (!popupOpen) return;
                var t = e.target as VisualElement;
                if (t != null && (popup.Contains(t) || (popupAnchor != null && popupAnchor.Contains(t)))) return;
                ClosePopup();
            }, TrickleDown.TrickleDown);
        }

        static VisualElement Wide(VisualElement b) { b.style.alignSelf = Align.Stretch; return b; }

        void BuildPause()
        {
            pausePanel = Hidden(Panel(20, 0.94f)); Size(pausePanel, 400); Pad(pausePanel, 28, 28, 28, 28);
            var col = new FlexBox(false, 12); col.style.alignItems = Align.Center; pausePanel.Add(col);
            col.Put(Circle(56, A(Cyan, 0.14f), A(Cyan, 0.9f), 1.5f, Icon(IconId.Pause, 26, Cyan)));
            col.Put(Txt("Paused", TS.H1, Text));
            col.Put(Txt("Your investigation is safe. Take your time.", TS.Small, TextSecondary)).style.marginBottom = 6;
            sysFocus[pausePanel] = col.Put(Wide(Btn("Resume", ButtonKind.Primary, IconId.Play, CloseSystem)));
            col.Put(Wide(Btn("Settings", ButtonKind.Secondary, IconId.Settings, () => OpenSystem(settingsPanel))));
            col.Put(Wide(Btn("Help", ButtonKind.Secondary, IconId.Help, () => OpenSystem(helpPanel))));
            col.Put(Wide(Btn("Music credits", ButtonKind.Secondary, null, () => OpenSystem(creditsPanel))));
            col.Put(Wide(Btn("Restart investigation", ButtonKind.Secondary, IconId.Reset, () => OpenSystem(resetPanel))));
            col.Put(Wide(Btn("Exit", ButtonKind.Ghost, null, () => OpenSystem(exitPanel))));
            CenterRow(systemLayer, 220).Add(pausePanel);
        }

        void BuildSettings()
        {
            settingsPanel = Hidden(Panel(20, 0.95f)); Size(settingsPanel, 600); Pad(settingsPanel, 28, 28, 28, 28);
            var col = new FlexBox(false, 18); settingsPanel.Add(col);
            var head = col.Put(new FlexBox(true, 10));
            head.Put(Txt("Settings", TS.H1, Text)).style.flexGrow = 1;
            head.Put(Round(44, IconId.Close, 18, TextSecondary, CloseSystem, "ps-round--ghost"));

            var tabs = col.Put(new VisualElement()); tabs.style.flexDirection = FlexDirection.Row; tabs.style.alignSelf = Align.FlexStart;
            Pad(tabs, 4, 4); Radius(tabs, 999); Bg(tabs, AG(Sunken, 0.8f));
            string[] names = { "Audio", "Display", "Accessibility" };
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                var s = tabSegs[i] = Stadium(new VisualElement()); s.AddToClassList("ps-seg"); s.AddToClassList("ps-seg--green"); Pad(s, 8, 18);
                if (i > 0) s.style.marginLeft = 4;
                s.Add(Txt(names[i], TS.Button, Text));
                Click(s, () => SelectTab(idx), Sfx.UiSelect);
                tabs.Add(s);
                settingsTabs[i] = col.Put(new FlexBox(false, 16));
            }

            var audio = settingsTabs[0];
            SettingRow(audio, "Music volume", musicSlider = new PsSlider(0.6f, v => { var a = Ad; if (a) a.MusicVolume = v; PlayerPrefs.SetFloat("ps.vol.music", v); }));
            SettingRow(audio, "Effects volume", sfxSlider = new PsSlider(0.8f, v => { var a = Ad; if (a) a.SfxVolume = v; PlayerPrefs.SetFloat("ps.vol.sfx", v); }));
            SettingRow(audio, "Dr. Curio voice", voiceSlider = new PsSlider(1f, v => { var a = Ad; if (a) a.VoiceVolume = v; PlayerPrefs.SetFloat("ps.vol.voice", v); }));
            SettingRow(audio, "Microphone", Drop(MicOptions, () => string.IsNullOrEmpty(MicDevice) ? 0 : Array.IndexOf(MicDevices(), MicDevice) + 1,
                i =>
                {
                    var d = MicDevices(); MicDevice = i > 0 && i <= d.Length ? d[i - 1] : null;
                    var m = Module.I; if (m && m.ai) m.ai.micDevice = MicDevice ?? "";   // Ask AI records from its own field
#if UNITY_WEBGL && !UNITY_EDITOR
                    CurioSelectMicrophone(MicDevice ?? "");
#endif
                }, out micValue));
#if UNITY_WEBGL && !UNITY_EDITOR
            audio.Put(Btn("Allow / refresh microphones", ButtonKind.Secondary, IconId.Mic, () => { micValue.text = "Checking microphone access…"; CurioMicrophones(gameObject.name); }));
#endif

            var display = settingsTabs[1];
            SettingRow(display, "Graphics quality", Drop(() => QualitySettings.names, QualitySettings.GetQualityLevel,
                i => { QualitySettings.SetQualityLevel(i, true); PlayerPrefs.SetInt(Module.QualityKey, i); PlayerPrefs.Save(); }, out qualityValue));   // the learner's choice wins over the iGPU auto-pick next launch
            SettingRow(display, "Fullscreen", fullToggle = new PsToggle(Screen.fullScreen, v => Screen.fullScreen = v));

            var access = settingsTabs[2];
            SettingRow(access, "Subtitles", subsToggle = new PsToggle(subtitles, v => Subtitles = v));
            SettingRow(access, "Subtitle size", Drop(() => SubSizes, () => largeSubs ? 1 : 0, i => { largeSubs = i == 1; ApplySubtitles(); }, out subSizeValue));
            SettingRow(access, "Reduce motion", reduceToggle = new PsToggle(ReduceMotion, v => { ReduceMotion = v; root.EnableInClassList("ps-reduce", v); PlayerPrefs.SetInt("ps_reduce_motion", v ? 1 : 0); PlayerPrefs.Save(); }));

            // Every tab body reserves the tallest tab's height (Audio: 4 rows x 34 + 3 gaps x 16 = 184),
            // so the panel keeps one size and Done / Reset progress stay in place when switching tabs.
            int maxRows = 0;
            foreach (var t in settingsTabs) maxRows = Math.Max(maxRows, t.childCount);
            foreach (var t in settingsTabs) t.style.minHeight = maxRows * 34 + (maxRows - 1) * 16;

            var foot = col.Put(new FlexBox(true, 10)); foot.style.justifyContent = Justify.SpaceBetween;
            foot.Put(Btn("Reset progress", ButtonKind.Ghost, IconId.Reset, () => OpenSystem(resetPanel)));
            sysFocus[settingsPanel] = foot.Put(Btn("Done", ButtonKind.Primary, IconId.Check, CloseSystem));
            SelectTab(0);
            CenterRow(systemLayer, 110).Add(settingsPanel);
        }

        void BuildMusicCredits()
        {
            creditsPanel = Hidden(Panel(20, .95f)); Size(creditsPanel, 720); Pad(creditsPanel, 28, 28, 28, 28);
            creditsPanel.Add(Txt("Music credits", TS.H1, Text));
            var scroll = new ScrollView(ScrollViewMode.Vertical); scroll.style.height = 440; scroll.style.marginTop = 16;
            var credits = Resources.Load<TextAsset>("MusicCredits");
            scroll.Add(Txt(credits ? credits.text : "Music credits unavailable.", TS.Small, TextSecondary, 640));
            creditsPanel.Add(scroll);
            sysFocus[creditsPanel] = Btn("Done", ButtonKind.Primary, IconId.Check, CloseSystem);
            creditsPanel.Add(sysFocus[creditsPanel]); CenterRow(systemLayer, 120).Add(creditsPanel);
        }

        static readonly string[] SubSizes = { "Normal", "Large" };
#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void CurioMicrophones(string target);
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void CurioSelectMicrophone(string device);
        [Serializable] public class BrowserMic { public string id, label; }
        [Serializable] public class BrowserMics { public BrowserMic[] devices; public string error; }
        static BrowserMic[] browserMics = Array.Empty<BrowserMic>();
        static string[] MicDevices() => Array.ConvertAll(browserMics, d => d.id);
        public void OnBrowserMicrophones(string json)
        {
            var result = JsonUtility.FromJson<BrowserMics>(json);
            browserMics = result.devices ?? Array.Empty<BrowserMic>();
            micValue.text = !string.IsNullOrEmpty(result.error) ? result.error : browserMics.Length > 0 ? "Choose microphone below" : "No microphone found";
            if (!string.IsNullOrEmpty(result.error)) Debug.LogWarning("[Curio] " + result.error);
        }
#else
        static string[] MicDevices() => Microphone.devices;
#endif
        static string[] MicOptions()
        {
            var d = MicDevices(); var o = new string[d.Length + 1]; o[0] = "Default microphone";
#if UNITY_WEBGL && !UNITY_EDITOR
            for (int i = 0; i < d.Length; i++) o[i + 1] = browserMics[i].label;
#else
            Array.Copy(d, 0, o, 1, d.Length);
#endif
            return o;
        }

        static void SettingRow(FlexBox tab, string label, VisualElement control)
        {
            var r = tab.Put(new FlexBox(true, 12)); Size(r, 544); r.style.minHeight = 34;
            r.Put(Txt(label, TS.Body, TextSecondary)).style.flexGrow = 1;
            r.Put(control);
            if (control is PsToggle t)
            {   // the whole 544×48 row is the touch target, not the 46×26 switch; the row takes keyboard focus and the switch shows it
                r.style.minHeight = 48; r.style.alignItems = Align.Center; t.pickingMode = PickingMode.Ignore; t.focusable = false; r.AddToClassList("ps-exprow");
                Click(r, t.Flip, Sfx.UiSelect);
            }
        }

        void SelectTab(int i)
        {
            for (int j = 0; j < 3; j++)
            {
                tabSegs[j].EnableInClassList("ps-seg--on", j == i);
                settingsTabs[j].style.display = j == i ? DisplayStyle.Flex : DisplayStyle.None;
            }
            ClosePopup();
        }

        void RefreshSettings()
        {
            var a = Ad; var m = Module.I;
            if (string.IsNullOrEmpty(MicDevice) && m && m.ai && !string.IsNullOrEmpty(m.ai.micDevice)) MicDevice = m.ai.micDevice;
            if (a) { musicSlider.Value = a.MusicVolume; sfxSlider.Value = a.SfxVolume; voiceSlider.Value = a.VoiceVolume; }
            subsToggle.Value = subtitles; reduceToggle.Value = ReduceMotion; fullToggle.Value = Screen.fullScreen;
            var q = QualitySettings.names; int qi = QualitySettings.GetQualityLevel();
            qualityValue.text = qi >= 0 && qi < q.Length ? q[qi] : "—";
            micValue.text = string.IsNullOrEmpty(MicDevice) ? "Default microphone" : MicDevice;
#if UNITY_WEBGL && !UNITY_EDITOR
            var selectedMic = Array.Find(browserMics, d => d.id == MicDevice);
            if (selectedMic != null) micValue.text = selectedMic.label;
#endif
            subSizeValue.text = SubSizes[largeSubs ? 1 : 0];
        }

        /// Figma "Dropdown": value + chevron; opens an option list above everything in the system layer.
        VisualElement Drop(Func<string[]> options, Func<int> current, Action<int> pick, out Label value)
        {
            var d = new VisualElement(); d.AddToClassList("ps-drop");
            var v = Txt("", TS.Small, Text);
            v.style.flexGrow = 1; v.style.overflow = Overflow.Hidden; v.style.textOverflow = TextOverflow.Ellipsis; v.style.whiteSpace = WhiteSpace.NoWrap;
            d.Add(v); d.Add(Icon(IconId.ChevronDown, 16, TextSecondary));
            Click(d, () =>
            {
                var opts = options();
                OpenPopup(d, opts, current(), i => { pick(i); if (i < opts.Length) v.text = opts[i]; });
            });
            value = v; return d;
        }

        void OpenPopup(VisualElement anchor, string[] opts, int cur, Action<int> onPick)
        {
            if (popupOpen && popupAnchor == anchor) { ClosePopup(); return; }
            for (int i = popup.childCount - 1; i >= 1; i--) popup.RemoveAt(i);        // keep the shadow
            for (int i = 0; i < opts.Length && i < 10; i++)
            {
                int idx = i;
                var it = new VisualElement(); it.AddToClassList("ps-pop-item"); it.EnableInClassList("ps-pop-item--on", i == cur);
                var l = Txt(opts[i], TS.Small, Text); l.style.overflow = Overflow.Hidden; l.style.textOverflow = TextOverflow.Ellipsis; l.style.whiteSpace = WhiteSpace.NoWrap;
                it.Add(l);
                Click(it, () => { onPick(idx); ClosePopup(); }, Sfx.UiSelect);
                popup.Add(it);
            }
            var wb = anchor.worldBound;
            var p = systemLayer.WorldToLocal(new Vector2(wb.xMin, wb.yMax + 4));
            popup.style.left = p.x; popup.style.top = p.y; popup.style.width = Mathf.Max(wb.width, 200);
            popup.style.display = DisplayStyle.Flex; popupOpen = true; popupAnchor = anchor;
        }

        void ClosePopup() { if (popup == null) return; popup.style.display = DisplayStyle.None; popupOpen = false; popupAnchor = null; }

        void BuildHelp()
        {
            helpPanel = Hidden(Panel(20, 0.95f)); Size(helpPanel, 640); Pad(helpPanel, 28, 28, 28, 28);
            var col = new FlexBox(false, 14); helpPanel.Add(col);
            col.Put(Txt("How to explore", TS.H1, Text));
            col.Put(Txt("Turn, zoom and question everything you see.", TS.Small, TextSecondary));
            (IconId, string, string)[] rows =
            {
                (IconId.Replay, "Rotate the plant", "Drag"), (IconId.Zoom, "Zoom in / out", "+ / − buttons · mouse wheel"),
                (IconId.Target, "Pan", "Right-drag (mouse only)"), (IconId.Eye, "Select a structure", "Tap or click"),
                (IconId.Mic, "Ask Dr. Curio", "ASK AI button · hold the mic to talk"), (IconId.CC, "Subtitles", "CC button · C"),
                (IconId.ChevronRight, "Continue dialogue", "› button · Space"), (IconId.Check, "Answer a question", "Tap an answer · 1–4 · Enter"), (IconId.Pause, "Pause", "Menu button · Esc")
            };
            foreach (var (ic, action, keys) in rows)
            {
                var r = col.Put(new FlexBox(true, 14)); Size(r, 584); Pad(r, 8, 12); Radius(r, 12); Bg(r, AG(Raised, 0.45f));
                r.Put(Circle(36, A(Cyan, 0.12f), Color.clear, 0, Icon(ic, 18, Cyan)));
                r.Put(Txt(action, TS.BodyStrong, Text)).style.flexGrow = 1;
                r.Put(Txt(keys, TS.Small, TextMuted));
            }
            var f = col.Put(new FlexBox(true, 10)); f.style.marginTop = 4;
            f.Put(Txt("Stuck? Ask Dr. Curio: “What should I do next?”", TS.Small, Cyan)).style.flexGrow = 1;
            sysFocus[helpPanel] = f.Put(Btn("Got it", ButtonKind.Primary, IconId.Check, CloseSystem));
            CenterRow(systemLayer, 120).Add(helpPanel);
        }

        void BuildReset()
        {
            resetPanel = Hidden(Panel(20, 0.95f, A(Gold, 0.7f), 1.5f)); Size(resetPanel, 480); Pad(resetPanel, 32, 32, 32, 32);
            var col = new FlexBox(false, 14); col.style.alignItems = Align.Center; resetPanel.Add(col);
            col.Put(Circle(60, A(Gold, 0.14f), A(Gold, 0.9f), 1.5f, Icon(IconId.Reset, 28, Gold)));
            col.Put(Txt("Restart the investigation?", TS.H2, Text));
            var b = col.Put(Txt("Your clues and answers will be cleared and you will return to the start of Scene 2 — A New World.", TS.Body, TextSecondary, 416));
            b.style.unityTextAlign = TextAnchor.UpperCenter;
            var row = col.Put(new FlexBox(true, 10)); row.style.marginTop = 6;
            sysFocus[resetPanel] = row.Put(Btn("Cancel", ButtonKind.Secondary, null, CloseSystem));
            row.Put(Btn("Restart", ButtonKind.Gold, IconId.Reset, () => { CloseAllSystem(); if (chatOpen) CloseChat(); ResetRequested?.Invoke(); }));
            CenterRow(systemLayer, 330).Add(resetPanel);
        }

        /// Pause ▸ Exit confirm (there is no menu scene: Exit quits the module and nothing is saved).
        void BuildExit()
        {
            exitPanel = Hidden(Panel(20, 0.95f, A(Gold, 0.7f), 1.5f)); Size(exitPanel, 480); Pad(exitPanel, 32, 32, 32, 32);
            var col = new FlexBox(false, 14); col.style.alignItems = Align.Center; exitPanel.Add(col);
            col.Put(Circle(60, A(Gold, 0.14f), A(Gold, 0.9f), 1.5f, Icon(IconId.Exit, 28, Gold)));   // leaving, not "close / wrong"
            col.Put(Txt("Leave the investigation?", TS.H2, Text));
            var b = col.Put(Txt("Your progress is not saved — you'll start again at Scene 2 next time.", TS.Body, TextSecondary, 416));
            b.style.unityTextAlign = TextAnchor.UpperCenter;
            var row = col.Put(new FlexBox(true, 10)); row.style.marginTop = 6;
            // the safe choice is the rewarding one: green Primary on the right (as Pause "Resume"); Exit stays a quieter Secondary
            row.Put(Btn("Exit", ButtonKind.Secondary, IconId.Exit, () => { CloseAllSystem(); ExitRequested?.Invoke(); }));
            sysFocus[exitPanel] = row.Put(Btn("Keep investigating", ButtonKind.Primary, IconId.ChevronRight, CloseSystem));
            CenterRow(systemLayer, 330).Add(exitPanel);
        }

        /// Pause (Esc): time stops (Time.timeScale 0) and audio pauses while any system panel is open.
        void OpenSystem(VisualElement panel)
        {
            if (panel == null || (sysStack.Count > 0 && sysStack[sysStack.Count - 1] == panel)) return;
            ClosePopup();
            bool swap = sysStack.Count > 0;
            if (!swap)
            {
                preSysFocus = root.panel?.focusController?.focusedElement as VisualElement;   // restored on close (keyboard users keep their place)
                if (preSysFocus != null && preSysFocus.ClassListContains("ps-tile")) preSysFocus = null;
                savedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
                Time.timeScale = 0f; AudioListener.pause = true;
                Show(sysDim, true);
            }
            else Show(sysStack[sysStack.Count - 1], false);
            sysStack.Add(panel);
            if (panel == settingsPanel) RefreshSettings();
            if (swap) ShowSystemTop(); else Show(panel, true);
            sysFocus.TryGetValue(panel, out var d); FocusSoon(d, swap ? 170 : 30);
        }

        /// Swapping system panels: the outgoing one clears before the next fades in (no double exposure).
        void ShowSystemTop()
        {
            var p = sysStack[sysStack.Count - 1];
            if (ReduceMotion) { Show(p, true); return; }
            p.schedule.Execute(() => { if (sysStack.Count > 0 && sysStack[sysStack.Count - 1] == p) Show(p, true); }).StartingIn(140);
        }

        void CloseSystem()
        {
            if (sysStack.Count == 0) return;
            ClosePopup();
            Show(sysStack[sysStack.Count - 1], false); sysStack.RemoveAt(sysStack.Count - 1);
            if (sysStack.Count > 0) { var top = sysStack[sysStack.Count - 1]; ShowSystemTop(); sysFocus.TryGetValue(top, out var d); FocusSoon(d, 170); return; }
            Show(sysDim, false); AudioListener.pause = false;
            if (Time.timeScale == 0f) Time.timeScale = savedTimeScale;
            root.panel?.focusController?.focusedElement?.Blur();   // a focused Menu tile must not reopen on the next Enter
            if (preSysFocus != null && preSysFocus.panel != null && preSysFocus.enabledInHierarchy) FocusSoon(preSysFocus);
            preSysFocus = null;
        }

        void CloseAllSystem() { while (sysStack.Count > 0) CloseSystem(); }
        VisualElement preSysFocus;

        public void TogglePause() { if (sysStack.Count > 0) CloseAllSystem(); else OpenSystem(pausePanel); }

        /// Dev/test only (-uishots): open a system panel directly. 0 pause · 1–3 settings tab · 4 help · 5 reset · 6 exit · other: close all.
        public void DebugSystem(int i)
        {
            CloseAllSystem();
            if (i == 0) OpenSystem(pausePanel);
            else if (i >= 1 && i <= 3) { OpenSystem(settingsPanel); SelectTab(i - 1); }
            else if (i == 4) OpenSystem(helpPanel);
            else if (i == 5) OpenSystem(resetPanel);
            else if (i == 6) OpenSystem(exitPanel);
        }

        // ================================================================== Ask AI chat
        VisualElement chatPanel, voiceStrip, micBtn; ScrollView chatScroll; TextField chatField; Label voiceLabel; ArtElement chatAvatar;
        readonly VisualElement[] bars = new VisualElement[16];
        bool chatOpen, micHeld; VoiceState voice; string lastLearner; float lastLearnerTime;
        static readonly string[] Suggestions = { "What am I looking at?", "How does water reach leaves?", "Explain this again." };

        public event Action<string> ChatSubmitted;
        public event Action MicDown, MicUp, ChatClosed;
        public event Action<bool> VoiceToggled;
        Label aiStatus, aiTranscript;
        VisualElement chatBlocker;
        public void SetAIStatus(string text) { if (aiStatus != null) aiStatus.text = text; }
        public string AIStatus => aiStatus?.text ?? "";
        public void SetAITranscript(string text) { if (aiTranscript != null) { aiTranscript.text = text; aiTranscript.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex; } }
        public void RestoreLessonSpeed(float speed) { savedTimeScale = speed; }
        public bool ChatOpen => chatOpen;

        void BuildChat()
        {
            chatBlocker = Abs(new VisualElement(), 0, 0, 0, 0); chatBlocker.style.display = DisplayStyle.None; chatLayer.Add(chatBlocker);
            chatPanel = Hidden(Abs(Panel(20, 0.92f), float.NaN, float.NaN, 40, 180)); Size(chatPanel, 460); Pad(chatPanel, 18, 18, 18, 18);
            var col = new FlexBox(false, 14); chatPanel.Add(col);

            var head = col.Put(new FlexBox(true, 12));
            head.Put(Circle(48, Raised, A(Cyan, 0.9f), 2, chatAvatar = new ArtElement(ArtId.Curio, 42)));
            var hc = head.Put(new FlexBox(false, 0)); hc.style.flexGrow = 1;
            hc.Put(Txt("Ask Dr. Curio", TS.H2, Text)); hc.Put(Txt("Botanist · Investigator · AI companion", TS.Caption, TextMuted));
            head.Put(Round(44, IconId.Close, 18, TextSecondary, CloseChat, "ps-round--ghost"));

            chatScroll = col.Put(new ScrollView(ScrollViewMode.Vertical));
            chatScroll.style.maxHeight = 200; chatScroll.style.flexShrink = 1;
            chatScroll.verticalScrollerVisibility = ScrollerVisibility.Hidden; chatScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;

            voiceStrip = col.Put(new FlexBox(true, 12)); Pad(voiceStrip, 8, 14); Radius(voiceStrip, 14); Bg(voiceStrip, AG(Sunken, 0.75f));
            voiceStrip.style.display = DisplayStyle.None;
            var barBox = ((FlexBox)voiceStrip).Put(new FlexBox(true, 4)); barBox.style.height = 40;
            for (int i = 0; i < bars.Length; i++) { var b = bars[i] = barBox.Put(new VisualElement()); Size(b, 5, 8); Radius(b, 2.5f); Bg(b, Cyan); }
            voiceLabel = ((FlexBox)voiceStrip).Put(Txt("", TS.Small, TextSecondary));

            aiStatus = col.Put(WrapText(Txt("Ready to ask", TS.Small, TextSecondary))); aiStatus.enableRichText = false;
            aiTranscript = col.Put(WrapText(Txt("", TS.Body, Text))); aiTranscript.enableRichText = false;
            aiTranscript.style.maxHeight = 110; aiTranscript.style.overflow = Overflow.Hidden;
            aiTranscript.style.display = DisplayStyle.None;
            var voiceToggle = col.Put(new Toggle("Mute AI voice")); voiceToggle.style.minHeight = 44;
            voiceToggle.RegisterValueChangedCallback(e => VoiceToggled?.Invoke(e.newValue));
            var chips = col.Put(new VisualElement { pickingMode = PickingMode.Ignore });
            chips.style.flexDirection = FlexDirection.Row; chips.style.flexWrap = Wrap.Wrap; chips.style.marginBottom = -8;
            foreach (var q in Suggestions)
            {
                var text = q;
                var c = Stadium(new VisualElement()); c.AddToClassList("ps-chip"); c.Add(Txt(q, TS.Small, Text));
                chips.Add(Click(c, () => Submit(text), Sfx.UiSelect));
            }

            var input = col.Put(new FlexBox(true, 10));
            chatField = new TextField { maxLength = 500, tooltip = "Ask Mr. Curio a question" }; chatField.AddToClassList("ps-field");
            chatField.textEdition.placeholder = "Type your question, or hold the mic…";
            chatField.textEdition.hidePlaceholderOnFocus = false;            // the field is focused on open: keep its affordance text
            chatField.textSelection.cursorColor = Cyan; chatField.textSelection.selectionColor = A(Cyan, 0.35f);
            var inner = chatField.Q(className: "unity-base-text-field__input");
            if (inner != null) { Font(inner, false); inner.style.color = Text; Stadium(inner); }   // 21 px end caps: the placeholder sits inside the shape
            chatField.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { SubmitField(); e.StopPropagation(); }
                else if (e.character == '\n') e.StopPropagation();
            }, TrickleDown.TrickleDown);
            input.Put(chatField);
            micBtn = input.Put(Round(44, IconId.Mic, 20, Cyan, null, "ps-round--mic"));
            micBtn.RegisterCallback<PointerDownEvent>(e => { micBtn.CapturePointer(e.pointerId); StartTalk(); });
            micBtn.RegisterCallback<PointerUpEvent>(e => { micBtn.ReleasePointer(e.pointerId); StopTalk(); });
            micBtn.RegisterCallback<PointerCaptureOutEvent>(_ => StopTalk());
            input.Put(Round(44, IconId.Send, 20, Scene, SubmitField, "ps-round--send"));

            chatLayer.Add(chatPanel);
            AddChat(false, "Hi! I'm Dr. Curio. Ask me about photosynthesis! Your lesson will wait while we talk.");
        }

        public void OpenChat() => OpenChat(true);

        /// focusField: the learner opened it to type (orb click); the hint button and the M talk key keep the keyboard free.
        void OpenChat(bool focusField)
        {
            if (chatOpen || !askOn || Paused) return;
            chatOpen = true; chatBlocker.style.display = DisplayStyle.Flex; Show(chatPanel, true); RefreshPersistent();   // RefreshPersistent also moves the info card aside
            exploreRow.style.right = 40 + 460 + 20;   // the Free Explore bar re-centres left of the chat
            ScrollChatToEnd();
            if (focusField) FocusSoon(chatField);
        }

        public void CloseChat()
        {
            if (!chatOpen) return;
            chatOpen = false; chatBlocker.style.display = DisplayStyle.None; Show(chatPanel, false);
            if (TypingInChat) chatField.Blur();
            StopTalk(); ChatClosed?.Invoke(); RefreshPersistent();
            exploreRow.style.right = 0;
        }

        void SubmitField() { var s = chatField.value; chatField.value = ""; Submit(s); }

        void Submit(string text)
        {
            text = text?.Trim();
            if (string.IsNullOrEmpty(text)) return;
            AddChat(true, text);
            ChatSubmitted?.Invoke(text);
        }

        public void AddChat(bool learner, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            // The HUD echoes typed questions immediately; skip the same text if Ask AI echoes it again.
            if (learner && text == lastLearner && Time.unscaledTime - lastLearnerTime < 3f) return;
            if (learner) { lastLearner = text; lastLearnerTime = Time.unscaledTime; }
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 10;
            row.style.justifyContent = learner ? Justify.FlexEnd : Justify.FlexStart;
            var b = new VisualElement(); Pad(b, 10, 14); Radius(b, 14); b.style.maxWidth = 330;
            Bg(b, learner ? A(Green, 0.85f) : AG(Raised, 0.75f));
            if (!learner) Border(b, A(Cyan, 0.35f), 1);
            var l = WrapText(Txt(Prose(text), TS.Body, Text)); l.style.maxWidth = 302; b.Add(l);   // replies are teaching text: Body, not Small
            row.Add(b);
            AppendChat(row);
        }

        public void ShowWorldAction(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            const string Prefix = "World action · ";
            if (text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) text = text.Substring(Prefix.Length);
            var p = new FlexBox(true, 8); Pad(p, 8, 12); Radius(p, 16); Bg(p, A(Gold, 0.12f)); Border(p, A(Gold, 0.8f), 1);
            p.style.marginBottom = 10; p.style.alignSelf = Align.FlexStart;
            p.Put(Icon(IconId.Sparkle, 14, Gold));                                   // a discovery glyph, not a play button
            var l = p.Put(WrapText(Txt(Prefix + text, TS.Label, Text))); l.style.maxWidth = 370;   // the pill hugs its text instead of reading as a bar
            AppendChat(p);
            Play(Sfx.UiSelect, 0.5f);
        }

        void AppendChat(VisualElement row)
        {
            row.AddToClassList(Anim); row.AddToClassList(Off);
            chatScroll.Add(row);
            row.schedule.Execute(() => row.RemoveFromClassList(Off)).StartingIn(16);
            var cc = chatScroll.contentContainer;
            while (cc.childCount > 40) cc.RemoveAt(0);
            ScrollChatToEnd();
        }

        void ScrollChatToEnd() =>
            chatScroll.schedule.Execute(() => chatScroll.scrollOffset = new Vector2(0, Mathf.Max(0, chatScroll.contentContainer.layout.height))).StartingIn(40);

        void StartTalk()
        {
            if (micHeld) return;
            micHeld = true; micBtn.AddToClassList("ps-round--live"); Play(Sfx.UiClick, 0.6f);
            MicDown?.Invoke();
        }

        void StopTalk()
        {
            if (!micHeld) return;
            micHeld = false; micBtn.RemoveFromClassList("ps-round--live");
            MicUp?.Invoke();
        }

        public void SetVoiceState(VoiceState s) { voice = s; ApplyVoice(); }

        /// Orb ring/tag + chat voice strip per voice state (Figma "Ask AI Button" variants).
        void ApplyVoice()
        {
            if (orb == null) return;
            Color c; string tag, strip = null; IconId? badge = null;
            switch (voice)
            {
                case VoiceState.Listening: c = Incorrect; tag = "Listening"; badge = IconId.Mic; strip = "Listening… release to send"; break;
                case VoiceState.Transcribing: c = Cyan; tag = "Transcribing"; badge = IconId.Sparkle; strip = "Turning your speech into text…"; break;
                case VoiceState.Thinking: c = Gold; tag = "Thinking"; badge = IconId.Sparkle; strip = "Dr. Curio is thinking…"; break;
                case VoiceState.Speaking: c = GreenBright; tag = "Speaking"; badge = IconId.Energy; strip = "Dr. Curio is speaking"; break;
                default: c = orbHover ? GreenBright : Cyan; tag = "Ask AI"; break;
            }
            BorderColor(orb, c); orbGlow.Color = A(c, 0.6f); BorderColor(orbTag, A(c, 0.9f));
            Set(orbTagLabel, tag);
            orbTagIcon.style.display = badge.HasValue ? DisplayStyle.Flex : DisplayStyle.None;
            if (badge.HasValue) { orbTagIcon.Id = badge.Value; orbTagIcon.Color = c; }
            orbTagLabel.style.marginLeft = badge.HasValue ? 6 : 0;
            voiceStrip.style.display = strip != null ? DisplayStyle.Flex : DisplayStyle.None;
            Set(voiceLabel, strip);
            foreach (var b in bars) b.style.backgroundColor = c;
        }

        void UpdateChat(float t)
        {
            if (!chatOpen || voice == VoiceState.Idle) return;
            var m = Module.I; var a = Ad;
            float lvl = voice == VoiceState.Listening ? (m && m.ai ? m.ai.MicLevel : 0f) : a ? a.VoiceLevel : 0f;
            for (int i = 0; i < bars.Length; i++)
            {
                float n = Mathf.PerlinNoise(t * 6f, i * 0.37f), h;
                // listening: the learner's live mic level, spread across the bars with noise so it reads as a waveform
                if (voice == VoiceState.Listening) h = 6f + 32f * Mathf.Clamp01(lvl * 1.6f) * (0.45f + 0.55f * n) + 3f * n;
                else if (voice == VoiceState.Speaking) h = 6f + 32f * Mathf.Clamp01(lvl * 1.4f + 0.2f * n) * (0.7f + 0.3f * Mathf.Sin(t * 11f + i * 0.8f));
                else h = 6f + 8f * (0.5f + 0.5f * Mathf.Sin(t * 5f - i * 0.5f));
                bars[i].style.height = h;
            }
        }

        // ================================================================== developer
        VisualElement debugPanel, debugChips; Label statsLabel, tsLabel;
        Action<Beat> dbgJump; Action<bool> dbgSkip; Action<float> dbgTs; Action dbgUnlock; Beat[] dbgBeats;
        readonly Dictionary<Beat, VisualElement> beatChips = new Dictionary<Beat, VisualElement>();

        void BuildDebugPanel()
        {
            debugPanel = Hidden(Abs(Panel(18, 0.94f, A(Gold, 0.6f), 1.5f), 40, 150)); Size(debugPanel, 560); Pad(debugPanel, 22, 22, 22, 22);
            var col = new FlexBox(false, 14); debugPanel.Add(col);
            var head = col.Put(new FlexBox(true, 10));
            head.Put(Icon(IconId.Bug, 24, Gold));
            head.Put(Txt("Developer debug", TS.H2, Text)).style.flexGrow = 1;
            head.Put(Pill("Dev build · F1", Gold, null, 0.12f));
            col.Put(Txt("Jump to beat", TS.Label, TextMuted));
            debugChips = col.Put(new VisualElement { pickingMode = PickingMode.Ignore });
            debugChips.style.flexDirection = FlexDirection.Row; debugChips.style.flexWrap = Wrap.Wrap; debugChips.style.marginBottom = -8;
            DebugRow(col, "Skip voice-over", new PsToggle(false, v => dbgSkip?.Invoke(v)));
            DebugRow(col, "Show FPS & stats", new PsToggle(true, v => statsLabel.style.display = v ? DisplayStyle.Flex : DisplayStyle.None));
            var ts = new FlexBox(true, 10);
            ts.Put(new PsSlider(0.5f, v =>
            {
                float s = v <= 0.5f ? Mathf.Lerp(0.1f, 1f, v / 0.5f) : Mathf.Lerp(1f, 4f, (v - 0.5f) / 0.5f);
                tsLabel.text = $"{s:0.0}×"; dbgTs?.Invoke(s);
            }));
            tsLabel = ts.Put(Txt("1.0×", TS.Caption, TextSecondary)); tsLabel.style.width = 40;
            DebugRow(col, "Time scale", ts);
            statsLabel = col.Put(Txt("", TS.Caption, TextMuted, 516));
            var foot = col.Put(new FlexBox(true, 10));
            foot.Put(Btn("Restart", ButtonKind.Secondary, IconId.Reset, () => { if (dbgBeats != null && dbgBeats.Length > 0) Jump(dbgBeats[0]); }));
            foot.Put(Btn("Unlock all clues", ButtonKind.Ghost, IconId.Unlock, () => dbgUnlock?.Invoke()));
            debugLayer.Add(debugPanel);
        }

        static void DebugRow(FlexBox col, string label, VisualElement control)
        {
            var r = col.Put(new FlexBox(true, 12)); Size(r, 516);
            r.Put(Txt(label, TS.Body, TextSecondary)).style.flexGrow = 1;
            r.Put(control);
        }

        static string BeatName(Beat b) => b switch
        {
            Beat.Scene2Start => "Scene 2 Start", Beat.Chamber => "Botanical Chamber", Beat.CurioIntro => "Dr. Curio Intro",
            Beat.Clue1 => "Clue 01", Beat.Clue2 => "Clue 02", Beat.Clue3 => "Clue 03", Beat.CO2 => "CO₂", Beat.XylemRide => "Xylem Ride",
            Beat.EnterLeaf => "Enter Leaf", Beat.PlantCell => "Plant Cell", Beat.FinalPlant => "Final Working Plant", Beat.FreeExplore => "Free Explore",
            _ => b.ToString()
        };

        public void BuildDebug(Beat[] beats, Action<Beat> onJump, Action<bool> onSkipVO, Action<float> onTimeScale, Action onUnlockAll)
        {
            dbgBeats = beats; dbgJump = onJump; dbgSkip = onSkipVO; dbgTs = onTimeScale; dbgUnlock = onUnlockAll;
            debugChips.Clear(); beatChips.Clear();
            if (beats == null) return;
            foreach (var b in beats)
            {
                var beat = b;
                var chip = Stadium(new VisualElement()); chip.AddToClassList("ps-chip"); chip.Add(Txt(BeatName(b), TS.Small, Text));
                debugChips.Add(Click(chip, () => Jump(beat)));
                beatChips[b] = chip;
            }
            if (Module.I) HighlightBeat(Module.I.Beat);
        }

        void Jump(Beat b) { CloseAllSystem(); dbgJump?.Invoke(b); }

        void HighlightBeat(Beat b) { foreach (var kv in beatChips) kv.Value.EnableInClassList("ps-chip--on", kv.Key == b); }

        public void ToggleDebug() => Show(debugPanel, !IsShown(debugPanel));

        public void SetStats(string text) { if (statsLabel.text != text) statsLabel.text = text; }
    }
}

namespace Photosynthesis
{
    public partial class Hud
    {
        VisualElement startBanner, startButton;
        public void ShowStartBanner(Action start)
        {
            HideTransient(); HideLine(); SetCinematic(false); SetHudVisible(false); SetProgressVisible(false); SetAskAIVisible(false); SetZoomControlVisible(false);
            if (startBanner != null) startBanner.RemoveFromHierarchy();
            startBanner = Layer(overlayLayer); startBanner.name = "Start Investigation banner";
            startBanner.pickingMode = PickingMode.Position;
            var shade = new VisualElement { pickingMode = PickingMode.Ignore }; Abs(shade, 0, 0, float.NaN, 0); shade.style.width = Length.Percent(43); Bg(shade, AG(Scene, 0.76f)); startBanner.Add(shade);
            var col = new FlexBox(false, 22); col.name = "Photosynthesis title and actions";
            Abs(col, 72, float.NaN); col.style.top = Length.Percent(17); col.style.width = Length.Percent(43); col.style.maxWidth = 760;
            startBanner.Add(col);
            var over = Txt("DR. CURIO'S SCIENCE ADVENTURE", TS.Readout, GreenBright); over.style.fontSize = 18; over.style.alignSelf = Align.FlexStart;
            Pad(over, 9, 16); Radius(over, 20); Border(over, A(GreenBright, .75f), 1); Bg(over, AG(GreenDeep, .7f)); col.Put(over);
            var title = Txt("PHOTOSYNTHESIS", TS.H1, Text); Font(title, true); title.style.fontSize = 72; title.style.letterSpacing = -1.6f; title.style.whiteSpace = WhiteSpace.Normal; col.Put(title); title.style.marginTop = 76;
            var sub = Txt("THE HIDDEN FOOD FACTORY", TS.H1, GreenBright); sub.style.fontSize = 30; sub.style.whiteSpace = WhiteSpace.Normal; col.Put(sub);
            var hook = Txt("Discover how plants make their own food.", TS.Body, TextSecondary); hook.style.fontSize = 23; hook.style.whiteSpace = WhiteSpace.Normal; col.Put(hook);
            startButton = Btn("START INVESTIGATION", ButtonKind.Primary, IconId.Play, start); startButton.name = "Start Investigation";
            startButton.style.height = 84; startButton.style.width = 545; startButton.style.maxWidth = Length.Percent(100); startButton.style.minWidth = 360; startButton.style.justifyContent = Justify.Center;
            var play = startButton.Q<IconElement>(); if (play != null) { play.RemoveFromHierarchy(); play.style.width = 30; play.style.height = 30; play.style.marginLeft = 0; play.style.marginRight = 18; startButton.Insert(1, play); }
            foreach (var label in startButton.Query<Label>().ToList()) label.style.fontSize = 25;
            col.Put(startButton);
            startButton.style.marginTop = 72;
            var row = col.Put(new FlexBox(true, 16));
            row.Put(Btn("How to Play", ButtonKind.Secondary, IconId.Help, () => OpenSystem(helpPanel))).style.minHeight = 52;
            row.Put(Btn("Settings", ButtonKind.Secondary, IconId.Settings, () => OpenSystem(settingsPanel))).style.minHeight = 52;
            var footer = Txt("A greener world starts with one discovery.", TS.Body, TextSecondary); footer.style.fontSize = 20; Abs(footer, 76, float.NaN, float.NaN, 70); startBanner.Add(footer);
            FocusSoon(startButton); root.AddToClassList(Kbd);
        }

        public void DismissStartBanner()
        {
            if (startBanner == null) return;
            startButton.SetEnabled(false); startBanner.pickingMode = PickingMode.Ignore;
            startBanner.style.opacity = 0; startBanner.style.display = DisplayStyle.None;
        }

        public void ReviewStartButton()
        {
            using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = startButton; startButton.SendEvent(submit); }
        }
        public void ReviewStartSettings(bool help) => OpenSystem(help ? helpPanel : settingsPanel);
        public void ReviewCloseSystem() => CloseAllSystem();
    }
}
