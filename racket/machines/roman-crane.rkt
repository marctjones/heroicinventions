#lang heroic
;; A Roman building crane (Vitruvius X.2; the Haterii relief, c. AD 100):
;; men walking inside a 4.5 m treadwheel turn its axle; a 25 cm drum on
;; the same axle (an arbor) winds in the hoisting rope, which runs over a
;; pulley at the top of the jib and down to a block of granite.
;;
;; The wheel and axle is a lever that goes round: the walkers push at the
;; wheel's radius, the stone pulls at the drum's, so the walkers need only
;; 25/225 of the stone's weight — a mechanical advantage of 9. Nothing
;; here computes that: the rope's tension turns the drum, the drum shares
;; its axle with the wheel, and the walkers' torque (below) either beats
;; the stone's or doesn't.
;;
;; A walker standing a third of the way up the wheel's side (30° from the
;; bottom) turns it with his weight × radius × sin 30°. Two men give
;; ~1,550 N·m against the stone's ~1,430 N·m, so they lift it, slowly; one
;; man alone (~770 N·m) can't — the stone would run the wheel backwards.
;;
;; Operated (issue #157): lift, hold, lower. The walkers' motor is the wheel's
;; drive (click the treadwheel: drive-rpm to 0 holds the stone, Shift+click
;; reverses it; right-click for drive-torque, 0 lets the walkers go and the
;; stone runs the wheel back). The demo operator lifts for 20 s, holds for 10,
;; then walks it down for 20. The margin is thin: 1,545 N.m against the stone's
;; m g r = 1,430 N.m, so 115 N.m accelerates the wheel and drum (I = 1,823
;; kg.m2, 1,861 with the stone and pulley) against the axle's 0.2 /s damping:
;; the wheel comes up to w = 115 / (0.2 x 1,823) = 0.315 rad/s (the motor's 3 rpm,
;; 0.314) with a time constant 1,861 / 364.5 = 5.1 s, and the stone, rising at r w,
;; has climbed 0.25 x 0.314 x (t - 5.1) in t s: about 1.18 m by 20 s (1.15 above
;; where it started, less the coasting). Held at 0 rpm it hangs still (the motor's
;; 1,545 N.m against 1,430); reversed at -3 rpm it comes down at exactly
;; r x 3 rpm = 7.854 cm/s, to the ground after 1.21 / 0.0785 = 15.4 s: 45.4 s in.
(require racket/math)

(define wheel-r (m 2.25))
(define drum-r (cm 25))
(define axle (list (m -1.6) (m 2.6) 0))
(define rope-z (m 1.05))                 ; the drum, and the plane the rope runs in
(define walkers 2)
(define walker-torque (* 70 9.81 wheel-r (sin (degrees->radians 30))))

;; The jib's feet stand clear of the treadwheel (whose rim reaches x = 0.65).
(define jib-base (list (m 1.0) 0 rope-z))
(define jib-reach (m 2.2))
(define jib-height (m 6.8))
(define pulley-r (cm 15))
(define pulley-at (list (+ (car jib-base) jib-reach) (- jib-height pulley-r) rope-z))
(define stone-size (m 0.6))              ; granite: 583 kg
(define stone-at (list (+ (car pulley-at) pulley-r) (+ (/ stone-size 2) (mm 5)) rope-z))

;; The rope runs from the drum over the pulley's top and down its far side
;; to the stone; its length is that path, so it starts just taut.
(define over-top (list (car pulley-at) (+ (cadr pulley-at) pulley-r) rope-z))
(define over-side (list (+ (car pulley-at) pulley-r) (cadr pulley-at) rope-z))
(define stone-top (list (car stone-at) (+ (cadr stone-at) (/ stone-size 2)) rope-z))
(define (dist p q) (sqrt (for/sum ([a p] [b q]) (sqr (- a b)))))
(define drum-centre (list (car axle) (cadr axle) rope-z))
(define rope-length (+ (sqrt (- (sqr (dist drum-centre over-top)) (sqr drum-r)))
                       (dist over-top over-side) (dist over-side stone-top)))

(define-machine roman-crane
  #:source "Vitruvius, De Architectura X.2; Haterii relief, c. AD 100"
  (wheel tympanus #:shape (treadwheel #:radius wheel-r #:width (m 1.2))
         #:at ((car axle) (cadr axle) 0) #:material oak
         #:drive-rpm 3 #:drive-torque (* walkers walker-torque))
  (wheel drum #:shape (drum #:radius drum-r #:length (cm 70))
         #:at ((car axle) (cadr axle) rope-z) #:material oak)
  (arbor tympanus drum)
  (fixture jib #:shape (crane-jib #:height jib-height #:reach jib-reach #:spread (m 1.6))
           #:at ((car jib-base) 0 rope-z) #:material oak)
  (wheel pulley #:shape (pulley #:radius pulley-r #:width (cm 8))
         #:at ((car pulley-at) (cadr pulley-at) rope-z) #:material bronze)
  (block stone #:at ((car stone-at) (cadr stone-at) rope-z) #:size stone-size #:material granite)
  (rope hoist #:wind-on drum #:to (stone 0 (/ stone-size 2) 0) #:length rope-length
        #:over (((car over-top) (cadr over-top) rope-z) ((car over-side) (cadr over-side) rope-z))
        #:diameter (cm 4) #:turns pulley)
  ;; the walkers lift for 20 s, stand still holding the stone for 10, then walk it back down (issue #157)
  (operator (at 20 (tympanus drive-rpm 0)) (at 30 (tympanus drive-rpm -3)) (at 50 (tympanus drive-rpm 0))))
