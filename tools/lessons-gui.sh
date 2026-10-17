#!/usr/bin/env bash
# "Build five machines" (docs/plans/tutorial-plan.md) played in the real window by the mouse words alone: hidden (tools/gui-check.sh
# --hidden, never takes focus), real clicks through Godot's own input pipeline, a camera line before each shot. Each lesson in its own
# run with a fresh lesson folder in OUT_DIR, so the player's own progress (user://lessons) is left alone.
#
#   tools/lessons-gui.sh [OUT_DIR] [LESSON ...]   # LESSON: gears, windlass, crane (default: all three); a minute and a half or two each
#
# Shots are named as docs/art/tutorial keeps them, NN-lesson-SS-what.png, and each lesson's log is OUT_DIR/NN-lesson-run.log.
#   Lesson 5, gears: 01 the lit Post and the green post; 02 the lit Gear, its card's 72 and the green gear over the post; 03 the crank
#   selected and its lit "Add a meshing gear"; 04 the 18's ghost on the green circle; 05 the 18 selected and its lit button; 06 the 36's
#   ghost on its circle; 07 the three gears and the live ratio's label "crank -> third: 2:1" (the parts are named by their lesson roles);
#   08 the lit Test it; 09 the Test's sentence and the lesson's done text.
#   Lesson 6, windlass: 01 the lit Post, its card's 3 m pressed, the green post; 02 the lit Winding drum and its ghost over the post; 03
#   the drum selected, "Put a wheel on this shaft" lit and Wheel in its Which list; 04 the wheel's ghost on the drum's axle line; 05 the
#   block's ghost under the drum's edge; 06 the rope wound on the drum, the block hung; 07 testing; 08 the Test's sentence and done text.
#   Lesson 7, crane: 01 the lit Treadwheel and the green wheel; 02 "Put a drum on this shaft" lit and the drum's ghost clear of the wheel;
#   03 the green jib post; 04 the pulley's ghost on the post's top; 05 the stone; 06 the rope over the pulley, the stone hung; 07 testing;
#   08 the Test's sentence and done text.
set -euo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
out="${1:-${TMPDIR:-/tmp}/lessons-gui}"
[ $# -gt 0 ] && shift
lessons=("$@")
[ ${#lessons[@]} -eq 0 ] && lessons=(gears windlass crane)
mkdir -p "$out"

play() {
  local prefix=$1; shift
  shot() { echo "camera; shot $out/$prefix-$1.png"; }
  local steps=()
  case $prefix in
    05-gears) steps=(
      "wait 60; lesson gears; wait 30; $(shot 01-post)"
      "palette-click post; wait 10; hover-at 0 0 0; wait 10; click-at 0 0 0; wait 20"
      "palette-click gear; wait 10; hover-at 0.02 1.0 0; wait 20; $(shot 02-gear-on-the-post); click-at 0 1.0 0; wait 20"
      "$(shot 03-the-crank-and-its-button)"
      "mesh-with 18 hover; wait 10; hover-at 0.225 1.19 0; wait 20; $(shot 04-the-18-on-its-circle); click-at 0.225 1.19 0; wait 20"
      "$(shot 05-the-18-and-its-button)"
      "mesh-with 36 hover; wait 10; hover-at 0.36 1.19 0; wait 20; $(shot 06-the-36-on-its-circle); click-at 0.36 1.19 0; wait 20"
      "key escape; wait 20; $(shot 07-the-ratio-label)"
      "$(shot 08-test-it)"
      "test; wait 300; $(shot 08b-testing); wait-test; wait 30; log; $(shot 09-the-test-and-done); quit") ;;
    06-windlass) steps=(
      "wait 60; lesson windlass; wait 30; palette-click post; wait 10; hover-at 0.3 0 0.3; wait 20; $(shot 01-the-post)"
      "click-at 0 0 0; wait 20"
      "palette-click drum; wait 10; hover-at 0 3.0 0; wait 20; $(shot 02-the-drum-on-the-post); click-at 0 3.0 0; wait 20"
      "$(shot 03-the-drum-and-its-button)"
      "gear-on cart-wheel-120cm hover; wait 10; hover-at 0 3.22 -0.3; wait 5; hover-at 0 3.22 -0.39; wait 20; $(shot 04-the-wheel-on-the-axle); click-at 0 3.22 -0.39; wait 20"
      "palette-click block; wait 10; hover-at 0.15 0 0; wait 20; $(shot 05-the-block); click-at 0.15 0 0; wait 20"
      "palette-click rope; wait 10; click-part drum; wait 10; click-part load; wait 20; key escape; wait 20; $(shot 06-the-rope-wound-on-the-drum)"
      "test; wait 600; $(shot 07-testing); wait-test; wait 30; log; $(shot 08-the-test-and-done); quit") ;;
    07-crane) steps=(
      "wait 60; lesson crane; wait 30; palette-click treadwheel; wait 10; hover-at 0.6 0 0.6; wait 20; $(shot 01-the-treadwheel)"
      "click-at 0 0 0; wait 20"
      "gear-on drum-30cm hover; wait 10; hover-at 0 2.4 -1.0; wait 5; hover-at 0 2.4 -1.25; wait 20; $(shot 02-the-drum-on-the-wheel-s-axle); click-at 0 2.4 -1.25; wait 20"
      "palette-click post; wait 10; hover-at -3.7 0 -1.0; wait 20; $(shot 03-the-jib-post); click-at -4 0 -1.25; wait 20"
      "palette-click pulley; wait 10; hover-at -4.12 3.0 -1.25; wait 20; $(shot 04-the-pulley-on-the-post); click-at -4.12 3.0 -1.25; wait 20"
      "palette-click block; wait 10; hover-at -4.45 0 -1.25; wait 10; click-at -4.45 0 -1.25; wait 20; key escape; wait 10; $(shot 05-the-stone)"
      "palette-click rope; wait 10; click-part drum; wait 10; click-part stone; wait 20; key escape; wait 20; $(shot 06-the-rope-over-the-pulley)"
      "test; wait 600; $(shot 07-testing); wait-test; wait 30; log; $(shot 08-the-test-and-done); quit") ;;
  esac
  mkdir -p "$out/lessons-$prefix"
  local script
  script="$(IFS=';'; echo "${steps[*]}")"
  GUI_CHECK_TIMEOUT="${GUI_CHECK_TIMEOUT:-300}" "$here/tools/gui-check.sh" --hidden \
    HEROIC_EDITOR=1 HEROIC_LESSONS_DIR="$out/lessons-$prefix" -- "$script" | tee "$out/$prefix-run.log" | grep -E '\[Lesson\]|train:|facts:|shot |error' || true
}

for lesson in "${lessons[@]}"; do
  case $lesson in
    gears) play 05-gears ;;
    windlass) play 06-windlass ;;
    crane) play 07-crane ;;
    *) echo "no lesson $lesson (gears, windlass, crane)" >&2; exit 2 ;;
  esac
done
