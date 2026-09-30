#lang heroic
;; A rain-house on Mars (issue #58): a sealed room with a warm pond on its
;; floor and a roof chilled by the -63 °C outside. The pond's water
;; evaporates into the room's air; the vapour condenses on the cold roof and
;; runs into a gutter 4.6 m up, lifting water with no pump. Worked out before
;; running:
;;
;; roof     condensing, its inside stands at the air's dew point, and the heat
;;          leaving through it, U (T_dew - T_out), is the latent heat of what
;;          condenses. The pond's 1 kW heater is all the heat there is, so at
;;          steady state 1 kW leaves through the roof: its 40 W/K puts the dew
;;          point at -63 + 1000/40 = -38 °C, and it rains
;;          1000 / 2.257e6 J/kg = 0.4431 g/s, 1.595 kg an hour.
;; pond     200 L, 1 m² of surface, evaporating at k A (p_sat(T_w) - p_v),
;;          k = 3.6e-8 kg/(s m² Pa). To carry off its 1 kW it needs
;;          p_sat(T_w) = 21.9 Pa (the vapour at -38 °C's dew point) +
;;          4.431e-4 / 3.6e-8 = 12,329 Pa: 50.05 °C. It starts at 50 °C, so it
;;          is there from the first.
;; gutter   in a sol (88,775 s) the roof fills it with 88,775 x 4.431e-4 =
;;          39.33 kg, lifted 4.6 m: m g h = 39.33 x 3.71 x 4.6 = 671 J stored,
;;          against 88.8 MJ of heat spent. A slow pump: its worth is the water.
(define-machine rain-house
  #:source "A rain-house: evaporation and condensation in a sealed room on Mars"
  #:planet mars
  (enclosure house #:at (0 0 0) #:size ((m 4) (m 5) (m 4))
             #:pressure 13500 #:air '((o2 0.21) (n2 0.79)) #:temperature 20 #:insulation 0 #:material glass)
  (tank basin #:at ((m -1) 0 0) #:area (m2 1) #:height (m 0.5) #:water (L 200) #:material limestone)
  (pond warm #:at ((m -1) (m 0.2) 0) #:on basin #:heater 1000 #:temperature 50)
  (post pier #:at ((m 1.2) 0 0) #:size ((m 0.4) (m 4.6) (m 0.4)) #:material limestone)
  (tank gutter #:at ((m 1.2) (m 4.6) 0) #:area (m2 0.25) #:height (m 0.4) #:material oak)
  (roof lid #:at (0 (m 5) 0) #:on house #:conductance 40 #:gutter gutter))
