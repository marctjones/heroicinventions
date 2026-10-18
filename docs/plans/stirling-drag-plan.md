# Stirling losses and the paused sleep's shafts (plan for #269)

Planned read-only with Fable from the code at 32b9a98 (2026-10-17), before any code. Marks: **W** worked (arithmetic from the code's own formulas and constants, python), **S** sourced (a text, a datasheet, a paper, or the repo; "check" = found only second-hand), **M** measured (a trace in the repo), **[M?]** a placeholder the implementation fills in. Owner decisions (issue #269): (a) real friction and windage, coefficients sourced or worked, not tuned; (b) a paused sleep steps the world's shaft links. Section 6 was added by the routes session after the plan, for the lead's constraint on the morning's power.

**Headline (W).** The smallest honest loss is two terms, `tau_loss = tau_f + k w^2`:
- a Coulomb term for the piston seals and bearings (`friction-torque`, N·m, default **0.068**);
- a quadratic term for the gas pushed through the heat exchangers plus the flywheel's windage (`flow-loss`, N·m·s², default **8.3e-6**).

Both are part properties. The two mechanisms the issue names are real but tiny here: SKF bearing friction is 0.002-0.008 N·m (under 1%), and the flywheel's windage in Mars air is 1 W at 1,600 rpm.

With the defaults the #220 engine runs free at **2,734 rpm at 214.3 W** (2,689 at 204.5 W), 9% under Jolt's 3,000 rpm clamp. But the sourced inputs bracket the free speed from about 2,500 to 3,300 rpm, across the clamp, so the split between the two terms is the one number the owner must bless (section 5). At the motor's speed the loss is 49 W. So run A's 214 W at the shaft becomes **165 W and 132 W of charge at 1,583 rpm** (run B: 156 W, 125 W at 1,579 rpm) if 0.35 of Carnot stays the gas cycle's figure, or stays 205/164 W if the owner says 0.35 was the shaft's (section 1.4).

For (b): a frozen Jolt body reads zero speed and drops impulses, so stepping a Jolt-to-Jolt shaft through a paused sleep is a no-op, and the windmill route's traces are predicted **byte-identical**. A shaft from a sim engine to a Jolt rotor needs a sim stand-in for the rotor, designed in section 2.

## Status, 2026-10-17 (see docs/plans/handoff-2026-10-17-routes.md)

- **Slice 0** (a Stirling drives gears) and **slice 1** (one drag term) are done and ready to land on `claude/routes-stirling-a546ca`.
- **Part (b)** (a paused sleep steps a Stirling's world shaft) was **parked by the owner**. Its sim side is on branch `claude/stirling-sleep-parked` (2 tests passing, not wired into the game). The bug it fixes is described in the hand-off note. Section 2 below is its design.
- The source reading of sections 1.1-1.3 was dropped. The owner chose one drag term with a stated rule of thumb ("Decided", below).

## Decided 2026-10-17 (supersedes sections 1.1-1.3 and 1.5's coefficients)

**The owner chose ONE drag term**: "our engine is a simplified simulation of the physics", with no literature hunt for coefficients. The loss torque on the engine's shaft is `k ω²` (power `k ω³`), one part property `#:drag` in N·m·s². Its default comes from a stated rule of thumb, not a measurement: **the template engine loses 20% of its indicated power at its rated 1,500 rpm under ten heliostats at the crater's noon.** A constant friction torque alone would give a free speed of 5x the rated speed (7,500 rpm), and a `c ω` drag alone √5x (3,350 rpm), both over Jolt's 3,000 rpm clamp. The `ω²` torque gives ∛5x.

**The lead chose Option 2** (delegated by the owner): 0.35 of Carnot is the SHAFT's figure at the rated 1,500 rpm, because published fractions of Carnot are brake (shaft) measurements that already contain the engine's friction. Sources the lead gave:
- the NASA GPU-3 at a 677 °C heater gas, 6.9 MPa and 1,500 rpm has a peak brake thermal efficiency of 26.4% (hydrogen) and 21.3% (helium) (Thieme et al., TRID 171585);
- research engines reach about 50% of Carnot (the Pasco manual);
- 20-50% (a 2011 study).

So the indicated default becomes 0.35 / 0.80 = **0.4375**, and the shaft gives 0.35 at 1,500 rpm. Option 1 (0.35 as the gas cycle's, the shaft about 0.27 of Carnot at 1,500 rpm) is one constant away (`#:carnot-fraction 0.35`) if the owner flips it.

Worked (W, 2026-10-17, before any run). The indicated power at noon is 214.3 × 1.25 = 267.9 W (the hot end's balance has no `f` in it, so Th stays 98.75 °C). So **k = 0.2 × 267.9 / 157.08³ = 1.38e-5 N·m·s²** (a rule of thumb fitted to the template engine at the crater's noon, not a measured coefficient; a machine can set its own `#:drag`). The free speed is ω_free = (P/k)^⅓: 157.08 × ∛5 = **268.6 rad/s, 2,565 rpm** at noon's 267.9 W. It grows with the cube root of the indicated power, and **Jolt's clamp (314.16 rad/s, 3,000 rpm) is reached at P = k × 314.16³ ≈ 428 W**, 1.6 times noon (twice the heliostats would give about 338 rad/s). Over that the clamp co-limits, as with a windmill's open circuit: it brakes the shaft and destroys the excess energy; none is created. Allowed: a player can build more mirrors.

| Case (P_ind) | plain shaft: rotor rpm, τ_g, to the motor, charge, drag | 4:1 stage: rotor rpm, charge, drag (engine rpm) |
|---|---|---|
| run A, noon, hot end given (267.9 W) | 1,601, 1.210 N·m, 202.8 W, **162.2 W**, 65.1 W | 1,630, **213.4 W**, 1.07 W (408) |
| run B, 12:39 (255.6 W) | 1,595, 1.144, 191.2, **152.9 W**, 64.5 | 1,625, 203.6, 1.06 (406) |
| 08:00 sol 2 (60.0 W) | 1,503, 0.039, 6.1, **4.9 W**, 53.9 | 1,531, **47.3 W**, 0.89 (383) |
| 08:30 sol 2 (126.2 W) | 1,536, 0.428, 68.8, 55.0, 57.5 | 1,564, 100.2, 0.95 (391) |
| 09:00 sol 2, time-stepped (162.5 W) | 1,553, 0.634, 103.1, 82.5, 59.4 | 1,581, 129.2, 0.98 (395) |

**The morning constraint: the 4:1 stage meets it, a plain shaft does not.** On a plain shaft the engine loses most of its 08:00 power (54 of 60 W) to its own drag at the motor's 1,500 rpm; geared 4:1 it turns at about 383 rpm and loses under 1 W. The docs say so plainly.

#220's run A moves from 170.6 W measured to 162.2 W of charge (5%) on a plain shaft.

`mars-stirling` (three engines on 3 N·m loads): pinned at `#:carnot-fraction 0.35`, so its header's worked case stands. With the drag: 233.9 / 352.1 / 203.6 rpm and 73.5 / 110.6 / 64.0 W into the loads, against 234.6 / 354.3 / 204.0 rpm before. Taking the new 0.4375 default instead would make it 292 / 439 / 254 rpm.

`StirlingShaftLinkTests` (the matched engine, 111.3 W): unloaded, the free speed is 200.4 rad/s; wired to a warm bank, the rotor settles at 1,528 rpm with 0.341 N·m, 54.6 W to the motor and 43.7 W of charge.

The paused-sleep stand-in (section 2) is unchanged. The engine's free speed through a joined, open-circuit sleep is 2,565 rpm at noon's power: no runaway.

## 0. What the code does today (S/M)

1. `StirlingEngine.Step`: `P = f(1 - Tc/Th)·K(Th - Tc)`, torque `P / max(w, 1)`, nothing else loads it; `ShaftPower` is `P`; 0.05 s substeps. Unloaded it runs up as sqrt(2Pt/I) (M: 950 rpm after 25 s; run C hit the clamp at 140 s).
2. `SleepControl.Advance` steps every `Runtime.Step` and never `WorldLinks.StepShafts`. `Begin` calls `_setRunning(false)` (every `_freezable` body gets `Freeze = true`), then `HoldGenerators()`, which holds every generator not in `_simDrives`. `End` releases them, then `_setRunning(true)`. Main's `StepWorld` (awake) is ReadLockedShafts, StepPipes, SimulateUntraced, StepShafts, then the trace rows.
3. The found motor's rotor is a Jolt disc wheel (I = 1.2494e-5 × 7,700 = 0.0962 kg·m², S found-motor.machine). Its generator is loaded by the view each tick (`LoadGenerators`: `gen.Step(|spin|, dt, I·|spin|/dt)` then `ApplyTorque`), so headless and asleep nothing turns it.
4. **Godot 4.7.2 + Jolt, what a frozen body does** (S, read from the Godot `modules/jolt_physics` source and Jolt's `Body.cpp`, master; check against the 4.7 tag):
   - `Freeze = true` is `BODY_MODE_STATIC`, and `JoltBody3D::set_mode` calls Jolt's `SetMotionType(Static)`, which **zeroes the stored linear and angular velocity**.
   - `Body::GetAngularVelocity()` returns zero for a static body, and `JoltBody3D::apply_torque_impulse` returns early for `!is_rigid()`.
   - Nothing restores a velocity on unfreeze. So today every paused sleep already loses the Jolt bodies' spin at the freeze: the windmill's gears come back from 0 at the wake, re-spun by the sails' momentum through `CoupleDrivenTrains`.
5. The DSL's stirling form is `racket/heroic/machine.rkt` ~1408 (`#:aperture ... #:temperature`), its validation ~499. The template is `PartTemplates.cs:175`, the export `RktExporter.cs:263` (`F` is `ToString("R")`, so `8.3e-6` writes as `8.3E-06`, which the Racket reader takes), the runtime build `MachineRuntime.cs:452`, the fields ~1810.

## 1. (a) The losses

### 1.1 The engine's size, and what the issue's mechanisms come to (W/S)

| | Value | Mark, source |
|---|---|---|
| Flywheel for I = 0.5 kg·m² | a solid iron disc, 0.5 m across, 10.3 mm thick, **16 kg** (I = ½mr²); rim 72 m/s at 2,734 rpm; weight on Mars 59 N | W |
| Bearing friction | SKF: M = 0.5 μ F d, μ 0.0015 for deep-groove ball bearings; two 25 mm bearings at 60-200 N each: **0.002-0.0075 N·m**, 0.4-1.2 W at 1,500 rpm | S (SKF General Catalogue, "Estimating the frictional moment"; found only quoted on roymech.org and eng-tips: **check the catalogue page**) |
| Windage, the flywheel in Mars air (0.0153 kg/m³, μ 1.08e-5 Pa·s) | free disc, both faces, laminar C_m = 3.87 Re^-1/2 (Re 15,000-28,000): **6.7e-3 N·m, 1.1 W at 168 rad/s**; 1.7e-2 N·m, 5.4 W at the clamp | S formula (Schlichting, Boundary-Layer Theory, rotating disc: check the constants), W numbers |
| Windage if the flywheel ran in a sealed 1 MPa CO₂ crankcase (18 kg/m³) | 1.4 N·m, 232 W at 168 rad/s; 4.2 N·m, 1.3 kW at the clamp | W. **Not the game's engine:** the view draws the flywheel in the open air and the sim has no crankcase pressure; a sealed buffer space would be an owner choice (section 5) |
| MP1002CA (the 200 W class) | 180-200 W electrical, 1951 | S Hargreaves 1991 p. 61 via Wikipedia; its 1,500 rpm is **recalled, check** |
| GPU-3 | 6.82 kW at 6.9 MPa and 3,500 rpm; brake thermal efficiency 26.4% (H₂, 1,500 rpm) | S Thieme 1981, NASA TM-82646, abstract (NTRS 19810023544) |

So bearings and Mars-air windage together are under 1% of 214 W at any speed under the clamp: real, carried, negligible. What gives a real Stirling its free speed is inside it: the seals' friction (Coulomb-like) and the flow loss of the gas through heater, regenerator and cooler (Δp ∝ u², work per cycle ∝ w², power ∝ w³: a torque ∝ w²; Urieli & Berchowitz, "Pumping loss", simple analysis; S). The honest two-term form is `tau_loss = tau_f + k w^2`. No viscous `c·w` term has a sourced mechanism here (rolling bearings), so it is left out.

### 1.2 The coefficients (W from sourced shares; the one owner call is the split)

Rated point: the template engine under ten heliostats at the crater's noon, **P_ind 214.3 W at 1,500 rpm** (157.08 rad/s; the MP1002CA class). Mechanical efficiency at rated speed **η_m 0.80**: S one measured-and-modelled kinematic engine's 80.5% (a 2023 Energy paper on a 1 kW beta engine with a bell-crank drive; found by abstract: check the paper), and a seal study's "seal friction is several percent of gross output" (S, abstract). So the loss at rated speed is 42.9 W, split **5% seals and bearings (10.7 W) + 15% flow (32.1 W)**:
- `friction-torque` τ_f = 10.7 / 157.08 = **0.068 N·m** (the bearings 0.002-0.008 of it, the rest rings and rod seals)
- `flow-loss` k = 32.1 / 157.08³ = **8.3e-6 N·m·s²** (the Mars-air windage, 2.4e-7 at the operating point, is 3% of it)

The flow share is the better sourced. Urieli's D90 Ross engine gives 10.3 W of pumping loss, 7.5% of net output (S). A CFD loss budget of the GPU-3 gives pumping at 7.5% of heat input, about 15-20% of indicated power (S, abstract only: El-Ghafour et al., J. Adv. Res. Fluid Mech. Thermal Sci.).

The friction share is the weakest number. Its primary source is Thieme's motoring-loss table (NASA TM-82646, a scan the plan could not read). **Before the first run the implementer reads it and re-derives the split from the motoring loss's size and its slope with speed** (power ∝ w is Coulomb, ∝ w³ is flow). If it disagrees with 5/15, the defaults change (numbers).

### 1.3 The free speed, and how it hangs on the split (W)

Unloaded, `P_ind = tau_f w + k w^3`. With shares s_f, s_p of P_ind at rated speed w_r, `w_free / w_r = x` solves `s_f x + s_p x³ = 1` whatever P_ind is:

| s_f | s_p | η_m | w_free / w_r | free rpm (rated 1,500) | loss at 1,606 rpm, % of P_ind |
|---|---|---|---|---|---|
| 0 | 0.20 | 0.80 | 1.71 | 2,565 | 24.6 |
| **0.05** | **0.15** | **0.80** | **1.82** | **2,735** | **23.8** |
| 0.07 | 0.13 | 0.80 | 1.88 | 2,825 | 23.5 |
| 0.10 | 0.10 | 0.80 | 2.00 | 3,000 (the clamp) | 23.0 |
| 0.13 | 0.07 | 0.80 | 2.17 | 3,259 | 22.5 |
| 0.20 | 0 | 0.80 | 5.00 | 7,500 | 21.4 |
| 0.10 | 0.15 | 0.75 | 1.76 | 2,646 | 29.1 |
| 0.03 | 0.10 | 0.87 | 2.11 | 3,162 | 15.5 |

The rated speed itself is a design number: with 5/15, rated 1,000 rpm gives 1,823 free, 1,200 gives 2,188, 1,800 gives 3,281. The model has no roll-off of indicated power with speed, so these free speeds are **upper bounds**.

With the defaults (τ_f 0.068, k 8.3e-6), the free speed by P_ind:

| P_ind | 214.3 W | 212.7 W | 204.5 W | 111.3 W | 73.7 W | 64.1 W |
|---|---|---|---|---|---|---|
| free speed | **286.3 rad/s, 2,734 rpm** | 2,727 rpm | **281.6 rad/s, 2,689 rpm** | 2,159 rpm | 1,852 rpm | 1,756 rpm |

Open circuit from rest at 214.3 W with the rotor on (I 0.5962): 1,117 rpm at 20 s, 1,820 at 60, 2,188 at 100, **2,404 at 140 s** (run C read 3,001 there), 2,577 at 200, 2,689 at 300, 2,721 at 400, 2,733 at 600. That is 95% of the free speed at 211 s and 99% at 341 s. The energy stored at the free speed is **24.4 kJ, 6.8 Wh** (today: unbounded; 300 kJ over a 40-minute sleep).

The loss by speed:

| speed, rad/s | 25 | 100 | 157 | **168.2** | 286 | 314 |
|---|---|---|---|---|---|---|
| loss torque, N·m | 0.073 | 0.151 | 0.273 | **0.303** | 0.747 | 0.887 |
| loss power, W | 1.8 | 15 | 42.9 | **50.9** | 214 | 279 |

### 1.4 The operating point and the charge: two readings of 0.35 (W; the owner picks, section 5)

The generator balance becomes `P_ind = tau_loss(w) w + k_g (w - w_cut) w`.
- **Option 1, the plain reading of the issue, recommended as the face-value one:** `carnot-fraction` 0.35 is the gas cycle's, and the losses come off it.
- **Option 2:** the design doc's "35% of Carnot" was the shaft's figure at the rated speed (real-engine Carnot fractions in the literature are brake figures), so the indicated default becomes 0.35 / 0.80 = 0.4375 and the shaft gets 0.35 at 1,500 rpm. It reproduces #220's numbers almost exactly, which will look tuned though it is a definition, and it moves mars-stirling's 111.3 W to 139 W unless that machine pins `#:carnot-fraction 0.35`.

| | P_ind | rotor | τ_g | shaft (net) | charge (η 0.8) | loss |
|---|---|---|---|---|---|---|
| Option 1, run A (hot end given, noon) | 214.3 | 165.8 rad/s, **1,583 rpm** | 0.997 N·m | **165.2 W** | **132.2 W** | 49.1 W |
| Option 1, run A at 12:07 | 212.7 | 1,582 | 0.988 | 163.7 | 130.9 | 49.0 |
| Option 1, run B (12:39 peak) | 204.5 | 165.3, **1,579 rpm** | 0.942 | **155.8 W** | **124.6 W** | 48.7 |
| Option 1, StirlingShaftLinkTests (111.3 W) | 111.3 | 160.66 rad/s (was 163.04) | 0.411 (was 0.683) | 66.0 | 52.8 W (was 89.0) | 45.3 |
| Option 2, run A | 267.9 | 167.7, 1,602 rpm | 1.287 | 204.7 | 163.7 | 63.2 |
| Option 2, run B | 255.6 | 1,596 rpm | | 192.9 | 154.4 | |

Whole-sol charge (Option 1): about 0.85 kWh a sol against the doc's 1.1, so about 6 sols for the 5 kWh bank on the engine alone (the doc says 5).

### 1.5 Other machines and the tests whose numbers move (W, Option 1)

| Where | Today | With the defaults |
|---|---|---|
| `mars-stirling` small / matched / large on 3 N·m | 234.6 / 354.3 / 204.0 rpm; `shaft-power` 73.7 / 111.3 / 64.1 | **229.0 / 345.2 / 199.3 rpm**; into the loads 72.0 / 108.4 / 62.6 W; loss 1.8 / 2.9 / 1.5 W; `indicated-power` 73.7 / 111.3 / 64.1 (the hot ends unchanged) |
| `machine-behavior-test.rkt` Stirling case | `(check-= (at 'shaft-power) (power K T tc) 0.05)`, `(check-= (at 'rpm) (* (/ (power K T tc) 3) ...) 0.2)` | `'indicated-power` against `(power K T tc)`; `'rpm` against the root of `(3 + 0.068 + 8.3e-6 w²) w = P`; `'shaft-power` = 3 w; the 20 °C case's 55.97 W indicated unchanged |
| `StirlingShaftLinkTests` | unloaded 20 s at 111.3 W: 86.2 rad/s; wired 163.04 rad/s, 0.683 N·m, 89.0 W | **83.97 rad/s** (±1); work = kinetic + `LossHeat` (1%); wired **160.66 rad/s** (±0.5), **0.411 N·m**, **52.8 W**; `0.8 × ShaftPower` still holds with ShaftPower net |
| `stirling-route-test.rkt` run A | `eng.shaft-power` 214.0 ± 2; `motor.rpm` 1,606; `motor.torque` 1.27; `shaft-1.power` 214; `wire-1.power` 171; slope 171 | the thermal checks read **`eng.indicated-power` 214.0 ± 2**; `eng.shaft-power` **165.2 ± 2**; `motor.rpm` **1,583 ± 15**; `motor.torque` **1.00 ± 0.04**; `shaft-1.power` 165 ± 3; `wire-1.power` and the slope **132 ± 3 / ± 5**; at 390 s shaft 163.7 ± 2 |
| run B | asleep on 2 N·m 977 rpm; the wake 204.5 W; after the join 1,602 rpm, 164 W, 12 ± 1.5 Wh at 2,700 s | asleep **911 ± 30 rpm** (95.4 rad/s, 2.3 kJ stored); the wake `eng.indicated-power` 204.5 ± 3, `eng.shaft-power` 190.8 ± 3 (13.7 W lost at 95 rad/s); join: shared speed 80 rad/s, the cut-in about 30 s later; **1,579 ± 15 rpm, 124.6 ± 3 W, `shaft-1.power` 156 ± 3; 9.2 ± 1.5 Wh at 2,700 s** |
| run C (cold bank), the clamp gate | 3,000 ± 30 rpm by 140 s, `spin-limit.hits` ≥ 1 | inverts to the free-speed gate: `motor.rpm` **2,577 ± 50 at 200 s, 2,721 ± 40 at 400 s**, `spin-limit.hits` **0**, `eng.shaft-power` under 5 W at 400 s, `eng.indicated-power` 214 ± 2 at 60 s |
| `PausedSleepChargeTests`, `sleep-charge-test.rkt`, `GeneratorPrimeLinkTests`, `e2e-route-test.rkt` | | **unchanged**: no Stirling, and their generators are on in-machine Jolt trains (held as before) or `#:on` a windmill (SimGeneratorDrive) |
| `racket racket/build.rkt` | | rewrites only `game/machines/mars-stirling.machine` (two new props): commit it in slice 1 |

Code shape for (a):
- `StirlingEngine` gets `FrictionTorque` and `FlowLoss` (set; validated ≥ 0), `LossTorque(w)`, `IndicatedPower` (the old `ShaftPower`), `ShaftPower = Indicated - LossTorque·w`, `LossPower`, `LossHeat` (J) and `FreeSpeed` (bisection on `tau_f w + k w³ = P_ind`, 0 when `P_ind / StallSpeed ≤ tau_f`).
- In `Step`: `net = torque - LossTorque(w) - Load`. The existing `Math.Max(0, …)` gives Coulomb stiction and never reverses the engine; the w² term is stable at both 0.05 s and 1/120 s.
- Runtime: the props `friction-torque` and `flow-loss` with the defaults; getters `indicated-power`, `loss-torque`, `loss-power`, `loss-heat`, `free-rpm`; setters for both coefficients (#60).
- DSL: two optional keywords beside `#:inertia`, defaults in the `(cons …)` list, `>= 0` in the validation; `PartTemplates` and `RktExporter` take the same two props.
- `MachineView.Stirlings`' label already shows `ShaftPower`; add the loss in brackets.

## 2. (b) The paused sleep

### 2.1 A shaft between two Jolt ends (the windmill route's `drive-1`, exchange only) (S/W)

Both `JoltShaft` ends read `BodyGetDirectState(...).AngularVelocity`, and frozen bodies are Jolt-static and read **0** (section 0.4). So the slip is 0 - 1·0 = 0, λ = 0, and `StepAll` skips the impulse (`if (j != 0)`). Were an impulse ever computed, `apply_torque_impulse` drops it on a non-rigid body; were the inertia unreadable, the `ia > 0 && ib > 0` guard skips the link. `l.Torque` reads 0 during the sleep instead of the stale pre-sleep value, but no row is written inside a sleep. The sails' own train (`CoupleDrivenTrains`, `_drivenShaftLinks`) lives in the view, not in `WorldLinks`, and is untouched.

**Prediction:** `e2e-route-test`'s sleep run (loaded asleep, everything at rest) and the director's solve (its three sleeps, the mill turning at the first's start) are byte-identical, traces and expect lines, A against B; the call stays on sol 3 at 03:00 (144,259 s). Nothing may legitimately differ.

### 2.2 A shaft from a sim prime mover to a Jolt rotor carrying a generator: the stand-in

Rule: for every shaft link with exactly one Jolt end (an end `shaftFor` supplied: record `FromJolt`/`ToJolt` on `WorldLinks.Link` in `BuildShaft`) and one sim end (a Windmill, WaterWheel, JetWheel or StirlingEngine), a paused sleep turns the Jolt end in the sim's stead. Today only the Stirling route has such a link.

- **`WorldLinks.BeginPausedSleep(...)`** makes a `SleepStandIn : IShaft` for each such link from the body's speed and inertia and a `Link.SleepShaft = new ShaftLink(sim end, standIn, spec.Ratio)`. It attaches the generators of the Jolt end's machine whose `GeneratorShaft(id)` is that part or on its arbor (ratio 1; a generator geared inside a crate machine at another ratio is left held and logged: none exists today).
  - **The speed and inertia must be read before `_setRunning(false)`**: the freeze zeroes the body's velocity.
  - The stand-in's `Step(dt)`, for each generator: `tau = gen.Step(w, dt, most: I w / dt)`, then `w = max(0, w - tau dt / I)`, the view's `LoadGenerators` arithmetic. So the generator's `Omega`, `Power`, `Delivered` and the bank's record are the real ones, filed under `DrivenBy`.
- **`WorldLinks.StepShafts(dt)` while asleep** steps each stand-in, then `link.SleepShaft ?? link.Shaft` for every link. **The original sim-to-Jolt link must not be stepped while its stand-in exists**: the frozen end reads 0, and the exchange would drain the engine a tick at a time. The `shaft-N.*` getters read the active link.
- **`SleepControl`** gets three hooks that Main sets:
  - `BeforePause`, in `Begin` and `Resume`, before `_setRunning(false)`: `_links.BeginPausedSleep(...)`;
  - `AfterStep(dt)`, once per tick in `Advance` after `_session.Run(1)`, the wake tick included: `_links.StepShafts(dt)`;
  - `AfterWake`, in `End` **after** `_setRunning(true)`: `_links.EndPausedSleep(...)`, which puts the stand-in's speed back into the body.
  
  None of them runs on a live sleep. A `Resume` from a mid-sleep save restores body velocities first; check it with a save taken inside the new test's paused run.
- **`MachineView.Links`** gets `ShaftState(part)` (the speed and inertia) and `SetShaftSpeed(part, w)` (`PhysicsServer3D.BodySetState` on the body and its arbor mates, as the save restores velocities).
- **No double counting**: `MachineRuntime.HoldGenerators(ISet<Generator>? turnedAsleep)` skips the stand-ins' generators; the log says `motor: turned through the sleep by built-1.eng over shaft-1 (real power, not held)`. `MachineView.Electrics`' `rotorAsleep` is false for such a generator. A generator on a Jolt-driven train with no sim end (the windmill route, `found-electrics`) is held exactly as before (ruling 2026-10-09).
- **An unloaded engine joined through a sleep** (bank cold): the stand-in carries no generator torque, so the engine and stand-in run up to the free speed (24 kJ at most) and the body gets 286 rad/s at the wake, under the clamp: no runaway, no dump. **An engine on its own `#:load`, unjoined**: no link, no stand-in, unchanged.

## 3. The checks (each number worked first)

**C#** (`dotnet test tests/HeroicInventions.Sim.Tests`, about 2 minutes):
- `StirlingLossTests` (new):
  - `LossTorque(168.2) = 0.3028 ± 1e-4` (50.9 W);
  - unloaded at a held balance (Th 98.75, Tc -28.79, K 14, HeatInput 2,866), 600 s at 1/120 s: `AngularVelocity` 286.3 ± 1.4 rad/s, `ShaftPower` < 1 W, `FreeSpeed` 286.3 ± 0.3;
  - the energy over the run: ∫P_ind dt = ½Iw² + `LossHeat` + `Work` within 0.1%;
  - an engine making 0.05 W stays at rest (stiction);
  - mars-stirling's warm matched engine on 3 N·m settles at 36.15 ± 0.1 rad/s.
- `StirlingShaftLinkTests`: the lines in section 1.5.
- `PausedSleepStandInTests` (new, the `Linked` fixture): a watched stepping against a paused-sleep stepping over 300 s from the same warm start.
  - Expect: bank charge within 0.5%; rotor and stand-in speed within 0.2 rad/s; the motor never `Held`; `EndPausedSleep` leaves the rotor's speed equal to the stand-in's.
  - Bank cold: both settle at 226.1 ± 2 rad/s (111.3 W free), `Generated` 0.

**Godot** (headless):
- `stirling-route-test.rkt` with section 1.5's numbers (about 4 minutes; run C's gate inverted).
- `stirling-sleep-test.rkt` (new): run A's build, the hot end given 98.75 °C, `#:load 0`, both joins at once, `HEROIC_SET "battery-bank:cells temperature 25; battery-bank:bank capacity 5000"`. About 5 minutes.
  - **E-watched**: `waitsim 1300`, quit at 1,300, trace dt 10.
  - **E-paused**: `waitsim 100; sleep until scene.elapsed above 1200 limit 1500 paused; waitsim 1300`.
  - Predicted for E: both charge at 132 W falling to about 128 W; the bank about 45 ± 2 Wh at 1,300 s and **equal within 1%**; `motor.rpm`, `eng.rpm` and `wire-1.power` at the wake row equal to the watched run's within 0.5%; `motor.held` 0 in every paused row; `spin-limit.hits` 0; the log's "turned through the sleep" line.
  - **F** (no runaway, under the clamp): the bank cold until 1,200 s (`battery-bank:cells temperature 25 1200`), a paused sleep to 1,200 s, `waitsim 1400`.
  - Predicted for F: at the wake `motor.rpm` = `eng.rpm` ≈ **2,720 ± 50**, `spin-limit.hits` 0, `bank.charge` 0; then charging settles at about 130 W, **10.5 ± 1.5 Wh at 1,400 s** (3.6 Wh of it the flywheels'); `motor.rpm` continuous across the wake.
- **The windmill A/B.** A = before #269, B = after, the same binary.
  - e2e: the wired save's sleep run directly on both trees (`HEROIC_WORLD=lonely-rover-e2e HEROIC_LOAD=<save> HEROIC_SLEEP=pre-dawn HEROIC_QUIT_AFTER_SIM_SECONDS=55604 HEROIC_TRACE=... HEROIC_TRACE_DT=10`), `cmp` the traces.
  - The director's solve headless: `HEROIC_WORLD=lonely-rover-opening HEROIC_HINTS=0 HEROIC_MARKERS=0 HEROIC_INPUT_FILE=routes/lonely-rover-opening/solve.steps HEROIC_QUIT_AFTER_SIM_SECONDS=144400 HEROIC_TRACE=... HEROIC_TRACE_DT=60 --fixed-fps 30`. Read every `[director] expect ok:` line, `[sleep] woke after … s`, the `win:` line's sol and time, the `ending` lines; `cmp` the traces. Expected equal to the byte; a difference is explained and the owner told before landing.
  - The A runs are recorded already (2026-10-17, before any #269 code, at 32b9a98): e2e 460 s of wall time, the solve 625 s, the solve's win on sol 3 at 03:00 (cells past 0 °C at 35,605 s, bank full 500 Wh, then the call). After the rebase onto main, rerun e2e A on the new base and check it is byte-identical before reusing the solve's A.

## 4. Docs

- This plan, with a **Measured** section.
- `docs/plans/stirling-route-plan.md`: a dated note at the head of "Measured": the shaft numbers are now net of the engine's losses, with a pointer here; section 9's questions answered by #269.
- `docs/lonely-rover.html`:
  - the Stirling bullet: the engine has real seal and flow losses; the free speed under the 3,000 rpm limit; the measured W; the sols; and the sentence "Its power does not depend on how fast it turns" reworded (the lead's note);
  - the checked-model row the same;
  - the sleep paragraph (~576): a found rotor a world shaft joins to a sim-turned engine is turned in the sim's stead through the sleep and given its speed back on waking, and a paused sleep steps the world's shafts.
- `docs/e2e-route.md`: GAP 7 gains (b) and the A/B result; "The Stirling in the crater (#220)" gets the new numbers, and its two open points are marked resolved by #269.
- `docs/conventions.md`: the `SLEEP` row, and a new trap under Jolt: **freezing a body (Pause, a paused sleep) zeroes its velocity: read a body's speed before `Freeze = true`.**
- `racket/machines/mars-stirling.rkt` header: the loss sentence and the new rpm.

## 5. Risks and owner questions (nobody but the owner decides)

1. **What does 0.35 name?** Option 1 (the gas cycle's: the shaft 165 W, the charge 132 W, about 6 sols) or Option 2 (the shaft's at 1,500 rpm: an indicated default of 0.4375, the doc's 205/164 W stand, mars-stirling pins 0.35). Recommended: Option 1, as the face-value reading of "friction added".
2. **The free speed sits near the clamp.** The sourced inputs bracket it from about 2,500 to 3,300 rpm; the recommended 5/15 split gives 2,735 (9% under). If Thieme's motoring table pushes the friction share past 10%, the free speed crosses 3,000 rpm and "under the clamp" needs one of:
   - accept the clamp as co-limiting;
   - a lower design speed for the template engine (1,200 rpm rated gives 2,190 free);
   - a higher `jolt_physics_3d/limits/max_angular_velocity`.
3. **Scope of the stand-in rule.** As designed it covers any world shaft with one sim end and one Jolt end. A windmill joined straight to the rotor changes nothing in practice, but it is a rule: enable it beyond the Stirling only on the owner's word. Jolt-driven trains (the windmill route) stay held.
4. **A finding, not this issue:** a paused sleep (and Pause) already loses every Jolt body's spin at the freeze (section 0.4). Worth an issue and the conventions trap.
5. The doc's route time (about 5 → about 6 sols under Option 1) and the headline W move: told before landing. mars-stirling's demo numbers move 2-5 W.
6. A sealed, pressurised crankcase (windage 232 W at 1,600 rpm) is a different engine; only if the owner wants one.
7. The split and η_m rest on abstracts (the 2023 Energy paper, the GPU-3 CFD paper) and one unread scan (Thieme): the implementer reads the three before the first run and records what they say in Measured.
8. #232 (the Stirling win) can then sleep its night with the engine joined; the held `EstimatePower` problem for a Stirling-linked motor goes with the stand-in.

## 6. The morning's power (added by the routes session, for the lead's constraint)

The lead's constraint for #269's done-when: the drag at the motor's 157 rad/s cut-in must be a small fraction of the morning's power, shown with numbers. On sol 2 in lonely-rover-e2e the engine makes about 48 W at 08:00, 101 W at 08:30 and 144 W at 09:00 (quasi-steady; about 130 W at 09:00 time-stepped from the dawn air; docs/plans/stirling-win-plan.md section 2).

With the plan's defaults the loss at 157.08 rad/s is 0.068 × 157.08 + 8.3e-6 × 157.08³ = 10.7 + 32.2 = **42.9 W: 89% of the 08:00 power. The constraint is not met on a plain shaft.** The coefficients were not fitted to it and are not changed for it.

Worked balance `P_ind = loss(w_engine) + k_g (w_rotor - w_cut) w_rotor`, with w_engine = w_rotor / G:

| P_ind | plain shaft (G = 1): rotor rpm, charge, loss | 4:1 stage (G = 4): rotor rpm, charge, loss (engine rpm) |
|---|---|---|
| 48 W (08:00) | 1,503 rpm, **4.0 W**, 43.0 W | 1,523 rpm, **35.8 W**, 3.2 W (381) |
| 64 W | 1,511, 16.3 W, 43.6 W | 1,532, 48.6 W, 3.3 W (383) |
| 101 W (08:30) | 1,529, 44.8 W, 45.0 W | 1,550, 78.1 W, 3.3 W (388) |
| 130 W (09:00, time-stepped) | 1,543, 67.2 W, 46.0 W | 1,564, 101.3 W, 3.4 W (391) |
| 144 W (09:00, quasi-steady) | 1,550, 78.0 W, 46.5 W | 1,571, 112.5 W, 3.4 W (393) |
| 204.5 W (12:39) | 1,579, 124.6 W, 48.7 W | 1,600, 160.8 W, 3.5 W (400) |
| 214.3 W (noon) | 1,583, 132.2 W, 49.1 W | 1,605, 168.7 W, 3.5 W (401) |

With real losses that grow with the engine's speed, a plain shaft makes the engine run at the motor's 1,580 rpm, where it loses 43-49 W. A 4:1 stage lets it run at about 390 rpm, where it loses about 3 W. So the constraint is met by gearing the engine down (a player's build), not by the coefficients. The doc's original "one gear stage brings it to the generator's cut-in", which #220 found had no effect in the frictionless model, becomes worth building again.

A player cannot build that stage today. `MachineView.Gears.IsSimTurned` leaves Stirlings out, so an engine on an arbor drives no Jolt gear, and the join gesture's link ratio is 1 (`(ratio N)` exists only in a save or world file). Making a Stirling drive gears on its arbor, as a windmill does, fills an omission like #220's link change, but it is outside this plan's files (`MachineView.Gears.cs` is unowned: the lead's call). This is for the lead and the owner before slice 1's numbers are fixed in the tests.

## 7. The second pass (Fable, after the morning constraint), and slice 0

Fable's second pass on #269, given the lead's constraint, kept the coefficients (0.068 N·m and 8.3e-6 N·m·s²; not fitted) and added the following. Slice 0 then made the gear stage of section 6 buildable.

**The sol-2 morning, plain shaft (W, Option 1).** The drag at the cut-in is 42.9 W whatever the split: at the rated speed every split gives η_m.
- The found motor can first load when the engine's power passes 42.9 W: about 07:56 quasi-steady, about 08:05 with the engine's 6 K thermal lag.
- From rest the pair reaches the cut-in in 355 s at 48 W, or 93 s at 101 W.
- Time-stepped with the generator loading: cut-in 07:59-08:05, the 25 Wh bank full 08:44-08:50 (the #232 plan's frictionless 07:49:41 and 08:29). At its 08:15 wake the bank holds about 3.7 Wh at 24 W, not 16.5 Wh at 51 W. #232's section 2 table and fallback are to be redone with these numbers.
- After sunset the losses bring the pair from 286 rad/s to rest in 17 minutes (I/√(τ_f k)·atan(ω√(k/τ_f)) = 1,004 s), at rest by about 18:00, before the cells pass 0 °C at 19:27: no dump into the warming bank.
- The rated speed moves the margin: rated 1,200 rpm gives 76 W of drag at the cut-in (loads from about 08:16), 1,800 rpm gives 27.5 W (from 07:46).

**The tests whose lines move (Option 1):**
- `machine-behavior-test.rkt:1356-1383`: `'indicated-power` against `(power K T tc)`; `'rpm` against the root of `(3 + τ_f + k ω²) ω = P`, 23.98/36.15/20.87 rad/s = 229.0/345.2/199.3 rpm.
- `StirlingShaftLinkTests`: unloaded 84.0 ± 0.5 rad/s at 20 s, with work = kinetic + `LossHeat`; wired 160.66 rad/s, 0.411 N·m, 52.8 W.
- `stirling-route-test.rkt`: run A `eng.indicated-power` 214.0 and `eng.shaft-power` 165.2, 1,583 rpm, 132 W; run B 911 rpm asleep, then 1,579 rpm, 124.6 W, 9.2 Wh; run C inverted to the free speed (2,577 ± 50 rpm at 200 s, 2,721 ± 40 at 400 s, `spin-limit.hits` 0).
- `stirling-gear-test.rkt` (slice 0): with the losses, the 4:1 stage gives 168.7 W of charge at noon (section 6), against 132 W on a plain shaft.

**StirlingLossTests also gets** (vi): `friction-torque 0` and `flow-loss 0` reproduce today's √(2Pt/I) (86.2 rad/s at 20 s), so the losses are numbers, not a formula change.

**`stirling-sleep-test.rkt` (new, four runs at once, about 6 minutes):**
- **D, the free speed under the clamp:** run C's shape. 2,721 ± 40 rpm at 400 s, `eng.free-rpm` 2,727-2,734, `spin-limit.hits` 0.
- **E, watched against a paused sleep of the same span:** the bank warm, capacity 5,000 Wh; E-watched `waitsim 1300`, E-paused `waitsim 100; sleep until scene.elapsed above 1200 limit 1500 paused; waitsim 1300`. Predicted: 43.5 ± 1.5 Wh in both, equal within 1%; `motor.rpm` 1,583 ± 15 at every common row; `motor.held` 0 throughout E-paused; the log line `motor: turned through the sleep by built-1.eng` and no `Machines paused: … motor`; the hot ends equal within 0.1 K.
- **F, joined with the bank cold through a paused sleep, then warmed:** at the wake 2,727 ± 50 rpm and no charge. Then the pair falls to 1,583 rpm within about 60 s and the bank holds 10.5 ± 1.5 Wh at 1,500 s (3.6 Wh of it the flywheels'). No `motor.rpm` frame above 2,800, and none at 0 after the wake.

**Stand-in details:**
- `MachineView.Electrics`' "sleeping, machines paused" label is false for a sleep-driven generator, which shows its real watts.
- `Resume` from a mid-sleep save: `Main.LoadWorld` (:1267-1303) restores body velocities before `BeforePause` reads them. A test loads a mid-sleep save and asserts the stand-in's speed equals the save's rotor speed.

**Slice 0, done before the losses (the lead, 2026-10-17):** a Stirling drives Jolt gears on its arbor, as a windmill does. Before it, the view failed to build an arbor holding an engine (`KeyNotFoundException: 'eng'` in `BuildArbors`); the engine turned alone at 462 rpm and its gears stood at 0. `MachineView.Gears.IsSimTurned`, `SimShaft` and `SimAngle` now count Stirlings. `SimSense` takes the engine's crankshaft along X through its flywheel, 0.3 m over its foot (`StirlingShaftHeight`), turning forward about +X. The sim's mirror of the rule (`MachineRuntime.RegisterOperatorFields`) counts them too.

`stirling-gear-test.rkt`, frictionless, worked against measured:

| | Worked | Measured |
|---|---|---|
| engine | 401.5 rpm | 400.9 rpm |
| the 72 | at the engine's speed | 401.7 rpm |
| the 18 | 4.000× the 72 | 1,606.8 rpm (4.000×) |
| rotor | 1,606 rpm | 1,605.7 rpm |
| charge | 171 W | 170.6 W |

## Measured (slice 1, the drag, 2026-10-17)

One drag term k ω² with k 1.38e-5 N·m·s², the template's 0.4375 of Carnot (0.35 at the shaft at 1,500 rpm), and mars-stirling pinned at 0.35. Every number below was worked before its run (the "Decided" section and the tests' headers).

| Test | Worked | Measured |
|---|---|---|
| `StirlingDragTests`: free speed, on 3 N·m, k = 0 after 20 s | 268.8 rad/s; 86.34 rad/s; 146.4 rad/s | within 0.5, 0.2, 0.5 (4 passed) |
| `StirlingShaftLinkTests`: unloaded 20 s; wired | 84.9 rad/s, work = kinetic + drag heat; 160.06 rad/s, 0.341 N·m, 43.7 W | passed within 0.5 rad/s, 1% of the work; 0.5 rad/s, 0.02 N·m, 1 W |
| `machine-behavior-test` Stirling case | 233.9 / 352.1 / 203.6 rpm, the loads 3ω | passed (0.2 rpm; 0.05 W) |
| `stirling-route-test` A: shaft, rotor, torque, charge | 202.3 W, 1,601 rpm, 1.21 N·m, 162 W | 202.3 W, 1,600.2 rpm, 1.203 N·m, 161.2 W (slope 161.05 W) |
| B: asleep; rotor, charge, bank at 2,700 s | 1,115 rpm; 1,595 rpm, 152.9 W, 11.3 Wh | 1,115.6 rpm; 1,595.1 rpm, 152.4 W, 11.67 Wh |
| C: at 200 s, at 400 s, the clamp | 2,521, 2,560 rpm, never | 2,517.7, 2,559.4 rpm, `spin-limit.hits` 0; `free-rpm` 2,559.9, 0.13 W left at the shaft |
| `stirling-gear-test`, 4:1: engine, rotor, charge | 407.5, 1,630 rpm, 213.4 W | 406.8, 1,629.7 rpm, 212.5 W |

The whole sol (worked hour by hour, quasi-steady, if the bank takes charge all day): a plain shaft gives about 0.9 kWh of charge (5.5 sols for 5 kWh) and a 4:1 stage about 1.3 kWh (3.8 sols).
