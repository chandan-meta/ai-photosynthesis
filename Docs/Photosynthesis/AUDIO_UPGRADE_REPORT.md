# Licensed soundtrack replacement and audio integration

The requested seven tracks are downloaded and integrated into the existing AudioDirector. Its old procedural music is no longer generated or played. Narration files, speech/TTS code, procedural SFX, characters, animations, learning sequence and saved volume preference keys are retained. No second audio manager or additional scene was introduced.

## Files and assignments

All playback assets below are under `Assets/Photosynthesis/Audio/Resources/Soundtrack/`. Original official MP3 downloads remain under `Assets/Photosynthesis/Audio/Originals/`.

| Exact track | Playback asset | Assignment |
|---|---|---|
| Dreamy Flashback — Kevin MacLeod | BGM_Cinematic_Intro.ogg | Start banner, introduction, cinematic reveals and sunlight reveal |
| Light Thought var 2 — Kevin MacLeod | BGM_Main_Exploration.ogg | Main exploration, explanations, leaf/cell, assessment and free exploration |
| Silver Blue Light — Kevin MacLeod | BGM_Root_Water.ogg | Clue 02, roots, water absorption, xylem ride; existing oxygen transport ride |
| Cipher — Kevin MacLeod | BGM_Scientific_Discovery.ogg | Chloroplast, reaction and glucose discovery |
| Carefree — Kevin MacLeod | BGM_DrCurio_Playful.ogg | Existing Curio introduction/scanner gag only, followed by the cinematic cue |
| Heartwarming — Kevin MacLeod | BGM_Completion.ogg | Normal Finale and optional completion screen; non-looping |
| Forest Ambience — TinyWorlds | AMB_Forest_Nature.ogg | Greenhouse/natural surroundings, on the separate ambience bus |

Incompetech's official catalog verified all six supplied ISRC/title pairs. Cipher's official filename is **Cipher2.mp3**. Other original filenames match the music titles; the ambience original is **Forest_Ambience.mp3**. No unrelated track or unlicensed mirror was substituted. SHA-256 hashes, source URLs, formats, measured levels and edits are recorded in `Audio/soundtrack_manifest.json`.

## Existing audio audit and replacements

The existing Story/Module state calls owned all BGM playback. The audit found no separately authored scene/prefab music AudioSources, AudioMixer assets or Timeline audio tracks to replace. AudioDirector's eight procedural music states now resolve to the licensed clips. `Runtime/Audio/Synth.cs` retains the original music generator as a backup, but AudioDirector no longer queues it. The existing micro/soil/xylem/dark/cell ambience generators remain; the chamber bed now uses Forest Ambience. Forest ambience fades out underground and in microscopic environments. Existing intentional darkness silence remains.

## Controller and mixer

`Audio/Resources/PhotosynthesisSoundtrack.asset` exposes seven AudioClip assignments, four mixer group assignments and mixing settings in the Inspector. `PhotosynthesisAudio.mixer` has Master → Music / Voice / SFX / Ambience, with an All audio mixer view. Existing `ps.vol.music`, `ps.vol.sfx` and `ps.vol.voice` preferences and UI sliders continue to work.

- Music multiplier 0.25 × existing default music slider 0.6 gives nominal source gain 0.15. Banner uses 70% of that gain.
- Ambience multiplier 0.125 × default effects slider 0.8 gives nominal gain 0.10, reduced further under narration.
- Voice and SFX gains are unchanged and routed to their separate groups.
- Crossfades default to two seconds and stay within 1.5–2.5 seconds. Related states sharing Light Thought retain playback position. Repeated state requests do not restart music. Only two music sources exist.
- Actual VoicePlaying/recorded voice/TTS state drives −10 dB music ducking: 0.2-second attack and 0.9-second release, plus the existing 0.6-second hold between lines.
- Music/ambience fade progress freezes while AudioListener is paused. Reset restores the banner cue through the existing flow; free exploration restores Light Thought. Heartwarming never loops.

The first complete review showed that normal gameplay ends through Finale and bypasses optional Complete. Heartwarming was therefore added to Finale too, without changing waits, narration, activities, camera animation or UI timing.

## Preparation, import and licences

`Tools/PrepareSoundtrack.py` validates decoding and preserves the original hashes. It creates separate stereo 44.1 kHz Vorbis playback copies, with approximately −20 LUFS music and −24 LUFS forest ambience. Peak safety limits Silver Blue Light to approximately −20.7 LUFS. All prepared files contain exactly one audio stream, no video/artwork, and measured true peaks remain below clipping. The first Heartwarming conversion contained embedded cover art; it was rebuilt audio-only and the conversion recipe fixed before successful import.

