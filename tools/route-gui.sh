#!/usr/bin/env bash
# The fastest route played as a person plays it (issue #218): the real window (tools/gui-check.sh --hidden, never takes focus),
# real mouse and key events through Godot's own input pipeline (game/scripts/ScriptedInput.cs), in lonely-rover-e2e. What differs from
# the automated route (racket/heroic/tests/e2e-route-test.rkt) is listed in docs/e2e-route.md, "The route played as a person".
#
#   tools/route-gui.sh [OUT_DIR]       # about 20 minutes of wall time; OUT_DIR gets the shots, the trace, the saves and run.log
#
# What it does, in order (R = a real click or key, C = the build console path, J = the scripted join step):
#   1  R  drive the rover from its start to 8 m south of where the windmill goes (arrow keys held, `rover` printed on the way)
#   2  R  press "Build a new machine"; the palette's Post (R: click the entry, click the scene), then its size by C (a click
#         places on the surface and the numbers are typed: #223)
#   3  C  the windmill and the four-stage train: the palette keeps the sails and the gears under "Show all" (#222) and the gears'
#         offsets are millimetres, so they are typed as the automated route types them
#   4  R  press "Leave" to close build mode
#   5  R  try the join gesture on the train's last pinion, the motor and the bank: each click lands on another part (see the log:
#         "the click crossed ... boxes"); the break is recorded in docs/e2e-route.md. Then J: the two links, by name
#   6  R  open "Sleep until...", pick "pre-dawn", press "Sleep" (the game's own sleep, paused: the engine does not run through it)
#   7  R  after the wake, press 20x; the call goes out at 03:00 on sol 2 and the run ends 120 s later
# Prediction (from e2e-route-test.rkt, the automated route): the cells are warm and the 25 Wh bank is full at the wake (54,800 s),
# the rotor near 1,570 rpm and 117 to 127 W, bank.won first 1 between 55,484 and 55,495 s (sol 2, 03:00).
set -euo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
out="${1:-${TMPDIR:-/tmp}/route-gui}"
mkdir -p "$out/saves"
cam="camera"
shot() { echo "$cam; shot $out/$1.png"; }
click() { echo "move $1 $2; wait 3; down $1 $2; up $1 $2"; }   # a click at viewport pixels (the window is 1280x800)
stage() {   # a 72-tooth wheel on the previous pinion's arbor and an 18-tooth pinion meshing it (as e2e-route-test.rkt)
  echo "build (wheel $1 #:catalogue involute-gear-m5-72 #:at ($3 11 $4)); build (arbor $6 $1); build (wheel $2 #:catalogue involute-gear-m5-18 #:at ($5 11 $4)); build (mesh $1 $2)"
}
steps=(
  "wait 100; rover; $(shot 01-start)"
  # 1: turn left about 115 degrees to face the windmill's spot, drive, turn back to face -z, drive the last stretch
  "hold left 3.2; wait 20; hold up 10; wait 10; hold right 0.8; wait 20; hold up 4.6; wait 30; rover; $(shot 02-at-windmill-spot)"
  # 2: the button, the palette's Post, a click in the scene
  "$(click 126 166); wait 60; $(shot 03-build-mode)"
  "$(click 52 265); wait 10; move 620 450; wait 5; down 620 450; up 620 450; wait 20; $(shot 04-post-placed)"
  "build (set post_1 #:size-y 10.6); build (set post_1 #:size-x 0.6); build (set post_1 #:size-z 0.6)"
  # 3: the windmill and the train, typed
  "build (windmill sails #:at (0 11 0))"
  "$(stage wheel-a pinion-b 0 -1.3 0.225 sails)"
  "$(stage wheel-b pinion-c 0.225 -1.4 0.45 pinion-b)"
  "$(stage wheel-c pinion-d 0.45 -1.5 0.675 pinion-c)"
  "$(stage wheel-d pinion-e 0.675 -1.6 0.9 pinion-d)"
  "build list; wait 20; $(shot 05-built)"
  # 4: Leave
  "$(click 159 781); wait 60; $(shot 06-left-build)"
  # 5a: the gesture on the pinion: Join machines, the camera swung to the east side of the windmill and out, one click on pinion-e
  "$(click 126 126); wait 10; hold minus 0.5; hold shift+right 1.05; wait 20; $(shot 07-join-pinion); joinclick built-1.pinion-e; wait 10; key escape; wait 10; key home; wait 20"
  # drive to the motor: turn right to face it, drive, turn left to face +x, drive the last metres
  "hold right 3.15; wait 10; hold up 14; wait 10; hold up 13; wait 10; hold left 0.65; wait 20; hold up 2; wait 20; rover; key home; wait 20; hold shift+up 0.5; wait 20; $(shot 08-at-motor)"
  # 5b: the gesture on the motor, then on the bank (Escape between)
  "$(click 126 126); wait 10; joinclick motors.motor; wait 10; key escape; wait 10; $(click 126 126); wait 10; joinclick battery-bank.bank; wait 10; key escape; wait 10; $(shot 09-join-probes)"
  # 5c: the links, by name (the click cannot make them)
  "join built-1.pinion-e motors.rotor; join motors.motor battery-bank.bank; wait 10; $(shot 10-joined)"
  # 6: Sleep until..., the pre-dawn wake, Sleep (the left panel has the Edit built-1 button now, so the rows are 32 px lower; picking the wake shows its estimate, which pushes Sleep down to y 510)
  "$(click 126 238); wait 30; $(click 137 289); wait 20; $(click 140 339); wait 20; $(shot 11-sleep-panel)"
  "$(click 43 510); wait 120; $(shot 12-sleeping)"
  "waitsim 54799; wait 60; $(shot 13-woke); rover"
  # 7: 20x (the Speed row is at y 741 once the wake note is shown), then the pass
  "$(click 205 741); wait 30; $(shot 14-fast)"
  # run on to the sim-seconds limit (55,604 s): that exit closes the traces; a `quit` step would leave their last block unwritten
  "waitsim 55480; $(shot 15-before-pass); waitsim 55500; wait 30; $(shot 16-after-pass); waitsim 55610; quit"
)
script="$(IFS=';'; echo "${steps[*]}")"
script="${script//;;/;}"
log="$out/run.log"
GUI_CHECK_TIMEOUT="${GUI_CHECK_TIMEOUT:-2400}" "$here/tools/gui-check.sh" --hidden \
  HEROIC_WORLD=lonely-rover-e2e HEROIC_SAVES_DIR="$out/saves" \
  HEROIC_TRACE="$out/route" HEROIC_TRACE_DT=10 HEROIC_QUIT_AFTER_SIM_SECONDS=55604 -- "$script" > "$log" 2>&1 || echo "route-gui: gui-check exited $?" >&2
echo "route-gui: log $log, trace $out/route.battery-bank, shots $out/*.png"
grep -E "^\[(links|build|sleep|frontend)\]|win:|join:" "$log" | grep -v "Join machines:" | head -80
