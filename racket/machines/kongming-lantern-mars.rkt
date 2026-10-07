#lang heroic
;; The Kongming lantern of kongming-lantern.rkt on Mars (issues #111, #125):
;; the same 1 m3, 50 g paper envelope with a 20 g, 800 W burner, but in
;; 610 Pa of CO2 at -63 C. The air there is
;; 610 x 0.04349 / (8.314 x 210.15) = 0.0152 kg/m3 (80 times thinner than
;; Earth's), so even with every bit of air out of it the lift (rho_out - 0) g V
;; is the weight of 0.0152 kg, short of the lantern's 0.070 kg: the flame
;; heats the envelope (the thin air in it takes little heat, so it gets as
;; hot as its skin's 15 W/K lets it: -63 + 800/15 = -10 C) and the lantern
;; sits on the ground. A lantern needs air to float in.
(define-machine kongming-lantern-mars
  #:source "The Kongming lantern on Mars: 0.015 kg/m3 of air cannot lift 70 g from a cubic metre"
  #:planet mars
  (envelope lantern #:at (0 0 0) #:volume 1 #:envelope-mass (g 50) #:burner-mass (g 20)
            #:burner-power (W 800) #:fuel (g 10) #:skin-conductance 15
            #:height (m 1.2) #:drag-coefficient 0.8 #:material hemp))
