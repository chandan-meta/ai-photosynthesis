# Handoff — Photosynthesis module, 2026-10-10 (stomata UI · Clue 3 · formula sweep)

Read this first. Sections 0d and 0c are the latest work (branch `codex/root-material-upgrade`); 0a/0b and 1–8 still hold except where
0c/0d say otherwise. The 2026-10-09/10 sessions in between (materials, imported leaf/chloroplast models, licensed soundtrack)
are documented in `Docs/Photosynthesis/*.md`.
Project: `D:\AI unity project\AI PHOTOSYNTHESIS` (Unity 6000.3.0f1, URP 17.3, UI Toolkit, Input System).
Git: local repository, `main` = the module (baseline `daf9bf5`, `85e0b0a` review passes, `ea251e4` the Dr. Curio scarf, then the
new chamber environment). Branch `feature/tripo-curio` parks the rejected character replacement (0b). Since 2026-10-10 the remote
`origin` is https://github.com/chandan-meta/ai-photosynthesis (`main` pushed at `8902082`); section 7 predates it.

## 0d. WebGL build (2026-10-10, evening)

**Request.** "make webgl build and give me zip, after push on git".

**Done.** `Photosynthesis ▸ Build WebGL` (`PhotosynthesisBuild.BuildWebGL`) → `Builds/WebGL/`, release, 216 MB (214 MB data, 12.5 MB wasm).
It sets gzip with the decompression fallback (any static host works, no server headers needed) and a 4 GB memory ceiling, and after
building it injects CSS into `index.html` so the canvas fills the window: the Default template draws 960×600 on desktop, which would
shrink the HUD below its 1600×900 floor. `AudioDirector` rendered the procedural SFX and ambience on `Task.Run`, which never runs in a
single-threaded browser; on WebGL, `Update` now renders one clip per frame on the main thread instead. Hosting zip:
`Builds/Photosynthesis_WebGL.zip` (226.5 MB, `index.html` at the root, `README.txt` with hosting notes). Batch build: start directly
on the target (`-buildTarget WebGL`, about 20 min including the one-time WebGL import), then reopen with
`-buildTarget StandaloneWindows64 -quit` so the editor is back on Windows (both done).

