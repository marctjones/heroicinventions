# Building phase 5: the vault by mouse (plan, 2026-10-13, for #254)

Planned by Fable, read-only, from main 7fca779 (line numbers at that commit). `BM` = game/scripts/BuildMode.cs, `PAL` = game/scripts/BuildMode.Palette.cs, `MR` = src/HeroicInventions.Sim/Machines/MachineRuntime.cs. Owner rules: allow don't forbid; physics stays real (nothing in Thermo/, MachineRuntime's builders or WorldZones changes); show state in the scene; keep scope light; one worktree; every number worked BEFORE the run, as a trace.

## 1. What exists

The vault is already buildable by mouse except three typed things (docs/e2e-route.md; tools/hand-vault-gui.sh): the room's three sizes typed; the rock and bin landed on the surface and sunk by Depth slider / Page keys; the bin's `holds` picked from a list of every part. The night numbers (room -55/-55, rock 40 kg at 200 C, bin open) were never set by mouse; hand-vault-test (racket/heroic/tests/hand-vault-test.rkt) proves +4.279 C with typed `build (...)`.

Pattern to copy is phase 2's: a slot consulted in `MoveGhost` before the free ghost (BM:1941-1963), a `Place...` in `PlaceGhost` (BM:2016-2042), a pure `...Slots` class in src/HeroicInventions.Sim/Editor/ (ShaftSlots, MeshSlots, StageSlots) with Sim.Tests, a drawn guide with `NoDepthTest` (BuildMode.Shaft.cs DrawAxle 143-161), an overlay so the ghost reads through what hides it (ShaftGhostOverlay, Shaft.cs:82-86), and a script word that does what the click does without the camera (`gear-on`, Shaft.cs:174-211).

Presets: `Presets` (PAL:187-195, posts only), `PresetOf`/`ChoosePreset`/`ExtraProps` (PAL:200-217), card buttons (PAL:456-473), the `preset NAME SIZE` word (BM:365-389). Inspector: BM:1093-1278; Depth row BM:1693-1724; `NumberField` commits on Enter and focus loss (BM:1306-1312); a part-naming `SSymbol` is an OptionButton of every part with "(choose one)" first when `?` (BM:1178-1190).

Facts: an enclosure's `#:at` is the middle of its floor and the box rises `size-y` above it (`WorldZones.Inside`, WorldZones.cs:52-55). `#:material` is only the wall's colour; physics is `#:wall` (MR:1225). A heat store's `#:at` is its base. A bin that `holds` a store is drawn at the store's `#:at` (MR:617). Reference kinds in MR: `holds` heat-store (601-607), `drives` heat-bin with no strip yet (642-645), `senses` heat-store or enclosure (646-648), `onto` whatever `HeatedNamed` takes (686-688). Live edits rebuild the machine and carry running state only where the setting is unchanged from the baseline (`TakeStateFrom`, MR:239-275; `StateCopy.Carry`), so `(set heat_store_2 #:temperature 200)` after placing does start the rock at 200. `build WORD` reaches `BuildMode.ScriptStep` (Main.Build.cs:226-229) -> `EditorStep` (BM:353-546). `click-at` projects through the camera (fine in a hidden window, nonsense headless), so the headless tier needs a camera-free word.

## 2. The acceptance number, worked before running

Network (hand-vault-test job 0, lonely-rover-opening, headless 120 Hz, `battery-bank:cells temperature -55 0`): a 0.5 m room with `#:pressure 610 #:temperature -55 #:ground -55` (WithPlaceDefaults makes it `#:wall regolith #:insulation 0` on Mars, BM:766-773); 40 kg basalt at 200 C; a `heat-bin` holding it, `#:open 1 #:leak 0.1`; the found bank's cells (16 kJ/K) inside the room's box; read at 36,990-37,020 s (03:00). Worked in racket/machines/night-heat.rkt's header (wall conductance 3.0 W/K after an hour, 1.0 at dawn; rock 823.8 W on 33.6 kJ/K at the start, 0.0245 K/s; bank 0.75 W/K on 16 kJ/K, tau 5.9 h); traced: bank **+4.28 C** at 03:00 (vault-zone-test), **+4.279 C** by the typed vault (rock 28.7 C, room 5.5 C, insulation 0). The implementer's first act: run hand-vault-test alone and re-read the `HAND VAULT:` line; that trace is what the script header cites.

