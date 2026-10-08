#lang heroic
;; Two peg wheels working trip-hammers (issue #48): a wheel with four pegs,
;; each lifting a 5 kg hammer 10 cm over half a pitch (45 degrees of the
;; wheel) on a cosine, then letting it fall on its anvil. Both wheels are
;; turned by a motor; they differ in how much torque it can give (the strong
;; one 20 N.m at 30 rpm, the weak one 5 N.m, turning slowly at 2 rpm so that
;; it stalls rather than swings on: a motor set to a low speed pulls back
;; against any overshoot, which is what settles a stall). Working it through:
;;   strikes  n = 4 a turn; at 30 rpm the strong wheel turns twice in 4 s: 8
;;            strikes in 4 s, one every 0.5 s.
;;   fall     the hammer drops h = 0.1 m, at sqrt(2 g h) = 1.401 m/s.
;;   work     lifting the hammer costs m g h = 4.905 J a peg, 19.62 J a turn,
;;            so the wheel's mean torque is 19.62 / 2 pi = 3.12 N.m.
;;   peak     the follower's weight is m g = 49.05 N and the lift's steepest
;;            slope is pi h / (2 alpha) = 0.2 m/rad (alpha = pi/4), so holding
;;            it up there takes 9.81 N.m; moving at 30 rpm the wheel must also
;;            accelerate it, and the peak is 11.9 N.m early in the rise: still
;;            under the strong wheel's 20 N.m.
;;   stall    the weak wheel gives 5 N.m, less than that. Its motor could lift the
;;            hammer only 5 x pi/4 = 3.9 J of the 4.9 J a peg needs, so it climbs
;;            the ramp until the follower's weight matches what the wheel gives,
;;            m g y'(phi) = 5, sin(pi phi / alpha) = 5 / 9.81, phi = 7.7 degrees
;;            in, and no further (the pull and push settle it there): the hammer
;;            stands 0.70 cm up, never falls, and never strikes.
;; The two rigs stand side by side, 1 m apart along x (#192). One behind the other, their wheels shared an axle line,
;; so they were drawn on one axle under one stacked label; moved along x, every body's motion is the same to the bit.
(define-machine trip-hammer
  #:source "A trip-hammer: pegs on a wheel lift it, a weaker wheel stalls"
  (wheel strong-wheel #:shape (pulley #:radius (cm 15) #:width (cm 7.5)) #:at (0 (m 1) 0) #:material oak
         #:drive-rpm 30 #:drive-torque 20)
  (cam strong-hammer #:at ((cm 40) (cm 30) 0) #:on strong-wheel #:pegs 4 #:lift (cm 10) #:rise 0.5 #:mass 5)
  (wheel weak-wheel #:shape (pulley #:radius (cm 15) #:width (cm 7.5)) #:at ((m 1) (m 1) 0) #:material oak
         #:drive-rpm 2 #:drive-torque 5)
  (cam weak-hammer #:at ((cm 140) (cm 30) 0) #:on weak-wheel #:pegs 4 #:lift (cm 10) #:rise 0.5 #:mass 5))
