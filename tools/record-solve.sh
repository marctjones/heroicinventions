#!/usr/bin/env bash
# Records the rover's solve of the Lonely Rover's opening (game/routes/lonely-rover-opening/solve.steps, the director's steps:
# game/scripts/Main.Director.cs) in a hidden window (tools/gui-check.sh --hidden, never takes focus) with Godot's Movie Maker,
# then makes an mp4 with ffmpeg.
#
#   tools/record-solve.sh [OUT_DIR] [STEPS_FILE]
#
# OUT_DIR gets solve.avi, solve.mp4, run.log, the trace (solve.*) and the saves. It runs at 30 fps of game time a second of video: the
# drives, digs and builds play at their real pace; the three sleeps play as a time-lapse (the clock at the top of the screen).
# The same steps run headless (no window, the same numbers) with:
#   cd game && HEROIC_WORLD=lonely-rover-opening HEROIC_HINTS=0 HEROIC_INPUT_FILE=routes/lonely-rover-opening/solve.steps \
#     HEROIC_QUIT_AFTER_SIM_SECONDS=144400 <godot> --headless --fixed-fps 30 --path .
# Keep --fixed-fps 30 for both: the steps' `wait N` counts frames.
set -euo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
out="${1:-${TMPDIR:-/tmp}/record-solve}"
steps="${2:-$here/game/routes/lonely-rover-opening/solve.steps}"
mkdir -p "$out/saves"
GUI_CHECK_ARGS="--write-movie $out/solve.avi --fixed-fps 30" GUI_CHECK_TIMEOUT="${GUI_CHECK_TIMEOUT:-14400}" \
  "$here/tools/gui-check.sh" --hidden HEROIC_WORLD=lonely-rover-opening HEROIC_HINTS=0 HEROIC_MARKERS=0 HEROIC_INPUT_FILE="$steps" \
  HEROIC_SAVES_DIR="$out/saves" HEROIC_TRACE="$out/solve" HEROIC_TRACE_DT=60 HEROIC_QUIT_AFTER_SIM_SECONDS=144400 \
  -- "" > "$out/run.log" 2>&1 || echo "record-solve: gui-check exited $?" >&2
if [[ -f "$out/solve.avi" ]] && command -v ffmpeg >/dev/null; then
  ffmpeg -y -loglevel error -i "$out/solve.avi" -c:v libx264 -pix_fmt yuv420p -crf 22 -movflags +faststart "$out/solve.mp4"
fi
grep -E "^\[director\] (caption|expect|FAILED|dig|drive-to reached)|^\[zones\] battery|^\[links\] joined|^\[sleep\] woke|win:|NOT ON SCREEN" "$out/run.log" || true
if grep -q "FAILED\|NOT ON SCREEN" "$out/run.log"; then echo "record-solve: a step FAILED (see $out/run.log)" >&2; exit 1; fi
echo "record-solve: $out/solve.mp4 (log $out/run.log)"
