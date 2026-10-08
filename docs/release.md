# Cutting a release

Issue #99. A release is a tagged commit on `main`, built by `tools/release.sh`, with the artifacts in `dist/` (gitignored). Nothing here publishes anything: tagging, pushing and the GitHub release are the owner's steps (see "Checklist").

## Version numbers

`v<major>.<minor>.<patch>-alpha`, as `v0.1.0-alpha` and the planned `v0.2.0-alpha` (docs/handoff-2026-10-07.md). The number lives in one place, `config/version` in `game/project.godot`; the macOS preset's `application/version` and `short_version` and the Windows preset's `file_version` and `product_version` in `game/export_presets.cfg` repeat it and must be changed with it. The tag is `v` plus that number, annotated, with a summary.

## Checklist

The handoff's list for v0.2.0-alpha, plus the export steps:

1. All in-flight branches merged or deliberately left out; #150 fixed (timed sets under Godot), so the Jolt tests mean what they say.
2. The audit's WRONG machines fixed or filed with evidence.
3. `dotnet test tests/HeroicInventions.Sim.Tests` and `raco test racket/heroic/tests` (with `HEROIC_GODOT`) green; counts recorded in the release notes. `tools/release.sh --raco` runs both.
4. Legibility measured on the six reference machines (antikythera-lunar-train, roman-crane, newtons-cradle, water-wheels, herons-fountain, mars-stirling) and a world view (`tools/legibility.py`).
5. Bump `config/version` (and the preset versions) on `main`; commit.
6. `tools/release.sh --raco` from a clean checkout of that commit. It must exit 0.
7. Open the macOS app by hand once (the smoke test is headless and cannot see the window).
8. Write the release notes (below). `git tag -a v0.2.0-alpha` with the summary; push the tag; create the GitHub release from the tag and attach the three zips.

## What the script does

`tools/release.sh` stops at the first failure:

1. `racket racket/build.rkt`, so the machines, maps, meshes and `meshes/catalogue/` are fresh. It fails if the build changes any committed machine, map or mesh (drift): commit the regenerated files first.
2. `dotnet build` of `game/HeroicInventions.csproj`, Release (what is exported) and Debug (what the editor-side import and the source-tree smoke test load).
3. `dotnet test tests/HeroicInventions.Sim.Tests`; with `--raco`, `raco make` and `raco test racket/heroic/tests` (about 20 minutes). `--skip-tests` skips the first, for iterating on the export.
4. Smoke test of the source tree (below).
5. The meshes are loaded at run time from raw `.glb` files, so `game/meshes/.gdignore` hides them from Godot's importer, which would also keep them out of an export. For the export only the script lifts the `.gdignore` and marks each `.glb` as `importer="keep"` (so the raw file is packed), then puts both back, also on failure. Then `godot --headless --import`.
6. A data pack, `dist/HeroicInventions-<version>.pck`, which needs no export templates. The script lists what went in and fails if any kind of runtime file is short (machines, worlds, maps, lessons, meshes, catalogue, `catalogue.rktd`) or if tests, Racket sources or notes got in.
7. For each preset: `godot --headless --export-release <preset> <path>` into `dist/` as `HeroicInventions-<version>-{macos,windows,linux}.zip`. A missing export template is a failure; `--allow-missing-templates` skips that platform instead; `--pack-only` stops after step 6; `--platform NAME` picks presets.
8. If macOS was built: unzip it and repeat the smoke test on the exported binary, with its size.

Smoke test (headless, `HEROIC_QUIT_AFTER_SIM_SECONDS=2`): `HEROIC_AUTOSELECT=newtons-cradle` must write a trace; `HEROIC_WORLD=lonely-rover-opening` must write a trace for each of its machines; build mode (`HEROIC_EDITOR=1`, console command `(palette)`) must list catalogue entries, which proves it found `meshes/catalogue/catalogue.rktd`.

## Presets and what is packed

`game/export_presets.cfg` (committed; it was gitignored until #99): `macOS` (universal, unsigned, bundle id `com.heroicinventions.game`), `Windows Desktop` (x86_64, the pck beside the exe), `Linux` (x86_64). All three export all resources and add, by `include_filter`: `machines/*.machine`, `worlds/*.world`, `maps/*.map`, `lessons/*.lesson`, `meshes/*.glb`, `meshes/catalogue/*.glb`, `meshes/catalogue/catalogue.rktd`, `icons/*.png`. `exclude_filter` drops `*.md`, `*.rkt`, `tests/*`, `racket/*`. The tests and the Racket sources live outside `game/` and never enter the project. `materials.json` and `planets.rktd` are embedded in `HeroicInventions.Sim.dll`, so they need no filter. Only the macOS preset has been exercised, and only up to a data pack, because no export templates are installed on the build machine; Windows and Linux are untested.

## Export templates

Godot needs the **.NET** export templates for the exact editor version (now 4.7.2.stable.mono) in `~/Library/Application Support/Godot/export_templates/4.7.2.stable.mono/` (Linux: `~/.local/share/godot/export_templates/`). Install them in the editor: Editor > Manage Export Templates > Download and Install; or download `Godot_v4.7.2-stable_mono_export_templates.tpz` from the Godot release page and use Install From File. The script looks for `macos.zip`, `windows_release_x86_64.exe` and `linux_release.x86_64` in that folder.

## Release notes

Write them for a player, not a developer: what is new, how to run it, the known issues below, then the numbers (test counts, legibility). Copy this header and fill in:

```
Heroic Inventions v0.2.0-alpha
Licence: PENDING. No licence has been chosen yet (issue #92). Until one is added, all rights are reserved by the author.
Downloads: macOS (universal, unsigned), Windows x86_64, Linux x86_64.
```

## Known issues

- **Licence undecided (#92).** The repository has no LICENSE file; do not tag a public release before the owner chooses.
- **macOS builds are unsigned and not notarised.** Gatekeeper refuses a downloaded copy at first: right-click the app and choose Open, or run `xattr -dr com.apple.quarantine HeroicInventions.app`. Signing and notarisation need an Apple Developer ID (`codesign/*` and `notarization/*` in the preset) and are the owner's.
- **Windows and Linux presets are untested** until their templates are installed and a build is run on those systems.
- **Run-time assets read from outside the pack.** `MachineView.GeneratedMesh` loads a part's mesh with `GltfDocument.AppendFromFile(ProjectSettings.GlobalizePath("res://meshes/..."))`, and the build-mode palette loads `icons/rope.png` with `Image.LoadFromFile(GlobalizePath(...))`. In an exported game `res://` is inside the pck and `GlobalizePath` gives a relative filesystem path, so these two reads very likely fail in an export (every generated part would throw "couldn't load its mesh"). Fix in code: read the bytes with `Godot.FileAccess.GetFileAsBytes` and call `AppendFromBuffer`, and load the icon with `GD.Load<Texture2D>`. The pack already carries the raw `.glb` files. The exported-app smoke test in `tools/release.sh` will fail until this is fixed, which is the point of it.
- The headless smoke test cannot see the window: the first launch of a release should be checked by eye.
