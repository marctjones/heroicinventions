#lang heroic
;; What a boiler holds is its shell's to say (#139). Three identical pots, 30 cm
;; across, 3 mm walls, each 10 kg of 20 C water over the same wood fire that
;; hands the water 10 kW, each losing 2 W/K to the air. Only the metal differs,
;; and no pot is given a rating: the table's tensile strength decides.
;; By hand:
;;   shell   thin-wall hoop stress sigma = P r / t, so a pot bursts at
;;           P = sigma_t t / r = sigma_t * 0.003 / 0.15 = sigma_t / 50:
;;             lead   12 MPa  -> 0.24 MPa gauge
;;             copper 220 MPa -> 4.4  MPa
;;             bronze 350 MPa -> 7.0  MPa
;;           (no derating: lead melts at 327.5 C, so softens from 164 C;
;;            copper 1085 C from 543 C; bronze 950 C from 475 C; all hotter
;;            than any of these bursts)
;;   boiling sealed, water boils at P when the pot is at the Antoine
;;           temperature for 101.325 kPa + P: 138.22, 256.92, 285.999 C
;;   warming sealed, M c dT/dt = Q - h (T - 20), tau = M c / h = 20930 s:
;;             t = tau ln(5000 / (5000 - (T - 20)))
;;             lead 500.8 s, copper 1016.0 s, bronze 1144.2 s
;; So the lead pot bursts at 138.2 C of steam, 3.4 bar absolute, in under nine
;; minutes; the others are still sound at 900 s and give way a quarter hour on.
;; Each pot's outline warms to amber, then red, as its pressure goes from 60% to
;; 100% of what it holds. Thinner walls (a 0.5 mm bronze holds 1.17 MPa) are the
;; same rule: try #:wall.
(define-machine boiler-shells
  #:source "The shell sets the rating: a lead pot bursts at 0.24 MPa, copper at 4.4, bronze at 7.0"
  (boiler lead-pot #:at (0 (cm 25) 0) #:radius (cm 15) #:height (cm 30) #:water 10
          #:wall (mm 3) #:material lead)
  (hearth lead-fire #:at (0 0 0) #:heats lead-pot #:power (kW 20) #:fuel 3 #:fuel-kind wood #:efficiency 0.5)

  (boiler copper-pot #:at ((m 1.5) (cm 25) 0) #:radius (cm 15) #:height (cm 30) #:water 10
          #:wall (mm 3) #:material copper)
  (hearth copper-fire #:at ((m 1.5) 0 0) #:heats copper-pot #:power (kW 20) #:fuel 3 #:fuel-kind wood #:efficiency 0.5)

  (boiler bronze-pot #:at ((m 3) (cm 25) 0) #:radius (cm 15) #:height (cm 30) #:water 10
          #:wall (mm 3) #:material bronze)
  (hearth bronze-fire #:at ((m 3) 0 0) #:heats bronze-pot #:power (kW 20) #:fuel 3 #:fuel-kind wood #:efficiency 0.5))
