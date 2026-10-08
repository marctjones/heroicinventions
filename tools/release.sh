#!/usr/bin/env bash
# Builds a release (issue #99): fresh machines and catalogue, a Release C# build,
# the test suites, an export per platform, and a smoke test. Artifacts go to dist/.
#
#   tools/release.sh                      all platforms, dotnet tests, smoke test
#   tools/release.sh --raco               also run the Racket suite (about 20 minutes)
#   tools/release.sh --platform macOS     only this preset (repeatable): macOS, "Windows Desktop", Linux
#   tools/release.sh --skip-tests         skip dotnet test (for iterating on the export itself)
#   tools/release.sh --pack-only          export the data pack only; needs no export templates
#   tools/release.sh --allow-missing-templates   skip a platform whose template is not installed
#                                          (the default is to fail: no template, no artifact)
#
# Exits non-zero on the first failure. See docs/release.md.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

GODOT="${HEROIC_GODOT:-/Applications/Godot_mono.app/Contents/MacOS/Godot}"
RUN_RACO=0; SKIP_TESTS=0; PACK_ONLY=0; ALLOW_MISSING=0
PLATFORMS=()
while [ $# -gt 0 ]; do
  case "$1" in
    --raco) RUN_RACO=1 ;;
    --skip-tests) SKIP_TESTS=1 ;;
    --pack-only) PACK_ONLY=1 ;;
    --allow-missing-templates) ALLOW_MISSING=1 ;;
    --platform) shift; PLATFORMS+=("$1") ;;
    -h|--help) sed -n '2,13p' "$0"; exit 0 ;;
    *) echo "unknown option: $1" >&2; exit 2 ;;
  esac
  shift
