#!/usr/bin/env python3
# Legibility of game frames (owner feedback 2026-10-07): python3 tools/legibility.py shot.png ...
# sd: luminance spread of the scene region (want >= 40, framing-dependent); sep: the machine pixels' mean
# luminance against the background band (want |sep| >= 50); sky and ground patches: hue, saturation, L.
# Legibility metrics agreed with the other session: scene region x300-980 y80-720, luminance 0-255.
import sys, colorsys, statistics as st
from PIL import Image
def L(p): return 0.2126*p[0]+0.7152*p[1]+0.0722*p[2]
def patch(im, box):
    px=[im.getpixel((x,y)) for x in range(box[0],box[2],2) for y in range(box[1],box[3],2)]
    return tuple(sum(c[i] for c in px)/len(px) for i in range(3))
for path in sys.argv[1:]:
    im=Image.open(path).convert("RGB")
    lum=[L(im.getpixel((x,y))) for x in range(300,980,3) for y in range(80,720,3)]
    mean, sd = st.mean(lum), st.pstdev(lum)
    # background estimate: the most common luminance band in the region (sky or floor fill most of it)
    bins=[0]*26
    for v in lum: bins[min(25,int(v//10))]+=1
    bg=max(range(26),key=lambda i:bins[i])*10+5
    # machine pixels: those more than 25 away from the background band
    fg=[v for v in lum if abs(v-bg)>25]
    sep=(st.mean(fg)-bg) if fg else 0
    sky=patch(im,(240,5,470,40)); ground=patch(im,(240,740,470,795))
    h,s,v=colorsys.rgb_to_hsv(*[c/255 for c in sky])
    hg,sg,vg=colorsys.rgb_to_hsv(*[c/255 for c in ground])
    print(f"{path.split('/')[-1]:28s} mean {mean:5.0f} sd {sd:4.0f} | fg {len(fg)*100//len(lum):3d}% sep {sep:+5.0f} | sky L{L(sky):4.0f} hue {h*360:4.0f} sat {s:.2f} | ground L{L(ground):4.0f} hue {hg*360:4.0f} sat {sg:.2f}")
