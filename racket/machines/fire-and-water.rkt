#lang heroic
;; Water meets fire, two ways.
;;
;; 1. A drowned fire. A cistern on a post, kept brimming by a 20 g/s
;;    trickle, spills down a chute onto a 20 kW wood fire under a pot.
;;    Every kilogram of that water takes 4186 x 80 + 2.257e6 = 2.592 MJ to
;;    warm from 20 C and boil away, so the fire can boil off at most
;;    20000 / 2.592e6 = 7.72 g/s. The rest soaks in, 12.28 g/s, while the
;;    fuel burns down at 20000 / 15e6 = 1.33 g/s. The fire drowns when the
;;    soaked water outweighs the fuel left:
;;      q t - S - 7.72e-3 t = 2 - 1.33e-3 t,  S = the 38 g the cistern
;;      holds over its lip at 20 g/s,          t = 149.7 s.
;;    Until then every joule goes into boiling the water, none into the pot.
;;    Stoke the fire to 60 kW and it boils 23.1 g/s, more than arrives: it
;;    never drowns, and the pot gets (60000 - 0.02 x 2.592e6) x 0.3 = 2449 W.
;;
;; 2. Feed water. A copper holds 4 kg at 90 C over a 5 kW stove that
;;    delivers 2 kW. A feed tank empties 2 L of 20 C water down a chute
;;    into it. Mixed, (4 x 90 + 2 x 20) / 6 = 66.7 C. Back to boiling
;;    needs 6 x 4186 x 80 - 4 x 4186 x 70 = 837 kJ: 419 s at 2 kW, plus
;;    what the copper loses to the air meanwhile.
(define-machine fire-and-water
  #:source "Water poured on a fire, and fed to a boiler"
  ;; 1 ---- the drowned fire
  (post cistern-pier #:at ((m -1.2) 0 0) #:size ((cm 30) (m 1) (cm 30)) #:material limestone)
  (inflow trickle #:into cistern #:flow (L/s 0.02))
  (tank cistern #:at ((m -1.2) (m 1) 0) #:area 0.01 #:height (cm 30) #:water (L 1) #:material limestone
        (port lip #:height (cm 10)))
  (channel chute #:from cistern.lip #:to off #:end ((cm -30) (cm 30) 0) #:width (cm 5) #:onto campfire)
  (boiler pot #:at (0 (cm 25) 0) #:radius (cm 15) #:height (cm 20) #:water 3)
  (hearth campfire #:at (0 0 0) #:heats pot #:power (W 20000) #:fuel 2 #:fuel-kind wood #:efficiency 0.3)

  ;; 2 ---- feed water
  (post feed-pier #:at ((m 1.8) 0 0) #:size ((cm 30) (m 1) (cm 30)) #:material limestone)
  (tank feed-tank #:at ((m 1.8) (m 1) 0) #:area 0.02 #:height (cm 40) #:water (L 4) #:material limestone
        (port lip #:height (cm 10)))
  (channel feed #:from feed-tank.lip #:to off #:end ((m 3) (cm 80) 0) #:width (cm 10) #:onto copper)
  (boiler copper #:at ((m 3) (cm 25) 0) #:radius (cm 20) #:height (cm 40) #:water 4 #:temperature 90)
  (hearth stove #:at ((m 3) 0 0) #:heats copper #:power (W 5000) #:fuel 1 #:fuel-kind wood #:efficiency 0.4))
