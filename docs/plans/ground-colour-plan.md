# Ground colour: one rule for dark and light (plan, 2026-10-13)

Planned (Fable, read-only) from main 7fca779 for #241. It does not diagnose why a patch is dark (art-direction 12.16 did; the owner answered (b): the fixed soil patches). It is the spec and the delivery plan that follows.

## 0. Where it stands

- Soil colour path: `racket/heroic/materials.rktd:43-56` (hex) -> `SoilLook.Drawn` (85% saturation, `src/HeroicInventions.Sim/Game/SoilLook.cs:18-37`) -> `TerrainView.SoilColour` (`game/scripts/TerrainView.cs:90-94`) -> cell texture (`:240-283`) -> shader (`:385-511`). `NavMap.cs:149-185, 400-426` (map and key) reads the same function.
- What darkens or lightens ground today:

| Effect | Where | Size today |
|---|---|---|
| Hillshade from the fixed map light | `TerrainView.cs:342-345, 442` | x0.68 to x1.12 |
| 16% unlit share (emission) | `TerrainView.cs:493` | floor of 0.16 of lit |
| Sun, shadows, night | `Main.cs` ShowSky, `SkyLook.cs:114-117` | shadow -22%; night 0.49-0.63 of noon |
| Loose (spoil, slumped) | `TerrainView.cs:245` | +18% toward white |
| Scoured cells | `TerrainView.cs:249` | up to -45% by 5 cm |
| Deposited cells | `TerrainView.cs:249` | up to +60% toward cream by 5 cm |
| Worked patch dug / heaped | `TerrainView.Worked.cs:125-126`, shader `:465` | up to -55% dug; +45% heaped |
| Wet | `TerrainView.cs:435` | x0.6, glossy |
| Slope tint (warning) | `TerrainView.cs:446-451, 462, 493` | tinted ground 1.27-1.66x as bright as untinted; red (lum 90) darker than amber (187) |
| Contours, grid, patterns | `:452-461, 475-491` | marks, a few percent of area |

Drawn luminance (legibility.py: 0.2126 R + 0.7152 G + 0.0722 B): basalt sand 67, bedrock 99.5, regolith 104, ice-cemented 120 (renders 158, glossy), sublimed 137, silica 174 (renders 168).

The road has no look of its own: it is a worked patch (cut floor plus hauled heaps, `docs/plans/road-plan.md`). No new ground type.

## 1. Colour specification

### 1.1 The one rule (the legend says it in one line)

