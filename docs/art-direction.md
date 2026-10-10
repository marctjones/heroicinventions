# Art direction

Issue #100. Status: **recommendation, open to the owner's override.** Nothing in the game code changes with this document; it is the brief that issues #101 to #107 build against.

## 1. Decision

**Stylised workshop look. Keep the inverted-hull outline. Do not move to realistic PBR.**

Reasons, in order of weight:

1. **The game teaches by showing.** The look's job is legibility of physical state: which part is bronze and which is oak, which tank is full, which gear is slipping, whether a bearing is hot. A stylised look with flat, distinct colours and a rim line keeps neighbouring parts readable at any zoom. PBR spends its budget on realism that the player's eye does not need and that competes with state cues (#105).
2. **Everything is procedural.** Parts are generated boxes, cylinders and rods (`Shapes.cs`); there is no texture or mesh library. Convincing PBR needs authored textures, normal maps and detailed meshes, which this project has no pipeline, budget or licence-clean source for. A stylised look is the only one the existing geometry can carry without looking unfinished. Realism on primitive geometry reads as "bad CG"; stylisation on primitive geometry reads as a deliberate choice.
3. **Licence cost.** Stylised plus procedural means no third-party texture or model enters the repo, which keeps the licence position trivial (section 8). PBR would pull in scanned texture sets, each a provenance check.
4. **Scale range.** A clock gear (millimetres) and a crater (hundreds of metres) share one renderer. PBR texel density cannot cover that range without a texturing system. Flat colour plus a width-scaled outline already does.
5. **Cost and portability.** Flat shading with one shadowed sun and SSAO runs on the Compatibility renderer and on weak GPUs, and is what the Godot export (#99) should target.

What this gives up: photographic "wow" in screenshots. The mitigation is a strong palette, good light, atmospheric haze and a few handcrafted details (#102), which is what stylised games rely on.

Reversal cost if the owner prefers PBR: the material function (`Surface`) is one choke point, so swapping to textured materials is local, but #102 to #104 would need rescoping around authored assets and a texture licence audit. Say so on #100 and the dependent issues would be re-planned.

## 2. What exists today

Read from `game/scripts` at the time of writing. Screenshots of this current state are in `docs/art/` (section 9).

| Area | Current behaviour | Where |
|---|---|---|
| Albedo | One flat colour per material id from a switch (`ColorFor`); unknown ids fall back to stone. No textures. | `Shapes.cs` |
| Roughness | Derived from the material's friction coefficient: `0.12 + (mu - 0.30) / 0.30 * 0.83`, clamped 0.1 to 0.95. Metals get `metallic = 0.8`. | `MachineView.cs` `Surface` |
| Per-part variation | Brightness nudged by up to plus or minus 6 percent, seeded from an FNV-1a hash of the part id, so it is the same every run. | `PartSurface` |
| Outline | Inverted hull: a `NextPass` material, front-face culled, `Grow` on, unshaded, colour `(0.08, 0.06, 0.05)`, width `size * 0.012` clamped 0.2 mm to 12 mm. Applied only where `PartSurface` is called (balls, pistons, and other generated parts). Many parts call plain `Surface` and so have **no outline** (rods, doors, structures, capstans, etc.). | `PartSurface`, call sites in `MachineView.*.cs` |
| Geometry | `BoxMesh`, `SphereMesh`, `CylinderMesh`, and `Rod` (a cylinder between two points). Placeholder by the file's own description. | `Shapes.cs` |
| Sky | Default `ProceduralSkyMaterial`, top and horizon colours tinted by air temperature, planet and sun elevation; dust storm lerps toward brown `(0.45, 0.30, 0.20)`. | `Main.cs` `BuildEnvironment`, `ShowSky` |
| Light | One `DirectionalLight3D`, shadows on, default energy 1, colour lerped white to blue (cold) or yellow (hot), orange at low sun. On Mars energy is scaled by solar constant ratio (about 0.43 to 0.6 of Earth). No fill light, no fog, no tonemap or exposure settings, no glow. | `Main.cs` |
| Ambient | Sky-driven (Godot default). SSAO on, radius 0.5, intensity 2.5. | `Main.cs` |
| Ground, studio | A 2 km flat box in stone `(0.72, 0.70, 0.66)`; frost lerps it toward near-white below 0 C. | `Main.cs` |
| Ground, worlds | Vertex-coloured mesh through cell centres: soil colour times a slope shade of `0.65 + 0.35 * max(0, n.l)` with a fixed light direction, loose ground 18 percent lighter, scoured ground darker and deposited ground paler. Roughness 0.95. | `TerrainView.cs` |
| Water | Translucent quads per wet cell, vertex colour by depth, roughness 0.15. | `TerrainView.cs` |
| Mars | Planet data supplies `sky-color (0.78, 0.60, 0.45)` and `ground-color (0.60, 0.36, 0.22)`; the sky is lerped 85 percent toward it. | `Main.cs`, `Planet.cs`, `*.machine` |
| Labels | `Label3D`, billboarded, no depth test, outline size 6 to 8. | `AddLabel` |

Observed in the baseline screenshots (software GL, 1600x1000):

- Earth studio ground blows out to near-white under the sun and merges with the horizon with no haze or fog, so the scene has no sense of depth and the horizon is a hard seam.
- Mars is a single saturated orange: ground, sky and the heliostat mirrors are all the same hue family, so the machines (which are mostly bronze and wood) lose contrast against the ground.
- The outline is not visible at normal zoom on small parts and is absent on parts that use `Surface`. Silhouettes of thin parts (rods, ropes) rely on shadow alone.
- Metals read as grey plastic: `metallic 0.8` with no environment to reflect other than the procedural sky.

## 3. Palette

Rules: colours are authored as sRGB hex and defined once, in `Shapes.ColorFor` (or the data-driven table from #101). Keep value contrast between neighbouring materials of at least about 15 percent luminance; keep saturation moderate (workshop, not toy). Current values are kept where they work.

### Materials

| Material | Hex | Note |
|---|---|---|
| bronze | `#CC8F4A` | keep; warm, the hero metal |
| copper | `#B87333` | keep |
| iron | `#4A4A52` | slightly darker than now (`#595960`) to separate from stone |
| steel | `#B8BDC4` | keep |
| oak | `#8C6138` | keep |
| pine | `#D1A86E` | keep |
| cedar | `#C2855C` | keep |
| olive | `#998557` | keep |
| marble | `#E4E0D6` | a touch lower than now so it does not clip white in sun |
| hemp | `#C7B285` | keep |
| glass | `#D1EBF2` at alpha 0.18 to 0.25 | keep; roughness 0.1 |
| water | `#408CD9` | keep; deeper water darker, never black |
| stone (also unknown id) | `#A8A298` | **darker than now** (`#B8B2A8`) so the studio floor stops clipping |
| sand | `#DBC794` | keep |
| loam | `#6B5738` | keep |
| clay | `#9E6E4F` | keep |
| regolith | `#8A5A3C` | **darker and less saturated than now** (`#AD6B47`) so bronze and wood read against it |

Per-part variation stays at plus or minus 6 percent brightness (hash-seeded), never hue.

### Earth studio and sky

- Studio ground: `#A8A298` stone, roughness 0.85. Frost lerps toward `#EDF2F7`.
- Sky zenith `#5B7FA8`, horizon `#C9D6E3`, ground horizon `#B9B4AA`. Fog or aerial haze toward the horizon colour from about 150 m out, so the 2 km floor fades rather than ends.

### Mars

- Ground: `#8A5A3C` base, with large-scale value variation of plus or minus 8 percent from low-frequency noise (procedural, in the vertex colour), darker in crater floors, paler on wind-swept rims.
- Sky, midday: zenith `#B98B67`, horizon `#D9A57C`. Hazier and paler toward the horizon.
- Sky, dusty: toward `#6E4B35` as dust optical depth rises (existing behaviour, keep the direction).
- Sunset or sunrise: a blue-grey halo near the sun, `#8FA3B8`, is the one place Mars is blue. This is physically right (Rayleigh-poor, dust forward-scatter) and is the single biggest "this is Mars" cue that costs nothing. Optional in #104.
- Frost on Mars stays a thin dusting (about 25 percent toward white, as now).
- Light colour at noon `#FFE2C0`, never pure white.

## 4. Outline rule

The outline is the signature of the look, so it is applied consistently, not by accident of which helper a builder happened to call.

1. **Every solid, opaque, generated part has the outline.** The route is `PartSurface`, not `Surface`. Builders that currently call `Surface` for a visible solid body should switch (this is the work in #102).
2. **Exceptions, no outline:** transparent or translucent surfaces (glass, water, tank shells, pane pools, frost skins, grip reach volumes), ground and terrain, the sky, labels, and effect meshes (steam, sparks, spray).
3. **Colour (owner decision 2026-10-05): pure black `#000000`**, for a comic-book edge. Not tinted per part. Under Mars or night it stays the same; it is a line, not a lit surface.
4. **Width (comic weight):** scale with part size, `size * 0.018`, clamped 0.3 mm to 18 mm (about 1.5 times today's). Add a **screen-space floor**: the line should not shrink below about 1 pixel at the default camera distance, which means widening small parts' outline as the camera pulls out (an `Environment`-independent shader parameter on the outline pass, not per-frame C# for each part). Currently small parts lose the line entirely at overview zoom.
5. **Thin parts** (rods, ropes, chains under about 1 cm radius) get a fixed minimum so they do not disappear against bright ground.
6. **Interior creases** are the job of SSAO (keep on), not of the outline.
7. **State overrides:** the outline colour may be driven by state for #105 (for example an amber or red rim on a part near failure), through one shared helper, never an ad-hoc material swap.

## 5. Material treatment

- **Shading:** one lit pass with flat albedo, plus the outline pass. No textures on parts by default. If a material needs surface character (wood grain, stone speckle) it is a **procedural** triplanar shader or a tiny generated noise tint, kept subtle (plus or minus 5 percent value), authored in-repo.
- **Roughness stays friction-derived.** This is a design rule, not just an implementation detail: the friction coefficient the UI states is also what the eye sees. Do not override per material for looks.
- **Metals:** `metallic 0.8` is kept but needs something to reflect. Add a sky-derived reflection (the sky is already in the environment; set ambient and reflected light source to sky) rather than a texture. Bronze and steel should show a soft horizon-to-ground gradient on curved parts.
- **Polish and wear:** colour does not change with wear except through the existing mirror dust treatment; wear gets a value shift toward a dull grey-brown, not a texture.
- **Translucent materials:** glass and water keep alpha and low roughness; no refraction or screen-space reflection (cost, and not needed for legibility).
- **Ground:** vertex colour from soil, with the slope shade driven by the real sun direction, not a fixed vector, so shading and shadows agree.
- **Geometry detail** is added as extra primitives (bevels as short cylinders, bolts, rims), not as imported meshes. See #102.

## 6. Lighting targets

Targets, so any lighting change can be checked against numbers rather than taste:

| Item | Target |
|---|---|
| Key light | One shadowed `DirectionalLight3D`, as now. Elevation 35 to 55 degrees in the studio (currently -50 degrees pitch, keep). Shadow softness: blur up, no hard stair-stepping on the 2 km ground; use PSSM splits sized to the scene. |
| Ambient | Sky-sourced, energy about 0.5, so the shadow side of a part is about 40 to 50 percent of its lit value, never black and never flat. |
| Exposure | Filmic or ACES tonemap, white point such that a marble block in full sun peaks around 90 percent, not 100. This alone fixes the white-out ground. |
| Contrast check | Studio ground luminance about 0.45 to 0.55 in sun; lightest material (marble) at least 0.2 higher than the ground; darkest (iron) in shadow still above 0.08. |
| Mars key | Colour `#FFE2C0`, energy scaled from the solar constant ratio (current behaviour), but floor at about 0.45 so Mars is dim, not murky. |
| Fog or haze | Depth haze toward the horizon colour (section 3). Mars denser than Earth (suspended dust); extinction rising with the storm optical depth. |
| SSAO | Keep: radius 0.5, intensity 2.5 as a starting point; tune with the screenshot set. |
| Night | Existing behaviour (sun off, near-black zenith); add a faint cool ambient so machines stay silhouetted rather than vanishing. |
| Glow, bloom | Off by default. Allowed only as a state cue (for example a hot crucible). |
| Post-processing | None beyond tonemap and SSAO. No SSR, no SDFGI, no volumetric fog. |

## 7. What each dependent issue does under this look

- **#101 Data-driven material look.** Move colour, metallic and any roughness override out of the `ColorFor` switch into the material data (the same files that carry friction, restitution and category). One table, hex values as in section 3. Keep the friction-derived roughness rule. Unknown material ids log a warning and use stone rather than silently falling back. This is the foundation; do it first.
- **#102 Part detail pass on primitive parts.** Raise primitives from "a box" to "a made thing" with more primitives, never imported meshes: bevelled rims, bolt heads, hoops on tanks, spokes on wheels, plank seams on wooden bodies. Move every solid opaque part onto `PartSurface` so the outline rule (section 4) holds. Keep the silhouette strong and the parts chunky. Budget: no part above a few dozen mesh instances.
- **#103 Crater terrain rendering.** Ground coloured from section 3 Mars regolith, with large-scale procedural value variation, darker crater floor, paler rim, slope shading from the real sun direction, and scattered boulders drawn as stylised low-poly rocks with the outline. No photographic ground texture. Terrain itself is not outlined. Draw distance handled by haze, not by a hard edge.
- **#104 Mars sky and light driven by sol time and dust.** Keep the procedural sky; drive zenith, horizon, sun colour, energy and haze from sol time and dust optical depth, using the Mars values and targets in sections 3 and 6. Add the blue sunset halo as the one non-orange accent. No skybox image, no HDRI.
- **#105 State legibility: the failure is visible in the scene.** State is shown with **colour shifts and the outline channel**, not realistic damage textures: an amber then red rim on parts near their limit, a heat tint from the thermal state (dull to cherry to bright), overfull tanks showing a high waterline and spill, a slipping belt with a visible gap or stripe offset. All through shared helpers so the cues are consistent. Cues must pass the palette contrast check at default zoom.
- **#106 UI theme.** The HUD takes the same warm workshop palette: dark warm-grey panels (`#2B2724`) with a bronze accent (`#CC8F4A`), cream text (`#F1E9D8`), and the same outline logic for icons. Build palette thumbnails are rendered from the real parts with the real outline pass, so a thumbnail matches what is placed. Rover log styling reads as a ledger or notebook (paper tone `#E8DFC8`, ink `#2B2724`). UI fonts must be permissively licensed (section 8).
- **#107 Screenshot regression.** Uses the capture method in section 9 with the fixed camera frames. Because outline widths and exposure change, the first baseline is taken after #101 and #102 land, and tolerance is set by pixel-difference threshold, not exact match, since software GL and GPU output differ.

Order that avoids rework: #101, #102, then #103 and #104 together (shared haze and exposure), then #105, then #106, then #107 to freeze the result.

## 8. Licence rule for assets and references

The repo is an open-source project, so anything committed must be redistributable under the project's licence (see #92).

1. **Procedural first.** Generated geometry, shader code, vertex colour and generated noise are authored in the repo and are covered by the project licence. This is the default and the expected route for every item above.
2. **Reference images** (for studying how Mars or a bronze casting looks) may be consulted only from sources that allow reuse: **public domain or CC0** (for example NASA imagery, which is generally not copyrighted when produced by NASA staff, check each item's credit line, since some mission images carry third-party or ESA rights) and **CC BY** (attribution required). Avoid CC BY-SA unless the owner accepts share-alike on derived assets, and avoid any non-commercial or no-derivatives licence.
3. **References are not assets.** Do not copy, trace or colour-sample a photograph into a texture. A reference informs a hex value or a shape; the committed artefact is our own.
4. **If an external asset is committed** (a font, an icon, a texture), it must be CC0, public domain, MIT, BSD, Apache-2.0, or SIL OFL (fonts), and it must be recorded in an asset ledger (`docs/assets.md`, to be created with the first external asset): file, source URL, author, licence, date retrieved, and the exact attribution text if required.
5. **No AI-generated or scraped imagery** of unknown provenance goes into the repo or the ledger of references.
6. **Screenshots** in `docs/art/` are renders of this game and fall under the project licence.
7. When in doubt about a source, leave it out and use a procedural substitute.

## 9. Screenshots

`docs/art/` holds frames from the real game (Godot 4.7.2 .NET, Compatibility renderer, software GL under Xvfb, `--write-movie`, 1600x1000, frame 60 of a 90 frame recording):

| File | Machine | Shows |
|---|---|---|
| `trebuchet.png` | `trebuchet` | wood, iron, stone on the Earth studio floor |
| `herons-fountain.png` | `herons-fountain` | wood, water, translucent shells, labels |
| `mars-stirling.png` | `mars-stirling` | Mars sky and ground, bronze mirrors, hot-air engines |

**These show the current renderer, not the chosen look.** The task for this issue was to write the direction without changing game code, so the baseline is what exists today, and the problems listed in section 2 (blown-out ground, no haze, orange-on-orange Mars, missing outlines) are visible in them. The "done when" criterion of #100 asks for a set showing the chosen look on three machines; that set is produced after #101 and #102 land, using the same command, and should replace these files under the same names.

To reproduce:

```
export HEROIC_GODOT=/path/to/Godot_v4.7.2-stable_mono_linux.x86_64
HEROIC_AUTORUN=1 HEROIC_AUTOSELECT=trebuchet \
  xvfb-run -a -s "-screen 0 1280x720x24" "$HEROIC_GODOT" --path game \
  --rendering-driver opengl3 --write-movie /tmp/trebuchet.avi \
  --fixed-fps 30 --quit-after 90 scenes/Main.tscn
ffmpeg -i /tmp/trebuchet.avi -vf "select=eq(n\,60)" -vframes 1 docs/art/trebuchet.png
```

Notes: the build needs the .NET 10 SDK (the csproj rolls net8.0 forward); `dotnet build` in `game/` first. Movie Maker mode renders in about 0.15 to 0.3 s per frame on software GL, so a three-second clip takes about 25 s. The HUD panels are part of the capture; #107 should crop them or add a capture mode that hides them (the `H` key hides only the right-hand panel).

## 10. Owner decisions (2026-10-05)

1. **Stylised over PBR: confirmed, pushed further toward a comic-book look.** Concretely: pure black, heavier outlines (section 4), and toon (cel) shading, where light falls off in a few flat bands instead of a smooth gradient. In Godot this is a material setting (`diffuse_mode = toon`, `specular_mode = toon` on `StandardMaterial3D`), applied in the shared `Surface` helper, so it is a small change. Shadows stay on and read as a flat dark band.
2. **Outline colour: pure black.**
3. **Filmic tonemap: yes.** Plain words: a screen can only show brightness up to a fixed maximum, and a bright sunlit ground simply hits that maximum and turns flat white. A tonemap is a curve applied to the finished image that squeezes very bright values gently into the displayable range, so bright areas keep some shading and detail instead of clipping. Filmic means a curve modelled on how film responds, with a soft shoulder in the highlights. It is a single setting on the scene's environment. It changes every colour slightly, which is why it is called out.
4. **Mars sunset blue halo: conditional.** It is only a colour gradient near the sun in the procedural sky. Include it in #104 if it is a few lines of sky parameters; drop it if it needs more than that (a custom sky shader, extra render passes, per-frame work).


## 11. Skins: the rules as built (2026-10-07)

`game/scripts/Skins.cs` holds the look as rules, so a part nobody has drawn yet, in a material nobody has given a look, comes out in style, and parts never meant to stand together still sit well side by side. Builders do nothing extra: `Surface(material)` and `Shapes.Mat` apply all of it.

1. **What it is made of.** `materials.rktd` gives each material a `color` (sRGB hex, section 3) and optionally a `finish`. A material with no finish takes its category's: wood → `grain`, metal → `cast`, stone → `dressed`, fiber → `fibre`, soil → `granular`. Overrides in the table: iron `wrought` (fibrous slag lines, as real wrought iron has), steel `polished`, marble `veined`, granite `crystalline`, glass `clear`. A finish is procedural noise banded through a value ramp (`Skins.Recipes`), projected in the part's own space (it turns with a turning wheel) at a fixed scale in metres (oak's grain is the same size on a clock arbor and a crane jib). Ramps only darken, a few per cent, more only where the material is really figured. Roughness is still friction-derived. Metals are `metallic 0.5`, not 0.8: with only a studio sky to reflect, 0.8 made bronze as dark as oak.
2. **How it sits with others.** Toon shading everywhere. Every opaque surface gets the black outline (`Skins.Outline`), a shared shader whose width is a share of screen height (0.3%), so every part keeps a line at any zoom without knowing its size. It pushes out from the part's centre (`sign(VERTEX)`), not along face normals, so box corners stay closed, and it doesn't widen in the orthographic shadow pass. No line: alpha < 1, terrain, and `Shapes.Mat(..., outline: false)` (the studio floor). Grain runs along each mesh's longest side (`Skins.OrientGrain`, run once after a view is built). A material shared by several meshes takes the majority's direction and is never copied, because builders keep handles on materials they change at run time. `Skins.Vary` gives each named part its ±6% shade.
3. **What it is doing.** Heat: `Skins.Glow(mat, °C)` (the one incandescence ramp: dark below 500 °C, red, orange, yellow-white by 1,500 °C; crucibles and enclosure heaters use it). Strain: `Skins.Rim(mat, share)` turns the outline amber to red and thickens it from 60% to 100% of a limit. It is the only state allowed on the outline. Boilers with a rating (an explicit #:burst or a #:wall shell, #139) call it with pressure over what the shell holds now.

Environment: filmic tonemap (white 6), sky ambient 0.6, exponential haze 0.0025/m whose colour follows the sky's horizon (Mars, dusk and storm included). The studio floor has its own colour, `Shapes.StudioFloor` `#77726B`, darker than section 3's `#A8A298`. Toon shading lights a sun-facing floor fully, so `#A8A298` measured 0.68 luminance in sun. `#77726B` measures 0.52, inside the 0.45 to 0.55 target, with a stone block on it at 0.62 and iron in shadow at 0.24 (newcomen-engine, gui-check shot, 1280x800). `Shapes.Stone` stays the fallback for a material with no colour.

Gotcha: on Metal and Vulkan, `PROJECTION_MATRIX[1][1]` is negative (y flip), so any screen-space width in a shader needs `abs()`.

## 12. Sky, light and legibility (2026-10-07, #104 and owner feedback)

The owner found the first skins pass hard to read: "the coloring and the light levels are not better. It is hard to see many of the machines." Measured with `tools/legibility.py` (scene region of a 1280x800 frame), sky, horizon, floor and the wooden and bronze parts all sat at luminance 130 to 150. This section replaces section 3's sky and studio-floor values and section 6's floor target.

- **Figure-ground rule** (`SkyLook.GroundFor`): the ground stands apart from the machine on it. The game averages the luminance of the machine's materials. Mostly dark to mid-toned parts (wood, bronze, iron) get a light cool-grey ground (HSV 0.58/0.08/0.80). Clearly pale parts (average above 0.66: steel, limestone, marble) get a dark one (value 0.32). Mars is always dark (its rust greyed to 45% saturation, value 0.24), because its pale sky leaves no room for a light ground. Meridiani's plain is dark basaltic sand anyway.
- **Earth's sky** is a saturated blue, zenith `#3F74B5`, horizon `#B4CDE6`. Section 3's `#5B7FA8` measured as grey on screen.
- **Sky from conditions** (`SkyLook.Of`, a pure function):
  - The sun's colour is per-channel extinction e^(−k·AM), so a low sun reddens.
  - Its strength falls by a further e^(−Δτ·AM) in a storm while the sky's light remains, so storm shadows vanish.
  - Daylight runs from 6° below the horizon to 10° above. At night a cool fill (`#29334D`) keeps machines in silhouette.
  - Haze thickens with dust: 0.0025/m on Earth, 0.004/m on Mars, plus 0.003/m per unit of storm optical depth.
  - The sun disc is 0.53° on Earth, scaled by the square root of the solar constant (0.35° on Mars).
  - Mars's blue aureole round a low sun is a sky-only `DirectionalLight3D` (a 0.3° disc, the glow fading out by 25°), gone in a storm. It met the owner's condition: a few lines, no custom sky shader.
- **Ambient** 0.3, sun 1.0 at noon. Shadows: four cascades out to 250 m, blended at the seams.

Measured on `tools/gui-check.sh` frames (sep = machine against background, sd = scene spread):

| Machine | before sd / sep | after sd / sep |
|---|---|---|
| antikythera-lunar-train | 43 / −91 | 68 / −123 |
| roman-crane | 29 / −34 | 37 / −62 |
| newtons-cradle | 34 / −25 | 45 / +62 |
| water-wheels | 10 / +26 | 38 / +64 |
| herons-fountain | 17 / −7 | 23 / −93 |
| mars-stirling | 15 / +4 | 20 / +40 |

The spread (sd) also depends on how much of the frame the machine fills. Heron's fountain, the water wheels and the Mars engines are framed small; camera framing is #86. Frames: `docs/art/skins/legibility-before-after.png`. To aim a test frame, `tools/gui-check.sh` takes `look YAW PITCH [DISTANCE]`.

### 12.1 Vessels, water, mirrors (2026-10-07, owner rule: readable over realistic)

- **Water** is `#1F5BAA`, deep and saturated, well below any sky's value. It was `#408CD9`, the sky's own pale blue. Water held in a tank is 95% opaque; streams and falls stay translucent.
- **Vessel walls** (tank shells, pump barrels, the cistern, hoppers) use one helper, `Shapes.Glass()`: slate `#5E8696` at least 32% opaque, so an empty vessel still reads against a pale sky.
- **Earth's ground** is nearly neutral (saturation 0.03), so blue water and glass differ from it in hue as well as value. **Mars's** ground value is 0.18.
- **Mirror faces** are drawn bright: pale polished bronze `#F2CF85` at metallic 0.5. At metallic 0.9 they showed Mars's pale sky and read tan on a tan ground. Their posts are oak through `Surface`, so they get grain and the outline. Dust still dulls them toward brown.

Re-measured (sd / sep): herons-fountain 17/−53 → 19/−56 at default framing, 37/−72 framed close. mars-stirling 20/+40 → 23/+48. Others unchanged (antikythera 69/−123, roman-crane 38/−62, newtons-cradle 43/+54, water-wheels 36/+63). Frames: `docs/art/skins/vessels-mirrors-before-after.png`.

### 12.2 Joint fittings: the combination rule (2026-10-07, #102)

`Skins.FitJoints` runs once after a view is built. Wherever the physics joins two bodies, it draws what a workshop would put there. It reads only the scene tree, so it covers joints in builders it has never seen.

- **Hinges** (`HingeJoint3D`: axles, pivots, pins) get a collar along the hinge axis with a cap at each end. **Ball joints** (`ConeTwistJoint3D`, and `PinJoint3D`s the machine declares by name) get a ball.
- **Size** comes from the thinner of the two bodies (the smallest extent of its own opaque meshes, not counting other fittings), clamped 6 mm to 120 mm.
- **Material** is the harder of the two parts' materials if either is a metal (steel on Newton's cradle, bronze on the Antikythera arbors), else iron.
- **Not fitted:** chain links (their pins are unnamed, one per link), welds (`Generic6DofJoint3D`, made at run time by grips) and slides (pistons draw their own cylinder).
- The fitting rides on one of the bodies, so it turns and swings with it.
- `HEROIC_DEBUG_PHYSICS=1` logs each fitting: kind, material, pin size, host body.

Frames: `docs/art/skins/joint-fittings.png`. The rest of #102 is in 12.10.

### 12.3 Terrain and haze (2026-10-07, #103; readable over realistic)

- **Haze follows the view.** Depth fog begins at `SkyLook.Fog` camera-distances beyond the orbit's pivot and is full by six times that: 3 on Earth, 2.2 on Mars, nearer in a storm (÷ (1 + τ/4)). `Main._Process` updates it every frame. The old fixed 0.004/m hid the 850 m crater almost entirely (frame stdev 6).
- **Ground shader** (`TerrainView.GroundMaterial`):
  - Toon diffuse, no specular.
  - **Contour lines** at a round interval (1, 2 or 5 × 10ⁿ m, about a dozen over the map's relief), every fifth heavier. Their width is set on screen, and the fine ones fade where they would crowd together.
  - **Soil colours converted from sRGB.** Vertex colours had been taken as linear, which made every soil pale and grey. That was true of the old terrain too.
- **Hillshade** baked into the vertex colour: 0.45 to 1.0 from a fixed high light, cartographic rather than the sun's, so slopes read under any light.
- **Soils** are drawn at 85% of their table saturation, each keeping its hue so the crater's layers stay distinct. Water vertex colours are marked sRGB as well.
- **Mars light** is floored at 0.75 of Earth's (was 0.45), with ambient 0.45, because the crater went murky.
- `look YAW PITCH DIST X Y Z` in `tools/gui-check.sh` also sets the pivot.

| World | before sd / sep | after sd / sep |
|---|---|---|
| lonely-rover-opening | 6 / −58 (all haze) | 30 / +79 |
| crater-wind | 6 / −60 | 31 / +77 |
| talus | 26 / −43 | 57 / −109 |
| flood-plain | 19 / −35 | 51 / −100 |

**Reading the ground's shape while driving (2026-10-08, ground-read).** The crater's contours are metres apart, so near the rover the ground was one flat brown. The ground shader now adds, all fading with distance from the rover (`rover_pos`, a global shader parameter `TerrainView.Refresh` sets each tick) and from the camera:
- **Slope tint** against the rover's own limit (`RoverSpec.GradeDeg`, 30): none under 15 degrees, warm `#E8975A` 15 to 25, amber `#F5B82E` 25 to the limit, red `#E5362A` beyond it. Faded out between 30 and 60 m of the rover. The slope is the true one (COLOR.b, from neighbour-to-neighbour grades of the unsmoothed heights; the smoothed normals blurred a 1 m 45 degree bank to 20), so it reads on the 5 m crater cells, the 1 m dig-bank cells and the 0.25 m worked patch alike. The colours are `TerrainView.SlopeWarm/Amber/Red`; the rover panel's "ahead" line uses them.
- **Fine contours** every 0.5 m of height within about 30 m, and a **draped 1 m grid** within about 25 m (it bends over bumps and dips). Both pale on dark soil and dark on pale soil, fixed one to a few pixels wide, faded where they would crowd and beyond 40 to 110 m from the camera.
- **Brighter hillshade** from a fixed map light (`TerrainView.MapLight`, from the south-east, 40 degrees up; map and patch share `HillShade`), remapped to 0.68 to 1.12 in the shader, with 16% of it unlit (emission) so slopes facing away, the tint, the lines and the grid still read at night; before, the crater at night was black.
- **Panel:** the Driving section's "ahead" line gives the steepest metre of ground along the heading over the next 3 m (`GroundGrade.Ahead`), amber from 25 degrees and red from 30 with "too steep to climb". The steepest metre, not the 3 m mean: a 1 m bank at 45 degrees averages 18 over 3 m.


Also mars-stirling 23/+48 → 29/+60, mars-sols +73. Crater frame rate in a 1280x800 window is 113 fps steady (123 on the old terrain).

Frames: `docs/art/skins/terrain-before-after.png`, `terrain-crater-cargo.png`. Not done:
- the opening world's camera pivots on the map's origin, not the cargo (framing, #86);
- vertex colour steps show as stair-steps on the slide's debris fan at 5 m cells;
- no dust, ice sheen or wet-sand darkening yet beyond the existing scour and deposit cues.

### 12.4 Terrain cues (2026-10-07, #103 follow-ups)

- **No stair-steps.** Each cell's look (soil colour, the loose, scour and deposit cues, and roughness in alpha) goes into an `nx × nz` texture the ground shader samples bilinearly by world position. Before the texture is made, the finished colours get two passes of a 3×3 blur. Soils and cues change cell by cell (a slide lays its rubble down as its own soil), so their edges were 5 m staircases in the data. Smoothed, they read as lines.
- **Shading from smoothed heights.** Shading normals come from a smoothed copy of the heights. Mesh positions stay the true heights, so what is seen is still what bodies land on. The stepped scar left by a slide stays visible where it is real geometry.
- **Wet ground.** A second `R8` mask marks cells with water deeper than 0.5 mm. It's updated only when some cell changes. Wet ground draws 40% darker and glossy (roughness 0.25), so seeping shows before water pools.
- **Ice sheen.** Ice-cemented soils have roughness 0.3 against 0.95, and the shader uses `specular_toon`, so they glint in the sun.
- **Terrain water** uses the palette's `Shapes.Water`: lighter where shallow, darker where deep.
- **Speed.** The ground mesh is built from arrays in one `AddSurfaceFromArrays` call, with soil looks computed once per soil and the index buffer cached. A rebuild on the 170×170 crater takes about 17 ms, down from about 65 ms. That matters because rebuilds repeat while the ground settles. Steady frame time matches `main`.

Measured (sd / sep): lonely-rover-opening 33/+88, crater-wind 33/+85, talus 53/−98, flood-plain 49/−96. Frames: `docs/art/skins/terrain-cues-before-after.png`; top row is the debris fan before and after colour smoothing. Not done: settled-dust look after a storm.

### 12.5 State legibility checklist (2026-10-07, #105)

The design promises "the state is shown in the scene: a cold bank frosts, a stalled generator stops, a leaky lid glows". Each state the sim holds is listed here with how it shows and the frame that checks it. Strain uses `Skins.Rim` (amber from 60% of a limit, red at 100%, thickening to 3.5×; on glass no plain line, only the warning). Heat uses `Skins.Glow`.

| State | Cue | Frame check |
|---|---|---|
| Boiler pressure toward burst | Rim on the shell (#139) | boiler-shells: lead pot black at 0.58, amber ~0.73, red 0.99 (t 450 to 500 s), then wreck and steam cloud |
| Boiler burst | Shell hidden, wreck and cloud | same run, t > 505 s |
| Boiler dry and overheating | `Skins.Glow` on the shell | none yet: no blueprint boils a pot dry over a fire |
| Glass pane load toward cracking | Rim on the glass | glass-rooms: glazed and dark roofs at 85% of their crack load read orange |
| Pane cracked | Glass hidden, frame shown | glass-rooms: thin roof |
| Rope tension toward breaking | Rim on the rope | roman-crane with a 13 mm rope (scratch copy): amber while lifting |
| Rope broken | Segments hidden (**bug fix**: a broken rope used to stay drawn, looking whole) | roman-crane with an 11 mm rope: logged hidden after "rope hoist broke: 5730 N exceeds its 5702 N" |
| Tank ice / frozen solid | Ice slab, whiter when solid | existing |
| Cold room | Skin frosts white from 0 to −5 °C | existing (Enclosures) |
| Room leaking air | Hiss particles by flow | existing |
| Mirror dust | Face browns, beam dims | existing |
| Crucible, heater hot | `Skins.Glow` | existing |
| Galvanic jar spent | Iron rusts | existing |
| Windmill, Stirling stalled | Motion stops | implicit only |

| Battery bank charging | A fill column in the armoured case rises with the charge; green while it takes charge | `docs/art/skins/found-electrics-charging.png` |
| Bank cold or hot | Column blue-grey below 0 °C, red above 45 °C; amber when full but out of range | `docs/art/skins/bank-bench.png` |
| Bank full and warm, called | Cyan with a faint glow (a state cue), a column of light stands over it once the call has gone out | `docs/art/skins/found-electrics-called.png` |
| Generator | A motor can on the axle with a dial (a needle over the rated current) and a stripe that turns with the rotor; a copper line to the bank | `docs/art/skins/found-electrics-charging.png` |

**Not drawable yet, because the sim has no state for them:** heat leaking through a lid ("a leaky lid glows"), and pipes freezing. These need model work before any look. (The bank and generator of #64 are drawn as above: the case, column, dial and wire are drawn 2 to 3 times a real pack's size, since the scene they sit in is a 5 m windmill; the physics is the sim's. Their labels are the largest on screen so that the declutter keeps them over a vault's. The frames were taken with `HEROIC_SET="scene time 12; scene clock-rate 0"` (the machine's own 02:55 is dark) and, for the called frame, `bank call-any-time 1`; the camera of `found-electrics` is the menu one, the frames use `look`.)

### 12.6 Recognisable machines (2026-10-07, #167)

A machine reads as a crane or a mill from how its parts combine, not from any one part.

1. **Scale figure** (`Main.Composition.cs`). A plain 1.7 m person in muted slate stands beside every machine; **P** shows or hides it. It isn't part of the machine, so framing and bounds ignore it. Placement is a rule, not a choice per machine:
   - It tries four spots in turn: beside the machine on its screen-left, then screen-right, then just in front near either end.
   - It takes the first spot where the whole figure stands in the clear part of the screen (`ClearArea`).
   - It shows no figure when no spot fits (noria, sawmill), when the machine is tabletop-sized, or when a person would stand more than 1.3× the machine's height (Heron's fountain, the aeolipile). In those cases the figure would dominate or be cut off, not serve as a ruler.
   - Worlds get none (the rover is their scale).
2. **Structure recedes** (`Skins.RecedeStructure`, run once after a view is built). Wood and stone meshes that ride on no rigid body (posts, frames, supports, footings) draw at 80% value and 70% saturation. What moves keeps its full colour. Metal, glass, water and glowing parts are left alone, and so is any material shared by moving and still meshes, since builders keep live handles on their materials.
3. **The working path:** checked, not changed. Water is already the strongest colour in every frame that has it (§12.1). Thrown stones and bolts have their flight trail.
4. **Named assemblies:** waits on #24 (assemblies as Racket macros), which doesn't exist yet.
5. **Silhouette check:** `HEROIC_SILHOUETTE=1` draws the machine solid black on white (labels, particles, ground and effects hidden), and `tools/silhouette_sheet.py` crops and sheets the frames. Each mesh gets its own black material, reset every frame. With one shared material, the tank code's per-frame ice colour repainted every mesh white. The same hazard applies to any shared material a builder mutates through `MaterialOverride`.

Frames (`docs/art/skins/`): `recognisable-before-after.png` (pairs: crane, shaduf, trebuchet, noria / sawmill, Heron's fountain, aeolipile, lantern) and `silhouettes-ancient.png`. All eight silhouettes are distinct. The Kongming lantern's two lanterns read as plain cylinders on the ground; an envelope shape would help. legibility.py, sd / sep before → after: crane 42/−65 → 44/−68, shaduf 37/−75 → 40/−81, sawmill 55/−92 → 58/−97, Heron 29/−74 → 30/−77, noria 42/−86 → 42/−87, aeolipile 34/−69 → 34/−70, lantern 41/+83 → 41/+82, trebuchet 16/−135 → 20/−112 (its figure now shares the frame).

### 12.7 Labels at a constant screen size; the trebuchet's opening shot (2026-10-07, #148)

- **Labels** (`Skins.ScreenLabels`, and for labels added mid-run, a `NodeAdded` hook in `MachineView`):
  - every `Label3D` in a machine is `FixedSize`, at 0.0011 pixel size;
  - fonts are capped at 28 pt;
  - outlines are at least 8;
  - long readouts wrap at 220.
  - So a close-up's tags (the aeolipile's "kettle") no longer fill the frame, and a wide shot's no longer vanish. Impact readouts, fracture tags and room readouts follow the same rule.
- **The scale figure's "in front" spots** must not cover the machine's own outline on screen (one hid a block in the drop test). Spots beside the machine are unaffected.
- **The trebuchet** opens close, from behind its shoulder; the follow camera widens along the throw. Opening frame fill went from about 2% to 9% (`legibility.py`: sd 30, sep −83).

Frames: `docs/art/skins/labels-and-trebuchet.png`.

### 12.8 Label density (2026-10-07, #148)

`Main.Labels.cs`: about twenty times a second, every visible label in the running machines is laid out on screen, most important first:
- readouts (any label with a digit) come before part names;
- then larger text;
- then newer labels.

A label that would overlap one already placed fades out until there's room again. **L** shows every label. A label is hidden by zeroing its text and outline alpha, re-applied just before each frame is drawn, because impact readouts and digger tags rewrite their own alpha every frame. The alpha a builder last wrote is restored. `Visible` is never touched, since builders own it. `GeometryInstance3D.Transparency` was tried first, but a Label3D drawn without depth testing ignores it. Frames: `docs/art/skins/label-density.png` (all labels, left; laid out, right).

### 12.9 Warmth you can see, and turning you can see (2026-10-07, #169 and #172)

**Warmth tint** (`Skins.WarmthTint`, `Skins.Warmed`, `Skins.Warm`). A wash over a part's own colour, by the temperature the sim holds for it:
- below 0 °C a cool blue-grey, at 20 °C nothing (the material's own colour), a burnt ochre by 100 °C (share 0.55), through to a dull red by 400 °C (share 0.65), held there;
- `Incandescence` starts at 500 °C from that same dull red at zero energy, so the two join with no step;
- never more than a wash, so the material still reads. `Warm` keeps the albedo the material had when first asked and works only on the albedo; emission (`Glow`) and the outline (`Rim`) are not touched. A surface its builder recolours every frame (an enclosure's skin: frost) folds `Warmed` into its own colour instead.

Used wherever the sim has a temperature for a part: every boiler's shell (`Boiler.Temperature`; `MachineView.Warmth.cs`), crucible bowl, sand and glass (`Crucible.Temperature`), enclosure skin and heater (`Enclosure.Temperature`). Not invented: a brake or bearing holds only heat in joules (no temperature), so those carry no tint; the Stirling's face keeps its own hot-to-cold colour.

**Stored heat (#71, `MachineView.HeatStores.cs`).** A heat store (a bed of rock, the battery bank, a tank of water) is a block the size of its volume (a cube; a cylinder for water) washed by `Skins.Warm` with its own `HeatStore.Temperature`, plus `Glow` from 500 °C: 200 °C rock reads as an ochre wash on dark basalt, a frozen bank blue-grey. A lidded bin is an open-fronted insulated box whose lid hinges at the back and swings to 105 degrees with `HeatBin.Open`, so a store letting its heat out looks open. A room with a regolith wall is drawn cut away (back and sides, front and top open), its inner face four 1 cm sheets washed with `HeatSlab.SurfaceTemperature`, so the warmth arriving in the wall shows as its face warms from blue-grey; a label gives the depth the heat has reached. Each store's label gives its temperature and, for a bin, the lid's state and leak. Frames: `docs/art/skins/heat-store-vault.png` (the tight vault 1.7 h in: 135 °C rock, a -40 °C bank, the wall's face at 32 °C and 3 cm deep) and `heat-store-tanks.png` (three tanks of water, the 10 kg one's room frosted); both taken with the sun held at 17:00 (`HEROIC_SET="scene clock-rate 0"`), because at night the game's own lighting leaves the whole scene near black.

**Steam at boiling.** Every boiler has a plume at its lid that is on exactly while the water is at or above `Boiler.SaturationTemperature(zone pressure)` (100 °C at sea level, 0.1 °C on Mars), the boiler is not dry and not burst. Set in the same frame the sim crosses the line (`HEROIC_DEBUG_PHYSICS=1` prints the crossing). It is separate from the older vapour wisp, which shows at 25 K over the air.

**Turning marks** (`Skins.MarkTurning`, `MachineView.Marks.cs`). Everything that turns carries a mark painted on, not built: a pass added to the part's own surface (after it, before its outline, so nothing is replaced), a shader in the part's own space.
- a stripe a tenth of the radius wide along one half-plane through the axis: on a wheel, disc, drum, pulley or gear it runs hub to rim across each face and over the rim; on a shaft (a drum) it runs along the side; on a ball it is a meridian. Dark on a light surface, pale on a dark one;
- above 15 turns a second (900 rpm), where a stripe aliases at 60 fps, the stripe fades out (gone by 30 turns a second) and a blur ring takes over: a band over each face between 0.4 and 0.92 of the radius, and all of a rim or shaft's side, opacity 25% at 15 turns a second rising to 75% by 30;
- speed is the part's own: a body's angular velocity about its axle, or the rotor's or jet wheel's sim rpm.
- the mark is a pass on the surface of that one mesh, so it is only given to surfaces no other mesh shares (the rotor's ball and jet wheel's hub get a surface of their own). `UseOutline` looks through it, so palette thumbnails keep their wide line.
- not marked: the fixed axle rods drawn behind a group of wheels (they do not turn in the view, and wheels on one rod may turn at different speeds), and the water wheels' and windmills' spoked frames (their spokes already show the turn).

`HEROIC_MARKS_REPORT=1` prints each wheel's mark angle about its axle and the time the ring comes on, to check a frame against the trace. Frames: `docs/art/skins/warmth.png`, `docs/art/skins/turning-marks.png`.

### 12.10 HUD theme and the rest of the part detail (2026-10-07, #106 and #102)

**HUD theme.** `game/scripts/Theme.cs` (`HudTheme`) builds one Godot Theme in code: panels `#2B2724` (94% opaque) with a dim bronze edge, buttons raised warm grey (`#3B342F`) with a bronze edge on hover and solid bronze with ink text when pressed or on, cream `#F1E9D8` text with a panel-coloured 3 px outline (so the bottom help line still reads over a pale ground), bronze `#CC8F4A` for rules, scroll grabbers, carets and the window title. The build console, the only `RichTextLabel`, is the rover's ledger: ink `#2B2724` on paper `#E8DFC8`. Contrast: cream on panel 12.3:1, cream on a raised button 10.1:1, ink on pressed bronze 5.4:1, dimmed text `#B8AE9C` on panel 6.7:1, ink on paper 11.2:1; all above 4.5:1.
- A theme does not cross a `CanvasLayer`, and there is no project theme to merge into at runtime (`ThemeDB.GetProjectTheme()` is null), so `HudTheme.Install(layer)` is called once per layer (one line each in `Main.BuildUI` and `BuildMode.BuildUi`) and themes every direct child of the layer, including ones added later, and dialogs. No per-widget styling is needed; the few overrides left only change size or alpha.
- Font: Godot's bundled default, so no font was added and `docs/assets.md` needs no entry. Any font added later must be SIL OFL or equivalent and recorded there (section 8).
- Frames: `docs/art/skins/hud-theme-run.png`, `docs/art/skins/hud-theme-build.png`.

**Part detail rules** (all in `Skins.cs`, one per feature, drawn with extra primitives and never imported meshes):
- `HoopTank`: flat iron hoops round a tank, one per 0.9 sides of height, 1 to 4 of them (4 strips each, children of the shell, so a hung tank carries them).
- `BandBoiler`: a band near each end and 1 to 4 between, by height against radius; bronze on an iron or steel boiler, iron otherwise, so the bands read against the shell. Boilers with a rating still turn their own outline amber and red; the bands are separate meshes with their own material.
- `RimWheel`: a rim bead on each face and a hub boss on a solid `disc-wheel`, not on a millstone (stone, or a grind torque). Deliberately no spokes on a disc wheel: it is solid on purpose (the carts demo compares it with a spoked one), and the generated spoked wheels (cart-wheel, noria, water wheels) already have theirs.
- `PlankSeams`: wooden boxes wider than 0.3 m get dark seams along their length on their two broad faces, one plank to about 0.2 m of width (2 to 6 planks); a beam or post is one piece and gets none. Runs once after a view is built, so any wooden box qualifies, a crate, cart bed or platform.
- Shared-material hazard respected: every detail uses its own material instance (made for the part), none replaces a builder's `MaterialOverride`, and seams carry no outline. The Kongming lantern envelope was done by another agent.
- Frames: `docs/art/skins/detail-before-after.png` (crate, Heron's fountain, Newcomen's boiler, carts' wheels).

### 12.11 Pressure you can see, and scales on vessels (2026-10-07, #170 and #174)

**Dials** (`MachineView.Gauges.cs`, rule functions in the last block of `Skins.cs`). One dial for every pressurised vessel; the needle sweeps 270 degrees from lower left over the top (`Skins.GaugeAngle`: `135 - 270 x share` degrees).
- A **rated boiler** (its own #:burst or a #:wall shell): full scale is its burst limit as it stands now (`BoilerShare`: gauge over `BurstLimit`, derated by heat), an amber tick at 60% (`Skins.RimFrom`, the value `Skins.Rim` starts at) and a red one at 100%. The needle and the shell's outline read the same share, so the needle crosses the amber tick on the tick the outline turns amber.
- No limit, plain scale, no amber or red: an unrated boiler (200 kPa gauge), a cylinder on an unrated boiler (100 kPa; on a rated boiler, that boiler's limit with the same marks), a sealed air vessel (25 kPa), a room (100 kPa gauge over the air outside).
- **Below the air outside** the needle swings the other way from zero and turns blue (`Skins.Vacuum`), a quarter turn at a full vacuum: a Newcomen cylinder under the cold jet reads -76 kPa at 2.0 s. Scale for the negative side is the ambient pressure.
- Placement: a boiler's dial stands proud of its bands; a cylinder's is low on its casing; a sealed air vessel's (Heron's fountain) is on its shell and rides with it; a room's is on its front wall and follows it as it billows.
- **Soft rooms** (membrane enclosures, `Skins.Billow`): slack and flat at nothing over the outside, full height by 1 kPa, then up to 8% wider and taller as they fill (saturating near 30 kPa). A wood, stone or metal room keeps its shape and only has the dial.
- **Jets** (`Skins.JetSpeed`): a gas leaving a hole, a door's rush and a safety valve's plume all leave at `0.4 + 2.6 x sqrt(dP / 101,325 Pa)` m/s (to 3 m/s), so a harder push always throws farther. Particle count still follows the flow. Water jets from tank holes (Leaks.cs) already follow `sqrt(2 g h)`.
- **Suction limit** on a lift pump's pipe: the existing red tick is where this water at this temperature lets go ((P - P_v)/(rho g) = 10.09 m on Earth at 20 C, nothing on Mars); a pale line above it at P/(rho g) from the pump's own air and gravity (10.33 m on Earth, 0.164 m on Mars).

**Graduations** (`MachineView.Graduations.cs`). Every tank and hopper gets a scale on its wall, as children of its shell (a hung vessel carries them) in one merged mesh per colour with its own unlit material.
- Plain marks: dark rings at round steps (1, 2 or 5 times a power of ten) of capacity, no more than ten, in litres for vessels of 0.1 to 1 m² (or any a wake watches as `water`) and cm of depth otherwise (or any a trigger or wake watches as `level`; hoppers always). The lowest and highest are numbered.
- **Thresholds** in the trigger's amber (`Skins.Watch`), a little thicker, always numbered: every wake term or event, and every field trigger, on the vessel. `water` is litres, `level` cm.
- **Capstan slip line**: a hold scale beside the hauler, as tall as the load's weight; a bar for the hold (green while it holds, red once it slips) and an amber line at weight / e^(mu theta), numbered "slips below N".
- Not done: a time scale on a clock's vessel (the header's rate is prose, not a declared field the view can read); the Stirling engine, bellows and balloon envelopes have no dial.

**Checks** (`HEROIC_GAUGE_TRACE=<path> HEROIC_GAUGE_DT=<s>` logs each dial's share, needle angle, rim colour and kPa, each pump's two lines and the first frame a drawn level reaches each mark, beside `HEROIC_TRACE`):
- airlock: needle angle equals `135 - 2.7 x gauge(kPa)` from the trace exactly at 100, 400 and 700 s (chamber 64.388, 123.147, 135.000 degrees); a fit of the red pixels in a paused close-up gives 66.40, 123.28, 135.13 against 65.91, 123.15, 135.00 logged at the same pause. Against absolute pressure the gap is a constant 1.65 degrees (Mars's 0.61 kPa).
- boiler-shells: lead pot needle reaches -27.0 degrees (60%) and the outline turns `ffb219` in the same frame, t 452.608 s, at 144.0 kPa = 0.6 x 240 kPa.
- suction-limit: pale line at 10.3287 m over the well = 101.325 kPa / (1000 x 9.81); red tick at 10.0913 m. earth-machines-on-mars: 0.1644 m = 610 Pa / (1000 x 3.71).
- wake-clock: drawn level reaches 20, 30, 40, 50 L marks at 10.000, 15.008, 20.008, 25.008 s; the trace crosses them at 10.000, 15.000, 20.000, 25.008 s; sleeps woke at 10 (either), 15 (guarded), 20 (both), 25.01 s (filled), each within one 1/120 s step of its mark. dam-break: 55 cm mark drawn at 20.000 s, trace 20.008; hillside-pond: 10 cm at 10.542 s, trace 10.542.
- water-clock: receiver marks every 20 cm are crossed at 67.4, 134.8, 202.2 s = 20 cm / 0.2967 cm/s (the header's 2.967 mm/s).

Frames: `docs/art/skins/pressure-and-graduations.png`.

### 12.12 Water you can see moving, and what a machine produces (2026-10-07, #171 and #173)

Each cue is drawn from a number the sim already holds, or not drawn (see the last list). All of it is new meshes or per-mesh materials; no builder's material was touched. Code: `MachineView.Flow.cs`, `MachineView.Products.cs`, and `Skins.FlowDashes` (the shader, appended to Skins).

**Flow along a pipe (one rule for every `pipe`).**
- The sim's `Pipe` has a conductance and no diameter, so every pipe is drawn with one bore: 30 mm radius (it was a 6 mm rod, under a pixel at any distance). The pipe is now iron, not bronze: bronze with pale dashes measured Heron's fountain at sep -61 (before -83), bronze with blue dashes -80, iron with pale blue dashes -87.
- A sleeve of dashes (15 cm to a dash and its gap) rides each pipe. They move at the speed the water would: flow ÷ the bore's cross-section (π·0.03² = 2.83 L per metre), signed by the flow's direction, advanced by the sim clock (so they pause with it and follow the speed buttons). No flow, no sleeve.
- Above 15 dashes a second, where a dash would alias at 60 frames, they fade into an even band, complete by 30 (the turn marks' rule).
- Channels already carried foam flecks at the water's own speed (Channels); constant-head has channels, not pipes, so it is covered by those.
- Check, cistern-and-trough (`HEROIC_FLOW_REPORT=1` prints it every 5 s): trace `feed.flow` 3.333 L/s at 5 s → 3.333 ÷ 2.827 = 1.179 m/s = 7.86 dashes/s; 2.235 L/s at 60 s → 0.791 m/s = 5.27 dashes/s. The report prints the same, from the pipe's flow: 7.86 and 5.27. Frames: `flow-pipes-noria.png`.

**The pour where lifted water arrives.**
- A noria's pour comes once per bucket. A bucket tips when its angle (read from the wheel's body) reaches the angle of the trough's lip; the stream is drawn from that bucket's lip down to the trough's water, for 35% of the time between buckets, thickness from the bucketful over that time. The sim lifts continuously; the pulses are the same water in bucketfuls.
- Check, hama-noria: 30 pours between 1.08 s and 59.44 s = 0.49692 a second. The trace's mean `raise.rpm` over the same time is 1.2423, × 24 buckets ÷ 60 = 0.49691. Ratio 1.00002. water-mill-race (12 buckets) 1.014 over 41 s, since its speed is still settling.
- Screws and pumps pour continuously, as their water moves (Lifts, Pumps): checked on archimedes-screw (`levels-oxygen-screw.png`).

**Water held in a bucket.** Each of a noria's 24 pockets fills across the river's surface (the angles where the circle the buckets ride crosses the water's surface, from the river tank's level), stays full up the rising side, and empties across the pour window. Drawn as a blue slab a hair proud of the bucket's side walls, so its level reads from the side (readable over realistic), against the back wall, as long as the water is deep. Own material for each pocket, parented to the view, not the wheel. **Hazard:** any mesh parented to a `RigidBody3D` changes the box `BoxSize()` measures for its buoyancy and drag. The first build parented them to the wheel and hama-noria settled at 1.1776 rpm instead of 1.2335. Keep added meshes on the view and set `GlobalTransform` from the body each frame.

**Heaps, levels, piles.**
- A heap's volume is mass ÷ density, exactly: a flattened box 1.6 : 0.4 : 1.6 whose scale's product is that volume. Gold 19,300 kg/m³ (the sluice's heavy density), sand 2,650, flour 600 (loose-poured; gristmill's header gives no bulk density, so the stated 600 in `Mills` stays). It was gold at 1.7× and flour in a cone cut off at the top; both are exact now.
- Check, placer-sluice (mesh scale read back, `HEROIC_FLOW_REPORT=1`): 10 s, 20 g kept (trace `riffles.kept-heavy` 0.02) → 1.0363 cm³ = 0.02 ÷ 19,300; 60 s, 120 g → 6.2185 cm³ = 0.12 ÷ 19,300. A cube of that is 1.0 and 1.8 cm, so a gold-yellow, unshaded disc (14 cm to the channel's width, as the cube root of the heap) floats on the water over it while any gold is kept. It is a marker; the heap is the quantity.
- Check, gristmill (sharp stone, 120 rpm): trace `sharp.flour` 0.5908 kg at 20 s → 0.5908/600 = 0.985 L, a cone as high as a third of its radius: 4.71 cm; 2.604 kg at 60 s → 4.34 L, 7.72 cm. The mesh reads back the same.
- A tank that holds 5 mL or more draws its water at least 4 mm deep (below that, the true level, from 1 mm). Rain-house's gutter holds 17.7 mL at 60 s (true level 0.07 mm); holy-water's basin 0.195 L (1 mm true). Drawn thicker than life, as the ice sheet is.
- Hearths shrink with their fuel (`Fuel ÷ initial`) as before (bellows-forge, blown hearth: 0.92 at 60 s, 0.84 at 120 s); plants grow as the cube root of wood per square metre as before.
- Oxygen: pale green-white motes rise from a plant bed while its growth beats its respiration, which is when the sim adds oxygen (`Plants.OxygenMade`).

**Legibility** (`tools/legibility.py`, six references, 1280×800, `--hidden`, 6 s of sim; sd / sep, before → after): antikythera 71 / -127 → same; roman-crane 46 / -71 → same; newtons-cradle 43 / -83 → same; water-wheels 34 / -61 → same; mars-stirling 29 / +55 → same; herons-fountain 33 / -83 → 34 / -87.

**Not drawable yet, because the sim has no state for them:**
- **hierapolis-sawmill's kerf:** nothing models stone removed; the stone is a fixed post and the saw a moving block.
- **shaduf's water:** the buckets are granite blocks, sized by what they carry; there is no water in them.
- **greenhouse growth at trace values:** 1.9e-5 kg of wood at 60 s over 10 m² puts the crown scale (cube root) under its 0.08 floor. It grows once time runs on (#168).
- **trench-crew and dig-out spoil:** the heap is the ground's own (loose cells, paler), drawn by `TerrainView`.

Frames (`docs/art/skins/`): `flow-pipes-noria.png`, `products-heaps.png`, `levels-oxygen-screw.png`.

### 12.13 Framing between the panels, labels that step aside, throws you can follow (2026-10-08, #190 and #86)

A re-sweep of all 103 machines after waves 1-3 (gauges, graduations, flow, operators, reload). Code: `Main.Framing.cs`, `Main.Labels.cs`, `Main.Trail.cs`, `Main.Composition.cs`, the Profiles block of `Main.cs`. Frames: `docs/art/review/framing-2026-10-08.jpg`, `labels-action-2026-10-08.jpg`, `throwers-2026-10-08.jpg`; the verdicts are in `docs/machine-review-2026-10-07.md` under *Re-sweep*.

**The clear area was measured in the wrong units.** The viewport is 1600 x 1000 whatever the window (project.godot stretches canvas items), so at 1280 x 800 the left column ends at 295 units and the info panel starts at 1220. The settled clear area was 230..1300: window pixels taken as viewport units, so a machine centred by `CentreInClearArea` could still run 80 units under the info panel (castellum-aquae, floats, gristmill, windmills, tank-leaks, hierapolis' header tank). It now takes the info panel where it stands and the collapsed left column at 296.

**Fit, then keep the action in frame.**
- After centring, a machine still wider or taller than the clear area (less a 24-unit margin) is backed off along the camera's own line of sight, at most 2.2 x its profile's distance, and nudged up or down only if an edge is out. A profile is the closest a machine is framed; angles are never changed. This is the distance rule that was withdrawn in #86 (bounds include ponds and launch arcs), brought back with a cap and with a better measure:
- What is fitted is the union of each part's own box on screen, not the projection of the one box round them all. That big box's near corners stand in empty air in front of a deep machine and measured newcomen-engine 79% "outside", two-modules 66%, solar-steam-wheel 66%, while each sat whole in its frame. Parts more than 3 m underground (the Newcomen engine's 48 m pump rod) are left out: a mine is framed at its head.
- A profile that looks away from the machine as built (under 20% of its parts in the clear area) is aimed at where the action will be (material-samples' cubes fall into the frame) and is left alone. Throwers keep their own camera.
- What a machine sets moving is followed: a body more than a quarter of the machine's size (at least 0.5 m) off it is fitted in as the camera eases out; one the camera could only fit by backing off further than 2.2 x is let go, and the camera goes back to the machine. Not followed: bodies falling below the ground (bare buried-crate), further than the machine's size plus 30 m, worlds, throwers, or anything after the player has moved the camera. Checked: ball-ramp follows its balls to 10.5 m (home 4.8) and returns when they roll on; carts keeps both carts and the slope in frame (20.8 m, home 10.5); rail-wagons, carts side-on so the run crosses the screen.
- The scale figure no longer stands in front of a machine where it would look more than 15% taller than beside it (it stood before the post-and-lintel crane, half again too tall).

**Labels step aside before they fade.** Each label, most important first as before, is tried on its anchor, then one step to either side, and (a name only) above and below; it fades only when none is clear. A number keeps its anchor's height and goes one step aside at most, since a level mark's number, a water level or a gauge's reading belongs to that height. A readout wrapped to three lines or more grows up from its anchor instead of hanging over its part (the sluice box's riffles readout over its gold). Besides other labels, a label keeps off: a dial's face (a near-white opaque disc at most 12.5 mm thick), a gold glint (an unshaded one), the scale figure, the throw's trail and landing marks, and both HUD panels (so a label is no longer written under the info panel, e.g. capstans' "two-turns", boiler-shells' bronze pot). Dark discs (jar seals, leak holes) and water films are not kept clear: the Baghdad battery's seals had faded all three of its labels. `HEROIC_FRAMING_REPORT=1` prints every 5 s how many labels are shown, moved aside and faded. Heron's fountain: the supply's "8 cm" is off its dial.

**Throwers.** Trebuchet, torsion catapult and catapulta are framed side-on, the throw running to screen-left and the machine in the right half of the clear area. The follow camera keeps the machine and the missile in the clear area (pivot at their midpoint, shifted to the clear area's centre; distance so 1.2 x the span plus 2 m fits its width), holds on the landing 2.5 s, then eases back to the machine for the re-span and reload of #161; the next throw gets a fresh trail. The figure stands beside the machine at its own depth, not before the lens. Measured: trebuchet 10.6 m then 17.7 m, torsion catapult 29.5 m then 29.1 m, catapulta 36.5 m twice, each landing on screen with the machine. *Since #187 and #202 (2026-10-08): the catapulta's bolt leaves at 33.3 m/s and rests 126 m out, too far and too fast to keep with the machine in frame (it asked for 182 m of back-off and the machine was a speck). A missile faster than 20 m/s, or a throw needing more than 60 m of back-off, is let go: the camera stays at the machine (7.6 m) and the bolt leaves screen-left with its trail. The onager's 29.7 m (46 m of back-off) and the trebuchet are still followed to their landings and the camera is home 2.5 s after they rest. `tools/throw-camera-check.sh`.*

**Worlds.** flood-plain and dig-out have a fixed opening frame (the blueprint agent's checked shots): flood-plain `look 0 32 50 14 1.5 0`, the pond, race, valley and hollow between the panels; dig-out `look 90 35 8 0 -0.75 0`, the crate and the ground over it.

**Legibility** (`tools/legibility.py`, six references, 1280 x 800, `--hidden`, shot at 600 frames, after = the final build; sd / sep, before -> after): antikythera 71 / -126 -> 71 / -126; roman-crane 47 / -71 -> 47 / -72; newtons-cradle 42 / -83 -> 44 / -86; water-wheels 34 / -61 -> 36 / -63; mars-stirling 29 / +56 -> 29 / +55; herons-fountain 35 / -88 -> 35 / -88. mars-stirling's one point is two more mirror labels (s1, s3) now placed beside their mirrors; with the figure hidden it reads the same, so it is the labels' dark outlines in the measured band, not the framing.

**Still open:** falling-stones and tunnel-test drop their bodies from 300-350 m, so the landing view shows only the impact readouts and the bodies are out of frame; crate-tongs' tongs are too small to read beside the crates (a part-size matter, not framing); trip-hammer's two rigs stand one behind the other and its labels stack; ratchet-windlass' four drum names stack in a column above the drums; glass-rooms' membrane readout fades for want of room (L shows it); bare buried-crate still falls for ever (its home is the dig-out world).

### 12.14 Stacked vessels, air you can see, and the head rule on the scene (Heron's fountain redrawn)

The owner asked why Heron's fountain "looks so strange" (three tanks 1 m apart, joined by fat slanted rods) and for a display that makes its function obvious. It is now drawn as Hero drew it: one cut-away vessel, a basin on top, the sealed water (supply) chamber in the middle, the sealed air chamber (receiver) at the bottom. Code: `MachineView.Stacks.cs` (the rules), small hooks in `MachineView.cs`, `MachineView.Flow.cs`, `MachineView.Gauges.cs`; tokens in `Skins.cs`. The rules name no machine; each fires on a shape in the blueprint.

1. **A stack is cut away.** A vessel that holds sealed air, or has another tank directly above or below it (footprints overlapping, the gap at most 0.2 m), is drawn with its near walls removed: the glass is back-faces-only (`CullMode.Front`), and the iron hoop across the front goes. The water level and the air show from the front. A tank that rests on another stands on four oak corner posts in the neck, not on a post to the ground (which would run through the vessels below). A tank in a stack carries its name on its front, over the water.
2. **A pipe up a stack goes round.** The sim's ports sit at a tank's middle, so a drain and a nozzle in one column would lie on each other inside the water. A pipe between tanks of one column with another tank between its ends is drawn up the outside of the column, entering each vessel through its wall; the first such pipe takes the left, the next the right, and so on, each a bore and a half off the wall so it stands clear of the dark frame. The bore is thin beside a small vessel: 7% of its narrower end's width, 30 mm radius at most (the old fountain's pipes were 60 mm across on a 141 mm vessel). A pipe with nothing in its way (the nozzle) goes straight up the middle. The pipe's flow dashes ride each segment. A pipe whose far end has no room (receiver full) moves no water, so its dashes stop (the sim reports its head-driven flow either way). Other machines' pipes are drawn as before.
3. **Air is drawn as air.** `Skins.Air`, a warm cream (the one hue in the machine that is neither water blue nor metal): the air shut in a sealed vessel is a tinted box from the water's surface to its lid, with opacity `Skins.AirTint(gauge Pa) = 0.18 + 0.42 (1 - e^(-P/6 kPa))`, so it is faint at rest and thickens as it is squeezed (0.40 at 4.5 kPa). A receiver filling with water shows its air shrinking and darkening, the supply's growing. A tube of the same tint joins the vessels of a stack that share a pocket, up the side the first pipe did not take, and the pocket's pressure gauge hangs beside it where it covers no water (a lone sealed vessel keeps the dial on its front, as in 12.11). The temple doors' altar and globe take the tint and the cut-away too.
4. **The head rule.** A jet pipe out of a vessel on a sealed air pocket, into an open tank that drains by a pipe into another tank of the pocket, is Heron's fountain, and the scene says its rule: water that falls h (the open tank's surface to the receiver's) lifts the jet about h above the supply's surface. Two matched brackets in `Skins.Head` (deep magenta, away from water blue, bronze and the amber of 12.11) follow the live surfaces: "falls h" on the left from the receiver's surface up to the basin's, "lifts about h" on the right from the supply's surface up by the air's pressure head P/(rho g), which is where the jet's top stands while it runs; level lines run from each end to the surface it measures. While the drain runs the lift is a few cm short of the fall (5 cm at the start): that is the drain's own head loss, flow over conductance, the part of the fall that moves water instead of building pressure. An amber tick marks the nozzle's tip (named once the lift falls short of it): when the "lifts" bracket's top meets the tick the jet is gone. When the drain stops the "falls" bracket dims and says why ("receiver full", "basin empty", "levels met"). It is found from the blueprint, so no hint was added to the DSL.

**Physics.** Restacking changes the heads, so the machine's numbers were re-derived (header of `herons-fountain.rkt`, tests in `operated-test.rkt`): the drain now ends on the receiver's floor, so its head is exactly the fall less P/(rho g), and the rule above is the sim's own. Jet rise J = (F + ss - tip) / (1 + Gn/Gd): worked 27.6 cm (rigid air), measured 26.2 cm; the formula on the run's own levels holds to 0.05 cm through the run; receiver full at 24.2 s worked, 24.0 s measured; the end state from Boyle, supply 1.4249 L, basin 5.8751 L, 3.1065 kPa, all measured to the digit shown. The demo operator's refill at 36 s still returns it to its first state.

**Legibility** (`tools/legibility.py`, six references, 1280 x 800, `--hidden`, shot at 600 frames; sd / sep, before -> after): herons-fountain 35 / -88 -> 49 / -91 (start 49 / -92, dying 50 / -93; before at start, mid and dying all 35 / -88; the machine fills 43% of the clear area, it was 12%); antikythera 42 / -92 -> 42 / -93, roman-crane 47 / -72 (bit-identical), newtons-cradle 44 / -86 and water-wheels 36 / -63, mars-stirling 29 / +55 all unchanged (the frames of antikythera, newtons-cradle and water-wheels differ in pixels from run to run and between worktrees, on identical code too, by the phase of what turns; the water wheels' differ 2,800 px between two builds of the old code). The fountain's camera (`Main.cs` profile) and `tools/pick-check.sh`'s basin aim moved with the stack. Frames: `docs/art/skins/heron-restacked-before-after.png` (start, mid-jet and dying, before above at frames 60, 600 and 1800, after below at 60, 600 and 1500), `docs/art/herons-fountain.png` (mid-jet, replaced).

### 12.15 Wind you can see (2026-10-09, owner ruling after the design review; `game/scripts/WindView.cs`)

A world whose map has a wind field (the crater's corridor, #61) draws it: pale, dust-coloured streaks (unshaded, 3.5 cm thick, at most 35% opaque) drift through a disc 40 m wide round the rover (round the camera's pivot when there is no rover), 0.25 to 2.75 m above the ground. Each one is carried along the way the wind blows where it is (the map's heading, veered by the hour) at that wind's own speed, so the corridor shows as a stronger stream:

- **Denser where stronger.** A streak shows once the wind there passes its own share of 8 m/s (the corridor's night peak is 8.1), so at 8 m/s all 360 are up and at 2 m/s about a quarter.
- **Longer and quicker where stronger.** Length 0.4 + 0.3 v metres (2.8 m at 8 m/s, 1.0 m at 2 m/s); speed v m/s.
- **Subtle.** Alpha fades smoothly from half the reach to its edge, so the edge never shows; the streaks hold still while the sim is paused. They are a cue and not a simulation, and they take no part in the physics.
- **The same number the sails get.** `WindField.SpeedAt` at the machine's solar hour; the rover's panel says it too, "wind 7.2 m/s here" under Driving, and a windmill that reads the map shows its own wind over its sails as before.

Hidden frames (`tools/gui-check.sh --hidden`, the opening world, rover placed on the corridor's line at (-94, -34.2) and 80 m across it at (-66.6, -109.4)): 02:00 on the line 7.2 m/s (gusting): dense long bars; 02:00 off it 4.0 m/s: roughly a third as many, shorter; 14:00 on the line 3.5 m/s: a handful; 14:00 off it 1.9 m/s: a few short flecks. The map gives the wind no heading (the owner kept the default, no 200 degree notch), so every streak drifts the way the mills' wind blows, from +z, and the corridor shows as a stronger band, not as a stream from the notch. Machine runs have no map and no wind field, so none of this exists there: the six legibility references are bit-identical (sd / sep unchanged: antikythera 71 / -127, roman-crane 47 / -72, newtons-cradle 44 / -86, water-wheels 36 / -63, mars-stirling 29 / +55, herons-fountain 35 / -88).

### 12.16 Why some ground reads dark (#242, 2026-10-09)

The owner finds the mix of dark and light ground confusing and has not said where (#241). This is the diagnosis step: every effect that lightens or darkens ground, found in the code, with a measured luminance (0-255, `0.2126 R + 0.7152 G + 0.0722 B` of the PNG's sRGB bytes, as `tools/legibility.py`; the mean of a marked rectangle on a 1280x800 hidden shot, with `camera` printed at each). No game code, shader, world or test was changed. Shots and scripts are in `/private/tmp/claude-501/-Users-marc-Documents-GitHub-heroicinventions/f99b2200-8ada-4180-8dfd-8b6a87d8d3ae/scratchpad/ground/` (scratch space; the table below is the record).

**How it was measured.** The same camera (`look YAW PITCH DIST`, pivot on the rover) at the same spot, in four setups:
- **A, the game:** `lonely-rover-opening`, rover placed with `rover place X Z HEADING`, `HEROIC_SET="scene time H; scene clock-rate 0"` (`lonely-rover-easy` has the same crater and cargo, so the same ground). Shots `opening/<time>-<spot>.png`.
- **B, same ground without the rover's own effects:** `crater-wind` (the same map, no rover), camera pivot set by `look ... X Y Z`. Slope tint, fine contours and the 1 m grid are all keyed to `rover_pos` (`TerrainView.cs:352`, `NoRover` at 300), so with no rover they are off. B shows no rubble or boulders (in that world the rim did not come down), so compare B with A only where the ground is not the slide: flat, rim base, steep, ice, silica; the apron in `rubbleedge`. Shots `norover/`.
- **C, soil swatches:** B again, looking straight down from 6 m at the middle of one soil, noon, studio light. Shots `swatches/`.
- **D, the ground's own scour, deposit and loose cues:** `trench` (a gang digs clay; no rover, so no tint). Shots `trench/`.
- **E, a world that draws the time of day:** `rover-sleep-hint-check` (victoria map, rover, `found-electrics` first, which has a `(sun ...)`), time held at 12, 15.5, 18.5 (sun 5.8 degrees up) and 0. Shots `rover-sleep-hint-check/`. Its rim does not come down the way the opening's does (no boulders), so its `rubbleedge` and `rubbleface` rows are not comparable and are left out below.

**A finding before the table: the Lonely Rover worlds have no day or night look.** `ShowSky` lights the scene from the real sun only when the focused machine has a `(sun ...)` clause or mirrors (`Main.cs:1537`, `MachineRuntime.cs:193`); otherwise the studio light stands 50 degrees up (`Main.cs:1560`). The opening and easy worlds place `found-bank` first, which has no sun, and a machine built there takes the scene's sun, which is none (`Main.Build.cs:87-91`). Noon, 15:30, dusk and 00:00 shots of the opening at the same spot differ in under 1% of pixels (the wind streaks) and the region means agree to within 1 (table, last four columns of column A). So **time of day is not what the owner sees in those worlds**, and #246 (night readability) can only be checked in a world with a sun; the night numbers below come from E. Whether the game should draw its own hours is a separate question for the owner.

#### Effects that darken or lighten ground (verified in the code)

| # | Effect | Where | What it does |
|---|---|---|---|
| 1 | **Slope tint** against the rover's limit | `TerrainView.cs:395-411` (colours `:288`) | none under 13.5 degrees; warm `#E8975A` fully on by 16.5 (mixed 38% into the soil), amber `#F5B82E` from 23.5 to 26.5 (50%), red `#E5362A` from 28.5 to 31.5 (60%); also adds `0.12 x tint` of emission. Fades out between 30 and 60 m from the rover (`:400`), so ground changes colour as the rover moves. |
| 2 | **Hillshade** from the fixed map light | `TerrainView.cs:294,297` (Worked patch `TerrainView.Worked.cs:122`), shader remap `:391` | `0.45 + 0.55 n.L` remapped to 0.68-1.12. By arithmetic, a 20 degree slope facing away is x0.90 and one facing the light x1.10; 30 degrees x0.83 and x1.11. (The comment says the light is 40 degrees up; the vector `(0.55, 0.8, 0.4)` is 50.) |
| 3 | **16% emission** (unlit share) | `TerrainView.cs:414` | the ground never goes below about 16% of its lit value, at night too. |
| 4 | **Loose ground lighter** | `TerrainView.cs:198` (cells: +18% toward white); patch: `TerrainView.Worked.cs:126` + shader `:387` (at least 11% toward cream) | spoil and slumped ground. |
| 5 | **Scoured darker** | `TerrainView.cs:199-202` (cells: up to 45% darker, reached after about 2 cm lowered); patch `TerrainView.Worked.cs:125` + shader `:386-387` (x0.51 at 0.2 m dug) | ground lowered since the start. |
| 6 | **Deposited paler** | same lines (cells: up to 60% toward cream by 3 cm; patch: 45% toward cream at 0.1 m) | ground raised since the start. |
| 7 | **Soil types** | `racket/maps/victoria.rkt:89-97`; colours `materials.json`; drawn at 85% saturation `TerrainView.cs:110-114` | basalt sand floor, bedrock wall, ice-cemented north wall, silica sand bay, plain regolith, `sublimed-regolith` on the weakened section **and in the rubble it sheds** (the soil travels with the material, `Earthworks.cs:60`). Swatch values below. |
| 8 | **Colour smoothing** | `TerrainView.cs:226-235` | two 3x3 blurs: a soil edge is a gradient about two cells (10 m on the crater) wide, not a line. |
| 9 | **Fine contours (0.5 m) and 1 m grid** near the rover | `TerrainView.cs:402-413` | pale lines on dark soil, dark on pale (`dark_soil`, `:408`), within about 30 m (grid 26 m) of the rover. The map's own contour lines (a round interval, about a dozen over the relief, `:378-379`) darken the albedo 16% (every fifth 32%, `:413`). |
| 10 | **Wet ground** | `TerrainView.cs:382-383` | 40% darker and glossy where water is deeper than 0.5 mm. Not measured: there is no standing water in the opening. |
| 11 | **Real sun shadows** | `Main.cs:1483-1488` (four cascades to 250 m, blended, blur 1.5) | only as bright as the sun is: see the no-sun finding above. |
| 12 | **Time of day, night** | `SkyLook.cs:61` (daylight from -6 to +10 degrees), `:115` (ambient 0.45 on Mars; night fill `(0.16, 0.2, 0.3)`), `Main.cs:1574-1579` | only in a world with a sun. |
| 13 | **Ambient occlusion** | `Main.cs:1458-1460` (radius 0.5 m, intensity 2.5) | darkens creases: pit walls, under the rover and crates. Not isolated. |
| 14 | **Haze** | `Main.cs:1472-1477`, `1750-1752`, `SkyLook.cs` (Mars 2.2 camera distances) | far ground fades toward the horizon colour, so a dark floor looks paler far away (B: ice wall 73 near, 129 far, part soil and part haze). |
| 15 | **Filmic tonemap** | `Main.cs:1463` | compresses all of it. |

**Dropped:** the **large-scale value noise of about plus or minus 8%** (art direction section 3, line 83): there is no ground noise in `TerrainView.cs`, `Main.Ground.cs` or `racket/heroic/map.rkt` (the only noise is machine finishes, `Skins.cs`). The darker crater floor and paler rim are the soil table (row 7), not noise. The doc line proposed it for #103, and section 12.3 (what #103 built) never listed it: it was not implemented.

#### What it measured

Soil by itself (C, studio light, from straight above): **basalt sand 56.7, bedrock 102.7, regolith 107.0, sublimed-regolith (the rubble's soil) 136.6, ice-cemented 175.5, silica sand 198.1.** The darkest soil is the one the rover starts on.

Patches (A = the game, B = same view without the rover's tint, contours and grid, noon, same light; E = a world with a sun, in the order noon / 15:30 / dusk / midnight):

| Shot | Patch | Cause | A | B | E noon / 15:30 / dusk / night |
|---|---|---|---|---|---|
| flat | the floor, level | basalt sand, no tint (grid and contours add about 2: 57.9 against 55.6) | 57.9 | 55.6 | 72 / 69 / 37 / 19 |
| flat | the dune ahead, 15-20 degrees | **same sand, warm tint** (B: 54.2, so hillshade changes nothing here) | 101.9 | 54.2 | 120 / 116 / 76 / 49 |
| pitwide | the dune and beyond | warm tint | 119.6 | 52.9 | 140 / 136 / 93 / 62 |
| pit | the pit's wall | dug (scour) >30 degrees: **red tint over the scour** | 111.2 | - | 129 / 126 / 78 / 54 |
| pit | the spoil heap | deposited and loose, steep: **pink-red tint over a pale heap** | 136.6 | - | 157 / 151 / 94 / 65 |
| pit | the floor beside it | basalt sand | 58.2 | - | 72 / 68 / 36 / 19 |
| rubbleedge | apron near the rover (bedrock, 7-20 degrees) | warm tint | 116.1 | 68.5 | 128 / 133 / 74 / 25 (not comparable) |
| rubbleface | low ground beside the rubble | tint | 128.1 | invalid (B has no rubble) | |
| rubbleface | the amber band | 25-30 degrees: amber | 117.5 | | |
| rubbleface | the rubble face | >30 degrees: red | 127.4 | | |
| rubbleface | in a boulder's shadow | **real sun shadow** (studio light) | 104.4 | | |
| rubbleface | beside that shadow, in sun | same ground | 132.4 | | (shadow is 21% darker) |
| rimbase | all the ground at the rim's foot (basalt to bedrock, 14 degrees) | warm tint | 118.4 | 54.4 | 139 / 135 / 88 / 60 |
| rimbase | the bedrock beyond | warm tint | 125.6 | 75.6 | 147 / 143 / 93 / 64 |
| steep | bedrock 24-27 degrees | **amber** | 145.0 | 90.8 | 168 / 165 / 121 / 83 |
| steep | bedrock beyond 30 degrees | **red** (darker than the amber it borders) | 107.8 | 89.1 | 130 / 126 / 88 / 57 |
| ice | the wall's foot, 14 degrees | tint | 129.7 | 73.3 | 149 / 146 / 95 / 64 |
| ice | ground 5-25 m ahead, on the ice-cemented wall (soil edge blurred over about 10 m) | tint over a pale soil (swatch 175.5) | 151.3 | 129.2 | 193 / 189 / 123 / 74 |
| silica | the bay's foot | tint | 129.2 | 68.3 | 148 / 138 / 79 / 65 |
| silica | ground 10-25 m ahead, where the sand turns to silica (edge blurred over about 10 m) | tint over a pale soil (swatch 198.1) | 136.9 | 104.4 | 157 / 148 / 84 / 66 |
| pit (E) | the rover's own shadow, dusk | real sun shadow at 5.8 degrees | | | 29.1 against 37.5 beside it (22% darker); none at noon or midnight |
| trench (D) | untouched clay | soil | 109.2 | | |
| trench (D) | the heap | **deposited and loose** (cells, no tint) | 200.8 | | +84% on the clay |
| trench (D) | the pit's dark edge | **scoured** (or the pit wall in shade) | 86.3 | | -21% on the clay |
| trench (D) | the pit's floor | lit tan under the gang's frame | 111.5 | | the 45% darkening does not show on the floor |

The same camera and the same soil, with and without the tint: the dune ahead is 54 and 102, the rim's foot 54 and 118, the steep bedrock 91 and 145, the silica bay's foot 68 and 129. **The tint roughly doubles the luminance of every sloping patch near the rover and turns it orange.**

**The tint is the brightest ground in the frame at night** (E: 49 against the floor's 19, and a pit's red wall 54), by design (emission, row 1), but a level floor at 19 is nearly black beside it: a difference of 30, below the 50 that `legibility.py` asks of a machine against its ground.

#### Which effects most likely read as "dark instead of light" (ranked)

1. **The slope tint, red above all.** To someone who does not know the rule it is not a warning but the colour of the ground: the dark flat floor (57) meets an orange area (102-120, about twice as bright) at a line that is the 15 degree contour, and the red (108-111) is darker than the amber (145) and the orange next to it, so the part that means "too steep" looks dirtier, not lighter. It fills the whole view near the rover on any slope (rim foot, wall, rubble, a pit's walls), so it is most of what the player sees. It moves with the rover (fade at 30-60 m), so the same dune is dark from far off and orange close up. This is the issue's own #245, now with numbers.
2. **Soil layers.** The floor is the darkest soil on the map (basalt sand 57) beside bedrock and regolith (103-107), the rubble's sublimed regolith (137) and two pale layers (ice-cemented 176, silica sand 198): a 3.5 to 1 step from sand to silica. These are the crater's strata by design (`victoria.rkt:12-20`), and the rubble that buried the cargo is the second-brightest soil on the map and 18% lighter again while loose. Under the tint the soils' range (57 to 198) is squeezed to 116-151 on every sloping patch measured near the rover (rim foot, wall, ice, silica, apron), so the layers are mostly invisible exactly where the rover works, and what remains is the step from the dark level floor.
3. **Shadow, scour and deposit are all the same size of step.** A shadow is 21% to 22% darker than the same ground, a scoured edge 21% darker; a heap is 84% lighter (clay, cells) or reads pink-red (rover's patch). A player cannot tell a shadow from scoured ground from a soil change. In the rover's own patch the scour never shows as dark at all, because the pit's walls are over 30 degrees and the tint covers the scour.

Not candidates in the opening and easy worlds: night, dusk, real shadows' direction, time of day (no sun, see above).

#### The question to ask the owner

"Where the dark and light ground confuses you, is it (a) orange, yellow or red ground that appears near the rover on slopes and changes as you drive, (b) dark grey-brown crater floor beside lighter brown or pale ground that stays the same as you drive, or (c) a darker patch where something was dug or beside a rock or the rover? A screenshot, or the rover's x and z from the rover panel and which way it faces, would settle it."

#### Not measured, or not comparable

- The slide at dusk or night: no world has both the slide the way the opening has it and a sun. In E the rim comes down without boulders and with a different height, so its two slide rows are dropped.
- Wet ground (row 10), the haze and the ambient occlusion on their own, and the 15.5 hour boulder shadows; the rubble's own colour with the loose lightening (the two cannot be separated in a frame).
- B is the same ground and light as A but not the same frame in the rubble; its slide rows are marked invalid.
- Hidden shots drifted nothing here (the camera printed each time), but the images are 1280x800 and the HUD panels cover the left 240 pixels; every region is inside x 300-1250.
- The easy world has the same ground; it was not shot separately.

### 12.17 Soil contrast and key (#243, #244, 2026-10-09)

The owner's answer to the diagnosis (12.16, #241): the confusing ground is the **soil layers**, the dark grey-brown crater floor beside lighter brown or pale ground that stays the same as you drive. Chosen fix: a key and softer contrast. Colours only: **the physics is unchanged** (A/B trace below).

**The key (#243).** The navigation map (M) now draws each soil in the colour the ground itself is drawn in (before, its base was a grey ramp by height) and lists the soils of the loaded map in a panel at its left: a swatch, the name in plain words and a few words of what it is ("Basalt sand: dark grey sand of the crater floor"; only soils that lie under at least one cell, so the 1 m dig-bank map has a key of one). The Driving section has a line under "ahead": `ground here: basalt sand (dark)`, with `, loose` where the soil lies loose (spoil, slumped ground). Where the rover has dug, the line reads the worked patch first, as the height does (`Terrain.SoilAt`, the same cells the terrain is drawn from; no second source). The words are in `SoilLook.WordsFor` (`src/HeroicInventions.Sim/Game/SoilLook.cs`), none of them a field name or an id (the goals panel's leak of that kind is #249). The one colour rule is `SoilLook.Drawn` (85% of the table colour's saturation, same hue and value, as the ground has always done): `TerrainView.SoilColour` calls it for the ground's cell colours, and NavMap calls `TerrainView.SoilColour` for its ground and for each swatch, so a swatch is the ground's colour by construction. The test (`racket/heroic/tests/soil-key-test.rkt`) works the colour out again from `materials.rktd` by its own arithmetic and checks, for all six soils, that the swatch, the colour the ground's own cell texture holds in the middle of a patch of that soil and the map's texel there are that colour to a byte; the Sim test (`SoilLookTests`) pins the six drawn colours by hand arithmetic and the neighbours' separation. The key shows the flat colour in the light; the map and the world shade it by slope (a slope turned away is darker), and the panel says so; its last line says what the paler and darker patches on the ground are (loose or freshly tipped soil is paler, dug-away ground and shadow darker), which closes the wording of #243's own Done-when (the slope colours are in the map's footer line and the Driving section's `ahead` line, #245). `map print` prints each key entry (swatch, ground, map, name, note) and the scripted step `rover ground` prints the Driving line.

**The contrast (#244), the arithmetic first.** The drawn colour is the table colour (`materials.rktd`) at 85% saturation, and what the camera measures is that colour lit and tone-mapped: lighter for ice (it is drawn glossy, roughness 0.3: table 139 drawn, 175 measured), darker for the palest (silica table 226, 198 measured), about the same in the middle (basalt 57.8 drawn, 56.7 measured; sublimed 136.7, 137.1). So the table colours are scaled by target / measured (same hue and saturation), then measured again:

| soil | measured before | target | scale | table colour before -> after | measured after |
|---|---|---|---|---|---|
| basalt sand (the floor) | 56.7 | 67 | 67 / 56.7 = 1.18 | #3D3838 -> #474141 | **67.1** |
| bedrock | 102.7 | unchanged | | | 102.7 |
| regolith | 107.0 | unchanged | | | 107.0 |
| sublimed regolith (the slide's rubble) | 137.1 | unchanged | | | 137.1 |
| ice-cemented regolith | 175.5 | about 155 | 155 / 175.5 = 0.88 (glints a little less than the first guess, so 0.857 was used) | #998582 -> #83726F | **157.7** |
| silica sand (the palest) | 197.8 | about 168 | 168 / 198 = 0.85, then 0.78 on the table colour to allow for the tone-map | #EBE0C7 -> #B6AC93 | **168.2** |

The range from darkest to palest is 3.49 to 1 (56.7 to 197.8) before and 2.51 to 1 (67.1 to 168.2) now. Measured as in 12.16 (top-down at 6 m, noon, studio light, `crater-wind`; `/tmp` scripts `sw.sh`, from `runC.sh`), numbers in 0-255. Why not lift the floor more or calm the pale ones further: **three limits**, each measured.
1. **Neighbours.** Soils that touch on the victoria map (4-neighbours, counted from `game/maps/victoria.map`), the gap in measured luminance, before -> after: bedrock | regolith (333 cell pairs) 4 -> 4; basalt | bedrock (321) 46 -> 36; bedrock | sublimed (144) 34 -> 34; regolith | ice (119) 69 -> 51; basalt | ice (114) 119 -> 91; bedrock | ice (92) 73 -> 55; bedrock | silica (89) 95 -> 66; basalt | silica (29) 141 -> 101. **The task's "neighbours still differ by 50 or more" cannot hold for every pair: before, four of the eight were already under 50** (bedrock and regolith are the same hue and differ only in how grey they are: 4 apart, left as they were; a decision for the owner whether they should be pulled apart). The rule kept: every pair that was 50 or more apart still is (regolith | ice is 50.7, the reason ice stops at 158 and not 150); the one the owner complained about (floor against wall) narrows from 46 to 36, which is what lifting the floor costs. Ice (158) and silica (168) do not touch each other.
2. **The rover.** `legibility.py` on the opening's frame at the floor (rover on basalt, noon): sd 26 -> 23, sep +60 -> +53. A floor at 77 (the first try, #514A4A) gave sep +46, under 50, so the floor stops at 67. On the three slope frames (rim foot, ice, silica) sep reads +25 -> +6, +28 -> +15, +39 -> +25 (the rover is 4 to 13% of those frames, so the metric is crude there; the frames are not among the six references).
3. **The slope tint** (`TerrainView.cs`, mixes `SlopeWarm` 38% into the soil). On a tinted slope, A (rover, tint) against B (no rover) at the same camera, mean of the marked region: luminance, hue (degrees), saturation:

| ground | before, tinted (A) | before, plain (B) | after, tinted | after, plain |
|---|---|---|---|---|
| dune of basalt sand, 15-20 degrees | L102 h25 s0.49 | L54 h13 s0.18 | L106 h24 s0.46 | L64 h12 s0.18 |
| rim foot (basalt to bedrock) | L118 h26 s0.57 | L54 h13 s0.20 | L121 h26 s0.55 | L64 h13 s0.20 |
| ice-cemented wall | L151 h24 s0.38 | L129 h13 s0.18 | L146 h25 s0.42 | L115 h14 s0.20 |
| silica bay | L137 h26 s0.42 | L104 h26 s0.19 | L133 h26 s0.44 | L98 h26 s0.20 |
| bedrock, amber | L145 h41 s0.67 | L91 h22 s0.38 | the same | the same |

The tint still reads as a tint on every soil: saturation more than doubles on the dark floor (0.18 -> 0.46), the pale ice (0.20 -> 0.42) and the silica (0.20 -> 0.44), as before, and on the dark soil it is still 42 brighter. On silica the hue does not move (the sand is already orange-cream), it is the saturation and 35 points of luminance that carry it. The grid and contour lines (`dark_soil` switch at TerrainView.cs:408) read on the lifted floor (shots below).

**Legibility** (`tools/legibility.py`, six references, 1280 x 800, `--hidden`, shot at 600 frames; sd / sep, before -> after): antikythera 42 / -93 -> 42 / -92, roman-crane 47 / -72 -> 47 / -72 (bit-identical), newtons-cradle 44 / -86 -> 44 / -86, water-wheels 36 / -63 -> 35 / -63 (one run in four read 56 / -102: the sky region caught a different frame; three repeats read 36 / -63 and 35 / -63), mars-stirling 29 / +55 -> 29 / +55, herons-fountain 49 / -91 -> 49 / -91. None is worse beyond the run-to-run jitter that earlier sections of this file record (antikythera and water-wheels differ in pixels between runs on identical code). They stand on a studio floor; the soil colours never reach them.

**Physics unchanged (A/B `HEROIC_TRACE`).** `trace.sh`: for each of lonely-rover-opening, rover-dig-bank, rover-hands-check, slide, dig-out and talus, headless at a fixed 120 Hz, `HEROIC_TRACE_DT=0.5`, hints off, the same input script in every world (only the opening and the two rover worlds have a rover to drive; `rover`, `hold left 1.5; hold up 6; hold right 1; hold up 6`, `key b`, `map open; map print`; 60 s of play), at 754c3fa (before) and at this branch (after, and again): **all 19 trace files (every machine's trace and the links trace) are byte-identical, `cmp` clean, and the four `rover` position lines of the opening agree to the printed digit; two runs of the after agree with each other**. (Without `--fixed-fps` a held key counts wall time, so the first attempt differed by up to 0.08 m between two runs of the same code; with it, nothing differs.)

**Frames** (`docs/art/ground/`, noon, `camera` printed at each in the logs): `floor-before.png` and `floor-after.png` (the opening's floor, rover placed at 180 140, yaw -90, pitch 35, distance 14: the same view as 12.16's `flat`; the floor is 57 -> 67, still the darkest ground in the crater, a modest lift), `map-key.png` (the map zoomed out with the key), `driving-ground-line.png` (the Driving section's "ground here: basalt sand (dark)").

**Not done, by choice or limit.** The floor is only 10 points lighter: lifting it further costs the rover's legibility on it (limit 2). Bedrock and regolith are the same hue 4 apart (limit 1). Roughness was left alone (ice still glints). The 'dark_soil' line colours switch at luminance 0.18 to 0.36 of the lit colour; the lifted floor (0.26) is inside that band and its grid reads in both. The slope tint is not touched (#245).

### 12.18 A sun in the rover worlds (#250, 2026-10-09)

Owner, 2026-10-09: "Yes, night clearly dim." The Lonely Rover worlds (opening, easy, e2e) now show the sun and the scene's clock: sunrise, noon, sunset and a night that is clearly dimmer but still readable. Look only; no sim number moved.

**The cause (12.16, verified).** `ShowSky` lit from the real sun only when the focused machine had a `(sun ...)` clause or mirrors (`MachineRuntime.SunShown`); the found bank has neither, and a machine the player builds takes the scene's sun, which is none (`Main.Build.cs`), so the studio light stood 50 degrees up at every hour.

**The fix (`Main.cs`, `ShowSky`).** A world with a scenario draws the focused runtime's own sun even when `SunShown` is false. Every runtime already owns one: with no sun clause it is `Sun(31.2, 172, 12)` on the machine's planet (`MachineRuntime.cs:333`), the one the found bank, the heliostat and the 03:00 call already use, and it advances with the scene clock. So the scene's sky, light and shadow come from the same model as the sim, and nothing is given to any machine: `SunShown` stays false for the found bank, no sim object is touched, and a machine the player builds has the same default sun without any change in `Main.Build.cs`. The three worlds are covered by their `(scenario ...)`; no world file was edited. (The only other scenario world, `rover-sleep-hint-check`, already had a sun on its first machine.)

**Night floor (`SkyLook.cs`).** With the sun below the horizon the ground gets only its 16% emission and the ambient fill. At the old fill (0.45, colour (0.16, 0.2, 0.3)) the opening's flat ground read 20 and the rover was a black shape on black. The night fill is now three times the energy and twice the colour (`AmbientEnergy x (1 + 2 night)`, `(0.32, 0.40, 0.58)`); daytime is unchanged (night = 0), and so are the six reference machines (they have no night).

**Predicted (planet model, Mars: obliquity 25.19, year 669, latitude 31.2, day 172, declination -22.90).**

| Hour | Sun elevation | Azimuth | Predicted scene |
|---|---|---|---|
| 07:00 | 0.1 | 117 (ESE) | sunrise, long shadows to the WNW |
| 12:00 | 35.9 | 180 (south) | full day; shadows fall to the north |
| 16:30 | 5.7 | 239 (WSW) | low, orange, long shadows to the ENE |
| 17:12 (sunset 17:01) | -2.2 | 244 | dusk, light almost gone |
| 18:00 | -11.6 | 250 | night |
| 00:00 | -81.7 | - | night |
| 03:00 | -49.4 | 90 | night |

Ground luminance, the lit part scales with sin(elevation): noon 0.59 of the studio's 0.77 on level ground, so noon about 0.75 to 1.0 of before (the 16% emission and the ambient are unchanged); night about 0.3 to 0.55 of noon.

**Observed** (mean luminance 0-255 of the same rectangle, 1280x800 hidden shots, `opening` at `rover place 180 140 270`, `HEROIC_SET="scene time H; scene clock-rate 0"`; the same camera `pivot=(180.003 -61.433 140.481) distance=14 yaw=-1.571 pitch=0.611`, the horizon shot `distance=30 pitch=0.14`, rim base `pivot=(-0.627 -53.493 271.992) yaw=3.143`, ice `pivot=(0.617 -52.524 -275.989) yaw=-0.002`). Before: 12 / 16.5 / 17.2 / 0 / 3 h; after likewise.

| Region | Before (all hours) | After 12 | 16.5 | 17.2 | 0 | 3 | night / noon |
|---|---|---|---|---|---|---|---|
| flat floor | 57.9 (60.2 at 0 and 3) | 55.9 | 35.0 | 25.0 | 27.4 | 27.4 | 0.49 |
| far ground (flat shot) | 102 | 99.7 | 71.7 | 56.7 | 58.6 | 58.9 | 0.59 |
| horizon shot, ground | 111.8 | 109.3 | 78.8 | 64.2 | 66.3 | 66.5 | 0.61 |
| horizon shot, sky | 105.9 | 106.3 | 92.5 | 52.3 | 49.7 | 49.7 | 0.47 |
| rim base | 118.4 | 114.6 | 82.4 | 69.7 | 71.9 | 71.9 | 0.63 |
| ice wall | 129.7 | 128.1 | 91.5 | 73.8 | 76.0 | 76.1 | 0.59 |

Noon is within 4% of before (the studio light is a flat 50 degrees; the real noon sun is 36), so the day look does not move. The night ratio chosen is about 0.5 on the darkest soil (basalt sand) and 0.6 on paler ground: halved, plainly dimmer in the shots, with the rover and the grid still seen.

**Readability** (`tools/legibility.py`, this framing; sd and sep): noon sd 26, sep +59; 16:30 sd 23, sep +51; 17:12 sd 19, sep +46; midnight sd 22, sep +50; 03:00 sd 21, sep +49. The framing is mostly bare ground, so sd is under 40 at every hour including noon; night is within 10 of noon on sep (the guard against the day) and sits at the 50 threshold, dusk just under it. A brighter night would pass but would not read as night; the owner asked for clearly dim.

**Physics unchanged.** `HEROIC_TRACE` before and after, byte for byte: `lonely-rover-opening` 300 s at 1 s (6 machines and links), `lonely-rover-e2e` 300 s at 1 s (7 machines, links, vault), the e2e world's `HEROIC_SLEEP=pre-dawn` to the wake at 54,800 s (10 s frames, all machines and links), `newtons-cradle` and `mars-stirling` 20 s. All identical. The six reference machines' `legibility.py` lines are unchanged (antikythera sep -87 to -88, the rest identical).

**Not done / open.** The haze, the far wall and the grid keep their own looks at night (the far rim reads brown and brighter than the floor at midnight: it is ground, with its 16% emission, not sky). The e2e route's 55,000 s was not shot. Sleeping skips the drawing, so the sky is shown once on waking.
