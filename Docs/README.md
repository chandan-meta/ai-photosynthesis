# Photosynthesis — The Hidden Food Factory (Scene 2 onward)

Unity 6000.3.0f1 · URP · Input System · UI Toolkit. Scene 1 (rooftop) is out of scope and untouched.

## Open & play
1. Open the project in Unity 6000.3.0f1.
2. Open the existing `Assets/Photosynthesis/Scenes/Scene02_BotanicalChamber.unity`. **Build Scene regenerates this scene**; it is not needed to play or build the current polished project.
3. Press Play, then **Start Investigation** on the live greenhouse banner. The original module starts at **Scene 2 — A New World** (golden particles settle, the
   Botanical Investigation Chamber forms) and runs through Scene 15 (Free Explore).

Windows player: **Photosynthesis ▸ Build Windows Player (development)** → `Builds/Win/Photosynthesis.exe`.
Release player for schools (no watermark, no profiler, no F1 panel): **Photosynthesis ▸ Build Windows Player (release)** →
`Builds/WinRelease/Photosynthesis.exe` (batch: `-executeMethod Photosynthesis.EditorTools.PhotosynthesisBuild.BuildPlayerRelease`).
Leave the `*_BurstDebugInformation_DoNotShip` folder out of the zip given to schools.

## Controls
| Action | Input |
|---|---|
| Rotate / zoom / pan the plant | left-drag · wheel, HUD +/− or the **+**/**−** keys · right-drag |
| Select clue / continue | click the clue card or progress item · Next (›) or **Space** skips a line |
| Answer a question | click an option or press **1–4**, then Submit (or **Enter**) · **Tab** / arrows move keyboard focus, **Enter** presses the focused button |
| Inspect a plant part | click a leaf, the stem or the roots while you control the camera (tooltip + highlight) |
| Hands-on moments | zoom into the leaf (3 scroll notches / + / tap 10×) · zoom into one pore · pick a water molecule · click the **skylight** to let the sun in · click the CO₂ and water to feed the chloroplast · **Factory controls** switches (break the factory, watch FOOD MADE / FOOD DELIVERED) |
| Ask Dr. Curio | **ASK AI** button (type, tap a suggestion, or hold the mic / **M** to speak) |
| Subtitles | **C** or the CC toggle |
| Pause · Settings · Help · Restart | **Esc** / Menu · Reset · Help tiles (top-right) |
| Developer debug panel (dev builds/editor) | **F1** — jump to any beat, skip VO, time scale, unlock clues, FPS/draw-call stats |

## The learning flow (storyboard Scenes 2–15)
A New World → Meet Dr. Curio (learner scan) → three locked clues →
**Clue 01 CO₂** (giant leaf, 1×/10×/100× microscope, stomata & guard cells labelled with a reading card at 100×, CO₂ enters) →
**Clue 02 Water** (scanner sweep, signal below, transparent floor, roots, soil world, pick a water molecule, **xylem ride**) →
**Clue 03 Sunlight** (factory won't start, FOOD PRODUCTION 0, darkness, straight into the "what's missing?" question, then the learner
opens the skylight → sunbeam WHOOM) → Enter Leaf
(leaf → tissue → cell) → chloroplast (chlorophyll absorbs light; the learner feeds in CO₂ and water) → reaction (unknown molecule →
glucose C₆H₁₂O₆, uses: energy/growth/storage) →
oxygen (followed out through the same stoma: CO₂ in / O₂ out) → phloem (food to shoot/roots/flowers/fruits/storage; xylem vs
phloem toggle) → equation assembled from the discovered pieces → the whole working plant → **finale**: a slow crane rises over the
working plant while the relationship is labelled on it (WATER, CARBON DIOXIDE, SUNLIGHT go in; GLUCOSE, OXYGEN come out), Dr. Curio says
**"This is photosynthesis!"** as the title appears in the upper third, the processes settle, the plant turns solid, the music resolves on a
cadence and the picture fades to black under the title. That is the last educational statement; nothing mandatory follows. A quiet end menu offers **Replay · Review the plant · Exit**. Review = Free Explore (whole plant ↔
leaf ↔ cell ↔ chloroplast, follow water/CO₂/oxygen/food, plus the optional **Factory controls** experiment). The adaptive final
question set and the Case-solved card remain in the code (debug panel beats Assessment / Complete) but are no longer part of the flow.

**Opening (Scene 2–3):** the chamber forms around the learner (16 s crane-orbit, title), a 3 s free look, then Dr. Curio materialises
and reads his scan of the learner aloud. His briefing is shot as a cinematic prologue: hero plant reveal → sunlight lands on the lit upper
leaf → push to the leaf, which turns glassy → through the surface (warm flash) → the hidden world inside the leaf (the client's
cross-section model, continuing into green fog) lights up on "food is being made" → back out to the clue cards.

**Clue hubs:** hubs 2 and 3 open with a spoken cue; after ~12 s of quiet Dr. Curio nudges ("Psst! Tap the glowing clue card…") and after
~22 s a big gold OPEN CLUE button appears, so nobody waits in silence.

**Adaptive assessment:** storyboard bank Q1–Q20 (`Learning/Assessment.cs`). In-journey questions are gated by learner level
(struggling → recall + visual reinforcement; average → mix; high → application/reasoning, e.g. Q2, Q6, Q12). Wrong answers
trigger a 3D reteach (e.g. plant turns transparent and water replays root → xylem → leaf) and a retry; predictions never fail.
The final set (~5) is chosen from Q14–Q20 plus re-checks of missed concepts and re-plans if the level changes mid-set.

**Ask Dr. Curio (AI):** `Learning/AskAI.cs` + `IntentRouter` + `CurioKnowledge` (offline, context-aware) →
`Story.WorldAction(intent)` performs safe predefined world actions ("Show me water" → water investigation / xylem demo,
"Where does CO₂ enter?" → leaf underside + CO₂ flow, "Take me to the chloroplast", …) without ever breaking clue order.
Optional backend: set `AskAI.endpoint` (same JSON contract as the Ohm's Law Wattson proxy); keys never live in Unity.
Voice: hold-to-talk → offline Windows speech recognition (`StreamingAssets/CurioSpeech/Recognize.ps1`). Replies are spoken in the same neural
child voice as the story (pre-generated per sentence, `Resources/VO/kb`); a reply with any sentence that has no clip (backend answers,
composed text) falls back to the device voice.

## Voice-over
Lines: `Assets/Photosynthesis/Resources/curio_lines.txt` (id|text — single source for subtitles and VO).
Dr. Curio speaks with a neural child voice (Microsoft Edge TTS `en-US-AnaNeural`, rate +10 %): `python Tools/GenerateVO.py [--force] [--only id1,id2]`
(needs `pip install edge-tts numpy`, ffmpeg on PATH and internet; caps pauses inside a clip at 0.32 s, trims head and tail, writes 22050 Hz
mono WAV). `python Tools/GenerateVO.py --kb` pre-generates every fixed Ask-AI reply sentence into `Resources/VO/kb/<key>.wav`
(manifest: `Tools/kb_manifest.tsv`; rerun it after editing `CurioKnowledge.cs`). `Tools/GenerateVO.ps1` is the offline fallback (Windows SAPI).
Replies that cannot be assembled from clips use the device voice (SAPI Zira, raised pitch).
To use recorded VO, drop `Resources/VO/<id>.wav` files with the same ids.

## Input
Mouse (drag rotate · wheel zoom · right-drag pan · click) and touch (one finger drags to rotate, tap to select; HUD + / − zoom) — no
keyboard, hover, right-click or multi-touch is required anywhere. Keyboard shortcuts in the table above are conveniences. Every control a
learner needs is at least 44 px (zoom ± 48, dialogue › 48, closes 44, segments / chips / dropdowns ~44, Factory controls rows 48).
Windows' delayed mouse echo of a tap is ignored for 0.6 s, so a tap never counts twice.

## Testing
Current polish evidence and review results: [Photosynthesis/PLAYTEST_RESULTS.md](Photosynthesis/PLAYTEST_RESULTS.md). Changes and practical limitations: [Photosynthesis/FINAL_REPORT.md](Photosynthesis/FINAL_REPORT.md).

* Compile without Unity: `bash Tools/Compile/check.sh` (Runtime + Editor projects against Unity's DLLs).
* Full loop: `powershell -File Tools/BuildAndTest.ps1 [-Autoplay] [-SkipBuild]` → builds scene + player, captures a screenshot
  per beat to `Logs/shots`, and (with `-Autoplay`) plays the whole module unattended twice (second run answers wrong first to
  exercise every reteach branch). Logs in `Logs/player-*.log`.
* Player flags: `-beat <Beat>` · `-autoplay` · `-wrongfirst` · `-realvoice` (autoplay at true VO pacing) · `-film <dir>` (a frame
  every 3 s — a play-through filmstrip for review) · `-uishots <dir>` (every UI state: pause, settings, help, confirms, chat states,
  cards, experiment, cinematic HUD) · `-perf` (vsync off, logs `[Perf] beat fps dc setpass tris` every 2 s) · `-quality <name|index>` ·
  `-reducemotion` · `-shots <dir>` · `-diag` · `-hide a,b` · `-snap <png>` · `-quitafter <s>` · `-touchtest` (a simulated touchscreen
  drags and taps the Free Explore plant; logs `touchtest: … ok`). Pass **absolute** output paths.
* Everything at once (~40 min): `bash Docs/handoff/fulltest.sh` — filmstrips, shots, both autoplays, UI shots, perf table, touch test.
* Contact sheets: `python Docs/handoff/film_sheets.py Logs/film Logs/filmsheets 4 3 480` (PIL) — 12 labelled frames per sheet.
* Quality: two tiers (**Performance**, **High**). Integrated GPUs start on Performance; Settings ▸ Display and Accessibility ▸
  Reduce motion (cuts instead of camera moves, no shake/spin/drift) are saved between runs.

## Where things live
See `Docs/ARCHITECTURE.md` (composition, rules), `Docs/UI_SPEC.md` (Figma → UI Toolkit spec), `Docs/ASSETS_AND_LICENSES.md`.
Figma UI system: https://www.figma.com/design/suKcNjnLh8gigaytOygbBs
