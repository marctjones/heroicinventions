#!/usr/bin/env bash
# The fastest route played as a person, the windmill built by the mouse alone (building plan, phase 4's acceptance,
# docs/plans/building-plan.md): tools/route-gui.sh with its typed `build (...)` lines replaced by palette clicks and buttons. The real
# window (tools/gui-check.sh --hidden, never takes focus), real mouse and key events through Godot's own input pipeline, in lonely-rover-e2e.
#
#   tools/route-mouse.sh [OUT_DIR]     # about 4 minutes of wall time; OUT_DIR gets the shots (a camera line before each), the trace, the saves and run.log
#
# What it does, in order (R = a real click or key; no `build (...)` line and no typed size anywhere):
#   1  R  drive the rover from its start to 8 m south of where the windmill goes (route-gui.sh's drive, the same keys)
#   2  R  press "Build a new machine"; "build palette-click post" (a click on the list's Post), "build preset post 10.6" (a click on the
#         card's 10.6 m button), "build click-at 0.45 0 -1.45" (a click on the ground where the route's tower stands)
#   3  R  "build palette-click windmill" (a click on Show all, then on the list's Windmill), "build click-at" the post's top: the sails' hub on
#         it (phase 1); the new windmill is selected
#   4  R  "build add-stage" four times: each a click on the selected part's "Add a stage" (the windmill's, then each new pinion's): a 72 on
#         its axle and an 18 meshing it, 72 drives 18 as the stage's lists start; the pinion selected after each, the pair framed
#   5  R  Esc (nothing selected), the view of the finished train (shot 07: the label "windmill_2 -> wheel_10: 256:1"); press "Leave"
#   6+ R  as route-gui.sh: Join machines and joinclick on the last pinion (wheel_10 here, pinion-e there), the rotor, the generator and the
#         bank; "Sleep until...", pre-dawn, Sleep; 20x after the wake
# The vault is lonely-rover-e2e's own, placed by the world round the found bank's crate (as in route-gui.sh; Phase 5, the vault by mouse,
# is not asked for).
# Predicted before the first run: "built-1: design post_1:post windmill_2:windmill wheel_3:wheel ... wheel_10:wheel; running 10 parts";
# wheel_3..wheel_10 the 72s and 18s alternately at (0 0 -1.3), (0.225 0 -1.3), (0.225 0 -1.4), (0.45 0 -1.4), (0.45 0 -1.5), (0.675 0 -1.5),
# (0.675 0 -1.6), (0.9 0 -1.6) from the hub, the route's wheel-a ... pinion-e (solve.steps:93-115) within 1 mm; arbors windmill_2+wheel_3,
# wheel_4+wheel_5, wheel_6+wheel_7, wheel_8+wheel_9 and meshes wheel_3+wheel_4 ... wheel_9+wheel_10, as the route's; "train: windmill_2 ->
# wheel_10, ratio 256:1 (4 meshes)"; the hub on the post's top (phase 1), so 5 cm higher than the route's typed (0 11 0) over the tower's
# ground, give or take the ground under the click. The run as route-gui.sh's (docs/e2e-route.md, "The route played as a person"): the bank
# full by the wake (54,800 s), the call at 55,484 to 55,495 s (03:00 on sol 2), a trace frame with bank.won 1 at 55,490 s and none before.
# Observed (2026-10-11, two runs to the win; the last after the stage view was turned face on): as predicted. The build's spot (178.69,
# 103.88) (the open-loop drive ends 1.5 m off route-gui.sh's recorded (180.12, 105.00); the spot is wherever the button opens it);
# post_1 #:size-y 10.6 at (179.05 -63.48 102.5); windmill_2 at (178.6 -52.43 103.95), the hub (-0.09 10.98 0.07) from the spot (the route
# types (0 11 0)); wheel_3..wheel_10 exactly the route's offsets from the hub, 72/18 alternately; the saved world's arbors windmill_2 wheel_3,
# wheel_4 wheel_5, wheel_6 wheel_7, wheel_8 wheel_9 and meshes wheel_3 wheel_4 ... wheel_9 wheel_10; "ratio 256:1 (4 meshes)"; joined: shaft
# shaft-1 from built-1.wheel_10 to motors.rotor, wire wire-1 from motors.motor to battery-bank.bank (each a plain click, no pick list); the
# sleep held the motor at 17.2 W (estimate); full at 32,095 s; at the wake cells 24.476 C, 25 Wh; the rover log "t=55484.4s route: ...
# Call Earth" and "t=55485.4s win"; first trace frame with bank.won 1 at 55,490 s (cells 24.60 C), none before; "ending source wind:
# 25.000 Wh". Shots: docs/art/building/route-mouse-06-first-stage.png (the first stage framed face on, its 4:1 label),
# route-mouse-07-the-mouse-built-train.png (the finished train, "windmill_2 -> wheel_10: 256:1").
set -euo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
out="${1:-${TMPDIR:-/tmp}/route-mouse}"
mkdir -p "$out/saves"
cam="camera"
shot() { echo "$cam; shot $out/$1.png"; }
click() { echo "move $1 $2; wait 3; down $1 $2; up $1 $2"; }   # a click at viewport pixels (the window is 1280x800)
steps=(
  "wait 100; rover; $(shot 01-start)"
  # 1: turn left about 115 degrees to face the windmill's spot, drive, turn back to face -z, drive the last stretch
  "hold left 3.2; wait 20; hold up 10; wait 10; hold right 0.8; wait 20; hold up 4.6; wait 300; rover; $(shot 02-at-windmill-spot)"
  # 2: the button, the palette's Post (a click), the card's 10.6 m (a click), a click on the ground where the route's tower stands
  "$(click 126 166); wait 60; $(shot 03-build-mode)"
  "build palette-click post; wait 20; build preset post 10.6; wait 20; build look 125 20 30 0 5.2 0; wait 20; build hover-at 0.45 0 -1.45; wait 20; $(shot 04-post-ghost); build click-at 0.45 0 -1.45; wait 20"
  # 3: the palette's Windmill (Show all, then the entry), a click on the post's top
  "build palette-click windmill; wait 20; build look 90 20 34 0 9.2 0; wait 20; build hover-at 0.45 10.6 -1.45; wait 20; $(shot 05-windmill-ghost-on-the-post); build click-at 0.45 10.6 -1.45; wait 20; build log"
  # 4: four stages, each one click on "Add a stage"
  "build add-stage; wait 20; $(shot 06-first-stage); build add-stage; wait 20; build add-stage; wait 20; build add-stage; wait 30; build log; build list"
  # 5: the finished train and its ratio, nothing selected; Leave
  "key escape; wait 10; build look 205 14 2.6 0.3 11.1 -1.4; wait 30; $(shot 07-the-mouse-built-train); build look 215 14 7 0.3 10 -1; wait 30; $(shot 08-the-mouse-built-mill)"
  "$(click 198 781); wait 60; $(shot 09-left-build)"
  # 5a: the gesture on the pinion: Join machines, the view of the mill the director takes after its build (solve.steps), one click on the last pinion, wheel_10
  "$(click 126 126); wait 10; look 20 12 26 179.5 -56 103.5; wait 20; $(shot 10-join-pinion); joinclick built-1.wheel_10; wait 10; follow; key home; wait 20"
  # drive to the motor: turn right to face it, drive, turn left to face +x, drive the last metres
  "hold right 3.15; wait 10; hold up 14; wait 10; hold up 13; wait 10; hold left 0.65; wait 20; hold up 2; wait 20; rover; key home; wait 20; hold shift+up 0.5; wait 20; $(shot 11-at-motor)"
  # 5b: joining stays on across the drive, so the pinion is still the first pick; a view of the motor (the open-loop drive ends a few metres
  # from route-gui.sh's, the build's spot 1.5 m off its, so the camera is aimed as the director aims it), click the rotor (the shaft), then
  # the generator; a view of the bank, click it (the wire); Escape; the camera back on the rover
  "look 200 15 13 233.8 -51.5 135; wait 20; joinclick motors.rotor; wait 10; $(shot 12-shaft-joined); joinclick motors.motor; wait 10; look 200 15 12 254.5 -44.2 137.9; wait 20; joinclick battery-bank.bank; wait 10; key escape; wait 10; $(shot 13-joined); follow; key home; wait 20"
  # 6: Sleep until..., the pre-dawn wake, Sleep (the left panel has the Edit built-1 button now, so the rows are 32 px lower; picking the wake shows its estimate, which pushes Sleep down to y 510)
  "$(click 126 238); wait 30; $(click 137 289); wait 20; $(click 140 339); wait 20; $(shot 14-sleep-panel)"
  "$(click 43 510); wait 120; $(shot 15-sleeping)"
  "waitsim 54799; wait 60; $(shot 16-woke); rover"
  # 7: 20x (the Speed row is at y 741 once the wake note is shown), then the pass
  "$(click 205 741); wait 30; $(shot 17-fast)"
  # run on to the sim-seconds limit (55,604 s): that exit closes the traces; a `quit` step would leave their last block unwritten
  "waitsim 55480; $(shot 18-before-pass); waitsim 55500; wait 30; $(shot 19-after-pass); waitsim 55610; quit"
)
script="$(IFS=';'; echo "${steps[*]}")"
script="${script//;;/;}"
log="$out/run.log"
GUI_CHECK_TIMEOUT="${GUI_CHECK_TIMEOUT:-2400}" "$here/tools/gui-check.sh" --hidden \
  HEROIC_WORLD=lonely-rover-e2e HEROIC_SAVES_DIR="$out/saves" \
  HEROIC_TRACE="$out/route" HEROIC_TRACE_DT=10 HEROIC_QUIT_AFTER_SIM_SECONDS=55604 -- "$script" > "$log" 2>&1 || echo "route-mouse: gui-check exited $?" >&2
echo "route-mouse: log $log, trace $out/route.battery-bank, shots $out/*.png"
grep -E "^\[(links|build|sleep|frontend)\]|^\[BuildMode\] (stage on|train:|the windmill|preset|palette-click|> \((post|windmill))|win:|join:" "$log" | grep -v "Join machines:" | head -120
