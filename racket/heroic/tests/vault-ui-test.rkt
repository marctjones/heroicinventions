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
              "key shift+pagedown; wait 5; key shift+pagedown; wait 5; key shift+pagedown; wait 5; build log; wait 30; quit")))))
  (define all (string-join lines "\n"))

  (test-case "case 1: the 0.5 m preset, a room put down by `build put`, sunk by Shift+Page Down to the bank"
    (check-true (and (index-of-line #rx"^\\[BuildMode\\] preset enclosure: 0.5 m \\(size-x 0.5 size-y 0.5 size-z 0.5\\)" lines) #t) all)
    (check-true (and (index-of-line #rx"^\\[BuildMode\\] > \\(enclosure enclosure_1 #:at \\([-0-9. ]+\\) #:material bronze #:size-x 0.5 #:size-y 0.5 #:size-z 0.5 #:wall regolith #:insulation 0\\)$" lines) #t) all)
    (define depths (for/list ([l (matching #rx"^\\[BuildMode\\] depth: enclosure_1" lines)]) (cadr (regexp-match #rx"depth ([0-9.]+) m below the ground" l))))
    (check-equal? depths '("0.50" "1.00" "1.50") all)
    (define join (index-of-line #rx"battery-bank.cells joined built-1.enclosure_1" lines))
    (check-true (and join #t) all)
    (check-true (< (index-of-line #rx"depth: enclosure_1 depth 1.50 m" lines) join) "the join comes after the room reaches 1.50 m, not at 1.00 or before")))
