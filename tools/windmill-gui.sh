#!/usr/bin/env bash
# Building plan, phase 0 (docs/plans/building-plan.md): a gear on the windmill can be clicked in build mode. In the opening world, in a
# real hidden window (tools/gui-check.sh --hidden, never takes focus), real mouse events through Godot's own input pipeline.
#
#   tools/windmill-gui.sh [OUT_DIR]     # about a minute of wall time; OUT_DIR gets the shots (a camera line is printed before each) and run.log
#
# Steps:
#   1  the rover is put at (180, 113) facing +x; build mode opens on the route's windmill spot, "build new 180 105"
#   2  the route's windmill by the typed "build (...)" lines of game/routes/lonely-rover-opening/solve.steps (the post, the sails, the four
#      72:18 stages), as the director types them
#   3  the route's view of the train (look 180 12 3 180.45 -52.45 103.6), then "build click-part pinion-e": a real click where the camera
#      draws pinion-e's middle; "build log" prints the selection
#   4  the same click again (same spot, same view): the pick list, nearest first; "build picklist 1" takes the nearest, the sails
# Predicted before the first run: the click's ray enters the sails' box first (10 m sails, the box reaching back to the windshaft's
# end, holding every gear), so the log says "the click crossed N parts' boxes, nearest first: sails ..." and "pinion-e lies inside sails's
# box and is the smaller: it takes the click"; the state line says selected=pinion-e. The second click says "pick list at ...: 1 sails, ..."
# with pinion-e in it; item 1 makes the state line say selected=sails.
# Observed (first run, 2026-10-11): "crossed 3 parts' boxes, nearest first: sails (0.0 m), wheel-d (2.7 m), pinion-e (2.7 m)": the
# camera stands inside the sails' box (t = 0), the ray through pinion-e's middle grazes wheel-d's box too (grown 5 cm); pinion-e, the
# smallest box inside the sails', takes the click; selected=pinion-e; the list "1 sails, 2 wheel-d, 3 pinion-e"; item 1: selected=sails.
# The shot of pinion-e selected is docs/art/building/windmill-02-pinion-e-selected.png, the list windmill-03-pick-list.png.
#
# Building plan, phase 1 (post and sails by mouse), the same run after "build done": a new build at (200, 130), whose spot the log prints
# ("[build] new machine built-N at (200.00 Y 130.00)");
#   5  "build palette post" (the entry, as a click in the list), "build preset post 10.6" (a real click on the card's 10.6 m button), a view
#      onto the build's spot, "build hover-at 0 0 0" (the tall ghost on the card's size, shot 05), "build click-at 0 0 0" (a real click
#      there): the post
#   6  "build palette windmill", a view onto the post's top (from the side, across the windshaft: the post 1.45 m behind the sails), "build hover-at 0 10.6 0" (the
#      mouse over the top: the ghost sits on it, shot 06), "build click-at 0 10.6 0" (a real click on its top): the sails (shot 07)
# Predicted: "[BuildMode] preset post: 10.6 m (size-x 0.6 size-y 10.6 size-z 0.6)"; "(post post_1 #:at (...) ... #:size-y 10.6 ...)";
# "the windmill's hub on post_1's top: ... hub - post foot = (-0.45 11.05 1.45), the post under the train 1.45 m behind the sails and
# 0.45 m aside; the sails' disc clears the post by 1.03 m"; "build list" says "post_1:post windmill_2:windmill" (the serial counts every
# placing). The route's hub - post foot is (0 11 0) - (0.45 0 -1.45) = (-0.45, 11, 1.45): within 0.1 m.
# Observed: the first version (2026-10-11) put the hub over the post's middle, (0, 11.05, 0), and the lower sail swept through the post;
# the merger asked for the route's layout. Second run, 2026-10-11: as predicted. post_1@(200 -59.22 130) #:size-y 10.6; "hub - post foot
# = (-0.45 11.05 1.45) ... the sails' disc clears the post by 1.03 m"; windmill_2@(199.55 -48.17 131.45); "built-2: design post_1:post
# windmill_2:windmill". Shots: docs/art/building/windmill-05-post-tall-preset.png (the card's 10.6 m pressed, the ghost),
# windmill-06-ghost-on-the-post.png, windmill-07-windmill-on-the-post.png.
set -euo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
out="${1:-${TMPDIR:-/tmp}/windmill-gui}"
mkdir -p "$out/saves"
shot() { echo "camera; shot $out/$1.png; "; }
# the windmill's build lines from the route, between its "build new 180 105" and the next "build done"
mill="$(awk '/^build new 180 105/{on=1; next} on && /^build done/{exit} on && /^build \(/{printf "%s; wait 10; ", $0}' "$here/game/routes/lonely-rover-opening/solve.steps")"
[[ -n "$mill" ]] || { echo "windmill-gui: no windmill build lines found in solve.steps" >&2; exit 1; }
S="waitsim 8; rover place 180 113 0; wait 120; build new 180 105; wait 60; "
S+="$mill wait 60; build list; "
S+="look 180 12 3 180.45 -52.45 103.6; wait 40; $(shot 01-the-train)"
S+="build click-part pinion-e; wait 20; build log; $(shot 02-pinion-e-selected)"
S+="build click-part pinion-e; wait 20; $(shot 03-pick-list)"
S+="build picklist 1; wait 20; build log; $(shot 04-sails-from-the-list) "
# phase 1: the post and the sails by mouse at (200, 130), where the ground is about -59.2 m (the log's "new machine" line says)
S+="build done; wait 30; build new 200 130; wait 60; build palette post; wait 10; build preset post 10.6; wait 10; "
S+="look 125 20 30 200 -54 130; wait 20; build hover-at 0 0 0; wait 20; $(shot 05-post-tall-preset)build click-at 0 0 0; wait 20; build log; "
S+="build palette windmill; wait 10; look 90 20 34 200 -50 130; wait 20; build hover-at 0 10.6 0; wait 20; $(shot 06-ghost-on-the-post)"
S+="build click-at 0 10.6 0; wait 20; build log; build list; "
S+="look 90 12 40 200 -52 130; wait 30; $(shot 07-windmill-on-the-post) quit"
GUI_CHECK_TIMEOUT="${GUI_CHECK_TIMEOUT:-300}" "$here/tools/gui-check.sh" --hidden \
  HEROIC_WORLD=lonely-rover-opening HEROIC_HINTS=0 HEROIC_SAVES_DIR="$out/saves" -- "$S" > "$out/run.log" 2>&1 || echo "windmill-gui: gui-check exited $?" >&2
grep -E "^\[(build|BuildMode)\]|camera:" "$out/run.log" | grep -v "input:" || true
