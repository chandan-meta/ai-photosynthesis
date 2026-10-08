using System;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

namespace Photosynthesis
{
    /// Figma text styles (Resources/Fonts Inter Regular + SemiBold).
    public enum TS { Display, H1, H2, H3, Body, BodyStrong, Small, Label, Button, Caption, Readout }

    /// Flex container with a fixed gap between in-flow children (UI Toolkit has no `gap`).
    public class FlexBox : VisualElement
    {
        readonly float gap; readonly bool row; int count;
        public FlexBox(bool row, float gap)
        {
            this.row = row; this.gap = gap;
            style.flexDirection = row ? FlexDirection.Row : FlexDirection.Column;
            if (row) style.alignItems = Align.Center;
        }
        public T Put<T>(T child) where T : VisualElement
        {
            if (count++ > 0) { if (row) child.style.marginLeft = gap; else child.style.marginTop = gap; }
            Add(child); return child;
        }
        public void Reset() { Clear(); count = 0; }
    }

    /// Element factory for the Figma component library ("🧩 Design System"). Colours/sizes are the Figma tokens;
    /// hover/pressed states live in Resources/UI/Hud.uss (inline styles would override pseudo-classes).
    static class Kit
    {
        public const string Off = "ps-off", Anim = "ps-anim", Scrim = "ps-scrim";
        public const float DisplaySpacing = 3.8f;   // 8% of 48
        static readonly object UpperTag = new object();
        static bool fontsLoaded; static Font reg, semi, display;

        public static Color A(Color c, float a) { c.a = a; return c; }

        /// Figma blends in sRGB; this project's UI blends in linear (PanelSettings.forceGammaRendering is ignored without a targetTexture).
        /// Maps a Figma alpha of a DARK fill (glass/sunken/raised/scene/dim) to the alpha that gives the same pixel; the slight overshoot
        /// on bright backdrops stands in for the missing background blur 24.
        public static float GA(float a) => QualitySettings.activeColorSpace == ColorSpace.Linear ? 1f - Mathf.Pow(1f - Mathf.Clamp01(a), 2.2f) : a;
        public static Color AG(Color c, float a) { c.a = GA(a); return c; }

        static void LoadFonts()
        {
            if (fontsLoaded) return; fontsLoaded = true;
            reg = Resources.Load<Font>("Fonts/Inter-Regular"); semi = Resources.Load<Font>("Fonts/Inter-SemiBold");
            display = Resources.Load<Font>("Fonts/Cinzel-SemiBold");   // optional (SIL OFL): Display falls back to Inter SemiBold
        }

        public static void Font(VisualElement e, bool semiBold)
        {
            LoadFonts(); var f = semiBold ? semi : reg;
            if (f) e.style.unityFontDefinition = FontDefinition.FromFont(f);
        }

        public static Label Txt(string text, TS t, Color? color = null, float width = 0f)
        {
            var l = new Label { pickingMode = PickingMode.Ignore, enableRichText = true };
            var s = l.style;
            s.marginLeft = s.marginRight = s.marginTop = s.marginBottom = 0;
            s.paddingLeft = s.paddingRight = s.paddingTop = s.paddingBottom = 0;
            float size = 15, spacing = 0; bool sb = false, upper = false;
            switch (t)
            {
                case TS.Display: size = 48; sb = true; spacing = DisplaySpacing; upper = true; break;
                case TS.H1: size = 28; sb = true; spacing = -0.28f; break;
                case TS.H2: size = 20; sb = true; spacing = -0.1f; break;
                case TS.H3: size = 16; sb = true; break;
                case TS.BodyStrong: sb = true; break;
                case TS.Small: size = 13; break;
                case TS.Label: size = 12; sb = true; spacing = 1.1f; upper = true; break;     // 12/13 (Figma 11/12): ≥9.6 px at the 720p scale floor
                case TS.Button: size = 14; sb = true; spacing = 0.14f; break;
                case TS.Caption: size = 13; sb = true; spacing = 0.24f; break;
                case TS.Readout: size = 18; sb = true; spacing = 1.44f; upper = true; break;
            }
            s.fontSize = size; s.letterSpacing = spacing; Font(l, sb);
            if (t == TS.Display && display) s.unityFontDefinition = FontDefinition.FromFont(display);   // Cinzel SemiBold when present (Font() loaded it)
            if (color.HasValue) s.color = color.Value;
            if (width > 0) { s.width = width; s.whiteSpace = WhiteSpace.Normal; text = Prose(text); }
            if (upper) l.userData = UpperTag;
            Set(l, text);
            return l;
        }

