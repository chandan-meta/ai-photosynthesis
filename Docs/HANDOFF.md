# Handoff — Photosynthesis module, session of 2026-10-07

Read this first. It records where the work against the 2026-10-07 master prompt stopped, what is verified, and what is left.
Project: `D:\AI unity project\AI PHOTOSYNTHESIS` (Unity 6000.3.0f1, URP 17.3, UI Toolkit, Input System). Not a git repository.

## 1. Where it stopped

Work stopped at the user's request right after the first multi-pass review. Review passes 1 (gameplay), 3 (cinematic) and 4 (audio)
ran on the current build and produced **25 confirmed findings (section 4). None of them has been applied yet.** Review passes 2 (UI/UX)
and 5 (performance/QA) have not run; their evidence now exists. Everything before that point is implemented, built and tested.

One source change is newer than the build: `Scripts/Runtime/Micro/LeafInteriorWorld.cs` (saved 18:55, build 18:50). It ignores the two
decorative "_Render" planes when measuring the client's leaf model and keeps the glide's last two camera points further out of the block.
It passes the compile check but is not in `Builds/Win`.

## 2. Build and test state (dev player `Builds/Win/Photosynthesis.exe`, Assembly-CSharp 18:50)

| Run | Log | Result |
|---|---|---|
| Player build | `Logs/build-player2.log` | Succeeded, 0 errors |
| Intro filmstrip (real pacing, 105 s) | `Logs/player-film-intro.log` → `Logs/film_intro`, `Logs/filmsheets_intro` | errors=0, 33 frames |
| Finale filmstrip (real voice) | `Logs/player-film-finale.log` → `Logs/film_finale`, `Logs/filmsheets_finale` | errors=0, 18 frames |
| Beat screenshots | `Logs/player-shots.log` → `Logs/shots` | errors=0, 24 shots |
| Autoplay, normal | `Logs/player-autoplay.log` | errors=0 |
| Autoplay, wrong answers first | `Logs/player-autoplay-wrong.log` | errors=0 |
| UI states | `Logs/player-uishots.log` → `Logs/uishots` | errors=0, 34 shots incl. `24_end_menu`, `25_finale_title` |
| Performance (vsync off) | `Logs/player-perf.log` | errors=0; table below |

No exceptions in any of the seven logs.

Performance on this PC (RTX 5060 Ti; tablet-class hardware not measured):

| Beat | avg fps | min fps | draw calls |
|---|---|---|---|
| FreeExplore | 327 | 246 | 734 |
| FinalPlant | 557 | 422 | 763 |
| Equation | 648 | 600 | 767 |
| Clue3 | 570 | 118 | 566 |
| all other beats | 549–1040 | ≥ 360 (Scene2Start 0 = loading frame) | 146–645 |

## 3. Done this session

**Audit (master prompt phase 1).** Project, scenes, flow (`Story.cs`), camera (hand-rolled `CameraDirector`, no Cinemachine), URP
volume, UI Toolkit HUD, procedural audio, SAPI voice-over, storyboard and the client ZIP were inspected.