done
[ ${#PLATFORMS[@]} -eq 0 ] && PLATFORMS=("macOS" "Windows Desktop" "Linux")

step() { printf '\n==> %s\n' "$*"; }
fail() { printf 'RELEASE FAILED: %s\n' "$*" >&2; exit 1; }

[ -x "$GODOT" ] || fail "Godot not found at $GODOT (set HEROIC_GODOT)"
VERSION="$(sed -n 's/^config\/version="\(.*\)"/\1/p' game/project.godot)"
[ -n "$VERSION" ] || fail "game/project.godot has no config/version"
GODOT_VERSION="$("$GODOT" --version | cut -d. -f1-5)"        # 4.7.2.stable.mono
TEMPLATES="$HOME/Library/Application Support/Godot/export_templates/$GODOT_VERSION"
[ "$(uname)" = Darwin ] || TEMPLATES="$HOME/.local/share/godot/export_templates/$GODOT_VERSION"
DIST="$ROOT/dist"
SCRATCH="$(mktemp -d "${TMPDIR:-/tmp}/heroic-release.XXXXXX")"
echo "version $VERSION, Godot $GODOT_VERSION, templates $TEMPLATES"

# --- the meshes are loaded at run time from raw .glb files (GltfDocument), so the
# --- editor must not import them (meshes/.gdignore) but the pack must carry them as
# --- they are. For the export only: lift the .gdignore and mark each .glb "keep".
# --- Always put back, even on failure.
MESH_DIRS=(game/meshes game/meshes/catalogue)
restore_meshes() {
  for d in "${MESH_DIRS[@]}"; do rm -f "$d"/*.glb.import; done
  [ -f "$SCRATCH/gdignore" ] && mv "$SCRATCH/gdignore" game/meshes/.gdignore || true
}
trap 'restore_meshes; rm -rf "$SCRATCH"' EXIT

# 1. Machines, maps, meshes and the catalogue, fresh from the Racket sources.
step "racket/build.rkt"
export PLTCOLLECTS="$ROOT/racket:"
before="$(git diff -- game/machines game/maps game/meshes src/HeroicInventions.Sim/Materials | shasum)"
racket racket/build.rkt > "$SCRATCH/build.txt" 2>&1 || { tail -20 "$SCRATCH/build.txt"; fail "racket/build.rkt"; }
after="$(git diff -- game/machines game/maps game/meshes src/HeroicInventions.Sim/Materials | shasum)"
[ "$before" = "$after" ] || fail "racket/build.rkt changed committed machines, maps or meshes: commit the regenerated files first (git diff game/machines)"
[ -f game/meshes/catalogue/catalogue.rktd ] || fail "the catalogue was not built (game/meshes/catalogue/catalogue.rktd)"

# 2. C# solution, Release.
step "dotnet build (Release)"
(cd game && dotnet build HeroicInventions.csproj -c Release -nologo -v q) || fail "Release build"
(cd game && dotnet build HeroicInventions.csproj -nologo -v q) || fail "Debug build (the editor-side import and the source-tree smoke test run it)"

# 3. Tests.
if [ "$SKIP_TESTS" = 0 ]; then
  step "dotnet test"
  dotnet test tests/HeroicInventions.Sim.Tests -nologo -v q || fail "dotnet test"
fi
if [ "$RUN_RACO" = 1 ]; then
  step "raco test (about 20 minutes)"
  export HEROIC_GODOT="$GODOT"
  raco make racket/heroic/main.rkt
  raco test racket/heroic/tests > "$SCRATCH/raco.txt" 2>&1 || { tail -30 "$SCRATCH/raco.txt"; fail "raco test"; }
  tail -3 "$SCRATCH/raco.txt"
fi

# 4. Smoke test of the data in the source tree: a machine, a world, the build-mode catalogue.
smoke() {   # smoke LABEL COMMAND...    (the command is the game, headless)
  local label="$1"; shift
  local out="$SCRATCH/smoke-$label"; mkdir -p "$out"
  local t0 t1
  t0=$(python3 -c 'import time; print(time.time())')
  env HEROIC_AUTOSELECT=newtons-cradle HEROIC_AUTORUN=1 HEROIC_QUIT_AFTER_SIM_SECONDS=2 HEROIC_TRACE="$out/machine.txt" \
      timeout 180 "$@" > "$out/machine.log" 2>&1 || fail "$label: machine run exited non-zero (see $out/machine.log)"
  t1=$(python3 -c 'import time; print(time.time())')
  [ "$(wc -l < "$out/machine.txt")" -ge 5 ] || fail "$label: newtons-cradle wrote no trace"
  echo "$label: newtons-cradle loaded and ran 2 s of sim in $(python3 -c "print(round($t1-$t0,1))") s wall"
  env HEROIC_WORLD=lonely-rover-opening HEROIC_AUTORUN=1 HEROIC_QUIT_AFTER_SIM_SECONDS=2 HEROIC_TRACE="$out/world.txt" \
      timeout 180 "$@" > "$out/world.log" 2>&1 || fail "$label: world run exited non-zero (see $out/world.log)"
  ls "$out"/world.txt.* > /dev/null 2>&1 || fail "$label: lonely-rover-opening wrote no trace"
  echo "$label: lonely-rover-opening loaded ($(ls "$out"/world.txt.* | wc -l | tr -d ' ') machines traced)"
  env HEROIC_EDITOR=1 HEROIC_EDITOR_INPUT="cmd (palette); wait 1; quit" \
      timeout 180 "$@" > "$out/editor.log" 2>&1 || fail "$label: build mode exited non-zero (see $out/editor.log)"
  grep -q "antikythera-gear-24" "$out/editor.log" || fail "$label: build mode's palette has no catalogue entries (meshes/catalogue/catalogue.rktd not found)"
  echo "$label: build mode found the catalogue"
}
step "smoke test: source tree"
smoke source "$GODOT" --headless --path game

# 5. Export. The editor imports the project first (no .godot cache on a fresh checkout).
step "export"
rm -rf "$DIST"; mkdir -p "$DIST"
[ -f game/meshes/.gdignore ] && mv game/meshes/.gdignore "$SCRATCH/gdignore"
for d in "${MESH_DIRS[@]}"; do
  for f in "$d"/*.glb; do printf '[remap]\n\nimporter="keep"\n' > "$f.import"; done
done
"$GODOT" --headless --path game --import > "$SCRATCH/import.txt" 2>&1 || { tail -20 "$SCRATCH/import.txt"; fail "godot --import"; }

# The data pack on its own: needs no templates, and is where a missing data file shows up.
PCK="$DIST/HeroicInventions-$VERSION.pck"
"$GODOT" --headless --path game --export-pack macOS "$PCK" > "$SCRATCH/pack.txt" 2>&1 || { tail -20 "$SCRATCH/pack.txt"; fail "export-pack"; }
sed 's/\x1b\[[0-9;]*m//g' "$SCRATCH/pack.txt" | grep -o 'Storing File: .*' | sed 's/Storing File: //' > "$SCRATCH/packed.txt"
need() {   # need PATTERN MIN
  local n; n=$(grep -c -E "$1" "$SCRATCH/packed.txt" || true)
  [ "$n" -ge "$2" ] || fail "the data pack has $n files matching $1 (expected at least $2)"
  echo "pack: $n x $1"
}
need '^res://machines/.*\.machine$' 100
need '^res://worlds/.*\.world$' 20
need '^res://maps/.*\.map$' 11
need '^res://lessons/.*\.lesson$' 4
need '^res://meshes/[^/]*\.glb$' 30
need '^res://meshes/catalogue/[^/]*\.glb$' 40
need '^res://meshes/catalogue/catalogue\.rktd$' 1
need '^res://icons/rope\.png' 1
if grep -E '/(tests|racket)/|\.rkt$|\.md$' "$SCRATCH/packed.txt" | grep -v catalogue.rktd | grep -q .; then fail "the data pack holds files it should not (tests, racket sources, notes)"; fi
echo "data pack: $(du -h "$PCK" | cut -f1) $PCK"

if [ "$PACK_ONLY" = 1 ]; then
  step "done (pack only)"; ls -la "$DIST"; exit 0
fi

template_for() {
  case "$1" in
    macOS) echo macos.zip ;;
    "Windows Desktop") echo windows_release_x86_64.exe ;;
    Linux) echo linux_release.x86_64 ;;
    *) fail "no such preset: $1" ;;
  esac
}
slug_for() { case "$1" in macOS) echo macos ;; "Windows Desktop") echo windows ;; Linux) echo linux ;; esac; }

BUILT=()
for p in "${PLATFORMS[@]}"; do
  slug="$(slug_for "$p")"; tpl="$(template_for "$p")"
  if [ ! -f "$TEMPLATES/$tpl" ]; then
    msg="no export template for $p: $TEMPLATES/$tpl (Godot editor: Editor > Manage Export Templates > Download and Install, for the .NET build $GODOT_VERSION)"
    if [ "$ALLOW_MISSING" = 1 ]; then echo "SKIPPED: $msg"; continue; fi
    fail "$msg"
  fi
  step "export $p"
  out="$DIST/$slug"; mkdir -p "$out"
  if [ "$p" = macOS ]; then target="$out/HeroicInventions-$VERSION-macos.zip"; else target="$out/HeroicInventions$( [ "$p" = Linux ] && echo .x86_64 || echo .exe )"; fi
  "$GODOT" --headless --path game --export-release "$p" "$target" > "$SCRATCH/export-$slug.txt" 2>&1 \
    || { tail -20 "$SCRATCH/export-$slug.txt"; fail "export $p"; }
  [ -e "$target" ] || fail "export $p wrote nothing at $target"
  if [ "$p" != macOS ]; then
    (cd "$out" && zip -qr "../HeroicInventions-$VERSION-$slug.zip" . ) && rm -rf "$out"
  else
    mv "$target" "$DIST/" && rmdir "$out"
  fi
  BUILT+=("$p")
done

# 6. Smoke test of the exported macOS app, when we built it and are on a Mac.
if printf '%s\n' "${BUILT[@]:-}" | grep -qx macOS && [ "$(uname)" = Darwin ]; then
  step "smoke test: exported macOS app"
  mkdir -p "$SCRATCH/app"; unzip -q "$DIST/HeroicInventions-$VERSION-macos.zip" -d "$SCRATCH/app"
  APP="$(find "$SCRATCH/app" -name '*.app' -maxdepth 2 | head -1)"
  BIN="$(find "$APP/Contents/MacOS" -type f -perm +111 | head -1)"
  [ -n "$BIN" ] || fail "no executable in the exported app"
  echo "app size: $(du -sh "$APP" | cut -f1); zip: $(du -h "$DIST/HeroicInventions-$VERSION-macos.zip" | cut -f1)"
  smoke app "$BIN" --headless
fi

step "artifacts"
ls -la "$DIST"
printf 'release %s: ok\n' "$VERSION"
