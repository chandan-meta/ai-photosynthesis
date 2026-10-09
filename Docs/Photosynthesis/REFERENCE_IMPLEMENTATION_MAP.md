# Reference implementation map

Entry scene remains `Assets/Photosynthesis/Scenes/Scene02_BotanicalChamber.unity`. Every microscopic view is an existing runtime world, not an added gameplay scene. Existing Beat order, clues, questions, callbacks, narration text, camera destinations, picking anchors and finale remain intact.

| PDF page / target | Updated Unity source / assets | Camera and comparison state |
|---|---|---|
| 3 Start banner | Module.ShowStartScreen/LaunchInvestigation; HudSystem.ShowStartBanner; actual Chamber/HeroPlant/Curio | New front layer over the same lab, camera base+(0,3.2,-8.2), look base+(-1.3,2,0), FOV56. Banner waits; Help/Settings reuse existing systems. |
| 4 Dr. Curio | Curio.cs, Characters/Curio_ScarfMesh.obj + baked scarf, Shell/Screen/Scarf/Gold/Guard maps; Icons.cs | Same transform rig and companion positioning; oval higher-resolution helmet, cream shell, scanner trim, cyan eyes, wave/blink/voice/head/hand gestures, damped sprout and scarf follow-through. |
| 5/8 Greenhouse scanners | ChamberBuild.cs, PlantBuild.cs, exact LeafTop/Under UV maps, Soil maps, imported lab material masks, ChamberVolume | Clue1 and Clue2 retain scan origins/routes and original camera scripts; environmental model, central plant, lab screens and panorama preserved. |
| 6/7/20 Stomata / CO2 / explore | LeafSurfaceWorld.cs, Epidermis/Patch + Guard maps | Original 10x and 100x poses and hero pore; natural wall detail, seven background CO2 (previously22), correct red-black-red inputs, unchanged ingress/egress lanes. |
| 9 Roots | PlantBuild roots/hairs; ChamberBuild soil collar/cutaway and batched small clods | Existing roots shot and transparent-floor reveal; root system stays attached to hero plant. |
| 10 Water absorption | SoilWorld existing root/hair meshes and pick paths; Root/Soil detail maps via SurfaceArt | Existing Water pose, H2O pick and root hair uptake; soil colors no longer multiply down to almost black. |
| 11 Xylem ride | XylemRide.cs, Vascular maps; CameraDirector companion-aware focus | Original arc-length route and camera direction; cellulose bands and readable blue flow, white wall emission reduced; Curio stays sharp. |
| 12 Leaf tissue | LeafTissueWorld.cs, Membrane/Grana/Vascular maps; LeafInteriorWorld client model | Original layered geometry/labels and dive anchors; differentiate palisade/spongy membranes, air spaces and blue/amber vessels. |
| 13 Plant cell | CellWorld.cs, Membrane/Grana maps | Original organelle layout/streaming paths; restrained shell/stack highlights, detailed selected chloroplasts, no full-screen DOF blur on the guide. |
| 14 Chloroplast | ChloroplastWorld.cs, Membrane/Grana maps | Existing 3/4 cutaway Pose and Open sequence; double envelope, original layered disks and lamellae; green detail survives energy pulses. |
| 15 Reaction | Same ChloroplastWorld feed/pick methods; shared Fx.Molecule | Separate CO2/H2O inputs; unchanged tap/confirmation mechanics and stroma reaction schematic. |
| 16 Glucose | Fx.Molecule glucose schematic, ChloroplastWorld FormGlucose | Replaced the schematic flat ring with verified PubChem CID64689 3D coordinates: 6 carbon,12 hydrogen,6 oxygen, complete bonds and CH2OH. Forming/revealed models share one molecular graph; the formula label is outside the molecule. Existing reveal/collider/root anchor and narration preserved. |
| 17 Oxygen | Fx.Molecule O2 and atom palette; existing O2 exit | Two red oxygen atoms; deeper red avoids pastel wash. Same stomatal exit route and learning sequence. |
| 18 Phloem | LeafTissueWorld and HeroPlant existing phloem geometry with Vascular maps | Amber sugar remains distinct from blue water; same tube and destinations. |
| 19 Equation / completion | Existing HudLearning equation + Story.Finale; upgraded greenhouse | Balanced equation and original quiz timing; unchanged final statement, Replay/Review/Exit. No added mandatory quiz. |

## Scientific exceptions to reference art

Reference illustrations are aesthetic targets, not molecular diagrams. Preserve linear CO2, bent H2O, diatomic O2 and a complete C6H12O6 schematic. The existing model explicitly places carbon fixation in stroma and light action at thylakoids. The simplified transport visuals remain lesson diagrams, rather than asserting glucose is the only sugar transported in phloem.

## Practical rendering choices

Later user direction: remove the procedural outdoor ground, mountains, hills and forest silhouette bands. `ChamberBuild` now builds no exterior geometry; the user's existing panorama skybox is the sole exterior background. Indoor floor/planter and educational underground cutaway remain part of the original activity.

Keep URP/Windows and the existing Performance/High tiers. Imported greenhouse PBR/normal atlases remain intact; new derived wood/floor/beam masks follow their original UVs. Dynamic formation, daylight changes, moving plant and distant runtime micro worlds make an indiscriminate whole-scene GI bake unsuitable; retain the user's scene lighting settings, existing ambient fill, SSAO and realtime reflection probe. Chamber cinematic DOF remains; near-camera companion micro views favor scientific/character readability.

Actual captures and review results are in PLAYTEST_RESULTS.md and BEFORE_AFTER_SCREENSHOTS. Concept art is never drawn as a gameplay backdrop.

## Verified glucose geometry

The user supplied cyclic and open-chain references. The cyclic reference (image3) is matched with [PubChem beta-D-glucose, CID64689](https://pubchem.ncbi.nlm.nih.gov/compound/64689). Original 3D SDF is retained in Tools/Chemistry/BetaDGlucose_64689.sdf; ExportGlucose.py generates Resources/Molecules/GlucoseGeometry.json with native Unity coordinates. Assertions verify C6H12O6 and carbon/oxygen/hydrogen valences4/2/1. The gold discovery cue is restrained and the ring rocks gently instead of spinning through unreadable orientations.
