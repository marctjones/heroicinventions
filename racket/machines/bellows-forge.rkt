#lang heroic
;; Two forges, otherwise identical: 1 kg of wood at 5 kW under a small
;; crucible. The left burns on its own natural draught — the air its
;; 5 kW / 15 MJ/kg burn rate already draws, at wood's 6:1 air-fuel ratio:
;; (5000 / 15e6) x 6 = 2 g/s — and takes 3000 s to burn the kilogram out.
;; The right has a bellows forcing in 5 L/s of air (density 1.204 kg/m3,
;; so 6.02 g/s) on top of that: nearly five times the natural draught
;; alone, so it burns nearly five times as fast, out in a few hundred
;; seconds — but gives up no more heat in all, still 1 kg x 15 MJ/kg,
;; however fast it is burned.
;;
;; The crucibles are 3 mm bronze shells (#148, #139): thin-wall hoop stress
;; sigma = P r / t, so each holds sigma_t t / r = 350e6 x 0.003 / 0.07 = 15.0 MPa
;; gauge, which is steam at 342.4 C (Antoine). They are sealed, with 50 g of
;; water, so the limit matters: before it they were unrated and the sealed
;; steam climbed past any shell (1.2 GPa bare, 8 GPa blown); now the pots
;; give way, the bare at 31.2 s and the blown at 7.0 s, as each fire raises
;; the water to 342 C (0.05 x 4186 x 322 = 67.5 kJ: 27 s at the bare fire's
;; 2.5 kW to the water, 6.7 s at the blown fire's 10 kW; the rest is the steam's
;; latent heat). The fires burn on: the burn and energy numbers above are as before.
(define-machine bellows-forge
  #:source "a smith's two forges, one worked by a bellows"
  (post bare-post #:at (-0.3 0 0) #:size ((cm 30) (cm 20) (cm 30)) #:material limestone)
  (boiler bare-crucible #:at (-0.3 (cm 26) 0) #:radius (cm 7) #:height (cm 10) #:water (kg 0.05) #:wall (mm 3) #:material bronze)
  (hearth bare #:at (-0.3 (cm 20) 0) #:heats bare-crucible #:power (W 5000) #:fuel (kg 1) #:fuel-kind wood #:efficiency 0.5)

  (post blown-post #:at (0.3 0 0) #:size ((cm 30) (cm 20) (cm 30)) #:material limestone)
  (boiler blown-crucible #:at (0.3 (cm 26) 0) #:radius (cm 7) #:height (cm 10) #:water (kg 0.05) #:wall (mm 3) #:material bronze)
  (hearth blown #:at (0.3 (cm 20) 0) #:heats blown-crucible #:power (W 5000) #:fuel (kg 1) #:fuel-kind wood #:efficiency 0.5)
  (bellows pump #:at (0.3 (cm 20) (cm 40)) #:on blown #:airflow (L/s 5) #:material oak))
