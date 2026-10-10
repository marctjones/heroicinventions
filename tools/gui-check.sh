#!/usr/bin/env bash
# Run the game's real window with a scripted input sequence, in the background:
# it never comes to the front, never takes the keyboard or mouse (input goes
# through Godot's own pipeline, see game/scripts/ScriptedInput.cs), and its
# log comes back on stdout. Shots land wherever the script's `shot` says.
#
#   tools/gui-check.sh [--hidden] [VAR=value ...] -- 'wait 30; hold right 1; camera; shot /tmp/a.png; quit'
#
# VAR=value pairs pass through as environment (HEROIC_AUTOSELECT=trebuchet,
# HEROIC_EDITOR=1, ...). The script goes to HEROIC_INPUT, or to
# GUI_CHECK_ARGS adds Godot arguments (e.g. '--write-movie out.avi --fixed-fps 30' to record the run).
# HEROIC_EDITOR_INPUT when HEROIC_EDITOR=1. --hidden launches the app hidden
# as well (no window on screen at all; macOS may then draw it less often).
# Put `quit` last; a timeout (GUI_CHECK_TIMEOUT, default 120 s) stops a run
# that never ends.
set -euo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
godot_app="${HEROIC_GODOT_APP:-/Applications/Godot_mono.app}"
hidden=()
envs=(--env HEROIC_BACKGROUND=1)
editor=0
while [[ $# -gt 0 && "$1" != "--" ]]; do
  case "$1" in
    --hidden) hidden=(-j) ;;
    *=*) envs+=(--env "$1"); [[ "$1" == HEROIC_EDITOR=1 ]] && editor=1 ;;
    *) echo "unknown argument $1" >&2; exit 2 ;;
  esac
  shift
done
[[ "${1:-}" == "--" ]] && shift
script="${1:-quit}"
if [[ $editor == 1 ]]; then envs+=(--env "HEROIC_EDITOR_INPUT=$script"); else envs+=(--env "HEROIC_INPUT=$script"); fi

log="$(mktemp -t gui-check)"
# -g: don't bring it forward; -n: a fresh instance; -W: wait for it to quit
( open -g -n -W ${hidden[@]+"${hidden[@]}"} -a "$godot_app" --stdout "$log" --stderr "$log" "${envs[@]}" \
    --args --path "$here/game" --resolution 1280x800 ${GUI_CHECK_ARGS:-} ) &
opener=$!
for ((t = 0; t < ${GUI_CHECK_TIMEOUT:-120}; t++)); do
  kill -0 $opener 2>/dev/null || break
  sleep 1
done
if kill -0 $opener 2>/dev/null; then
  echo "gui-check: timed out; stopping the game" >&2
  pkill -f "$here/game" || true
fi
wait $opener 2>/dev/null || true
cat "$log"
rm -f "$log"
