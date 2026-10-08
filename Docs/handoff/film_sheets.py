"""Contact sheets from a frame folder: python film_sheets.py <frames> <out> cols rows tileW  (labels = file names)."""
import os, sys, glob
from PIL import Image, ImageDraw
src, out, cols, rows, tw = sys.argv[1], sys.argv[2], int(sys.argv[3]), int(sys.argv[4]), int(sys.argv[5])
os.makedirs(out, exist_ok=True)
files = sorted(glob.glob(os.path.join(src, "*.png")))
per = cols * rows; th = tw * 9 // 16; pad = 22
for s in range(0, len(files), per):
    sheet = Image.new("RGB", (cols * tw, rows * (th + pad)), (12, 14, 14)); d = ImageDraw.Draw(sheet)
    for i, f in enumerate(files[s:s + per]):
        im = Image.open(f).convert("RGB").resize((tw, th)); x, y = (i % cols) * tw, (i // cols) * (th + pad)
        sheet.paste(im, (x, y)); d.text((x + 6, y + th + 4), os.path.basename(f), fill=(230, 230, 230))
    sheet.save(os.path.join(out, f"sheet_{s // per:02d}.jpg"), quality=82)
print(len(files), "frames ->", (len(files) + per - 1) // per, "sheets in", out)
