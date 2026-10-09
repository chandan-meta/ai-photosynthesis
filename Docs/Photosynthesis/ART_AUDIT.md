# Art audit - 2026-10-09

Inspected the complete supplied master prompt, 30-page PDF in page order, reference ZIP (extracted to Logs/ReferenceStudy/assets), and 497-second phooto.mp4 sampled across the lesson. References guide appearance; existing gameplay and the user's direct requirements govern behavior.

## Existing project

- Unity 6000.3.0f1, URP 17.3, linear color, Windows x64 builds; Input System and UI Toolkit. No pipeline migration needed.
- Entry: Assets/Photosynthesis/Scenes/Scene02_BotanicalChamber.unity. Module.cs creates HeroPlant, Curio and seven MicroWorlds at runtime. Story.cs owns the established 24 Beat values, clue ordering, adaptive questions, narration, finale and optional review. Preserve these values and all interactions.
- The open scene had unsaved user edits (including the panorama); saved them before implementation. Existing dirty asset/import/license changes captured in Logs/Polish/preexisting_changes.patch; pre-save scene backed up. Do not regenerate the greenhouse with BuildAll.
- Greenhouse: Art/Environment/Chamber.fbx, ChamberBuilder.cs, Chamber.cs/ChamberBuild.cs, imported material/texture sets. Preserve the model, placement, screens and central planter.
- Plant: PlantBuild.cs, PlantLeafShape.cs, PlantMesh.cs, HeroPlant.cs, PlantFlows.cs. Existing blade UV uses u=0.5+x, v=(s+0.30)/1.30. Preserve silhouettes and interaction queries.
- Character: Curio.cs procedural transform rig plus Resources/Characters/Curio_ScarfMesh.obj. Source dr curio.blend exists; inspected mesh inventory (131004 polygons on the combined Tripo mesh, atlas UV and mixed materials). The existing separable procedural rig supports every speech/scan/point callback and is the practical remodeling base. Retain source unchanged and record provenance.
- Micro worlds: LeafSurfaceWorld (warped cellular heightfield and real pore); SoilWorld (root/hair uptake); XylemRide (arc-length tunnel); LeafTissueWorld; CellWorld; ChloroplastWorld; LeafInteriorWorld (client LeafInside model). Preserve coordinates, anchors, routes and picking colliders.
- Materials: Fx.cs clones Resources/PhotosynthesisMaterials.asset template variants; URP Lit and MoleculeRim.shader. Existing maps are mostly base color, with procedural textures on micro geometry; these require correlated normals/AO/roughness.
- Camera: CameraDirector.cs, custom eased coroutines, no Cinemachine dependency. Narration: Dialogue.cs, AudioDirector.cs, Synth.cs, 170 existing story clips and offline KB voice. Existing ducking/settings should be reused.
- Verification: DebugTools supports beat shots, autoplay, wrong-first, real voice, UI states, film, touch and performance. Existing build sampled as baseline.

## Observed gaps / priorities

1. No front-layer start state: gate story launch, narration, progression and camera input; reuse actual greenhouse and existing settings/help.
2. Curio target needs cream oval helmet, softer materials, clearer eyes, textured scarf and spring sprout; preserve rig APIs and gestures.
3. Leaf/soil/root/membrane/grana/vascular surfaces need authored asset maps assigned to real geometry. Avoid atlas replacement on imported environment or scarf.
4. Guard-cell and cell-envelope reflections/transparency wash out structure. White emissive xylem walls and excessive bloom flatten micro detail.
5. Preserve scene-local daylight and panorama; add contact texture/shadow and bounded reflection improvements. Animated formation/daylight and runtime micro worlds make a single baked lighting solution unsuitable.

## Reference mapping

PDF 3=start overlay; 4=Curio; 5/8/19=Chamber + HeroPlant; 6/7/20=LeafSurfaceWorld; 9=Chamber underground + HeroPlant roots; 10=SoilWorld; 11=XylemRide; 12/18=LeafTissueWorld; 13=CellWorld; 14-17=ChloroplastWorld. Review in the four pass order explicitly requested by the user (UI, fidelity, pacing/audio, regression/build).