**Tested in a browser** (the Claude desktop app's browser, local `http.server`, 1280×720 viewport): it loads and runs. Played from Start
Investigation through the intro, Clue 1 (pore label riding the zoom, GUARD CELLS, the card in the tracker's slot, CO₂ ENTERING leaving
at entry, reward with the tracker back), Q1/Q2, Clue 2 (roots, soil pick, xylem ride), Clue 3 (narration straight into the Q5 card, no
inspect step), skylight, Q6, and Enter Leaf ("Time to go deeper." with the push into the tissue). Console: no errors, exceptions or audio
render failures; only URP's usual start-up notices that `Hidden/CoreSRP/CoreCopy`, `StencilDitherMaskSeed` and `HDRDebugView` are not
supported. **Limits:** voice questions are Windows-only (the Ask AI mic says so); SFX render on the main thread during the first seconds,
while the start banner is up. Not tested: Safari, mobile browsers, low-end GPUs; frame rate not measured.

**GitHub Pages.** GitHub refuses files over 100 MB, so `Builds/WebGL-pages/` (made by a one-off script from `Builds/WebGL`) splits the
214 MB data file into five parts under 45 MB, and its `index.html` downloads and joins them in the browser before starting Unity
(tested locally: loads to the start banner, no console errors). It is pushed as branch `gh-pages` (`66560b4`, its own history) of
`chandan-meta/ai-photosynthesis`. Pages did not switch on by itself because the repository is private: enable it under Settings ▸ Pages
(source `gh-pages`, folder root), which needs a public repository or GitHub Pro; the site would be
https://chandan-meta.github.io/ai-photosynthesis/. Alternatively push the same branch to a new public repository. Each visit downloads
about 227 MB, and Pages has a soft limit of 100 GB of traffic a month.
The 27 art files changed outside this session (eight leaf surface maps, 19 Upgraded materials) were committed afterwards at the
user's request ("push final change"); the WebGL build already includes them.

## 0c. Stomata reading card, Clue 3 without the plant-inspection step, glucose-formula sweep (2026-10-10)

**Request.** (1) The stomata close-up explained nothing readable; (2) Clue 3's "Tap the plant to inspect it" window and its line were
unwanted: Clue 3 should go straight from its narration into the question, then the existing Enter Leaf line and camera; (3) the whole
project had to be checked for wrong glucose formulas (C₆H₁₂O₆; 6CO₂ + 6H₂O → C₆H₁₂O₆ + 6O₂).

**Done.**
- *Stomata (Story.Stomata / Story.CO2, Hud callouts and info card).* STOMATAL PORE is attached to the hero stoma's slit as the 10× → 100×
  zoom starts and rides the zoom (screen-space UI Toolkit, so nothing can hide it behind the leaf); GUARD CELLS appears with "Found
  something."; the Info Card "Stomata — / Tiny Gateways of the Leaf" fades in with "These tiny openings are called stomata." in the
  progress panel's slot (the tracker steps aside: under it, the 323 px card covered Dr. Curio's head on every 16:9 canvas) and fades out
  before the reward card, which brings the tracker back in time for its pop; CO₂ ENTERING rides the glowing molecule and leaves when the
  4 s entry ends, so the pore centre is clear. The Ask-AI "show me CO₂ / stomata" action uses the same pore label (`Story.PoreLabel`);
  the Q1/Q2 replay mirrors it to the slit's right tip, clear of the reteach card. Both replace a centre-anchored STOMA tag that hid the
  resting CO₂. Small system additions: `Hud.Callout(..., left:)` forces a tag's side; a new tag's leader draws out from its dot
  (`.ps-leader` width transition, gated by `CalloutUi.born`); `Hud.ShowInfo(..., slot: true)` puts the card in the tracker's place
  (`infoInSlot` in `RefreshPersistent`, which now also owns the card's chat-open offset): its × button restores the tracker at once, and
  it steps aside while the Ask AI chat is open. Design record: `UI_SPEC.md` ▸ Stomata close-up.
- *Clue 3 (Story.Clue3).* After "We're missing something." the beat calls `Ask("Q5")` at once. Removed: the `s8_inspect` line (from
  `curio_lines.txt` and `Resources/VO/s8_inspect.wav`) and the `LookAround("Tap the plant to inspect it…")` window. The Sunlight beat
  (skylight, beam, Q6, recap) and EnterLeaf (ENTER LEAF → "Time to go deeper." → focus/push → tissue) are untouched: on the baseline
  film they already run focus (1.2 s) → push (1.6 s) → warm flash into the tissue, with the line starting after "…how they become food."
  ends. The Ask-AI "what should I do" hints for Clue 3 no longer say "inspect the plant" (`CurioKnowledge.Next`); their reply clips were
  regenerated (`python Tools/GenerateVO.py --kb`: new clip `VO/kb/bcb97796.wav`, the two orphaned clips deleted, manifest rewritten).
  The chamber tooltip branch for Beat.Clue3 stays: the plant can still be tapped while the Clue 3 hub waits.
- *Formulas: none wrong, nothing changed.* Checked by hand (the review Workflow's sweep agents hit a usage limit): 1,353 text files
  token-scanned, including digit-zero look-alikes; 116 colour textures and 69 images embedded in the FBX/GLB files viewed on contact
  sheets; every story line, all 271 Ask-AI reply sentences and both speech transforms read; the runtime glucose model checked with the
  player's `-sciencecheck` (Glucose C:6 H:12 O:6, 24 bonds, PASS on the current build). Every glucose formula reads C₆H₁₂O₆ / C6H12O6 and
  every equation 6CO₂ + 6H₂O → C₆H₁₂O₆ + 6O₂; the lab posters name GLUCOSE without a formula; no line speaks a formula. No Timeline,
  TextMeshPro or scene/prefab text exists. **Not a glucose formula, but wrong:** the lab's acid/base glassware texture
  (`Art/Environment/Textures/Acid base_Acid_base_BaseColor.png`, from the user's lab model) has a sticker "ACID pH>7" (acids are below
  pH 7). Left for the user: it is their art, and `Tools/Chamber/build_env.py` would overwrite a hand edit.

**Review.** An independent Workflow review (3 lenses; 3 adversarial verifiers per finding) confirmed 11 findings and refuted 1. Fixed:
the card covering Dr. Curio's head (major), the CO₂ tag staying on the resting molecule after the entry, the replay / Ask-AI pore label
on the pore centre, GUARD CELLS timing against the docs, doc wording. A second review of those fixes (2 lenses, 2 verifiers each)
confirmed 2 more, both fixed: the replay's left-side label sat under the reteach card on 1280×720 and 1366×768 screens, and with the
chat open the shifted card covered GUARD CELLS. Left as is: the pore tag overlaps the thin pinch where the two guard
cells meet at the slit tip (inherent to a left-side tag; the old tag covered the pore itself); the card names carbon dioxide about 3 s
before "And look what's entering" (user-supplied text); Q5's lead now follows straight on "We're missing something." and repeats the
four facts just narrated (a shorter lead would need a new clip); pre-existing: Ask-AI Help replies that embed a hint use the device
voice, because the runtime sentence split keeps "try “Show me water.” Select…" together.

**Verification and its limit.** Update, same evening: the final code was then played through both sequences in the WebGL build (0d),
which supersedes the "not run" below; a Windows player rebuild and `fulltest.sh` are still to do.
`bash Tools/Compile/check.sh "Runtime Editor" 60` → no errors or warnings. **No Play Mode / player run
of the final code was possible:** the user's Unity editor held this project until about 17:35 (`Temp/UnityLockfile`, Unity.exe with
`-projectpath "…AI PHOTOSYNTHESIS"`), which blocks batch builds, and both drives were down to about 1 GB free, so a project copy could
not be built either. Run instead: the existing dev player (`Builds/Win`, built 2026-10-10 13:37, before this change) with
`-beat Stomata -autoplay -realvoice -film … -filmrate 1` and `-beat Clue3 … -filmrate 1.5` as the baseline (frames and contact sheets in
the session scratchpad, not kept), `-sciencecheck`, and static review. The open editor itself (driven from outside this session)
recompiled the first version of these changes with no errors and entered Play Mode; `Editor.log` shows that session running Clue 1 into
the Stomata beat with no exceptions. That is partial evidence only: it predates the review fixes and nothing confirms what was on screen.
**Not part of this work:** while this session ran, other activity changed project files (an OpenAI Codex session was active on the
machine). At 16:56 the eight `Art/Generated/Resources/SurfaceMaps/LeafTop_*` / `LeafUnder_*` maps were rewritten and imported; at 17:35,
as the editor closed, it re-saved 19 materials under `Art/Upgraded/Resources/` (`UpgradedModels/LeafPart*`, `Chloroplast*`,
`UpgradedMaterials/VesselWall.mat`: `m_DoubleSidedGI` 0 → 1 and a `_MainTex` assigned, which looks like Play Mode writes to shared
material assets). All were left as they are; review them separately before committing. The editor holding this project closed at about
17:35, after which batch builds were possible again, but D: was down to about 1 GB free (a dev build stages about 600 MB under `Temp`)
and another Unity project was open and had just run out of memory, so no build was started. **First thing to do in the editor:** Play → F1 → jump to
`Stomata` (the pore label riding the zoom, GUARD CELLS on "Found something.", the card replacing the tracker, the CO₂ tag leaving as the
molecule settles, the card leaving before the reward) and to `Clue3` (narration → Q5 card with no instruction pill → skylight), then
rebuild the dev player and run `bash Docs/handoff/fulltest.sh`.

**Not changed, by design.** The ENTER LEAF button, the skylight tap and Q6 stay: the request said to remove only the inspection
instruction and its voice-over. If "Clue 3 → question → Enter Leaf" was meant to skip the skylight discovery too, that is a separate
decision, since it would remove the clue's unlock, Q6 and the sunbeam reveal.

## 0a. New chamber environment: the user's lab model (2026-10-08, night)

**Request.** "Change the environment dome model with this (`Ai Photoshynthesis1.fbx`), keep the centre plant as it is, change the rest of
the environment." Done: the old glass dome, the tropical vegetation and all procedural decor (floor + engraving, beds, pots, research
stations, banners, vines, suspended leaves) are gone; the user's glass-roofed lab replaces them. The hero plant, its soil, the cyan ring
(now on the planter rim), the skylight, the sun beam, the formation swirl, the underground cross-section and the outside scenery stay.