**The strip changes the network.** A bimetal that `drives` the bin takes the lid over (MR:642-653): shut at 40 C, open about 35 K lower. Early in the night the cavity sits near 40 C, so a strip would throttle the lid for hours and the bank at 03:00 would NOT be +4.28. Therefore the acceptance night is hand-vault-test's network exactly: room, rock, bin with `open 1`, **no strip driving the bin**. The strip's placement and defaults are proven in the log and in the mouse script, which places it and then deletes it before the night (one real Delete; `(remove bimetal_4)` asserted). A night with the strip is a later run if the owner wants it: first a typed variant of hand-vault-test job 0 for that trace number.

**"By mouse" bar:** no `build (...)` line, no part name, no depth, no size typed. The night's scenario numbers (room -55, ground -55, pressure 610; rock mass 40, temperature 200; bin open 1) are typed into the inspector's own fields by a real click and keys. Presets stay size presets; a "200 C" button would be a cheat.

**Prediction for tools/vault-mouse.sh (write into its header before the first run):**
1. `[build] new machine built-1 at (254.4x..254.5x -42.9x 137.9x)`.
2. `[BuildMode] preset enclosure: 0.5 m`; `> (enclosure enclosure_1 #:at (X Y Z) #:material bronze #:size-x 0.5 #:size-y 0.5 #:size-z 0.5 #:wall regolith #:insulation 0)`; `depth: enclosure_1 on the ground`.
3. Three `key shift+pagedown`: depth 0.50, 1.00, 1.50 m; `[zones] battery-bank.cells joined built-1.enclosure_1` after 1.50 and not before (the crate's middle is 1.35 m down; floor must be 1.35-1.85 m; hand-vault-gui saw the join at 1.40).
4. `[BuildMode] in enclosure_1: the ghost on its floor, Y m below the ground`; `> (heat-store heat_store_2 #:at (x 254.6x, room y, z 137.9) ...)`; `placed in enclosure_1: heat_store_2 on its floor`.
5. `> (begin (heat-bin heat_bin_3 ...) (set heat_bin_3 #:holds heat_store_2))`; `in enclosure_1: heat_bin_3 holds heat_store_2 (the room's only heat store)`.
6. `> (begin (bimetal bimetal_4 ...) (set bimetal_4 #:senses enclosure_1) (set bimetal_4 #:drives heat_bin_3))`; then Delete: `> (remove bimetal_4)`.
7. `(set enclosure_1 #:temperature -55)`, `#:ground -55`, `#:pressure 610`, `(set heat_store_2 #:mass 40)`, `(set heat_bin_3 #:open 1)`, LAST `(set heat_store_2 #:temperature 200)`. Each may appear twice if NumberField's focus-loss commit fires on the inspector rebuild: harmless, or add an `IsInsideTree()` guard.
8. Leave; `sleep until scene.elapsed above 37000`; last trace frame `cells.temperature` = **4.28 +- 0.3** (the mouse build spends 60-120 s more scene time, expect 4.2-4.28); `heat_store_2.temperature` about 28.7; `enclosure_1.insulation` 0; `enclosure_1.temperature` about 5.5.

## 3. The UI

**3a. Enclosure size presets (PAL).** Add `["enclosure"]` to `Presets` (PAL:187): "2 m" (usual size, first), "1 m", "0.5 m" (the tight vault round the found bank's crate, the route's). The card, `ExtraProps`, `BuildPartModel` and the `preset` word already work: `build preset enclosure 0.5`. Placed prop order differs from hand-vault-test's regex (hand-vault-test.rkt:67); the new test needs its own.

**3b. Size handle (inspector, BM).** `AddSizeRow(id, part)` after `AddDepthRow` (BM:1129), enclosure only: label "Size (m a side)", HSlider 0.3..4 step 0.05 (`Name = "SizeSlider"`, meta `size_handle`), and under it the same three preset buttons (meta `size_preset` + label). Preview while dragging: scale the room's drawn nodes (`NodesOf(id)`; `Scale = (v/w, v/h, v/d)` about the floor's middle is exact) and write the label; on `DragEnded` run ONE `(begin (set id #:size-x v) (set id #:size-y v) (set id #:size-z v))` so Ctrl+Z takes it back in one; the floor stays where it is. Log `[BuildMode] size: enclosure_1 0.5 m a side (floor 1.50 m below the ground)`. The three number fields stay for a non-cube. Script word `build size V`. A 3D corner handle is the heavier alternative; propose it only if the slider reads badly.

