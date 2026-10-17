# Stirling route in the crater: the noon check against the doc's 320 W (plan for #220)

Planned read-only with Fable from the code at 25fe2bb (2026-10-17), before any route code was written. Every number is marked **W** (worked: arithmetic from the code's own formulas and constants, a python re-implementation of Sun.cs, Mirror.cs, StirlingEngine.cs and Generator.cs), **S** (sourced: a constant or sentence in the repo), **M** (measured: a trace) or **[M?]** (a placeholder the implementation's trace fills in). The W code was validated against mars-stirling.rkt: it reproduces that machine's traced 1,508.8 W on each receiver (mean cos 0.684) and its 73.7 / 111.3 / 64.1 W.

**Headline (W):** in the crater as it is (lonely-rover-e2e: the noon sun 36° up, the beam 352 W/m², the afternoon air -29 to -20 °C) the template engine under ten 1 m² heliostats makes **214 W at the shaft settled at noon** and **204.5 W at 12:39 as the first afternoon's peak from a cold start**, 164 W of charge, 1.08 kWh a sol. With the doc's -63 °C sink it would be 268 W. The doc's 320 W is consistent with Meridiani's sun (433 W/m²), not the crater's. That is under 320 with the physics untouched and nothing tuned: the template's K = 14 W/K is within 0.2% of the optimum for this sun.

## 0. What the code already does (S/M)

