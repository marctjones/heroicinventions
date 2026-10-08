#lang racket/base
;; Issue #163: in the Lonely Rover game every action is the rover's own and passes one capability check: reach, force, never up, and
;; only what a rover could do. Each case runs the real game headless (Jolt, 120 Hz) and drives the rover's hands with the game's own
;; scripted steps (game/scripts/Main.RoverHands.cs: "rover near", "rover press", "rover hand", "rover operate", ...), which go through the
;; code a mouse press and click use (headless has no pixels, so the ray is cast from above the part instead of through the cursor).
;; The numbers are worked out beforehand (RoverSpec, game/scripts/Rover.cs):
;;   reach     the arm's boom 0.8 + stick 0.7 + bucket 0.3 = 1.8 m from its pivot on the deck
;;   push      min(wheels' pull 185 x 9.81 x sin 30 = 907 N, tyre grip 1.0 x 185 x g): 907 N on Earth, 685 N on Mars (g 3.72)
;;   a body    takes mu m g to slide: the 21.6 t boulder (2 m of granite, 2700 kg/m3) on Mars with mu 0.6: 0.6 x 21600 x 3.71 = 48 kN
;;   lifting   none: the hand stops 5 cm above where it took hold
;; Skipped when Godot is not installed.
(require rackunit racket/system racket/port racket/string racket/list racket/runtime-path
         (only-in heroic/godothost godot-available? godot-binary godot-simulate-world final-of values-of))

(define-runtime-path game-dir "../../../game")

;; runs a world with a script of steps; returns the game's output lines
(define (run-game world script #:env [extra '()])
  (define env (environment-variables-copy (current-environment-variables)))
  (environment-variables-set! env #"HEROIC_WORLD" (string->bytes/utf-8 world))
  (environment-variables-set! env #"HEROIC_INPUT" (string->bytes/utf-8 script))
  (for ([kv extra]) (environment-variables-set! env (string->bytes/utf-8 (car kv)) (string->bytes/utf-8 (cdr kv))))
  (define out
    (parameterize ([current-directory game-dir] [current-environment-variables env])
      (with-output-to-string
        (λ () (system* godot-binary "--headless" "--fixed-fps" "120" "--path" ".")))))
  (string-split out "\n"))

(define (lines-with lines prefix) (filter (λ (l) (string-prefix? l prefix)) lines))
(define (refusals lines) (map (λ (l) (substring l (string-length "[rover] refused: "))) (lines-with lines "[rover] refused: ")))
(define (positions lines)   ; every "rover body" line: (x y z)
  (for/list ([l (lines-with lines "[rover] body")])
    (map string->number (cdr (regexp-match #rx"at \\(([-0-9.]+) ([-0-9.]+) ([-0-9.]+)\\)" l)))))
(define (number-in s) (string->number (cadr (regexp-match #rx"([0-9]+[.][0-9]+)" s))))

(when (godot-available?)

  (test-case "reach: a crate 1.0 m from the arm's pivot can be taken hold of, one 3.0 m away is refused with the distance to drive"
    (define lines
      (run-game "rover-hands-check"
                (string-append "wait 120; rover near crate-a:crate 3.0; wait 60; rover press crate-a:crate;"
                               " rover near crate-a:crate 1.0; wait 60; rover press crate-a:crate; rover release; quit")))
    (define presses (lines-with lines "[rover] press"))
    (check-equal? (length presses) 2)
    (check-true (string-contains? (first presses) "not used") "out of reach: no hand took hold")
    (check-true (string-contains? (second presses) "used, hand holds crate") "in reach: the hand holds the crate")
    (check-equal? (length (refusals lines)) 1 "only the far one was refused")
    (define why (first (refusals lines)))
    (check-true (regexp-match? #rx"^Out of reach: drive [0-9.]+ m closer$" why) why)
    (check-= (number-in why) (- 3.0 1.8) 0.15 "3.0 m away, 1.8 m of reach: drive 1.2 m closer"))

  (test-case "reach: 1.7 m is within the arm's 1.8 m, 1.9 m is 0.1 m short"
    (define lines
      (run-game "rover-hands-check"
                (string-append "wait 120; rover near crate-a:crate 1.7; wait 60; rover reach crate-a:crate;"
                               " rover near crate-a:crate 1.9; wait 60; rover reach crate-a:crate; quit")))
    (define reaches (lines-with lines "[rover] reach"))
    (check-true (string-suffix? (first reaches) "within reach") (first reaches))
    (check-true (string-contains? (second reaches) "Out of reach: drive 0.1 m closer") (second reaches)))

  (test-case "direction: a hand dragged 0.6 m up with a crate stops 5 cm above where it took hold, and the rover says it can't lift"
    (define lines
      (run-game "rover-hands-check"
                (string-append "wait 120; rover near crate-a:crate 1.0; wait 90; rover body crate-a:crate;"
                               " rover press crate-a:crate; rover hand 0 0.6 0; wait 120; rover body crate-a:crate;"
                               " wait 120; rover body crate-a:crate; rover release; quit")))
    (define ys (map cadr (positions lines)))
    (check-equal? (length ys) 3)
    (check-true (< (- (apply max ys) (first ys)) 0.06) (format "rose ~a m of the 0.6 asked" (- (apply max ys) (first ys))))
    (check-equal? (refusals lines) '("The rover can't lift loads")))

  (test-case "direction: sideways moves a crate, down is allowed, neither is refused"
    (define lines
      (run-game "rover-hands-check"
                (string-append "wait 120; rover near crate-a:crate 1.0; wait 90; rover body crate-a:crate;"
                               " rover press crate-a:crate; rover hand 0 0 0.5; wait 120; rover body crate-a:crate; rover release; wait 60;"
                               " rover press crate-a:crate; rover hand 0 -0.3 0; wait 120; rover release; quit")))
    (define zs (map caddr (positions lines)))
    ;; the spring takes the held point 0.5 m toward the rover (+z); a 90 kg crate on loam drags with it and stops a little short (measured 0.425)
    (check-true (< 0.35 (- (second zs) (first zs)) 0.55) (format "moved ~a m of 0.5 asked" (- (second zs) (first zs))))
    (check-equal? (refusals lines) '() "nothing refused: sideways and down are what a rover does"))

  (test-case "force: a hand pulls no harder than the wheels push, 907 N on Earth"
    (define lines
      (run-game "rover-hands-check"
                (string-append "wait 120; rover near crate-a:crate 1.0; wait 90; rover press crate-a:crate; rover hand 0 -0.3 0; wait 120;"
                               " rover body crate-a:crate; rover release; quit")))
    (define force (string->number (cadr (regexp-match #rx"hand force ([0-9]+) N" (last (lines-with lines "[rover] body"))))))
    (check-= force 907 1 "held against the ground at the wheels' pull: 185 x 9.81 x sin 30"))

  (test-case "controls: a tap within reach is worked and logged; out of reach, a wheel's drive, pouring water and any unnamed field are refused with why"
    (define lines
      (run-game "rover-hands-check"
                (string-append "wait 120; rover near tank:cistern 1.0; wait 60;"
                               " rover operate tank:cistern tap 1; rover operate tank:cistern water 500; rover operate tank:cistern water 0;"
                               " rover operate tank:cistern capacity 99;"
                               " rover near walkers:tympanus 1.0; wait 60; rover operate walkers:tympanus drive-rpm 10;"
                               " rover near tank:cistern 3.0; wait 30; rover operate tank:cistern tap 0; quit")))
    (define ops (lines-with lines "[rover] operate"))
    (check-equal? (map (λ (l) (last (string-split l ": "))) ops) '("done" "refused" "done" "refused" "refused" "refused"))
    (check-equal? (refusals lines)
                  '("The rover can't pour water: it moves loads sideways or down, never up"
                    "The rover can't set that by hand"
                    "The rover can't turn a treadwheel or a screw by hand"
                    "Out of reach: drive 1.2 m closer"))
    ;; the two allowed actions are in the operator log, the refused ones are not (the log is the replayable record)
    (check-equal? (length (lines-with lines "[operate]")) 2 "tap 1 and water 0 only"))

  (test-case "the 21.6 t boulder nearest the battery bank can't be lifted or dragged: refused with its mass, and it does not move"
    ;; the opening's slide has come down and settled by 3000 frames (25 s); the boulder nearest the battery bank (6.4 m from its crate in this run:
    ;; none came to rest on it, the opening's own check #61 owns that) is 21.6 t of granite; the refusal is by its mass, wherever it lies
    (define lines
      (run-game "lonely-rover-opening"
                (string-append "wait 3000; rover nearboulder battery-bank:crate 1.0; wait 60; rover body boulder:last;"
                               " rover press boulder:last; wait 120; rover body boulder:last; quit")))
    (define why (refusals lines))
    (check-equal? (length why) 1)
    (check-true (string-prefix? (first why) "Too heavy for the rover's arm: 21.6 t") (first why))
    (check-true (string-contains? (first why) "48.1 kN to slide, the rover can push 0.7 kN") "0.6 x 21600 x 3.71 against min(907, 185 x 3.71 = 685) N")
    (check-true (string-contains? (first (lines-with lines "[rover] press")) "hand has nothing"))
    (define ps (positions lines))
    (check-equal? (first ps) (second ps) "the boulder did not move"))

  (test-case "a replayed drag in the game goes through the same check: out of reach it is refused and logged as the rover's refusal, not a failed setting"
    (define drag "drag crate 0 0.25 0 at 3; to 0 2.893 0.5 at 4; release at 4")   ; the crate rests on the flood plain at 2.393 m, its top 2.893
    (define lines (run-game "rover-hands-check" "wait 1000; quit" #:env `(("HEROIC_DRAG" . ,drag) ("HEROIC_QUIT_AFTER_SIM_SECONDS" . "5"))))
    (check-equal? (length (refusals lines)) 1)
    (check-true (string-prefix? (first (refusals lines)) "Out of reach: drive "))
    (check-false (ormap (λ (l) (string-contains? l "HEROIC_DRAG:")) lines) "not reported as a setting that failed"))

  (test-case "a machine run keeps the free operator: the same drag moves the crate when the world has no rover (HEROIC_ROVER=0)"
    (define (crate-z rover?)
      (define run
        (hash-ref (godot-simulate-world 'rover-hands-check #:seconds 6 #:sample-dt 0.5
                                        #:env `(("HEROIC_DRAG" . "drag crate 0 0.25 0 at 3; to 0 2.893 0.5 at 4; release at 4")
                                                ,@(if rover? '() '(("HEROIC_ROVER" . "0")))))
                  'crate-a))
      (final-of run '(crate z)))
    (check-= (crate-z #f) 0.57 0.1 "the free hand takes the held point 0.5 m and the crate comes along")
    (check-= (crate-z #t) 0.0 0.02 "the rover, 8 m away, can't reach it: the crate does not move")))