**Pipeline.** `blender -b --factory-startup -P Tools/Chamber/build_env.py` (source FBX only read) → `Art/Environment/Chamber.fbx` +
`Textures/` (53 maps, 2048 px architecture / 1024 px props). It cuts the soil out of the model's centre planter (faces centred inside
r 2.85 above 0.3 m; the rim and its inner wall stay), splits the floor into `Floor Inner` (r 2.9 to 6.4, fades for the roots x-ray; open under the soil like the old platform) and
`Floor Outer` with the same planar UVs, and decimates the five microscopes (19k → 4k triangles each); 193 meshes, 255k triangles.
Build Scene (`ChamberBuilder`) does the rest: importer settings (normal maps by file name, no colliders, axis conversion baked: door at
−Z), derived materials in `Art/Environment/Materials` (cutout double-sided foliage, glass, glowing hologram screens, warm lamps), the
roof split into 24 sectors (`RoofPieces.asset`), and 208 pieces handed to the Scene 2 formation. Details: `ARCHITECTURE.md` ▸ Chamber
environment.

**Gameplay changes that follow from the model.** `Chamber.PlatformHeight` 0.35 → 0.55 (the soil sits inside the planter rim; every
scripted shot is plant-relative, so it moved with it). Skylight y 10.5 → 11.9 (its stack stays under the roof crown hub). Story camera bound
(12, 10.3, 12) → (9.4, 11.4, 9.4): the posts' inner corners stand at r ≈ 9.0 and the building ends at r ≈ 10.4 (open sides, so the
outside ground now starts at r 10.2). Establish pose z −10.2 → −8.4 (it was inside the door wall), hero shot (−3.4, 0.7, −8.6) →
(−2.6, 0.7, −7.4) (it was inside a post), finale crane top (0, 6.2, −8.4) → (0, 5.4, −7.4) (under the ivy shelf). Lamps, screens and
emissive maps dim with daylight and the dark beats (runtime copies); ivy and hologram screens are culled when the orbit lens is inside
them. The old `DomeInterior.fbx`, `DomePieces.asset`, `TropicalPlants.glb`, `DomeGlass.mat` and `DomeLamp.mat` are no longer used (kept).

