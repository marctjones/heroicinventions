#!/usr/bin/env bash
# A vault built round the found bank by mouse (issues #222, #223): in the opening world, in a real hidden window
# (tools/gui-check.sh --hidden, never takes focus), real mouse and key events through Godot's own input pipeline. No depth and no
# part name is typed; the room's three sizes are typed numbers (the inspector has no size handle).
#
#   tools/hand-vault-gui.sh [OUT_DIR]     # about 30 s of wall time; OUT_DIR gets the shots (a camera line is printed before each) and run.log
#
# Steps (every click is at window pixels, 1280x800, found by looking at the shots):
#   1  the rover is put 8 m west of the bank's crate (254.5, 137.9) facing +x; "Build a new machine" (left panel, 126 166)
#   2  the short list is up (Beam, Block ... Water tank) with the "More parts: keep things warm, ..." button under it: press it (100 700)
#   3  the full list opens with "Keep things warm" first: press Enclosure (100 143), then click the scene over the marked bank (640 500)
#   4  "Show all its numbers" (1115 273), scroll the panel, type 0.5 into size-x, size-y, size-z (a regolith cavity as the route's)
#   5  scroll back up; drag the Depth slider (1102 130) right until the label reads about 1.1 m: the crate is not in the room yet;
#      Page Down (0.1 m each) until it is
#   6  Heat store (100 178), click the scene, drag the Depth slider to the room; Lidded bin (100 213), the same; its "holds" list: heat_store
#   7  press Leave
# Predicted before the first run (the crate's middle is 1.04 m + 0.25 m under the surface; the room's floor lies at the depth d, 0.5 m
# high): the log says nothing of the bank while the room lies shallower than d = 1.3 m and
# "[zones] battery-bank.cells joined built-1.enclosure_1" from the first Page Down that takes it to 1.3 m or deeper (and no deeper than 1.8 m).
set -euo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
out="${1:-${TMPDIR:-/tmp}/hand-vault-gui}"
mkdir -p "$out/saves"
click() { echo "move $1 $2; wait 3; down $1 $2; up $1 $2; wait 40; "; }
size() { echo "$(click 1200 "$1") key meta+a; type 0.5; key enter; wait 40; "; }
shot() { echo "camera; shot $out/$1.png; "; }
S="waitsim 8; rover place 246.5 137.9 270; wait 120; $(shot 01-near-the-bank)"
S+="$(click 126 166) wait 60; $(shot 02-build-mode-short-list)"
S+="$(click 100 700) $(shot 03-more-parts)"
S+="$(click 100 143) $(click 640 500) $(shot 04-enclosure-placed-on-the-surface)"
S+="$(click 1115 273) wheel 1150 450 4; wait 40; $(size 389) $(size 419) $(size 449) $(shot 05-sized)"
S+="wheel 1150 450 -4; wait 40; drag 1102 130 1130 130; wait 40; $(shot 06-sunk-not-yet-at-the-crate)"
S+="key pagedown; wait 20; key pagedown; wait 20; key pagedown; wait 20; key pagedown; wait 40; $(shot 07-sunk-to-the-crate)"
S+="$(click 100 178) $(click 640 500) drag 1102 130 1142 130; wait 40; $(shot 08-heat-store)"
S+="$(click 100 213) $(click 640 500) drag 1102 130 1142 130; wait 40; key pagedown; wait 20; key pagedown; wait 20; key pagedown; wait 40; $(click 1210 332) $(click 1170 401) $(shot 09-bin-holds-the-store)"
S+="$(click 198 781) wait 60; $(shot 10-left-build-mode) quit"
GUI_CHECK_TIMEOUT="${GUI_CHECK_TIMEOUT:-300}" "$here/tools/gui-check.sh" --hidden \
  HEROIC_WORLD=lonely-rover-opening HEROIC_HINTS=0 HEROIC_SAVES_DIR="$out/saves" -- "$S" > "$out/run.log" 2>&1 || echo "hand-vault-gui: gui-check exited $?" >&2
grep -E "^\[(build|BuildMode|zones)\]|camera:" "$out/run.log" | grep -v "input:" || true
