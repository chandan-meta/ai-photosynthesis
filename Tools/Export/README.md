# Model export (2026-10-09)

1. `Builds/Win/Photosynthesis.exe -exportmodels "<abs dir>"` — the player writes every runtime-built model (hero plant, Dr. Curio,
   chamber pieces, molecules, micro worlds) as `<dir>/<Name>/<Name>.obj` + `.mtl` + `textures/*.png`, then quits (`ModelExport.cs`).
2. `bash Tools/Export/convert_all.sh "<abs dir>" "<out dir>" "<preview dir>"` — Blender: each OBJ → FBX with textures embedded,
   re-import check, preview render. One object per part.
3. File-based models: `export_used.py chamber|curio|leaf` (the lab environment, the Curio kit + scarf, the ZIP leaf via
   `fbx_ascii_to_obj.py`, which reads Maya ASCII FBX that Blender cannot open).

Blender 5.1 at `C:/Program Files/Blender Foundation/Blender 5.1/blender.exe` (paths inside the scripts). Output for the user: `Exports/UsedModels/`.
