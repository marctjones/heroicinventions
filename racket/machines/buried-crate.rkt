#lang heroic
;; A crate buried a metre deep in loam, and a gang digging down to it
;; (issue #54). The crate (oak, 50 cm, 90 kg) lies with its lid 1 m under
;; the surface; the gang (1.5 kW) cuts a trench 2 m long and 1 m wide over
;; it, a 25 cm spit at a time.
;;
;; Worked out beforehand, loam with c = 5 kPa, tan φ = 0.55, γ = 1400 g =
;; 13734 N/m³, K0 = 1 - sin φ = 0.518: pulling the crate straight out takes
;; its weight, the soil on its lid and the soil's grip on its sides,
;;   m g + γ D s² + 4 s [c s + K0 γ tan φ ((D + s)² - D²) / 2]
;;   = 883 + 3433 + 2 x (2500 + 2446) = 14.2 kN under D = 1 m,
;; and 10.5 kN under 0.5 m. It is held until its cover is under a quarter of
;; its height, 12.5 cm: after the third spit it is still under 25 cm; the
;; fourth, which takes the trench to 1 m, bares its lid and frees it. That
;; takes 2 m³ at c + γ (1 + tan φ) D/2 = 15.6 kJ/m³, 31.3 kJ, 20.9 s at 1.5 kW.
(define-machine buried-crate
  #:source "a crate buried in the ground, and the gang digging it out"
  (block crate #:at (0 (m -1.25) 0) #:size (cm 50) #:material oak)
  (digger gang #:at (-1 0 0) #:length (m 2) #:width (m 1) #:depth (m 1) #:power 1500 #:spoil (m 4)))
