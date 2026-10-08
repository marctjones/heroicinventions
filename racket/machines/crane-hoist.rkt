#lang heroic
;; The Roman crane (racket/machines/roman-crane.rkt) without its
;; treadwheel: the 25 cm drum, the jib and pulley, the rope and the 583 kg
;; granite block. Alone nothing turns the drum and the stone sits where it
;; is. In game/worlds/split-crane.world a shaft (issue #78) joins the drum
;; to the treadwheel (racket/machines/walkers-wheel.rkt), a separate machine
;; placed on the same axle line.
;;
;; Predicted: the drum turns at the treadwheel's 3 rpm, winding rope in at
;; 2 pi x 0.25 x 3 / 60 = 7.85 cm/s, and the stone rises as it does in the
;; one-machine crane; the shaft carries what the stone's weight needs at
;; the drum, 583 x 9.81 x 0.25 = 1430 N·m, within the walkers' 1545.
;;
;; The stone starts resting on the ground (#148): its centre 0.30 m up, half
;; its 0.6 m side. (It used to start 5 mm up and settle in the first second;
;; the rope, cut to reach it, is 5 mm shorter now. Traced before, it came
;; down to 0.300 m and sat there; now it sits at 0.300 m from the first frame.)
(require racket/math)

(define drum-r (cm 25))
(define axle (list (m -1.6) (m 2.6) 0))
(define rope-z (m 1.05))
(define jib-base (list (m 1.0) 0 rope-z))
(define jib-reach (m 2.2))
(define jib-height (m 6.8))
(define pulley-r (cm 15))
(define pulley-at (list (+ (car jib-base) jib-reach) (- jib-height pulley-r) rope-z))
(define stone-size (m 0.6))
(define stone-at (list (+ (car pulley-at) pulley-r) (/ stone-size 2) rope-z))
(define over-top (list (car pulley-at) (+ (cadr pulley-at) pulley-r) rope-z))
(define over-side (list (+ (car pulley-at) pulley-r) (cadr pulley-at) rope-z))
(define stone-top (list (car stone-at) (+ (cadr stone-at) (/ stone-size 2)) rope-z))
(define (dist p q) (sqrt (for/sum ([a p] [b q]) (sqr (- a b)))))
(define drum-centre (list (car axle) (cadr axle) rope-z))
(define rope-length (+ (sqrt (- (sqr (dist drum-centre over-top)) (sqr drum-r)))
                       (dist over-top over-side) (dist over-side stone-top)))

(define-machine crane-hoist
  #:source "Vitruvius, De Architectura X.2: the hoist, without its treadwheel"
  (wheel drum #:shape (drum #:radius drum-r #:length (cm 70))
         #:at ((car axle) (cadr axle) rope-z) #:material oak)
  (fixture jib #:shape (crane-jib #:height jib-height #:reach jib-reach #:spread (m 1.6))
           #:at ((car jib-base) 0 rope-z) #:material oak)
  (wheel pulley #:shape (pulley #:radius pulley-r #:width (cm 8))
         #:at ((car pulley-at) (cadr pulley-at) rope-z) #:material bronze)
  (block stone #:at ((car stone-at) (cadr stone-at) rope-z) #:size stone-size #:material granite)
  (rope hoist #:wind-on drum #:to (stone 0 (/ stone-size 2) 0) #:length rope-length
        #:over (((car over-top) (cadr over-top) rope-z) ((car over-side) (cadr over-side) rope-z))
        #:diameter (cm 4) #:turns pulley))
