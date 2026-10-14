#lang racket/base
;; Building phase 5 (#254, docs/plans/building-phase5-plan.md): the vault by mouse, the headless tier. Headless Godot at 120 Hz, the
;; game's build mode in the opening world (the rover building: found parts hidden), driven by HEROIC_INPUT's `build WORD` steps; the
;; camera-free `build put X Z` stands for a click at that world point (the mouse tier is tools/vault-mouse.sh).
;;
;; Case 1 (commit 1: the enclosure's size presets and `build put`). Predicted before the run: the card offers three sizes, 2 m (the usual),
;; 1 m and 0.5 m; `build preset enclosure 0.5` picks the last: "[BuildMode] preset enclosure: 0.5 m (size-x 0.5 size-y 0.5 size-z 0.5)";
;; `build put 0 0` places the room at the build's spot (254.5, 137.9) on the ground: the command is
;; (enclosure enclosure_1 #:at (X Y Z) #:material bronze #:size-x 0.5 #:size-y 0.5 #:size-z 0.5 #:wall regolith #:insulation 0) (Mars is
;; below 0 C: WithPlaceDefaults). Three Shift+Page Down sink it 0.5 m each, so the depth reads
;; 0.50, 1.00, 1.50 m (it starts on the ground, depth 0.00), and the found bank's cells join it after 1.50 m and not before: the crate's
;; middle is 1.35 m down, so the 0.5 m room's floor must lie 1.35 to 1.85 m down (hand-vault-gui saw the join at 1.40 m).
;;
;; Cases 2-4 (commit 3: building inside the selected room; BuildMode.Room.cs, Sim/Editor/RoomSlots.cs). The same room, sunk to 1.50 m, now
;; the selected part. A heat store put at (0.12, 0) from the build's spot lands on the room's floor, not the ground: the log says
;; "in enclosure_1: the ghost on its floor, 1.5x m below the ground" (1.50 at the room's own spot; the ground rises toward +x, so 1.55 at 254.6 (observed 1.556)),
;; the command is (heat-store heat_store_2 #:at (254.6 -44.4 137.9) ...) (254.62 snapped to the 5 cm grid; y the floor: -44.4, the
;; room's #:at, since a store's #:at is its base), then "placed in enclosure_1: heat_store_2 on its floor at (254.6 -44.4 137.9)". The
;; store is now the selected part, and the next part still goes in its room: a heat bin at the same spot is one edit,
;; (begin (heat-bin heat_bin_3 ...) (set heat_bin_3 #:holds heat_store_2)), "in enclosure_1: heat_bin_3 holds heat_store_2 (the room's only
;; heat store)"; a bimetal strip at (-0.19, 0) (254.3) is (begin (bimetal bimetal_4 ...) (set bimetal_4 #:senses enclosure_1) (set bimetal_4
;; #:drives heat_bin_3)), and its two log lines. `build log` says room=enclosure_1 while a part is being placed and none after.
;; Case 4: a second store beside the first (put -0.12 0) makes the bin's holds ambiguous: the bin is the bare (heat-bin heat_bin_4 ...),
;; the log says "heat_bin_4 holds stays blank, it could name heat_store_2 or heat_store_3", and no (set heat_bin_4 #:holds ...) is run.
;; Skipped when Godot is not installed.
(require rackunit racket/system racket/port racket/string racket/list
         racket/runtime-path
         (only-in heroic/godothost godot-available? godot-binary))

(define-runtime-path game-dir "../../../game")

(define (run env-list)
  (define env (environment-variables-copy (current-environment-variables)))
  (for ([kv (append '(("HEROIC_WORLD" . "lonely-rover-opening") ("HEROIC_HINTS" . "0")) env-list)])
    (environment-variables-set! env (string->bytes/utf-8 (car kv)) (string->bytes/utf-8 (cdr kv))))
  (string-split
   (parameterize ([current-directory game-dir] [current-environment-variables env])
     (with-output-to-string (λ () (system* godot-binary "--headless" "--fixed-fps" "120" "--path" "."))))
   "\n"))
(define (index-of-line rx ls) (for/first ([l ls] [i (in-naturals)] #:when (regexp-match? rx l)) i))
(define (matching rx ls) (for/list ([l ls] #:when (regexp-match? rx l)) l))

(when (godot-available?)
  (define lines
    (run `(("HEROIC_INPUT" .
            ,(string-append
              "wait 360; build new 254.5 137.9; wait 60; build palette enclosure; build preset enclosure 0.5; build put 0 0; wait 5; "
              "build select enclosure_1; "
              "key shift+pagedown; wait 5; key shift+pagedown; wait 5; key shift+pagedown; wait 5; build log; "
              "build palette heat-store; build log; build put 0.12 0; wait 5; "
              "build palette heat-bin; build put 0.12 0; wait 5; "
              "build palette bimetal; build put -0.19 0; wait 5; build log; wait 30; quit")))))
  (define lines2
    (run `(("HEROIC_INPUT" .
            ,(string-append
              "wait 360; build new 254.5 137.9; wait 60; build palette enclosure; build preset enclosure 0.5; build put 0 0; wait 5; "
              "key shift+pagedown; wait 5; key shift+pagedown; wait 5; key shift+pagedown; wait 5; "
              "build palette heat-store; build put 0.12 0; wait 5; "
              "build select enclosure_1; build palette heat-store; build put -0.12 0; wait 5; "
              "build palette heat-bin; build put 0.12 0; wait 5; build log; wait 30; quit")))))
  (define all2 (string-join lines2 "\n"))
  (define all (string-join lines "\n"))

  (test-case "case 1: the 0.5 m preset, a room put down by `build put`, sunk by Shift+Page Down to the bank"
    (check-true (and (index-of-line #rx"^\\[BuildMode\\] preset enclosure: 0.5 m \\(size-x 0.5 size-y 0.5 size-z 0.5\\)" lines) #t) all)
    (check-true (and (index-of-line #rx"^\\[BuildMode\\] > \\(enclosure enclosure_1 #:at \\([-0-9. ]+\\) #:material bronze #:size-x 0.5 #:size-y 0.5 #:size-z 0.5 #:wall regolith #:insulation 0\\)$" lines) #t) all)
    (define depths (for/list ([l (matching #rx"^\\[BuildMode\\] depth: enclosure_1" lines)]) (cadr (regexp-match #rx"depth ([0-9.]+) m below the ground" l))))
    (check-equal? depths '("0.50" "1.00" "1.50") all)
    (define join (index-of-line #rx"battery-bank.cells joined built-1.enclosure_1" lines))
    (check-true (and join #t) all)
    (check-true (< (index-of-line #rx"depth: enclosure_1 depth 1.50 m" lines) join) "the join comes after the room reaches 1.50 m, not at 1.00 or before"))

  (test-case "case 2: a heat store put into the selected room stands on its floor; the next part still goes in that room"
    (check-true (and (index-of-line #rx"^\\[BuildMode\\] in enclosure_1: the ghost on its floor, 1\\.[45][0-9]* m below the ground" lines) #t) all)
    (check-true (and (index-of-line #rx"^\\[BuildMode\\] > \\(heat-store heat_store_2 #:at \\(254.6 -44.4 137.9\\)" lines) #t) all)
    (check-true (and (index-of-line #rx"^\\[BuildMode\\] placed in enclosure_1: heat_store_2 on its floor at \\(254.6 -44.4 137.9\\)" lines) #t) all)
    (check-true (and (index-of-line #rx"state: .* placing=heat-store room=enclosure_1 " lines) #t) all)
    (check-true (and (index-of-line #rx"state: .* placing=none room=none " lines) #t) all))

  (test-case "case 3: the bin holds the room's only store and the strip senses the room and drives the bin, each as one edit"
    (check-true (and (index-of-line #rx"^\\[BuildMode\\] > \\(begin \\(heat-bin heat_bin_3 #:at \\([-0-9. ]+\\) [^)]*\\) \\(set heat_bin_3 #:holds heat_store_2\\)\\)$" lines) #t) all)
    (check-true (and (index-of-line #rx"^\\[BuildMode\\] in enclosure_1: heat_bin_3 holds heat_store_2 \\(the room's only heat store\\)" lines) #t) all)
    (check-true (and (index-of-line #rx"^\\[BuildMode\\] > \\(begin \\(bimetal bimetal_4 #:at \\(254.3 [-0-9. ]+\\) [^)]*\\) \\(set bimetal_4 #:senses enclosure_1\\) \\(set bimetal_4 #:drives heat_bin_3\\)\\)$" lines) #t) all)
    (check-true (and (index-of-line #rx"in enclosure_1: bimetal_4 senses enclosure_1 \\(the room itself\\)" lines) #t) all)
    (check-true (and (index-of-line #rx"in enclosure_1: bimetal_4 drives heat_bin_3" lines) #t) all)
    (check-true (and (index-of-line #rx"^\\[BuildMode\\] state: .* parts=enclosure_1@[^,]*,heat_store_2@[^,]*,heat_bin_3@[^,]*,bimetal_4@" lines) #t) all))

  (test-case "case 4: two stores in the room leave the bin's holds blank, and the log names both"
    (check-true (and (index-of-line #rx"^\\[BuildMode\\] > \\(heat-bin heat_bin_4 #:at" lines2) #t) all2)
    (check-true (and (index-of-line #rx"in enclosure_1: heat_bin_4 holds stays blank, it could name heat_store_2 or heat_store_3" lines2) #t) all2)
    (check-false (index-of-line #rx"set heat_bin_4 #:holds" lines2) all2)
    (check-false (index-of-line #rx"^\\[BuildMode\\] > \\(begin \\(heat-bin" lines2) all2)))
