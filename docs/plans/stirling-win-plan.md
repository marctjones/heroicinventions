# The Stirling win and the second route (plan for #232)

Planned read-only with Fable from the code at 32b9a98 (2026-10-17), after #220 and on the owner's answers on #269 (the Stirling gets real friction and windage; a paused sleep steps world shaft links; #269 lands first). Committed before #269 so it is saved; the session that takes #232 updates it with #269's results (the engine's losses, the free speed, the paused sleep). Numbers: **W** worked (a python re-run of Sun.cs, Mirror.cs, StirlingEngine.cs, Generator.cs reproducing #220's 213.9 W at 11:38 and 1.348 / 1.077 kWh a sol), **S** sourced, **M** measured, **[V]** to be filled by the vault run before the win run is asserted. Powers and fill times are formulas of P, so #269's friction can be folded in.

**Headline.** The windmill route's `cut-in`, `wired` and `gear-up` conditions read any generator or train, so a Stirling turning the found motor would start "The windmiller". The smallest honest change: a route condition may name its prime mover (`(rotor-over-cut-in wind)`, `(generator-wired stirling)`, `(train-ratio-at-least 100 wind)`), read from `Generator.Mover.Name` (wind, water-wheel, steam-jet, stirling), and the windmill's three conditions say `wind` (a data change, no number changes). The Stirling route has six steps (engine, heat, cut-in, wired, warm, full), two new zero-argument kinds (`stirling-exists`, `engine-heated`) and three new reason numbers read from the engine. The win: in lonely-rover-e2e the engine built at noon on sol 1 and joined at once charges nothing until the bank passes 0 °C at 19:27 (M), fills from the dawn of sol 2 (cut-in 07:49, full 08:29; W, no friction) and calls at 03:00 on sol 3, 144,259 s. The one thing that could stop it is the vault, not the engine. On sol 2 the mirror has the rock for ten hours and the lid shuts at 40 °C; the director's solve wins at 03:00 sol 3 with the cells at 39.7 °C (M, 2026-10-17), 5.3 K under the 45 °C limit, and the e2e vault had 2-3 more hours of sol-1 sun. So a vault-only run is step 1.

## Status, 2026-10-17 (the session was stopped by the owner; see docs/plans/handoff-2026-10-17-routes.md)

Done on branch `claude/stirling-win-wip` (commit `3ab31c7`), not yet landed:
- the route data and code of section 1: a generator reading's source, `(rotor-over-cut-in wind)` and so on, `stirling-exists`, `engine-heated`, `engine-watts`; `windmill.route` says `wind`; `stirling.route`;
- the reveal tests (StirlingRouteRevealTests: Stirling-only, windmill-only, mixed);
- `stirling-win-test.rkt`, written, not yet run.

What changed since this plan was written:
1. **#269 slice 1 gave the engine its drag** (k ω², 0.4375 of Carnot). The plain shaft charges about 5 W at 08:00 on sol 2 (most of the engine's 60 W is lost to drag at the motor's 1,500 rpm) and about 82 W at 09:00. The winning run therefore joins at 09:00 on sol 2, and the fill is worked at about 900 s: full near 09:15.
2. **#269 (b) is parked**, so the run must never sleep with the engine joined while the motor would charge. The order is: build at noon on sol 1 (unjoined); a paused sleep to 09:00 on sol 2; join while watched; a live sleep until full; a paused sleep to the 03:00 pass with a full bank. This is section 3's fallback shape, with the engine on load 0 (the drag now bounds it).
3. **The vault on sol 2 was the risk this plan named, and it was real.** With the old physics the cells were 45.6 °C at 03:00 on sol 3, over the limit, because a shut lid did not block the mirror (the rock reached 762 °C). The lid fix (commit `c6c63db` on the same branch; its own issue in milestone 17) gives 34.5 °C at the call. The win test needs it.

## 1. Code and route data

