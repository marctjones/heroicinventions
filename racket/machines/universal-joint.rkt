#lang heroic
;; A universal (Cardan, Hooke's) joint: two shafts meeting at 30 degrees,
;; joined by an iron cross whose arms are hinged one to each shaft's yoke.
;; The driving shaft turns steadily at 30 rpm; the driven one does not.
;; Twice a turn it runs slow, cos 30 = 0.866 of the driver's speed, and
;; twice fast, 1 / cos 30 = 1.155 of it:
;;   w_out / w_in = cos b / (1 - sin^2 b sin^2 theta)
;; with theta the driver's turn from the start, where the cross's first arm
;; stands square to the plane of the two shafts. Turned, the driven shaft
;; has gone tan(out) = cos b tan(theta): 90 degrees each quarter turn, but
;; lagging between, most (4.1 degrees) at 47 degrees; at 45 it has turned
;; 40.9. The label over the cross gives the ratio as it turns. That is why
;; a car's drive shaft has two, phased to cancel.
(require racket/math)
(define centre-y (m 1.2))
(define b (degrees->radians 30))
(define reach (m 0.4))                   ; each shaft's middle from the cross

(define-machine universal-joint
  #:source "Gerolamo Cardano (1545); Robert Hooke (1676)"
  (wheel driver #:shape (drum #:radius (cm 3) #:length (cm 70)) #:at ((- reach) centre-y 0) #:axis x
         #:material iron #:drive-rpm 30)
  (wheel driven #:shape (drum #:radius (cm 3) #:length (cm 70))
         #:at ((* reach (cos b)) (+ centre-y (* reach (sin b))) 0) #:axis x #:tilt-deg 30 #:material iron)
  (joint cross #:kind universal #:a driver #:b driven #:at (0 centre-y 0)))
