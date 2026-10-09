# Working conventions

Issues that say "Conventions: see #79" mean this file. It was moved here from #79 (superseded by `docs/handoff-2026-10-07.md` and the issue map), with what the 2026-10-07/08 waves added.

## Done when

- Every issue's Done-when is implemented across:
  - the Racket DSL (`racket/heroic/machine.rkt`, `emit.rkt`);
  - the C# sim (`src/HeroicInventions.Sim`);
  - the Godot view (`game/scripts`);
  - the editor (`RktExporter`, `PartTemplates`, `BuildSession`, the palette in `BuildMode.cs`).

  It also needs a machine in `racket/machines/`, a test asserting a hand-computed prediction, all existing suites green, and the state visible in the scene, not only in the HUD.
- **A machine works only when a trace shows it doing its job**, against a number worked out beforehand. Loading and exiting 0 doesn't count. Check the frames as well as the trace.
- **Check design numbers before writing them into docs or issues.** Model anything with a time constant, and give a source for any recalled fact.
- **Change numbers, never formulas.** Allow poor strategies rather than forbidding them; put risky constants behind Advanced tuning.
- **Legibility over realism** for colour and light; the physics stays real. Measure with `tools/legibility.py`: sd ≥ 40 and |sep| ≥ 50 on the six references (antikythera, roman-crane, newtons-cradle, water-wheels, mars-stirling, herons-fountain), and none may get worse.
- **A look fix must not change physics.** Prove a layout move with a before/after `HEROIC_TRACE`. Where a number is the point of a demo (a drop height, a material), fix the view instead of the number, or re-derive the header.

## Parallel work

- **Exactly one session merges and pushes `main`.** Other sessions and agents work in their own worktrees and branches and hand back.
- Give each agent a list of files it owns and files it must not touch, so that merges don't conflict.
- Merge into an integration branch (`integ/<wave>`), then run: build, `dotnet test`, `racket racket/build.rkt` (no drift in `game/machines`), and the full raco suite (about 20 minutes). Then fast-forward `main` to that exact commit and push. Integration branches can be stacked, so one branch's suite runs while the next merge is being checked.
- **Engine changes that touch many machines:** trace every machine before and after (all numeric fields; flag anything more than 1e-3 and 1% apart). Jolt is deterministic, so unaffected machines come out bit-identical.
- Close each issue with its worked number against its measured number.

## Commands

```bash
dotnet test tests/HeroicInventions.Sim.Tests -nologo -v q
export PLTCOLLECTS=$PWD/racket: HEROIC_GODOT=/Applications/Godot_mono.app/Contents/MacOS/Godot
raco make racket/heroic/main.rkt && raco test racket/heroic/tests > /tmp/rt.txt 2>&1; echo exit $?   # check the exit code: a pipe through tail hides failures
racket racket/build.rkt                     # racket/machines/*.rkt -> game/machines/*.machine (rewrites all; commit only what you changed)
cd game && dotnet build HeroicInventions.csproj -nologo -v q
/Applications/Godot_mono.app/Contents/MacOS/Godot --path game           # not on PATH
tools/gui-check.sh --hidden HEROIC_AUTOSELECT=<m> HEROIC_AUTORUN=1 -- "wait 5; shot /path/a.png; quit"   # real window, never takes focus
```

Environment switches (all `HEROIC_*`):

