#lang racket/base
;; Issue #96, the rover's steps of the fastest route in the crater world (game/worlds/lonely-rover-e2e.world), by the game's own keys
;; (HEROIC_INPUT: hold left, hold up, key b), headless Godot at 120 Hz. The rest of the route is e2e-route-test.rkt; docs/e2e-route.md
;; has the whole list.
;;  - the rover turns left through 96 degrees (about 35 degrees a second) and drives north up the floor 39 m to the works (settled: 1.9 m/s
;;    on the 3 degree floor) and stops within 10 m of the windmill's place (the player builds there, 180 105);
;;  - GAP 1: at the slide the rover digs at four standing places (west, on top, east and north of the crate). Since the dirt rule (#63) a cycle
;;    digs and dumps (0.20 m3); it was "Dug nothing: bedrock under the teeth" before. This is recorded, not asserted: it prints GAP 1 lines.
;;    Freeing the crate takes many cycles (about 1 m of rubble since the owner's ruling of 2026-10-09; it was 4 m) and is not run here: freeing it is optional.
;; Skipped when Godot is not installed.
(require rackunit racket/system racket/port racket/string racket/list racket/math racket/runtime-path
         (only-in heroic/godothost godot-available? godot-binary))

(define-runtime-path game-dir "../../../game")

(define (run-game script)
  (define env (environment-variables-copy (current-environment-variables)))
  (environment-variables-set! env #"HEROIC_WORLD" #"lonely-rover-e2e")
  (environment-variables-set! env #"HEROIC_INPUT" (string->bytes/utf-8 script))
  (string-split
   (parameterize ([current-directory game-dir] [current-environment-variables env])
     (with-output-to-string (λ () (system* godot-binary "--headless" "--fixed-fps" "120" "--path" "."))))
   "\n"))

(define (rover-lines lines) (filter (λ (l) (string-prefix? l "[view] rover:")) lines))
(define (pos l) (map string->number (cdr (regexp-match #rx"at \\(([-0-9.]+) ([-0-9.]+) ([-0-9.]+)\\)" l))))
(define (arm l) (cadr (regexp-match #rx"arm: ([^,]*)," l)))

(when (godot-available?)
  (define attempts '("252.5 137.9 270" "254.5 135.9 270" "258.5 137.9 90" "254.5 132.9 0"))   ; west, on top, east and north of the crate (254.5, 137.9)
  (define lines
    (run-game (string-append "wait 3000; rover; hold left 2.74; hold up 20; wait 120; rover; "
                             (string-join (for/list ([a attempts]) (format "rover place ~a; wait 240; key b; rover until Stowed; rover" a)) "; ")
                             "; quit")))
  (define rs (rover-lines lines))

  (test-case "the rover drives from its start up the floor to the works by the game's keys"
    (define start (pos (first rs)))
    (define arrived (pos (second rs)))
    (check-= (first start) 190 0.1) (check-= (third start) 140 0.1)
    (define vault '(186 101))   ; 6 m from where the route builds the windmill
    (define d (sqrt (+ (sqr (- (first arrived) (first vault))) (sqr (- (third arrived) (second vault))))))
    (check-true (< d 10) (format "the rover stopped ~a m from the build site" d))
    (check-true (> (- 140 (third arrived)) 30) "and drove at least 30 m north")
    (check-true (string-prefix? (arm (second rs)) "Backhoe stowed")))

  (test-case "GAP 1 (recorded): digging the slide's rubble over the bank, four places"
    (define digs (drop rs 2))
    (check-equal? (length digs) (length attempts) "a dig at each place")
    (define outcomes (map arm digs))
    (for ([a attempts] [o outcomes]) (printf "GAP 1: rover at ~a: ~a\n" a o))
    (check-true (andmap string? outcomes))))