**Verification (final source; scene + dev player 274 MB, 0 build errors; release player rebuilt from the same source).** Full
suite (`Docs/handoff/fulltest.sh`), every run errors=0 and no exceptions: real-pacing intro film (formation → hero shot → leaf vista) ·
finale film · 24 beat screenshots · autoplay · autoplay wrong-answers-first · UI states (Ask-AI voice ok) · touch (drag 67.9°, 1 tap) ·
perf run (lowest per-beat minimum 257 fps at Sunlight, FreeExplore 261; Scene2Start's 0 is the loading frame). Extra animation bursts
(`-film -filmrate`): formation, Scene 8 skylight + beam, roots reveal, finale crane, Phloem auto-spin orbit — no clipping found.
An independent review (3 reviewers + adversarial verifiers) confirmed 12 findings, all fixed: hologram screens glowed where their art is
transparent (now straight alpha), acid/base glassware drew opaque (opacity map merged into the base alpha), thin ivy strands and the ends of
the curved screens escaped the near-camera cull (now bounds-based), the inner floor ghost lay across the root column (now a ring from the
planter wall), fade twins popped glossy (they copy the source gloss), the skylight's throat poked into the crown hub (y 12.1 → 11.9), three
light shafts started inside tables, the Assessment card pose sat outside the bound (0.19 m pop), whole-plant orbits could dip into the bench
props (pitch ≥ 0 now), plus dead code. 2 findings were refuted. Known and **not** from this change: a player run quit with `-quitafter`
while music was still rendering crashed on shutdown (`audio render … Thread was being aborted`, also in older logs) — flagged as a
separate task (AudioDirector background render vs. quit).

## 0a-2. Model exports, ZIP folder, gameplay recording (2026-10-09)

- **`-exportmodels <dir>`** (dev or release player, `ModelExport.cs`): dumps every runtime-built model — hero plant (grown, roots and
  vessels on), Dr. Curio, the chamber's procedural pieces (static batching is skipped under this flag so the combined meshes stay
  readable), the four molecules and all seven micro worlds — as OBJ + MTL + PNG (GPU read-back of the generated textures), one folder
  each, then quits. `Tools/Export/export_used.py` (Blender) turns each into an FBX with the textures embedded (`leaf` mode; `verify`
  mode re-imports and counts packed images); `chamber` and `curio` modes export the lab model and the Curio kit + scarf. The user's
  copies live in `Exports/UsedModels/` (git-ignored, README inside): 17 FBX files.
