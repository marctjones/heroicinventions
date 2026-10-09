#lang heroic
;; The route's vault (issues #96, #211): a tight regolith vault dug round the found bank's crate, with 40 kg of basalt in a lidded bin, a
;; bimetal strip on the lid and a heliostat on the rock, as a machine of its own. The found bank (racket/machines/found-bank.rkt) is
;; another machine; its cells are in this vault's air because the crate's centre is inside the vault's box (WorldZones, #211), not
;; because the vault was written round them. It is the first machine of lonely-rover-e2e.world, so its wakes are the ones a sleep takes.
;;   vault   a 0.5 m cavity (A = 1.5 m2) in a 0.5 m regolith wall at the ground's -55 C (night-heat's tight)
;;   rock    40 kg of basalt, frozen at -55 C at the start, in a bin that leaks 0.1 W/K shut
;;   strip   a brass-steel bimetal (bimetal-night's) on the lid, sensing the vault's air: a strip on another machine's cells is not a part
;;           this machine can have, and the air round them is what the cells follow (their 16 kJ/K through about 0.8 W/K: tau 5.6 h)
;;   mirror  a 4 m2 heliostat on the rock (the design doc's), all afternoon, the lid open while the vault is cold (the doc has the rover
;;           push rock heated in the open into the bin: GAP 4 of docs/e2e-route.md)
;; The clock is the found bank's and the opening's: no sun is given, so both start at the default noon (latitude 31.2, day 172), and the
;; bank's call is its own 03:00 (it has no weather), 15 local hours of 3,699 s on: 55,484 s. pre-dawn wakes 11 minutes before it.
;; The sun sets at about 17:00 on that day, so the mirror has the rock for 5 hours (the stand-in of #96 had 11, from 07:00 at latitude -2).
;; Checked in the C# sim with the cells written into this vault (the network the world's zone makes: the same answer, -27.76 C against the
;; game's -27.76 at 1.5 m2) before the area was chosen, the cells at 02:49 against the mirror's area:
;;   1.5 m2  rock peak  54 C, cells -27.8 C (fails: the 1.5 m2 of the stand-in was for 11 hours of sun)
;;   3 m2    rock peak 148 C, cells  +8.8 C
;;   4 m2    rock peak 224 C, cells +24.5 C, the lid 37% open (the doc's "rock heated to about 200 C")
(define-machine route-vault
  #:source "issues #96 and #211: the route's vault, rock, strip and heliostat round the found bank's crate, a machine of its own beside it"
  #:planet mars
  #:weather (weather #:passes '(3 15) #:pass-minutes 10)
  (enclosure vault #:at (0 0 0) #:size ((m 0.5) (m 0.5) (m 0.5)) #:pressure 610 #:temperature -55
             #:wall regolith #:wall-thickness 0.5 #:ground -55)
  (heat-store rock #:at ((m 0.12) (cm 25) 0) #:mass 40 #:contents basalt #:temperature -55)
  (heat-bin bin #:at ((m 0.12) (cm 25) 0) #:holds rock #:leak 0.1)
  (bimetal strip #:at ((m -0.19) (m 0.46) 0) #:senses vault #:drives bin #:layers (brass steel))
  (mirror heliostat #:at (0 (m 1.5) (m -3)) #:area 4 #:onto rock)
  (wake pre-dawn #:when ((scene elapsed above 54800)) #:limit 60000))
