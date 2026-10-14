#!/usr/bin/env python3
# Legibility of game frames (owner feedback 2026-10-07): python3 tools/legibility.py shot.png ...
# sd: luminance spread of the scene region (want >= 40, framing-dependent); sep: the machine pixels' mean
# luminance against the background band (want |sep| >= 50); sky and ground patches: hue, saturation, L.
# Legibility metrics agreed with the other session: scene region x300-980 y80-720, luminance 0-255.
#
# Ground regions (docs/art-direction.md 12.23, docs/plans/ground-colour-plan.md 3C):
#   python3 tools/legibility.py --region floor 300 560 560 780 --region far 300 10 900 120 --pair floor far shot.png ...
# prints, for each shot, the mean luminance, hue and saturation of every --region NAME x0 y0 x1 y1 (pixels of the shot), and for every
# --pair A B the luminance gap and the colour distance (Euclidean, bytes of R, G, B of the two means) of two named regions. With any
# --region or --pair the usual sd/sep line is not printed. `--regions FILE CAMERA` takes the `region CAMERA ...` and `pair CAMERA ...`
# lines of that camera from a file (tools/ground-regions.txt), which is how tools/ground-check.sh reads them.
import sys, colorsys, statistics as st
from PIL import Image
def L(p): return 0.2126*p[0]+0.7152*p[1]+0.0722*p[2]
def patch(im, box):
    px=[im.getpixel((x,y)) for x in range(box[0],box[2],2) for y in range(box[1],box[3],2)]
    return tuple(sum(c[i] for c in px)/len(px) for i in range(3))
def stats(im, box):
    m=patch(im, box); h,s,_=colorsys.rgb_to_hsv(*[c/255 for c in m])
    return m, L(m), h*360, s
args=sys.argv[1:]; regions={}; pairs=[]; paths=[]
while args:
    a=args.pop(0)
    if a=="--region": regions[args[0]]=tuple(int(v) for v in args[1:5]); args=args[5:]
    elif a=="--pair": pairs.append((args[0],args[1])); args=args[2:]
    elif a=="--regions":
        want=args[1]
        for line in open(args[0]):
            w=line.split()
            if len(w)>=7 and w[0]=="region" and w[1]==want: regions[w[2]]=tuple(int(v) for v in w[3:7])
            elif len(w)>=4 and w[0]=="pair" and w[1]==want: pairs.append((w[2],w[3]))
        args=args[2:]
    else: paths.append(a)
if regions or pairs:
    for path in paths:
        im=Image.open(path).convert("RGB"); got={n:stats(im,b) for n,b in regions.items()}
        print(path.split('/')[-1])
        for n,(m,l,h,s) in got.items(): print(f"  {n:12s} L {l:6.1f} hue {h:4.0f} sat {s:.2f}  rgb ({m[0]:.0f} {m[1]:.0f} {m[2]:.0f})")
        for a,b in pairs:
            (ma,la,_,_),(mb,lb,_,_)=got[a],got[b]
            print(f"  {a} | {b}: gap {abs(la-lb):5.1f} (L {la:.1f} to {lb:.1f}), colour distance {sum((x-y)**2 for x,y in zip(ma,mb))**0.5:5.1f}")
    paths=[]
for path in paths:
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
