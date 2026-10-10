# UI spec — source of truth: Figma "Photosynthesis — Hidden Food Factory · UI System"
File: https://www.figma.com/design/suKcNjnLh8gigaytOygbBs · pages "🧩 Design System" (tokens, 43 icons, 22 component sets)
and "🖥 UI Sheet" (26 screens, 1920×1080). Do NOT call Figma read tools (rate-limited plan); this document + screenshots are the spec.

Screenshots (open with an image viewer / Read tool):
* Components: `C:/Users/ADMIN/.claude/projects/D--AI-unity-project-AI-PHOTOSYNTHESIS/08400774-4b85-4650-a052-6db228df9b07/tool-results/mcp-34fbe002-2dfd-44f5-8a2b-8fe16735db19-blob-1791291764934-n0852w.png`
* Screens A (story/HUD 01–08): `…/tool-results/mcp-34fbe002-2dfd-44f5-8a2b-8fe16735db19-blob-1791292143831-6lgwjs.png`
* Screens B (Ask AI 09–10): `…/tool-results/mcp-34fbe002-2dfd-44f5-8a2b-8fe16735db19-blob-1791292143831-lptnxj.png`
* Screens C (learning 11–15): `…/tool-results/mcp-34fbe002-2dfd-44f5-8a2b-8fe16735db19-blob-1791292402607-dc1ocw.png`
* Screens D (assessment 16–19): `…/tool-results/mcp-34fbe002-2dfd-44f5-8a2b-8fe16735db19-blob-1791292402607-fjxp4h.png`
* Screens E (explore/system 20–26): `…/tool-results/mcp-34fbe002-2dfd-44f5-8a2b-8fe16735db19-blob-1791292591427-t5aiq2.png`
* Client UI reference board: `C:/Users/ADMIN/AppData/Local/Temp/claude/D--AI-unity-project-AI-PHOTOSYNTHESIS/08400774-4b85-4650-a052-6db228df9b07/images/2.webp`

## Panel settings
Reference resolution 1920×1080, ScaleWithScreenSize, match 0.5 → `Resources/PhotosynthesisPanel` (PanelSettings, created by the
builder; create one at runtime with the same values if missing, theme `Resources/PhotosynthesisTheme`).
Runtime UI-scale floor 0.8, logical canvas ≥ 1600×900 (Hud.FitUiScale on a per-run copy; deliberate deviation so 720p text stays legible).

## Tokens (also `Palette` in Types.cs)
bg/scene #06120E · surface/glass #0A1C17 · surface/raised #123229 · surface/sunken #050E0B · stroke/glass #7FE6CF
brand/green #1F8A55 · green-bright #4BE08A · green-deep #0F4D33 · accent/cyan #3DD6E8 · gold #F5C451 · gold-deep #C98D16
text/primary #FFFFFF · secondary #CFE5DD · muted #86A39A · on-gold #1E1505 · correct #4CD68E · incorrect #FF6B6B · locked #5E6E69
science: water #45A8FF · co2 #A9BACB · oxygen #9BE7FF · glucose #FFB547 · xylem #3D9BFF · phloem #FF9F43 · light #FFD866
Spacing 4/8/12/16/24/32 · radius sm 8 · md 12 · lg 16 · panel 18–20 · pill 999 (runtime: Kit.Stadium re-fits it to height/2, since UI Toolkit clamps 999 per axis into an ellipse).
Translucency used: glass panel fill = surface/glass @ 84%, border = stroke/glass @ 25% 1px; raised chips @ 55–80%.
Linear project: dark-fill alphas pass through Kit.GA (1-(1-a)^2.2); Figma values stay the tokens (Hud.uss holds the precomputed GA values).

