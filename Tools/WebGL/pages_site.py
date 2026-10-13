"""GitHub Pages copy of the WebGL build.

GitHub refuses files over 100 MB, so the gzipped data file is split into parts under 45 MiB, and the Photosynthesis WebGL
template's index.html (Assets/WebGLTemplates/Photosynthesis) downloads and joins them in the browser: this script fills in its
`var dataParts = null, dataBytes = 0;` line. The output folder keeps its own .git, so publishing is a commit and a push to the
repository's gh-pages branch:

    python Tools/WebGL/pages_site.py                      # Builds/WebGL -> Builds/WebGL-pages
    cd Builds/WebGL-pages && git add -A && git commit -m "Update site" && git push origin gh-pages
"""
import json
import os
import shutil
import sys

src = sys.argv[1] if len(sys.argv) > 1 else "Builds/WebGL"
dst = sys.argv[2] if len(sys.argv) > 2 else "Builds/WebGL-pages"
PART = 45 * 1024 * 1024
MARKER = "var dataParts = null, dataBytes = 0;"

build = os.path.join(src, "Build")
data = next(f for f in os.listdir(build) if ".data" in f)
html = open(os.path.join(src, "index.html"), encoding="utf-8").read()
if MARKER not in html:
    sys.exit("index.html has no data-parts line: build with the Photosynthesis WebGL template (PhotosynthesisBuild.BuildWebGL)")

os.makedirs(dst, exist_ok=True)
for name in os.listdir(dst):                      # start clean, but keep the gh-pages repository
    if name != ".git":
        path = os.path.join(dst, name)
        shutil.rmtree(path) if os.path.isdir(path) else os.remove(path)
shutil.copytree(src, dst, dirs_exist_ok=True,
                ignore=lambda folder, names: [data] if os.path.abspath(folder) == os.path.abspath(build) else [])

parts, total = [], os.path.getsize(os.path.join(build, data))
with open(os.path.join(build, data), "rb") as f:
    while chunk := f.read(PART):
        name = f"{data}.part{len(parts) + 1}"
        with open(os.path.join(dst, "Build", name), "wb") as out:
            out.write(chunk)
        parts.append("Build/" + name)

html = html.replace(MARKER, f"var dataParts = {json.dumps(parts)}, dataBytes = {total};")
open(os.path.join(dst, "index.html"), "w", encoding="utf-8").write(html)
open(os.path.join(dst, ".nojekyll"), "w").close()   # serve the files as they are, no Jekyll pass

largest = max(os.path.getsize(os.path.join(dst, p)) for p in parts)
print(f"{dst}: {len(parts)} data parts, largest {largest / 1048576:.1f} MiB, {total} bytes in all")
