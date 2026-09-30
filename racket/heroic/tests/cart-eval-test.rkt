#lang racket/base
;; Issue #34: rigid-wheel carts against Godot's VehicleBody3D, both run in the
;; game's own physics (Jolt, 120 Hz) by game/scenes/CartEval.tscn. The scene
;; prints "EVAL <name> <value>" lines; this checks them against the formulas.
;; Skipped when Godot is not installed. See docs/design.html "Carts: rigid
;; wheels or VehicleBody3D".
(require rackunit racket/system racket/port racket/string racket/runtime-path
         (only-in heroic/godothost godot-available? godot-binary))

(define-runtime-path game-dir "../../../game")

(define g 9.81)
(define theta (* 10 (/ 3.141592653589793 180)))
(define M 28.0)                      ; chassis 20 kg + four 2 kg wheels
(define wheel-share 4.0)             ; sum(I/r^2) = 4 x (1/2 x 2 kg)
(define c-rr 0.02)

(define (run-eval)
  (define out
    (parameterize ([current-directory game-dir])
      (with-output-to-string
        (λ () (system* godot-binary "--headless" "--fixed-fps" "120" "--path" "." "res://scenes/CartEval.tscn")))))
  (for/hash ([line (in-list (string-split out "\n"))]
             #:when (string-prefix? line "EVAL "))
    (define parts (string-split line))
    (values (cadr parts) (string->number (caddr parts) 10 'read 'decimal-as-inexact))))

(when (godot-available?)
  (define e (run-eval))
  (define (v k) (hash-ref e k (λ () (error 'cart-eval "no measurement ~a" k))))

  (test-case "a rigid-wheel cart rolls down a slope at g sin(theta) / (1 + sum(I/r^2)/M)"
    (define predicted (/ (* g (sin theta)) (+ 1 (/ wheel-share M))))   ; 1.491 m/s^2
    (check-= (v "RigidSlope.acceleration") predicted 0.03
             "within 2%: the wheels' inertia is felt, and nothing else is")
    ;; a sliding block would do g sin(theta) = 1.70: the wheels are plainly slowing the cart
    (check-true (< (v "RigidSlope.acceleration") (* 0.95 g (sin theta)))))

  (test-case "a coasting rigid cart slows at C_rr g / (1 + sum(I/r^2)/M), which is C_rr g for light wheels"
    (define light-wheel (* c-rr g))                                   ; 0.196 m/s^2, the issue's figure
    (define with-inertia (/ light-wheel (+ 1 (/ wheel-share M))))     ; 0.172 m/s^2: the energy balance
    (define measured (- (v "RigidCoast.acceleration")))
    (check-= measured with-inertia 0.01 "matches the balance that counts the wheels' own inertia")
    (check-true (< (abs (- measured with-inertia)) (abs (- measured light-wheel)))
                "and is closer to it than to the light-wheel C_rr g"))

  (test-case "VehicleBody3D under Jolt does not roll freely: it stops in a fraction of a second and holds a slope"
    ;; no engine force, no brake, yet the car is dead still on a 10 degree slope where a free
    ;; wheel cart is doing 1.5 m/s^2, and a car pushed to 2 m/s is stopped inside a quarter second
    (check-true (< (abs (v "VehicleSlope.speed-at-2.00")) 0.1))
    (check-true (< (abs (v "VehicleCoast.speed-at-0.50")) 0.1))
    (check-true (> (v "RigidCoast.speed-at-2.00") 1.5) "while the rigid cart is still rolling")))