        const float ChemMaxPx = 18f;   // H2+ (equation tokens 20 px) keeps Inter's own subscript glyphs
        static readonly Regex SubRun = new Regex("[₀-₉]+");
        static string SubDigits(Match m)
        {
            var d = m.Value.ToCharArray();
            for (int i = 0; i < d.Length; i++) d[i] = (char)(d[i] - '₀' + '0');
            return "<voffset=-0.12em><size=75%>" + new string(d) + "</size></voffset>";
        }
        /// Inter's Unicode subscripts are 0.45 em tall (6 px in 13 px text at 1080p, 4 px at 720p). This redraws them as plain
        /// digits at 75% (0.56 em, 25% taller), dropped 0.12 em. The smaller size keeps the line's top edge where it was.
        public static string Chem(string s) => string.IsNullOrEmpty(s) ? s : SubRun.Replace(s, SubDigits);

        /// Sets label text honouring the style's UPPERCASE rule; formula subscripts in ≤18 px text are redrawn legibly (Chem).
        public static void Set(Label l, string text)
        {
            if (l.userData == UpperTag) text = text?.ToUpperInvariant();       // uppercase before adding tags
            l.text = l.style.fontSize.value.value <= ChemMaxPx ? Chem(text) : text;
        }

        /// Typographic glue for wrapped copy (TextCore never breaks at U+00A0): keeps "Dr. Curio" whole and joins a short
        /// final word to the word before it, so a paragraph never ends on a one-word line.
        public static string Prose(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.Replace("Dr. ", "Dr.\u00A0");
            int i = s.LastIndexOf(' ');
            if (i > 0 && s.Length - i <= 14 && s.IndexOf('>', i) < 0) s = s.Substring(0, i) + "\u00A0" + s.Substring(i + 1);
            return s;
        }

        public static Label WrapText(Label l) { l.style.whiteSpace = WhiteSpace.Normal; return l; }

        // ------------------------------------------------------------------ style shorthands
        public static T Pad<T>(T e, float t, float r, float b, float l) where T : VisualElement
        { var s = e.style; s.paddingTop = t; s.paddingRight = r; s.paddingBottom = b; s.paddingLeft = l; return e; }
        public static T Pad<T>(T e, float v, float h) where T : VisualElement => Pad(e, v, h, v, h);

        /// r >= 999 means a fully round pill. UI Toolkit clamps the x and y radii separately (to w/2 and h/2), which gives an ellipse,
        /// so pills are re-fitted to height/2 after every layout change. This gives a true Figma stadium.
        public static T Radius<T>(T e, float r) where T : VisualElement
        {
            SetRadius(e, r);
            if (r >= 999f) Stadium(e);
            return e;
        }
        static void SetRadius(VisualElement e, float r)
        { var s = e.style; s.borderTopLeftRadius = s.borderTopRightRadius = s.borderBottomLeftRadius = s.borderBottomRightRadius = r; }

        /// Corner radius = half the laid-out height, re-applied on every layout change.
        /// The callback is a static method, so registering it twice is deduplicated.
        public static T Stadium<T>(T e) where T : VisualElement { e.RegisterCallback<GeometryChangedEvent>(FitStadium); return e; }
        static void FitStadium(GeometryChangedEvent ev)
        {
            float h = ev.newRect.height;
            if (h > 0f) SetRadius((VisualElement)ev.currentTarget, h * 0.5f);   // display:none gives 0, so the last value is kept
        }

        public static T Border<T>(T e, Color c, float w) where T : VisualElement
        {
            var s = e.style; s.borderTopWidth = s.borderRightWidth = s.borderBottomWidth = s.borderLeftWidth = w;
            BorderColor(e, c); return e;
        }

