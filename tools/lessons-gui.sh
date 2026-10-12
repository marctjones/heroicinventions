#!/usr/bin/env bash
# Lesson 5, "Crank and gears" (the first of "Build five machines", docs/plans/tutorial-plan.md), played in the real window by the mouse
# words alone: hidden (tools/gui-check.sh --hidden, never takes focus), real clicks through Godot's own input pipeline, a camera line
# before each shot. A fresh lesson folder in OUT_DIR, so the player's own progress (user://lessons) is left alone.
#
#   tools/lessons-gui.sh [OUT_DIR]     # about a minute and a half of wall time; OUT_DIR gets the shots and run.log
#
# Shots, one a step: 01 the lit Post and the green post; 02 the lit Gear, its card's 72 and the green gear over the post; 03 the crank
# selected and its lit "Add a meshing gear"; 04 the 18's ghost on the green circle; 05 the 18 selected and its lit button; 06 the 36's
# ghost on its circle; 07 the three gears and the live ratio's label "... -> wheel_4: 2:1"; 08 the lit Test it; 09 the Test's sentence
# and the lesson's done text.
set -euo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
out="${1:-${TMPDIR:-/tmp}/lessons-gui}"
mkdir -p "$out/lessons"
shot() { echo "camera; shot $out/$1.png"; }
steps=(
  "wait 60; lesson gears; wait 30; $(shot 01-post)"
  "palette-click post; wait 10; hover-at 0 0 0; wait 10; click-at 0 0 0; wait 20"
  "palette-click gear; wait 10; hover-at 0.02 1.0 0; wait 20; $(shot 02-gear-on-the-post); click-at 0 1.0 0; wait 20"
  "$(shot 03-the-crank-and-its-button)"
  "mesh-with 18 hover; wait 10; hover-at 0.225 1.19 0; wait 20; $(shot 04-the-18-on-its-circle); click-at 0.225 1.19 0; wait 20"
  "$(shot 05-the-18-and-its-button)"
  "mesh-with 36 hover; wait 10; hover-at 0.36 1.19 0; wait 20; $(shot 06-the-36-on-its-circle); click-at 0.36 1.19 0; wait 20"
  "key escape; wait 20; $(shot 07-the-ratio-label)"
  "$(shot 08-test-it)"
  "test; wait 300; $(shot 08b-testing); wait-test; wait 30; log; $(shot 09-the-test-and-done); quit"
)
script="$(IFS=';'; echo "${steps[*]}")"
GUI_CHECK_TIMEOUT="${GUI_CHECK_TIMEOUT:-300}" "$here/tools/gui-check.sh" --hidden \
  HEROIC_EDITOR=1 HEROIC_LESSONS_DIR="$out/lessons" -- "$script" | tee "$out/run.log" | grep -E '\[Lesson\]|train:|facts:|shot |error' || true
