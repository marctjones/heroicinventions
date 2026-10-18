# Hand-off: the routes work (Stirling route, a second way to win, multi-sol), 2026-10-17

For the session that picks this up. Milestone **17 "A second way to win"** holds the open issues. The owner stopped this session to archive it. Everything below is committed; nothing is left running.

## What landed or is ready to land

- **#220, landed on main** (`1cdb2d4..0e75dcf`, closed). The Stirling built in the crater by typed build words and joined to the found motor gives 204.5 W at the shaft at 12:39 from a cold start (214 W settled at noon), not the doc's 320 W, because the crater's midwinter sun is weaker than Meridiani's. A Stirling is a world shaft-link end. `stirling-route-test.rkt`, `StirlingShaftLinkTests`. Plan: `docs/plans/stirling-route-plan.md`.
- **#269 slices 0 and 1, ready to land** on branch `claude/routes-stirling-a546ca` (rebased on main after #220; 5 commits). The lead runs the full suite and lands.
  - **Slice 0:** a Stirling drives the Jolt gears on its arbor, as a windmill does (`MachineView.Gears.IsSimTurned`). Its crankshaft runs along X through its flywheel, 0.3 m over its foot. `stirling-gear-test.rkt`. Windmill A/B: e2e byte-identical. The director's solve wins on sol 3 at 03:00 with identical expect lines; its traces are not byte-repeatable run to run (a paused sleep's wake lands where the wall-clock budget puts it), and a rerun of the same code differs as much.
  - **Slice 1:** the engine's own drag, ONE term k ω² (the owner's choice of the simplest model), k = 1.38e-5 N·m·s² by a rule of thumb (a fifth of the power lost at the rated 1,500 rpm). The default fraction of Carnot is 0.4375, so 0.35 at the shaft at 1,500 rpm (the lead's Option 2, delegated by the owner); `mars-stirling` is pinned at 0.35.
    - Unloaded, the engine settles at its free speed, (P/k)^⅓: about 2,560 rpm at noon's power. Jolt's 3,000 rpm clamp co-limits above about 428 W (1.6x noon, e.g. twice the heliostats); the clamp then brakes the shaft and destroys the excess, as with a windmill's open circuit.
    - A plain shaft charges 161 W at noon; a 4:1 gear stage charges 212 W.
    - At 08:00 on sol 2 a plain shaft charges about 5 W and the stage about 47 W.
    - Tests: `StirlingDragTests`, the re-worked route, gear and behaviour tests.

  Plan: `docs/plans/stirling-drag-plan.md` (sections "Decided" and "Measured").

## Unfinished work, on local branches (not pushed: the lead decides)

| Branch | Commit | What it is | What is left |
|---|---|---|---|
| `claude/stirling-win-wip` | "WIP: a shut lid blocks the mirror's light" | **A shut lid blocks the mirror's light** (#280). The fix is one helper, `MachineRuntime.LidOpenOver`. Owner approved. | Re-run `e2e-route-test.rkt` (its night numbers are already updated to the measured 14.5 °C cells, 49.9 °C rock, lid 0.69); run the director's solve (it must still win on sol 3 at 03:00); update `docs/e2e-route.md`'s vault numbers. |
| `claude/stirling-win-wip` | "WIP #232: the Stirling as a second route" | **#232**: the Stirling as a second route. The route data and the reveal tests are done (dotnet 1,531 passed). `stirling-win-test.rkt` is written. | Run `stirling-win-test.rkt` (about 6 minutes), fix what it shows, then the docs (`docs/e2e-route.md`, `conventions.md` routes section, `stirling-win-plan.md` Measured). |
| `claude/stirling-sleep-parked` | "WIP, parked: #269 (b)" | **#269 part (b), parked by the owner:** a paused sleep steps a Stirling's world shaft through a stand-in rotor. Sim side only, 2 tests passing. | Only if the owner un-parks it: the three `SleepControl` hooks, `Main` wiring, `MachineView.Links` `ShaftState`/`SetShaftSpeed`, a Godot test, a windmill A/B. |

Rebase each onto main once its base has landed (`git rebase --onto <new base> <old base> <branch>`; never move main).

## Findings a new session needs

1. **The lid bug.**
   - What happens: the bimetal strip shuts the lid at 40 °C, but the mirror kept heating the rock through it. In `lonely-rover-e2e` (the vault alone) the rock reached 762 °C on sol 2, and the bank's cells were 45.6 °C at 03:00 on sol 3, over the 45 °C limit. So no route could call on sol 3, and #219 ("the rock re-heated each sol") could not work.
   - With the fix (the vault alone, traced): sol 1 is identical until 14:00. Then the cells are 14.5 °C at 02:49 on sol 2, the rock peaks at 176.6 °C on sol 2, and the cells are 34.5 °C at 03:00 on sol 3.
   - Before landing: the windmill route still called at 55,490 s with the fix (one run, before its numbers were updated). The director's solve has not been run with the fix.
2. **The Stirling sleep bug (#269 (b), parked).**
   - A paused sleep holds a Stirling-joined motor at its last steady rate (159.7 W in the trace) whatever the sun. The engine runs on alone, the frozen rotor wakes at 0, and the engine's stored speed is dumped into the bank after the wake.
   - A motor that has never charged is held at `EstimatePower`, which reads the engine's speed: about 2.6 kW at the free speed.
   - So any Stirling route must not sleep with the engine joined while the motor would charge. `stirling-win-test.rkt` joins on sol 2's morning while watched, fills live, then sleeps to the call with the bank full (a held motor cannot charge a full bank).
3. **A frozen Jolt body zeroes its velocity** (a paused sleep or Pause). Filed as #271 (owner decision).
4. **Not fixed, filed as #281:** `MachineView.Mirrors.cs:47` draws each mirror's post from y = 0. On a map that is about 55 m above the soil, so every built mirror has a rod into the sky; the director's solve heliostat too. The one-line fix is the pattern of `MachineView.Drains.cs:24` (`Ground?.HeightAt`).

## Next, in order

1. **The lid fix (#280):** verify (e2e-route-test, the director's solve), the docs, land.
2. **#232:** run `stirling-win-test.rkt` and finish it (plan: `docs/plans/stirling-win-plan.md`, its status section at the top).
3. **#219** (several sols on a 250-500 Wh bank, the rock re-heated each sol): needs the lid fix. Plan it first, then measure hours a sol in the charging window and the bank's charge each dawn. The windmill route's motor is held at its settled power in a paused sleep (the 2026-10-09 ruling), so #219 on the windmill does not need #269 (b).

## How to work here (what this session learned)

- The owner wants the **simplest honest model**: one or two numbers with a stated rule of thumb, and no literature hunts for coefficients.
- Keep each slice small and ask before widening the scope. The owner stopped two over-long detours.
- Plan with Fable first, read-only, and commit the plan. Work every number before a run, then trace the run against it.
- Run Godot tests one at a time, headless, in the background.
- Racket in a worktree needs `export PLTCOLLECTS=$PWD/racket:`. A fresh worktree needs `racket racket/build.rkt`, `cd game && dotnet build` and `Godot --headless --import .` first.
- When a sim file changes, rebuild the game (`cd game && dotnet build`) before any Godot run. A stale build cannot parse new route kinds and silently loads no routes.
