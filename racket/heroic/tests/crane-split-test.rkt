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
;;  - Split, as it was (#191). The shaft traded angular momentum once a
;;    tick, before Jolt stepped the bodies (WorldLinks: pipes, machines,
;;    shafts, then Jolt). The walkers' motor brought their wheel back to 3 rpm
;;    within each step; the next tick's exchange then took J = tau dt from it
;;    to give the drum what the stone took, so the pair turned at
;;    w_c = cap - tau dt / I_wheel, with tau the 1430.6 N m the shaft carried,
;;    dt = 1/120 s and I_wheel = 1818.3 kg m2: 0.31416 - 0.00656 = 0.30760
;;    rad/s (measured 0.307603, the link's 2.937 rpm). The drum alone then lost
;;    its own 0.2 /s damping over the step, w_c (1 - 0.2 dt), and the rope came
;;    in at 0.25 x 0.30709 = 7.677 cm/s (measured 7.6770): 2.25% slower than
;;    the one machine (2.09% the exchange's lag, 0.17% the drum's damping).
;;    Before the walkers took the load the rope, which saw only the drum's
;;    4.5 kg m2, paid out and set the stone down on the ground for 0.4 s.
;;  - Split, now. A shaft between two Jolt bodies on one axle line is also a
;;    lock between them inside Jolt's step, as the wheels of one arbor are
;;    (Main.Links.cs, MachineView.LockAxleTo), and each end's bodies are the
;;    other's arbor-mates, so the rope sees the whole shaft's 1822.8 kg m2.
;;    The motor's cap then holds both ends, the lag is gone, and the split
;;    crane is the one machine: rope at 7.854 cm/s, the shaft at 3.000 rpm
;;    carrying m g r = 1430.3 N m (read after the step: the exchange's share,
;;    I_wheel / (I_wheel + I_drum) of it, plus the lock's; less the drum's own
;;    0.3 N m of damping), and the stone at every frame where the one
;;    machine's is.
;; Both are stepped 1/120 s of sim time at every speed, so 20x is 1x.
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

;; rope speed: the stone's rise over 25 to 30 s, when both have settled
(define (rope-speed run) (/ (- (value-at run 'stone.y 30) (value-at run 'stone.y 25)) 5))

(test-case "Roman crane, one machine and split in two: the split rope runs with the one machine's, the shaft locked within Jolt's step (#191)"
  (when (godot-available?)
    (define I-wheel (machine-inertia 'walkers-wheel 'tympanus))
    (check-= I-wheel (machine-inertia 'roman-crane 'tympanus) 1e-9 "the same treadwheel in both")
    (define (runs speed)
      (define env `(("HEROIC_SPEED" . ,speed)))
      (values (godot-simulate 'roman-crane #:seconds 30 #:sample-dt 0.5 #:actions '() #:env env)
              (godot-simulate-world 'split-crane #:seconds 30 #:sample-dt 0.5 #:env env)))
    (define-values (one world) (runs "1"))
    (define-values (one-20x world-20x) (runs "20"))
    (define (speeds one world)
      (values (rope-speed one) (rope-speed (hash-ref world 'hoist))))
    (define-values (one-1 split-1) (speeds one world))
    (define-values (one-20 split-20) (speeds one-20x world-20x))
    (define links (hash-ref world 'links))
    (define torque (value-at links 'axle.torque 30))
    (define link-w (* (value-at links 'axle.rpm 30) 2 pi 1/60))
    (printf "crane rope speed, m/s: one machine ~a (20x ~a), split ~a (20x ~a); shaft ~a N m at ~a rad/s\n"
            one-1 one-20 split-1 split-20 torque link-w)
    ;; the shaft carries the stone's weight at the drum
    (check-= torque (* 2700 0.6 0.6 0.6 9.81 drum-r) 1.0 "N m: m g r")
    ;; one machine: at the walkers' cap
    (check-= one-1 (* drum-r cap) 1e-4 (format "one machine: ~a m/s, the cap's 0.07854" one-1))
    ;; split: both ends at the cap, the lag of one tick's torque (tau dt / I_wheel = 0.00656 rad/s) gone
    (check-= (value-at (hash-ref world 'walkers) 'tympanus.omega 30) cap 1e-4 "the walkers' own wheel turns at 3 rpm")
    (check-= link-w cap 1e-5 (format "the shaft turns at ~a rad/s, the cap's ~a" link-w cap))
    (check-= (value-at links 'axle.driven-rpm 30) (value-at links 'axle.rpm 30) 1e-9 "one speed both sides")
    ;; so the two ropes agree, within 0.2% (it was 2.25%)
    (define shortfall (/ (- one-1 split-1) one-1))
    (check-= shortfall 0 2e-3 (format "the split crane is ~a% slower" (* 100 shortfall)))
    ;; and the stone rises as the one machine's does once both are at the cap, from a fixed offset. The split's
    ;; stone rests on the ground (crane-hoist.rkt, 0.300 m: #148) and the one machine's still starts 5 mm up
    ;; (roman-crane.rkt, 0.305 m). A resting stone is lifted only after the drum has wound the rope's stretch in, and
    ;; until then the walkers turn the wheel and drum unloaded (1545 / 1822.8 = 0.85 rad/s2), so the split's spin-up
    ;; leads and its stone has risen further by the time both are at the cap (traced 7.7 cm at 20 s): the start-up is
    ;; no longer the same, only the rate.
    (define hoist (hash-ref world 'hoist))
    (check-= (value-at hoist 'stone.y 0) 0.300 1e-4 "the split's stone rests on the ground")
    (check-= (value-at one 'stone.y 0) 0.305 1e-4 "the one machine's starts 5 mm up")
    (define offset (- (value-at hoist 'stone.y 25) (value-at one 'stone.y 25)))
    (check-= offset 0.077 0.003 "the split's stone is 7.7 cm further up")
    (for ([f1 one] [f2 hoist] #:when (>= (car f1) 25))
      (check-= (- (cadr (assq 'stone.y (cdr f2))) (cadr (assq 'stone.y (cdr f1)))) offset 1e-4
               (format "the same rate at ~a s" (car f1))))
    ;; and the speed the game runs at changes none of it
    (check-= one-20 one-1 1e-6 "one machine, 20x as 1x")
    (check-= split-20 split-1 1e-6 "split, 20x as 1x")))
