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
