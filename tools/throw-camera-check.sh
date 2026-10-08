#!/usr/bin/env bash
# The follow-the-shot camera on the throwers (#202), in hidden frames (wait N = N frames):
#   - the catapulta's 33 m/s bolt is let go: the camera never leaves its 7.6 m home distance while the bolt flies 126 m;
#   - the torsion catapult's second throw (29.7 m) is followed out to about 46 m and the camera is home 2.5 s after it rests.
# Usage: tools/throw-camera-check.sh      (prints the camera distances and PASS/FAIL; takes about 2 minutes)
set -euo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
run() {  # machine first-wait steps step-wait
  local script="wait $2"; for ((i = 0; i < $3; i++)); do script="$script; wait $4; camera"; done
  GUI_CHECK_TIMEOUT=300 "$here/tools/gui-check.sh" --hidden "HEROIC_AUTOSELECT=$1" HEROIC_AUTORUN=1 HEROIC_SPEED=1 HEROIC_FRAMING_REPORT=1 -- "$script; quit" 2>&1
}
dist() { grep -o 'camera: .*distance=[0-9.]*' | sed 's/.*distance=//'; }
cat=$(run vitruvian-catapulta 2500 40 10 | dist | sort -n | tail -1)
tor=$(run torsion-catapult 1700 40 40 | dist)
tor_max=$(echo "$tor" | sort -n | tail -1); tor_last=$(echo "$tor" | tail -1)
echo "catapulta: furthest camera distance while the bolt flies and rests: $cat m (home 7.6)"
echo "torsion-catapult: furthest $tor_max m, last $tor_last m (home 4.5)"
awk -v c="$cat" -v m="$tor_max" -v l="$tor_last" 'BEGIN {
  ok = (c < 8) && (m > 35 && m < 60) && (l < 5);
  print ok ? "PASS" : "FAIL"; exit !ok }'