### `src/HeroicInventions.Sim/Game/Routes.cs`
- `GeneratorReading` gains `string? Source` (after `Ratio`): `Generator.Mover?.Name`, null while nothing the sim knows turns it.
- New `EngineReading(string Id, double HeatW, double HotC, double ColdC, double ShaftW, double Rpm)` from `rt.Stirlings`; `RouteReading` gains `IReadOnlyList<EngineReading>? Engines = null` (last positional).
- `BuiltTrains` becomes `IReadOnlyList<(string Source, double Ratio)>?` (Windmills → wind, WaterWheels → water-wheel, JetWheels → steam-jet, Stirlings → stirling); `TrainRatios(string? source)` filters by source (null = any).
- `RouteCondition(string Kind, IReadOnlyList<double> Args, string? Source = null)`; `Kinds` becomes `(int Args, bool Source, string Reads)`: `stirling-exists` (0, no), `engine-heated` (0, no: `<engine>.heat > 0`), and `train-ratio-at-least` (1, yes), `rotor-over-cut-in` (0, yes), `generator-wired` (0, yes) with "naming a prime mover reads only generators and trains turned by that kind (Generator.Mover.Name)"; the rest unchanged.
- `Sources = ["wind", "water-wheel", "steam-jet", "stirling"]` (the closed set `PrimeMoverFrom` names).
- `Holds` filters by source; `stirling-exists` → `Engines is { Count: > 0 }`; `engine-heated` → `Engines.Any(e => e.HeatW > 0)`.
- `ParseCondition`: a trailing `SSymbol` is the source; refused with "KIND takes no prime mover" or "no prime mover named X; they are: ...".
- `NumberNames` adds `engine-watts` (max ShaftW), `engine-hot-c` (max HotC), `engine-heat-w` (max HeatW). `wind-watts` reads only `Source == "wind"` generators.

### `game/routes/windmill.route` (data only)
`(met (train-ratio-at-least 100 wind))`, `(met (rotor-over-cut-in wind))`, `(met (generator-wired wind))`; the header's mapping lines say "turned by the wind (Generator.Mover.Name; a Stirling on the same motor does not count)".

