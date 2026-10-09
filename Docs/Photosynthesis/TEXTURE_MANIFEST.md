# Texture manifest - 2026-10-09

Editable recipe: `Tools/GenerateSurfaceMaps.py` (NumPy/Pillow, deterministic seed 4209). Exact native UV sources exported by `PolishReview.ExportSurfaceSources`: `PlantLeafShape.BladeTexture`, and the existing `LeafSurfaceWorld.MakeSites/PlaceStomata/CellColor` functions. Reference images are art direction, never runtime scene textures.

Runtime assets: 65 PNG maps. Source-only albedo exports, height and roughness recipes are in `Assets/Photosynthesis/Art/Generated/Sources`, outside Resources to avoid automatic runtime packaging. Runtime root: `Assets/Photosynthesis/Art/Generated/Resources/SurfaceMaps`. URP Lit variants preauthored on Resources/PhotosynthesisMaterials templates; no runtime shader/keyword lookup. `SurfaceArt.Apply` assigns maps to real materials; `DressWorld` binds named existing micro geometry after construction. Generated meshes receive tangent vectors before normal mapping.

Color space: Albedo/Emission sRGB; Normal/Height/Roughness/AO/MetallicSmoothness linear. Normal importer is NormalMap; max 2048 for hero leaf/soil, 1024 for small surfaces, compressed HQ, mipmaps, anisotropy 4, non-readable. Blade/epidermis use Clamp; fiber/grain recipes Repeat. MetallicSmoothness R=metallic (Gold .92, other families 0), A=1-roughness; AO is separately sampled from G. `_Smoothness=1` keeps packing physically consistent; the epidermis deliberately scales it to .85 to restrain mirror streaks. Height is source only (no platform-costly parallax).

| Family | Source resolution | Albedo | Normal | Roughness (source) | AO | Packed metallic/smoothness |
|---|---|---|---|---|---|---|
| Epidermis | 1024 x 1024 | `Resources/SurfaceMaps/Epidermis_Albedo.png` | `Resources/SurfaceMaps/Epidermis_Normal.png` | `Sources/Epidermis_Roughness.png` | `Resources/SurfaceMaps/Epidermis_AO.png` | `Resources/SurfaceMaps/Epidermis_MetallicSmoothness.png` |
| EpidermisPatch | 640 x 640 | `Resources/SurfaceMaps/EpidermisPatch_Albedo.png` | `Resources/SurfaceMaps/EpidermisPatch_Normal.png` | `Sources/EpidermisPatch_Roughness.png` | `Resources/SurfaceMaps/EpidermisPatch_AO.png` | `Resources/SurfaceMaps/EpidermisPatch_MetallicSmoothness.png` |
| Gold | 1024 x 1024 | `Resources/SurfaceMaps/Gold_Albedo.png` | `Resources/SurfaceMaps/Gold_Normal.png` | `Sources/Gold_Roughness.png` | `Resources/SurfaceMaps/Gold_AO.png` | `Resources/SurfaceMaps/Gold_MetallicSmoothness.png` |
| Grana | 1024 x 1024 | `Resources/SurfaceMaps/Grana_Albedo.png` | `Resources/SurfaceMaps/Grana_Normal.png` | `Sources/Grana_Roughness.png` | `Resources/SurfaceMaps/Grana_AO.png` | `Resources/SurfaceMaps/Grana_MetallicSmoothness.png` |
| Guard | 1024 x 1024 | `Resources/SurfaceMaps/Guard_Albedo.png` | `Resources/SurfaceMaps/Guard_Normal.png` | `Sources/Guard_Roughness.png` | `Resources/SurfaceMaps/Guard_AO.png` | `Resources/SurfaceMaps/Guard_MetallicSmoothness.png` |
| LabBeam | 2048 x 2048 | `Sources/LabBeam_Albedo.png` | `Sources/LabBeam_Normal.png` | `Sources/LabBeam_Roughness.png` | `Resources/SurfaceMaps/LabBeam_AO.png` | `Resources/SurfaceMaps/LabBeam_MetallicSmoothness.png` |
| LabFloor | 1024 x 1024 | `Sources/LabFloor_Albedo.png` | `Sources/LabFloor_Normal.png` | `Sources/LabFloor_Roughness.png` | `Resources/SurfaceMaps/LabFloor_AO.png` | `Resources/SurfaceMaps/LabFloor_MetallicSmoothness.png` |
| LabWood | 1024 x 1024 | `Sources/LabWood_Albedo.png` | `Sources/LabWood_Normal.png` | `Sources/LabWood_Roughness.png` | `Resources/SurfaceMaps/LabWood_AO.png` | `Resources/SurfaceMaps/LabWood_MetallicSmoothness.png` |
| LeafTop | 2048 x 2048 | `Resources/SurfaceMaps/LeafTop_Albedo.png` | `Resources/SurfaceMaps/LeafTop_Normal.png` | `Sources/LeafTop_Roughness.png` | `Resources/SurfaceMaps/LeafTop_AO.png` | `Resources/SurfaceMaps/LeafTop_MetallicSmoothness.png` |
| LeafUnder | 1024 x 1024 | `Resources/SurfaceMaps/LeafUnder_Albedo.png` | `Resources/SurfaceMaps/LeafUnder_Normal.png` | `Sources/LeafUnder_Roughness.png` | `Resources/SurfaceMaps/LeafUnder_AO.png` | `Resources/SurfaceMaps/LeafUnder_MetallicSmoothness.png` |
| Membrane | 1024 x 1024 | `Resources/SurfaceMaps/Membrane_Albedo.png` | `Resources/SurfaceMaps/Membrane_Normal.png` | `Sources/Membrane_Roughness.png` | `Resources/SurfaceMaps/Membrane_AO.png` | `Resources/SurfaceMaps/Membrane_MetallicSmoothness.png` |
| Root | 1024 x 1024 | `Resources/SurfaceMaps/Root_Albedo.png` | `Resources/SurfaceMaps/Root_Normal.png` | `Sources/Root_Roughness.png` | `Resources/SurfaceMaps/Root_AO.png` | `Resources/SurfaceMaps/Root_MetallicSmoothness.png` |
| Scarf | 1024 x 1024 | `Sources/Scarf_Albedo.png` | `Resources/SurfaceMaps/Scarf_Normal.png` | `Sources/Scarf_Roughness.png` | `Resources/SurfaceMaps/Scarf_AO.png` | `Resources/SurfaceMaps/Scarf_MetallicSmoothness.png` |
| Screen | 1024 x 1024 | `Resources/SurfaceMaps/Screen_Albedo.png` | `Resources/SurfaceMaps/Screen_Normal.png` | `Sources/Screen_Roughness.png` | `Resources/SurfaceMaps/Screen_AO.png` | `Resources/SurfaceMaps/Screen_MetallicSmoothness.png` |
| Shell | 1024 x 1024 | `Resources/SurfaceMaps/Shell_Albedo.png` | `Resources/SurfaceMaps/Shell_Normal.png` | `Sources/Shell_Roughness.png` | `Resources/SurfaceMaps/Shell_AO.png` | `Resources/SurfaceMaps/Shell_MetallicSmoothness.png` |
| Soil | 2048 x 2048 | `Resources/SurfaceMaps/Soil_Albedo.png` | `Resources/SurfaceMaps/Soil_Normal.png` | `Sources/Soil_Roughness.png` | `Resources/SurfaceMaps/Soil_AO.png` | `Resources/SurfaceMaps/Soil_MetallicSmoothness.png` |
| Vascular | 1024 x 1024 | `Resources/SurfaceMaps/Vascular_Albedo.png` | `Resources/SurfaceMaps/Vascular_Normal.png` | `Sources/Vascular_Roughness.png` | `Resources/SurfaceMaps/Vascular_AO.png` | `Resources/SurfaceMaps/Vascular_MetallicSmoothness.png` |

