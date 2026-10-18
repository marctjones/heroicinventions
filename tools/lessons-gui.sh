#!/usr/bin/env bash
# "Build five machines" (docs/plans/tutorial-plan.md) played in the real window by the mouse words alone: hidden (tools/gui-check.sh
# --hidden, never takes focus), real clicks through Godot's own input pipeline, a camera line before each shot. Each lesson in its own
# run with a fresh lesson folder in OUT_DIR, so the player's own progress (user://lessons) is left alone.
#
#   tools/lessons-gui.sh [OUT_DIR] [LESSON ...]   # LESSON: gears, windlass, crane, mill, generator (default: all); about two minutes each;
#                                                 # pulley, ramp, water (lessons 2-4's experiments, #197) only when named
#
# Shots are named as docs/art/tutorial keeps them, NN-lesson-SS-what.png, and each lesson's log is OUT_DIR/NN-lesson-run.log.
#   Lessons 2-4's experiments (named only): pulley: 06 the first Test's result, 07 the blocks swapped by the lesson (stone on the ground, wood
#   hung), 08 the second Test and done text; ramp: 04 the oak block beside the ball, 05 the Test and done text; water: 06 the lower tank on its
#   tall post, 07 the second Test and done text.
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
#   Lesson 8, mill: 01 the lit Post, its card's 10.6 m, the green tower; 02 the windmill's ghost on the tower's top; 03 the sails selected
#   and their lit "Add a stage"; 04 the first stage, framed; 05 four stages and the label "sails -> pinion-e: 256:1"; 06 testing; 07 the
#   Test's sentence and done text.
#   Lesson 9, generator: 01 the tower; 02 the sails; 03 three stages, 64:1; 04 the generator's ghost on the last pinion's shaft ("Put a
#   generator on this shaft" lit, Generator in its Which list); 05 the heat store on the ground (the step's own view); 06 the bank beside
#   it; 07 charging, the generator's dial and label (the Test step's view); 08 the Test's sentence and done text.
set -euo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
out="${1:-${TMPDIR:-/tmp}/lessons-gui}"
[ $# -gt 0 ] && shift
lessons=("$@")
[ ${#lessons[@]} -eq 0 ] && lessons=(gears windlass crane mill generator)
mkdir -p "$out"

play() {
  local prefix=$1; shift
  shot() { echo "camera; shot $out/$prefix-$1.png"; }
  local steps=()
  case $prefix in
    02-pulley) steps=(
      "wait 60; lesson pulley; wait 30; palette-click post; wait 10; hover-at 0 0 0; wait 10; click-at 0 0 0; wait 20"
      "palette-click pulley; wait 10; hover-at 0 1.0 0; wait 20; click-at 0 1.0 0; wait 20"
      "palette-click block; wait 10; hover-at 0.1 0 0; wait 20; click-at 0.1 0 0; wait 20"
      "palette-click block; wait 10; hover-at -0.1 0 0; wait 20; click-at -0.1 0 0; wait 20"
      "palette-click rope; wait 10; click-part heavy; wait 10; click-part light; wait 20; key escape; wait 20"
      "test; wait 300; wait-test; wait 30; $(shot 06-the-first-test)"
      "wait 30; $(shot 07-the-blocks-swapped)"
      "test; wait 300; wait-test; wait 30; log; $(shot 08-the-second-test-and-done); quit") ;;
    03-ramp) steps=(
      "wait 60; lesson ramp; wait 30; palette-click ramp; wait 10; hover-at 0 0 0; wait 20; click-at 0 0 0; wait 20"
      "palette-click ball; wait 10; hover-at 0 0.3 -0.85; wait 20; click-at 0 0.3 -0.85; wait 20"
      "test; wait 900; wait-test; wait 30"
      "palette-click block; wait 10; hover-at 0.1 0.3 -0.8; wait 5; hover-at 0.15 0.3 -0.85; wait 20; click-at 0.15 0.3 -0.85; wait 20; $(shot 04-the-block-beside-the-ball)"
      "test; wait 900; $(shot 05a-testing); wait-test; wait 30; log; $(shot 05-the-block-held-and-done); quit") ;;
    04-water) steps=(
      "wait 60; lesson water; wait 30; palette-click post; wait 10; hover-at 0 0 0; wait 20; click-at 0 0 0; wait 20"
      "palette-click tank; wait 10; hover-at 0 1.0 0; wait 20; click-at 0 1.0 0; wait 20"
      "palette-click tank; wait 10; hover-at 0.8 0 0; wait 20; click-at 0.8 0 0; wait 20"
      "click-port upper.outlet; wait 10; click-port lower.inlet; wait 30"
      "test; wait 600; wait-test; wait 30; $(shot 05-the-first-test)"
      "wait 120; $(shot 06-the-lower-tank-raised)"
      "test; wait 300; wait-test; wait 30; log; $(shot 07-no-water-moved-and-done); quit") ;;
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
    08-mill) steps=(
      "wait 60; lesson mill; wait 30; palette-click post; wait 10; hover-at 0.8 0 -1.0; wait 20; $(shot 01-the-tower)"
      "click-at 0.45 0 -1.45; wait 20"
      "palette-click windmill; wait 10; hover-at 0.45 10.6 -1.45; wait 20; $(shot 02-the-sails-on-the-tower); click-at 0.45 10.6 -1.45; wait 20"
      "$(shot 03-the-sails-and-add-a-stage)"
      "add-stage; wait 30; $(shot 04-the-first-stage)"
      "add-stage; wait 30; add-stage; wait 30; add-stage; wait 30; $(shot 05-four-stages-256-to-1)"
      "test; wait 900; $(shot 06-testing); wait-test; wait 30; log; $(shot 07-the-test-and-done); quit") ;;
    09-generator) steps=(
      "wait 60; lesson generator; wait 30; palette-click post; wait 10; hover-at 0.8 0 -1.0; wait 20; $(shot 01-the-tower)"
      "click-at 0.45 0 -1.45; wait 20"
      "palette-click windmill; wait 10; hover-at 0.45 10.6 -1.45; wait 20; $(shot 02-the-sails-on-the-tower); click-at 0.45 10.6 -1.45; wait 20"
      "add-stage; wait 30; add-stage; wait 30; add-stage; wait 30; $(shot 03-three-stages-64-to-1)"
      "gear-on generator hover; wait 10; hover-at 0.675 11.05 -1.6; wait 5; hover-at 0.675 11.05 -1.74; wait 20; $(shot 04-the-generator-on-the-pinion-s-shaft); click-at 0.675 11.05 -1.74; wait 20"
      "palette-click heat-store; wait 10; hover-at 2.5 0 -3; wait 20; $(shot 05-the-heat-store); click-at 2.5 0 -3; wait 20"
      "palette-click battery-bank; wait 10; hover-at 3.5 0 -3; wait 20; $(shot 06-the-bank); click-at 3.5 0 -3; wait 20; key escape; wait 10"
      "test; wait 2100; $(shot 07-charging); wait-test; wait 30; log; $(shot 08-the-test-and-done); quit") ;;
  esac
  mkdir -p "$out/lessons-$prefix"
  local script
  script="$(IFS=';'; echo "${steps[*]}")"
  GUI_CHECK_TIMEOUT="${GUI_CHECK_TIMEOUT:-300}" "$here/tools/gui-check.sh" --hidden \
    HEROIC_EDITOR=1 HEROIC_LESSONS_DIR="$out/lessons-$prefix" -- "$script" | tee "$out/$prefix-run.log" | grep -E '\[Lesson\]|train:|facts:|shot |error' || true
}

for lesson in "${lessons[@]}"; do
  case $lesson in
    pulley) play 02-pulley ;;
    ramp) play 03-ramp ;;
    water) play 04-water ;;
    gears) play 05-gears ;;
    windlass) play 06-windlass ;;
    crane) play 07-crane ;;
    mill) play 08-mill ;;
    generator) play 09-generator ;;
    *) echo "no lesson $lesson (gears, windlass, crane, mill, generator, pulley, ramp, water)" >&2; exit 2 ;;
  esac
done
