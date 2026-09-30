#lang heroic
;; A solar furnace on Mars (issue #56): glass from sand and sunlight, at
;; Meridiani Planum at noon. A target under concentrated sunlight heats
;; until it re-radiates what it receives: its hot face stops where
;; σ(T⁴ − T_air⁴) = C·I, the flux on it. The sun at noon here (80.7° up,
;; air mass 1.013) sends 586.2 x 0.741^1.013 = 432.7 W/m² through the clear
;; Mars sky; the sun is held still at noon for these numbers.
;;
;; flat     ten flat 1 m² heliostats in a ring, all on one 1 m² crucible of
;;          basalt: a flat mirror's image is as big as itself, so they pile up
;;          at best C = 10, and less by each one's cos(θ/2) (these stand low,
;;          about 0.64): a flux near 2.4 kW/m² stops it near 190 °C,
;;          (flux/σ + T_air⁴)^¼. Flat mirrors can't melt basalt (1,200 °C).
;; focused  a 12 m² burning mirror gathering its light into a 50 cm² spot on
;;          10 kg of basalt: 432.7 x 12 x 0.85 = 4,413 W, 883 kW/m², C ≈ 2,000.
;;          It would stop at 1,713 °C, well past basalt's melting point, and
;;          melts the charge: 10 kg from -63 °C, 12.63 MJ to 1,200 °C and
;;          4 MJ of fusion, 16.63 MJ in all, absorbing 0.9 of what lands and
;;          losing what its hot face radiates: 4,895 s, 82 minutes, by a
;;          numerical integration of m c dT/dt = ε(P − σA(T⁴ − T_air⁴)).
;;          Dark glass: 5% of the sun through.
;; clear    a 16 m² burning mirror on 2 kg of silica sand in a 40 cm² spot:
;;          5,885 W, 1.47 MW/m², would stop at 1,984 °C; fused silica needs
;;          1,700 °C and 0.14 MJ/kg: melted in 930 s by the same integration.
;;          Clear glass: 90% of the sun through.
(define-machine solar-furnace
  #:source "A solar furnace: heliostats, burning mirrors, and glass from Mars sand"
  #:planet mars
  #:latitude -2 #:day 100 #:time 12
  (post pedestal #:at (0 0 0) #:size ((m 0.8) (m 1) (m 0.8)) #:material granite)
  (crucible flat #:at (0 (m 1) 0) #:sand basalt #:charge 10 #:spot (m2 1))
  (mirror h1 #:at ((m 4) (m 1.5) (m 0)) #:area 1 #:onto flat)
  (mirror h2 #:at ((m 3.24) (m 1.5) (m 2.35)) #:area 1 #:onto flat)
  (mirror h3 #:at ((m 1.24) (m 1.5) (m 3.8)) #:area 1 #:onto flat)
  (mirror h4 #:at ((m -1.24) (m 1.5) (m 3.8)) #:area 1 #:onto flat)
  (mirror h5 #:at ((m -3.24) (m 1.5) (m 2.35)) #:area 1 #:onto flat)
  (mirror h6 #:at ((m -4) (m 1.5) (m 0)) #:area 1 #:onto flat)
  (mirror h7 #:at ((m -3.24) (m 1.5) (m -2.35)) #:area 1 #:onto flat)
  (mirror h8 #:at ((m -1.24) (m 1.5) (m -3.8)) #:area 1 #:onto flat)
  (mirror h9 #:at ((m 1.24) (m 1.5) (m -3.8)) #:area 1 #:onto flat)
  (mirror h10 #:at ((m 3.24) (m 1.5) (m -2.35)) #:area 1 #:onto flat)
  (post stand #:at ((m 10) 0 0) #:size ((m 0.5) (m 1) (m 0.5)) #:material granite)
  (crucible focused #:at ((m 10) (m 1) 0) #:sand basalt #:charge 10 #:spot (m2 0.005))
  (burning-mirror dish #:at ((m 10) (m 3.5) (m -2)) #:area 12 #:image (m2 0.005) #:onto focused)
  (post stand2 #:at ((m 16) 0 0) #:size ((m 0.5) (m 1) (m 0.5)) #:material granite)
  (crucible clear #:at ((m 16) (m 1) 0) #:sand silica #:charge 2 #:spot (m2 0.004))
  (burning-mirror dish2 #:at ((m 16) (m 3.5) (m -2)) #:area 16 #:image (m2 0.004) #:onto clear))
