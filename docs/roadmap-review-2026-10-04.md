# Roadmap review, 2026-10-04

A review of the issues, milestones and handoff material against the goal in
[lonely-rover.html](lonely-rover.html), with the working notes from the earlier
"Issue 76 handoff investigation" session folded in so they live in the repo, not
only in an issue comment or a chat. Issue numbers refer to
<https://github.com/marctjones/heroicinventions/issues>. The tracker itself was
not changed by this review; the "Proposed" items are suggestions.

## Goal

The Lonely Rover: a Mars rover that can't make electronics builds Heron-style
mechanisms, frees a buried battery bank from a rim collapse, charges it through a
geared salvaged motor, keeps it between 0 and 45 °C overnight, and calls Earth on
the 03:00 relay pass. Stages 1-4 (engine) are nearly done; stage 5 (the game) is
almost entirely open.

| Stage | Open / closed |
|---|---|
| 1 Sandbox world | 1 / 7 (#24) |
| 2 Mechanisms | 2 / 14 (#13, #47) |
| 3 Ground and water | 0 / 10 |
| 4 Heat and air | 1 / 9 (#71) |
| 5 The Lonely Rover | 9 / 1 |
| 6 Later | 3 / 0 (#6, #35, #77) |

## Where work from the other session stands

- Code: everything it wrote (#33 air drag and the damping retune, #67 save/load,
  PR #81) is on `main`; `origin/main`, `origin/wip/33-air-drag-ucrl3j` and the
  review branch are the same commit. `origin/wip/33-air-drag` is one commit
  behind. `origin/m5-editor` is an old pre-rewrite history whose files are all
  present on `main` (the editor is `BuildMode.cs`, `BuildSession.cs`,
  `PartTemplates.cs`, `RktExporter.cs`), so it can be deleted.
- Planning: it filed #82-#90 and posted a state comment on #79 (reproduced below).
  #79's body was deliberately not edited and is stale.

## Environment traps (from the #79 state comment)

- A fresh container has no Racket, .NET or Godot: `apt-get install racket
  dotnet-sdk-10.0`, Godot 4.7 .NET from the godotengine releases, then
  `export HEROIC_GODOT=<path to Godot_v4.7-stable_mono_linux.x86_64>`. Without
  `HEROIC_GODOT` the Jolt tests silently skip (`godot-available?`), so green can
  mean not run.
- `raco pkg install` does not work on `racket/`; use
  `export PLTCOLLECTS=<repo>/racket:` (also in worktrees).
- `racket racket/build.rkt` on a different Racket version rewrites many
  `.machine` files with last-digit float noise; do not commit those.
- `machine-test.rkt` takes over 25 minutes on a 1-core container; run it in the
  background.
- `#:sample-dt` coarser than 1/120 misses single-tick events (landings,
  tunnelling); use 1/120 for anything that depends on a strike or bounce.
- #33 changed the baseline: engine damping is 0 and air drag is per part
  (`#:drag-coefficient`). Tests that used to hide behind damping now show real
  behaviour (see #80).

## Alignment findings

1. Issues with no milestone: #80, #82-#90. Proposed homes: #82, #83, #85, #86 to
   stage 1; #80, #87 to stage 2; #84, #88, #89, #90 to stage 3 (or 6 for #89).
2. #88 (boulders from terrain collapse) blocks the rover: the design needs the
   bank pinned by boulders too big for the backhoe (#61's scenario test, and the
   Undermined, Lever and Siege-engineer routes). #54 moves terrain only.
3. #13 (friction on Jolt-driven wheels and hinges) is a hidden rover dependency:
   the 100:1 generator gear train needs loss at every stage.
4. #79's body is stale (lists closed issues as next, stops at #75, omits #80-#90).
   Regenerate it from labels and the state comment above.
5. #6's electrics half is superseded by #64; retitle to aero only or close.
   #77 largely duplicates the closed #74; narrow it to batching.

## Missing for the game (no issue today)

- The Heron's catalogue machines themselves (Dionysus shrine, Nauplius theatre,
  singing birds and owl, wind organ, coin dispenser, self-trimming lamp) and their
  decorative figure meshes. #46-#52 build parts and #68 detects achievements.
- A rover body: mesh, backhoe animation, camera and control scheme. The "rover as
  player" question in the design doc is unanswered.
- Front-end flow: new game and scenario selection, the opening sequence, an ending
  screen, a rover log screen (#68 mentions log entries, not the UI), a tutorial.
- An end-to-end test that the fastest route (3-5 sols) wins.
- A bimetal strip as its own part (mentioned in #64 and #68 only).
- Godot export and release (the `*.machine` export filter is only in the README).
- A LICENSE file, and a rule that third-party art and audio need compatible licences.

## Proposed milestone: "7 Look and feel"

No issue owns visual quality today; #85 and #86 are bug fixes. The renderer is
flat albedo per material, roughness from friction, a per-part brightness shift and
an inverted-hull outline, untextured procedural glTF, a procedural sky and one
shadowed sun. Proposed issues:

1. Art direction decision (stylised workshop look vs realistic PBR), written down.
2. Data-driven material look: visual fields in `materials.rktd`, generated
   procedurally so there are no asset-licence problems.
3. Part detail pass on primitive parts (rivets, spokes, bearings, fittings).
4. Crater terrain rendering: per-material colour, slope shading, dust, ice sheen,
   wet-sand darkening, water and ice surfaces, detail at 800 m scale.
5. Mars sky and light driven by sol time and #69's dust data.
6. State legibility checklist: frost on a cold bank, glow on a leaky lid, a
   stalled generator, each with a frame check.
7. Rover model and backhoe.
8. Heron figures and decor meshes.
9. UI theme: HUD, build palette thumbnails, rover log.
10. Screenshot regression, extending #86's per-machine framing check.

Move #85 and #86 into it.
