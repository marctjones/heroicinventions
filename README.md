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

Open `game/project.godot` in the Godot .NET editor and press Play. Every `.machine` file in `game/machines/` appears in a row.

Controls: **Space** switches between build and run, **F** turns fires on and off, **T** runs time at 1× or 10×, **R** reloads the machine files. After a rebuild, pressing R picks up your changes without restarting the game.

When exporting the game, add `*.machine` to the export preset's "Filters to export non-resource files" so the machine files are included.
