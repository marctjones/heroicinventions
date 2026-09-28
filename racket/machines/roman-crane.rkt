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
        #:diameter (cm 4)))
