#!/usr/bin/env bash
# The vault round the found bank, built by the mouse alone (building phase 5's acceptance, #254, docs/plans/building-phase5-plan.md).
# The real window (tools/gui-check.sh --hidden, never takes focus), real mouse and key events through Godot's own input pipeline, in
# lonely-rover-opening with the bank's cells at -55 C (HEROIC_SET, as hand-vault-test's night). No `build (...)` line, no part name, no
# depth, no size is typed: the room is a click on the card's 0.5 m button and a click on the ground, sunk by three Shift+Page Downs; the rock,
# the bin and the strip are palette clicks and clicks on the room's floor; the night's numbers are typed into the inspector's own fields.
#
#   tools/vault-mouse.sh [OUT_DIR]     # a few minutes of wall time; OUT_DIR gets the shots (a camera line before each), the trace, run.log
#   VAULT_MOUSE_BUILD_ONLY=1 tools/vault-mouse.sh OUT   # the build and its shots only, no night (to look at the views)
#
# Steps (R = a real click or key; `build select` / `build field` are what a click on the part / on its field and typing do, by name):
#   1  R  the rover is put 8 m west of the bank's crate (254.5, 137.9) facing +x; "Build a new machine" (left panel, 126 166)
#   2  R  `build palette-click enclosure` (a click on Show all, then the list's Enclosure), `build preset enclosure 0.5` (a click on
#         the card's 0.5 m), `build click-at` the ground over the crate: the room on the ground; three Shift+Page Down sink it 0.5 m each
#   3  R  `build palette-click heat-store`, `build hover-at` / `build click-at` on the room's floor 1.5 m down (the ray meets the floor
#         plane; the floor is drawn green and the ghost seen through the soil): the rock; the heat bin the same way (it holds the rock);
#         the bimetal strip the same way (it senses the room and drives the bin); then the Delete key takes the strip out again
#   4  R  `build select` + `build field`: the room's temperature -55, ground -55, pressure 610; `build choose wall none`, `build choose
#         wall regolith` (a click on the Wall list, the entry by the arrow keys: the soil starts afresh at the ground's -55 C, see the first
#         run below); the rock's mass 40; the bin's open 1; the rock's temperature 200 LAST (the rock starts hot when the machine runs); a
#         click on "Leave" (198 781)
#   5     `sleep until scene.elapsed above 37000 limit 40000 paused`; the run ends at 37,020 s (HEROIC_QUIT_AFTER_SIM_SECONDS)
#
# The strip is NOT in the night. A bimetal strip that drives the bin takes the lid over (MachineRuntime: shut at 40 C, open about 35 K
# lower): early in the night the cavity sits near 40 C, so a strip would throttle the lid for hours and the bank at 03:00 would not be
# +4.28 C. The acceptance night is hand-vault-test's network exactly: room, rock, bin with `open 1`, no strip driving the bin. The
# strip's placement and defaults are proven in the log; it is deleted before the night. (A night with the strip is a later run if the
# owner wants it: first a typed variant of hand-vault-test job 0 for that trace number.)
#
# Worked BEFORE the first run (docs/plans/building-phase5-plan.md section 2; racket/machines/night-heat.rkt's header; the typed vault of
# racket/heroic/tests/hand-vault-test.rkt, run alone at the start of this work: "HAND VAULT: at 03:00 the found bank's cells are 4.279079182341042 C
# (vault-zone-test: +4.28), the rock 28.690752607782144 C, insulation 0 W/K"). Wall conductance 3.0 W/K after an hour, 1.0 at dawn; the rock
# 823.8 W on 33.6 kJ/K at the start (0.0245 K/s); the bank 0.75 W/K on 16 kJ/K, tau 5.9 h.
# Predicted log lines, in order:
#  1  "[build] new machine built-1 at (254.4x..254.5x -42.9x 137.9x)"
#  2  "[BuildMode] preset enclosure: 0.5 m (size-x 0.5 size-y 0.5 size-z 0.5)"; "> (enclosure enclosure_1 #:at (X Y Z) #:material bronze
#     #:size-x 0.5 #:size-y 0.5 #:size-z 0.5 #:wall regolith #:insulation 0)" (Mars is below 0 C: WithPlaceDefaults)
#  3  three `key shift+pagedown`: "depth: enclosure_1 depth 0.50", "1.00", "1.50 m below the ground" (the room starts on the ground);
#     "[zones] battery-bank.cells joined built-1.enclosure_1" after 1.50 and not before (the crate's middle is 1.35 m down; the 0.5 m room's
#     floor must lie 1.35 to 1.85 m down)
#  4  "[BuildMode] in enclosure_1: the ghost on its floor, 1.5x m below the ground"; "> (heat-store heat_store_2 #:at (254.6 -44.4 137.9) ...)"
#     (the ground rises toward +x: 1.556 m at 254.6); "placed in enclosure_1: heat_store_2 on its floor"
#  5  "> (begin (heat-bin heat_bin_3 ...) (set heat_bin_3 #:holds heat_store_2))"; "in enclosure_1: heat_bin_3 holds heat_store_2 (the room's only heat store)"
#  6  "> (begin (bimetal bimetal_4 ...) (set bimetal_4 #:senses enclosure_1) (set bimetal_4 #:drives heat_bin_3))"; then Delete: "> (remove bimetal_4)"
#  7  the fields, each once and in this order: "(set enclosure_1 #:temperature -55)", "#:ground -55", "#:pressure 610", "(set heat_store_2 #:mass 40)",
#     "(set heat_bin_3 #:open 1)", and LAST "(set heat_store_2 #:temperature 200)"
#  8  Leave; the run to 37,020 s: the last trace frame cells.temperature = 4.28 +- 0.3 (the mouse build spends 60 to 120 s more scene time with the
#     rock cooling from 200 C at 0.0245 K/s at most, so expect 4.2 to 4.28), heat_store_2.temperature about 28.7, enclosure_1.insulation 0,
#     enclosure_1.temperature about 5.5.
# FIRST RUN (without the wall step), observed against the prediction above: cells -0.892 C at 37,000 s (predicted +4.2 to +4.28), the rock 25.36 C
# (28.7), the room 0.149 C (5.5); every log line as predicted. Cause, found after: a live edit carries the room's soil (TakeStateFrom carries the
# wall's cell temperatures, Array.Copy in StateCopy.Carry, whatever the setting says): the room is placed with no #:ground, so its soil starts at
# the air's -63 C, and typing ground -55 afterwards changes only the far face's setting; the soil stays -63 and soaks more of the rock's heat.
# Worked/traced to confirm, headless, hand-vault-test's job 0 with the room made first without #:ground and "(set vault #:ground -55)" after: cells
# -0.8915 C, rock 25.349 C, room 0.145 C (the mouse run: -0.8924, 25.36, 0.149); the same and then "(set vault #:wall #f)", "(set vault #:wall
# regolith)" (a fresh wall built at the ground's -55 C): cells +4.2789 C, rock 28.692 C, room 5.544 C: the typed vault's +4.279 exactly. So the
# script resets the soil as a player could: the room's Wall list, none then regolith, after the ground is typed (WallChanged puts the insulation
# back to 2 and then to 0). Nothing in the physics changes. SECOND PREDICTION: cells 4.279 +- 0.05 (the wall-off-and-on typed trace), rock 28.69,
# room 5.54, insulation 0; the mouse build's extra 60 to 120 s of scene time before the rock is hot moves it by under 0.01 C (the rock is set to
# 200 C last, and the cells, 16 kJ/K at 0.75 W/K, drift 0.05 K in 120 s).
# Observed: see docs/e2e-route.md, "The vault by the mouse alone (phase 5)".
set -euo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
out="${1:-${TMPDIR:-/tmp}/vault-mouse}"
mkdir -p "$out/saves"
shot() { echo "camera; shot $out/$1.png"; }
click() { echo "move $1 $2; wait 3; down $1 $2; up $1 $2"; }   # a click at window pixels (the window is 1280x800)
# the view: yaw pitch distance, pivot (x y z from the build's spot); the vault lies 1.5 m under the ground
view() { echo "build look $1 $2 $3 $4 $5 $6; wait 20"; }
steps=(
  "waitsim 8; rover place 246.5 137.9 270; wait 120; $(shot 01-near-the-bank)"
  "$(click 126 166); wait 60; $(shot 02-build-mode)"
  # 2: the room, a card button and a click on the ground over the crate; the bank lies under it (the marker drawn)
  "build palette-click enclosure; wait 20; build preset enclosure 0.5; wait 20; $(view 215 45 4.5 0 0 0); build hover-at 0 0 0; wait 20; $(shot 03-room-ghost-0.5m); build click-at 0 0 0; wait 30; $(shot 03b-room-placed-on-the-ground); build log"
  "key shift+pagedown; wait 20; key shift+pagedown; wait 20; $(view 215 45 4.5 0 -0.8 0); $(shot 04-sunk-1m-no-bank-yet); key shift+pagedown; wait 40; $(shot 05-sunk-1.5m-the-bank-joins); build log"
  # 3: the rock, the bin and the strip on the room's floor
  "build palette-click heat-store; wait 20; $(view 215 50 3 0 -1.4 0); build hover-at 0.12 -1.5 0; wait 20; $(shot 06-rock-ghost-on-the-floor); build click-at 0.12 -1.5 0; wait 30"
  "build palette-click heat-bin; wait 20; build hover-at 0.12 -1.5 0; wait 20; build click-at 0.12 -1.5 0; wait 30; $(shot 07-bin-holds-the-rock)"
  "build palette-click bimetal; wait 20; build hover-at -0.19 -1.5 0; wait 20; build click-at -0.19 -1.5 0; wait 30; $(shot 08-strip-senses-the-room-drives-the-bin); key delete; wait 30; build log"
  # 4: the night's numbers, typed into the inspector's own fields; the rock's 200 C last
  "build select enclosure_1; wait 10; build field temperature -55; wait 10; build field ground -55; wait 10; build field pressure 610; wait 10; build choose wall none; wait 20; build choose wall regolith; wait 20; build select heat_store_2; wait 10; build field mass 40; wait 10; build select heat_bin_3; wait 10; build field open 1; wait 10; build select heat_store_2; wait 10; $(shot 09-fields-typed); build field temperature 200; wait 20; $(shot 10-rock-at-200C); build list"
  "$(click 198 781); wait 60; $(shot 11-left-build-mode)"
)
if [[ -z "${VAULT_MOUSE_BUILD_ONLY:-}" ]]; then
  steps+=("sleep until scene.elapsed above 37000 limit 40000 paused; wait 2000")
else
  steps+=("quit")
fi
script="$(IFS=';'; echo "${steps[*]}")"
script="${script//;;/;}"
log="$out/run.log"
GUI_CHECK_TIMEOUT="${GUI_CHECK_TIMEOUT:-2400}" "$here/tools/gui-check.sh" --hidden \
  HEROIC_WORLD=lonely-rover-opening HEROIC_HINTS=0 HEROIC_SAVES_DIR="$out/saves" HEROIC_SET="battery-bank:cells temperature -55 0" \
  HEROIC_TRACE="$out/n" HEROIC_TRACE_DT=600 HEROIC_QUIT_AFTER_SIM_SECONDS=37020 -- "$script" > "$log" 2>&1 || echo "vault-mouse: gui-check exited $?" >&2
echo "vault-mouse: log $log, trace $out/n.battery-bank, shots $out/*.png"
grep -E "^\[(build|zones)\]|^\[BuildMode\] (preset|palette-click|> |depth:|in enclosure_1|placed in|field|state:)|camera:" "$log" | grep -v "input:" || true