        /// null clears the inline colour so the USS class (and its :hover) applies again.
        public static void BorderColor(VisualElement e, Color? c)
        {
            var s = e.style;
            if (c.HasValue) s.borderTopColor = s.borderRightColor = s.borderBottomColor = s.borderLeftColor = c.Value;
            else s.borderTopColor = s.borderRightColor = s.borderBottomColor = s.borderLeftColor = StyleKeyword.Null;
        }

        public static T Bg<T>(T e, Color c) where T : VisualElement { e.style.backgroundColor = c; return e; }

        public static T Size<T>(T e, float w, float h = float.NaN) where T : VisualElement
        {
            if (!float.IsNaN(w)) e.style.width = w;
            if (!float.IsNaN(h)) e.style.height = h;
            return e;
        }

        public static T Abs<T>(T e, float left, float top, float right = float.NaN, float bottom = float.NaN) where T : VisualElement
        {
            var s = e.style; s.position = Position.Absolute;
            if (!float.IsNaN(left)) s.left = left;
            if (!float.IsNaN(top)) s.top = top;
            if (!float.IsNaN(right)) s.right = right;
            if (!float.IsNaN(bottom)) s.bottom = bottom;
            return e;
        }

        /// Full-width absolute row that centres its content horizontally (layout only — never picks).
        public static VisualElement CenterRow(VisualElement parent, float top, float bottom = float.NaN)
        {
            var r = Abs(new VisualElement { pickingMode = PickingMode.Ignore }, 0, top, 0, bottom);
            r.style.flexDirection = FlexDirection.Row; r.style.justifyContent = Justify.Center; r.style.alignItems = Align.FlexEnd;
            parent.Add(r); return r;
        }

        public static VisualElement Layer(VisualElement parent)
        {
            var l = Abs(new VisualElement { pickingMode = PickingMode.Ignore }, 0, 0, 0, 0);
            parent.Add(l); return l;
        }

        /// Panel that is hidden until Hud.Show (fade + 14px slide + 0.97 scale).
        public static T Hidden<T>(T e) where T : VisualElement
        {
            e.AddToClassList(Anim); e.AddToClassList(Off); e.style.display = DisplayStyle.None; return e;
        }

        // ------------------------------------------------------------------ components
        /// Figma "Glass Panel": surface/glass @84 + stroke/glass @25 1px + soft drop shadow. `alpha` is the Figma token (GA maps it for linear blending).
        public static VisualElement Panel(float radius = 18f, float alpha = 0.84f, Color? stroke = null, float strokeW = 1f)
        {
            var e = new VisualElement();
            Bg(e, A(Palette.Glass, GA(alpha))); Radius(e, radius); Border(e, stroke ?? A(Palette.GlassStroke, 0.25f), strokeW);
            e.Add(new GlowElement(Color.black, radius, 26f, GA(0.32f)) { Shadow = true });
            return e;
        }

        /// Coloured glow behind the element (inserted first so content draws over it). Replaces the panel's drop shadow.
        public static GlowElement Glow(VisualElement e, Color c, float radius, float blur = 18f, float alpha = 0.55f)
        {
            if (e.childCount > 0 && e[0] is GlowElement s && s.Shadow) e.RemoveAt(0);
            var g = new GlowElement(c, radius, blur, alpha); e.Insert(0, g); return g;
        }

        public static VisualElement Circle(float size, Color fill, Color stroke, float strokeW, VisualElement child = null)
        {
            var c = new VisualElement();
            Size(c, size, size); c.style.flexShrink = 0; Radius(c, size / 2); Bg(c, fill);
            if (strokeW > 0) Border(c, stroke, strokeW);
            c.style.alignItems = Align.Center; c.style.justifyContent = Justify.Center;
            if (child != null) c.Add(child);
            return c;
        }

        public static IconElement Icon(IconId id, float size, Color c) => new IconElement(id, size, c);

        public static void Play(Sfx s, float volume = 1f)
        {
            // While the pause menu holds AudioListener.pause, a started source stays paused and would fire in a burst on resume.
            // ponytail: menus are silent while paused; AudioDirector could mark its SFX voices ignoreListenerPause instead.
            var m = Module.I;
            if (m && m.audioDirector) m.audioDirector.Play(s, volume);
        }

