#lang racket/base
;; Issue #63: the rover's earthworks on the fine ground laid over the map's 5 m cells, run in the game's own physics
;; (Jolt, 120 Hz) by game/scenes/RoverDigEval.tscn, which prints "EVAL <name> <value>" lines. Checked here against the
;; numbers worked out beforehand (game/scripts/RoverDigEval.cs, Rover.Backhoe.cs, Sim/Fluids/WorkedGround.cs):
;;  - a bucketful of 0.2 m3 scrapes the ten or so 0.25 m nodes within 0.45 m of the teeth, about a third of a metre deep (one 5 m
;;    cell would drop 8 mm), and tipped makes a heap of about 0.35 m that stands at the soil's repose (tan 0.7);
;;  - every m3 dug is dumped: the patch ends where it began, to 1e-9;
;;  - a 45 degree bank of ice-cemented soil stops the rover; eight bucketfuls cut from beside the path and tipped at its foot
;;    make a ramp of about 24 degrees from foot to crest, and the rover then climbs it.
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

  (test-case "on the fine ground the rover climbs to 41 degrees and stalls at 43 (the plane test's 30 to 31 is for a tilted box, not a height map)"
    (for ([deg '(30 35 41)]) (check-true (> (v (format "slope-~a.climb.furthest-x" deg)) 15) (format "climbs ~a degrees" deg)))
    (for ([deg '(43 45)]) (check-true (< (v (format "slope-~a.climb.furthest-x" deg)) 5) (format "stalls at ~a degrees" deg)))
    (check-true (< (v "plane-30.travel") 1) "the same 30 degrees as a tilted box is not climbed")
    (check-true (< (v "plane-35.travel") 1)))

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