### `game/routes/stirling.route` (new)
```
(route stirling
  (name "The hot-air engineer")
  (description "Let the sun's heat charge the bank: a hot-air engine under mirrors, a shaft to a generator over its cut-in, a wire to a warm bank.")
  (proven-by "racket/heroic/tests/stirling-win-test.rkt")
  (step engine (title "An engine that runs on heat")
    (reason "Mars's cold air is a fine sink for a hot-air engine, and the bank needs {bank-wh|a good many} Wh from somewhere.")
    (met (stirling-exists)))
  (step heat (title "Land the sun on its hot end")
    (reason "A hot-air engine works only while its hot end is hotter than the air: it is at {engine-hot-c|?} °C with {engine-heat-w|no} W of light on it. Mirrors aimed at it put the sun there; a square metre of flat mirror lands about 300 W at noon.")
    (met (engine-heated)))
  (step cut-in (title "Turn the generator over its cut-in")
    (reason "The engine makes {engine-watts|0} W at the shaft; the generator charges only above {cut-in-rpm|1,500} rpm and its rotor turns at {rotor-rpm|0}. A shaft from the engine to the rotor turns it there; the engine's power does not depend on its speed, so no gearing is needed.")
    (met (rotor-over-cut-in stirling)))
  (step wired (title "Wire the generator to the bank")
    (reason "A generator with no wire to a bank charges nothing, however fast it turns.")
    (met (generator-wired stirling)))
  (step warm ... (shared #t) (met (bank-temperature-between 0 45)))      ; the windmill's text
  (step full (title "Fill the bank")
    (reason "The bank needs {bank-wh|?} Wh and holds {bank-charge-wh|?}; the engine gives about {engine-watts|some} W at the shaft while the sun is on its mirrors, four fifths of it as charge, and nothing after sunset.")
    (shared #t) (met (bank-full))))
```
(After #269 the cut-in reason's "does not depend on its speed" sentence must match the engine's friction.) No "hot enough" threshold step (any light makes some power: one 1 m² mirror gives Th -13 °C, 4.8 W, W); a kind `engine-power-at-least W` is one line if the owner wants it.

### Cross-start audit
| Windmill step | in a Stirling run | Stirling step | in a windmill run |
|---|---|---|---|
| windmill-exists | no windmill in e2e | stirling-exists | no |
| train-ratio-at-least 100 wind | a Stirling's train is 1 and the kind needs `wind` | engine-heated | no engine |
| rotor-over-cut-in wind | the motor's Mover is stirling: no | rotor-over-cut-in stirling | no |
| generator-wired wind | same | generator-wired stirling | no |
| warm, full | shared: recorded quietly for both, start neither | | |

RouteReveal.cs and Main.Routes.cs: no change.

## 2. The winning run (W), lonely-rover-e2e, 25 Wh bank, #269 in

Noon sol 1 is 0 s; a sol 88,775 s; an hour 3,698.96 s; the passes at 55,484.4 s (sol 2) and **144,259.4 s (sol 3)**. Sunrise 06:59 (70,237 s), sunset 17:01. The build is #220's, the engine `#:load 0`.
```
wait 360; build new 220 126; build (post stand ...); build (stirling eng ... #:load 0); build (mirror m1..m10 ...); build done
join built-1.eng motors.rotor; join motors.motor battery-bank.bank
sleep until scene.elapsed above 27750 limit 30000 paused; wait 2400      ; 19:30 sol 1
sleep until scene.elapsed above 74904 limit 50000 paused; wait 1200      ; 08:15 sol 2
sleep until bank.full above 0.5 limit 3600 live; wait 1200               ; watched through the fill (worked 08:29)
sleep until scene.elapsed above 88775 limit 20000 paused; wait 1200      ; noon sol 2
sleep until scene.elapsed above 107600 limit 25000 paused; wait 1200     ; 17:05 sol 2
sleep until scene.elapsed above 143900 limit 40000 paused                ; 02:54 sol 3, then watched through the pass
```
`HEROIC_QUIT_AFTER_SIM_SECONDS=144400`, `HEROIC_TRACE_DT=10`, no HEROIC_SET. The chain of wakes is there because a machine trace writes no rows inside a paused sleep (GAP 8). First-met route times survive the sleeps (`RoutesTick` runs on `Stepped` every 0.5 s).

### The engine's sol-2 morning (W; quasi-steady balance, K 14, ten 1 m² mirrors at 0.85)
| clock | elev ° | DNI W/m² | air °C | on the aperture W | Th °C | P W |
|---|---|---|---|---|---|---|
| 07:30 | 5.7 | 37 | -61.5 | 271 | -46 | 4.9 |
| 08:00 | 11.1 | 128 | -57.8 | 954 | -7 | 48 |
| 08:30 | 16.1 | 202 | -53.9 | 1,535 | 24 | 101 |
| 09:00 | 20.8 | 254 | -50.0 | 1,964 | 47 | 144 |
| 10:00 | 28.7 | 315 | -42.2 | 2,507 | 76 | 195 |
| 11:00 | 34.0 | 343 | -35.0 | 2,782 | 91 | 214 |
| 12:00 | 35.9 | 352 | -28.8 | 2,866 | 99 | 214 |
Time-stepped from the dawn air the engine lags this by about 6 K: 130 W at 09:00, peak 213.9 W at 11:38, 1.348 kWh at the shaft and 1.077 kWh of charge over the sol.

### The fill (W, frictionless; redo with #269's coefficients)
The pair (I 0.596 kg·m²) at rest at dawn. Spin-up to the cut-in: ∫P dt = ½Iω_c² = 7,350 J → cut-in 07:49:41 (73,344 s). Charging at the balance: η∫P dt = 90,000 J → full 08:29:16 (75,783 s), P 82 W then (rotor 1,543 rpm, 0.52 N·m, 67 W of charge); at the 08:15 wake 16.5 ± 3 Wh at about 51 W. With friction τ_f and windage: P_gen = P - τ_f ω - P_wind(ω); t_full - t_cut = 90,000 / (η · mean P_gen); the loaded point exists only while P > τ_f(ω_c)·ω_c (at 08:00 that is 48 W, so #269's losses at 157 rad/s must be under that).

### Two branches for `cut-in`, decided by #269's free speed
| | free speed over 1,500 rpm | free speed under 1,500 rpm |
|---|---|---|
| sol 1 noon, bank -63 °C, open circuit | the pair races: cut-in at about 410 s; (frictionless: clamp at about 660 s) | settles under the cut-in |
| 19:27 sol 1 | frictionless: a 22 kJ dump (4.9 Wh); with #269: at rest by night | nothing |
| `route.stirling.cut-in` | about 410 s | about 73,344 s |
The test accepts either and reads which from the trace.

### What each wake checks (W unless marked)
| wake | s | engine | motor and bank | routes |
|---|---|---|---|---|
| 19:30 sol 1 | 27,750 | shaft-power 0, hot end within 2 K of the air | cells 0-2 °C (M 0.1 at 26,857), charge 0 | started = engine about 3-4 s, heat, wired about 4 s, warm 26,857 ± 10 |
| 08:15 sol 2 | 74,904 | P about 64 W, hot end about 5 °C | 16.5 ± 3 Wh, motor about 1,530 rpm, wire-1.power about 51 W, motor.held 0, cells [V] | cut-in per branch |
| live to full | about 75,783 | | slope = 0.8·shaft-power ± 5 W; full 08:29 ± 5 min | full about 75,780 ± 300 |
| noon sol 2 | 88,775 | 214 ± 3 W (less #269's loss), hot end 98.8 ± 1, air -28.8 | 25.000 Wh, open circuit, the pair at its free speed | |
| 17:05 sol 2 | 107,600 | about 0 | 25.000 Wh, cells [V] | |
| 02:54 sol 3 | 143,900 | 0 | 25.000 Wh, cells [V] in 0-45, won 0 | |
| the pass | 144,259.4 | | first won frame ≤ 144,270, none before; sol 3, 03:00-03:01 | the-call about 144,260; route.windmill.started/windmill/gear-up/cut-in/wired = -1 |
Log lines: `Route started: The hot-air engineer ...` sol 1 12:00; back in range sol 1 19:27; 100 % charged sol 2 08:29; the win line; `ending source stirling: 25.000 Wh`, `sources 1`; never `Route started: The windmiller`. Under #269(b) the `[sleep] Machines paused: generators charge at their last steady rate` line must not name `motor`. Wall time about 10 min.

### The bank's cells
Sol 1 (M): -63 at noon, 0.1 at 19:27, 8.9 at 20:54, 24.5 at 02:49 (rock 92, vault 27.2, lid 37 %). Sol 2 to 03:00 sol 3: [V]. The lid shuts at 40 °C vault air; the mirror lands on the rock through the shut lid (Mirror.Delivered is geometric; no lid factor in HeatInput) and the rock has no cap. Anchor: the director's solve ends sol 2's night at 39.7 °C (M). **Run V** measures it first: lonely-rover-e2e, no build, `sleep until scene.elapsed above T paused; wait 1200` at 27,750; 70,300; 74,904; 77,700; 88,775; 99,900; 107,600; 122,000; 133,100; 143,900, quit 144,400, traces battery-bank and vault (cells, rock, vault, bin.open) at dt 10, about 5 min. W asserts its cells equal V's to 0.2 K at every wake.

## 3. Fallback (F): no dependence on #269's details
The #220 Run-B shape: the engine built `#:load 2` and not joined; `HEROIC_SET "built-1:eng load 0 77678; built-1:eng load 2 78950"` (settings only at wake times).
```
... build done
sleep until scene.elapsed above 77678 limit 80000 paused        ; 09:00 sol 2; load 0 lands at the wake
join built-1.eng motors.rotor; join motors.motor battery-bank.bank
sleep until bank.full above 0.5 limit 3600 live                 ; worked: cut-in +48 s, full +859 s (09:14:19, 78,537 s)
sleep until scene.elapsed above 78950 limit 2000 live           ; load 2 lands at this wake
sleep until bank.won above 0.5 limit 66000 paused
```
At 09:00 P 130 W (hot end 41 °C), the engine at 65 rad/s on its 2 N·m, evened into the pair 55 rad/s; cut-in +48 s; full +859 s at 148 W (118 W of charge). Through the day sleep the engine turns at P/2 and stops at night; the motor is held at its SettledPower but the bank is full, so nothing is charged. About 10 min of wall time.

## 4. Tests
- `racket/heroic/tests/stirling-win-test.rkt` (new; the route's proven-by): runs W, V and R (the windmill-only reveal: e2e-route-test's build at (180, 105), joins pinion-e to the rotor and the motor to the bank, waitsim 60, quit 70, links trace dt 1). Checks: the build and joins accepted; V's table and its cells in 0-45 at 143,900; W's wakes; the fill; the call; the routes (route.stirling.* as the table, route.windmill.* -1 but the shared steps, no windmiller line); R's routes (route.windmill.started 3-10 s, every route.stirling.* -1). About 10 min. F behind HEROIC_STIRLING_FALLBACK=1.
- `tests/HeroicInventions.Sim.Tests/RoutesTests.cs`: helpers `Gen(..., source = "wind")`, `Eng(heatW, hotC, shaftW)`, `Built((source, ratio))`; cases: the Stirling route loads with its steps and proof; a Stirling-only sequence reveals only it; a windmill-only sequence leaves it hidden and unnamed; a mixed sequence reveals both (the windmill's wired DONE EARLIER after the motor is re-joined); a condition naming a prime mover reads only that kind; parser rows; the engine's reason numbers; a real runtime reading (StirlingShaftLinkTests' Linked world: Source stirling, Ratio 1, Engines heated); EveryShippedRouteNamesATestThatExists gains the second route.

## 5. The director
`solve.steps` does not change. A second steps file `game/routes/lonely-rover-opening/stirling.steps` is optional and not in #232's done-when.

## 6. Docs
`docs/e2e-route.md` (the Stirling section gains "The win (#232)"; Not done; fun-check Sols), `docs/conventions.md` (conditions may name a prime mover; the new kinds and numbers; the waiting-routes line), windmill.route header, `docs/lonely-rover.html` (it wins at 03:00 sol 3 in the route-check world), `docs/plans/stirling-route-plan.md` section 9 item 3 points here.

## 7. Risks and owner questions
1. The vault on sol 2 is the one thing that can stop the win (V decides). If the cells pass 45 °C at 03:00 sol 3, the honest fixes belong to #219; a stand-in (`HEROIC_SET "heliostat area 0 70237"`) would be the owner's call, not the test's.
2. #269's coefficients: the losses at 157 rad/s must stay under the morning's power (48 W at 08:00) or the motor never loads; the free speed decides when cut-in is first met.
3. Names and words (the owner's): "The hot-air engineer", the step titles, the cut-in reason's gearing sentence, whether a "hot enough" step is wanted.
4. `train-ratio-at-least` taking a source changes BuiltTrains to (source, ratio) pairs: a test-helper edit, no route number moves.
5. Shared steps' quiet times: route.windmill.warm and .full carry times in a Stirling-only run while route.windmill.started is -1; say so in the trace docs.
6. A mixed run in the game would need the motor's shaft re-joined from the pinion to the engine; the gesture may refuse a second shaft on a rotor (no unjoin word); the mixed case is met at the reading level, not in the game.
7. Wall time: the new Racket test is about 10 min; V could leave the suite once its numbers are in the header.
8. `wait N` counts frames (1/120 s headless); use trace dt 5 if a window needs more rows.

## Files
`src/HeroicInventions.Sim/Game/Routes.cs`, `game/routes/stirling.route` (new), `game/routes/windmill.route`, `racket/heroic/tests/stirling-win-test.rkt` (new), `tests/HeroicInventions.Sim.Tests/RoutesTests.cs`, `docs/plans/stirling-win-plan.md`, `docs/e2e-route.md`, `docs/conventions.md`, `docs/lonely-rover.html`, `docs/plans/stirling-route-plan.md`. Untouched: RouteReveal.cs, Main.Routes.cs, solve.steps, e2e-route-test.rkt, every physics file.
