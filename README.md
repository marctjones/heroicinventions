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
| `racket/heroic/geometry/` | Generated part shapes: gears, Archimedes' screws, pulleys, drums, treadwheels, norias, Vitruvian catapults; glTF writer; standard-parts catalogue. |
| `racket/machines/` | Blueprints, one machine per file: Heron's aeolipile and fountain, the Antikythera lunar train, Vitruvius's screw, crane and catapulta, a noria, a trebuchet, Newcomen's engine, and classic mechanics demos. |
| `racket/build.rkt` | Writes `game/machines/*.machine`, `game/meshes/*.glb`, the catalogue, and `materials.json` from the Racket sources. |
| `src/HeroicInventions.SimHost/` | Runs a machine's solvers without Godot, for `simulate` in Racket tests. |
| `src/HeroicInventions.Sim/` | Engine-independent C# solvers plus the `.machine` reader and `MachineRuntime`. |
| `tests/HeroicInventions.Sim.Tests/` | xUnit tests, including simulations of the generated blueprints. |
| `game/` | Godot 4.6 .NET project. `MachineView` builds any machine's scene from its definition. |

## Download and run

Releases are zips of the exported game (macOS, Windows, Linux), made by `tools/release.sh` and listed on the repository's Releases page; how a release is cut is in [docs/release.md](docs/release.md). Players need neither Godot nor Racket nor .NET.

- **macOS:** unzip and open `HeroicInventions.app`. The app is not yet signed or notarised, so the first time right-click it and choose Open (or run `xattr -dr com.apple.quarantine HeroicInventions.app`).
- **Windows:** unzip and run `HeroicInventions.exe`; keep the `.pck` and the other files beside it.
- **Linux:** unzip, `chmod +x HeroicInventions.x86_64`, run it; keep the `.pck` beside it.

The release builds are alpha: see the known issues in [docs/release.md](docs/release.md).

## Licence

**Pending.** No licence has been chosen yet (issue #92), so there is no LICENSE file, and until one is added all rights are reserved by the author. This section will be replaced when the owner decides.

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

**Camera (the same in run view and build mode):** drag to orbit (right-drag in build mode), **Shift**+drag or middle-drag to pan, scroll or pinch to zoom (two-finger trackpad scrolling zooms too, and pans with **Shift**). From the keyboard: **arrow keys** move over the ground (also **W A S D** in build mode), **Shift**+arrows orbit, **+**/**−** or **Page Up**/**Page Down** zoom, **Home** goes back to the starting view (build mode: frames the whole design). Taking the camera while a trebuchet's stone is in flight stops the camera following it.

**Building with the mouse (build mode):** each part in the palette shows a thumbnail of itself. Drag a part out of the palette and let go over the scene to place it there, or click it in the palette and then click in the scene (hold **Shift** to keep placing). Drag a placed part to move it (**Ctrl** to raise or lower it). Hold **R** and drag a part to turn it: half a degree a pixel, in 15° steps while grid snap is on (**G** turns snapping off for any angle); each turn is one undo. **T** / **Shift+T** turn it by 15°.

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

### Checking the game window without losing your desktop

`tools/gui-check.sh` runs the real game window with a scripted sequence of mouse and key steps (see `game/scripts/ScriptedInput.cs`) and prints its log. It opens the window behind your other apps (`open -g`) and the window never takes the keyboard (`HEROIC_BACKGROUND=1`); the steps go through Godot's own input pipeline, not your mouse and keyboard, so you can keep working while it runs. `shot PATH` saves what the window shows.

```bash
tools/gui-check.sh HEROIC_AUTOSELECT=pendulum-demo -- "wait 40; pause; hold right 1; camera; shot /tmp/after.png; quit"
```

Pass `HEROIC_EDITOR=1` to script build mode instead, which adds steps such as `palette tank`, `palette-drag tank 640 400` and `click-part tank_1`; `press r` / `release r` hold a key across a drag. `racket/heroic/tests/editor-gestures-test.rkt` runs these headless.

If you add a part keyword to `machine.rkt` and `#lang heroic` doesn't know it, recompile the language: `raco make racket/heroic/main.rkt`. Its `all-from-out` export list is fixed when `main.rkt` compiles, and the old `.zo` stays in use while `main.rkt` itself is unchanged.

When exporting the game, add `*.machine` to the export preset's "Filters to export non-resource files" so the machine files are included.
