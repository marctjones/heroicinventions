#lang heroic
;; Grain against water as a clock (issue #50). Three hoppers of sand of 1600
;; kg/m3 in 0.3 mm grains, each 10 x 10 cm (0.01 m2), 5 kg deep (31.25 cm),
;; beside a water tank of the same footing.
;;   sand    through a 10 mm orifice: Beverloo's law, W = C rho sqrt(g)
;;           (D - k d)^(5/2) = 0.58 x 1600 x 3.132 x (10 - 0.45 mm)^(5/2) =
;;           25.9 g/s, whatever the depth. The level falls linearly, at
;;           W / (rho A) = 1.619 mm/s, and the 5 kg are gone in 5 / 0.0259 =
;;           193.0 s. Half way through, at 96.5 s, half the sand is left: 50%.
;;   water   the tank starts at the same 31.25 cm and its floor-level hole is
;;           sized (2.18e-5 m2, 5.3 mm across) to empty it in those same
;;           193 s. Torricelli, though, slows as the head falls: the level is
;;           (sqrt(h0) - k t)^2, so at half the time only a quarter of the
;;           water is left: 25%, against the sand's 50%. A water clock
;;           has to be read on a scale with the top spaced widest.
;;   mars    the flow goes as sqrt(g): on Mars, in 3.71 m/s2, the sand runs at
;;           sqrt(3.71/9.81) = 0.615 of the rate, 15.9 g/s, and every step of
;;           a program takes 1.63 times as long: 313.9 s to empty.
;;   arch    the third hopper's orifice is 8 mm and its grains 2 mm: 4 grains
;;           across, under the 5 at which grain arches over and stops. It
;;           gives no grain at all.
(define-machine sand-timer
  #:source "Heron's grain clock against a water clock"
  (hopper sand #:at (0 (m 1) 0) #:area 0.01 #:grain 5 #:orifice (mm 10) #:grain-size (mm 0.3))
  (tank water #:at ((m 0.6) (m 0.7) 0) #:area 0.01 #:height (m 0.4) #:water (L 3.125) #:material oak)
  (leak drain #:on water #:height 0 #:area 2.18e-5)
  (hopper arch #:at ((m 1.2) (m 1) 0) #:area 0.01 #:grain 5 #:orifice (mm 8) #:grain-size (mm 2)))
