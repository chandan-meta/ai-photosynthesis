#!/bin/bash
# convert_all.sh <obj root> <fbx out dir> <preview dir> — every <root>/<Name>/<Name>.obj -> <out>/<Name>.fbx (textures embedded) + verify + preview
# <obj root> is what the player wrote with:  Photosynthesis.exe -exportmodels <abs dir>
S="$(cd "$(dirname "$0")" && pwd)"
B="/c/Program Files/Blender Foundation/Blender 5.1/blender.exe"; ROOT="$1"; OUT="$2"; PREV="$3"; mkdir -p "$OUT" "$PREV"
for d in "$ROOT"/*/; do
  n=$(basename "$d"); obj="$d/$n.obj"; [ -f "$obj" ] || continue
  name="$n"; [ "$n" = "DrCurio" ] && name="DrCurio_InGame"
  "$B" -b --factory-startup -P "$S/export_used.py" -- leaf "$OUT/$name.fbx" "$obj" 2>&1 | grep "\[export\]\|Traceback\|Error:" | head -3
  "$B" -b --factory-startup -P "$S/export_used.py" -- verify "$OUT/$name.fbx" 2>&1 | grep "\[verify\]"
  "$B" -b --factory-startup -P "$S/render_fbx.py" -- "$OUT/$name.fbx" "$PREV" "$name" 2>&1 | grep "Error:" | head -2
  echo "$name: $(ls -la --block-size=K "$OUT/$name.fbx" | awk '{print $5}')"
done