Five looping music copies use a two-second tail/head overlap and rotated start so the playback boundary joins contiguous source material. Forest Ambience already loops and is gain-adjusted only; Heartwarming is not loop-edited. Boundary jumps fell within each recording's normal adjacent-sample range. This objective continuity check does not certify subjective musical phrasing.

Unity menu **Photosynthesis → Audio → Import licensed soundtrack** recreates import settings and assignments. Default, Standalone, Android and iOS use streaming Vorbis, quality 0.8, preserved sample rate, stereo, background loading and no preload/full decompression. A compressed-in-memory WebGL override is requested where supported by the installed importer; browser/device builds were not verified. This project's actual tested build target is Windows.

Attribution and modification notes are in [MUSIC_CREDITS.md](../../Assets/Photosynthesis/Audio/MUSIC_CREDITS.md), the manifest and runtime MusicCredits TextAsset. **Menu → Music credits** exposes these credits to learners without replacing existing documentation. Sources: [Incompetech licensing](https://incompetech.com/music/royalty-free/licenses/) for CC BY 4.0, and [TinyWorlds Forest Ambience](https://opengameart.org/content/forest-ambience) for CC0 1.0.

## Four review passes and actual results

1. **Download verification:** all seven exact official files decoded successfully; identity/license matching, original hashes, loudness measurements and audio-only playback copies passed. No HTML/error page was imported as audio.
2. **Unity integration:** seven streaming clips and four mixer routes passed importer/runtime validation. Final development/release Windows builds succeeded after the Finale cue fix at 08:07:54 / 08:08:26 UTC on 2026-10-10. Focused playback passed 31 checks: six cue decoders, five loop wraps, non-looping completion, repeated/related-state continuity, pause/resume, original voice playback, duck/release, separate forest ambience, environment switch and reset/source count.
3. **Gameplay:** the initial full voiced module passed in 499.4 seconds, exit 0, zero logged errors. Initial wrong-answer/reteaching review passed in 149.1 seconds. After the Finale assignment fix, Direct3D 11 voiced ending through free exploration passed in 64.6 seconds, exit 0; full wrong-answer/reteaching sequence passed in 149.1 seconds, exit 0, including all six music tracks and Heartwarming on the normal ending. Release focused review passed all 31 checks in 50.3 seconds, exit 0. All these runs recorded zero clipped output samples.
4. **Mix/performance:** full voiced listener peak was 0.547; full wrong-answer peak was 0.645, with zero clipped samples. Maximum measured streamed music AudioClip memory was 581 KB in development runs; release memory counters returned zero/unavailable and were not treated as valid measurements. Final Direct3D 11 whole-player peak working sets were 805 MB (voiced ending), 793 MB (full performance run) and 575 MB (release focused review). Sampled whole-player CPU times were 32.8, 500.1 and 18.1 seconds; the uncapped performance test intentionally consumes more CPU. These are desktop diagnostics, not isolated audio CPU measurements or mobile guarantees.

The release credits panel was inspected on screen at 1920×1080. Track/source/licence text, scrolling and Done returning to Pause worked; an actual [Unity screenshot](AUDIO_UPGRADE_SCREENSHOTS/CREDITS_PANEL.png) is saved. Logs/results/mix summaries are under `Logs/AudioUpgrade/`, including `results.txt`, `final-results.txt` and `final-mix-summary.json`. `-audiocheck` observes gameplay; `-audiotest` exercises audio without saving preference changes. The listener probe measures output without changing samples.

## Remaining limitations

- One final D3D12 voiced-ending run completed with zero gameplay/audio errors but faulted in **D3D12Core.dll** during process shutdown, exit −1073741819. The event is saved in `D3D12-shutdown-event.txt`.
- Closing the interactive credits test with Alt+F4 reproduced a separate native **UnityPlayer/Windows-message shutdown exception**, exit −1073740771, after successful UI verification and zero in-game audio errors. Its crash/event is retained in the logs. This is a failed shutdown test, not a clean pass. Automatic Direct3D 11 exits passed; interactive closing still requires engine/platform investigation.
- Neither native shutdown issue is claimed fixed. Render defaults, accessibility and the existing quit system were preserved for this audio task. Direct3D 11 can be selected with `-force-d3d11`.
- No human listening approval, subjective perfect-phrase loop guarantee or mobile/VR/WebGL certification is claimed. The pre-existing SFX background worker also needs platform work before a WebGL port.
- Generated model-review and audio-UI captures were moved to `C:/Users/ADMIN/.codex/audio-upgrade-review-backup` to relieve Unity's low-disk warning; the saved report screenshots, source assets and audio logs remain available.
