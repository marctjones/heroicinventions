#lang racket/base
;; Issue #63: the rover's earthworks on the fine ground laid over the map's 5 m cells, run in the game's own physics
;; (Jolt, 120 Hz) by game/scenes/RoverDigEval.tscn, which prints "EVAL <name> <value>" lines. Checked here against the
;; numbers worked out beforehand (game/scripts/RoverDigEval.cs, Rover.Backhoe.cs, Sim/Fluids/WorkedGround.cs):
;;  - a bucketful of 0.2 m3 scrapes the ten or so 0.25 m nodes within 0.45 m of the teeth, about a third of a metre deep (one 5 m
;;    cell would drop 8 mm), and tipped makes a heap of about 0.35 m that stands at the soil's repose (tan 0.7);
;;  - every m3 dug is dumped: the patch ends where it began, to 1e-9;
;;  - a 45 degree bank of ice-cemented soil stops the rover; eight bucketfuls cut from beside the path and tipped at its foot
;;    make a ramp of about 24 degrees from foot to crest, and the rover then climbs it;
;;  - the rover's grade is its tyres' grip, tan 30 = 0.577 (#198): on the fine ground (a height map) it climbs 29 degrees from rest
;;    and not 31, as on a box; at 30 degrees grip and weight balance (0.577 cos 30 = sin 30), so a run-up carries it at a steady speed.
;; Skipped when Godot is not installed.
(require rackunit racket/system racket/port racket/string racket/runtime-path
         (only-in heroic/godothost godot-available? godot-binary))

(define-runtime-path game-dir "../../../game")

(define (run-eval)
  (define out
    (parameterize ([current-directory game-dir])
      (with-output-to-string
        (λ () (system* godot-binary "--headless" "--fixed-fps" "120" "--path" "." "res://scenes/RoverDigEval.tscn")))))
  (for/hash ([line (in-list (string-split out "\n"))]
             #:when (string-prefix? line "EVAL "))
    (define parts (string-split line))
    (values (cadr parts)
            (or (string->number (caddr parts) 10 'read 'decimal-as-inexact) (caddr parts)))))

(when (godot-available?)
  (define e (run-eval))
  (define (v k) (hash-ref e k (λ () (error 'rover-dig "no measurement ~a" k))))

  (test-case "on the fine ground (a height map) the rover's grade is 30 degrees, as on a box (#198: it was 41 with a run-up)"
    ;; from rest: 29 degrees climbs (grip 0.577 cos 29 = 0.505 against sin 29 = 0.485), 31 does not (0.495 against 0.515)
    (check-true (> (v "rest-29.travel") 3) "climbs 29 degrees from rest")
    (check-true (< (v "rest-31.furthest") 0.5) "does not climb 31 degrees from rest")
    (check-= (v "rest-31.rescues") 0 0)
    ;; with a 2 m/s run-up: at 30 degrees the forces balance and it keeps the speed it came with; at 31 it slows at
    ;; g (sin 31 - 0.577 cos 31) = 0.2 m/s^2 and stops in a few metres, short of the 8 m rise (15.5 m along); 45 is far beyond
    (check-true (> (v "slope-30.climb.furthest-x") 15) "a run-up carries it up 30 degrees")
    (check-true (< (v "slope-31.climb.furthest-x") 10) "31 degrees is refused even with a run-up")
    (check-true (< (v "slope-45.climb.furthest-x") 5) "stalls at 45 degrees")
    (check-true (< (v "plane-35.travel") 1) "the same 35 degrees as a tilted box is not climbed"))

  (test-case "six buckets make a trench and heaps that can be seen, and every m3 dug is dumped"
    ;; 0.2 m3 over the nodes within 0.45 m of the teeth: about a third of a metre deep, where a 5 m cell would drop 8 mm
    (check-true (< 0.3 (v "trench.deepest") 0.45))
    (check-true (> (v "trench.deepest") (* 30 (v "trench.coarse-bucket-depth"))))
    ;; a heap of 0.2 m3 at repose 0.7 is a cone 0.455 m high, lower on a square grid whose diagonals hold it round
    (check-true (< 0.3 (v "trench.heap-height") 0.46))
    (check-= (v "trench.steepest-step-grade") 0.7 1e-6 "no step steeper than the soil's repose")
    (check-= (v "trench.dug") 1.2 1e-9)
    (check-= (v "trench.dumped") 1.2 1e-9)
    (check-= (v "trench.net-volume") 0 1e-9)
    (check-= (v "trench.rover-above-ground") 0.15 0.02 "it stands on the patch, a wheel radius up")
    (check-true (< (v "trench.trench.refresh-max-ms") 8.33) "a patch's redraw fits in a tick"))

  (test-case "a 45 degree bank stops the rover; the ramp built at its foot, under 30 degrees from foot to crest, is climbed"
    (check-= (v "bank.attempt.on-top") 0 0)
    (check-true (< (v "bank.attempt.furthest-x") 1.0) "it gets onto the face and no further")
    (check-= (v "ramp.before.on-top") 0 0)
    (check-= (v "ramp.dug") 1.6 1e-9 "eight buckets")
    (check-= (v "ramp.dumped") 1.6 1e-9)
    (check-= (v "ramp.net-volume") 0 1e-9)
    (check-true (< 20 (v "ramp.ramp-mean-grade-deg") 30) "worked: 1.0 m in 2.5 m is 21.8 degrees; measured with the heaps' own rise")
    (check-= (v "ramp.after.on-top") 1 0)
    (check-= (v "ramp.after.rescues") 0 0)))
