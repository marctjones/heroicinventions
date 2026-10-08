# Machine-by-machine review, 2026-10-07

Every machine in `game/machines/*.machine` (100), looked at in the real window and traced headless, on main at f50d225 after the day's look changes. Read-only on game and engine code; this file and `docs/art/review/` are the only additions. Issue with the fix list: see the end.

**Counts: GOOD 41, FIX 58, BROKEN 1 (of 100). None skipped; every machine ran.**

## How it was done

1. Shot: `tools/gui-check.sh HEROIC_AUTOSELECT=<name> -- "wait 300; shot <png>; quit"`, background window, 1280x800, 300 frames in (about 5 s of sim at the machine's own default speed). A second shot at 40 frames was taken for the machines whose action leaves the frame (ball-ramp, carts, falling-stones, tunnel-test, trebuchet, torsion-catapult, vitruvian-catapulta, post-and-lintel-crane, buried-crate, drop-test, mars-stirling). Whole shot pass: 669 s (11 min), 6.7 s a machine.
2. `python3 tools/legibility.py` on each shot: sd (luminance spread, want >= 40) and sep (machine against background, want |sep| >= 50). The scene region is x300-980, which leaves out both panels, so a machine hidden under a panel can still score well; the table's verdict comes from looking at the shot, not from the numbers. Thin or small machines score low on sd and can still read clearly.
3. Each shot was looked at on contact sheets and, where needed, full size. Verdict GOOD = machine whole, readable, labels legible; FIX = a concrete visual or text problem; BROKEN = the machine does not do its job or cannot be seen at all.
4. Function: every machine was run headless (`HEROIC_AUTORUN=1 HEROIC_DEBUG_PHYSICS=1 HEROIC_QUIT_AFTER_SIM_SECONDS=<s>`, `HEROIC_SPEED=20`, or 5 for runs under 10 s; the whole pass took 683 s) and the final HUD line compared with `docs/machine-audit-2026-10-07.md` and the header. Details under *Function*.

Shot files are in the session scratchpad (`.../scratchpad/shots/<name>.png`, 1280x800, about 180 kB each, not committed to keep the repo small). The committed contact sheets in `docs/art/review/` hold the same shots at 400 px per tile, 8 per sheet in alphabetical order; the table gives sheet and tile.

## Table

| # | machine | sd | sep | verdict | problems | sheet |
|---|---|---|---|---|---|---|
| 1 | `aeolipile` | 34 | -70 | FIX | kettle's base and the "ball" label are cut off at the bottom and top of the frame; the sphere nearly touches the top edge | sheet-01.jpg tile 1 |
| 2 | `airlock` | 36 | +65 | FIX | chamber and outer door sit under the info panel; panel title "An Airlock on Mars (Doors and Air Pump)" runs off the right screen edge; labels wrap into 3 lines | sheet-01.jpg tile 2 |
| 3 | `antikythera-lunar-train` | 70 | -124 | GOOD | clear gears on dark plate, teeth labels readable (strong sep); slightly right of centre | sheet-01.jpg tile 3 |
| 4 | `archimedes-screw` | 40 | -65 | FIX | trough and the far tank sit under the info panel; "spring/cochlea/trough" labels overprint each other | sheet-01.jpg tile 4 |
| 5 | `axle-friction` | 32 | -52 | FIX | machine is small and spread wide; water wheel runs under the panel; "spring" and "pin worn" labels overlap | sheet-01.jpg tile 5 |
| 6 | `baghdad-battery` | 28 | -97 | FIX | jars are tiny in a big empty frame; label of the single "drops" jar overprints the jar | sheet-01.jpg tile 6 |
| 7 | `ball-ramp` | 17 | -38 | FIX | the balls have already rolled off the foot and are out of frame, only the ramp is left; ramp is nearly the floor's colour (sd 17) | sheet-01.jpg tile 7 |
| 8 | `bar-crane` | 41 | -64 | GOOD | two wheels and stones all visible; the two top labels crowd slightly | sheet-01.jpg tile 8 |
| 9 | `battering-rams` | 34 | -60 | FIX | rightmost ram and its post sit under the info panel; labels collide with the frame and with each other | sheet-02.jpg tile 1 |
| 10 | `bearing-friction` | 40 | -72 | FIX | far too close: third pendulum is under the info panel and the "dry" label is hidden behind it; top labels overprint the frame | sheet-02.jpg tile 2 |
| 11 | `bellows-forge` | 41 | +70 | FIX | too close: the left forge is cut off at the left edge; "bare-crucible"/"blown" labels overprint bubbles | sheet-02.jpg tile 3 |
| 12 | `belt-drive` | 42 | -63 | GOOD | pulleys, belts and labels readable; small but centred | sheet-02.jpg tile 4 |
| 13 | `boiler-safety` | 32 | +43 | FIX | second (unguarded) boiler and its labels are under the info panel; "unguarded rated to 200 kPa" label cut | sheet-02.jpg tile 5 |
| 14 | `boiler-shells` | 25 | -98 | FIX | bronze pot is under the info panel; "rated to ..." labels of the left and right pots overprint each other and the panel | sheet-02.jpg tile 6 |
| 15 | `boulder` | 22 | -87 | GOOD | boulder clear on the floor; no scale figure but fine | sheet-02.jpg tile 7 |
| 16 | `branca-steam-wheel` | 39 | +60 | GOOD | pot, plinth, wheel and steam all clear | sheet-02.jpg tile 8 |
| 17 | `buried-crate` | 39 | -63 | BROKEN | alone it has no ground: the crate falls forever (49.8 m/s at the shot; frame shows a row of figures and a wall of cylinders, no crate); known from the audit, still open | sheet-03.jpg tile 1 |
| 18 | `capstans` | 41 | -70 | FIX | third post and the "two-turns" label run under the info panel | sheet-03.jpg tile 2 |
| 19 | `cargo-crate` | 15 | +46 | GOOD | oak crate clear against the Mars background | sheet-03.jpg tile 3 |
| 20 | `carts` | 24 | -48 | FIX | the two carts have rolled out of frame, only the sledge and ramp are left; ramp matches the floor colour (sd 24) | sheet-03.jpg tile 4 |
| 21 | `castellum-aquae` | 40 | +64 | FIX | thin machine spanning the whole width, both ends under the panels; "spring" label overprinted by the HUD; dark ground makes the pale piers faint | sheet-03.jpg tile 5 |
| 22 | `cistern-and-trough` | 33 | +59 | GOOD | cistern, pipe, trough, person all clear | sheet-03.jpg tile 6 |
| 23 | `cistern-drain` | 28 | -117 | GOOD | cistern, grate and labels clear (outlines faint but readable) | sheet-03.jpg tile 7 |
| 24 | `cistern` | 41 | +54 | GOOD | cistern, person for scale, level clear | sheet-03.jpg tile 8 |
| 25 | `component-gallery` | 25 | -93 | FIX | five benches in a row: tiny and both ends under the HUD and the info panel | sheet-04.jpg tile 1 |
| 26 | `constant-head` | 45 | +37 | FIX | bottom of the two receivers is cut off, right cistern against the panel; "aqueduct: a spring..." and tap labels pile up | sheet-04.jpg tile 2 |
| 27 | `crane-hoist` | 33 | -108 | GOOD | frame, drum, pulley, stone and person all visible | sheet-04.jpg tile 3 |
| 28 | `crank-slider` | 42 | -76 | GOOD | crank disc, piston and person visible; the person dwarfs the machine | sheet-04.jpg tile 4 |
| 29 | `crate-tongs` | 30 | -51 | FIX | tongs are tiny and almost invisible next to the crates; labels crowd; person much larger than the machine | sheet-04.jpg tile 5 |
| 30 | `crate` | 26 | -83 | GOOD | oak crate clear | sheet-04.jpg tile 6 |
| 31 | `dam-break` | 31 | +49 | FIX | only a strip of the race is in frame, dam and ponds are out of it; dark floor (known, #148) | sheet-04.jpg tile 7 |
| 32 | `drop-test` | 21 | -47 | FIX | blocks are tiny in a mostly empty frame; strike labels overprint each other | sheet-04.jpg tile 8 |
| 33 | `dry-mill` | 43 | -105 | GOOD | water wheel and person clear | sheet-05.jpg tile 1 |
| 34 | `earth-machines-on-mars` | 48 | +72 | FIX | windmill rotor is cut off at the top and runs under the info panel; panel title and text run off the right screen edge | sheet-05.jpg tile 2 |
| 35 | `fall-and-swing` | 28 | -56 | GOOD | small but all parts and the person are in frame | sheet-05.jpg tile 3 |
| 36 | `falling-stones` | 17 | -98 | FIX | only the person for scale is in frame: the three balls (350 m up and falling at 49 m/s) are out of sight | sheet-05.jpg tile 4 |
| 37 | `field-windmill` | 47 | +72 | GOOD | mill clear against the Mars background | sheet-05.jpg tile 5 |
| 38 | `fire-and-water` | 36 | +55 | FIX | left cistern under the HUD, right stove under the info panel; overprinted "spring" label; pale piers on dark ground | sheet-05.jpg tile 6 |
| 39 | `floats` | 38 | -97 | FIX | big tank runs under the info panel, "Oak"/"5.76 kg" labels hidden by it | sheet-05.jpg tile 7 |
| 40 | `free-sails` | 31 | -112 | GOOD | mill and figure clear | sheet-05.jpg tile 8 |
| 41 | `geared-brake` | 56 | -140 | GOOD | two geared flywheel rigs clear; the left labels crowd a little | sheet-06.jpg tile 1 |
| 42 | `glass-rooms` | 21 | +43 | FIX | fourth room ("thin") is under the info panel and its label is hidden; "membrane" label under the HUD; panel title cut at the screen edge; low contrast (sd 21) | sheet-06.jpg tile 2 |
| 43 | `greenhouse` | 30 | +57 | FIX | panel title and description run off the right screen edge; machine itself readable | sheet-06.jpg tile 3 |
| 44 | `gristmill` | 36 | -101 | GOOD | three millstone beds clear; "sharp"/"sharp-bed" labels touch | sheet-06.jpg tile 4 |
| 45 | `hama-noria` | 42 | -87 | FIX | spring tank and weir at the right are under the info panel, tail race cut at left; labels pile up there | sheet-06.jpg tile 5 |
| 46 | `hanging-chain` | 35 | -58 | GOOD | chain, posts, labels readable; "right-hook" label touches the info panel | sheet-06.jpg tile 6 |
| 47 | `heading-rig` | 24 | -90 | FIX | parts are small, the side-slope is under the info panel, the figure under the HUD edge | sheet-06.jpg tile 7 |
| 48 | `hearth-engine` | 41 | +70 | GOOD | kettle, sphere and plinth clear | sheet-06.jpg tile 8 |
| 49 | `heliostats` | 21 | -57 | GOOD | mirrors, beams and posts clear | sheet-07.jpg tile 1 |
| 50 | `heron-temple-doors` | 38 | -89 | GOOD | altar, doors, bucket, person clear | sheet-07.jpg tile 2 |
| 51 | `herons-fountain` | 30 | -78 | GOOD | basin, jet, supply, receiver clear | sheet-07.jpg tile 3 |
| 52 | `hierapolis-sawmill` | 58 | -97 | FIX | header tank and spring are under the info panel, labels overprinted by it; heavy blue shadow pattern on the ground | sheet-07.jpg tile 4 |
| 53 | `hillside-pond` | 32 | -94 | GOOD | pond, gate, race clear | sheet-07.jpg tile 5 |
| 54 | `holy-water` | 35 | -67 | GOOD | lever, urn, basin clear | sheet-07.jpg tile 6 |
| 55 | `inclined-plane-demo` | 19 | -44 | GOOD | slope and three blocks with labels clear (small, sd 19) | sheet-07.jpg tile 7 |
| 56 | `kitchen-smoke-jack` | 37 | -53 | FIX | the cauldron and hearth are cut off at the bottom of the frame, the chimney leaves the top | sheet-07.jpg tile 8 |
| 57 | `kongming-lantern-mars` | 47 | +90 | GOOD | lantern and its figures clear on Mars colours | sheet-08.jpg tile 1 |
| 58 | `kongming-lantern` | 42 | +81 | FIX | both lanterns are tiny, the left label overprints the figure | sheet-08.jpg tile 2 |
| 59 | `lever-demo` | 37 | -75 | FIX | granite end touches the HUD edge, cedar end touches the info panel | sheet-08.jpg tile 3 |
| 60 | `mars-sols` | 36 | +68 | FIX | panel title runs off the right edge ("...Meridiani (Mar"); scene is clear | sheet-08.jpg tile 4 |
| 61 | `mars-stirling` | 29 | +58 | FIX | left and right engine groups run under the HUD and the info panel; only the middle group is whole (reference machine) | sheet-08.jpg tile 5 |
| 62 | `material-samples` | 34 | -84 | GOOD | four cubes clear | sheet-08.jpg tile 6 |
| 63 | `newcomen-engine` | 40 | -66 | GOOD | beam, boiler, cistern, person clear | sheet-08.jpg tile 7 |
| 64 | `newcomen-hearth` | 40 | -66 | GOOD | as newcomen-engine, fire visible | sheet-08.jpg tile 8 |
| 65 | `newtons-cradle` | 46 | +56 | FIX | too close: frame top and bottom cut off, fifth ball against the info panel (reference machine) | sheet-09.jpg tile 1 |
| 66 | `pendulum-demo` | 39 | -94 | FIX | feet of the frame cut off at the bottom, narrow strip of scene | sheet-09.jpg tile 2 |
| 67 | `placer-sluice` | 36 | -82 | FIX | close-up of the middle of the sluice; the ends are out of frame and the label overprints the HUD | sheet-09.jpg tile 3 |
| 68 | `post-and-lintel-crane` | 16 | -36 | FIX | after ~5 s the machine is invisible: the camera has pulled out and the frame holds only a floating "Granite 21.6 kg" label over bare floor (sd 16, sep -36); at 40 frames the crane is framed fine; trace shows it works (load 1.52 m up) | sheet-09.jpg tile 4 |
| 69 | `rail-wagons` | 36 | -59 | GOOD | wagons and rails clear; rails run under the info panel at the right | sheet-09.jpg tile 5 |
| 70 | `rain-house` | 39 | +73 | FIX | panel title and text run off the right screen edge; house glass is faint against the Mars sky | sheet-09.jpg tile 6 |
| 71 | `ratchet-windlass` | 33 | -54 | FIX | machine is tiny; hold/free/wind-drum labels and energy labels stack on top of each other | sheet-09.jpg tile 7 |
| 72 | `roman-crane` | 45 | -69 | GOOD | wheel, rope, pulley, stone and figure clear (reference machine) | sheet-09.jpg tile 8 |
| 73 | `rope-over-bars` | 49 | -82 | FIX | fourth bar is under the info panel; label text overprints the bars and runs off the panel edge | sheet-10.jpg tile 1 |
| 74 | `sand-timer` | 31 | -56 | GOOD | timers and the figure clear (timers are tiny next to the figure) | sheet-10.jpg tile 2 |
| 75 | `shaduf` | 42 | -83 | GOOD | poles, buckets and figure clear | sheet-10.jpg tile 3 |
| 76 | `sluice-demo` | 41 | +65 | FIX | whole run is wider than the frame: spring under the HUD, end under the info panel; "spring" label overprinted | sheet-10.jpg tile 4 |
| 77 | `solar-furnace` | 27 | +33 | FIX | left heliostat bank under the HUD, right dish under the info panel; low separation (sep +33) | sheet-10.jpg tile 5 |
| 78 | `solar-steam-wheel` | 24 | -58 | FIX | the pot and wheel are small and hidden behind the mirror in front; labels overlap | sheet-10.jpg tile 6 |
| 79 | `spill-tank` | 38 | -91 | FIX | panel title runs off the right screen edge; butt itself clear | sheet-10.jpg tile 7 |
| 80 | `steam-engines-mars` | 43 | +77 | FIX | open-engine group is under the left HUD edge and the pressurised hut under the info panel; open-/hut- labels overlap | sheet-10.jpg tile 8 |
| 81 | `stove-rooms` | 21 | -94 | FIX | third room (stove3) is under the info panel; title cut at the screen edge | sheet-11.jpg tile 1 |
| 82 | `suction-limit` | 49 | -74 | GOOD | three pumps clear, labels busy but readable | sheet-11.jpg tile 2 |
| 83 | `tank-leaks` | 29 | -51 | FIX | fourth barrel ("seep") is under the info panel, machine small | sheet-11.jpg tile 3 |
| 84 | `torsion-catapult` | 17 | -45 | FIX | only the flight trail is in frame; the onager is out of sight at the left and the trail runs under the info panel (known, #148) | sheet-11.jpg tile 4 |
| 85 | `trebuchet` | 16 | -52 | FIX | the camera follows the stone: only the trail and the landing mark are in frame, the trebuchet is not (#148) | sheet-11.jpg tile 5 |
| 86 | `trench-crew` | 36 | -96 | GOOD | gang and figure clear | sheet-11.jpg tile 6 |
| 87 | `trip-hammer` | 33 | -60 | FIX | machine is tiny beside the person, wheel and hammer labels overprint | sheet-11.jpg tile 7 |
| 88 | `trip-sluice` | 42 | +60 | FIX | right end under the info panel, pool near the HUD; dark ground gradient | sheet-11.jpg tile 8 |
| 89 | `trough` | 23 | +21 | GOOD | empty trough clear (glass on dark ground, sep +21 but readable) | sheet-12.jpg tile 1 |
| 90 | `tunnel-test` | 24 | -47 | FIX | only the two planks are in frame; the bolts have bounced out of sight | sheet-12.jpg tile 2 |
| 91 | `two-modules` | 40 | +70 | FIX | the left module's three labels overprint each other | sheet-12.jpg tile 3 |
| 92 | `universal-joint` | 35 | -105 | GOOD | two shafts and the joint clear | sheet-12.jpg tile 4 |
| 93 | `vitruvian-catapulta` | 16 | -39 | FIX | only the bolt's trail is in frame, catapulta not visible (known, #148) | sheet-12.jpg tile 5 |
| 94 | `wake-clock` | 42 | -75 | FIX | spring tank at the right is under the info panel and its labels cross the panel | sheet-12.jpg tile 6 |
| 95 | `walkers-wheel` | 45 | -95 | GOOD | wheel clear, person for scale | sheet-12.jpg tile 7 |
| 96 | `water-clock` | 42 | -68 | GOOD | spring, clock, receiver and person clear | sheet-12.jpg tile 8 |
| 97 | `water-mill-race` | 31 | -60 | FIX | spring under the HUD, mill end under the info panel; "spring" label overprinted | sheet-13.jpg tile 1 |
| 98 | `water-wheels` | 43 | +62 | FIX | right-hand machines under the info panel, left under the HUD; dark floor | sheet-13.jpg tile 2 |
| 99 | `windmills` | 40 | -63 | FIX | right mill (gale) is cut by the info panel | sheet-13.jpg tile 3 |
| 100 | `winter-night` | 38 | -112 | GOOD | basin, hob and cistern clear | sheet-13.jpg tile 4 |

## BROKEN

- **buried-crate**: alone it has no ground: the crate falls forever (49.8 m/s at the shot; frame shows a row of figures and a wall of cylinders, no crate); known from the audit, still open

## FIX, grouped by kind of problem

A machine appears under every kind it has. Most FIX machines are framing: the camera centres the machine on the whole 1280x800 window and does not allow for the 224 px left HUD and the 290 px right info panel, so anything wider than about 60% of the frame runs under a panel.

### Framing (cut off, too small, under a panel, action out of frame): 54

- `aeolipile`: kettle's base and the "ball" label are cut off at the bottom and top of the frame; the sphere nearly touches the top edge
- `airlock`: chamber and outer door sit under the info panel; panel title "An Airlock on Mars (Doors and Air Pump)" runs off the right screen edge; labels wrap into 3 lines
- `archimedes-screw`: trough and the far tank sit under the info panel; "spring/cochlea/trough" labels overprint each other
- `axle-friction`: machine is small and spread wide; water wheel runs under the panel; "spring" and "pin worn" labels overlap
- `baghdad-battery`: jars are tiny in a big empty frame; label of the single "drops" jar overprints the jar
- `ball-ramp`: the balls have already rolled off the foot and are out of frame, only the ramp is left; ramp is nearly the floor's colour (sd 17)
- `battering-rams`: rightmost ram and its post sit under the info panel; labels collide with the frame and with each other
- `bearing-friction`: far too close: third pendulum is under the info panel and the "dry" label is hidden behind it; top labels overprint the frame
- `bellows-forge`: too close: the left forge is cut off at the left edge; "bare-crucible"/"blown" labels overprint bubbles
- `boiler-safety`: second (unguarded) boiler and its labels are under the info panel; "unguarded rated to 200 kPa" label cut
- `boiler-shells`: bronze pot is under the info panel; "rated to ..." labels of the left and right pots overprint each other and the panel
- `capstans`: third post and the "two-turns" label run under the info panel
- `carts`: the two carts have rolled out of frame, only the sledge and ramp are left; ramp matches the floor colour (sd 24)
- `castellum-aquae`: thin machine spanning the whole width, both ends under the panels; "spring" label overprinted by the HUD; dark ground makes the pale piers faint
- `component-gallery`: five benches in a row: tiny and both ends under the HUD and the info panel
- `constant-head`: bottom of the two receivers is cut off, right cistern against the panel; "aqueduct: a spring..." and tap labels pile up
- `crate-tongs`: tongs are tiny and almost invisible next to the crates; labels crowd; person much larger than the machine
- `dam-break`: only a strip of the race is in frame, dam and ponds are out of it; dark floor (known, #148)
- `drop-test`: blocks are tiny in a mostly empty frame; strike labels overprint each other
- `earth-machines-on-mars`: windmill rotor is cut off at the top and runs under the info panel; panel title and text run off the right screen edge
- `falling-stones`: only the person for scale is in frame: the three balls (350 m up and falling at 49 m/s) are out of sight
- `fire-and-water`: left cistern under the HUD, right stove under the info panel; overprinted "spring" label; pale piers on dark ground
- `floats`: big tank runs under the info panel, "Oak"/"5.76 kg" labels hidden by it
- `glass-rooms`: fourth room ("thin") is under the info panel and its label is hidden; "membrane" label under the HUD; panel title cut at the screen edge; low contrast (sd 21)
- `hama-noria`: spring tank and weir at the right are under the info panel, tail race cut at left; labels pile up there
- `heading-rig`: parts are small, the side-slope is under the info panel, the figure under the HUD edge
- `hierapolis-sawmill`: header tank and spring are under the info panel, labels overprinted by it; heavy blue shadow pattern on the ground
- `kitchen-smoke-jack`: the cauldron and hearth are cut off at the bottom of the frame, the chimney leaves the top
- `kongming-lantern`: both lanterns are tiny, the left label overprints the figure
- `lever-demo`: granite end touches the HUD edge, cedar end touches the info panel
- `mars-stirling`: left and right engine groups run under the HUD and the info panel; only the middle group is whole (reference machine)
- `newtons-cradle`: too close: frame top and bottom cut off, fifth ball against the info panel (reference machine)
- `pendulum-demo`: feet of the frame cut off at the bottom, narrow strip of scene
- `placer-sluice`: close-up of the middle of the sluice; the ends are out of frame and the label overprints the HUD
- `post-and-lintel-crane`: after ~5 s the machine is invisible: the camera has pulled out and the frame holds only a floating "Granite 21.6 kg" label over bare floor (sd 16, sep -36); at 40 frames the crane is framed fine; trace shows it works (load 1.52 m up)
- `rain-house`: panel title and text run off the right screen edge; house glass is faint against the Mars sky
- `ratchet-windlass`: machine is tiny; hold/free/wind-drum labels and energy labels stack on top of each other
- `rope-over-bars`: fourth bar is under the info panel; label text overprints the bars and runs off the panel edge
- `sluice-demo`: whole run is wider than the frame: spring under the HUD, end under the info panel; "spring" label overprinted
- `solar-furnace`: left heliostat bank under the HUD, right dish under the info panel; low separation (sep +33)
- `solar-steam-wheel`: the pot and wheel are small and hidden behind the mirror in front; labels overlap
- `steam-engines-mars`: open-engine group is under the left HUD edge and the pressurised hut under the info panel; open-/hut- labels overlap
- `stove-rooms`: third room (stove3) is under the info panel; title cut at the screen edge
- `tank-leaks`: fourth barrel ("seep") is under the info panel, machine small
- `torsion-catapult`: only the flight trail is in frame; the onager is out of sight at the left and the trail runs under the info panel (known, #148)
- `trebuchet`: the camera follows the stone: only the trail and the landing mark are in frame, the trebuchet is not (#148)
- `trip-hammer`: machine is tiny beside the person, wheel and hammer labels overprint
- `trip-sluice`: right end under the info panel, pool near the HUD; dark ground gradient
- `tunnel-test`: only the two planks are in frame; the bolts have bounced out of sight
- `vitruvian-catapulta`: only the bolt's trail is in frame, catapulta not visible (known, #148)
- `wake-clock`: spring tank at the right is under the info panel and its labels cross the panel
- `water-mill-race`: spring under the HUD, mill end under the info panel; "spring" label overprinted
- `water-wheels`: right-hand machines under the info panel, left under the HUD; dark floor
- `windmills`: right mill (gale) is cut by the info panel

### Colour and contrast (against the floor or sky): 6

- `ball-ramp`: the balls have already rolled off the foot and are out of frame, only the ramp is left; ramp is nearly the floor's colour (sd 17)
- `castellum-aquae`: thin machine spanning the whole width, both ends under the panels; "spring" label overprinted by the HUD; dark ground makes the pale piers faint
- `fire-and-water`: left cistern under the HUD, right stove under the info panel; overprinted "spring" label; pale piers on dark ground
- `solar-furnace`: left heliostat bank under the HUD, right dish under the info panel; low separation (sep +33)
- `trip-sluice`: right end under the info panel, pool near the HUD; dark ground gradient
- `water-wheels`: right-hand machines under the info panel, left under the HUD; dark floor

### Labels (overlapping each other, the machine, or a panel): 28

- `archimedes-screw`: trough and the far tank sit under the info panel; "spring/cochlea/trough" labels overprint each other
- `axle-friction`: machine is small and spread wide; water wheel runs under the panel; "spring" and "pin worn" labels overlap
- `baghdad-battery`: jars are tiny in a big empty frame; label of the single "drops" jar overprints the jar
- `battering-rams`: rightmost ram and its post sit under the info panel; labels collide with the frame and with each other
- `bearing-friction`: far too close: third pendulum is under the info panel and the "dry" label is hidden behind it; top labels overprint the frame
- `bellows-forge`: too close: the left forge is cut off at the left edge; "bare-crucible"/"blown" labels overprint bubbles
- `boiler-safety`: second (unguarded) boiler and its labels are under the info panel; "unguarded rated to 200 kPa" label cut
- `boiler-shells`: bronze pot is under the info panel; "rated to ..." labels of the left and right pots overprint each other and the panel
- `capstans`: third post and the "two-turns" label run under the info panel
- `castellum-aquae`: thin machine spanning the whole width, both ends under the panels; "spring" label overprinted by the HUD; dark ground makes the pale piers faint
- `constant-head`: bottom of the two receivers is cut off, right cistern against the panel; "aqueduct: a spring..." and tap labels pile up
- `drop-test`: blocks are tiny in a mostly empty frame; strike labels overprint each other
- `fire-and-water`: left cistern under the HUD, right stove under the info panel; overprinted "spring" label; pale piers on dark ground
- `floats`: big tank runs under the info panel, "Oak"/"5.76 kg" labels hidden by it
- `glass-rooms`: fourth room ("thin") is under the info panel and its label is hidden; "membrane" label under the HUD; panel title cut at the screen edge; low contrast (sd 21)
- `hama-noria`: spring tank and weir at the right are under the info panel, tail race cut at left; labels pile up there
- `hierapolis-sawmill`: header tank and spring are under the info panel, labels overprinted by it; heavy blue shadow pattern on the ground
- `kongming-lantern`: both lanterns are tiny, the left label overprints the figure
- `placer-sluice`: close-up of the middle of the sluice; the ends are out of frame and the label overprints the HUD
- `ratchet-windlass`: machine is tiny; hold/free/wind-drum labels and energy labels stack on top of each other
- `rope-over-bars`: fourth bar is under the info panel; label text overprints the bars and runs off the panel edge
- `sluice-demo`: whole run is wider than the frame: spring under the HUD, end under the info panel; "spring" label overprinted
- `solar-steam-wheel`: the pot and wheel are small and hidden behind the mirror in front; labels overlap
- `steam-engines-mars`: open-engine group is under the left HUD edge and the pressurised hut under the info panel; open-/hut- labels overlap
- `trip-hammer`: machine is tiny beside the person, wheel and hammer labels overprint
- `two-modules`: the left module's three labels overprint each other
- `water-mill-race`: spring under the HUD, mill end under the info panel; "spring" label overprinted
- `water-wheels`: right-hand machines under the info panel, left under the HUD; dark floor

### Physics: 1

- `buried-crate`: alone it has no ground: the crate falls forever (49.8 m/s at the shot; frame shows a row of figures and a wall of cylinders, no crate); known from the audit, still open

### Text (panel title or description clipped): 8

- `airlock`: chamber and outer door sit under the info panel; panel title "An Airlock on Mars (Doors and Air Pump)" runs off the right screen edge; labels wrap into 3 lines
- `earth-machines-on-mars`: windmill rotor is cut off at the top and runs under the info panel; panel title and text run off the right screen edge
- `glass-rooms`: fourth room ("thin") is under the info panel and its label is hidden; "membrane" label under the HUD; panel title cut at the screen edge; low contrast (sd 21)
- `greenhouse`: panel title and description run off the right screen edge; machine itself readable
- `mars-sols`: panel title runs off the right edge ("...Meridiani (Mar"); scene is clear
- `rain-house`: panel title and text run off the right screen edge; house glass is faint against the Mars sky
- `spill-tank`: panel title runs off the right screen edge; butt itself clear
- `stove-rooms`: third room (stove3) is under the info panel; title cut at the screen edge

## Function (does it still do its job?)

All 100 ran headless to the end with exit 0. Compared with the audit's numbers and the headers, **the look changes did not change any physics**: every figure the audit recorded came back identical, to the digit, where the run reached the same sim time. Examples: castellum-aquae houses 64.2 L / fountains 468.4 L / baths 374.7 L; cistern-and-trough 109.6 L / 290.4 L at 130 s; bearing-friction 13.8 / 6.0 / 0.3 deg and 5.92 J; branca 105.3 C, 21.0 kPa; steam-engines-mars 460.3 / 365.5 kPa; solar-steam-wheel 113.1 C, 57.6 kPa, mirrors 674+662+637+698 W; winter-night copper 84.4 C, ice 9.3 mm; water-wheels overshot 14.1 rpm, 150 L/s at 7.7 cm and 3.27 m/s; hama-noria 1.3 rpm, 962.2 L; trip-sluice pool 53 L, reach 247 L; mars-stirling six mirrors 255+234+231+249+269+271 = 1,509 W (audit 1,508.8).

Machines with no number in the audit, checked against their own headers:

- **boiler-shells** (new since the audit): lead BURST at 240.0 kPa, 500.8 s; copper at 4400.1 kPa, 1016.0 s; bronze at 7000.0 kPa, 1144.2 s. Header: 500.8 / 1016.0 / 1144.2 s. Exact.
- **boiler-safety**: guarded 121.1 C, 103.4 kPa, venting 4.34 g/s; unguarded BURST at 200.0 kPa, 482.3 s, 0.629 kg flashed. Exact (a first 200 s run read 67.6 C, which is simply early: the burst is at 482 s).
- **free-sails** 28.6 rpm in 6 m/s (header 28.6) once run for 60 s; at 5 s it is still spinning up (12 rpm). **windmills** breeze 14.3 rpm, 12.24 kW, Cp 0.300, gale 41.36 kW at 60 s; at 10 s still spinning up (9.5 rpm).
- **heron-temple-doors**: bucket 3.0 L at 900 s, the door threshold. **hierapolis-sawmill** wheel 19.3 rpm (header "near 20 rpm"). **hearth-engine** 105.9 C, 23.7 kPa, 2742 rpm, as audited.
- **aeolipile** at 60 s: 111.0 C, 46.9 kPa, 3,541 rpm (audit at 100 s: 111.2 C, 47.9 kPa, 3,908 rpm; still rising, consistent).
- **geared-brake** ends at 0.0 rpm after the brake stops both flywheels; stop times (header 3.4 s and 3.77 s) were not extracted from the trace. **kongming-lantern** climbs (1.4 m/s at 120 s) and the Mars twin stays on the ground (0.00 m/s, lift 0.01 N against 0.26 N weight), as its description says.

Not re-derived (the HUD's final line does not print the number): stove-rooms and glass-rooms room temperatures, kitchen-smoke-jack rpm, mars-sols (matches at 400 s: mirror 241 W, pot 3.4 C, air density 0.013).

Still true, none of it new (all in the audit): **buried-crate** alone falls forever (y = -492.24 m at 10 s, 98.02 m/s, identical to the audit); **bellows-forge** crucibles still read 1,242.7 C / 1,115,121 kPa (bare) and 4,923 C / 8,215,851 kPa (blown) because the sealed 50 g bronze boilers carry no rating (audit UNCLEAR, issue #139 territory); roman-crane wheel reaches 5.0 rpm against the header's 3 (#86); crane-hoist's drum free-spins 1.1 rpm at 10 s; torsion-catapult and vitruvian-catapulta have no worked number to compare.


## Issue

Fix list filed as https://github.com/marctjones/heroicinventions/issues/190, milestone "7 Look and readability" (the "1 Sandbox world" milestone no longer exists: it was renamed "1 Build mode and UX"; this is a look review, so 7 fits better). Already in #148 and not duplicated: buried-crate, the catapult/trebuchet/falling-stones/dam-break framing, bellows-forge, crane-hoist.

## Re-sweep, 2026-10-08 (branch wave4/framing, after waves 1-3)

All 103 machines (three new since the first pass: cart-push, roman-crane-reload, roman-crane-reload-gang) shot again in a hidden real window, `tools/gui-check.sh --hidden HEROIC_AUTOSELECT=<m> HEROIC_AUTORUN=1 HEROIC_FRAMING_REPORT=1 -- "wait 200; shot a; wait 400; shot b; quit"`, each at its default framing and speed, once on the base (2eb643a) and once on this branch, and looked at on contact sheets. The changes are in `docs/art-direction.md` §12.13; frames in `docs/art/review/framing-2026-10-08.jpg`, `labels-action-2026-10-08.jpg`, `throwers-2026-10-08.jpg`.

**Counts: GOOD 97, FIX 5, BROKEN 1 (buried-crate opened alone).**

How the numbers were taken. `HEROIC_FRAMING_REPORT=1` at selection, both builds measured against the same clear area (296..1220 of the 1600-unit viewport: the base's own report used 230..1300, window pixels read as viewport units, which is why its numbers looked better than its frames). *box outside*: the share of the one box round the whole machine that projects outside the clear area (the old measure). *parts outside*: the same for the union of each part's own projected box, which is what the eye sees; the big box's near corners stand in empty air in front of a deep machine, so "box" figures such as newcomen-engine's 79% are projection artefacts, not verdicts. The base's parts figure does not leave out deep parts (the branch leaves out anything over 3 m underground), so newcomen-engine and -hearth's 79% -> 0% is partly that. "n/a": part of the box is behind the camera at selection (falling-stones and tunnel-test drop from 300-350 m), or the machine had no profile. *parts fill*: how much of the clear area the parts cover; machines that overflowed get smaller, on purpose.

| # | machine | parts outside, before -> after | box outside, before -> after | parts fill | verdict | what is left / what changed |
|---|---|---|---|---|---|---|
| 1 | `aeolipile` | 0% -> 0% | 7% -> 5% | 37% -> 37% | GOOD | close-up by design; kettle base and sphere in frame now (was cut) |
| 2 | `airlock` | 0% -> 0% | 1% -> 1% | 37% -> 37% | GOOD | door labels (inner, outer, bleed) now placed |
| 3 | `antikythera-lunar-train` | 1% -> 0% | 9% -> 5% | 53% -> 53% | GOOD | reference; three more gear labels placed |
| 4 | `archimedes-screw` | 33% -> 0% | 40% -> 1% | 66% -> 36% | GOOD | whole run between the panels (was 33% out) |
| 5 | `axle-friction` | 0% -> 0% | 6% -> 2% | 25% -> 24% | GOOD | busy but every label legible |
| 6 | `baghdad-battery` | 0% -> 0% | 0% -> 1% | 20% -> 19% | GOOD | jars small; labels back after the dial filter was narrowed |
| 7 | `ball-ramp` | 0% -> 0% | 0% -> 0% | 8% -> 8% | GOOD | camera follows the balls off the foot, then returns to the ramp when they roll on |
| 8 | `bar-crane` | 1% -> 0% | 3% -> 2% | 36% -> 37% | GOOD |  |
| 9 | `battering-rams` | 14% -> 0% | 19% -> 0% | 39% -> 30% | GOOD | third ram out from under the panel |
| 10 | `bearing-friction` | 0% -> 0% | 0% -> 0% | 34% -> 34% | GOOD |  |
| 11 | `bellows-forge` | 0% -> 0% | 0% -> 0% | 16% -> 16% | GOOD | whole; labels clear of the bubbles |
| 12 | `belt-drive` | 0% -> 0% | 0% -> 0% | 26% -> 26% | GOOD | drive labels stepped aside |
| 13 | `boiler-safety` | 0% -> 0% | 0% -> 0% | 22% -> 22% | GOOD |  |
| 14 | `boiler-shells` | 0% -> 0% | 4% -> 1% | 17% -> 16% | GOOD | rating labels no longer under the panel or on each other |
| 15 | `boulder` | 64% -> 0% | 64% -> 0% | 2% -> 5% | GOOD |  |
| 16 | `branca-steam-wheel` | 32% -> 0% | 44% -> 14% | 80% -> 83% | GOOD | wheel and its label in frame (were cut at the top) |
| 17 | `buried-crate` | 44% -> 0% | 56% -> 13% | 81% -> 74% | BROKEN alone | no ground when opened bare: the crate falls for ever (dig-out world frames it; see coordinator note) |
| 18 | `capstans` | 0% -> 0% | 0% -> 0% | 32% -> 32% | GOOD | two-turns label out from under the panel |
| 19 | `cargo-crate` | 0% -> 0% | 0% -> 0% | 8% -> 8% | GOOD |  |
| 20 | `cart-push` | 0% -> 0% | 0% -> 0% | 8% -> 8% | GOOD |  |
| 21 | `carts` | 0% -> 0% | 0% -> 0% | 13% -> 9% | GOOD | side-on; both carts and the slope stay in frame as they roll (was: carts gone) |
| 22 | `castellum-aquae` | 0% -> 0% | 2% -> 4% | 21% -> 21% | GOOD | whole aqueduct, baths end just inside the panel |
| 23 | `cistern` | 0% -> 0% | 0% -> 0% | 15% -> 15% | GOOD |  |
| 24 | `cistern-and-trough` | 0% -> 0% | 0% -> 0% | 30% -> 30% | GOOD |  |
| 25 | `cistern-drain` | 19% -> 0% | 25% -> 0% | 51% -> 35% | GOOD | cistern out from under the panel (world part; '200 L' is the buried cistern) |
| 26 | `component-gallery` | 2% -> 0% | 11% -> 3% | 8% -> 7% | GOOD | all five benches in; small by nature |
| 27 | `constant-head` | 54% -> 0% | 85% -> 0% | 84% -> 34% | GOOD | whole (was 54% of parts outside, receivers cut) |
| 28 | `crane-hoist` | 0% -> 0% | 0% -> 0% | 35% -> 34% | GOOD |  |
| 29 | `crank-slider` | 0% -> 0% | 0% -> 0% | 15% -> 15% | GOOD | figure beside it at true scale |
| 30 | `crate` | 0% -> 0% | 0% -> 0% | 8% -> 8% | GOOD |  |
| 31 | `crate-tongs` | 0% -> 0% | 0% -> 0% | 38% -> 38% | FIX | tongs too small to read beside the crates: part size, not framing |
| 32 | `dam-break` | 30% -> 0% | 40% -> 3% | 14% -> 8% | GOOD | whole 65 m: pond, gate, race and low pond in frame (parts 30% -> 0% out); small |
| 33 | `drop-test` | 0% -> 0% | 0% -> 0% | 4% -> 4% | GOOD |  |
| 34 | `dry-mill` | 0% -> 0% | 0% -> 0% | 30% -> 30% | GOOD |  |
| 35 | `earth-machines-on-mars` | 8% -> 0% | 34% -> 34% | 62% -> 66% | GOOD | windmill whole |
| 36 | `fall-and-swing` | 0% -> 0% | 0% -> 0% | 35% -> 35% | GOOD |  |
| 37 | `falling-stones` | n/a -> n/a | n/a -> n/a | n/a -> n/a | FIX | 10 cm balls dropped from 350 m: landing view shows the readouts, the balls are out of frame (blueprint/profile) |
| 38 | `field-windmill` | 0% -> 0% | 0% -> 0% | 47% -> 47% | GOOD |  |
| 39 | `fire-and-water` | 0% -> 0% | 0% -> 0% | 20% -> 20% | GOOD |  |
| 40 | `floats` | 36% -> 0% | 43% -> 0% | 60% -> 29% | GOOD | tank out from under the panel |
| 41 | `free-sails` | 0% -> 0% | 0% -> 0% | 31% -> 31% | GOOD |  |
| 42 | `geared-brake` | 0% -> 0% | 0% -> 0% | 38% -> 38% | GOOD |  |
| 43 | `glass-rooms` | 6% -> 0% | 6% -> 0% | 17% -> 14% | GOOD | all four rooms in; the membrane readout fades for want of room (L shows it) |
| 44 | `greenhouse` | 0% -> 0% | 0% -> 0% | 33% -> 32% | GOOD |  |
| 45 | `gristmill` | 21% -> 0% | 25% -> 0% | 20% -> 13% | GOOD | third bed in |
| 46 | `hama-noria` | 0% -> 0% | 8% -> 8% | 32% -> 32% | GOOD | weir and spring in |
| 47 | `hanging-chain` | 1% -> 0% | 1% -> 0% | 59% -> 60% | GOOD |  |
| 48 | `heading-rig` | 4% -> 0% | 7% -> 0% | 18% -> 15% | GOOD | small but whole |
| 49 | `hearth-engine` | 38% -> 0% | 45% -> 0% | 70% -> 66% | GOOD |  |
| 50 | `heliostats` | 0% -> 0% | 0% -> 1% | 19% -> 18% | GOOD |  |
| 51 | `heron-temple-doors` | 0% -> 0% | 0% -> 0% | 21% -> 21% | GOOD |  |
| 52 | `herons-fountain` | 0% -> 0% | 0% -> 0% | 18% -> 18% | GOOD | reference; supply's 8 cm off its dial |
| 53 | `hierapolis-sawmill` | 11% -> 0% | 29% -> 11% | 76% -> 60% | GOOD | header tank and spring in (were under the panel) |
| 54 | `hillside-pond` | 0% -> 0% | 0% -> 0% | 20% -> 20% | GOOD | world: flood-plain opening frame set |
| 55 | `holy-water` | 31% -> 0% | 42% -> 2% | 69% -> 36% | GOOD | basin in |
| 56 | `inclined-plane-demo` | 0% -> 0% | 0% -> 0% | 33% -> 33% | GOOD |  |
| 57 | `kitchen-smoke-jack` | 0% -> 0% | 0% -> 0% | 19% -> 19% | GOOD |  |
| 58 | `kongming-lantern` | 0% -> 0% | 0% -> 0% | 13% -> 13% | GOOD | follows the lantern up |
| 59 | `kongming-lantern-mars` | 0% -> 0% | 0% -> 0% | 8% -> 8% | GOOD |  |
| 60 | `lever-demo` | 29% -> 0% | 40% -> 6% | 60% -> 50% | GOOD | both ends in |
| 61 | `mars-sols` | 0% -> 0% | 0% -> 0% | 16% -> 17% | GOOD |  |
| 62 | `mars-stirling` | 0% -> 0% | 5% -> 4% | 11% -> 11% | GOOD | reference; all three groups in, two more mirror labels |
| 63 | `material-samples` | 100% -> 100% | 100% -> 100% | 0% -> 0% | GOOD | profile aims at the landing; left alone |
| 64 | `newcomen-engine` | 79% -> 0% | 79% -> 79% | 74% -> 60% | GOOD | framed at the shaft's head (bounds run 48 m down) |
| 65 | `newcomen-hearth` | 79% -> 0% | 80% -> 80% | 74% -> 60% | GOOD | as newcomen-engine |
| 66 | `newtons-cradle` | 7% -> 0% | 7% -> 0% | 60% -> 64% | GOOD | reference; frame no longer cut |
| 67 | `pendulum-demo` | 2% -> 0% | 2% -> 0% | 58% -> 58% | GOOD |  |
| 68 | `placer-sluice` | 0% -> 0% | 3% -> 0% | 19% -> 17% | GOOD | from higher; riffles readout rises off the gold |
| 69 | `post-and-lintel-crane` | 0% -> 0% | 0% -> 0% | 25% -> 24% | GOOD | figure beside it, not in front; no pull-out seen |
| 70 | `rail-wagons` | n/a -> 0% | n/a -> 0% | n/a -> 8% | GOOD | side-on; wagons run across the screen |
| 71 | `rain-house` | 0% -> 0% | 0% -> 0% | 38% -> 38% | GOOD |  |
| 72 | `ratchet-windlass` | 19% -> 0% | 21% -> 0% | 48% -> 35% | FIX | whole now, but its four drum names stack in a column above the drums |
| 73 | `roman-crane` | 0% -> 0% | 0% -> 1% | 74% -> 73% | GOOD | reference |
| 74 | `roman-crane-reload` | n/a -> 0% | n/a -> 3% | n/a -> 74% | GOOD | new profile (camera had stood inside the wheel) |
| 75 | `roman-crane-reload-gang` | n/a -> 0% | n/a -> 3% | n/a -> 74% | GOOD | new profile (as above) |
| 76 | `rope-over-bars` | 0% -> 0% | 0% -> 0% | 45% -> 45% | GOOD |  |
| 77 | `sand-timer` | 0% -> 0% | 0% -> 5% | 32% -> 59% | GOOD | closer: timers fill the frame (parts 32% -> 59%) |
| 78 | `shaduf` | 0% -> 0% | 0% -> 0% | 48% -> 48% | GOOD |  |
| 79 | `sluice-demo` | 0% -> 0% | 6% -> 0% | 23% -> 21% | GOOD |  |
| 80 | `solar-furnace` | 0% -> 0% | 4% -> 4% | 21% -> 21% | GOOD |  |
| 81 | `solar-steam-wheel` | 25% -> 0% | 66% -> 25% | 68% -> 47% | GOOD | pot and wheel visible past the mirrors |
| 82 | `spill-tank` | 0% -> 0% | 0% -> 0% | 51% -> 51% | GOOD |  |
| 83 | `steam-engines-mars` | 0% -> 0% | 0% -> 2% | 25% -> 24% | GOOD |  |
| 84 | `stove-rooms` | 0% -> 0% | 2% -> 5% | 23% -> 23% | GOOD | third stove in |
| 85 | `suction-limit` | 1% -> 0% | 1% -> 1% | 63% -> 64% | GOOD | busy but legible |
| 86 | `tank-leaks` | 0% -> 0% | 3% -> 1% | 21% -> 19% | GOOD | fourth barrel in |
| 87 | `torsion-catapult` | 0% -> 0% | 0% -> 0% | 3% -> 6% | GOOD | side-on; follows the throw, lands in frame with the machine, returns for the reload |
| 88 | `trebuchet` | 0% -> 0% | 4% -> 0% | 20% -> 6% | GOOD | as torsion-catapult; small at home (6% of the clear area) to leave room for the throw |
| 89 | `trench-crew` | 0% -> 0% | 0% -> 0% | 23% -> 23% | GOOD | framing; alone it has no ground (its home is the trench world) |
| 90 | `trip-hammer` | 0% -> 0% | 0% -> 0% | 9% -> 10% | FIX | two rigs one behind the other: wheel and hammer labels stack; figure dominates |
| 91 | `trip-sluice` | 1% -> 0% | 7% -> 1% | 54% -> 48% | GOOD |  |
| 92 | `trough` | 0% -> 0% | 0% -> 0% | 19% -> 19% | GOOD |  |
| 93 | `tunnel-test` | n/a -> n/a | n/a -> n/a | n/a -> n/a | FIX | bolts dropped from 300 m bounce out of sight; only planks and readouts in frame |
| 94 | `two-modules` | 46% -> 0% | 66% -> 0% | 67% -> 69% | GOOD | both modules in (were 46% of parts out); readouts large but clear |
| 95 | `universal-joint` | 2% -> 0% | 4% -> 0% | 43% -> 44% | GOOD |  |
| 96 | `vitruvian-catapulta` | 47% -> 5% | 54% -> 11% | 14% -> 5% | GOOD | side-on; bolt's 36.5 m flight and the machine in one frame; returns for the reload |
| 97 | `wake-clock` | 15% -> 0% | 20% -> 0% | 55% -> 43% | GOOD | spring tank in |
| 98 | `walkers-wheel` | 0% -> 0% | 0% -> 0% | 41% -> 41% | GOOD |  |
| 99 | `water-clock` | 0% -> 0% | 0% -> 0% | 48% -> 48% | GOOD |  |
| 100 | `water-mill-race` | 25% -> 0% | 33% -> 2% | 19% -> 12% | GOOD | both ends in |
| 101 | `water-wheels` | 0% -> 0% | 0% -> 0% | 16% -> 16% | GOOD | reference |
| 102 | `windmills` | 16% -> 0% | 17% -> 0% | 50% -> 37% | GOOD | gale mill in |
| 103 | `winter-night` | 0% -> 0% | 0% -> 0% | 22% -> 22% | GOOD |  |

### #190 checklist, re-swept

- Framing under the panels (castellum-aquae, glass-rooms, solar-furnace, sluice-demo, mars-stirling, fire-and-water, water-wheels, hama-noria, constant-head, floats, gristmill, windmills, tank-leaks, stove-rooms, battering-rams, capstans, hierapolis-sawmill, wake-clock, water-mill-race, archimedes-screw, lever-demo, holy-water, heading-rig, cistern-drain): done, every one 0% of parts outside.
- dam-break: done, the whole 65 m in frame (parts 30% -> 0%).
- Throwers (trebuchet, torsion-catapult, vitruvian-catapulta): done; side-on, the machine and the landing in one frame, back to the machine for the reload, a fresh trail each throw; the figure beside the machine.
- Labels over gauges and each other (herons-fountain, placer-sluice, boiler-shells, capstans, belt-drive, bellows-forge): done by the general rule in `Main.Labels.cs`. Still stacked: ratchet-windlass, trip-hammer.
- Things leaving the frame: ball-ramp, carts, rail-wagons done. falling-stones and tunnel-test still open (bodies dropped from 300-350 m).
- Too close / cut off (aeolipile, branca-steam-wheel, newtons-cradle, pendulum-demo, kitchen-smoke-jack, hearth-engine): done by the fit.
- Panel title wraps: done before this pass (`Wrap(_hudTitle)`), seen on airlock, glass-rooms, greenhouse, mars-sols, rain-house, earth-machines-on-mars.
- Missing profiles: roman-crane-reload, roman-crane-reload-gang (new); worlds flood-plain and dig-out have opening frames.
- Still open, not framing: crate-tongs' tongs too small; bare buried-crate has no ground; falling-stones and tunnel-test bodies out of frame.
