# Biological material upgrade — 10 October 2026

Project: `D:/AI unity project/AI PHOTOSYNTHESIS`. Working branch: `codex/root-material-upgrade`, based on `8902082`. This report covers the new eight-page Photosynthesis_Reference.pdf and AI_Photosynthesis_Codex_Master_Prompt.md brief, separately from the previous banner/character upgrade.

## Changes and scope

Saved 50 real texture images, eight material assets and two URP HLSL shaders. An AI-generated flat root surface contributes longitudinal microvariation; deterministic UV-aligned recipes supply cell relief, normal, roughness, AO, thickness and channel packing. Root-hair, vessel, atom, connector and water maps are procedural. The source PNG and reproducible recipe remain in the project. Reference-board swatches and gameplay screenshots are never used as surface maps.

Clue 02 retains the diagonal root mesh, hair paths, water-film geometry, molecular spawn positions, picking colliders, root callout and all transport animations. The root now uses pale matte epidermis with restrained transparency, and the cream hairs use opaque thin-tissue lighting. Clear water uses low opacity, Fresnel reflections and animated normal sampling. Small mineral grains use rough earthy surfaces instead of glass. Neutral underground fill improves tissue legibility; camera, exposure and post-processing remain unchanged.

Vessel-wall maps reach the stele and named xylem/phloem transport meshes through the existing SurfaceArt binding. Existing material instances retain their scripted color/emission changes. Shared atom materials use oxygen red, hydrogen white and carbon dark gray, with zero metallic response and no permanent emissive rim draw. CO₂ and O₂ now have explicit paired bond sticks; H₂O is bent at 104.5°. The already validated cyclic β-D-glucose graph retains all 24 atoms and 24 single bonds.

The greenhouse, start banner, Dr. Curio geometry/rig/voice/placement, UI, narration text, story order and interactions are unchanged. No scene, prefab identity, animation controller, Timeline binding or functional parent is replaced. Figma edits were unnecessary because the new brief locks the existing interface.

## Model decisions

Every model in neww was inspected, including its UVs, hierarchy, imported triangle count and animation clips. The read-only Unity audit resolves differences from Blender polygon counts.

| Candidate | Unity meshes / triangles | Decision and evidence |
|---|---:|---|
| neww/LeafInside.fbx | 11 / 240,821 | Retain as source. Static cutaway with generic names is not a clear improvement over the active anatomy. The active 91-part, 91,744-triangle model keeps semantic palisade/spongy hosts, guard cells and vessel bindings used for chloroplast placement. |
| neww/chloroplast.glb | 3 / 243,464 | Retain as source. Fused static stacks/contents cannot replace the independently animated membranes, cap, grana, reaction site and products without rebuilding working anatomy. No clips to retarget. |

Original source files and GUIDs are preserved. New animation work: none; existing absorption, gas paths, stomata, membrane opening and reaction animations are reused.

## Maps and materials

New texture root: `Assets/Photosynthesis/Art/Upgraded/Textures/`. Material root: `Assets/Photosynthesis/Art/Upgraded/Resources/UpgradedMaterials/`. Source: `Assets/Photosynthesis/Art/Upgraded/Sources/RootEpidermis_AI.png`. Recipe: `Tools/GenerateBiologyMaps.py`. All individual saved filenames are listed in ROOT_UPGRADE_SCREENSHOTS/texture_files.json.

Albedo is sRGB; normals use Unity NormalMap import; all roughness/AO/thickness/packed masks are linear. Mipmaps, repeat wrapping, anisotropy 4 and HQ compression are enabled; CPU readability is disabled. MetallicSmoothness R is zero, A is one minus roughness; AO samples G. Thickness samples R. Height and standalone roughness are retained sources, without runtime parallax. Assigned maps are referenced by actual material assets; unused source maps live outside Resources.

Root epidermis is 2048², vessel walls 1024², hairs 512², and atoms/connectors/water 256². The actual 1080p camera does not justify 4K here; the smaller repeated materials avoid unnecessary memory cost. Previous UV-specific leaf, guard-cell, grana, cell membrane and soil maps remain in use; see TEXTURE_MANIFEST.md for those retained paths.

| Scene / asset | Decision / source | New map family | Material / shader | Animation / integration |
|---|---|---|---|---|
| Clue 02 root and cap | Keep / SoilWorld tube and sphere | RootEpidermis | RootEpidermis.mat / BiologicalSurface.shader | Same transforms; epidermis U tiling 18, length V unchanged |
| Clue 02 root hairs and hero hair | Keep / merged hair tubes | RootHair | RootHair.mat / BiologicalSurface.shader | Same hair samples, callout and absorption anchors |
| Clue 02 water films / meniscus | Keep / merged shells and bridge | WaterFlow normal | ClearWater.mat / ClearWater.shader | Same flow locations and timing |
| Stele, xylem and phloem | Keep / transport meshes | VesselWall | VesselWall.mat and existing runtime materials / URP Lit | Existing reveal, flow, dimming and arrival callbacks |
| Molecules across all scenes | Keep / Fx spheres, cylinders and glucose JSON | AtomC, AtomO, AtomH, Connector | Matching .mat assets / URP Lit | Same roots, colliders and paths; bond rendering corrected |
| Soil and mineral grains | Keep / merged original grains | Retained Soil family | Existing runtime URP Lit materials | Same geometry and water attachment sites |
| Leaf, stomata, cells and chloroplast | Keep / active scientific sets | Retained leaf, Guard, Membrane, Grana families | Existing URP materials | Original labels, opening, dive and reaction bindings |

