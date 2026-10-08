# Assets & licences (Scene 2+ module)

| Asset | Source | Licence | Use |
|---|---|---|---|
| `Art/Environment/Chamber.fbx` + `Textures/` (+ derived `Materials/`, `RoofPieces.asset`) | the user's `C:\Users\ADMIN\Documents\Received Files\Ai Photoshynthesis1.fbx` (2026-10-08) via `Tools/Chamber/build_env.py` (soil cut from the centre planter, floor split, microscopes decimated, textures capped at 2048/1024 px) | supplied by the user, with the textures packed in it (they include third-party sets, e.g. Poly Haven "vintage wooden drawer 01", CC0; the rest are as licensed to the user — not verified here) | Botanical Investigation Chamber: the lab around the hero plant |
| `Art/Dome/DomeInterior.fbx`, `Art/Dome/DomePieces.asset` | `D:\AI unity project\models\photosyn\dome interior env [1].fbx` (local library) | supplied by client | **no longer used** (replaced by the environment above, 2026-10-08); kept in the repo |
| `Art/Vegetation/TropicalPlants.glb` | local library · Sketchfab "Tropical Plants Pack M02P" by MozzarellaARC | **CC-BY 4.0** — credit required if used | **no longer used** (the new environment brings its own plants); kept in the repo |
| `Resources/Models/LeafInside.fbx` | client ZIP `G7_Photosynthesis_VR1.zip` → `Assets/ActivityFolder/Photosynthesis_Leaf_inside.fbx` | supplied by client | Scene 3 prologue: the hidden world inside the leaf (`LeafInteriorWorld`, re-materialled) |
| `Art/G7/*.fbx` | client ZIP (same) | supplied by client | library copy of every usable module model, inventoried below; not referenced by the scene, so not in builds |
| `Resources/Fonts/Inter-*.ttf` | shipped with Unity 6000.3 editor (rsms/Inter) | SIL OFL 1.1 | UI typography |
| `Resources/Fonts/Cinzel-SemiBold.ttf` (not yet added; Display falls back to Inter SemiBold until it is) | Google Fonts / NDISCOVER Cinzel (static instance) | SIL OFL 1.1 | Display titles |
| Dr. Curio, hero plant, micro worlds, molecules, icons, SFX, music | generated procedurally in code | project-owned | — |
| `Resources/Characters/Curio_ScarfMesh.obj` + `Curio_Scarf.png` | the user's `D:\AI unity project\charcter\dr.blend` via `Tools/Curio/scarf.py`: scarf mesh from the user's Tripo-generated model; fabric baked from the BlenderKit material \"Snooker Pool Table Cloth\" by Textures Can (textures in `charcter\textures\Snooker_Pool_Table_Cloth_*.jpg`) | mesh: supplied by the user (Tripo; plan/licence per the user's account) · fabric: **BlenderKit Royalty Free** (use in products allowed; do not redistribute the source textures) | Dr. Curio's green leaf scarf |
| (branch `feature/tripo-curio` only) Dr. Curio replacement | client file `D:\AI unity project\charcter\dr curio.blend` (Tripo meshes) | supplied by client | not used on main (the user kept the classic robot) |
| `Shaders/MoleculeRim.shader` | written for this project | project-owned | fresnel edge light on molecule atoms |
| Voice-over `Resources/VO/*.wav` | Microsoft Edge neural TTS, voice `en-US-AnaNeural` (child, "cute" category) via `Tools/GenerateVO.py` | synthetic voice, generated for this project | Dr. Curio: 170 story lines (`VO/<id>.wav`) + 272 Ask-AI reply sentences (`VO/kb/<key>.wav`). Replace story lines with recorded VO by dropping same-named WAVs |

Not used (licence/weight): `potato_plants_collection.glb` (CC-BY-**NC** 4.0 — non-commercial, unsuitable for client demos),
`plants_ready.glb` (CC-BY 4.0, 238k tris) and `potted_house_plants.glb` (CC-BY 4.0, 737k tris) — available if a heavier set dressing is wanted.

