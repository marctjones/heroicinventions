#lang heroic
;; The Roman crane's treadwheel on its own (racket/machines/roman-crane.rkt):
;; two men walking inside a 4.5 m wheel, turning it at 3 rpm with at most
;; 1545 N·m. Alone it turns nothing. In game/worlds/split-crane.world a
;; shaft (issue #78) joins its axle to the crane hoist's drum, built as a
;; separate machine: together they should lift the stone exactly as the
;; one-machine crane does.
(require racket/math)

(define wheel-r (m 2.25))
(define axle (list (m -1.6) (m 2.6) 0))
(define walkers 2)
(define walker-torque (* 70 9.81 wheel-r (sin (degrees->radians 30))))

(define-machine walkers-wheel
  #:source "Vitruvius, De Architectura X.2: the treadwheel alone"
  (wheel tympanus #:shape (treadwheel #:radius wheel-r #:width (m 1.2))
         #:at ((car axle) (cadr axle) 0) #:material oak
         #:drive-rpm 3 #:drive-torque (* walkers walker-torque)))
