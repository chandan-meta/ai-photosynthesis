# Photosynthesis visual and gameplay polish — 9 October 2026

Implemented directly in the existing Unity 6000.3.0f1 / URP project. The existing investigation, scientific explanations, interaction gates, adaptive questions, reteaching, finale and optional exploration remain in their original order. The only new opening step is the requested Start Investigation banner.

## Delivered changes

- Live cinematic banner uses the existing greenhouse, plant and Dr. Curio. Start Investigation launches the original introduction once; How to Play and Settings reuse the existing functional panels. Narration and investigation controls wait for Start. Restart returns to the banner. No new location, gate or gameplay scene was added.
- Removed the generated exterior hills, mountains, forest silhouettes and outdoor ground following the later request. Only the existing skybox appears outside the greenhouse. Indoor flooring, planter soil and educational underground exploration are retained.
- Added 65 packaged surface maps and applied them to real meshes: albedo, tangent-space normals, roughness-derived smoothness, metallic where appropriate, AO and selective emission. Biological materials have no metallic response. Existing imported environment atlases remain in use. Soil, roots, leaf surfaces, tissue, guard cells, vascular structures, grana, membranes and the guide now have material detail beyond grading. See TEXTURE_MANIFEST.md for assignments and source paths.
- Improved the procedural Dr. Curio model: cream shell, better head/body proportions, smoother face geometry, dark glossy visor, cyan eyes, green fabric scarf and paired textured head leaves. Existing talk, scan, point and expression callbacks remain. The sprout uses damped motion during speech/movement; scarf follow-through and welcome gesture add life. Close companion shots remain sharp.
- Added soil clods batched into the existing planter mesh. Improved material response on laboratory wood, beams and floor. Restrained bloom, exposure, saturation and emissive washout improve readability; lighting, existing probes, ambient settings and SSAO support the actual geometry.
- Reduced distracting background molecule density, softened chloroplast and cell emission, reduced overbright xylem highlights, and refined camera depth of field, start transition and glucose motion. The original narration, SFX hooks and learning pacing remain.
- Corrected glucose to a complete folded cyclic β-D-glucose ball-and-stick model. Both its forming preview and final model use the same atom/bond graph. It contains 6 carbon, 12 hydrogen and 6 oxygen atoms, including the ring oxygen and CH₂OH branch. Carbon is gray, oxygen red, hydrogen white. The formula callout now sits outside the molecule rather than covering bonds.

The supplied images showed both open-chain and cyclic glucose. The cyclic reference was used for the existing ring presentation. Geometry comes from [PubChem β-D-glucose, CID 64689](https://pubchem.ncbi.nlm.nih.gov/compound/64689), retained in Tools/Chemistry/BetaDGlucose_64689.sdf. The exporter validates atom counts and valence and converts coordinates to Unity's handedness. This is actual 3D geometry, not a screenshot overlay.

## Inspection and review

Read the entire master prompt; studied all 30 PDF pages, extracted and inspected the reference ZIP, sampled the full supplied gameplay video, reviewed project/handoff documentation, runtime scripts, scenes, assets, URP materials and shaders, and inspected the supplied Blender character. The existing procedural rig was improved because it preserves the established animation and story integration. The source Blender character is retained.

Four review passes covered UI/start behavior, material and character fidelity, motion/voice/VFX pacing, and end-to-end gameplay/build/performance. Discovered issues were fixed and retested. Details and evidence are in PLAYTEST_RESULTS.md.

Actual Unity player screenshots are in BEFORE_AFTER_SCREENSHOTS. AFTER files are raw captures; COMPARE files are clearly labelled before / actual Unity after / art reference. The concept targets can depict different progress states, so comparisons assess appearance rather than identical UI content. AFTER_GLUCOSE_VERIFIED.png shows the final corrected molecule.

## Bug fixes found during review

- Guarded tangent generation on unreadable imported meshes; eliminated startup mesh-read errors.
- Prevented duplicate Start submissions and cleaned up the start gate on developer beat jumps.
- Made error collection begin during initialization so startup failures are counted.
- Stopped background audio rendering cleanly at application shutdown.
- Reproduced a native Windows Unity accessibility-provider crash on quit, resolved its native stack to PlatformAccessibilityNodeRoot::get_HostRawElementProvider, then cleared the hierarchy and disabled the UI document only during shutdown. Three consecutive short quit tests and subsequent banner/stage/full-loop runs passed. Normal-play accessibility settings and Windows settings are unchanged.

## Builds and reproducibility

- Development: Builds/Win/Photosynthesis.exe.
- Release: Builds/WinRelease/Photosynthesis.exe; latest release build succeeded at 2026-10-09 10:54:47 UTC.
- Build the current existing scene with Photosynthesis → Build Windows Player. Do not regenerate the scene with Build Scene merely to play or rebuild: that command replaces scene content.
- Tools/GenerateSurfaceMaps.py regenerates surface maps; Tools/Chemistry/ExportGlucose.py regenerates validated molecular geometry. Tools/ReviewPolish.ps1 supplies review runs. Logs/Polish contains detailed local evidence.
- Existing user edits were preserved. A pre-save scene copy and preexisting-change patch were retained in Logs/Polish.

## Practical limitations

The target images are art-direction renders; the Unity result approaches their materials, palette, composition and guide style but is not pixel-identical. Some microscopic geometry remains deliberately stylized. Native URP materials are used without a new physical subsurface/refraction shader. The character remains the existing upgraded procedural rig rather than a newly exported production FBX rig.

No whole-scene static GI bake was added: the chamber formation, changing sunlight and runtime-generated investigation worlds are dynamic. Existing lighting, probe and pipeline settings were retained and refined where appropriate.

Verification is on Windows PC. Simulated touch was tested; physical tablet/XR hardware was not. Voice clips were audited for availability/levels and the full journey was reviewed at recorded voice pacing; a final human listening review on the deployment speakers remains useful. Reported performance is a local measurement, not a low-end-device guarantee. No external Figma file was modified; the established interface structure was implemented directly in Unity.