**3c. Inside a selected room (new game/scripts/BuildMode.Room.cs + src/HeroicInventions.Sim/Editor/RoomSlots.cs).**
- `RoomSlots.RoomOf(doc, selectedId)`: the selected part if an enclosure, else the innermost enclosure whose box holds the selected part's `#:at`, else null (after the rock is placed the rock is selected, and the bin must still go into the room).
- `RoomSlots.Floor(doc, roomId)`: floor y = `At.Y`, centre `(At.X, At.Z)`, half sizes `size-x/2`, `size-z/2`.
- `private string? _room` set in `StartEntry` (PAL:364-380) for any placing entry; cleared in `CancelPlacing` (BM:1836). `MoveGhostInRoom(screen, floor)` consulted in `MoveGhost` right after the `_shaft` line (BM:1948): ray ∩ plane y = floor (skip when `dir.Y >= 0`); if inside the footprint, ghost at `(snap x, floorY - _ghostBottom, snap z)`, the floor drawn as a translucent green quad with `NoDepthTest`, the ghost given a `NoDepthTest` overlay, status "Inside enclosure_1, on its floor: click to put it there. Alt places it on the surface, Esc cancels"; outside the footprint fall through to the free ghost. Alt frees. Shift keeps placing in the same room.
- `PlaceGhost` (BM:2016): when `_ghostInRoom`, ask `RoomSlots.Defaults(doc, _room, kind, probeSpec)` for the `?` references with exactly one candidate and run `(begin (place...) (set id #:KEY target) ...)` as one edit (one undo); with no defaults the bare command (an enclosure inside a room stays bare: `WithPlaceDefaults` matches only a `(enclosure ` prefix, BM:769). Log `placed in ROOM: ID on its floor at (x y z)` and a line per default.
- `RoomSlots.Candidates(doc, roomId, kind, key)`: parts inside the room of the kinds the runtime takes: `holds` -> heat-store not held by another bin; `drives` -> heat-bin not driven by another bimetal; `senses` -> the room itself first, then its heat-stores; `onto` -> the kinds `HeatedNamed` accepts (MR:686). `Defaults` returns a pair only when the list has exactly one entry (for `senses`, the room counts as the one). Sim tests `RoomSlotsTests`: one store -> the bin holds it; two stores -> none (the `?` stays, logged with the candidates); strip senses the room, drives the only bin; a held store -> none; a part outside -> not a candidate; `RoomOf` through a selected rock.
- Inspector dropdown (BM:1178-1190): list `RoomSlots.Candidates` first, then every other part as today.
- `build log` (BM:539) gains `room=<_room or none>`.
- Script word `build put X Z`: the placing ghost put down where a click at world (X, Z) would land it, without the camera (the selected room's floor if inside its footprint, else the ground), then `PlaceGhost` by the same path. Headless-safe.

**3d. `build field KEY VALUE`.** With a part selected: click "Show all its numbers" if folded (give it `SetMeta("numbers")`); find the row whose first Label is KEY, `EnsureControlVisible`, a real click on its LineEdit, `SelectAll()`, the text as real key events and Enter (make ScriptedInput's `type`/`key` cases callable: `public void Type(string)`, `public void Key(string)`, ScriptedInput.cs:120-139). Works for `SNumber` and `SBool` "not set" rows. Log `[BuildMode] field KEY: typed VALUE into ID's field`.

## 4. Scripts and tests

**tools/vault-mouse.sh [OUT_DIR]** (modelled on tools/route-mouse.sh: hidden window, `camera` before every shot, predicted/observed header, final `[build]|[BuildMode]|[zones]` grep; ~2-3 min). Steps: place the rover (`rover place 246.5 137.9 270`), real click "Build a new machine"; `build palette-click enclosure; build preset enclosure 0.5; build look ...; build hover-at 0.05 0 0` (shot), `click-at`; three `key shift+pagedown` (shot, `build log`); heat-store `hover-at 0.12 -1.5 0` (floor drawn green, shot) and `click-at`; heat-bin click-at; bimetal click-at (shot) then `key delete`; the `build select`/`build field` sequence of prediction 7 (shot); real Leave click; `sleep until scene.elapsed above 37000 limit 40000 paused`. Environment: `HEROIC_WORLD=lonely-rover-opening HEROIC_HINTS=0 HEROIC_SAVES_DIR=$out/saves HEROIC_SET="battery-bank:cells temperature -55 0" HEROIC_TRACE=$out/n HEROIC_TRACE_DT=600 HEROIC_QUIT_AFTER_SIM_SECONDS=37020`. Shots to docs/art/building/vault-mouse-NN-*.png; the owner looks at them before landing.

**racket/heroic/tests/vault-mouse-test.rkt** (opt in `HEROIC_GUI_VAULT=1`, like route-gui-test / build-ui-test:67): runs the script; asserts the log lines of section 2 in order and from the trace `cells.temperature` 4.28 +- 0.3, `enclosure_1.insulation` 0, `0 < t < 45`; prints `VAULT BY MOUSE: ...`.

**racket/heroic/tests/vault-ui-test.rkt** (headless, always on with Godot; pattern build-ui-test.rkt): `HEROIC_INPUT="wait 360; build new 254.5 137.9; wait 60; build palette enclosure; build preset enclosure 0.5; build put 0 0; key shift+pagedown; wait 5; (x3) ; build palette heat-store; build put 0.12 0; build palette heat-bin; build put 0.12 0; build palette bimetal; build put -0.19 0; build field open 1; build select enclosure_1; build size 1; build size 0.5; build log; wait 120; quit"`. Asserts the preset line and sizes 0.5; depths 0.50/1.00/1.50 and the join after 1.50; the store on the floor under the ground; the bin's and strip's `(begin ...)` with defaults; `(set heat_bin_3 #:open 1)`; the size handle's one `(begin (set enclosure_1 #:size-x 1) ...)` and back; a second case with two stores where `holds` stays `?` and the log names both candidates. Plus `RoomSlotsTests`.

## 5. Commits (each small, built and its own test green before the next)

1. `Presets["enclosure"]` + `build put X Z` + vault-ui-test case 1. conventions.md `build WORD` row.
2. RoomSlots.cs + RoomSlotsTests (pure, no Godot).
3. BuildMode.Room.cs (`_room`, MoveGhostInRoom, drawn floor and overlay, PlaceGhost defaults, dropdown order, `put` inside a room, `build log` room field) + vault-ui-test cases for store, bin, strip and the two-store room.
4. `build field KEY VALUE` + ScriptedInput `Type`/`Key` + `numbers` meta + NumberField double-commit guard if seen.
5. tools/vault-mouse.sh with predicted header, vault-mouse-test.rkt, docs/e2e-route.md "The vault by the mouse alone (phase 5)" predicted then observed, building-plan.md phase 5 + Status, docs/art/building shots, conventions.md. The owner's look at the shots before landing.
6. The size handle (3b) + `build size V` + its test. Last, so it is the cut if scope presses (not on the acceptance path; the preset is).

## 6. Risks

- Ghost and floor under opaque soil: need `NoDepthTest` overlays (ShaftGhostOverlay is the fix).
- `TakeStateFrom` carries running state while the vault runs live; set the rock to 200 C last and Leave promptly.
- The strip: never in the acceptance night.
- `WithPlaceDefaults` only sees a bare `(enclosure ` command: never wrap a room in `(begin ...)`.
- NumberField commits twice on a rebuild (BM:1310).
- A stale `_room` (room deleted while placing): check `Parts.ContainsKey` before placing (as PlaceGearOnShaft, Shaft.cs:118) and fall back to the free ghost.
- The hidden window's `wait` counts frames: keep waits short so the rock is hot for under two minutes of scene time.
- Timing tests (sleep-charge, rover-dig) flake under load: rerun alone before believing a failure.

## 7. How to test

Worktree: `git worktree add -b building-phase5 .claude/worktrees/phase5 main`; `export PLTCOLLECTS=$PWD/racket: HEROIC_GODOT=/Applications/Godot_mono.app/Contents/MacOS/Godot; racket racket/build.rkt; (cd game && dotnet build HeroicInventions.csproj -nologo -v q && $HEROIC_GODOT --headless --import)`. Per commit: `dotnet test tests/HeroicInventions.Sim.Tests -nologo -v q`; `raco make racket/heroic/main.rkt && raco test racket/heroic/tests/vault-ui-test.rkt` (and hand-vault-test, build-ui-test, build-script-test, lesson-test). Mouse acceptance: `HEROIC_GUI_VAULT=1 raco test racket/heroic/tests/vault-mouse-test.rkt`. Before landing: the full suite in the background (see docs/plans/handoff-2026-10-12.md) and the director's solve headless must print the win. Then `git merge --ff-only`, push, close #254 with worked (+4.28) against measured.