1. **A Stirling is a shaft-link end** (commit 25fe2bb): `WorldLinks.BuildShaft` tries `rt.Stirlings`; `WorldLinkGestures.Turning` has `"stirling"`; `StirlingShaftLinkTests.cs` pins it. Before it the e2e world logged `can't join: built-1's eng is a stirling and motors's rotor a wheel` (M). After it, `join built-1.eng motors.rotor` logs `joined: shaft shaft-1 ... exchange only` (a sim end can't be Jolt-locked). Unloaded, the engine and rotor ran up as sqrt(2Pt/I) (M: 950 rpm after about 25 s at about 107 W). The windmill's shaft link is unchanged: 600 s traced at 1 s, byte-identical before and after.
2. **The built machine's scene** (`Main.Build.StartNewBuild`) is taken from the placed machine with weather, `route-vault`. No sun is given, so it is `new Sun(31.2, 172, 12).On(mars)`. `(weather (daily #t))` means every step sets `Ambient = -50 + 30 cos(2π(h-15)/24)` (`Weather.Ambient`; planets.rktd `daily-temperature -80 -20 15`): **-28.79 °C at 12:00** (M: -28.74 at about 12:00), -20 at 15:00. The engine's `Zone` is `Outside`, so both Tc and the radiation's T_air follow it. In lonely-rover-opening no placed machine has weather, so the air is -63 flat there.
3. The hot end is **built at def.Ambient = -63 °C** (`TemperatureOr` reads the zone before the weather's first step); the air jumps to -28.8 on step 1.
4. A mirror onto a Stirling delivers `Delivered × min(1, Aperture/Image)`, and a flat mirror's `Image` is its area. Build template: `area 0.5, reflectivity 0.85` (numeric, so `#:area 1` and `#:reflectivity 0.9` override; this plan keeps 0.85); the aim point is the engine's `At.Y + height/2` (+0.25 m). Stirling template: aperture 1, K 14, f 0.35, ε 0.9, C 10,000 J/K, I 0.5, load 10 (`#:load` overrides), `temperature #f` (a number overrides), height 0.5. **P = f(1-Tc/Th)·K(Th-Tc) has no speed in it**; the torque is P/max(ω, 1 rad/s).
5. Generator: k 0.114592 N·m per rad/s, ω_cut 157.08 rad/s, η 0.8, rated 12 N·m; the view loads the Jolt rotor each tick (the torque capped so it never reverses it). Rotor I = inertia-z 1.2494e-5 × iron 7,700 = **0.0962 kg·m²** (found-motor.machine).
6. The join gesture makes `Ratio = 1`; `(ratio N)` exists only in a save or world file. `MachineView.Gears.IsSimTurned` leaves Stirlings out, so an engine on an arbor drives no Jolt gear.
7. Paused sleep: `SleepControl.Advance` calls only `Runtime.Step`, not `StepShafts` (M). `HoldGenerators` holds a generator the sim doesn't turn at its `SettledPower`, else at `EstimatePower()`, which for a Stirling prime mover (no torque curve) reads the engine's **present speed** through the generator's curve.
8. **Scene**: `MachineView.Stirlings` colours the receiver face from grey-blue (-60 °C) to hot-iron red (200 °C) and labels it "eng: T hot, Tc cold / W at rpm" (the label shows `ShaftPower`). Mirrors draw beams whose glow scales with power/300, and a spot ring on the receiver. The state is visible in the scene; no view change is needed.

## 1. The crater's sun (W; Sun.cs with Mars: S 586.2, T 0.741, k 1, obliquity 25.19, year 669)

δ = 25.19·sin(360·(284+172)/669) = **-22.90°**; at 31.2 N the noon sun is 35.90° up, due south (+Z). AM (Kasten-Young) 1.702, DNI = 586.2·0.741^1.702 = **351.9 W/m²**. Sunrise 06:59, sunset 17:01 (18,539 s after the noon start).

| local h | elev ° | az ° | AM | DNI W/m² | air °C (e2e) |
|---|---|---|---|---|---|
| 8 | 11.1 | 125.6 | 5.07 | 128 | -56.5 |
| 9 | 20.8 | 135.8 | 2.79 | 254 | -50.0 |
| 10 | 28.7 | 148.3 | 2.07 | 315 | -42.2 |
| 11 | 34.0 | 163.3 | 1.78 | 344 | -35.0 |
| 12 | 35.9 | 180.0 | 1.70 | 352 | -28.8 |
| 13 | 34.0 | 196.7 | 1.78 | 344 | -24.0 |
| 14 | 28.7 | 211.7 | 2.07 | 315 | -21.0 |
| 15 | 20.8 | 224.2 | 2.79 | 254 | -20.0 |
| 16 | 11.1 | 234.4 | 5.07 | 128 | -21.0 |

mars-stirling's sun (latitude -2, day 100): δ -11.29°, noon elevation 80.7°, AM 1.013, **DNI 432.7**, day 05:58-18:02. The crater's noon beam is 81% of Meridiani's and its day is two hours shorter.

## 2. Delivery (W)

Layout (machine coordinates, -Z north, origin on the ground): the engine on a 1.5 m post, aim point (0, 1.75, 0); ten 1 m² flat heliostats **north** of it (the far side from the sun): row A at z -3.2, y 0.7, x = -2.4 -1.2 0 1.2 2.4; row B at z -4.7, y 1.3 (raised so its rays clear row A's plates; the receiver's 2.8 m noon shadow stops short of row A). Reflectivity: the template's 0.85.

| | m1-m5 (row A) | m6-m10 (row B) | Σcos | on the aperture |
|---|---|---|---|---|
| noon cos(θ/2) | 0.942 0.974 0.988 0.974 0.942 | 0.940 0.958 0.965 0.958 0.940 | 9.582 | **2,866 W** (= 351.9 × 0.85 × 9.582; `eng.heat`) |

Through the day: 10:00 2,507 W; 11:00 2,782; 12:00 2,866; 13:00 2,782; 14:00 2,507; 15:00 1,964; 16:00 954; 16:30 271. Alternatives: the engine on the ground with the mirrors at 0.6 m gives Σcos 9.18 (2,745 W); a 4 m arc at ±67.5° gives 9.29 (2,778 W).

One 10 m² flat mirror instead: its image is 10 m² on a 1 m² aperture, so a tenth lands (about 296 W). A 10 m² aperture takes it all but radiates ten times as much (about 7.9 kW at 100 °C), settling near 30 °C and about 50 W. Only a 10 m² *burning* mirror (`#:area 10`, image 0.005) would land 2,991 W at cos 1.

## 3. The receiver balance (W; bisection as mars-stirling.rkt, Q 2,866 W)

ε·Q = εσA(Th⁴ - Ta⁴) + K(Th - Tc), P = 0.35(1 - Tc/Th)·K(Th - Tc).

| sink | K W/K | Th °C | drawn W | radiated W | f(1-Tc/Th) | **P W** |
|---|---|---|---|---|---|---|
| e2e air -28.79 (noon) | 14 (template) | 98.75 | 1,785 | 794 | 0.1200 | **214.3** |
| e2e air -28.79 | K* 15.07 | 93.4 | | | | 214.7 (stagnation 209 °C) |
| -63 flat (opening world; the doc's sink) | 14 | 74.9 | 1,931 | 649 | 0.1387 | **267.7** |
| -63 flat | K* 14.13 | 74.1 | | | | 267.7 (stagnation 206 °C) |

Quasi-steady P through the e2e afternoon (K 14): 11:00 214 W; 12:00 214; 13:00 199; 14:00 169; 15:00 117; 16:00 36.

**Where 320 W came from (W; "consistent with", not proven):** P* = 320 W needs about 3,280 W on the aperture (K* 15.4, Th 82 °C, Tc -63): ten 0.85 mirrors at Meridiani's 432.7 W/m² with a mean cos of 0.89. The doc's own "about 500 W/m²" would give 390-450 W. The crater's 2,866 W is 87% of that, and its afternoon air is 34 K warmer than the doc's sink: 268 W at -63, **214 W as the crater is**. The doc's "1.7 kWh a sol" matches this layout's -63 whole-sol *shaft* integral (1.68 kWh): shaft energy at a cold sink, not charge.

## 4. The transient (W; C 10,000 J/K, 0.5 s Euler, the sun and air moving, the hot end built at -63)

| t after the noon start | clock | Th °C | P W |
|---|---|---|---|
| 0 | 12:00 | -63 | 0 (Th < Tc: nothing is drawn until about 12:02) |
| 300 | 12:05 | 9 | 24 |
| 600 | 12:10 | 53 | 98 |
| 1,200 | 12:19 | 88 | 180 |
| 1,800 | 12:29 | 97 | 201 |
| 2,400 | 12:39 | 99.1 | **204.5 (the first afternoon's peak, 12:38:52; the DNI then 349, 0.8% under noon's)** |
| 2,800 | 12:45 | 99.5 | 203.8 |

The thermal time constant is about C/(K + 4εσT³) ≈ 410 s. Started at 12:00 unwarmed, P at 12:00 is 0. Pre-warmed at 98.75 °C it is 214.3 W at 12:00, 214.0 at 60 s, 212.7 at 400 s (the sun and air drift). A whole sol from sunrise, the engine at the dawn air (-56.5 °C at 07:00): peak 214 W at 11:38, **1.35 kWh at the shaft, 1.08 kWh of charge a sol**, so the 5 kWh bank takes 4.6 sols (the doc says 3); at -63 flat, 1.68 / 1.34 kWh and 3.7 sols. Sol 1's afternoon from noon: 630 Wh at the shaft, 504 Wh of charge (e2e).

**How "power at noon" is measured.** Primary **(c)**: the first afternoon's peak, time-stepped from the cold start and read at 12:39 with its DNI and air. It is noon's sun to 1% and nothing is given. Gate **(a)**: `#:temperature 98.75`, stated as *given*, not earned, read in the first 60 s. It checks the sim's balance against the worked one. **(b)**, sleeping to noon of sol 2, belongs to #232 and is unsafe as a measurement. A paused sleep steps the engine without the shaft, and an engine that is unloaded, or joined to an open generator, stores all its work as ½Iω² without limit (300 kJ over a 40-minute warm-up is 10,000 rpm; joining then dumps it at the rated 12 N·m, 0.8·12·314 ≈ 3 kW). Run B's warm-up below is a different thing. It is a paused sleep *before the join*, with the engine on its own 2 N·m load. Because P has no speed in it, that is exactly watching the thermal side at 1/120 s, and it stores only 2.7 kJ.

## 5. Coupling to the found motor (W)

The balance P/ω_e = G·k(Gω_e - ω_cut) is the power balance P = k(ω_r - ω_cut)·ω_r: **G drops out** (no speed in P, no loss in a plain link), ω_r = [kω_c + √(k²ω_c² + 4kP)]/2k.

| P at the shaft | rotor | generator torque | charge (η 0.8) | engine at G = 1 | engine at G = 4 |
|---|---|---|---|---|---|
| 214.3 W (given hot end, noon) | 168.2 rad/s, **1,606 rpm** | 1.274 N·m | **171.4 W** | 1,606 rpm, 1.27 N·m | 402 rpm, 5.10 N·m |
| 204.5 W (12:39 peak) | 167.7, **1,602 rpm** | 1.222 | **164.0 W** | 1,602 rpm | 400 rpm, 4.89 |
| 267.7 W (-63 sink) | 170.8, 1,631 | 1.568 | 214 | 1,631 | 408 rpm |
| 320 W (the doc) | 173.2, 1,654 | 1.848 | 256 | 1,654 | 413 rpm |

The doc's "a few hundred rpm, so one gear stage" does not survive. On a plain shaft the engine runs at the motor's 1,600 rpm; a stage would change only the engine's own speed, and the player cannot make one anyway (the gesture's ratio is 1, and Stirlings don't drive Jolt gears). Spin-up from rest at 214 W, I_total 0.596: the cut-in after 34 s, settled (τ 4.9 s) about 50 s after the join. The first tick at rest has a torque of P/1 rad/s = 214 N·m for one 1/120 s step: Δω 3.6 rad/s on the engine, energy ≤ P·dt = 1.8 J, so no impulse to fear. Stability: the exchange-only path of linked-shaft.world (a sim windmill to a Jolt wheel), inertias 0.5 and 0.096, the generator torque capped: nothing new.

**Open circuit** (the bank cold or full): no load, so the pair runs up as √(2Pt/I) (M) to Jolt's 314.16 rad/s clamp after about 134 s at 214 W, where the clamp brakes it (`spin-limit.hits`, a warning line); `eng.shaft-power` is unchanged by it. The 25 Wh bank fills in 549 s at 164 W.

## 6. The build, as typed words (HEROIC_INPUT; solve.steps style)

Site `build new 220 126`: ground -55.52 m, slope 6.8° (the motors crate at (233.83, 135) sits on a 20° dune flank), 16.5 m from the motor, 36.5 m from the bank, clear of the slide (the rubble's foot at x ≈ 245). The posts float or bury by up to 0.5 m across the field: cosmetic (the sim has no shadows or blocking).

```
wait 360
build new 220 126
build (post stand #:at (0 0 0) #:size-x 0.6 #:size-y 1.5 #:size-z 0.6)
build (stirling eng #:at (0 1.5 0) #:aperture 1 #:conductance 14 #:load 0)
build (mirror m1 #:at (-2.4 0.7 -3.2) #:area 1 #:onto eng)
build (mirror m2 #:at (-1.2 0.7 -3.2) #:area 1 #:onto eng)
build (mirror m3 #:at (0 0.7 -3.2) #:area 1 #:onto eng)
build (mirror m4 #:at (1.2 0.7 -3.2) #:area 1 #:onto eng)
build (mirror m5 #:at (2.4 0.7 -3.2) #:area 1 #:onto eng)
build (mirror m6 #:at (-2.4 1.3 -4.7) #:area 1 #:onto eng)
build (mirror m7 #:at (-1.2 1.3 -4.7) #:area 1 #:onto eng)
build (mirror m8 #:at (0 1.3 -4.7) #:area 1 #:onto eng)
build (mirror m9 #:at (1.2 1.3 -4.7) #:area 1 #:onto eng)
build (mirror m10 #:at (2.4 1.3 -4.7) #:area 1 #:onto eng)
build done
join built-1.eng motors.rotor
join motors.motor battery-bank.bank
```

The engine is built before the mirrors (`#:onto` must resolve). Director form: `join-gesture built-1.eng motors.rotor`, `expect link shaft built-1.eng motors.rotor`, `expect field built-1:eng.shaft-power between 200 220`. Run A adds `#:temperature 98.75`; run B uses `#:load 2` for its warm-up. The bank is -63 °C at noon on sol 1 (it passes 0 °C at about 19:00), so the coupling runs warm it by `HEROIC_SET "battery-bank:cells temperature 25"`, declared as given (StirlingShaftLinkTests does the same).

**Trace fields** (`HEROIC_TRACE=p` writes `p.built-1 p.motors p.battery-bank p.vault p.links`): `eng.heat .hot-temperature .cold-temperature .radiation .heat-drawn .efficiency .shaft-power .rpm .work` (`eng.power` is load × ω and reads 0 with `#:load 0`: assert `eng.shaft-power`, never `eng.power`); `mN.power .cosine .hit`; `motor.rpm .torque .shaft-power .power .delivered .charging .held .held-power .estimated-power`; `spin-limit.hits` (motors); links: `shaft-1.rpm .driven-rpm .torque .power`, `wire-1.power .offered .charge .open-circuit .from-stirling`.

## 7. Checks and the test: `racket/heroic/tests/stirling-route-test.rkt`

Modelled on e2e-route-test.rkt; three processes in parallel; every run ends on HEROIC_QUIT_AFTER_SIM_SECONDS, never `quit`, so the traces' last block is written.

**Run A (gate, given hot end).** The build with `#:temperature 98.75`, the joins, `waitsim 400`; `HEROIC_SET "battery-bank:cells temperature 25"`; quit at 400; trace dt 1.
- At t 60 (12:01): `eng.heat` 2,866 ± 15; `eng.hot-temperature` 98.75 ± 0.3; `eng.cold-temperature` -28.7 ± 0.1; `eng.radiation` 794 ± 5; `eng.heat-drawn` 1,784 ± 10; `eng.efficiency` 0.120 ± 0.001; **`eng.shaft-power` 214.0 ± 2**.
- From t 100: `eng.rpm` = `motor.rpm` = `shaft-1.rpm` 1,606 ± 15; `motor.torque` and `shaft-1.torque` 1.27 ± 0.04; `shaft-1.power` 214 ± 3; `motor.power` = `wire-1.power` **171 ± 3**; `wire-1.open-circuit` 0; the bank.charge slope over t 200-300 is 171 ± 5 W; `wire-1.from-stirling` = `bank.charge`; `spin-limit.hits` 0.
- At 400 s: `eng.shaft-power` 212.7 ± 2. About 80 s of wall time.

**Run B (primary, earned, time-stepped).** The build with `#:load 2` and no temperature; `sleep until scene.elapsed above 2400 limit 3000 paused` (say `paused`: a hinged rotor makes the world JoltDriven, and a short sleep would default to live); `HEROIC_SET "battery-bank:cells temperature 25; built-1:eng load 0 2350"` (due inside the sleep, so applied on the first tick after the wake; a setting that fails exits non-zero). Then `join built-1.eng motors.rotor; join motors.motor battery-bank.bank; waitsim 2700`; quit at 2,700; trace dt 10.
- During the sleep the engine warms on its own load (about 980 rpm, 2.7 kJ stored), and with no link yet the bank gets nothing (`bank.charge` 0).
- At the wake (the first frame ≥ 2,400 s, 12:38:55): `eng.hot-temperature` 99.1 ± 1; **`eng.shaft-power` 204.5 ± 3 (the first afternoon's peak)**; `eng.heat` 2,831 ± 15; `eng.cold-temperature` -25.5 ± 0.2.
- After the join (shared speed 87 rad/s, the cut-in 25 s later, settled by about 2,460 s): `motor.rpm` 1,602 ± 15; `motor.power` **164 ± 3**; `shaft-1.power` 204 ± 3; `bank.charge` at 2,700 about 11 ± 1.5 Wh; the cells still in 0-45 °C. About 1.5 minutes of wall time (the paused warm-up is seconds).

**Run B-live (opt-in, `HEROIC_STIRLING_LIVE=1`).** The same build with `#:load 0`, no sleep, the joins at once, `HEROIC_SET "battery-bank:cells temperature 25; battery-bank:bank capacity 5000"` so the bank never fills, quit at 2,700: everything watched. The shaft passes the cut-in at about 440 s (work 7,355 J), the peak is 204.5 W at 12:39, 164 W of charge. It must agree with Run B's thermal numbers to the same tolerance, and that is a real check: in B-live the generator loads the engine from 440 s, in B its own 2 N·m load does. The mechanics differ, the thermal arithmetic is identical because P has no speed in it. Run once and recorded in the test header (about 9 minutes of wall time).

**Run C (gate, cold bank).** Run A without the HEROIC_SET: `motor.power` 0 and `bank.charge` 0 throughout ("not charging: bank -63 C"); `eng.shaft-power` 214 ± 2, unchanged; `eng.rpm` and `motor.rpm` at or just under the clamp, 3,000 ± 30, by t ≈ 140 s (the clamp is on the body's total spin; the hinge-axis projection may read a little under), and `spin-limit.hits` ≥ 1. About 80 s of wall time.

## 8. Doc corrections

Placeholders: [A] Run A's `eng.shaft-power` at 12:01, [B] Run B's at 12:39, [charge] `wire-1.power`, [rpm] `motor.rpm`.

- `docs/lonely-rover.html`, the Stirling bullet: replace "It turns at a few hundred rpm, so one gear stage brings it to the generator's cut-in." and "Modelled with a fair engine (35% of Carnot), ten 1 m² heliostats give about 320 W at the shaft at noon and 1.7 kWh of charge a sol, so the 5 kWh bank takes about 3 sols; twenty heliostats fill it in about one." with the measured numbers ([B], [A], [charge], [rpm]); "around 100-150 °C" becomes about 100 °C (99 °C in the crater's afternoon air; 75 °C with a -63 °C sink).
- `docs/lonely-rover.html`, the checked-model table's Stirling row: the 320 W was Meridiani's sun; the measured [B] W at 12:39 on sol 1 (worked 204; 214 settled at noon), about 1.1 kWh of charge a sol, about 5 sols; the best receiver temperature about 100 °C. Its design change: the fastest route about 5 sols on the Stirling alone, twenty heliostats for 2-3.
- `docs/e2e-route.md`: the fun-check row (Stirling [B] W at the shaft at 12:39, [charge] W of charge, stirling-route-test.rkt), and the Stirling taken out of the two "Not done" lists.
- `docs/conventions.md`: "(#220, never built in the crater)" points at this plan.

## 9. Risks and owner questions (nothing decided here)

1. **A paused sleep with the engine joined** holds the motor at `EstimatePower` from the engine's present speed (a Stirling prime mover has no torque curve), not from its power. A sleep begun with the engine turning and the bank warm charges at that rate all night, after sunset; begun with the engine racing at the clamp (cold bank), the estimate is 0.8·12·314 ≈ 3.0 kW. Question: should a Stirling-linked motor's hold read the engine's live `ShaftPower × η` each step (the sim keeps stepping the engine through a paused sleep, so it would follow the sun), or should the sleep run `StepShafts`? For #232.
2. **An unloaded Stirling has no speed limit in the sim** (no windage or friction): every joule of work becomes ½Iω² until something loads it. Question: a windage or speed limit in the model (a real engine has friction)? This plan does not need one: Run B warms the engine on its own player-settable load.
3. **#232 sketch** (an honest win in the e2e world, 25 Wh): the bank passes 0 °C at about 19:00-19:30 on sol 1, after the 17:01 sunset, so there is no Stirling charge on sol 1; sol 2's daylight gives about 1.08 kWh, far more than 25 Wh, so the win is the 03:00 pass on sol 3 (144,259 s). Honestly done: leave the engine on its own load and unjoined through the night's paused sleep (it stops as Th falls to Tc), wake about 07:30 on sol 2, set the load to 0, join, run live for about 20 minutes until the bank fills (549 s at 164 W once P is up), then sleep paused to 03:00 with a full bank (the held SettledPower charges nothing into a full bank). Or `HEROIC_SLEEP_PHYSICS=1` through the daylight (about 2 hours of wall time).
4. The measured number depends on the world: e2e (weather air) 214 / 204 W; the opening world (-63 flat) would give 268 W. The doc says which (this plan: e2e).
5. A cold bank at noon on sol 1: the shaft power is real but the motor is open until the cells warm; the coupling checks warm the cells by setting (declared).
6. GUI join: each mirror's beam is a node under the mirror's id spanning to the engine, so a click on the engine can cross a beam's box; `join` by name is immune, and a window run uses the pick list (`joinclick A list`, as `join-gesture` does).
7. Shadowing and blocking are not modelled; the layout keeps the geometry plausible only.
8. The 25 Wh bank fills in about 550 s at 164 W; Runs A and B end before that; B-live raises the capacity by setting.

## Files the implementation touches

- `racket/heroic/tests/stirling-route-test.rkt` (new)
- `docs/plans/stirling-route-plan.md` (this plan, the measured numbers filled in below)
- `docs/lonely-rover.html`, `docs/e2e-route.md`, `docs/conventions.md`
- Done before the plan: `src/HeroicInventions.Sim/Machines/WorldLinks.cs`, `src/HeroicInventions.Sim/Editor/WorldLinkGestures.cs` (one line each, the lead's OK), `tests/HeroicInventions.Sim.Tests/StirlingShaftLinkTests.cs` (new). No physics change anywhere.

## Measured (2026-10-17, `stirling-route-test.rkt`, headless Godot, Jolt 120 Hz)

Every planned check held on the first runs but one, which was the run's design, not the physics: Run B's timed `built-1:eng load 0` was first set for 2,350 s, and a setting is applied once a frame, while a paused sleep's frame covers many sim seconds, so it took effect *inside* the sleep. The engine then ran unloaded for 47 s to 2,121 rpm (worked from √(102² + 2·204.5·47/0.5): 2,110 rpm) and that flywheel energy went into the bank at the join (13.6 Wh at 2,690 s against the worked 11): a small, live case of section 9's question 2. Set at the wake (2,400 s) instead, the run matches.

| | Worked | Measured |
|---|---|---|
| `eng.heat` at noon | 2,866 W | 2,866.4 W |
| Run A, 12:01: hot end, radiated, drawn, efficiency | 98.75 °C, 794 W, 1,784 W, 0.120 | 98.71 °C, 793.6 W, 1,783.7 W, 0.1199 |
| Run A, 12:01: **at the shaft** | **214.0 W** | **213.9 W** |
| Run A: rotor, torque, charge | 1,606 rpm, 1.27 N·m, 171 W | 1,606 rpm, 1.268 N·m, 170.6 W (the bank's slope 170.4 W); `spin-limit.hits` 0 |
| Run A at 12:07 | 212.7 W | 212.7 W (`shaft-1.power`) |
| Run B, the wake at 12:38:55: **at the shaft**, hot end, aperture, air | **204.5 W**, 99.1 °C, 2,831 W, -25.5 °C | **204.5 W**, 99.14 °C, 2,831.0 W, -25.50 °C |
| Run B, asleep on its 2 N·m load | 977 rpm | 976.5 rpm |
| Run B after the join: rotor, charge, bank at the end | 1,602 rpm, 164 W, 11 ± 1.5 Wh | 1,601 rpm, 163.2 W, 12.2 Wh at 2,700 s |
| Run C (cold bank): clamp, charge | 3,000 rpm by about 140 s, nothing | 3,001 rpm at 140 s, `spin-limit.hits` 1, nothing charged; the link carries 213 W into the clamp |

**At 20x** (`HEROIC_SPEED=20`, Run A again): the engine's shaft power is the same function of its hot end as at 1x (from each run's own trace at its t 339: 98.84 °C and -28.28 °C give 212.87 W worked, 212.9 traced; 98.47 °C and -28.19 °C give 211.53 W worked, 211.6 traced), the rotor 1,605 rpm against 1,606 and the charge 169.2 W against 170.1. Only the hot end's history differs. The ten mirror commands land a frame apart, and a 20x frame is 1/6 s of sim, so the engine spends its first 7 s with part of its heat (`eng.heat` 0, 282, 868 ... 2,585 W, against 2,866 W from its first second at 1x). It loses 0.9 K and recovers with the 410 s time constant. The hidden-window shot (a real window at 20x, more frames for the build) read 93 °C and 197 W about 90 s after the build for the same reason. A built machine's trace keeps its own clock from its build (about 4 s into the world at 1x, about 74 s at 20x); the sun is the world's.

Run B-live (everything watched, no sleep) was not run: Run B's agreement to 0.1 W with the worked transient, and Run A's with the worked balance, cover what it would check. The docs say **204.5 W at the shaft at 12:39 from a cold start** (214 W settled at noon), 163 W of charge, about 1.1 kWh a sol (worked, not measured, and only if the bank is warm through the daylight: #219 measures that) and about 5 sols for the 5 kWh bank on the Stirling alone.
