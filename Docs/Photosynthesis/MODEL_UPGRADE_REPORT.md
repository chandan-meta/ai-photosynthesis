# Supplied neww models — integration

This follow-up implements the user's explicit request to replace the leaf and chloroplast models from `Assets/Photosynthesis/Art/G7/neww`. It supersedes the retention decisions in ROOT_UPGRADE_REPORT.md. Original source models remain intact; their geometry and native texture atlases are now used in gameplay.

## Active replacements

- LeafInteriorWorld loads `UpgradedModels/LeafInside`, a prefab referencing the supplied LeafInside.fbx. All eleven source mesh parts retain their UVs and original albedo/normal/roughness atlases. Palisade and spongy hosts are named explicitly for the existing chloroplast placement/pulse. The established glide, repeated tissue vista and educational sequence remain.
- ChloroplastWorld loads `UpgradedModels/Chloroplast`. `Tools/PrepareNewwModels.py` welds seam duplicates without discarding per-corner UVs, separates loose geometry, splits the double envelope into lower membranes and upper caps, and exports an animated-parts FBX. The source GLB's real geometry replaces the previous procedural membranes, thylakoids, starch and stroma contents.
- Source stack positions define 23 reaction anchors. Existing sunlight, water/CO₂ feeding, reaction, glucose formation, use and oxygen release now target the supplied internal layout. The original 2.5-second double-envelope peel is reused; no new Animator or narration delay is added.
- CellWorld's selected/enlarged chloroplast uses the same detailed supplied model. Small streaming background organelles retain their inexpensive meshes. Selection, cyclosis, enlargement, release and replay retain their original parent transforms and API.

## Assets and reproducibility

Prepared mesh/manifest/native maps: `Assets/Photosynthesis/Art/Upgraded/Models/`.
Saved prefabs and URP materials: `Assets/Photosynthesis/Art/Upgraded/Resources/UpgradedModels/`.
Run Blender with `Tools/PrepareNewwModels.py`, then Unity menu **Photosynthesis → Polish → Integrate neww models**. Blender packs supplied roughness into URP smoothness alpha; the importer checks scale, UVs and tangents and preserves native atlas colors. There are 37 native/packed texture maps, 19 materials and two saved prefabs. Original source GUIDs and bytes are retained; the leaf importer enables CPU readability for existing cell-host placement.

Both supplied files have no animation clips. Their required movement is supplied by the existing story coroutines: leaf glide/pulse, selected-organelle enlargement, membrane opening, light-capture highlight, ingredient paths and product animation.

## Verification

- Unity 6000.3.0f1 development and release Windows builds succeeded after the final code change (2026-10-10 07:19 UTC).
- Final voiced sequence from EnterLeaf through FreeExplore: 269.2 seconds, zero logged errors, exit code 0. Actual 1920×1080 frames verify selected-organelle enlargement, membrane opening, reaction, glucose and oxygen. Narration/UI/learning sequence were retained.
- Full start-to-finish autoplay with wrong answers/reteaching, Performance quality and 4× test-only simulation speed: 150.6 seconds, zero logged errors, exit code 0.
- Release D3D11 chloroplast/reaction smoke test: 38.2 seconds, zero logged errors, exit code 0.
- Final all-stage review: 24 Unity screenshots, zero logged errors, exit code 0. The leaf overview screenshot uses this final build; the cell, chloroplast and glucose screenshots use the final voiced run.
- `-modelcheck` verifies 33 imported leaf renderers (three tissue slabs), eight chloroplast mesh groups, UV/tangent completeness, native texture bindings, supported shaders and two complete double-envelope opening replays. All passed in the final runs. The 23 reaction anchors bind to the supplied stack layout.
- `-sciencecheck` confirms CO₂, H₂O, O₂ and C₆H₁₂O₆ atom counts and the water bond angle; all passed. The existing glucose model was retained.
- Desktop performance samples on the RTX 5060 Ti: leaf interior 186 draw calls/819k rendered triangles; chloroplast samples 202–428 draw calls/561k–1125k rendered triangles. Counters include effects/additional passes and the isolated test model. These are desktop diagnostics, not mobile/VR guarantees.

An initial all-stage screenshot run completed its captures with zero gameplay errors but crashed during native Windows UI Automation shutdown (`PlatformAccessibilityNodeRoot::get_HostRawElementProvider`). The HUD now unregisters accessibility/UI providers before permitting window destruction, allowing 100 ms for provider-removal events. Accessibility remains available during gameplay. Four subsequent final-build runs exited cleanly; this is evidence of mitigation rather than proof against every Windows accessibility configuration. An interrupted review affected by an overlapping rebuild was discarded and rerun against fresh player outputs.

Final logs are in `Logs/Polish/ModelUpgrade/final_*.log`; the run summary is `final-results.txt`. Raw Unity screenshots are in [MODEL_UPGRADE_SCREENSHOTS](MODEL_UPGRADE_SCREENSHOTS/):

- [Leaf cross-section](MODEL_UPGRADE_SCREENSHOTS/LEAF_INTERIOR.png)
- [Selected chloroplast](MODEL_UPGRADE_SCREENSHOTS/SELECTED_CHLOROPLAST.png)
- [Opened chloroplast](MODEL_UPGRADE_SCREENSHOTS/CHLOROPLAST_OPEN.png)
- [Glucose in the supplied chloroplast](MODEL_UPGRADE_SCREENSHOTS/GLUCOSE.png)

## Limits

The supplied source is an illustrative static cutaway, adapted to the established gameplay envelope. Its envelope opening is a teaching reveal, not a literal biological membrane rupture. Small background organelles intentionally use the existing low-detail meshes. GPU/device-specific mobile/VR performance is not established by Windows testing.
