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
;;
;; Case 5 (commit 4: `build field KEY VALUE`, a real click on the part's number field, the value as key events, Enter): with the bin
;; selected `build field open 1`, the store `mass 40`, the room `temperature -55`, `ground -55`, `pressure 610`, the store `temperature 200`:
;; each logs "field KEY: typed V into ID's field" and runs exactly one (set ID #:KEY V) (a rebuild of the inspector round the field must not
;; commit it a second time), in that order; the last `build log` shows the bin open (a real click on "Show all its numbers" opened the list
;; the first time). `build choose wall none` and `build choose wall regolith` take the room's Wall list to none and back (the inspector's list, a real click
;; and the arrow keys): (set enclosure_1 #:wall #f), (set enclosure_1 #:insulation 2), (set enclosure_1 #:wall regolith), (set enclosure_1 #:insulation 0).
;; These are the night's numbers of tools/vault-mouse.sh, typed into the inspector's own fields, never into a command.
;;
;; Case 6 (commit 6: the size handle, BuildMode.Room.cs). The room 0.5 m a side, 1.50 m down, selected; `build size 1` moves the inspector's
;; Size slider to 1 m, which is ONE edit, (begin (set enclosure_1 #:size-x 1) (set enclosure_1 #:size-y 1) (set enclosure_1 #:size-z 1)), logged
;; "size: enclosure_1 1 m a side, floor 1.4x-1.5x m below the ground" (1.497 observed; the floor stays: it grows upward); `build size 0.5` is the same edit back at 0.5.
;; The bank, whose crate's middle (1.35 m down) is in the 1 m room as in the 0.5 m one, stays joined: no "left" line.
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
              "build palette bimetal; build put -0.19 0; wait 5; build log; "
              "build select heat_bin_3; build field open 1; build select heat_store_2; build field mass 40; "
              "build select enclosure_1; build field temperature -55; build field ground -55; build field pressure 610; "
              "build choose wall none; build choose wall regolith; "
              "build select heat_store_2; build field temperature 200; wait 5; build log; wait 30; quit")))))
  (define lines2
    (run `(("HEROIC_INPUT" .
            ,(string-append
              "wait 360; build new 254.5 137.9; wait 60; build palette enclosure; build preset enclosure 0.5; build put 0 0; wait 5; "
              "key shift+pagedown; wait 5; key shift+pagedown; wait 5; key shift+pagedown; wait 5; "
              "build palette heat-store; build put 0.12 0; wait 5; "
              "build select enclosure_1; build palette heat-store; build put -0.12 0; wait 5; "
              "build palette heat-bin; build put 0.12 0; wait 5; build log; wait 30; quit")))))
  (define all2 (string-join lines2 "\n"))
  (define lines3
    (run `(("HEROIC_INPUT" .
            ,(string-append
              "wait 360; build new 254.5 137.9; wait 60; build palette enclosure; build preset enclosure 0.5; build put 0 0; wait 5; "
              "key shift+pagedown; wait 5; key shift+pagedown; wait 5; key shift+pagedown; wait 5; "
              "build select enclosure_1; build size 1; wait 5; build size 0.5; wait 5; build log; wait 30; quit")))))
  (define all3 (string-join lines3 "\n"))
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
    (check-false (index-of-line #rx"^\\[BuildMode\\] > \\(begin \\(heat-bin" lines2) all2))

  (test-case "case 5: `build field` types the night's numbers into the inspector's fields, each committed once, in order"
    (define sets (for/list ([l lines] #:when (regexp-match? #rx"^\\[BuildMode\\] > \\(set (heat_bin_3|heat_store_2|enclosure_1) #:(open|mass|temperature|ground|pressure) " l))
                   (cadr (regexp-match #rx"> (.*)$" l))))
    (check-equal? sets '("(set heat_bin_3 #:open 1)" "(set heat_store_2 #:mass 40)" "(set enclosure_1 #:temperature -55)"
                         "(set enclosure_1 #:ground -55)" "(set enclosure_1 #:pressure 610)" "(set heat_store_2 #:temperature 200)")
                  all)
    ;; `build choose KEY TEXT`: a real click on the Wall list and the entry by the arrow keys and Enter: none, then regolith; WallChanged puts the
    ;; room's 2 W/K back and takes it off again. (tools/vault-mouse.sh needs it: a live edit carries the soil's temperatures, see its header.)
    (define wall-lines (for/list ([l lines] #:when (regexp-match? #rx"^\\[BuildMode\\] > \\(set enclosure_1 #:(wall|insulation) " l)) (cadr (regexp-match #rx"> (.*)$" l))))
    (check-equal? wall-lines '("(set enclosure_1 #:wall #f)" "(set enclosure_1 #:insulation 2)" "(set enclosure_1 #:wall regolith)" "(set enclosure_1 #:insulation 0)") all)
    (for ([f '("open: typed 1 into heat_bin_3" "mass: typed 40 into heat_store_2" "temperature: typed -55 into enclosure_1" "ground: typed -55 into enclosure_1"
               "pressure: typed 610 into enclosure_1" "temperature: typed 200 into heat_store_2")])
      (check-true (and (index-of-line (regexp (string-append "^\\[BuildMode\\] field " (regexp-quote f) "'s field")) lines) #t) f)))


  (test-case "case 6: the size handle is one edit each way, the floor stays, the bank stays joined"
    (define sets (for/list ([l lines3] #:when (regexp-match? #rx"^\\[BuildMode\\] > \\(begin \\(set enclosure_1 #:size-x" l)) (cadr (regexp-match #rx"> (.*)$" l))))
    (check-equal? sets '("(begin (set enclosure_1 #:size-x 1) (set enclosure_1 #:size-y 1) (set enclosure_1 #:size-z 1))"
                         "(begin (set enclosure_1 #:size-x 0.5) (set enclosure_1 #:size-y 0.5) (set enclosure_1 #:size-z 0.5))") all3)
    (check-true (and (index-of-line #rx"^\\[BuildMode\\] size: enclosure_1 1 m a side, floor 1\\.[45][0-9]* m below the ground" lines3) #t) all3)
    (check-true (and (index-of-line #rx"^\\[BuildMode\\] size: enclosure_1 0\\.5 m a side, floor 1\\.[45][0-9]* m below the ground" lines3) #t) all3)
    (check-false (index-of-line #rx"battery-bank.cells left" lines3) all3)
    (check-true (and (index-of-line #rx"state: .*parts=enclosure_1@\\(254.5 -44.4 137.9\\)" lines3) #t) "the floor did not move")))