`Assets/Photosynthesis/Art/Upgraded/Shaders/BiologicalSurface.shader` uses native URP PBR lighting, mapped normals/roughness/AO and a restrained thickness-based backlight approximation. It supports the configured Forward+ light loop and forward tier. `ClearWater.shader` provides one front-face transparent pass, controlled Fresnel and flowing normals without adding an opaque-texture/refraction dependency. Both use UnityPerMaterial buffers and instancing variants.

## Review evidence

Before/after raw 1920×1080 Unity captures: ROOT_UPGRADE_SCREENSHOTS/BEFORE_WATER.png and AFTER_WATER.png. COMPARE_WATER.jpg is a clearly labelled side-by-side presentation, not a game asset. Camera/exposure and UI composition match; underground illumination and materials intentionally differ.

All four review passes are complete. The aborted preliminary run and black hidden-window screenshot are excluded from acceptance evidence.

| Review pass | Verification | Result |
|---|---|---|
| 1 — UI/UX and entry | Existing UI state captures; banner gate, Start, repeated submit, reset and debug jump; touch drag/tap | PASS, zero reported errors. Touch moved 67.9° and registered one tap. UI and banner structure retained. |
| 2 — Materials and fidelity | Actual Unity root/hair/water UVs, tangents, shader support and map bindings; all 50 PNGs and import metadata; all 24 beat screenshots; candidate model audit | PASS. No missing/pink shaders observed; model-retention decisions above preserve anatomy and animation bindings. |
| 3 — Pacing and effects | Full original-voice run from entry through FreeExplore; absorption, xylem, stomata, dive, reaction and products | PASS, 498 seconds, 49 captured frames, zero reported errors. Existing narration and timing retained. |
| 4 — End-to-end and build | Wrong-first answers and reteaching through completion; development/release builds; native release water click and Ask AI | PASS, zero reported errors. Clicking water triggered absorption. Asking “What is xylem?” displayed the correct explanation and played Curio's voice. |

Sequential automated runs: full_voice 498 s; all_beats 172.7 s; wrongfirst_perf 150.6 s; ui 52.2 s; touch 16.1 s; banner 16.1 s. Each exited with code 0 and reported zero errors. The additional native release interaction exited with code 0 after its 120-second capture. Logs and full capture sets remain under Logs/Polish/RootUpgrade/; selected raw screenshots and the 24-beat review sheet are saved alongside this report.

Chemistry assertions passed: CO₂ C₁O₂ / four double-bond sticks, H₂O H₂O / two sticks / 104.50° angle, O₂ / two double-bond sticks, and glucose C₆H₁₂O₆ / 24 single bonds. The existing glucose export also passed carbon/oxygen/hydrogen valence checks. Runtime and editor C# compilation passed with zero errors; the existing MSB3539 output-directory warning remains.

Windows development and release builds succeeded with zero errors (reported build sizes: 443 MB development, 384 MB release). Executables: Builds/Win/Photosynthesis.exe and Builds/WinRelease/Photosynthesis.exe. URP shader compilation covered D3D11/D3D12; the native release interaction used D3D11. No scene, prefab, UI, dialogue or story sequence assets changed.

Performance evidence is the sequential 1920×1080, uncapped Performance-quality run on RTX 5060 Ti 16 GB, driver 32.0.15.9186. After startup/loading samples (0 and 2 FPS), recorded active samples stayed at or above 470 FPS; Water sampled 966/979 FPS with 232/233 draws and 47/48 SetPass calls. Peak sampled scene counts were 993 draws, 134 SetPass calls and 3,778k triangles. These are periodic samples, not frame-time percentiles or a claim about slower devices. No comparable pre-change benchmark was taken. Raw samples are in ROOT_UPGRADE_SCREENSHOTS/performance_samples.json.

Development profiling reported 34 unique textures assigned to SoilWorld renderers, using 59,727,440 bytes (about 57 MiB). This is scoped texture memory, not total game/GPU memory. The release memory counter returned incomplete data and is excluded. Player startup's D3D12 debug info-queue query warning did not prevent rendering or completion.

## Practical limits

This is a material upgrade on the approved meshes; their silhouettes and low-poly geometry remain. Biological transmission is an economical lighting approximation, not volumetric subsurface scattering. Water has no physical refraction/depth absorption; low opacity protects lesson readability. The custom tissue shader does not add a separate shadow-caster/depth-normal pass. Baked GI was not introduced because the existing worlds and lighting are built and animated at runtime. Exact GPU overdraw and mobile/WebGL/Quest device behavior require device-specific profiling; desktop rendering alone cannot establish those results.

Classroom molecules use illustrative atom sizes and magnification. Blue selection halos and the revealed transport path remain intentional teaching cues; the atoms themselves are non-emissive. No pixel-identical match to concept boards is claimed.