- **`Art/G7/`** now holds all 15 client-ZIP models with their referenced textures, imported with materials (README inside). The 146 MB
  garden and its 27 textures are git-ignored (GitHub's 100 MB limit); everything else there is tracked.
- **Gameplay recording:** `Recordings/` (git-ignored) — `Tools/Record/record.sh <out.mp4> <max s> [player args]` (release player
  windowed 1600×900, ffmpeg gdigrab capture of the window's screen region, `loopback_rec.py` WASAPI loopback audio that follows the
  default output device, NVENC through the imageio-ffmpeg 7.1 binary because the installed ffmpeg 8.1 needs a newer NVIDIA driver;
  `postprocess.sh` makes the 720p copy). The 2026-10-09 run: 8 min 17 s, 1600×900/30 fps, plus a 720p copy and three sub-25 MB chapters.
- **Disk:** both drives were full on 2026-10-09; old test frame dumps under `Logs/` (film*/burst*/ui_*) were deleted. Watch free space before builds.
- **Editor open = no batch builds.** While the user's editor held the project, the export player was built from a copy of the project
  (`Library` copied without caches, Mono backend) — slow (shader variants recompile); better to ask for the editor to be closed.

## 0b. Dr. Curio: the classic robot, now with the user's green scarf (2026-10-08, evening)

**Decisions (user, in order).** (1) A full replacement Dr. Curio was built from `D:\AI unity project\charcter\dr curio.blend` (rigged in
Blender, 10 clips, face blendshapes, spring sprout) — the user preferred the original procedural robot; it is parked, complete, on branch
`feature/tripo-curio` (commit `3c35700`, pipeline `Tools/Curio` there). (2) The user got an FBX of the classic robot to repaint
(`D:\AI unity project\charcter\dr curio classic\`) and came back with `dr.blend`: their own Tripo robot built on that FBX. (3) Final call:
**keep the original Dr. Curio and its animation; add only the green scarf from `dr.blend`.** That is what `main` ships.

**The scarf.** `blender -b --factory-startup -P Tools/Curio/scarf.py` reads `dr.blend` (never written), takes the scarf, fits it to the
classic torso (85 % about the knot, 2 cm back), bakes its fabric (the cloth texture through the user's colour nodes) to
`Resources/Characters/Curio_Scarf.png` (512 px, empty texels filled so mipmaps stay green) and writes `Resources/Characters/Curio_ScarfMesh.obj`
already in Curio's rig space (Unity's OBJ import mirrors X into it). `Curio.Build` adds it under the rig (double-sided Leaf material, a faint
green lift like the shell's in dark micro worlds, casts shadows): 12 lines; every animation is the original.

**Also on main:** `-filmrate s` debug flag (animation bursts with `-film`, `-beat`, `-quitafter`; a bad value warns and keeps 3 s); Curio is
hidden before the white flash into the leaf-interior vista (he flashed up in companion mode during the fade).

**Housekeeping.** Build Scene (before 0a) regenerated `Materials/DomeGlass.mat` and `DomeLamp.mat`; their uncommitted local edits that existed at the
start of this session were overwritten that way (they match the last commit). The texture-painting kit folder above predates the scarf
decision: its in-game skin hook is not on main (its README says so).

**Client ZIP:** unchanged since the 2026-10-07 inventory; `Photosynthesis_Leaf_inside.fbx` remains in use (Scene 3 prologue interior), the
other models stay unused for the reasons in `ASSETS_AND_LICENSES.md`. **Lighting:** realtime only (story-animated sun/ambient, runtime-built
plant and micro worlds make baked GI wrong or empty); SSAO gives contact shadows.

**Verification (final build, dev player 435 MB, 0 build errors):** beat screenshots errors=0 (scarf visible in the chamber and in companion
mode) · intro animation burst errors=0 · autoplay errors=0 · autoplay wrong-answers-first errors=0 · UI states errors=0 with the Ask-AI voice
check ok · simulated touch ok (drag 67.9°, 1 click) · performance run: see `Logs/player-perf.log` (table in the commit notes). An independent
review (code + asset inspector, adversarial verification) confirmed 3 small issues, all fixed (licence row for the BlenderKit fabric,
handoff wording, stale staged DebugTools copy); 3 other findings were refuted.

## 1. Where it stands

All five review passes the master prompt asks for have run, and every confirmed finding is fixed:

| Pass | Run | Confirmed | Status |
|---|---|---|---|
| 1 Gameplay & education | 2026-10-07 | 25 findings across passes 1, 3 and 4 | all fixed today |
| 3 Cinematic & visual | 2026-10-07 | (included above) | all fixed today |
| 4 Audio & polish | 2026-10-07 | (included above) | all fixed today |
| 2 UI/UX | today | 9 (1 major: touch zoom targets) | all fixed |
| 5 Performance & bug QA | today | 5 (2 major: card size regression, Free Explore factory controls) | all fixed |

The previous handoff's findings list (with evidence and the skeptics' reasoning) stays in `Docs/handoff/review_findings_2026-10-07.json`.

## 2. Build and test state

Dev player `Builds/Win/Photosynthesis.exe` and release player `Builds/WinRelease/Photosynthesis.exe` (no watermark, no F1 panel) are
built from the same final source. Full evidence suite (`bash Docs/handoff/fulltest.sh`) on the dev build of 11:52, every run errors=0,
no exceptions:

| Run | Result |
|---|---|
| Intro filmstrip (real pacing) | errors=0, 33 frames → `Logs/filmsheets_intro` |
| Finale filmstrip (real voice) | errors=0, 20 frames → `Logs/filmsheets_finale` |
| Beat screenshots | errors=0, 24 shots → `Logs/shots` |
| Autoplay, normal | errors=0 |
| Autoplay, wrong answers first | errors=0 |
| UI states | errors=0, 34 shots → `Logs/uishots`; Ask-AI reply voice check "ok" |
| Performance (vsync off) | errors=0; table below |
| Simulated touch (`-touchtest`) | drag turned the camera 67.9°, the tap fired exactly 1 click → ok |

After that run, three small changes went in (Settings rows are whole-row touch targets, the zoom control hides at the finale fade, the
staged UI shot matches). Both players were rebuilt with them; the final checks on those builds are in section 8.

Performance (dev build, this PC: RTX 5060 Ti; tablet hardware not measured):

| Beat | avg fps | min fps | draw calls |
|---|---|---|---|
| FreeExplore | 245 | 206 | 738 |
| FinalPlant | 268 | 222 | 771 |
| Equation | 274 | 247 | 664 |
| Sunlight | 282 | 186 | 606 |
| every other beat | 279–413 | ≥ 232 (Scene2Start 0 = loading frame) | 143–649 |

Every beat is about 2× slower than in the 2026-10-07 run, including beats nothing touched today, while draw calls are within ±5 %. That
pattern makes today's changes an unlikely cause, but the actual cause was not established. Re-measure on a quiet machine before
quoting numbers.

## 3. Done (2026-10-07 and 2026-10-08)

**Ending.** The lesson ends in `Story.Finale()`: a 15 s crane rises over the working plant while five 3D labels show the relationship
(WATER, CARBON DIOXIDE, SUNLIGHT go in; GLUCOSE, OXYGEN come out); Dr. Curio says **"This is photosynthesis!"** as the title appears in the
upper third with the summary line at 22 px; the flows settle and the plant turns solid; the music resolves on a new cadence
(`Sfx.FinaleCadence`, Csus4 → F add9); the picture fades to black while the title stays up, then a quiet end menu: **Replay · Review the
plant · Exit** (Exit asks for confirmation; Review is the default). Nothing mandatory follows. Review = Free Explore with an optional
"Factory controls" experiment. The earlier "That's photosynthesis." / "…using photosynthesis." lines were reworded so the word lands once.

**Opening.** 3 s free look (was 7 s), then Dr. Curio reads his scan aloud ("Ooh! Unknown visitor detected…", "Species… human!",
"Curiosity level… suspiciously high!"). Prologue: hero plant (the Arc lands before the next shot; the readout no longer spoils the next
line) → sunlight lands on the lit upper leaf (camera from above; the beam fades as we dive) → the leaf turns glassy as the camera dives
through it → the client's leaf cross-section model (`LeafInteriorWorld`): chloroplasts now sit inside the cells (seeded on the cell
meshes), thinner green cuticle, the slab repeats either side into denser fog → back out to the clue cards.

**Voice.** All 170 story lines in the neural child voice (`en-US-AnaNeural`), regenerated with pauses inside a clip capped at 0.32 s
(575 s → 515 s of audio with 8 more lines). Ask-AI replies now use the same voice: 272 fixed reply sentences pre-generated
(`Resources/VO/kb`, `python Tools/GenerateVO.py --kb`), joined at runtime by `Dialogue.KbClip`; anything not covered falls back to the
device voice. `s13_highways` is split so each highway lights on its own line (no hard-coded waits).

**Guidance and silence.** Clue hubs 2 and 3 open with "Next clue unlocked!"; after ~12 s of quiet a spoken nudge; after ~22 s a big gold
"OPEN CLUE 0N" button. The dark Clue 3 → skylight stretch has a low "dark" ambience bed instead of silence. The skylight hint stops when
the learner acts. Factory-control lines queue instead of cutting each other off. Music / effects / voice volume survive a relaunch.

**Visuals.** Molecules: glossy atoms plus a soft fresnel rim (`Shaders/MoleculeRim.shader`, a second material on every atom, LDR so it
never blooms). The narrated CO₂ at the stoma is larger with a softer halo and nearby ambient CO₂ shrinks away during its entry; the
picked H₂O halo is softer. Leaf-tissue chloroplasts and palisade cells are smoother in close-ups. The phloem shot keeps the glucose in
frame from outside the cut face. Depth of field switches off when the lens almost touches its subject (no blur wash on the dive).

**UI/UX (pass 2).** Touch targets ≥ 44 px (zoom ± 48, dialogue › 48, closes 44, segments / chips / dropdowns ~44, slider hit area 44,
Factory-controls and Settings rows are whole-row targets ≥ 48). Reading cards are opaque (no ghosting of bright signs). Instructions
and Help use touch-first wording ("Tap…", "press + (or scroll)"); voice lines say "tap". Small text 14 px; chat replies in Body. The
working-plant instruction hides before the lines so the roots stay visible. `Docs/UI_SPEC.md` records all of it (Figma is read-only).

**QA (pass 5).** Question and info cards keep their 620 / 380 px size and padding (a regression from this morning's edits). An Ask-AI pick
during Free Explore's Factory controls now ends them cleanly and the tile bar comes back. No lit frame flashes between the end menu
and Replay / Review. Factory rows have one keyboard stop with a visible focus. The touch "mouse echo" guard only ignores a mouse click at
the tapped spot (hybrid laptops keep their trackpad).

**Earlier (2026-10-07, unchanged):** ZIP inventory and decisions (`Docs/ASSETS_AND_LICENSES.md`), touch input path in `CameraDirector`,
scripted-shot depth of field, the end menu and Free Explore "Factory controls".

## 4. Still open against the master prompt

- **Grading / LUT and a lighting pass (sections 16–17):** not done. Each micro world sets its own fog / ambient mood; no per-scene LUT.
- **The "wow" convergence (section 13) and the chloroplast hero reveal (section 15):** the Reaction and Plant-cell beats are unchanged
  from the original build. The finale now summarises the relationship, but the in-chloroplast convergence itself was not upgraded.
- **Optional ZIP use:** the client's chloroplast and stomata models could replace the procedural hero chloroplast / guard cells
  (offered to the user, not started). See the ZIP table in `Docs/ASSETS_AND_LICENSES.md`.
- **Touch on a real tablet:** the simulated touch test covers camera drag and tap; UI Toolkit buttons under touch and Windows' real tap
  promotion were not tested on hardware. No pinch zoom by design (HUD + / −).
- **Performance on tablet-class hardware:** not measured; FinalPlant / Equation / FreeExplore draw ~660–770 calls.

## 5. How to continue

| Step | Command (from the project root) |
|---|---|
| Compile check (seconds) | `bash Tools/Compile/check.sh "Runtime Editor" 60` |
| Rebuild scene (builder / import changes) | `"C:/Program Files/Unity/Hub/Editor/6000.3.0f1/Editor/Unity.exe" -batchmode -projectPath "D:/AI unity project/AI PHOTOSYNTHESIS" -executeMethod Photosynthesis.EditorTools.PhotosynthesisBuild.BuildAll -logFile "D:/AI unity project/AI PHOTOSYNTHESIS/Logs/build-scene.log" -quit` |
| Rebuild dev / release player | same with `...PhotosynthesisBuild.BuildPlayer` / `...BuildPlayerRelease` |
| Full evidence suite (~40 min) | `bash Docs/handoff/fulltest.sh` (filmstrips, shots, autoplay ×2, UI shots, perf table, touch test) |
| Story VO after editing `curio_lines.txt` | `python Tools/GenerateVO.py --only id1,id2` (needs internet, `pip install edge-tts numpy`) |
| Ask-AI reply VO after editing `CurioKnowledge.cs` | `python Tools/GenerateVO.py --kb` (manifest `Tools/kb_manifest.tsv`) |
| Review passes | Workflow tool with `scriptPath` = `Docs/handoff/review_workflow.js`, `args` = `{"passes": [...]}` (gameplay, uiux, cinematic, audio, qa) |

## 6. Gotchas

- Bash heredocs containing an apostrophe failed in this environment; write scripts with the Write tool, then run them.
- When stopping a background test run, also kill `fulltest.sh` itself (it relaunches the next player), then any `Photosynthesis.exe`.
- An open Unity Editor on another project (AI OHM'S LAW) does not block batch builds; an editor on this project does.
- Scripted shots are not clamped to the dome: keep (height / 11.4)² + (horizontal distance / 9.4)² < 1 from the chamber centre (inside the posts, under the skylight).
- The leaf model (`Resources/Models/LeafInside.fbx`) must stay **readable** (set by `ChamberBuilder.ConfigureImports`): chloroplasts are
  seeded on its cell meshes.
- Atoms carry two materials (body + rim): code that swaps their material must swap the whole `sharedMaterials` set.
- Comments appended to a line must come after its last statement (this morning two `Size`/`Pad` calls were swallowed by a comment).
- Figma: View seat only, no edits possible; `Docs/UI_SPEC.md` is the design record.

## 7. Git and push

Local repository with `.gitignore` / `.gitattributes` copied from the AI OHM'S LAW project (Library, Temp, Builds, Logs, UserSettings and
IDE files are ignored; no LFS; largest file 71 MB, under GitHub's 100 MB limit). Commits: `daf9bf5` baseline (2026-10-07 handoff), then
today's work. **Not pushed:** no GitHub repository exists for this project (checked `chandan-meta/` with several likely names) and none can
be created from this session (no `gh` CLI; the GitHub connector is not authorised). To push: create an empty repository (for example
`chandan-meta/ai-photosynthesis`, no README), then

    git -C "D:/AI unity project/AI PHOTOSYNTHESIS" remote add origin https://github.com/chandan-meta/ai-photosynthesis.git
    git -C "D:/AI unity project/AI PHOTOSYNTHESIS" push -u origin main

## 8. Final checks on the final builds

Both players rebuilt from the final source (dev 434 MB, release 376 MB, 0 errors each):

| Run | Result |
|---|---|
| Dev: UI states | errors=0, 34 shots (Settings rows 48 px, end menu alone on black); Ask-AI reply voice "ok" |
| Dev: simulated touch | drag 67.9°, 1 click → ok |
| Dev: autoplay normal / wrong answers first | errors=0 / errors=0 |
| Release: autoplay | errors=0 |
