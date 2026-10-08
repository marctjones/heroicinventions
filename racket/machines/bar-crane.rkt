#lang heroic
;; The Roman crane (roman-crane.rkt) without its pulley: the hoisting rope
;; runs over a fixed oak bar at the jib head instead, as a rope over a
;; beam end did before sheaves. Two cranes, 9 m apart, each with the
;; same 583 kg granite block and the same 4.5 m treadwheel and 25 cm drum.
;;
;; With a pulley, two men (~1,550 N.m) beat the stone's ~1,430 N.m. Over a
;; bar, the rope drags: lifting, the drum's side must pull e^(mu theta)
;; times the stone's (the capstan equation). Hemp on oak, mu = 0.4743. The
;; rope leaves the drum climbing at 38.9 degrees (41.2 to the bar's top,
;; less the 2.2 degrees its tangent off the drum takes) and hangs straight
;; down from the bar: it turns 128.9 degrees there, 2.250 rad, so
;; e^(mu theta) = 2.908, and lifting the 583 kg stone (5,721 N) needs
;; 16.6 kN on the drum side, 4,160 N.m at the drum.
;;   two walkers  (1,545 N.m, 6,180 N at the drum): can't lift it. Only
;;                6,180 / 2.908 = 2,125 N reaches the stone, which stays
;;                on the ground.
;;   seven walkers (5,408 N.m): lift it at the wheel's 3 rpm, 7.85 cm/s,
;;                the drum side carrying 2.908 times the stone side.
;; The friction's work warms the bar; it glows while the rope slides.
;;
;; Operated (issue #157): the seven walkers lift, hold and lower. Their motor is
;; seven-wheel's drive (click the wheel: drive-rpm 0 holds, Shift+click reverses;
;; right-click for drive-torque). The demo operator lets them lift for 20 s, stand
;; still for 10, and walk the stone down for 20; the two cannot lift it and the
;; demo leaves them alone. With 5,408 N.m against the bar's 2.908 x 1,430 = 4,160
;; the motor has 1,250 N.m to spare and holds its 3 rpm: the stone rises 7.854
;; cm/s (r x 3 rpm), 0.305 + 0.0785 x 20 = 1.876 m after 20 s, hangs there, and
;; comes down at the same speed to the ground, 1.57 / 0.0785 = 20 s after the
;; reversal (50 s), less the 0.3 s the motor takes to reach its speed.
(require racket/math racket/list)

(define wheel-r (m 2.25))
(define drum-r (cm 25))
(define rope-z (m 1.05))
(define walker-torque (* 70 9.81 wheel-r (sin (degrees->radians 30))))
(define jib-reach (m 2.2))
(define jib-height (m 6.8))
(define bar-r (cm 15))
(define stone-size (m 0.6))
(define (dist p q) (sqrt (for/sum ([a p] [b q]) (sqr (- a b)))))

;; One crane's numbers, its axle at x = -1.6 and everything shifted by dz.
(define axle-x (m -1.6))
(define axle-y (m 2.6))
(define jib-x (m 1.0))
(define bar-x (+ jib-x jib-reach))
(define bar-y (- jib-height bar-r))
(define stone-x (+ bar-x bar-r))
(define stone-y (+ (/ stone-size 2) (mm 5)))
;; three points on the bar: its top, 45 degrees round, its far side
(define over-pts
  (for/list ([deg '(90 45 0)])
    (list (+ bar-x (* bar-r (cos (degrees->radians deg)))) (+ bar-y (* bar-r (sin (degrees->radians deg)))))))
(define rope-length
  (+ (sqrt (- (sqr (dist (list axle-x axle-y) (first over-pts))) (sqr drum-r)))
     (dist (first over-pts) (second over-pts)) (dist (second over-pts) (third over-pts))
     (- (cadr (third over-pts)) (+ stone-y (/ stone-size 2)))))
(define (ox k) (car (list-ref over-pts k)))
(define (oy k) (cadr (list-ref over-pts k)))

(define two-z (m 0))
(define seven-z (m 0))
(define dx (m 9))                        ; the seven-walker crane stands 9 m along
;; (not behind: two wheels on one axis line would be drawn on one shaft)

(define-machine bar-crane
  #:source "Vitruvius, De Architectura X.2, with a fixed bar for the pulley"
  ;; two walkers
  (wheel two-wheel #:shape (treadwheel #:radius wheel-r #:width (m 1.2))
         #:at (axle-x axle-y two-z) #:material oak
         #:drive-rpm 3 #:drive-torque (* 2 walker-torque))
  (wheel two-drum #:shape (drum #:radius drum-r #:length (cm 70))
         #:at (axle-x axle-y (+ two-z rope-z)) #:material oak)
  (arbor two-wheel two-drum)
  (fixture two-jib #:shape (crane-jib #:height jib-height #:reach jib-reach #:spread (m 1.6))
           #:at (jib-x 0 (+ two-z rope-z)) #:material oak)
  (block two-stone #:at (stone-x stone-y (+ two-z rope-z)) #:size stone-size #:material granite)
  (rope two-hoist #:wind-on two-drum #:to (two-stone 0 (/ stone-size 2) 0) #:length rope-length
        #:over (((ox 0) (oy 0) (+ two-z rope-z)) ((ox 1) (oy 1) (+ two-z rope-z)) ((ox 2) (oy 2) (+ two-z rope-z)))
        #:diameter (cm 4) #:bar oak)
  ;; seven walkers
  (wheel seven-wheel #:shape (treadwheel #:radius wheel-r #:width (m 1.2))
         #:at ((+ dx axle-x) axle-y seven-z) #:material oak
         #:drive-rpm 3 #:drive-torque (* 7 walker-torque))
  (wheel seven-drum #:shape (drum #:radius drum-r #:length (cm 70))
         #:at ((+ dx axle-x) axle-y (+ seven-z rope-z)) #:material oak)
  (arbor seven-wheel seven-drum)
  (fixture seven-jib #:shape (crane-jib #:height jib-height #:reach jib-reach #:spread (m 1.6))
           #:at ((+ dx jib-x) 0 (+ seven-z rope-z)) #:material oak)
  (block seven-stone #:at ((+ dx stone-x) stone-y (+ seven-z rope-z)) #:size stone-size #:material granite)
  (rope seven-hoist #:wind-on seven-drum #:to (seven-stone 0 (/ stone-size 2) 0) #:length rope-length
        #:over (((+ dx (ox 0)) (oy 0) (+ seven-z rope-z)) ((+ dx (ox 1)) (oy 1) (+ seven-z rope-z)) ((+ dx (ox 2)) (oy 2) (+ seven-z rope-z)))
        #:diameter (cm 4) #:bar oak)
  ;; the seven walkers lift for 20 s, hold the stone for 10, walk it back down (the two stay at their wheel: they cannot lift it) (issue #157)
  (operator (at 20 (seven-wheel drive-rpm 0)) (at 30 (seven-wheel drive-rpm -3)) (at 50 (seven-wheel drive-rpm 0))))
