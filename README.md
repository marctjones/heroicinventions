# Heroic Inventions

A workshop game where players build historical machines one part at a time (Hero's aeolipile, Heron's fountain, Roman cranes, Leonardo's and Franklin's inventions) and then run them in a physics simulation.

Machines are written in **Racket** (`#lang heroic`), which handles design, compile-time checks, generated geometry, tests and live control from a REPL. They run in **Godot 4.6 with C#**, which handles rendering, Jolt physics and the per-tick solvers. The two meet through a plain S-expression data format (`.machine`), so the shipped game doesn't need Racket. The Racket side is planned. The C# runtime below exists.

Design document: [docs/design.html](docs/design.html), also published at https://claude.ai/artifact/6iP8BrM93Ao771cYoBZi3Z

## Layout

| Path | What |
|---|---|
| `src/HeroicInventions.Sim/` | Engine-independent simulation core: materials, fluid network, boiler, aeolipile, assembly graph. No Godot references. |
| `tests/HeroicInventions.Sim.Tests/` | xUnit tests for the core. |
| `game/` | Godot 4.6 .NET project (Jolt physics) that draws and drives the core. |
| `docs/` | Design document. |

## Requirements

- **Godot 4.6.x, .NET edition.** The standard Godot download does not run C#.
- Racket 9 (for the machine language, once it lands).
- .NET SDK 8 or newer. The game targets `net8.0` and rolls forward to newer runtimes.

## Run

```bash
dotnet test tests/HeroicInventions.Sim.Tests
```

Then open `game/project.godot` in the Godot .NET editor and press Play.

Controls: **Space** switches between build and run, **F** turns the fire on and off, **T** runs time at 1× or 10×, **R** resets.