        /// Click (Clickable manipulator → also drives :active) + hover tick. Children never steal the pointer.
        public static T Click<T>(T e, Action a, Sfx sfx = Sfx.UiClick) where T : VisualElement
        {
            // A panel that is fading out (.ps-off) no longer blocks the 3D view, so it must not take clicks either.
            e.AddManipulator(new Clickable(() => { if (Leaving(e)) return; Play(sfx, 0.8f); a?.Invoke(); }));
            // Keyboard: focusable, Enter (NavigationSubmit) presses it. Space is left to "Continue dialogue" (Dialogue.cs), so a
            // control focused by an earlier mouse click never fires a second time when the learner skips a line.
            e.focusable = true;
            e.RegisterCallback<NavigationSubmitEvent>(ev =>
            {
                ev.StopPropagation();
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && kb.spaceKey.isPressed) return;
                if (!e.enabledInHierarchy || e.pickingMode == PickingMode.Ignore || Leaving(e)) return;   // inert stays inert
                Play(sfx, 0.8f); a?.Invoke();
            });
            e.RegisterCallback<PointerEnterEvent>(_ => { if (e.enabledInHierarchy && !Leaving(e)) Play(Sfx.UiHover, 0.25f); });
            IgnoreChildren(e);
            return e;
        }

        static bool Leaving(VisualElement e)
        {
            bool cineHide = false;   // a ps-cine-hide subtree under a cinematic root is as gone as a fading .ps-off one
            for (; e != null; e = e.parent) { if (e.ClassListContains(Off)) return true; cineHide |= e.ClassListContains("ps-cine-hide"); if (cineHide && e.ClassListContains("ps-cine")) return true; }
            return false;
        }

        public static void IgnoreChildren(VisualElement e) { foreach (var c in e.Children()) { c.pickingMode = PickingMode.Ignore; IgnoreChildren(c); } }

        public static Color TextOn(ButtonKind k) => k == ButtonKind.Gold ? Palette.OnGold : k == ButtonKind.Ghost ? Palette.TextSecondary : Palette.Text;

        /// Figma "Button": pill, padding 11×22, Button text, optional 18px trailing icon; hover cyan stroke + glow.
        public static VisualElement Btn(string label, ButtonKind kind, IconId? icon, Action click)
        {
            var b = new VisualElement();
            b.AddToClassList("ps-btn"); b.AddToClassList("ps-btn--" + kind.ToString().ToLowerInvariant());
            var g = Glow(b, kind == ButtonKind.Gold ? Palette.Gold : kind == ButtonKind.Primary ? Palette.GreenBright : Palette.Cyan, 999f, 16f, 0.6f);
            g.AddToClassList("ps-hoverglow");
            b.Add(Txt(label, TS.Button, TextOn(kind)));
            if (icon.HasValue) { var i = Icon(icon.Value, 18, TextOn(kind)); i.style.marginLeft = 8; b.Add(i); }
            return Click(Stadium(b), click);
        }

        /// Figma "Icon Button" (Menu / Reset / Help): 64 wide, icon 22 + Caption.
        public static VisualElement Tile(IconId icon, string label, Action click)
        {
            var t = new VisualElement(); t.AddToClassList("ps-tile");
            Glow(t, Palette.Cyan, 14, 14, 0.5f).AddToClassList("ps-hoverglow");
            t.Add(Icon(icon, 22, Palette.Text));
            var l = Txt(label, TS.Caption, Palette.TextSecondary); l.style.marginTop = 4; t.Add(l);
            return Click(t, click);
        }

        /// Round icon button (zoom ±, dialogue next, close, mic).
        public static VisualElement Round(float size, IconId icon, float iconSize, Color iconColor, Action click, string extraClass = null)
        {
            var c = new VisualElement(); c.AddToClassList("ps-round"); if (extraClass != null) c.AddToClassList(extraClass);
            Size(c, size, size); Radius(c, size / 2); c.style.flexShrink = 0;
            c.Add(Icon(icon, iconSize, iconColor));
            return click != null ? Click(c, click) : c;
        }

