#!/usr/bin/env bash
# Which part is under a known pixel (#151): aims the camera at a point of a part, picks the centre of the viewport
# (1600x1000 whatever the window size) and checks the part id the picker reports. Headless, so it never takes the
# keyboard or mouse. One check per machine: the `look` step that aims at the part, and the part id expected.
#
#   tools/pick-check.sh
set -euo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
godot="${HEROIC_GODOT:-/Applications/Godot_mono.app/Contents/MacOS/Godot}"
fail=0
check() {
  local machine="$1" look="$2" want="$3" got
  got="$(cd "$here/game" && HEROIC_AUTORUN=1 HEROIC_AUTOSELECT="$machine" HEROIC_INPUT="wait 30; $look; wait 5; pick 800 500; quit" \
         "$godot" --headless . 2>&1 | sed -n 's/^\[pick\] 800 500 -> \([^ ]*\) .*/\1/p' | head -1)"
  if [[ "$got" == "$want" ]]; then echo "ok   $machine: $want"; else echo "FAIL $machine: wanted $want, got '${got:-nothing}'"; fail=1; fi
}
check lever-demo       'look 0 10 4 0 0.5 0'       beam    # the see-saw's beam, at its pivot
check roman-crane      'look 0 0 6 -1.6 2.6 1.05'  drum    # the drum, in front of the treadwheel behind it
check herons-fountain  'look 20 35 1.0 0 0.51 0'   basin   # the basin's bowl
check herons-fountain  'look 0 5 1.0 0 0.29 0'     supply  # the supply's front, through its open wall
check herons-fountain  'look 0 5 1.0 0 0.06 0'     receiver # the receiver's front, under the supply
exit $fail
