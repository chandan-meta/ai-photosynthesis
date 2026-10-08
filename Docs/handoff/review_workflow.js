export const meta = {
  name: 'photosynthesis-multipass-review',
  description: 'Five specialist review passes over the Photosynthesis module build evidence, findings adversarially verified',
  phases: [
    { title: 'Review', detail: 'gameplay/education · UI/UX · cinematic/visual · audio/polish · performance/QA' },
    { title: 'Verify', detail: 'two skeptics per finding' },
  ],
}

const ROOT = 'D:/AI unity project/AI PHOTOSYNTHESIS'
const COMMON = `
You are reviewing the Unity 6 (URP, UI Toolkit) educational module "Photosynthesis — The Hidden Food Factory" at ${ROOT}.
Read Docs/README.md, Docs/ARCHITECTURE.md and the master requirements summary below first. Evidence you MUST look at (use the Read tool on PNG/JPG files; they are screenshots):
- Beat screenshots: ${ROOT}/Logs/shots/*.png (one per story beat, taken a few seconds into each beat)
- Real-pacing filmstrips as contact sheets: ${ROOT}/Logs/filmsheets_intro/sheet_*.jpg (Scene 2–3 opening), ${ROOT}/Logs/filmsheets_finale/sheet_*.jpg (Scene 14 finale → end menu → Review/Free Explore). Each tile is labelled frame_seconds_Beat.
- UI state shots: ${ROOT}/Logs/uishots/*.png (every panel/card state at 1920x1080)
- Player logs: ${ROOT}/Logs/player-autoplay.log, player-autoplay-wrong.log, player-shots.log, player-uishots.log, player-perf.log (lines starting with [Photosynthesis] and [Perf]; "errors=0" markers)
- Code: ${ROOT}/Assets/Photosynthesis/Scripts/Runtime (Story/Story.cs is the script of the module; Core/CameraDirector.cs; Core/Dialogue.cs; Audio/*.cs; UI/*.cs; Micro/*.cs; World/*.cs), lines in Resources/curio_lines.txt, voice clips in Resources/VO.
Master requirements (client, 2026-10-07): cinematic + interactive + beautiful + scientifically clear + cute + premium; the module must END at "This is Photosynthesis." with nothing mandatory after it (a quiet end menu Replay/Review/Exit only); cinematic opening (hero plant → sunlight → push to leaf → through the surface → inside-the-leaf reveal using the client's leaf model); camera always on the narrated subject, no random moves; cute excited child voice, no dead air; molecules not plain spheres; tasteful VFX/lighting/post, no bloom overuse, DoF only for cinematic moments; mouse + touch only (no hover/keyboard/right-click/multitouch required); touch-sized targets; UI must not cover the centre; smooth performance on PC, practical on tablets.
Report ONLY concrete, evidence-backed findings (cite the exact screenshot/frame label, log line or file:line). No praise, no generic advice. Each finding: what is wrong, why it matters to a learner, the smallest fix (file + what to change). Severity: blocker (breaks the flow/requirement), major (clearly hurts quality), minor.
`
const FINDINGS = {
  type: 'object',
  properties: {
    findings: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          title: { type: 'string' }, severity: { type: 'string', enum: ['blocker', 'major', 'minor'] },
          evidence: { type: 'string' }, why: { type: 'string' }, fix: { type: 'string' }, file: { type: 'string' },
        },
        required: ['title', 'severity', 'evidence', 'why', 'fix'],
      },
    },
    summary: { type: 'string' },
  },
  required: ['findings', 'summary'],
}
const VERDICT = { type: 'object', properties: { refuted: { type: 'boolean' }, reason: { type: 'string' }, severity: { type: 'string', enum: ['blocker', 'major', 'minor'] } }, required: ['refuted', 'reason'] }

