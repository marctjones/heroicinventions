#lang heroic
;; A kitchen smoke jack (Leonardo sketched one around 1480; by the 1600s
;; they turned roasting spits across Europe). A cooking fire heats a
;; cauldron; what the cauldron doesn't take goes up the chimney, and the hot
;; air rising through a wheel of pitched vanes turns it, and through gearing
;; the spit.
;;
;; Working it through (checked against the simulation):
;;   heat    a 3 kW fire, half into the cauldron: 1.5 kW goes up the flue.
;;   draught a 2 m column of warm air in a 0.05 m2 flue rises at
;;           v = 0.7 sqrt(2 g H dT / T) while the flow is warmed by
;;           dT = Q / (mdot cp). Solved together: 22.7 K warmer, rising at
;;           1.18 m/s, 66 g of air a second (66 g/s x 1005 x 22.7 = 1.5 kW).
;;   speed   the vanes run up until the air's push, mdot (v - w r) = 45 mN,
;;           times r balances the spit's 3 mN.m, 2 mN.m of bearing and the
;;           vanes' windage: 39 rpm, 0.012 W. Slow and weak, but steady for
;;           as long as the fire burns, and free: it runs on heat the
;;           cauldron was going to waste anyway.
(define plinth-height (cm 30))
(define-machine kitchen-smoke-jack
  #:source "Leonardo da Vinci, Codex Atlanticus (smoke jack, c. 1480)"
  (post hearthstone #:at (0 0 0) #:size ((cm 60) plinth-height (cm 60)) #:material limestone)
  (hearth fire #:at (0 plinth-height 0) #:heats cauldron #:power (W 3000) #:fuel (kg 5) #:fuel-kind wood #:efficiency 0.5)
  (boiler cauldron #:at (0 (+ plinth-height (cm 8)) 0) #:radius (cm 15) #:height (cm 20)
          #:water (kg 3) #:fire 0 #:temperature 20 #:material iron)
  (smokejack jack #:at (0 (+ plinth-height (m 1.2)) 0) #:over fire #:radius (cm 12) #:material iron
             #:vanes 6 #:width (cm 6) #:mass (kg 0.3) #:load 0.003
             #:chimney-height (m 2) #:chimney-area 0.05))
