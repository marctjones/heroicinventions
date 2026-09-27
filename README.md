# Heroic Inventions

A workshop game where players build historical machines one part at a time (Hero's aeolipile, Heron's fountain, Roman cranes, Leonardo's and Franklin's inventions) and then run them in a physics simulation.

Machines are written in **Racket** as `#lang heroic` programs. Racket checks them while they compile (part names, materials, ports, steam supply) and emits plain S-expression `.machine` files. The **Godot 4.6 + C#** game loads those files and runs them with its fluid, steam and Jolt rigid-body simulation. Players don't need Racket installed.

Design document: [docs/design.html](docs/design.html), also published at https://claude.ai/artifact/6iP8BrM93Ao771cYoBZi3Z

## A machine

```racket
#lang heroic

(define-machine aeolipile
  #:source "Hero of Alexandria, Pneumatica"
  (boiler kettle #:at (0 (cm 8) 0) #:radius (cm 12) #:height (cm 16)
          #:water (kg 0.3) #:fire (W 3000) #:material bronze)
  (rotor ball #:at (0 (cm 45) 0) #:radius (cm 6) #:wall (mm 1)
         #:material bronze #:nozzles 2 #:bore (mm 2) #:arm (cm 8))
  (connect kettle.steam ball.steam-in))
```

Connecting `kettle.steam` to a water port instead fails at compile time:

```
aeolipile.rkt:6:24: define-machine: cannot join kettle.steam (steam port) to vat.drain (water port)
```

## Layout

| Path | What |
|---|---|
| `racket/heroic/` | The `#lang heroic` package: `define-machine`, compile-time checks, `.machine` emitter, units, material table. |
| `racket/machines/` | Blueprints: `aeolipile.rkt`, `herons-fountain.rkt`, `material-samples.rkt`. |
| `racket/build.rkt` | Writes `game/machines/*.machine` and `materials.json` from the Racket sources. |
| `src/HeroicInventions.Sim/` | Engine-independent C# solvers plus the `.machine` reader and `MachineRuntime`. |
| `tests/HeroicInventions.Sim.Tests/` | xUnit tests, including simulations of the generated blueprints. |
| `game/` | Godot 4.6 .NET project. `MachineView` builds any machine's scene from its definition. |

## Requirements

- **Godot 4.6.x, .NET edition.** The standard Godot download does not run C#.
- .NET SDK 8 or newer. The game targets `net8.0` and rolls forward to newer runtimes.
- Racket 9, only for writing machines. Generated `.machine` files are committed.

## Setup

Link the language package once so `#lang heroic` resolves:

```bash
raco pkg install --auto --link --name heroic racket/heroic
```

## Workflow

Edit or add a file in `racket/machines/`, then rebuild:

```bash
racket racket/build.rkt
```

Run the tests:

```bash
raco test racket/heroic/tests
```

```bash
dotnet test tests/HeroicInventions.Sim.Tests
```

Open `game/project.godot` in the Godot .NET editor and press Play. You get a menu: pick a machine from the eight currently defined (`aeolipile`, `herons-fountain`, `material-samples`, `pendulum-demo`, `lever-demo`, `inclined-plane-demo`, `newtons-cradle`, `trebuchet`), and it runs alone with its own close-up camera.

**Controls:** click a machine to run it. **Restart** reloads it fresh from its `.machine` file — a true reset, not a rewind. **Run/Pause** and the speed row (0.1×–20×) control simulated time directly: the aeolipile needs real time to boil (~30s at 1×, hence its 5× default), while the gravity-driven demos are easiest to watch at 1×. The **Window size** row and **Fullscreen** toggle resize the game window without leaving it. Keyboard shortcuts mirror the buttons: **Space** pause/run, **F** fire on/off, **R** restart, **D** toggle details, **Esc** back to the menu, **1**–**9** pick a machine by its menu position.

**The HUD is deliberately small.** By default it shows a curated energy summary, not a dump of every raw number: total mechanical energy and its kinetic/potential split, a single headline speed, and — depending on the machine — either an **efficiency** figure (heat-driven machines: how much of the delivered heat became motion; the aeolipile's is a genuinely tiny fraction, which is real and part of the point) or an **energy retained** figure (gravity-driven machines: how much of the starting mechanical energy is still in the system, which drops as friction and bearing damping dissipate it). Click **Show details** (or press **D**) for the full per-part numbers — temperatures, pressures, litres, rpm — when you want them.

Set `HEROIC_DEBUG_PHYSICS=1` to print each dynamic body's rotation/height and the energy summary to the console twice a second — useful for checking a new machine's physics without needing to look at the screen.

## Demo

[docs/demo.mp4](docs/demo.mp4) — real Metal-rendered footage, all three demo machines running: the aeolipile boils and spins up, Heron's fountain jets, and four material cubes fall and settle differently by material.

Two environment variables make headless recording and remote control possible without a keypress:

```bash
# Start in Run mode instead of paused (for capture, or just to skip Space)
HEROIC_AUTORUN=1 godot-mono --path game scenes/Main.tscn

# Record a movie without opening an interactive window (Godot's Movie Maker
# mode: it renders real frames but advances simulated time, not wall time,
# so 90 simulated seconds takes well under a minute to record)
HEROIC_AUTORUN=1 godot-mono --path game scenes/Main.tscn \
  --write-movie demo.avi --fixed-fps 30 --quit-after 2900

# Drive the running game live from Racket (needs HEROIC_LIVE_LINK=1 at launch)
racket -e '(require "racket/heroic/live.rkt") (live-connect)
           (live-get (quote aeolipile) (quote kettle) (quote temperature))
           (live-set! (quote aeolipile) (quote kettle) (quote fire) 6000)'
```

When exporting the game, add `*.machine` to the export preset's "Filters to export non-resource files" so the machine files are included.
