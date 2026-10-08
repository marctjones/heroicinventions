#lang racket/base
;; Issue #94: the player's rover, six hinge-driven wheels, run in the game's own physics (Jolt, 120 Hz) by
;; game/scenes/RoverEval.tscn, which prints "EVAL <name> <value>" lines. Checked here against the numbers
;; worked out beforehand (game/scripts/Rover.cs, RoverSpec): 2 m/s game speed, a steepest climb of about 30 degrees,
;; a brake that holds past it, and the backhoe's volumes (what is dug is what is dumped; never upward; never bedrock).
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

  (test-case "it climbs slopes up to about 30 degrees at speed and refuses steeper ones"
    ;; the steepest it climbs is set by the motors' torque (Jolt gives about half what it is told), measured at 30 to 31 degrees
    (for ([deg '(20 24 28)])
      (check-true (> (v (format "climb-~a.cruise" deg)) 1.5) (format "climbs ~a degrees at ~a m/s" deg (v (format "climb-~a.cruise" deg)))))
    (for ([deg '(33 36 40)])
      (check-true (< (v (format "climb-~a.cruise" deg)) 0.3) (format "does not climb ~a degrees" deg))))

  (test-case "with the wheels braked it holds a slope, even past the one it can climb, until it is steep enough to slide"
    (for ([deg '(20 24 28 30 33 36)])
      (check-true (> (v (format "hold-~a.distance" deg)) -0.3) (format "holds ~a degrees" deg)))
    (check-true (< (v "hold-40.distance") -0.3) "40 degrees is more than the tyres can hold"))

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

  (test-case "it will not dump above where it dug (#72), and keeps the soil in the bucket"
    (check-= (v "dig-uphill.dumped") 0 1e-9)
    (check-= (v "dig-uphill.carried") bucket 1e-9)
    (check-= (v "dig-uphill.ground-volume-change") (- bucket) 1e-9 "the volume is in the bucket, not lost")
    (check-true (string-prefix? (v "dig-uphill.status") "Kept")))

  (test-case "it cannot cut bedrock"
    (check-= (v "dig-bedrock.dug") 0 1e-9)
    (check-= (v "dig-bedrock.dumped") 0 1e-9)))
