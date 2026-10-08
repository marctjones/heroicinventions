#lang heroic
;; The Roman crane's treadwheel on its own (racket/machines/roman-crane.rkt):
;; two men walking inside a 4.5 m wheel, turning it at 3 rpm with at most
;; 1545 N·m. Alone it turns nothing. In game/worlds/split-crane.world a
;; shaft (issue #78) joins its axle to the crane hoist's drum, built as a
;; separate machine: together they should lift the stone exactly as the
;; one-machine crane does.
;;
;; Operated (issue #157): click the wheel to stop the walkers (drive-rpm 0), or
;; Shift+click to turn them round. The demo operator walks it round 10 s, stands
;; still 10, and walks it back 10: unloaded, the 1,545 N.m is far more than the
;; wheel needs, so it turns at the full 3 rpm, 18 degrees a second: 180 degrees
;; after 10 s, the same 180 at 20 s, and back at 0 at 30 s.
(require racket/math)

(define wheel-r (m 2.25))
(define axle (list (m -1.6) (m 2.6) 0))
(define walkers 2)
(define walker-torque (* 70 9.81 wheel-r (sin (degrees->radians 30))))

(define-machine walkers-wheel
  #:source "Vitruvius, De Architectura X.2: the treadwheel alone"
  (wheel tympanus #:shape (treadwheel #:radius wheel-r #:width (m 1.2))
         #:at ((car axle) (cadr axle) 0) #:material oak
         #:drive-rpm 3 #:drive-torque (* walkers walker-torque))
  ;; walk it round for 10 s, stand still for 10, walk it back for 10 (issue #157)
  (operator (at 10 (tympanus drive-rpm 0)) (at 20 (tympanus drive-rpm -3)) (at 30 (tympanus drive-rpm 0))))
