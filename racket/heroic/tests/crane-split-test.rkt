#lang racket/base
;; Issue #86, part 1: the Roman crane as one machine (roman-crane.rkt: the
;; treadwheel and drum locked on one arbor) against the same crane split in
;; two (game/worlds/split-crane.world: walkers-wheel.rkt's treadwheel turning
;; crane-hoist.rkt's drum through a shaft, #78). Both run with no demo
;; operator, so the walkers just walk, at 1x and at 20x (the speed a machine
;; with a demo operator opens at; every step is 1/120 s of machine time
;; either way, and the walkers' motor gives the same impulse per step since
;; it was made to use the step's sim length).
;;
;; Worked before the run that checks it:
;;  - One machine. The walkers' 1545 N m against the stone's m g r = 583.2 x
;;    9.81 x 0.25 = 1430.3 N m leave 115 N m to spin up the wheel, drum and
;;    stone against the axle's 0.2 /s damping: they would head for 115 / (0.2
;;    x 1823) = 0.3155 rad/s, past the walkers' 3 rpm (0.31416), so by 25 s
;;    the wheel turns at the 3 rpm cap and the rope comes in at 0.25 x
;;    0.31416 = 7.854 cm/s.
;;  - Split. The shaft trades angular momentum once a tick, before Jolt steps
;;    the bodies (WorldLinks: pipes, machines, shafts, then Jolt). The walkers'
;;    motor brings their wheel back to 3 rpm within each step; the next tick's
;;    exchange then takes J = tau dt from it to give the drum what the stone
;;    took, so at the exchange (and through the step that follows) the pair
;;    turn at w_c = cap - tau dt / I_wheel, with tau the 1430.6 N m the shaft
;;    carries, dt = 1/120 s and I_wheel = 1818.3 kg m2: 0.31416 - 0.00656 =
;;    0.30760 rad/s (the link's 2.937 rpm). The drum alone then loses its own
;;    0.2 /s damping over the step, w_c (1 - 0.2 dt), and the rope comes in
;;    at 0.25 x 0.30709 = 7.677 cm/s: 2.25% slower than the one machine
;;    (2.09% the exchange's lag, 0.17% the drum's damping in the step). In
;;    the one machine the drum is locked to the wheel inside Jolt's step, so
;;    the motor's cap holds for both and neither lag arises.
;; The lag is the coupling's, one tick of the shaft's torque against the
;; wheel's inertia; it scales with the step (1/120 s at every speed), not
;; with the speed the game runs at.
(require rackunit heroic/godothost racket/list racket/runtime-path
         (only-in racket/math pi)
         (only-in heroic/materials material-table material-field))

(define-runtime-path compiled-machines "../../../game/machines")
(define (machine-inertia machine part)
  ;; density x inertia-z of a part, from the compiled .machine file
  (define form (call-with-input-file (build-path compiled-machines (format "~a.machine" machine)) read))
  (define p (for/first ([c (cddr form)] #:when (and (pair? c) (eq? (car c) 'part) (eq? (cadr c) part))) c))
  (define (field l k) (for/first ([x l] #:when (and (pair? x) (eq? (car x) k))) (cadr x)))
  (* (material-field (assq (field p 'material) (material-table)) 'density) (field (cdr (assq 'props (cdddr p))) 'inertia-z)))

;; target.field in the frame nearest t seconds
(define (value-at run key t)
  (define frame (argmin (λ (f) (abs (- (car f) t))) run))
  (cadr (assq key (cdr frame))))

(define drum-r 0.25)
(define cap (* 3 2 pi 1/60))      ; the walkers' 3 rpm, rad/s
(define dt 1/120)                 ; s of machine time a step, at every speed
(define damping 0.2)              ; /s on each axle's body

;; rope speed: the stone's rise over 25 to 30 s, when both have settled
(define (rope-speed run) (/ (- (value-at run 'stone.y 30) (value-at run 'stone.y 25)) 5))

(test-case "Roman crane, one machine and split in two: the split rope runs 2.25% slow, one tick of the shaft's torque against the wheel"
  (when (godot-available?)
    (define I-wheel (machine-inertia 'walkers-wheel 'tympanus))
    (check-= I-wheel (machine-inertia 'roman-crane 'tympanus) 1e-9 "the same treadwheel in both")
    (define (speeds speed)
      (define env `(("HEROIC_SPEED" . ,speed)))
      (define one (godot-simulate 'roman-crane #:seconds 30 #:sample-dt 0.5 #:actions '() #:env env))
      (define world (godot-simulate-world 'split-crane #:seconds 30 #:sample-dt 0.5 #:env env))
      (define torque (value-at (hash-ref world 'links) 'axle.torque 30))
      (values (rope-speed one) (rope-speed (hash-ref world 'hoist)) torque
              (* (value-at (hash-ref world 'links) 'axle.rpm 30) 2 pi 1/60)
              (value-at (hash-ref world 'walkers) 'tympanus.omega 30)))
    (define-values (one-1 split-1 torque link-w wheel-w) (speeds "1"))
    (define-values (one-20 split-20 torque-20 link-w-20 wheel-w-20) (speeds "20"))
    (printf "crane rope speed, m/s: one machine ~a (20x ~a), split ~a (20x ~a); shaft ~a N m at ~a rad/s, walkers' wheel ~a rad/s\n"
            one-1 one-20 split-1 split-20 torque link-w wheel-w)
    ;; the shaft carries the stone's weight at the drum
    (check-= torque (* 2700 0.6 0.6 0.6 9.81 drum-r) 1.0 "N m: m g r")
    ;; one machine: at the walkers' cap
    (check-= one-1 (* drum-r cap) 1e-4 (format "one machine: ~a m/s, the cap's 0.07854" one-1))
    ;; split: the walkers' wheel ends each step at the cap; the exchange takes tau dt off it
    (check-= wheel-w cap 1e-4 "the walkers' own wheel turns at 3 rpm")
    (define w-c (- cap (/ (* torque dt) I-wheel)))
    (check-= link-w w-c 2e-5 (format "the shaft turns at ~a rad/s, predicted ~a" link-w w-c))
    (define predicted (* drum-r w-c (- 1 (* damping dt))))
    (check-= split-1 predicted 5e-5 (format "split: ~a m/s, predicted ~a" split-1 predicted))
    ;; so they differ, by the derived 2.25%
    (define shortfall (/ (- one-1 split-1) one-1))
    (check-= shortfall (- 1 (/ predicted (* drum-r cap))) 1e-3 (format "the split crane is ~a% slower" (* 100 shortfall)))
    ;; and the speed the game runs at changes none of it
    (check-= one-20 one-1 1e-6 "one machine, 20x as 1x")
    (check-= split-20 split-1 1e-6 "split, 20x as 1x")))