## Actual assignments / UV constraints

- LeafTop / LeafUnder: unchanged HeroPlant blade shape, `u=0.5+x`, `v=(s+0.30)/1.30`, one-to-one UV, opaque and x-ray materials. Original vein field supplies corresponding relief/roughness; no arbitrary tiled leaf atlas.
- Epidermis/Patch: original warped sites and separate full-field / hero-patch UV domains (44m / 9m). Pore opening and guard geometry unchanged. Matching cell-wall normals/AO.
- Soil: hero soil, chamber planter and cutaway detail, SoilWorld particles. Existing strata colors retained where layer meaning matters.
- Root: hero roots/hairs and microscopic root/hair surfaces; tube circumference/arc-length UV. Existing cellular base color retained in SoilWorld.
- Guard: LeafSurfaceWorld guard-cell tubes and Curio sprout; pore remains actual geometry, with visible interior.
- Membrane: tissue cuticles, palisade/spongy shells, cell wall/vacuole, chloroplast double envelope and leaf-interior client geometry. Transparency stays on existing sorted material variants. Imported model atlas layout preserved.
- Grana: layered thylakoids, lamellae, cell chloroplast interiors and tissue chloroplast details; existing geometry anchors and educational paths preserved.
- Vascular: xylem tunnel/walls/lignin, tissue xylem/phloem and leaf-interior vessels. Blue water / amber sugar still use original separate flow cues.
- Shell/Screen/Gold: actual Curio head/body, curved visor and scanner ring. Eye_Emission independently assigned to luminous eye material. Scarf normal/masks add fine cloth relief while preserving the user's baked scarf albedo atlas.
- LabWood/LabFloor/LabBeam (when exported): masks derived from existing imported material atlases; original color/normal maps retained. Imported environmental PBR sets remain in use elsewhere.

## Provenance / validation

All new recipe pixels are generated locally, without copying screenshot art. Existing environment/scarf/model provenance remains in Docs/ASSETS_AND_LICENSES.md. `dr curio.blend` was read, never saved/overwritten; remodeling uses the compatible existing transform rig because the combined candidate is 131004 polygons and does not preserve the current separable animation callbacks.

Recipe assertions check finite normalized tangent normals. Visual validation and matching scene captures are recorded in PLAYTEST_RESULTS.md; no claim that asset generation alone proves rendered fidelity.