        /// Figma "Status Pill" / generic tinted pill: padding 5×12, 14px icon + Label text.
        public static VisualElement Pill(string text, Color c, IconId? icon, float fillA = 0.14f, Color? textColor = null, Color? fill = null)
        {
            var p = new FlexBox(true, 6);
            Pad(p, 5, 12); Radius(p, 999); Bg(p, fill ?? A(c, fillA)); Border(p, A(c, 0.9f), 1);
            p.style.alignSelf = Align.FlexStart;
            if (icon.HasValue) p.Put(Icon(icon.Value, 14, textColor ?? c));
            p.Put(Txt(text, TS.Label, textColor ?? c));
            p.pickingMode = PickingMode.Ignore;
            return p;
        }

        /// 6px progress track + fill (Figma "Progress Bar").
        public static VisualElement Bar(float width, Color fillColor, out VisualElement fill)
        {
            var track = new VisualElement { pickingMode = PickingMode.Ignore };
            Size(track, width, 6); Radius(track, 3); Bg(track, Palette.Raised); track.style.overflow = Overflow.Hidden; track.style.flexShrink = 0;
            fill = new VisualElement { pickingMode = PickingMode.Ignore };
            Size(fill, 0, 6); Radius(fill, 3); Bg(fill, fillColor);
            track.Add(fill); return track;
        }
    }

    /// Figma "Toggle": 46×26 pill, 20px knob.
    public class PsToggle : VisualElement
    {
        readonly VisualElement knob; bool on; readonly Action<bool> changed;
        public PsToggle(bool value, Action<bool> changed)
        {
            this.changed = changed; AddToClassList("ps-toggle");
            Kit.Glow(this, Palette.Cyan, 13f, 10f, 0.6f).AddToClassList("ps-hoverglow");   // hover: cyan glow (no border, knob geometry unchanged)
            knob = new VisualElement { pickingMode = PickingMode.Ignore }; knob.AddToClassList("ps-knob"); Add(knob);
            Kit.Click(this, () => { Value = !on; this.changed?.Invoke(on); }, Sfx.UiSelect);
            Value = value;
        }
        public bool Value { get => on; set { on = value; EnableInClassList("ps-toggle--on", on); } }
    }

    /// Figma "Slider": 240 track, green fill, 18px knob. Drag anywhere on it (pointer captured).
    public class PsSlider : VisualElement
    {
        readonly VisualElement fill, knob; float v; readonly Action<float> changed; bool drag;
        const float W = 240f;
        public PsSlider(float value, Action<float> changed)
        {
            this.changed = changed; AddToClassList("ps-slider");
            Kit.Size(this, W, 20); style.flexShrink = 0; style.justifyContent = Justify.Center;
            var track = new VisualElement { pickingMode = PickingMode.Ignore }; Kit.Size(track, W, 6); Kit.Radius(track, 3); Kit.Bg(track, Palette.Raised);
            fill = new VisualElement { pickingMode = PickingMode.Ignore }; Kit.Size(fill, 0, 6); Kit.Radius(fill, 3); Kit.Bg(fill, Palette.GreenBright);
            track.Add(fill); Add(track);
            knob = new VisualElement { pickingMode = PickingMode.Ignore }; knob.AddToClassList("ps-slider-knob");
            Kit.Abs(knob, 0, 1); Kit.Size(knob, 18, 18); Kit.Radius(knob, 9); Kit.Bg(knob, Color.white); Add(knob);
            // no Clickable here, so :active never fires: the drag state is a class (.ps-slider--drag grows the knob)
            RegisterCallback<PointerDownEvent>(e => { drag = true; AddToClassList("ps-slider--drag"); this.CapturePointer(e.pointerId); Set(e.localPosition.x); });
            RegisterCallback<PointerMoveEvent>(e => { if (drag) Set(e.localPosition.x); });
            RegisterCallback<PointerUpEvent>(e => { if (!drag) return; drag = false; RemoveFromClassList("ps-slider--drag"); this.ReleasePointer(e.pointerId); Kit.Play(Sfx.UiClick, 0.5f); });
            RegisterCallback<PointerCaptureOutEvent>(_ => { drag = false; RemoveFromClassList("ps-slider--drag"); });
            Value = value;
        }
        void Set(float x) { Value = x / W; changed?.Invoke(v); }
        public float Value
        {
            get => v;
            set { v = Mathf.Clamp01(value); fill.style.width = v * W; knob.style.left = v * (W - 18); }
        }
    }
}
