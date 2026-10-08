#lang racket/base
;; Issue #94: the player's rover, six hinge-driven wheels, run in the game's own physics (Jolt, 120 Hz) by
;; game/scenes/RoverEval.tscn, which prints "EVAL <name> <value>" lines. Checked here against the numbers
;; worked out beforehand (game/scripts/Rover.cs, RoverSpec): 2 m/s game speed, a steepest climb of 30 degrees set by the
;; tyres' grip (tan 30 = 0.577) and the same on a box, a triangle mesh and a height map (#198), a brake that holds up to it,
;; and the backhoe's volumes (what is dug is what is dumped, uphill too; never bedrock).
;; Skipped when Godot is not installed.
(require rackunit racket/system racket/port racket/string racket/runtime-path
         (only-in heroic/godothost godot-available? godot-binary))

(define-runtime-path game-dir "../../../game")

(define game-speed 2.0)     ; m/s, RoverSpec.GameSpeed
(define bucket 0.2)         ; m^3, Rover.BucketVolume

(define (run-eval)
  (define out
    (parameterize ([current-directory game-dir])
      (with-output-to-string
        (λ () (system* godot-binary "--headless" "--fixed-fps" "120" "--path" "." "res://scenes/RoverEval.tscn")))))
  (for/hash ([line (in-list (string-split out "\n"))]
             #:when (string-prefix? line "EVAL "))
    (define parts (string-split line))
    (values (cadr parts)
            (or (string->number (caddr parts) 10 'read 'decimal-as-inexact) (caddr parts)))))

(when (godot-available?)
  (define e (run-eval))
  (define (v k) (hash-ref e k (λ () (error 'rover-eval "no measurement ~a" k))))

  (test-case "driving forward moves the rover at the game speed, metres per second, in a straight line"
    ;; the last 4 s of a 10 s run, after the speed has come up (2.5 m/s^2): 4 s x 2 m/s = 8 m
    (check-= (* 4 (v "flat.cruise")) (* 4 game-speed) 0.25)
    (check-= (v "flat.speed-at-1") game-speed 0.05 "it is up to speed inside a second")
    (check-true (< (v "flat.stray") 0.3) "no more than 30 cm off the line in 19 m")
    (check-true (< (v "flat.tilt-deg") 1)))

  (test-case "reversing is the same speed backwards"
    (check-= (v "reverse.cruise") (- game-speed) 0.06))

  (test-case "turning in place: skid steering gives about 0.64 rad/s of the 0.9 asked, and the rover stays where it is"
    (check-true (< 0.5 (abs (v "turn.yaw-rate")) 0.9))
    (check-true (< (v "turn.drift") 0.25)))

  (test-case "it climbs slopes up to 30 degrees at speed and refuses steeper ones: the tyres' grip, tan 30 = 0.577, is the limit"
    ;; up a slope theta the wheels push with at most mu m g cos theta against m g sin theta: tan theta = 0.577, 30 degrees.
    ;; The motors (6 x 42 N.m / 0.15 m = 1680 N) would climb 68. Measured: it stalls at 29.9 from rest.
    (for ([deg '(20 24 28)])
      (check-true (> (v (format "climb-~a.cruise" deg)) 1.5) (format "climbs ~a degrees at ~a m/s" deg (v (format "climb-~a.cruise" deg)))))
    (for ([deg '(31 33 36 40)])
      (check-true (< (v (format "climb-~a.cruise" deg)) 0.3) (format "does not climb ~a degrees" deg))))

  (test-case "the grade is the same on a box, a triangle mesh and a height map (#198: it was 29, 30-31 and 31-38 degrees)"
    ;; from rest, 29 degrees climbs and 31 does not on all three, and the speeds agree: the stall is the same within a degree
    (for ([shape '("box" "mesh" "height")])
      (check-true (> (v (format "grade-~a-29.cruise" shape)) 0.5) (format "climbs 29 degrees on the ~a" shape))
      (check-true (< (v (format "grade-~a-31.cruise" shape)) 0) (format "rolls back on 31 degrees on the ~a" shape)))
    (for ([deg '(29 31)])
      (check-= (v (format "grade-height-~a.cruise" deg)) (v (format "grade-box-~a.cruise" deg)) 0.05 (format "height map as box at ~a" deg))
      (check-= (v (format "grade-mesh-~a.cruise" deg)) (v (format "grade-box-~a.cruise" deg)) 0.05 (format "mesh as box at ~a" deg))))

  (test-case "with the wheels braked it holds a slope it can climb, and slides on one it can't: the same grip"
    (for ([deg '(20 24 28)])
      (check-true (> (v (format "hold-~a.distance" deg)) -0.3) (format "holds ~a degrees" deg)))
    (for ([deg '(31 33 36 40)])
      (check-true (< (v (format "hold-~a.distance" deg)) -0.3) (format "slides on ~a degrees" deg))))

  (test-case "on the crater's own height map it drives at speed, stays on the ground, and stops when told"
    (check-= (v "crater.cruise-8s") game-speed 0.15 "a little under on the 10 degree slope")
    (check-= (v "crater.rescues") 0 0)
    (check-true (< 0.1 (v "crater.above-ground") 0.2) "axle one wheel radius above the ground")
    (check-true (< (v "crater.tilt-deg") 30))
    (check-true (< (abs (v "crater.stopped-speed")) 0.01)))

  (test-case "the backhoe digs a bucketful and dumps the same volume: what is dug is what is dumped"
    (check-= (v "dig.dug") bucket 1e-9)
    (check-= (v "dig.dumped") (v "dig.dug") 1e-9)
    (check-= (v "dig.carried") 0 1e-9)
    (check-= (v "dig.ground-volume-change") 0 1e-9 "the ground has the volume it began with")
    (check-equal? (v "dig.cycles") 1))

  (test-case "it dumps above where it dug (#72, owner decision 2026-10-08: soil goes wherever the arm reaches)"
    (check-= (v "dig-uphill.dumped") bucket 1e-9)
    (check-= (v "dig-uphill.carried") 0 1e-9)
    (check-= (v "dig-uphill.ground-volume-change") 0 1e-9 "the ground has the volume it began with")
    (check-true (string-prefix? (v "dig-uphill.status") "Dumped")))

  (test-case "it cannot cut bedrock"
    (check-= (v "dig-bedrock.dug") 0 1e-9)
    (check-= (v "dig-bedrock.dumped") 0 1e-9)))
