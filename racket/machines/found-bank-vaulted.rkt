#lang heroic
;; The found bank with a vault round its cells (issue #96): found-bank's crate, cells and bank, exactly, plus the route's stand-ins
;; (docs/e2e-route.md GAPs 2 and 4) that keep the bank warm through a sol: a tight regolith vault, a lidded bin of rock heated in place,
;; a bimetal strip on the lid, and a heliostat on the rock. It is the first machine of lonely-rover-e2e.world, so that its wakes and its
;; clock (07:00 on sol 1, latitude -2, day 100: bimetal-night's sun) are the world's.
;;
;; Why a machine of its own and not a vault beside found-bank: an enclosure contains a part by the part's position inside the
;; enclosure's own machine (MachineRuntime.BuildEnclosures). A vault in another machine, or a crate moved into it, does not touch the
;; found bank's cells; they stay at the air's temperature. So the vault is built round the bank's own store, in the bank's machine.
;;   vault   a 0.5 m cavity (A = 1.5 m2) in a 0.5 m regolith wall at the ground's -55 C, holding the bank's cells (16 kg, 16 kJ/K)
;;           and 40 kg of basalt in a lidded bin that leaks 0.1 W/K
;;   strip   a bimetal on the cells working the bin's lid
;;   rock    heated by a 1.5 m2 heliostat all day with the lid open (the doc has the rover push rock heated in the open into the bin:
;;           GAP 4)
;; The bank is found-bank's: 5,000 Wh before the scenario's multiplier (the e2e world scales it to 25 Wh, #60), charged by a wire.
(define-machine found-bank-vaulted
  #:source "issue #96: the found bank with the route's vault, rock and heliostat as stand-ins, the machine whose wakes the route sleeps on"
  #:planet mars
  #:latitude -2 #:day 100 #:time 7
  #:weather (weather #:passes '(3 15) #:pass-minutes 10)
  (block crate #:at (0 (cm 25) 0) #:size (cm 50) #:material oak)
  (enclosure vault #:at (0 0 0) #:size ((m 0.5) (m 0.5) (m 0.5)) #:pressure 610 #:temperature -55
             #:wall regolith #:wall-thickness 0.5 #:ground -55)
  (heat-store cells #:at (0 (cm 25) 0) #:mass 16 #:contents cells #:temperature -55)
  (battery-bank bank #:at (0 (cm 25) 0) #:in cells #:on crate #:capacity 5000 #:charge 0)
  (heat-store rock #:at ((m 0.12) (cm 25) 0) #:mass 40 #:contents basalt #:temperature -55)
  (heat-bin bin #:at ((m 0.12) (cm 25) 0) #:holds rock #:leak 0.1)
  (bimetal strip #:at ((m -0.19) (m 0.46) 0) #:senses cells #:drives bin #:layers (brass steel))
  (mirror heliostat #:at (0 (m 1.5) (m -3)) #:area 1.5 #:onto rock)
  (wake pre-dawn #:when ((scene elapsed above 73300)) #:limit 80000)
  (wake call #:when ((bank won above 0.5)) #:limit 120000))