**Client ZIP** `C:\Users\ADMIN\Documents\Received Files\G7_Photosynthesis_VR1.zip`. It is a Unity 2019.4 Pico VR project (6.2 GB).
`Assets/ActivityFolder`, the voice-overs, scenes and settings were extracted (no Library) to
`D:\AI unity project\models\G7_Photosynthesis_VR1\`. All module FBX files were imported, measured and rendered
(menu **Photosynthesis ▸ G7 Inventory**, `Scripts/Editor/G7Inventory.cs` → `Logs/g7/inventory.md` + `Logs/g7/*_front|side|top.png`).
Decisions per file are in `Docs/ASSETS_AND_LICENSES.md`.
- Used: `Photosynthesis_Leaf_inside.fbx` → `Resources/Models/LeafInside.fbx`, the inside-the-leaf reveal (`LeafInteriorWorld`).
- Library copies, not referenced by the scene and so not in builds: 13 files in `Assets/Photosynthesis/Art/G7`.
- Not used, with reasons: the chloroplast (181k tris, coarser than the procedural chloroplast world), plain-sphere molecules (two collapse
  to zero scale), the stomata pair, the old VR animations, the 146 MB garden. The ZIP textures are 512 px noise; used parts are re-materialled.
- The user asked whether the ZIP models are used. The open offer is to also use the ZIP chloroplast for the plant-cell hero reveal and
  the ZIP stomata in the leaf-surface close-up. Not started.

**Ending (master prompt section 4).** The lesson now ends at FinalPlant → `Story.Finale()`: pull-back over the working plant, equation
tokens, title "THIS IS PHOTOSYNTHESIS" with the line "Light energy turns water and carbon dioxide into glucose and oxygen", flows settle,
plant turns solid, music fades, fade to black, then a quiet end menu: **Replay · Review the plant · Exit** (`HudLearning.EndMenu`).
Review opens Free Explore, which gained a "Factory controls" button (the former mandatory Scene 14 experiment, now optional; its button
reads "Back to the plant"). The Assessment and Complete beats stay in code and the debug panel but are not in the flow. Verified by the
finale filmstrip and both autoplay runs.

**Cinematic opening (sections 5–6).** `Story.CurioIntro()` now shoots his briefing as a prologue: hero plant (low three-quarter Arc) →
sunbeam lands on the focus leaf and the light/chloroplast flows switch on → push to the leaf → dive through the surface (warm flash) →
`LeafInteriorWorld` glide past the client's cross-section with 560 chloroplasts that light up on "…food is being made" → back to the
chamber and the clue cards. The 16 s chamber formation of Scene 2 is unchanged.

**Voice (sections 8–9).** All 162 lines regenerated with a neural child voice (Microsoft Edge TTS `en-US-AnaNeural`, rate +10 %, trailing
silence trimmed) by the new `Tools/GenerateVO.py`; 575 s of audio. The old SAPI clips are backed up in `D:\AI unity project\models\VO_backup_sapi`.
Ask-AI replies still use the device voice (now SAPI Zira at raised pitch; see finding 20).

**Camera and input.** `CameraDirector`: the primary touch is the pointer while a finger is down (drag rotates, tap selects; pan stays on
the mouse, zoom on the wheel or HUD + / −). A Bokeh depth of field was added to the volume (inactive) and is switched on only during
scripted shots on the High quality tier, focused on the shot's subject.

**Molecules.** Shared atom materials are glossier (smoothness 0.82, metallic 0.14). They are still ball-and-stick spheres; the redesign
asked for in sections 10–12 is not done.

**UI and Figma.** Figma write access is refused (View seat: `use_figma` "no edit access", `create_new_file` "no permission to add to this
plan"). The end menu and finale title were therefore designed directly in UI Toolkit and recorded in `Docs/UI_SPEC.md`.

**Docs.** `README.md`, `ARCHITECTURE.md`, `UI_SPEC.md`, `ASSETS_AND_LICENSES.md` updated; this file and `Docs/handoff/` added.

## 4. Confirmed review findings — not yet applied

Full text, evidence and both skeptics' reasoning: `Docs/handoff/review_findings_2026-10-07.json` (25 confirmed, 10 rejected).
Grouped below (duplicates merged), roughly in the order to apply them. Line numbers refer to `Story.cs` as of 18:47 and will drift.

**Finale (the climax)**
1. **Major — the climax line is never spoken; "photosynthesis" is said twice beforehand; the equation is rebuilt a second time.**
   Add `s14_this|This is photosynthesis!` to `Resources/curio_lines.txt`, run `python Tools/GenerateVO.py --only s14_this`, and in
   `Finale()` start `Parallel(Say("s14_this"))` on the frame of `hud.Title`. Reword `s13_thats` (e.g. "That's the whole recipe.") and
   `s14_make` (e.g. "They make it themselves.") and regenerate both. Drop the `hud.Equation()` rebuild from `Finale()`.
2. **Major — the title fades out before the picture is black.** Give `hud.Title` a 9 s hold, do not wait for it, and run
   `hud.Fade(1f, 3.2f, Color.black)` while the title is fully visible. `fadeEl` sits below `titleLayer` in `Hud.cs`, so the title stays
   readable on black as an end card.
3. **Major — the music does not resolve.** Add `Sfx.FinaleCadence` to `Synth.MakeSfx` (e.g. Csus4 resolving to an F(add9) pad, bells and a
   low F, ~7 s, long reverb; the Resolution music is in F). Play it as the fade starts and use `au.Music(MusicState.None, 1.2f)`.
4. **Major — the pull-back has no escalation and the title sits over a dimmed X-ray plant.** Settle the flows and make the plant opaque
   before or under the title; make the move a crane from low near the pot to above the crown; put the title in the upper third.
5. **Minor — two overlapping EnergyBuild risers peak 3 s before the title.** Remove the one at the start of `Finale()`; if a lift is
   wanted, time one riser to end on the title.
6. **Minor — Menu / Reset / Help stay on screen over the finale and the black end menu.** `hud.SetHudVisible(false)` before the fade;
   `Prepare()` restores it for Replay and Review.

**Opening**
7. **Major — about 30 s of opening without voice.** `ChamberLook()`: `LookAround(..., 7f)` → `3f`. Voice the scanner gag in `CurioIntro()`:
   replace the three `Fx.Wait` (1.4 / 1.6 / 1.8 s) with `yield return Say(...)` of new lines such as `s3_scan1|Ooh! Unknown visitor
   detected…`, `s3_scan2|Species… human!`, `s3_scan3|Curiosity level… suspiciously high!`, generated with GenerateVO.py.
8. **Minor — the hero shot's scanner text ("billions of tiny processes") spoils the next line.** Remove that `ShowScanner` on `s3_peaceful`.
9. **Major — the 3.2 s hero Arc is replaced after the 1.86 s line.** Keep the Arc's coroutine handle and wait for it after `Say("s3_peaceful")`,
   or shorten the Arc to about 2 s.
10. **Major — the sunlight push looks at the shaded underside of the leaf.** Use a from-above direction such as `new Vector3(0.1f, -0.45f, 1f)`
    and start `FadeSunBeam` at the dive (`s3_somewhere`), not at the impact.
11. **Major — "through the surface" reads as a cream whiteout.** Skip the DoF when the shot's subject is closer than 0.6 m (`CameraDirector`
    LateUpdate). The reviewer's fix also dives through a stoma in `LeafSurfaceWorld`; that pre-empts Clue 1's stomata reveal, so the
    alternative is to make the plant translucent during the push (`plant.SetTransparency`) so the dive reads as passing through the leaf.
12. **Major — the interior reads as a specimen slab in an empty background, with chloroplasts floating in the air.** In `LeafInteriorWorld`,
    place the chloroplasts at palisade / spongy mesh vertices pushed inward along the negative normal (needs `isReadable = true` for
    `Resources/Models` in `ChamberBuilder.ConfigureImports`), lower the epidermis glass alpha (0.3 → ~0.18) and tint it greener. Optional:
    tile the block at ±34 m and raise the fog density (0.014 → ~0.03) so its edges dissolve.

**Learner guidance and silence**
13. **Major — the clue hubs have no voice prompt and no idle nudge.** In `Hub()`: after ~10 s idle with `!dlg.Speaking`, say a new
    `hub_hint` line and point; after ~20 s, `Parallel(hud.WaitButton("OPEN CLUE 0N", ...))` that sets `hubGo` only if `hubWant` still
    matches; a short spoken cue when hubs 2 and 3 open. Call `hud.HideButton()` when the hub exits.
14. **Major — Clue 3 → Sunlight can be silent for an open-ended time.** Add a quiet "dark" ambience bed to `Synth.MakeAmbience` and
    `Synth.HasBed` (sub-bass hum, faint ticks) and use it where `Clue3()` and `Sunlight()` set `Ambience("none")`. The beam impact already
    switches back to "chamber".
15. **Minor — the skylight hint keeps talking over the skylight opening and the beam.** When `Sunlight()`'s wait loop exits:
    `au.StopVoice(); dlg.Cancel(); hud.HideLine();`.
16. **Minor — factory-control lines cut each other off.** Queue them in `Experiment()` (one pending line, one coroutine that waits for
    `!dlg.Speaking`).

**Visuals in later beats**
17. **Major — the narrated hero molecules are the smallest, most bloomed things in frame.** `LeafSurfaceWorld.BuildMolecules`: hero CO₂
    size 0.24 → ~0.45, halo HDR 2.4 → ~1.2; during `CO2Enter` also shrink or fade ambient CO₂ within ~2 m of the camera (`SetDim`).
    Same halo cut for the picked H₂O in `SoilWorld`.
18. **Minor — the phloem shot ends with the glucose out of frame behind translucent cells.** `LeafTissueWorld.FoodToPhloem` stage 3: stop
    the glucose at z ≈ 8 and move the camera to `tube.x + 2.5`, z ≈ −5.
19. **Minor — faceted cells and chloroplasts in close-ups.** `LeafTissueWorld`: `MicroKitB.Sphere(5, 9)` → about (8, 14); palisade
    `Super(..., 10, 14)` → about (14, 22).

**Voice and settings**
20. **Major — Ask-AI replies switch from the child voice to SAPI Zira.** Plan worked out, not built: pre-generate neural clips per
    *sentence* of every fixed reply in `Learning/CurioKnowledge.cs` (explain / why / tease, greetings, the Hint and Nudge templates expanded
    with every `memory` string), after the same transform as `CurioKnowledge.Spoken()`, split on `(?<=[.!?…])\s+`, stored as
    `Resources/VO/kb/<hash>.wav` (hash of the lower-case letters and digits, e.g. FNV-1a). In `Dialogue.SayText`, play the concatenated
    sentence clips (all 22050 Hz mono) when every sentence has a clip; otherwise fall back to the device voice.
21. **Minor — 66 of 162 clips have 0.85–0.95 s pauses between sentences.** Cap internal silences in `GenerateVO.py` `to_wav`
    (`silenceremove=stop_periods=-1:stop_threshold=-42dB:stop_duration=0.35:stop_silence=0.35` before the trailing trim) and run `--force`.
    First split `s13_highways` into three lines (highways / xylem / phloem) so `Equation()` can drop its hard-coded `Fx.Wait(3.1f)` and
    `Fx.Wait(2.3f)` and trigger each visual on its own line.
22. **Minor — music, effects and voice volume are lost on relaunch.** Save them with `PlayerPrefs` in the three slider callbacks in
    `UI/HudSystem.cs` and read them back in `AudioDirector.Init`.

Rejected by the skeptics (do not redo): Clue 3 told not discovered; "Where's the food?" two-shot; Scene 1 names; Scene 14 subjects out of
frame; "Food made." over the stoma; equation built three times; back-to-back question cards; interior glide ending inside the layers
(also covered by the unbuilt change in section 1); sunlight flare over the leaf; back-out two-shot cropping the crown.

## 5. Other work still open against the master prompt

- Review passes 2 (UI/UX) and 5 (QA/performance) — run them on the existing evidence (section 6).
- Molecule redesign (sections 10–12): fresnel/rim, soft internal glow, readable sizes; motion review.
- Lighting pass and per-environment grading / LUT (sections 16–17): nothing done beyond the DoF.
- The water + CO₂ + light → glucose + oxygen "wow" convergence (section 13) and the chloroplast hero reveal (section 15): existing beats, not upgraded.
- SFX and music pass beyond findings 3, 5 and 14 (sections 20–21).
- Touch: code path only; not tested on a touch device or with simulated touch. No pinch zoom by design (HUD + / −).
- Tablet performance not measured; FinalPlant, Equation and FreeExplore draw ~730–770 calls.
- Optional ZIP chloroplast / stomata use (section 3).
- After the fixes: rebuild, full evidence suite, filmstrip review, then the release build (`PhotosynthesisBuild.BuildPlayerRelease`, not rebuilt this session).

## 6. How to continue

| Step | Command (from the project root) |
|---|---|
| Compile check (seconds, no editor) | `bash Tools/Compile/check.sh "Runtime Editor" 60` |
| Rebuild scene (builder changes only) | `"C:/Program Files/Unity/Hub/Editor/6000.3.0f1/Editor/Unity.exe" -batchmode -projectPath "D:/AI unity project/AI PHOTOSYNTHESIS" -executeMethod Photosynthesis.EditorTools.PhotosynthesisBuild.BuildAll -logFile "D:/AI unity project/AI PHOTOSYNTHESIS/Logs/build-scene.log" -quit` |
| Rebuild dev player (~4 min) | same, with `-executeMethod Photosynthesis.EditorTools.PhotosynthesisBuild.BuildPlayer` |
| Full evidence suite (~30 min) | `bash Docs/handoff/fulltest.sh` — filmstrips, shots, autoplay ×2, UI shots, perf; prints a per-beat fps table |
| Contact sheets | `python Docs/handoff/film_sheets.py <frames dir> <out dir> 4 3 480` |
| New or changed VO lines | edit `Resources/curio_lines.txt`, then `python Tools/GenerateVO.py --only id1,id2` (needs internet) |
| Remaining review passes | Workflow tool with `scriptPath` = `Docs/handoff/review_workflow.js`, `args` = `{"passes": ["uiux", "qa"]}` |

## 7. Gotchas met this session

- Bash heredocs that contain an apostrophe failed in this environment ("unexpected EOF while looking for matching `'`"). Write scripts
  with the Write tool, then run them.
- The Unity Editor that is usually open holds the AI OHM'S LAW project. Batch builds of this project run fine beside it; only an editor
  with this project open blocks them.
- Scripted shots are not clamped to the dome. Keep (height / 10.3)² + (horizontal distance / 12)² < 1 from the chamber centre, or the lens
  enters the rib fins (the first finale pull-back did this and washed the frame out).
- The Figma account has no edit rights (see above). The `unity` CLI is not installed; batch `Unity.exe` works. ffmpeg 8.1, edge-tts 7.2.8,
  PIL and Blender 3.3–5.1 are installed.
- The G7 FBX files are ASCII, centimetre-scale; `LeafInteriorWorld` normalises the leaf block to 34 m at runtime.

## 8. Files changed this session

- `Scripts/Runtime/Story/Story.cs` — play loop ends at FinalPlant, `CurioIntro` prologue, `Finale`, end-menu branches, Free Explore
  "Experiment", `Place.Interior`.
- `Scripts/Runtime/Micro/LeafInteriorWorld.cs` — new.
- `Scripts/Runtime/Core/CameraDirector.cs` (touch, scripted-shot DoF), `Core/Module.cs` (interior world), `Core/Fx.cs` (glossy atoms).
- `Scripts/Runtime/Audio/AudioDirector.cs` (Ask-AI device voice), `UI/Hud.cs`, `UI/HudLearning.cs` (end menu, Factory controls button),
  `Story/DebugTools.cs` (end-menu and finale-title UI shots).
- `Scripts/Editor/PhotosynthesisBuild.cs` (DoF in the volume), `Editor/ChamberBuilder.cs` (G7 import settings), `Editor/G7Inventory.cs` — new.
- `Resources/Models/LeafInside.fbx`, `Art/G7/*.fbx`, all `Resources/VO/*.wav`, `Tools/GenerateVO.py` — new or regenerated.
- `Docs/*` as listed in section 3; `Docs/handoff/` (review findings JSON, review workflow, test suite, contact-sheet script).
