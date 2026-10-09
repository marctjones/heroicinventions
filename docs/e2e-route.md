# The fastest route, end to end (issue #96)

`docs/lonely-rover.html` ("Routes to the call", "The fastest route") as a script that runs in the crater world, and what stops a player doing it by hand today.

- World: `game/worlds/lonely-rover-e2e.world` (the opening's cargo, rover and slide, plus the finished works placed as `route`).
- Works: `racket/machines/e2e-route.rkt` (header has the worked numbers).
- Tests: `racket/heroic/tests/e2e-route-test.rkt` (the night, the win and two gates; about 3.5 minutes alone, 5.5 with the three runs in parallel) and `e2e-rover-test.rkt` (the rover's drive and the dig attempts, about 50 s).

## The route, step by step

G = done in the game as a player would; S = a stand-in; the GAPs are below.

| # | Doc step | In the test | |
|---|---|---|---|
| 1 | The slide buries the bank | The world's own opening: the bank's crate is held under about 4 m of rubble (`crate.buried` 1) | G |
| 2 | Free the bank: dig a pit beside the boulders and undercut them (#54) | The rover drives to the slide and digs at four places over the crate; every cycle is refused, "Dug nothing: bedrock under the teeth". The bank in the works is already free | S, GAP 1 |
| 3 | Bury it in a tight vault | A placed `enclosure` (0.5 m regolith cavity) with the bank's cells in it | S, GAP 2 |
| 4 | A windmill with a geared generator | Placed: 10 m sails, three 5:1 meshes at 0.97, a motor (1,500 rpm cut-in) charging the bank. The player can build these since #204, but not yet charge the found bank from them (#208) | S, GAP 3 |
| 5 | Rock heated by heliostats, pushed into the bin at dusk | A 1.5 m2 heliostat heats 40 kg of basalt where it lies in the bin, lid open | S, GAP 4 |
| 6 | A bimetal thermostat on the lid | The strip works the lid in proportion (#97) | G |
| 7 | Sleep until 03:00, windmill tops up, call | `HEROIC_SLEEP=pre-dawn`: the game's own sleep runs from 07:00 to 02:49 (the day's heating and the night, sim-side, the Jolt side paused: GAP 7), then real time: the windmill charges, the call goes out at the pass. A script can now sleep from a step (`sleep pre-dawn`, #207) | G, GAP 7 |

## Result (headless Godot, Jolt 120 Hz, traced at 10 s)

- The sleep ends at 73,300 s (02:49 of sol 2) with the bank at 20.10 C (the C# sim: 20.10) and the rock at 45.8 C.
- The windmill charges the bank at 153 W on average (105 to 287 W; worked 111 W at a steady 6 m/s). A 25 Wh bank is full at 73,730 s, 4.2 minutes before the pass, with `bank.from-wind` = 25 Wh and no other source.
- Nothing is won before the pass (`bank.won` 0 in every frame earlier). The call goes out at 73,980 s, 03:00 on sol 2, the bank at 19.9 C. `scene.won` = 1.
- Gates: with the heliostat covered, the bank stays under 0 C all night, charges nothing and no call goes out; with the motor's cut-in raised past the engine's 3,000 rpm cap it charges nothing and no call goes out. (The pass and the 0 to 45 C range are electrics-test.rkt's.)

## Fun-check against the doc

| | Doc | This route |
|---|---|---|
| Sols | "about 3 to 5" | The win is at 03:00 on sol 2, 20 hours after a 07:00 start, 0.83 sol of play. That is with a 25 Wh bank (a scenario number, #60). With the doc's 5 kWh it is 5,000 / 153 = 33 hours of charging, and the bank is warm for charging from 17:05, about 10 hours a sol with a wind that is lowest at dusk: about 3 to 4 sols, which agrees with the doc. The doc's "windmiller" row says 72 W and 7 to 10 sols; this windmill (10 m sails in the corridor, 0.86 of 6 m/s) gives 153 W |
| Charge rate | Stirling 320 W at noon, windmill about 72 W | Windmill 153 W mean at night, 287 W peak (the night wind is x1.35 at 02:00 over the day mean). The Stirling is not in this test |
| Night margin | bank +4.3 C at 03:00 (40 kg of rock at 200 C pushed in at 17:00 from a frozen start), "no margin to spare"; 35 kg fails | +20.1 C at 02:49 and +19.9 C at 03:00 (a 1.5 m2 heliostat on the rock through the day with the lid open), because the day's heat soaks into the walls while the sun is shining. The bank is over 0 C from 17:05 |
| Rock temperature | about 200 C, "a sol" | 118.7 C peak with 1.5 m2. With the doc's 4 m2 and the rock left in the sun, nothing limits it: 191 C at 11:00, 600 C by dusk. The doc's "rock heated to about 200 C" needs the rover to take it out of the sun (or a smaller mirror); the game has no cap |
| Bimetal | holds the bank near 38 C in a warm vault | The lid sits 57% open at 03:00 and the bank 20 C; the strip never shuts it |
| Sleep | the rover sleeps until the pass | The route's sleep (73,300 s) is longer than a sleep keeps the Jolt side running for by default (7,200 s, #207), so it stops only the Jolt side. The windmill and its train run free in the sim during the sleep (3.5 rad/s at the night wind) and are put into the Jolt bodies on waking, so the first seconds of charging are the sails' flywheel energy (up to 17 Wh), not wind |

## GAPs

What a player cannot yet do, the issue it belongs to, and the stand-in used. "NEW" has no issue yet.

1. **The rover cannot free the bank.** The backhoe refuses the slide: "Dug nothing: bedrock under the teeth is too hard" at all four standing places (west, on top, east and north of the crate; `e2e-rover-test.rkt` prints them). The rubble over the bank is classed as bedrock where the fine patch is made, so the 4 m of rubble cannot be dug, and no boulder can be undercut (21.6 t against a 0.4 kN push). Belongs to #54/#61 with #63 (the dirt rule). Stand-in: the works hold a bank that is already free. Related: the cargo crate and the bank part are different things (a `cargo-crate` is an oak block named `battery-bank`; the electrics' `battery-bank` is a part of a machine), so freeing the crate would not yet give the player a bank. NEW (a crate that opens into a bank part, or a machine file for the cargo), with #64.
2. **No way to bury the bank in a vault.** An `enclosure` is a part with a wall thickness, not ground the rover dug; nothing joins worked ground to an enclosure's wall, and a crate cannot be pushed into a cavity and covered. Belongs to #71 with #63; NEW. Stand-in: a placed enclosure (bimetal-night's vault).
3. **The windmill, train and motor are built in the world file, not by the player.** Since #204 a player can build them in the game: "Build a new machine" opens build mode live on the ground ahead of the rover, the parts go anywhere (building is free and reaches anywhere), and the machine is saved whole (`(build LABEL (at x y z) (machine ...))`); `build-in-world-test.rkt` builds a windmill, a 64:1 three-stage train and a motor in the opening world, saves, loads and checks it turning. The route's stand-in stays for now: its motor charges the bank, and a motor can only charge a bank in its own machine, which the player cannot build (the bank is found cargo). A built motor with no bank is set aside unfinished, so the conversion waits for the wire between machines (#208, GAP 6) and a motor that builds with its bank on the far end of a wire. The palette's gears give 4:1 a stage at most (72:18), so a player's train is 64:1, not 125:1, unless a stage is added. Stand-in: `(place route e2e-route ...)` in the world file.
4. **Nothing moves hot rock into the bin.** A heat store is not a body; a bin has a fixed store (`#:holds`) and there is no put-in, so "the rover pushes 40 kg of heated rock sideways into the lidded bin" cannot be done (the rover's push is for rigid bodies, #163). Belongs to #71/#163; NEW (a movable heat store: a rock body with a temperature that a bin takes). Stand-in: the heliostat's image falls on the rock in the bin. The doc's numbers differ for it (see the fun-check).
5. **Scripted sleep: done (#207).** `sleep WAKE [live|paused]` and `sleep until TARGET.FIELD above|below VALUE [limit S] [live|paused]` are `HEROIC_INPUT` steps: they sleep the scene as the panel does and hold the script until the wake, so a rover script can drive, sleep, then act in one run (`sleep-live-test.rkt`). The route's own test still starts its sleep at load (`HEROIC_SLEEP`) because the sleep to 03:00 is the 73,300 s of GAP 7.
6. **No wire between machines.** #78's title says "shafts, belts, pipes and wires across machines"; `WorldDef.LinkKinds` is `pipe` and `shaft`. A bank freed from a crate (its own machine) cannot be wired to a generator in another machine; the generator and bank must be parts of one machine. Belongs to #78/#82; NEW (a `wire` link kind). Stand-in: one machine holds the bank, generator and vault.
7. **Sleep and Jolt: the engine runs through a sleep, at the engine's pace (#207).** A sleep of a Jolt-driven machine (geared trains, driven shafts, axles, moving bodies) now lets the physics engine run: the game's own loop at 200x with the redrawing left out, so every tick is a watched tick and the result is the same (generator-train, 240 s: every field of every row equal to the watched run's, max difference 0; the crater world's route machine, 30 s slept: 0). The price is the engine's pace: about 600 steps a second in the crater world (a 600 s sleep took 120 s), so the 36,000 s from the bank's warming (17:05) to the wake would take two hours and the whole 73,300 s about four hours. A sleep is therefore live by default only while its estimate is within 7,200 s (`SleepControl.LiveLimit`); a longer one pauses the Jolt side as before (the sails run free in the sim and the bodies catch up on waking), unless told to keep the machines turning (the Sleep panel's "Keep machines turning", `live` in the step, `HEROIC_SLEEP_PHYSICS=1`). The route's test therefore still wakes 11 minutes before the pass and runs the windmill in real time. A Stirling-charged route would sleep the whole night sim-side. Left as a follow-up: a faster live night (a coarser Jolt step is a different result from watching, so it was not taken).
8. **Windmills face a fixed +z** (#193, open): the works' sails are placed square to +z, and the wind has a speed but the windmill ignores its direction in the model. No stand-in is needed for the test, but a player's windmill would need to be turned by hand.
9. **Tooling: done (#207).** A trace sampled every `dt` skips to the next sample after a sleep instead of writing a frame per tick to catch up (generator-train, dt 1, sleeping to 200 s of 240 s with the engine paused: 42 rows, none inside the sleep; before, one per 1/120 s). A sleep's autosave goes to `HEROIC_SAVES_DIR` when set (`godothost.rkt` gives every run a temporary one), and a scripted run (`HEROIC_QUIT_AFTER_SIM_SECONDS`) with none writes no autosave, so test sleeps leave the player's saves alone.

Not done: the doc's Stirling engine, the 5 kWh bank, a second sol (the rover re-heating the rock each sol, the vault warming through the week), the rover's own vault digging, the boulder undermining, and the overheating gate in this world (electrics-test.rkt and bimetal-night own it).
