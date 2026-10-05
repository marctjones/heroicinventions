#lang heroic
;; A broken water butt (issue #90): 1 m² and 1.2 m tall, holding 1000 L,
;; with a stave stove in 2 cm above its floor, a 100 cm² hole. Stood on a
;; map (game/worlds/spill.world, the spill-slope plain), the jet lands on
;; the ground beside it and runs off downhill; on the plain floor it would
;; just be lost. Worked out beforehand, Torricelli as for tank-leaks:
;;   the head over the hole, u = sqrt(h), falls at 0.6 a sqrt(2g) / (2 A) =
;;   0.6 x 0.01 x 4.4294 / 2 = 0.013288 m^1/2 a second from sqrt(0.98) =
;;   0.98995, so at 30 s it is (0.98995 - 0.39865)² = 0.3496 m: the butt
;;   holds 20 + 349.6 = 369.6 L and the ground 1000 - 369.6 = 630.4 L.
;;   It is down to the hole at 0.98995 / 0.013288 = 74.5 s, with 20 L left
;;   in the butt and 980 L on the ground.
;; Whatever the time, the butt and the ground between them hold the 1000 L
;; there were: the sand soaks up nothing and the plain is walled.
(define-machine spill-tank
  #:source "a broken water butt on a slope"
  (tank butt #:at (0 0 0) #:area 1 #:height (m 1.2) #:water (L 1000) #:material oak)
  (leak stove-in #:on butt #:height (cm 2) #:area (cm2 100)))