**Dark means light: ground is darker only where less light falls on it (a slope turned from the map light, a shadow, dusk and night, standing water). What the ground is (its soil) and what has been done to it (dug, heaped, loose) change its hue and marks, inside one band of lightness. Warnings (the slope against the rover's 30 degree limit) change hue, never lightness.**

### 1.2 Lightness bands (mean luminance 0-255)

| Band | Drawn (Sim test) | Measured noon | Measured midnight |
|---|---|---|---|
| Every soil's base colour | 70 to 150 (at most 2.2:1) | 70 to 160 | 35 to 100 |
| Disturbance (loose, dug, heaped, scoured, deposited) | base x0.80 to x1.20 | same | same ratio |
| Hillshade | x0.68 to x1.12 (kept) | | 16% emission floor keeps it above 0.16 of lit |
| Sun shadow | about x0.78 (kept) | | none at night |
| Wet ground | x0.6 -> x0.75, glossy | | |
| Warning: slope tint | x1.0 to x1.2 of untinted, all three bands the same | | same ratio |

The floor stays 67 until the owner agrees to the direct rover-vs-ring gate (section 5.1); with it the floor can rise to about 80.

### 1.3 Base colours per ground type

Slice 1 (owner chose 2026-10-10: keep the softened contrast, separate bedrock from regolith by hue, no other soil moves). Only bedrock changes; its luminance stays inside the pinned band (99 to 100.5, `SoilLookTests.cs:90`).

| Ground | Where | Table hex | Drawn hex (85% sat) | Drawn lum | Hue / sat | Pattern (12.22) |
|---|---|---|---|---|---|---|
| basalt sand | crater floor, apron | `#474141` kept | `#474242` | 67 | 0 / 0.07 | stipple |
| bedrock | wall and rim | **`#6A6356`** (was `#755C4D`) | `#6A6459` | 100.5 | **40 / 0.16 olive-grey** (was 23 / 0.29) | jointed blocks |
| regolith | plain outside the rim | `#8A5A3C` kept | `#8A6148` | 104 | 23 / 0.48 rust | dashes |
| sublimed regolith | weak rim section, rubble | `#BD7347` kept | `#BD7E59` | 137 | 22 / 0.53 | triangles |
| ice-cemented | cold north wall | `#83726F` kept | `#837572` | 120 drawn, 158 measured | 9 / 0.13 | pale blue flecks |
| silica sand | the pale bay | `#B6AC93` kept | `#B6AE98` | 174 drawn, 168 measured | 43 / 0.16 | stipple |
| spoil (dug or heaped) | worked patches, road heaps | the soil's own x0.8-1.2 | | | the soil's | rings and dots |
| road | cut floor of a worked patch | the soil's own; the lip line marks the edges | | | | |
| wet | any | the soil's x0.75, roughness 0.25 | | | | |

Bedrock moves 36 bytes from regolith (was 4 in luminance, same hue) and 54 from basalt sand; its luminance moves 1.

Recommended full table (slice 8, owner-gated): all six inside 80-150 drawn, floor lifted to 80, ice moved to a cold blue-grey.

| Soil | Table hex | Drawn hex | Drawn lum (night ~) | Hue / sat |
|---|---|---|---|---|
| basalt sand | `#544E4E` | `#544F4F` | 80 (40) | 0 / 0.06 |
| bedrock | `#6C6558` | `#6C665B` | 102 (51) | 40 / 0.16 |
| regolith | `#A36043` | `#A36A52` | 116 (58) | 18 / 0.50 |
| sublimed regolith | `#B3713F` | `#B37B51` | 132 (66) | 26 / 0.55 |
| ice-cemented | `#717C84` | `#747D84` | 124 (62), glossy ~140 | 205 / 0.12 |
| silica sand | `#9F947A` | `#9F967F` | 150 (75) | 43 / 0.20 |

Machine-world soils (sand `#DBC794` lum 203, loam 92, clay 124; `materials.rktd:41-42`) are outside the band at the top; Earth maps with a studio floor; optional last slice.

### 1.4 When darker, when lighter

1. **Slope vs the map light** (`TerrainView.MapLight`, `HillShade`, `:342-345`; remap `:442`): x0.68 away to x1.12 facing. Kept. The only effect allowed to darken by more than 20% in daylight.
2. **Sun and time of day** (`SkyLook.cs:114-117`, 12.18): shadows ~x0.78; dusk/night 0.49-0.63 of noon. Kept. Night stays "clearly dim".
3. **Wet** (`:435`): x0.6 -> **x0.75**, glossy (roughness 0.25).
4. **Soil type**: hue, saturation, pattern; luminance 70-150 drawn. Two soils that touch must pass 1.5.
5. **Disturbance** (hue kept, pattern changed, luminance x0.80-1.20):
   - loose: `Lightened(0.18)` at `:245` -> **+12%**; patch tint floor 0.25 of a +20% scale;
   - scoured / dug: `Darkened(up to 0.45)` at `:249` -> **up to 0.20**; patch `lit*(1+0.55*worked)` at shader `:465` -> **0.20**; the lip line (`:472-473`) and rings carry "dug";
   - deposited / heaped: cream mix 0.6 at `:249` -> **0.20**; patch `mix(lit, cream, 0.45*worked)` at `:465` -> **0.20**; clods (`:468-470`) and rings carry "heaped";
   - road: nothing new.
6. **Warnings**: hue and saturation only (1.6).
7. **Lines and marks**: unchanged.

### 1.5 Neighbour contrast

- **Rover (or machine) vs any ground it can stand on:** >= 50 by `legibility.py` sep where valid, and **>= 40 by the direct measure** (rover body mean luminance vs a ring of ground round it, 12.22) at noon, **>= 25 at midnight**. The six reference machines' sd/sep must not get worse.
- **Two grounds that touch, day (noon):** luminance gap >= 25, or colour distance >= 35 bytes with different patterns.
- **Night (midnight):** gap >= 12, or colour distance >= 18 bytes with different patterns (hue and pattern are what survive; #246 is answered that way, not by a brighter night).
- **Disturbance vs its own soil:** within x0.8-1.2 and marked (lip, rings, clods); the mark is the cue.
- **Warning vs the same ground untinted:** luminance ratio 1.0-1.2; saturation at least doubled or hue moved >= 10 degrees.

### 1.6 Warnings: hue, never darkness

Today `lit = mix(lit, band, grip)` (grip up to 0.6) plus `band*grip*0.12` emission: x1.66 on the dark floor; red `#E5362A` (lum 90) darker than amber `#F5B82E` (187), so "too steep" reads dirtier than "steep".

Spec: after the mix, rescale to the untinted luminance times a fixed 1.15 (one shader line after `:462`: `lit_after *= clamp(l0*1.15/l1, 0.5, 2.0)`); emission term keeps the same scale. All three bands sit at one lightness and differ by hue (warm 26, amber 42, red 4 degrees). `SlopeWarm/Amber/Red` (`TerrainView.cs:336`) stay as text/swatch colours for the HUD (`Main.Rover.cs:277-294`) and map (`NavMap.cs:229-231`). Optional (3b): a diagonal hatch on the red band so "cannot climb" survives night and colour-blindness.

### 1.7 The legend

The M key already lists the soils (`NavMap.cs:400-426`). Add, from the same constants: three slope-band swatches through the real tint rule ("steeper than 15 / 25 / 30 degrees (the rover cannot climb red)"); two light rows (one soil at hillshade 0.68 and 1.12, with the one-line rule of 1.1); a "dug / heaped" row (the soil's colour with rings). `map print` prints each row; the Driving "ahead" line gains the words #245 asks for.

## 2. Owner constraints honoured

Legibility over realism, physics real (colour fields carry no physics; one A/B `HEROIC_TRACE` per slice touching `materials.rktd`); show state in the scene; allow, don't forbid; bedrock/regolith hue difference (2026-10-10); 12.19-12.22 patterns stay; keep scope light; never take focus (`tools/gui-check.sh --hidden`).

## 3. Test plan

**A. Sim tests (`tests/HeroicInventions.Sim.Tests/SoilLookTests.cs`):**
- `EverySoilsDrawnLuminanceLiesInTheBand` (silica exempt until slice 8, written in the test); replaces `TheSoilsBrightnessRangeIsNarrowedAndTheOrderIsKept` (`:80-92`).
- `TheCratersSoilsAreDrawnInTheColoursTheKeyShows` (`:65-78`): bedrock `#6A6459` in slice 2; six new hexes in slice 8.
- `SoilsThatMeetOnTheMapStayApart` (`:94-131`): threshold becomes 1.5's; add `BedrockAndRegolithDifferInHueByAtLeastFifteenDegrees`.
- New `GroundCues` (in `SoilLook`): `Loose=0.12, Dug=0.20, Heaped=0.20, Wet=0.25, TintLift=1.15, HillShadeLow=0.68, HillShadeHigh=1.12`; TerrainView and the shader read them as uniforms. Tests `NoDisturbanceLeavesTheBand` and `OnlyLightDarkensBeyondTheBand` make "no darkness for warnings or soils" a CI assertion.
- `TheSlopeTintsAreOneLightnessOnTheGround`: the tint rule in C# on grey lum 100 gives three results within 2 of 115.

**B. Scripted Godot (`racket/heroic/tests/soil-key-test.rkt:77-108`):** `map print` rows include three slope rows and two light rows; slope swatch hexes equal `TerrainView.SlopeWarm/Amber/Red`; light rows are the soil's drawn colour x0.68 and x1.12 to a byte.

**C. Frames (hidden window; numbers recorded in `docs/art-direction.md` 12.23):**
- New `tools/ground-check.sh OUTDIR`: the 12.16 cameras (`rover place 180 140 270` flat; `rimbase`; `ice`; `silica`; `steep`; `rubbleface` (252.5, 137.9); `pit`) in `lonely-rover-opening`, `scene time H` for H = 12, 16.5, 0 with `scene clock-rate 0`, plus the 12.22 rover-ring frames.
- `tools/legibility.py` gains `--region NAME x0 y0 x1 y1` (mean L, hue, sat) and `--pair A B`; `tools/ground-regions.txt` holds rectangles per camera.
- Per slice: every soil in band at noon and midnight; touching pairs over 1.5's thresholds; tinted/untinted 1.0-1.2 at flat, rimbase, steep (red no darker than amber); rover body vs ring >= 40 noon, >= 25 midnight; six references' `legibility.py` lines unchanged.

**D. Existing suites:** `dotnet test tests/HeroicInventions.Sim.Tests`; `soil-key-test`, `markers-test`, `rover-dig-test` (redraw timing flakes under load); `racket racket/build.rkt` no drift; the director's solve must still win.

## 4. Milestone and issues

Milestone **16 Ground you can read**. #241 stays the umbrella. In order, each lands alone with the full suite:

1. **Ground rule written down and pinned** (constants, bands, tests, tools). Write 1.1-1.6 into art-direction 12.23; move every darkening/lightening constant (`TerrainView.cs:245, 249, 435, 442, 465`, `Worked.cs:125-126`) into `SoilLook`/`GroundCues` read as uniforms, at today's values; add tests A (Skip where they cannot yet pass, naming the slice); extend `legibility.py`; add `ground-check.sh` and `ground-regions.txt`; record the noon/16:30/midnight baseline. Nothing on screen changes. sonnet / medium.
2. **Bedrock told from regolith by hue** (`#6A6356`; frames at the contact (305, 360) and wall foot (295, -2.5), day and night; A/B trace). bedrock|regolith colour >= 35 bytes and hue >= 15 degrees; six references unchanged; traces identical. Depends on 1. sonnet / low.
3. **Warnings by hue, not lightness** (the luminance-preserving tint; same rule in `NavMap.cs:508` onward). Ratio 1.0-1.2 on all three; red at `steep` within 10 of amber; saturation on the floor still doubles. Depends on 1. sonnet / medium. 3b optional hatch (owner call).
4. **Legend complete: light and slope rows in the key** (closes #243's untested Done-when). Depends on 3. sonnet / low.
5. **#245 kept: the "ahead" line says what the tints mean, one-time hint.** Depends on 4. sonnet / low.
6. **Disturbance inside the band** (cue constants to 1.4's values). Pit and heap still read at 14 m by lip and rings. Depends on 1. sonnet / low.
7. **#246 closed by measurement: night readability table.** Every touching pair passes at midnight or is listed; rover vs ring >= 25 at midnight. Depends on 2, 3, 6. sonnet / low.
8. **Soils in one band of lightness** (owner-gated; recommended table 1.3; floor to 80; ice blue-grey). sonnet / medium.
9. **Optional: machine-world soils into the band.** sonnet / low.

#242 closed (diagnosis stands). #243 closed; its test is issue 4. #244 closed; its caveat is issue 2. #245 split into 3 (look) and 5 (words). #246 becomes issue 7.

## 5. Decisions for the owner

1. **Gate on ground frames.** `legibility.py` sep is built for a machine on a studio floor; on bare ground it reads sd < 40 at every hour (12.18). Proposal: for ground frames the direct rover-vs-ring measure is the gate (>= 40 noon, >= 25 night); the six references keep `legibility.py`. If no, issue 8 cannot lift the floor.
2. **Issue 8 at all** (six soils re-tabled, ice to blue-grey) vs "keep the softened contrast".
3. **3b**, a hatch on the red band.
4. Whether wet may stay x0.6 or go to x0.75.
