# The fastest route, end to end (issue #96)

`docs/lonely-rover.html` ("Routes to the call", "The fastest route") as a script that runs in the crater world, and what stops a player doing it by hand today.

- World: `game/worlds/lonely-rover-e2e.world` (the opening's cargo, rover and slide, and the real found bank, `found-bank`; scenario `bank-capacity 0.005`, the 5 kWh bank as 25 Wh).
- Vault machine: `racket/machines/route-vault.rkt`, placed round the found bank's crate: a tight regolith vault, the heated rock in a lidded bin, a bimetal strip on the lid (sensing the vault's air), a 4 m2 heliostat and the wakes; header has the reasoning. It is a machine of its own: the vault holds the found bank's cells because the crate's centre is inside its box (#211, `WorldZones`). The stand-in `found-bank-vaulted` (the vault written into the bank's own machine) is gone, as are the old placed works `e2e-route.rkt`: the windmill, train and generator are built by the test.
- Clock: the opening's and the found bank's own (no sun given: noon at the start, latitude 31.2, day 172; the sun sets near 17:00). The call is the found bank's 03:00, 15 local hours on: 55,484 s.
- Tests: `racket/heroic/tests/e2e-route-test.rkt` (builds, saves, wires, sleeps and wins, plus two gates; about 4 minutes, the three sleeps in parallel) and `e2e-rover-test.rkt` (the rover's drive and the dig attempts, about 12 s).

## The route, step by step

G = done in the game as a player would; S = a stand-in; the GAPs are below.

| # | Doc step | In the test | |
|---|---|---|---|
| 1 | The slide buries the bank | The world's own opening: the found bank's crate is held under 3.98 m of rubble (`crate.buried` 1, `crate.cover` 3.98) | G |
| 2 | Free the bank: dig a pit beside the boulders and undercut them | The rover drives to the slide and digs at four places over the crate: each cycle now digs and dumps 0.20 m3 (before the dirt rule it was refused). Freeing the crate is not run (about 4 m of rubble, many cycles); the bank stays buried and warm in its vault, which the sleep does not need freed | S, GAP 1 |
| 3 | Bury it in a tight vault | A vault of its own (`route-vault`, a 0.5 m regolith cavity) placed round the crate where it lies; the found bank's cells are in its air because the crate's centre is in its box (#211). Placed by the world, not dug by the rover | G (placed), GAP 2 |
| 4 | A windmill with a geared generator | BUILT in build mode (scripted `build` steps, the game's own path) at (180, 105): 10 m sails on a post, four 72:18 meshes (4:1 each, 256:1) and the salvaged motor on the last pinion; saved with the world (`(build built-1 ...)`) and loaded | G |
| 5 | Wire the generator to the bank | A world link `(link wire-1 wire (from built-1 motor) (to battery-bank bank))` written into the saved world's link list. The join gesture (click the generator, click the bank) needs a real window (`HEROIC_WORLD_JOIN` projects onto the screen), and the two are 90 m apart, so a headless test cannot click it | S, GAP 6 |
| 6 | Rock heated by heliostats, pushed into the bin at dusk | A 4 m2 heliostat (the doc's) heats 40 kg of basalt where it lies in the bin, lid open | S, GAP 4 |
| 7 | A bimetal thermostat on the lid | The strip, sensing the vault's air (a strip in the vault's machine cannot sense another machine's cells), works the lid in proportion (#97) | G |
| 8 | Sleep until 03:00, windmill tops up, call | `HEROIC_SLEEP=pre-dawn` (the vault's wake: it is the world's first machine): the game's own sleep runs from noon to 02:49, then real time: the windmill charges the found bank over the wire, the call goes out at the pass | G, GAP 5 |

## Result (headless Godot, Jolt 120 Hz, traced at 10 s)

- On loading, `[zones] battery-bank.cells joined vault.vault`: the vault of the other machine holds the found bank's cells.
- The sleep ends at 54,800 s (02:49 of sol 2) with the found bank's cells at 24.48 C (worked 24.47: the C# sim with the cells written into the vault, the network the zone makes), the rock at 92.2 C (92.1) after a peak of 224 C at sunset, the vault's air 27.2 C and the strip holding the lid 37% open.
- The built windmill charges the bank at 90 W once the sails' flywheel is spent (the rotor settles at 1,557 rpm, 57 rpm over the cut-in). The 25 Wh bank is full at 55,360 s, 124 s before the pass, and the game's ending reads `ending source wind: 25.000 Wh`, `sources 1`.
- Nothing is won before the pass (`bank.won` 0 in every frame earlier). The call goes out at 55,490 s, 03:00 on sol 2. `scene.won` = 1. The game's log: "The call went out from bank on sol 2 at 03:00. The game is won."
- Gates: with the heliostat covered the bank stays under 0 C all night, charges nothing and no call goes out; with the same windmill and no wire the generator is an open circuit, nothing is charged and no call goes out.
- The heliostat's area against the cells at 02:49 (the C# sim, before choosing): 1.5 m2 (the old stand-in's, which had 11 hours of sun from 07:00 at latitude -2) leaves them at -27.8 C (the game: -27.76, the same); 3 m2 +8.8 C; 4 m2 +24.5 C.

## The train: what ratio the generator needs

The sails' torque is tau = tau* (2 - w R / (v l*)), tau* = 104.0 N.m at 6 m/s in the world's air; the motor takes tau_g = 0.1146 (X - 157.08) N.m above its cut-in (157.08 rad/s, 1,500 rpm). With ratio G the rotor settles at X = (2 tau* + 157.08 G k) / (G k + 4 tau* / (v G)).

| Stages | Ratio | Rotor at 6 m/s | Lowest wind that passes the cut-in |
|---|---|---|---|
| 2 | 16:1 | 80 rad/s, under | none |
| 3 | 64:1 | 161.5 rad/s (1,542 rpm), over by 4.4 rad/s | 4.9 m/s |
| 4 | 256:1 | 162.7 rad/s (1,553 rpm; sim 1,557), over by 5.6 | 1.2 m/s |

A three-stage train DOES pass the cut-in at the palette windmill's 6 m/s (checked in the sim: it ran the same route and won at 03:00, settling near 1,555 rpm and 80 W, still falling), against the brief's expectation that it would not. It has no margin below 4.9 m/s; the fourth stage is what keeps the generator charging in a weaker wind and is why the route builds four. The torque curve makes the result a small difference of two large numbers: the worked 83 W against the sim's 90 W is a 0.4 rad/s difference in the rotor's speed.

## Fun-check against the doc

| | Doc | This route |
|---|---|---|
| Sols | "about 3 to 5" | The win is at 03:00 on sol 2 with a 25 Wh bank (a scenario number, #60). With the doc's 5 kWh the bank needs 5,000 / 90 = 55.6 hours of the windmill, and it is warm for charging about 10 hours a sol: about 5 to 6 sols, a little over the doc's 3 to 5 (the earlier route's 153 W gave 3 to 4: that windmill took the map's corridor and night wind; the palette windmill is a flat 6 m/s) |
| Charge rate | Stirling 320 W at noon, windmill about 72 W | Windmill 90 W, flat (no night factor, no corridor). The Stirling is not in this test |
| Night margin | bank +4.3 C at 03:00 (40 kg of rock at 200 C pushed in at 17:00 from a frozen start), "no margin to spare"; 35 kg fails | +24.5 C at 02:49 (a 4 m2 heliostat on the rock through the afternoon with the lid open), because the day's heat soaks into the walls while the sun is shining. The doc's own case is checked as well: the found bank pushed at 17:00 into a vault of another machine with 40 kg at 200 C is +4.28 C at 03:00 (`vault-zone-test.rkt`), night-heat's +4.3 |
| Rock temperature | about 200 C, "a sol" | 224 C peak at sunset with the doc's 4 m2 and 5 hours of sun (92 C at 02:49). With a longer day nothing limits it; the game has no cap |
| Bimetal | holds the bank near 38 C in a warm vault | The strip senses the vault's air (27 C at 02:49) and holds the lid 37% open; the bank 24.5 C; it never shuts it |
| Sleep | the rover sleeps until the pass | The sleep stops only the Jolt side. The built windmill and its train run free in the sim during the sleep (2.8 rad/s) and are put into the Jolt bodies on waking, so the first minute of charging is mostly the sails' flywheel energy (up to 56 Wh at that speed; about 8 Wh went in before the load slowed it), then the wind |

## GAPs

What a player cannot yet do, the issue it belongs to, and the stand-in used. "NEW" has no issue yet.

1. **Freeing the bank (changed).** The dirt rule landed: the backhoe digs the slide's rubble now (0.20 m3 a cycle at four places; `e2e-rover-test.rkt` prints them) where it was refused as bedrock. Not yet run: the many cycles that clear about 4 m over the crate, the boulder on top of it (21.6 t, undercut, #54), and then pushing the crate into a vault. The crate is the found bank itself since #209 (its charge, cells and record go with it), so the old "crate is not a bank part" half of this gap is closed. Stand-in: the bank is not freed; the vault is at the crate.
2. **Done: a vault of another machine holds the found bank (#211).** An enclosure holds any machine's heat store whose point lies in its box (`WorldZones`, worked out each tick from where the stores are: the found bank's cells ride the crate their bank is `#:on`; the point decides for a crate straddling the wall; a store already in a room of its own machine stays there). Pushing the crate in or out joins or leaves the vault (`[zones]` in the log), a save keeps it (it follows from where the crate is), and `vault-zone-test.rkt` checks the doc's night: +4.28 C at 03:00, night-heat's +4.3. Residual: the route's vault is placed by the world round the crate where it lies, not dug by the rover (the rover's own vault digging is not done), and the crate is not freed and pushed into it (GAP 1). A vault the player builds in build mode is a placement like any other, so the same rule applies to it; that is not run here (the checks place theirs).
3. **Done: the windmill, train and generator are built by the player** (since #204), the generator is wired to the found bank (#208) and the found bank charges and wins (#209). The palette gears give 4:1 a stage, so the train is four stages (see above). Residual: the palette windmill takes a flat 6 m/s and ignores the map's wind corridor and its night factor (the old placed works took both), so its charge rate is lower; it also faces a fixed +z (#193).
4. **Nothing moves hot rock into the bin.** A heat store is not a body; a bin has a fixed store (`#:holds`) and there is no put-in (#206, skipped by the owner as a simplification). Stand-in: the heliostat's image falls on the rock in the bin. The doc's numbers differ for it (see the fun-check).
5. **Scripted sleep: done (#207).** `sleep WAKE [live|paused]` and `sleep until TARGET.FIELD above|below VALUE [limit S] [live|paused]` are `HEROIC_INPUT` steps (the first looks for the wake on the focused machine, then on every placed machine): they sleep the world as the panel does and hold the script until the wake, so a rover script can drive, build, sleep, then act in one run (`sleep-live-test.rkt` sleeps in this world). The route's own test still starts its sleep at load (`HEROIC_SLEEP`); it could use the step, which would let the build and the sleep share a run.
6. **The join gesture is not scriptable headless.** The wire is a link in the saved world's link list; the gesture (J, click the generator, click the bank) is clicked at the parts' places on screen (`HEROIC_WORLD_JOIN`, needs a real window) and the generator (180, 105) and the bank (264, 143) are 90 m apart. The wire itself works (the same link as the gesture makes, restored by the save: `[links] restored 1 link(s)`). What would fix it: a scripted `join PART PART` input step that picks by part rather than by screen position. NEW.
7. **Sleep and Jolt (#207): the engine can run through a sleep, at its own pace.** A sleep of a Jolt-driven machine (geared trains, driven shafts, axles, moving bodies) lets the physics engine run: the game's own loop at 200x with the redrawing left out, so every tick is a watched tick and the result is the same (generator-train 240 s, and the crater world's route machine over a 30 s sleep: every field of every row equal to the watched run's, max difference 0). The price is the engine's pace, about 600 steps a second in the crater world (a 600 s sleep took 120 s), so the route's 54,800 s sleep would take about 2.5 hours live. A sleep is live by default only while its estimate is within 7,200 s (`SleepControl.LiveLimit`); a longer one pauses the Jolt side as before, unless told to keep machines turning (the panel's "Keep machines turning", `live` in the step, `HEROIC_SLEEP_PHYSICS=1`). So the route as it stands still sleeps with the Jolt side paused; sleeping it with the windmill turning (`HEROIC_SLEEP_PHYSICS=1`) is possible but hours long, and the first hours (before the bank warms) charge nothing anyway. Left: a faster live night (a coarser Jolt step is not the same result as watching).
8. **Tooling: done (#207, except the trace field).** A trace sampled every `dt` skips to the next sample after a sleep instead of writing a frame per tick (generator-train, dt 1, a sleep to 200 s of 240 s with the engine paused: 42 rows, none inside the sleep). A sleep's autosave goes to `HEROIC_SAVES_DIR` when set (`godothost.rkt` gives every run a temporary one), and a scripted run (`HEROIC_QUIT_AFTER_SIM_SECONDS`) with none writes no autosave. A found bank's `from-wind` is not a trace field; the source record is read from the game's ending lines.

Not done: the doc's Stirling engine, a second sol (the rover re-heating the rock each sol), the rover's own vault digging, the boulder undermining, the join gesture itself, the overheating gate in this world (electrics-test.rkt and bimetal-night own it), and the full 5 kWh bank (the scenario scales it to 25 Wh; it would take 55.6 hours of this windmill).
