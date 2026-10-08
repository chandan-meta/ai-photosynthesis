# Handoff — Photosynthesis module, 2026-10-08

Read this first. It records where the work against the client's 2026-10-07 master prompt stands, what is verified, and what is left.
Project: `D:\AI unity project\AI PHOTOSYNTHESIS` (Unity 6000.3.0f1, URP 17.3, UI Toolkit, Input System).
Git: local repository on `main` (baseline `daf9bf5` = the 2026-10-07 handoff state, then today's commit). **No remote yet** (section 7).

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
- Scripted shots are not clamped to the dome: keep (height / 10.3)² + (horizontal distance / 12)² < 1 from the chamber centre.
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