## Typography (fonts in `Resources/Fonts`: Inter-Regular.ttf, Inter-SemiBold.ttf; Cinzel-SemiBold.ttf for Display when present)
| Style | Size | Weight | Extra |
|---|---|---|---|
| Display | 48 | Cinzel SemiBold (fallback Inter SemiBold) | UPPERCASE, letter-spacing 8% (3.8px, Kit.DisplaySpacing) — gold, dark drop shadow |
| H1 | 28 | SemiBold | |
| H2 | 20 | SemiBold | |
| H3 | 16 | SemiBold | |
| Body | 15 | Regular | line-height 145% |
| Body Strong | 15 | SemiBold | |
| Small | 14 | Regular | (Figma 13; +1 so explanations stay ≥ 11 px at the 0.8 floor) |
| Label | 12 | SemiBold | UPPERCASE, letter-spacing 10% (Figma 11; deliberate deviation for the 720p floor) |
| Button | 14 | SemiBold | |
| Caption | 13 | SemiBold | letter-spacing 2% (Figma 12; deliberate deviation for the 720p floor) |
| Readout | 18 | SemiBold | UPPERCASE, letter-spacing 8% (scanner readouts, world callout titles) |
Chemistry: write the Unicode subscripts (₂ ₆ ₁). Kit.Set redraws them as 75% digits for text ≤ 18 px (Kit.Chem); larger text keeps Inter's glyphs.

## Effects
Glass Panel: drop shadow 0/12/32 black 45% + background blur 24 (UI Toolkit: emulate with fill + border; no blur).
Glows (cyan/green/gold/red): 0/0/18 colour @55% — emulate with a 2–4px border in the glow colour + a soft outer element at low opacity.

## Components (exact sizes from Figma)
* **Button** pill radius 999, padding 11×22, gap 8, label Button style, optional 18px trailing icon.
  Primary: fill green @92% + stroke green-bright @90% 1.5px · Secondary: raised @80% + stroke glass @35% · Gold: gold fill, on-gold text · Ghost: no fill, stroke glass @30%, text secondary.
  Hover: stroke cyan @90% + glow · Pressed: darker fill (green-deep / sunken) · Disabled: 40% opacity.
