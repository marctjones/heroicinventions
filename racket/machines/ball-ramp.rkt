#lang heroic
;; Two balls let roll down a 2 m limestone ramp at 15 degrees, from 1.5 m up
;; it: an iron ball 12 cm across and a lighter bronze one, 6 cm across.
;; Working it through (issue #52):
;;   rolling   a solid sphere has I = 2/5 m r^2, so as it goes down the slope
;;             a share 2/7 of the energy it loses goes into turning:
;;             a = g sin(theta) / (1 + 2/5) = (5/7) g sin(theta) = 0.7143 x
;;             9.81 x 0.2588 = 1.8136 m/s2, not the g sin(theta) = 2.539 of a
;;             block sliding without friction, and the same for both, whatever
;;             their size or mass.
;;   the run   1.5 m along the slope (the big ball) takes t = sqrt(2 s / a) = 1.286 s and
;;             it arrives at v = sqrt(2 a s) = 2.332 m/s = sqrt(10 g h / 7)
;;             for h = s sin(theta) = 0.388 m (a sliding block would have
;;             sqrt(2 g h) = 2.76).
;;   turning   it does not slip: w = v / r at every moment: 38.9 rad/s for the big
;;             ball at the foot of the ramp. The little bronze ball is started only
;;             0.5 m up (set when the engine capped any body at 47.1 rad/s, and a 3 cm
;;             ball rolling 1.5 m would want 58; since #187 the cap is 314): it
;;             arrives after sqrt(2 (0.5) / a) = 0.742 s at sqrt(2 (1.8136)(0.5)) =
;;             1.347 m/s, w = 44.9 rad/s.
;;   energy    at every point v^2 = (10/7) g (y0 - y), the rolling law that a
;;             sliding block would break with 2 g (y0 - y).
(require racket/math)
(define angle-deg 15)
(define a (degrees->radians angle-deg))
(define ramp-base-z (cm -40))
(define half-thickness (cm 2.5))                ; MachineView's ramp slab is 5 cm thick

;; Where a ball of radius r sits resting on the ramp, `along` metres up the slope and x to the side.
(define (on-ramp x along r)
  (define lift (+ half-thickness r (mm 1)))
  (list x
        (+ (* along (sin a)) (* lift (cos a)))
        (+ ramp-base-z (- (* along (cos a))) (* lift (sin a)))))

(define-values (x0 y0 z0) (apply values (on-ramp (cm -15) 1.5 (cm 6))))
(define-values (x1 y1 z1) (apply values (on-ramp (cm 15) 0.5 (cm 3))))

(define-machine ball-ramp
  #:source "Solid balls rolling down a slope at 5/7 of g sin(theta)"
  (ramp slope #:at (0 0 ramp-base-z) #:length (m 2) #:width (m 0.6) #:angle-deg angle-deg #:material limestone)
  (ball iron-ball #:at (x0 y0 z0) #:radius (cm 6) #:material iron)
  (ball bronze-ball #:at (x1 y1 z1) #:radius (cm 3) #:material bronze))
