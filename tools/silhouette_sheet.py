#!/usr/bin/env python3
"""Silhouette check (#167): every machine as a black shape on white, side by side at thumbnail size.

Take the frames with the game's silhouette mode, then sheet them:

    tools/gui-check.sh HEROIC_AUTOSELECT=<machine> HEROIC_SILHOUETTE=1 -- 'wait 45; key h; wait 5; shot <dir>/<machine>.png; quit'
    python3 tools/silhouette_sheet.py <dir> <out.png> [machine ...]

Each frame is cropped to its black pixels (the left panel excluded), squared, and scaled to a cell, so the shape is
judged and not its size on screen. If two machines' cells are hard to tell apart, their framing or composition needs work.
"""
import os, sys
from PIL import Image, ImageDraw

d, out = sys.argv[1], sys.argv[2]
ids = sys.argv[3:] or sorted(f[:-4] for f in os.listdir(d) if f.endswith(".png"))
cell, cols = 180, 4
rows = (len(ids) + cols - 1) // cols
sheet = Image.new("RGB", (cols * cell, rows * (cell + 16)), (255, 255, 255))
draw = ImageDraw.Draw(sheet)
for k, name in enumerate(ids):
    im = Image.open(os.path.join(d, name + ".png")).convert("RGB")
    im = im.crop((240, 0, im.width, im.height))                     # clear of the left panel
    mask = im.convert("L").point(lambda v: 255 if v < 60 else 0)
    x0, y0, x1, y1 = mask.getbbox() or (0, 0, im.width, im.height)
    side = max(x1 - x0, y1 - y0) + 16
    cx, cy = (x0 + x1) // 2, (y0 + y1) // 2
    square = Image.new("RGB", (side, side), (255, 255, 255))
    square.paste(im.crop((cx - side // 2, cy - side // 2, cx + side // 2, cy + side // 2)), (0, 0))
    x, y = (k % cols) * cell, (k // cols) * (cell + 16)
    sheet.paste(square.resize((cell - 10, cell - 10), Image.LANCZOS), (x + 5, y + 5))
    draw.text((x + 6, y + cell - 2), name, fill=(80, 80, 80))
sheet.save(out)
print(out, len(ids))