const PASSES = [
  { key: 'gameplay', prompt: COMMON + `
PASS 1 — GAMEPLAY & EDUCATION REVIEWER. Play the module in your head using Story/Story.cs beat by beat, the curio_lines.txt script, the beat screenshots and the two filmstrips. Evaluate engagement, clarity, pacing, educational logic, interactions (does each interaction strengthen understanding?), confusion points (does the learner always know what to do next?), boring pauses, repetition, and whether the flow truly ends at "This is Photosynthesis." with nothing mandatory after it. Check that every scripted line is shown WITH its subject in frame (read the Story code around each Say). Up to 12 findings.` },
  { key: 'uiux', prompt: COMMON + `
PASS 2 — UI/UX REVIEWER. Review only UI/UX using Logs/uishots, Logs/shots, the filmsheets and UI/Hud.cs, UI/HudLearning.cs, UI/HudSystem.cs, UI/HudKit.cs, Resources/UI/Hud.uss, Docs/UI_SPEC.md. Evaluate readability (contrast, sizes at 1080p and on a 1366x768 laptop), hierarchy, touch targets (>= 44 px), visual obstruction of the 3D subject (does any panel cover the narrated subject or the centre?), button placement, subtitle placement, responsiveness, consistency with the Figma tokens in UI_SPEC, the new end menu and finale title, the Factory controls button in Free Explore, and touch support in Core/CameraDirector.cs (anything still requiring hover, right-click, keyboard or multitouch to progress?). Up to 12 findings.` },
  { key: 'cinematic', prompt: COMMON + `
PASS 3 — CINEMATIC & VISUAL REVIEWER. Review camera, lighting, materials, molecules, shaders, VFX, transitions, post-processing and composition using the filmsheets (every tile), Logs/shots, Core/CameraDirector.cs, Story/Story.cs (CurioIntro prologue, GoTo transitions, Finale), Micro/LeafInteriorWorld.cs, Core/Fx.cs (molecules), World/Chamber.cs lighting, Editor/PhotosynthesisBuild.cs (volume profile). Ask of every shot: is the narrated subject framed and sharp, is anything blocking it, is motion motivated, is bloom/flare overused, does the inside-the-leaf reveal read as a living biological place, does the finale pull-back + title have a wow factor, does the camera ever clip through geometry (dome ribs/glass) or end inside translucent geometry? Up to 12 findings.` },
  { key: 'audio', prompt: COMMON + `
PASS 4 — AUDIO & FINAL POLISH REVIEWER. Review the voice (Resources/VO/*.wav generated by Tools/GenerateVO.py with edge-tts en-US-AnaNeural; check clip durations with ffprobe via Bash for silence/over-long lines; check curio_lines.txt for lines that read badly aloud or chemistry that TTS mispronounces), narration timing in Core/Dialogue.cs and Story.cs (dead air, lines overlapping camera moves, waits), music states and ducking in Audio/AudioDirector.cs and Audio/Synth.cs (does the finale resolve? does the intro build?), SFX coverage for the new prologue and finale moments, and the climax/final reveal ordering. Up to 12 findings.` },
  { key: 'qa', prompt: COMMON + `
PASS 5 — PERFORMANCE & BUG QA. Read the player logs for exceptions, errors and the [Perf] lines (fps, draw calls, setpass, tris per beat; the test machine is an RTX 5060 Ti so judge headroom for a tablet-class GPU). Review the new code for bugs: Story.cs Finale/Play/CurioIntro/Explore "Experiment", Core/CameraDirector.cs touch + DoF, Micro/LeafInteriorWorld.cs, UI/HudLearning.cs EndMenu, Core/Module.cs. Look for: coroutines left running after Run(), null refs on debug jumps (-beat X) into the new beats, double-fired input (touch + synthesized mouse), the end menu/fade state after Replay/Review, DoF left active, rapid clicks, pause during the finale, aspect ratios, missing references, GC/alloc per frame in LateUpdate. Reproduce claims with grep/Read, not guesses. Up to 12 findings.` },
]

phase('Review')
const WANT = args && args.passes ? args.passes : null
const RUN = WANT ? PASSES.filter(p => WANT.includes(p.key)) : PASSES
log(`running passes: ${RUN.map(p => p.key).join(', ')}`)
const results = await pipeline(
  RUN,
  p => agent(p.prompt, { label: `review:${p.key}`, phase: 'Review', schema: FINDINGS, effort: 'high' }),
  (r, p) => (r ? r.findings.map(f => ({ ...f, pass: p.key })) : []),
  fs => parallel(fs.map(f => () =>
    parallel(['correctness', 'learner-impact'].map(lens => () =>
      agent(COMMON + `
VERIFY (lens: ${lens}). A reviewer claims: "${f.title}" [${f.severity}, pass ${f.pass}]. Evidence: ${f.evidence}. Why: ${f.why}. Proposed fix: ${f.fix}.
Try to REFUTE it by checking the cited evidence and code yourself (Read the screenshot/frame, grep the code). Refuted = the claim is false, already handled in the code, not visible in the evidence, or the fix would break a stated requirement. If uncertain, refuted=true. Also give your own severity.`,
        { label: `verify:${f.pass}:${lens}`, phase: 'Verify', schema: VERDICT, effort: 'medium' })))
      .then(vs => ({ ...f, votes: vs.filter(Boolean), survives: vs.filter(Boolean).filter(v => !v.refuted).length >= 2 })))),
)
const all = results.flat().filter(Boolean)
const confirmed = all.filter(f => f.survives)
log(`${all.length} findings, ${confirmed.length} confirmed`)
return { confirmed, rejected: all.filter(f => !f.survives).map(f => ({ title: f.title, pass: f.pass, reasons: f.votes.map(v => v.reason) })) }