Credit line, only if the tropical plants come back into the scene: "Tropical Plants Pack M02P by MozzarellaARC (sketchfab.com/mozzarellaARC), CC BY 4.0".

## Client ZIP — `C:\Users\ADMIN\Documents\Received Files\G7_Photosynthesis_VR1.zip` (inventory, 2026-10-07)

A complete Unity 2019.4 VR project (Pico SDK, 6.2 GB with Library). Extracted (Assets/ActivityFolder, VoiceOvers, scenes, settings; no Library)
to `D:\AI unity project\models\G7_Photosynthesis_VR1\`. All module geometry lives in `Assets/ActivityFolder` (Maya, ASCII FBX, cm units).
Every FBX was imported and rendered (`Photosynthesis ▸ G7 Inventory` → `Logs/g7/inventory.md`, `Logs/g7/*_front|side|top.png`).
The supplied textures are 512 px flat noise / tiling fills (CO2.jpg is a water ripple, Green_Brick.jpg a green cloud), so every used
part is re-materialled with the module's URP templates; the geometry is what is kept.

| File (ZIP) | What it is | Tris | Decision |
|---|---|---|---|
| `Photosynthesis_Leaf_inside.fbx` | leaf cross-section block: upper epidermis sheet, palisade "green bricks", spongy cells, vein (xylem `Box_Pipe`, phloem `Red_Pipe`, sheath `Yellow_Rod`, core `Center_Purple`), 11 stomata, animated water spheres | 92k | **Used** — Scene 3 prologue interior (`LeafInteriorWorld`): translucent cell layers, 560 glowing chloroplast lentils, sun shafts, motes; animation dropped |
| `Chloroplast 1.fbx` | cut-open chloroplast (bowl with grana cylinders, inner/outer membranes) + 4 molecule spheres, 16.7 s animation | 181k (134k-vert membrane) | not used — the procedural `ChloroplastWorld` already has a richer cutaway (membranes, grana, lamellae, reaction site) and the mesh is too heavy for a floating cell chloroplast |
| `Co2_And_O2.fbx` | 24 s animation: CO₂ spheres in / O₂ out through stomata | 53k | not used — the interactive `LeafSurfaceWorld` CO2Enter / O2Exit covers it |
| `Chloroplast_co2_and_water.fbx` | 5.7 s animation: 25 H₂O + 15 CO₂ spheres flying in | 25k | not used — `ChloroplastWorld.FeedIngredients` is learner-driven |
| `Glucose_Anim(1).fbx` | 9.4 s: 32 spheres assembling | 17k | not used — `FormGlucose` builds the ring interactively |
| `Modles/CarbonDioxide/CO2_Molecule.fbx`, `O2_Molecules.fbx`, `H_molecules.fbx`, `Molecules.fbx`, `water_Mol_Anim.fbx`, `NADP.fbx` | plain sphere molecules (no bonds), O₂ / H / NADP collapsed to ~0 scale | 1–14k | not used — the procedural ball-and-stick molecules (glossy atoms, bonds, halos) read better and are already colour-coded to the Figma tokens |
| `Stomota/Stomata_10.fbx` | guard-cell pair, 848 tris, opening animation | 0.8k | not used — `LeafSurfaceWorld` guard cells animate with the magnification levels |
| `HL.fbx`, `Green brick highlight.fbx` | single palisade cell / brick | 1k | not used (highlight props of the old VR flow) |
| `Photosynthesisenviroment.fbx` | outdoor garden: pergola, stone border, pots, fence, one house plant, root/soil/water props | 55k, 146 MB | not used — the storyboard environment is the Botanical Investigation Chamber (Scene 2); removed from Assets, kept in the extracted copy |
| `Resources/VoiceOvers/{en-GB,hi-IN,mr-IN,as-IN}` | 38 MP3 lines per language for the old VR flow (Inst_*, Desc_*, Quiz*) | — | not used — a different script; could seed localisation later |
| `Mirza Beig/Particle Systems`, `_Creepy_Cat` weather, `FM_ExhibitionToolPack`, `PicoMobileSDK`, `QWR`, `VRPanorama` | third-party packs | — | not used (licensing unverified, VR/Pico specific) |