| Switch | What it does |
|---|---|
| `AUTORUN=1` | run on start |
| `AUTOSELECT=<machine>` | open this machine |
| `AUDIT=1` | moved/turned/rope report, untagged opaque meshes |
| `DEBUG_PHYSICS=1` | physics debug output |
| `QUIT_AFTER_SIM_SECONDS=<s>` | quit after this much sim time |
| `SPEED=<n>` | sim speed |
| `TRACE=<path>`, `TRACE_DT=<s>` | write a trace |
| `WORLD=<name\|gallery>` | open a world |
| `SET="target field value [at]"` | set a field (timed settings); in a world the first machine's, or `label:target` for another placed machine |
| `ACTIONS=<file>` | replay an operator log |
| `DRAG=...` | replay hand drags |
| `FRAMING_REPORT=1` | share outside the clear area; label counts |
| `GAUGE_TRACE=<path>` | gauge trace |
| `FLOW_REPORT=1` | flow report |
| `LIVE_EDIT_AFTER=<s>` | live edit after this much sim time |
| `EDITOR=1`, `EDITOR_INPUT=...` | scripted editor input; needs a real window |
| `FPS_REPORT=1` | frame-rate report |
| `FRONTEND=1/0` | force the title page on or off (a plain launch opens it; any of WORLD, AUTOSELECT, AUTORUN, EDITOR, INPUT or LOAD skips it) |
| `TUNING_PANEL=1`, `GOALS_PANEL=1` | open the game-tuning panel (F3, #60) or the goals panel (F2, #68) at the start, for frames |
| `GOALS_REPORT=1` | print the bank crate's cover and the goals earned every 2 s of the scene's clock |
| `SLEEP=<wake>` | sleep at load until the machine's named wake. A script step does it later: `sleep WAKE [live\|paused]` or `sleep until T.F above\|below V [limit S] [live\|paused]` (#207) |
| `INPUT` steps `join A B`, `joinclick A [N|list]`, `joinrightclick A`, `joinlist N`, `joinbutton`, `fps N` (#218, #224, #225) | `join LABEL.PART[.PORT] LABEL.PART[.PORT]` joins two placed machines' parts as the Join machines gesture does, anywhere in a script (after a build, with the parts tens of metres apart): the button's `SetJoining`, the two picks a click on each part makes (`Main.Pick`, the same rules and the same `[links] joined:` line), then joining off. A bad name, a part with no drawing, a malformed end or a join the rules refuse prints `join: ...` and ends the run with **exit 1**, even if a `quit` follows. `joinclick A` is one real mouse click on a part where the camera draws it, with joining on (it fails if the part is off screen). The pick rule is the join tool's (#225): nested pick boxes, the smaller wins (the gears inside the sails' box, the bank inside its crate's); boxes that only overlap, nearest first; a second click on the same spot or a right-click offers a list of the parts under it (`pick list at ...` in the log). `joinclick A N` clicks A, clicks again and takes item N (from 1) of that list; `joinclick A list` clicks A and, if another part was picked, clicks again and takes A from the list (fails if A is not there); `joinrightclick A` offers the list by a right-click and `joinlist N` takes item N of the list open. `fps N` caps the frame rate (`wait` counts frames and `hold` seconds, so a drive is repeatable at one rate only); `joinbutton` prints the button's label (`Join machines` in the rover game, `(J)` elsewhere). `wheel X Y N` (the mouse wheel) and `type TEXT` (into a focused field) are there for the same gui routes. `tools/route-gui.sh` plays the route with them |
| `SLEEP_PHYSICS=1/0` | a sleep keeps the physics engine running (default for Jolt-driven machines under 7,200 s) or pauses it |
| `SAVES_DIR=<dir>` | where saves go instead of the player's folder; a scripted run (`QUIT_AFTER_SIM_SECONDS`, `INPUT`) with none writes no autosave and no rover log. The rover log (#217) is `<scene>.rover-log.txt` there (or in `user://saves` for a player), one line appended as it is made. `godothost.rkt` sets a temporary one |
| `HINTS=1/0`, `HINTS_RATE=<n>` | first-run hints (#98) on or off in a scripted run (they are off there by default); `HINTS_RATE` runs their waiting clocks n times faster (the rover hints wait 20 s to about 6 min; at rate 1 a fixed-fps run counts game seconds, so it is the real threshold). `SETTINGS=<file>` keeps the retired list out of the player's settings |
| `ROUTES_PANEL=1\|next\|outline\|0\|off` | the level of the goals panel's Routes block (#230): next steps (`1`, the player's default), outline (route names and "N of M steps") or off (`0`). A scripted run (`INPUT`, `EDITOR_INPUT`, headless) has it off unless this is set, as hints are; a player's choice is remembered in `settings.cfg` (`SETTINGS=<file>`), in its own `[routes-panel]` section. Setting it also prints the block to the log, one `[routes-panel] begin level=.. t=..` ... `[routes-panel] KIND: text` ... `[routes-panel] end` group each time its shape changes (and when its numbers move, at most every 10 s of the scene's clock), which `goals-panel-test.rkt` reads |

## Routes to the win (#227)

A route is a data file, `game/routes/*.route` (and `game/routes/<world>/*.route` for one world): `(route ID (name ..) (description ..) (proven-by "path of the test that wins by it") (step ID (title ..) (reason ".. {NAME|fallback} ..") (met CONDITION ..)) ..)`. Conditions are a closed set (`RouteCondition.Kinds` in `src/HeroicInventions.Sim/Game/Routes.cs`, each documented with the sim field it reads); any one of a step's conditions meets it. A step marked `(shared #t)` (the bank warm, the bank full) counts as met and shows once its route is started, but never starts or names a route. The reveal rule, `RouteReveal.Reveal(routes, history, reading)`, is a pure function; `RouteTracker` keeps the history (first time of each step, in the world save as `(routes 1 ..)`, `WorldSave.Routes`) and the rover log gets a line for each first time. A malformed file fails with the file and step named.

- **The goals panel (#230):** F2's Routes block (`RoutesPanelText` in `game/scripts/Main.Routes.cs`) shows the final goal and, per started route, its name and "N of M steps" and (the next-steps level) the steps `RouteReveal` gives with their reasons. It adds nothing about routes the view does not show. Levels off / outline / next steps are three buttons in the panel, remembered in the settings file's `[routes-panel]` section (`Hints.cs` saves the whole file from its own copy, which drops that section, so a chosen level is written back whenever the file lacks it). The old "Routes" achievements section is now "Route achievements".
- **Gate (#231):** `RoutesTests.EveryShippedRouteNamesATestThatExists` fails if a shipped route's `proven-by` is not a file. Add a route only with a passing test that wins by it.
- **Playtest (#233):** the links trace (`<TRACE>.links`) carries `route.ROUTE.started`, `route.ROUTE.STEP` and `route.final.the-call`: the scene's clock at the first time, -1 before.
- **Routes in the design doc that wait for another issue before they may ship:** the Stirling route on mirror heat (#220, never built in the crater); the hydraulic miner and its hot-water night store (a hushing flood and a night store that charge a bank: no issue yet, and a winning test needs a generator driver as well); the gravity battery (a winning test, which the design doc calls "clever and weak"); the water wheel (designed as "effectively never": a documented reason would have to replace the test); the siege engineer (shatter the boulders with a trebuchet, #43, then any power route, so it waits for a passing test of both).
- `export_presets.cfg` lists `routes/*.route` and `routes/*/*.route` in each `include_filter`, so an exported game has its routes.

## Traps already met

- **A fresh worktree has no mesh catalogue.** `game/meshes/catalogue/` is gitignored and made by `racket racket/build.rkt`; without it, gear builds say 'no catalogue entry'. Run build.rkt before the Godot import below.
- **A fresh worktree has no Godot import.** The first headless run in a new worktree can hang for a long time importing; run `cd game && dotnet build && /Applications/Godot_mono.app/Contents/MacOS/Godot --headless --import` once first (then runs take seconds).
- **Background suites must prove where they ran.** Start with `cd <worktree> || exit 1`, then write `pwd` and `racket -l racket/base -e '(displayln (collection-file-path "godothost.rkt" "heroic"))'` into the log. A run whose `cd` didn't hold tested the main checkout's stale build and hung for two hours on a win that build couldn't reach.
- **Racket:**
  - **In a worktree, set `PLTCOLLECTS=$PWD/racket:`.** Otherwise raco silently uses the main checkout's `heroic` package.
  - If a new part says "expected one of these identifiers", the compiled language is stale: run `raco make`.
- **Godot and screenshots:**
  - Never test the GUI with computer-use; `tools/gui-check.sh --hidden` doesn't take focus. Hidden shots can drift if real input reaches the window, so confirm the camera with a `camera` print at each shot.
  - Headless Godot has no real window: projected screen positions are nonsense there.
  - **`res://` is not a folder in an exported game**: it lives inside the pack. Read with Godot's `FileAccess`/`DirAccess` (`GetFileAsBytes`, `AppendFromBuffer`, `LoadPngFromBuffer`), never `GlobalizePath` plus .NET or `*FromFile` reads (#99).
  - `QueueFree()` frees at the frame's end, so the node keeps its name until then. Creating a node with the same name in the same frame gets an auto-name (`@Node3D@390`). Detach first (`GetParent().RemoveChild(n)`), as ClearWorld does, or anything matched by name (saves, scripts) silently misses.
  - The viewport is 1600×1000 whatever the window size. Screen rules (the clear area between panels) are in viewport units.
- **Machines and traces:**
  - A game machine is listed by the name **inside** its file, not the file name.
  - In a trace, `rot-z` is the absolute tilt from vertical; `angle` is measured from the starting position.
  - Don't judge a periodic motion from frames at fixed times; read the trace.
- **Jolt physics:**
  - A Godot body's node shows the previous physics tick until the next step; read the physics server's state when copying poses.
  - **Jolt collision:** a body is pushed by another only if the other's layer is in its own **mask** (#189: the catapulta bolt fell through a frame that "saw" it).
  - Jolt takes the larger restitution of two surfaces (#87).
  - **Speed-ups raise the tick rate and the time scale together** (`Main.SetSpeed`). Anything per-step, such as a motor's max impulse, must use the step's sim length (time scale / tick rate), not 1 / tick rate.
  - **Anything exchanged between bodies once a tick lags one tick** (world shafts #191, brake friction #85). Put the coupling inside Jolt's step where possible.
  - `ApplyBuoyancy`'s `BoxSize()` merges every child mesh of a body. Parent view-only meshes (water in buckets, marks) to the view, not to the body.
  - Builders mutate their `MaterialOverride` every frame. Never share one material between meshes that need different looks.
- **Rope solver changes:** run the rope machines (trebuchet, Roman crane, post-and-lintel crane, catapulta, onager) and the Newcomen engine as the canary.

## Design

`docs/lonely-rover.html` (the game, its goal, routes and checked assumptions), `docs/design.html` (the engine), `docs/art-direction.md` (the look, rule by rule).
