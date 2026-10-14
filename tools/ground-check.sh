#!/usr/bin/env bash
# The ground colour rule's frames and numbers (docs/art-direction.md 12.23, docs/plans/ground-colour-plan.md section 3C): the 12.16 cameras
# in lonely-rover-opening at noon, 16:30 and midnight, each shot saved with the `camera` line printed, then the mean luminance, hue and
# saturation of every rectangle in tools/ground-regions.txt and the gap and colour distance of every pair, by tools/legibility.py.
# A hidden window (tools/gui-check.sh --hidden): it never takes focus.
#
#   tools/ground-check.sh OUTDIR              # shoots 3 times x 8 cameras, then prints the table (about a minute a time)
#   tools/ground-check.sh OUTDIR --table      # only prints the table again from the shots already in OUTDIR
#   GROUND_CHECK_TIMES="noon" tools/ground-check.sh OUTDIR     # only some of the times (noon, 1630, midnight)
#
# Shots are OUTDIR/TIME-CAMERA.png, the log OUTDIR/TIME.log (a `camera` line each). The scene clock is held (`scene time H; scene clock-rate 0`)
# and the rover is placed (`rover place X Z HEADING`), the camera fixed (`look YAW PITCH DIST`); the rover settles during the waits. Two runs of
# the same code give the same region means to within the wind's streaks and the pit's debris (a few tenths; 12.23 records the check), not the
# same pixels. The pit is the dug rubble: two buckets, each of its own launch.
set -euo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
out="${1:?usage: tools/ground-check.sh OUTDIR [--table]}"
mkdir -p "$out"
out="$(cd "$out" && pwd)"
declare -a times=(noon:12 1630:16.5 midnight:0)
# camera name: X Z HEADING YAW PITCH DISTANCE (the rover stands at X Z; the camera pivots on it)
cameras=(
  "flat 180 140 270 -90 35 14"
  "rimbase -0.6 272 180 180 35 14"
  "ice 0.6 -276 0 0 35 14"
  "silica -224 71 270 -90 35 14"
  "steep 0 352 180 180 35 14"
  "rubbleface 252.5 137.9 270 -90 35 14"
  "contact 305 360 270 -90 35 14"
  "pit 252.5 137.9 270 -90 35 14"
)
if [[ "${2:-}" != "--table" ]]; then
  for t in ${GROUND_CHECK_TIMES:-noon 1630 midnight}; do
    hour=""; for pair in "${times[@]}"; do [[ "${pair%%:*}" == "$t" ]] && hour="${pair##*:}"; done
    [[ -n "$hour" ]] || { echo "unknown time $t" >&2; exit 2; }
    : > "$out/$t.log"
    for c in "${cameras[@]}"; do
      read -r name x z heading yaw pitch dist <<<"$c"
      # one launch a camera: after a `look` the camera no longer follows a later `rover place`
      script="wait 120; rover place $x $z $heading; wait 90"
      if [[ "$name" == pit ]]; then script+="; rover dig right; wait 30; rover until Stowed; rover dig right; wait 30; rover until Stowed; wait 30"; fi
      script+="; look $yaw $pitch $dist; wait 40; camera; shot $out/$t-$name.png; quit"
      GUI_CHECK_TIMEOUT=300 "$here/tools/gui-check.sh" --hidden HEROIC_WORLD=lonely-rover-opening "HEROIC_SET=scene time $hour; scene clock-rate 0" -- "$script" \
        | grep -E "camera:|shot |rror" | sed "s|^|$t $name: |" >> "$out/$t.log" || true
    done
    grep -v "shot " "$out/$t.log" || true
  done
fi
# the numbers: each camera's rectangles and pairs (tools/ground-regions.txt: `region CAMERA NAME x0 y0 x1 y1`, `pair CAMERA A B`)
for t in ${GROUND_CHECK_TIMES:-noon 1630 midnight}; do
  echo "== $t"
  for c in "${cameras[@]}"; do
    name="${c%% *}"
    [[ -f "$out/$t-$name.png" ]] && python3 "$here/tools/legibility.py" --regions "$here/tools/ground-regions.txt" "$name" "$out/$t-$name.png"
  done
done
