#lang heroic
;; Heron's temple doors (Pneumatica I.38). A fire lit on a hollow bronze
;; altar heats the air sealed inside it. That air is shared, through a
;; tube, with a closed globe half full of water, so as it warms it presses
;; on the water and drives it through a siphon into a bucket hanging on a
;; rope. The rope is wound round the doors' spindle, against a counterweight
;; wound the other way: once the bucket and its water outweigh the
;; counterweight it sinks, and the doors swing open "of themselves". Let
;; the fire die, the air cools, the water siphons back, and the counterweight
;; shuts them.
;;
;; Working it through:
;;   heat    the fire puts 150 W x 0.6 = 90 W into the altar; its walls lose
;;           3 W/K, so it settles 30 K warm, at 50 C, with time constant
;;           (1900 J/K of bronze + 0.11 kg of air x 718) / 3 = 660 s.
;;   air     95.5 L sealed at 20 C, 1 atm. Heated at fixed volume P rises as
;;           T: lifting the globe's water 0.40 m to the empty bucket's
;;           bottom takes 3.92 kPa, a 3.9% rise, 11.3 K.
;;   bucket  the doors start to open once it holds 2 kg (counterweight 4 kg,
;;           bucket 2 kg). At 50 C the water settles where the air pressure
;;           m R T / (V0 + dV) equals the water it holds up: about 4.9 L,
;;           the doors wide open and the bucket let down 7.9 cm.
;;   out     the 30 g of wood burns 3000 s; then the altar cools, the air
;;           shrinks, the water runs back and the doors shut.
(define-machine heron-temple-doors
  #:source "Hero of Alexandria, Pneumatica I.38"
  (post altar-base #:at (0 0 0) #:size ((cm 55) (cm 80) (cm 55)) #:material limestone)
  (tank altar #:at (0 (cm 80) 0) #:area 0.25 #:height (cm 30) #:material bronze)
  (hearth offering #:at (0 (m 1.1) 0) #:heats altar #:power (W 150) #:fuel (g 30) #:fuel-kind wood #:efficiency 0.6)
  (tank globe #:at ((cm 90) 0 0) #:area 0.1 #:height (cm 40) #:water (L 20) #:material bronze
        (port outlet #:height 0))
  (sealed-air (altar globe) #:tube (L 0.5) #:heat-loss 3 #:heat-capacity 1900)
  (tank bucket #:at ((m 1.8) (cm 60) 0) #:area 0.04 #:height (cm 40) #:material bronze
        (port inlet #:height 0))
  (pipe siphon globe.outlet bucket.inlet #:conductance 2e-4)
  (counterpoise doors #:at ((m 1.8) (m 1.4) (m -1)) #:vessel bucket #:vessel-mass 2 #:counterweight 4
                #:radius (cm 5) #:turn-deg 90 #:leaf ((cm 60) (m 1.6)) #:leaf-inertia 3))