* **Icon Button** (Menu/Reset/Help) 64 wide, padding 10×8, radius 14, icon 22 + Caption label; glass @82%, stroke glass @25%; hover cyan stroke.
* **AI Command Chip** pill padding 9×14, Small text, raised @60%, stroke glass @30% (hover cyan).
* **Status Pill** padding 5×12, icon 14 + Label: Locked (locked @35%, muted) · Available (cyan @14%, cyan) · Discovered (green @95%, white, check) · In progress (gold).
* **Clue Card** 172 wide, padding 18×14, radius 18, gap 10: art 72 (leaf+CO₂ / water drop / sun), "CLUE 01" Label, name Body Strong, status pill. Until discovered the art slot shows a 64 px '?' disc and the name stays "???" (Locked: locked @30% fill, muted icon; Available: cyan @12% fill, cyan 1.5 stroke, pulsing). Locked: stroke glass @14%, pill without rim. Available: cyan stroke 1.5 + cyan glow @75% (pulsing). Discovered: clue-colour stroke 2 + glow @30% (CO₂ green-bright · water #45A8FF · sunlight gold).
* **Progress Item** 236 wide, padding 8×12, radius 12, gap 12: 32px icon ring + label (Body Strong) + trailing icon (lock / chevron while the hub waits for a pick, target otherwise / check); rows (and clue cards) take clicks only in the hub. Active: green @85% + green-bright stroke + glow.
* **Investigation Progress panel**: glass panel padding 16 gap 8, title Label "INVESTIGATION PROGRESS" (text/secondary), 3 progress items: Carbon Dioxide (CO2 icon), Water (Drop), Sunlight (Sun). Position x=1612 y=140 (right edge 40).
* **Current Investigation** (TL x40 y36): glass, padding 14/22/14/14, 48px icon ring (green-bright @16% + stroke) + Label "CURRENT INVESTIGATION" (text/secondary) / H3 title / Small subtitle.
* **Top actions** (TR x1672 y32): Menu · Reset · Help icon buttons, gap 8. Cinematic shots: top actions + Ask AI recede to 80% / 0.92 scale (full on hover or while Ask AI is live).
* **Dialogue Panel** 780 wide, radius 20, padding 14/18/14/14, gap 16, glass: avatar circle 68 (raised fill, cyan stroke 2) + column (speaker H3 cyan, line 18 Regular (Large 22), w 580) + 40px "next" circle (cyan stroke, chevron). Bottom-centre x=570, bottom margin 40.
* **Instruction** 560 wide, radius 16, stroke cyan @45%: 44px target ring + Body Strong text (no chevron, not interactive). Sits above the dialogue (y≈820) or at the bottom when no dialogue.
* **CC Toggle** BL x40 y992: CC box 32 (green when on) + "Subtitles On/Off" Caption.
* **Ask AI Button** BR: orb 88 (glass @92%, ring 3px state colour + glow) with Dr. Curio avatar 70, pill tag below overlapping by 14px ("ASK AI"). States: Closed cyan · Hover green-bright · Listening red #FF6B6B + mic · Transcribing cyan · Thinking gold · Speaking green.
* **Ask AI panel** 460 wide, padding 18, gap 14, glass, above the orb: header (avatar 48 + "Ask Dr. Curio" H2 + "Botanist · Investigator · AI companion" Caption + close), message bubbles (Curio: raised @75% + cyan @35% stroke, left; learner: green @85%, right; Small text, max 330 wide), gold World Action pill ("WORLD ACTION · …"), wrap chips, input row (pill text field sunken @90% + 44px mic circle cyan stroke + 44px send circle cyan fill). While open the progress panel hides.
* **Tooltip** 400 wide, padding 12, gap 14, stroke green-bright @45%: 84px thumb (green-deep, icon 44) + title H3 green-bright + Small body + "ⓘ Click to learn more" Caption muted.
* **Info Card** 380 wide, padding 20, gap 14: header (H2 + close 32), visual 340×150 (green-deep @90%, big icon 96), Body secondary text. Slides in at right.
* **Scanner Readout** 460 wide, radius 16, padding 14/18/14/14, sunken @88%, stroke state colour: 48px scan ring + Label "DR. CURIO'S SCANNER" + Readout (state colour) + Small detail + 360×6 progress. Scanning cyan · Detected gold · Complete green #4CD68E. Docks in the right column under the progress panel (or in its slot when progress is hidden); top-centre fallback while the chat or an info card takes that column.
* **Answer Option** 540 wide, radius 14, padding 10/14/10/10: 30px letter badge + Body answer (430 wide) + result icon. Default raised @55% · Hover cyan stroke · Selected green @70% + green-bright stroke (badge filled) · Correct correct @22% + correct stroke + check · Incorrect incorrect @18% + red stroke + ✕ · Disabled 50%.
* **Question Card** 620 wide, padding 24, gap 16 at x120 y170: header ("QUESTION n OF m" Label + gold pill e.g. "RECALL · ADAPTIVE"), Curio lead (Small cyan with 32px avatar), question H2 (572 wide), options A–D, footer (Ghost "Ask Dr. Curio for a hint" + Primary "Submit answer ›").
* **Feedback — Correct** 580 wide, padding 28, correct stroke 2 + green glow, in the question card's slot (x120 y170): 56px check ring + "Correct!" H1 green + Small; Body; visual recap 524×128 (the concept's 3-step path, last step gold); buttons (Secondary "See the journey again" + Primary "Continue ›").
* **Feedback — Reteach** 480 wide at x40 y260, red stroke 2 + red glow: replay ring + "Not quite — watch again" H2 red; Small; step pills (done ✓ green, current cyan ▶, todo muted); progress bar; Secondary "Watch again" + Primary "Try again". After a second miss: answer card (Feedback, red) with letter title ("The answer is B"), answer body, recap strip, Secondary "See it again" + Primary "Continue".
* **World Callout**: 12px anchor dot + 48×2 leader + tag (sunken @85%, stroke tone 1.5, radius 12, padding 8×14): Readout title + Small sub. Tones cyan/gold/green/blue/orange. The ring on the dot pulses; a new tag's leader draws out from the dot (0 → 48 px, 0.45 s ease-out-cubic, `.ps-leader`; instant under Reduce motion). The tag sits to the right of the dot and flips inward past 0.64 of the width, unless the caller forces a side (`Hud.Callout(..., left: true/false)`).
* **Magnification** segmented pill (1× · 10× · 100×), selected segment cyan @22% + cyan stroke, top-centre y40.
* **Zoom Path** breadcrumb: Whole Plant › Leaf › Cell › Chloroplast; current step green @90%.
* **Zoom Control** left x40 y420: glass radius 16, zoom icon, + and − 36px circles, "Zoom" caption.
* **Equation**: Display "PHOTOSYNTHESIS" (gold) y190; token row (150-wide tokens: molecule art 64 + formula H2 in science colour + Label name) 6CO₂ + 6H₂O → [Light Energy art 56 + "LIGHT ENERGY" Readout light + "absorbed by chlorophyll" Small + 240px arrow] → C₆H₁₂O₆ + 6O₂ (balanced), y330; word equation H1 + note (Body secondary) on one feathered scene @60% plate, y≈626. Highways beat: EquationCompact lifts the token row 150 px and fades the text.
* **Xylem vs Phloem toggle**: glass pill, 3 segments (Show Xylem · Show Phloem · Show Both — selected green), top-centre y140; legend card left x40 y300 (blue ↑ xylem, orange ↓ phloem).
* **Glucose uses**: 'FOOD MADE' gold pill (sunken fill @95, gold glow, check icon only) stacked 12 px above 3 glass cards (Energy · Growth · Storage, glucose-orange icon rings), one bottom-centre column that rides above the dialogue stack (RelayoutBottom).
* **In/Out badges**: left "CO₂ → IN" (cyan, arrow down) / right "O₂ → OUT" (#9BE7FF, arrow up) glass cards with molecule art; caption H1 "Same doorway. Different journey." on a feathered sunken @85% stadium plate, y≈786; it yields while an instruction or button is up.
* **Investigation Complete** 620 wide, gold stroke 2 + gold glow, centred y150: sun art 96, Display "CASE SOLVED", Body, 3 discovered progress items, chips of concepts, Caption score, buttons Gold "Free Explore" + Ghost "Exit".
* **Free Explore bar** 1240 wide bottom-centre: header (H3 "Choose a part of the plant" + Small hint), 9 Explore Tiles (92 wide, 60px tile radius 16 + Caption label; selected green), "FOLLOW" row of Secondary buttons (water/CO₂/oxygen/food; the active one green, like a selected tile). Exit Ghost button under top actions.
* **Pause** 400 wide (pause ring, "Paused" H1, full-width buttons Resume/Settings/Help/Restart/Exit) · **Settings** 600 wide (tabs Audio/Display/Accessibility; rows label + slider/toggle/dropdown: Music, Effects, Dr. Curio voice, Subtitles, Subtitle size, Graphics quality, Fullscreen, Reduce motion, Microphone; footer Ghost "Reset progress" + Primary "Done") · **Help** 640 wide (control rows, incl. "Answer a question · 1–4 · Enter") · **Reset confirm** 480 wide gold (Cancel + Gold Restart) · **Exit confirm** 480 wide gold (exit icon ring; Secondary "Exit" + Primary "Keep investigating").
* **Loading/transition**: dim, ring of gold particles, Label cyan "ENTERING THE LEAF · LEAF → TISSUE → CELL", Display title, progress bar, fun fact.
* **Debug panel** 560 wide at x40 y150, gold stroke: bug icon + "Developer debug" + "DEV BUILD · F1" pill; "JUMP TO BEAT" wrap chips; toggles Skip VO / Show FPS; time-scale slider; stats caption; Reload scene / Unlock all clues.
* **Clue unlocked** reward card centred y≈230: clue art 150, Readout "CLUE 0X UNLOCKED!", H1 "WATER ✓", Body, Gold button — with gold particle sparkle.

## Motion
Panels: opacity 0→1 + translate 12–16px + scale 0.97→1 over 0.22–0.3s (USS transitions: `transition-property: opacity, translate, scale; transition-duration: 0.25s; transition-timing-function: ease-out-cubic`).
Unlock: card pop (scale 0.8→1.05→1), glow pulse, sparkle. Ask AI orb: idle breathing ring; listening level bars.
Dialogue text: quick typewriter reveal (≈60 chars/s).

## Icons (24×24 viewBox, stroke 1.8 white, round caps/joins) — redraw with Painter2D
Menu M4 7h16M4 12h16M4 17h16 · Reset arc r8 + arrow M20 4v5h-5 · Help circle r9 + ? · Close M6 6l12 12M18 6L6 18 ·
Mic rect 9,3 6×11 r3 + M5.5 11a6.5 6.5 0 0 0 13 0 M12 17.5V21 · Send M5 12h14M13 6l6 6-6 6 · Chevron M9 6l6 6-6 6 ·
Plus/Minus · Zoom circle(11,11,6.5)+M20 20l-4.3-4.3 · Check M5 12.5l4.5 4.5L19 7 · Lock rect 5,11 14×10 r2.5 + shackle ·
Leaf M5 19C5 10 11 5 20 4c-1 9-6 15-15 15z + midrib · Drop M12 3s6 6.6 6 11a6 6 0 0 1-12 0c0-4.4 6-11 6-11z · Sun circle r4 + 8 rays ·
CO2 three circles (4.6,12,r2.2)(12,12,r3.2)(19.4,12,r2.2) · O2 two circles r4 · Glucose hexagon + centre dot · Scan corner brackets + mid line ·
Settings two sliders · Pause/Play · Replay (reset mirrored) · Info · Sparkle 4-point star · CC rounded rect + two C arcs · Eye ·
Plant/Root/Stem/Xylem(up arrow in tube)/Phloem(down arrow in tube)/Stomata(two ellipses)/Cell/Chloroplast(ellipse + bars)/Energy(bolt)/Target/ArrowUp/ArrowDown/Bug · Exit M10 4H6a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h4 M15 8l4 4-4 4 M19 12H9 (leaving through a door frame).
Dr. Curio avatar: white→#BFD3CE radial head r22, dark visor rect (14.5,26 35×20 r10) #0A1A1F, cyan eyes (ellipse rx3.4 ry4.6), white glints,
leaf sprout antenna (green gradient), gold-rimmed lens r6.5 at (47,44).

## Finale + end menu (added 2026-10-07; Figma write access was unavailable — View seat only — so this section is the design record)
* **Finale labels**: five World Callouts on the working plant during a 15 s crane: WATER "up from the roots" (blue, root tip),
  CARBON DIOXIDE "in from the air" (cyan, leaf underside), SUNLIGHT "light energy" (gold, crown), then GLUCOSE "food, made in the leaves"
  (orange, stem) and OXYGEN "out to the air" (green, leaf); cleared before the title.
* **Finale title** (Hud.Title with top 80, dim 0.1): big "THIS IS PHOTOSYNTHESIS" (Display, gold, dark plate as the other titles) in the
  upper third above the crown; small line "Light energy turns water and carbon dioxide into glucose and oxygen" at 22 px in text/primary
  (finale only; other titles keep Body 15 text/secondary). Spoken together with Dr. Curio's "This is photosynthesis!" (no subtitle bar).
  Fades in 1.2 s, holds 11 s. Cinematic HUD mode; Ask AI hidden; Menu/Reset/Help hide when the 3.2 s fade to black starts. The fade
  layer sits under the title layer, so the title stays alone on black for ~2 s, then fades.
* **End menu** (HudLearning.EndMenu, overlay layer, centred, top 330): glass panel 560 wide, radius 20, fill surface/glass @94 %,
  stroke gold @80 % 1.5 px, soft gold glow; padding 32; column gap 18, centred: Art/ClueSun 80 px, H1 "Investigation complete" (gold),
  Body (text/secondary, 480 wide, centred) "You caught a leaf making its own food. Watch it again, keep exploring the plant, or leave.",
  button row gap 10: **Replay** (Gold, Replay icon) · **Review the plant** (Secondary, Eye icon, initial focus) · **Exit** (Ghost, Exit
  icon, opens the same exit confirm as every other Exit). No narration, no score. Replay restarts at Scene 2; Review opens Free Explore
  (which now also has a Secondary "Factory controls" button in the Follow row, opening the Scene 14 switches panel; its continue button
  reads "Back to the plant").
* **Prologue** (Scene 3): cinematic HUD mode from the dive through the leaf until the clue cards appear; no new elements.
* **Hub button**: after ~22 s in a clue hub, the standard big Gold button (bottom slot) "OPEN CLUE 0N" with the Target icon.

## Stomata close-up (2026-10-10; Figma still View-only, so this is the design record)
Scene 4 at 100×: the learner must be able to read what is being explained while the stoma fills the frame, with the centre of the
screen left to the anatomy.
* **Reading card**: the standard Info Card (380 wide, padding 20) in the progress panel's slot (right 40, top 140): H2 "Stomata —" /
  "Tiny Gateways of the Leaf" (an explicit break after the dash), Stomata icon in the 340×150 visual, Body "Stomata are tiny openings on
  the leaf surface that allow carbon dioxide to enter and regulate gas exchange." About 323 px tall, so it ends near y 463: under the
  progress panel (top 380) it would end near 703 and cover Dr. Curio's head in companion mode (head top ≈ 0.634 of the canvas height:
  685 at 1080, 571 on the 1600×900 floor). The progress panel steps aside while the card is up (`Hud.ShowInfo(..., slot: true)`) and
  slides back when it goes. The card fades in (`.ps-anim`, from the right) as Dr. Curio says "These tiny openings are called stomata."
  and fades out just before the CLUE 01 UNLOCKED reward card (it would otherwise answer Q1); the tracker is back in time for the clue's
  pop. The learner may close it earlier with its × button, which brings the tracker straight back. While the Ask AI chat is open the
  card steps aside like the tracker it replaces (moved beside the chat it would cover the stoma's labels) and returns when the chat closes.
* **Labels** (World Callouts on the hero stoma): STOMATAL PORE (green, sub "One stoma · plural: stomata") anchored 1 m along the slit at
  its left tip with the tag forced to the LEFT, shown as the 10× → 100× zoom begins so it rides the zoom and points at the pore throughout
  (the Ask-AI "show me CO₂ / stomata" action uses the same anchor and side; the Q1/Q2 replay mirrors it to the slit's right tip with the
  tag to the right, because the reteach card is up on the left, x 40–520); GUARD CELLS (cyan, "Open and close the
  pore") on the far guard cell's crest, tag to the right, as Dr. Curio says "Found something."; CO₂ ENTERING (cyan, "Carbon dioxide from
  the air") riding the highlighted molecule from the air, through the pore, into the cavity, gone when the 4 s entry ends so the pore
  centre stays clear. The pore and guard-cell labels clear with the reward card.
* The glowing CO₂ (halo + trail), the pulsing guard-cell rim and the ambient CO₂ are the existing Scene 4 effects, unchanged.

## Touch sizes (2026-10-08)
Zoom ± 48 (icon 22) · dialogue › 48 · close buttons 44 · `.ps-seg` padding 13×18 (~44 tall; the breadcrumb sits at top 44 and the
magnification pill below it at 112) · `.ps-chip` 13×16 · `.ps-drop` 13×12 · slider hit area 240×44 · Factory controls: the whole row is the
target (min height 48), the 46×26 switch only shows the state. Reading cards (question, reteach, legend, Factory controls, info card) are
opaque (no blur in UI Toolkit, so bright 3D signs would ghost through text).
