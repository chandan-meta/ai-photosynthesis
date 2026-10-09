# G7 ZIP models — every model from `G7_Photosynthesis_VR1.zip` (client, 2026-10-07), in one folder

Source: `Assets/ActivityFolder` of the ZIP (Maya ASCII FBX, cm). Copied unchanged; `Textures/` holds every map the files refer to.
Only **LeafInside** is used by the game (its runtime copy is `Resources/Models/LeafInside.fbx`, imported without materials and
re-skinned in code). Nothing in the scene references this folder, so none of these files are in the player build.

| File here | File in the ZIP | What it is | Tris | In the game |
|---|---|---|---|---|
| LeafInside.fbx | Photosynthesis_Leaf_inside.fbx | leaf cross-section block: skin, palisade bricks, spongy cells, vein (xylem, phloem, sheath, core), 11 stomata, water spheres | 92k | **yes** — Scene 3 flight inside the leaf |
| Chloroplast.fbx | Chloroplast 1.fbx | cut-open chloroplast (grana stacks, inner/outer membrane) + 4 molecule spheres, 16.7 s animation | 272k | no — the game's own cutaway chloroplast; mesh too heavy |
| Chloroplast_Feed_Anim.fbx | Chloroplast_co2_and_water.fbx | 25 H2O + 15 CO2 spheres flying in, 5.7 s animation | 36k | no — feeding is learner-driven |
| CO2_O2_Stomata_Anim.fbx | Co2_And_O2.fbx | CO2 in / O2 out through stomata, 24 s animation | 77k | no — leaf-surface CO2/O2 is interactive |
| Glucose_Anim.fbx | Glucose_Anim(1).fbx | 32 spheres assembling, 9.4 s animation | 17k | no — the glucose ring is built interactively |
| Stomata.fbx | Stomota/Stomata_10.fbx | guard-cell pair with an opening animation | 0.8k | no — the game's guard cells animate with magnification |
| CO2_Molecule.fbx | Modles/CarbonDioxide/CO2_Molecule.fbx | plain spheres | 2k | no — ball-and-stick molecules in the game |
| O2_Molecule.fbx | Modles/O2_Molecules.fbx | plain spheres, collapsed to ~0 scale | 14k | no |
| H_Molecule.fbx | Modles/H_molecules.fbx | plain spheres, collapsed to ~0 scale | 7k | no |
| Water_Molecule.fbx | water_Mol_Anim.fbx | plain spheres | 1k | no |
| Molecules.fbx | Molecules.fbx | 3 plain spheres | 0.8k | no |
| NADP.fbx | Modles/NADP.fbx | plain sphere, collapsed to ~0 scale | 7k | no |
| GreenBrickCell.fbx | Green brick highlight.fbx | single palisade-cell outline (old VR highlight prop) | 1k | no |
| PalisadeCell.fbx | HL.fbx | single palisade cell (old VR highlight prop) | 1k | no |
| GardenEnvironment.fbx | Photosynthesisenviroment.fbx | outdoor garden: pergola, stone borders, pots, fence, one house plant | 55k | no — the chamber is the user's lab model. **146 MB: kept out of git** (GitHub's 100 MB limit), together with its 27 textures (98 MB, used by nothing else) — they exist only on this machine and in the ZIP |

Not copied: `Cone.fbx`, `Inverted_Sphere.fbx`, `Modles/Single brick highlight.fbx` (primitive helpers of the old VR scene, 20–760 tris).
Renders of each: `Logs/g7/*_front.png` (Photosynthesis ▸ G7 Inventory). Licence: supplied by the client (see Docs/ASSETS_AND_LICENSES.md).
