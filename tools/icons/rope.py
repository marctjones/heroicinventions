#!/usr/bin/env python3
"""The build palette's rope icon (#165): a coil of hemp rope with a loose end, in the thumbnails' look.

Rope is a join tool, not a part, so the thumbnail renderer has nothing to draw for it. This draws one in the same
style: the material table's hemp colour (#C7B285), two flat toon tones, twist marks, and a black outline about as
heavy as the thumbnails' (Skins.ThumbnailOutline). Drawn at 4x and scaled down for a clean edge, on a transparent
background. Procedural, so nothing outside the repo goes into it.

    python3 tools/icons/rope.py            # writes game/icons/rope.png and docs/art/icons/rope.png
"""
import math, os
from PIL import Image, ImageDraw

S = 4                      # supersampling
W = 96 * S
HEMP = (0xC7, 0xB2, 0x85)  # racket/heroic/materials.rktd: hemp
SHADE = tuple(int(c * 0.72) for c in HEMP)   # the toon's shadow band
LIGHT = tuple(min(255, int(c * 1.12)) for c in HEMP)
INK = (0, 0, 0)
ROPE = 15 * S              # rope thickness
LINE = 3 * S               # outline width, each side

img = Image.new("RGBA", (W, W), (0, 0, 0, 0))
d = ImageDraw.Draw(img)

def ellipse_points(cx, cy, rx, ry, a0, a1, n=120):
    return [(cx + rx * math.cos(a), cy + ry * math.sin(a)) for a in (a0 + (a1 - a0) * k / n for k in range(n + 1))]

def stroke(points, width, colour):
    d.line(points, fill=colour, width=width, joint="curve")
    r = width / 2
    for x, y in (points[0], points[-1]):
        d.ellipse((x - r, y - r, x + r, y + r), fill=colour)

def rope(points, twist=True):
    stroke(points, ROPE + 2 * LINE, INK)
    stroke(points, ROPE, SHADE)
    # the lit upper half of the strand: the same path nudged up, narrower
    stroke([(x, y - ROPE * 0.18) for x, y in points], int(ROPE * 0.55), HEMP)
    stroke([(x, y - ROPE * 0.3) for x, y in points], int(ROPE * 0.18), LIGHT)
    if twist:   # the lay of the strands: short dark slashes along the rope
        for k in range(4, len(points) - 4, 9):
            (x0, y0), (x1, y1) = points[k - 1], points[k + 1]
            tx, ty = x1 - x0, y1 - y0
            n = math.hypot(tx, ty) or 1
            nx, ny = -ty / n, tx / n
            x, y = points[k]
            a = (x + nx * ROPE * 0.42 - tx / n * ROPE * 0.25, y + ny * ROPE * 0.42 - ty / n * ROPE * 0.25)
            b = (x - nx * ROPE * 0.42 + tx / n * ROPE * 0.25, y - ny * ROPE * 0.42 + ty / n * ROPE * 0.25)
            d.line([a, b], fill=SHADE, width=max(1, S))

cx, cy = W * 0.44, W * 0.40
# the coil: loops seen from above and to one side (ellipses), drawn from the bottom of the stack to the top
for i, (rx, ry, lift) in enumerate([(29, 16, 10), (29, 16, 4), (29, 16, -2)]):
    rope(ellipse_points(cx, cy + lift * S, rx * S, ry * S, 0, 2 * math.pi))
# the loose end leaving the top loop and trailing off to the lower right
tail = []
# leaves the front of the top loop and curls down and out to the lower right, staying inside the frame
for k in range(41):
    t = k / 40
    ang = 0.35 + 1.0 * t
    tail.append((cx + 29 * S * math.cos(ang) + 30 * S * t, cy - 2 * S + 16 * S * math.sin(ang) + 30 * S * t * t))
rope(tail)
# a frayed tip
x, y = tail[-1]
for dx, dy in [(5, 3), (6, -1), (3, 6), (7, 2)]:
    d.line([(x, y), (x + dx * S, y + dy * S)], fill=INK, width=2 * S)
    d.line([(x, y), (x + dx * S * 0.9, y + dy * S * 0.9)], fill=HEMP, width=S)

out = img.resize((96, 96), Image.LANCZOS)
here = os.path.dirname(os.path.abspath(__file__))
root = os.path.normpath(os.path.join(here, "..", ".."))
for path in ("game/icons/rope.png", "docs/art/icons/rope.png"):
    out.save(os.path.join(root, path))
    print(path)
