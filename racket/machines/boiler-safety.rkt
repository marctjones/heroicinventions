#lang heroic
;; Papin's safety valve (1679), and what happens without one. Two identical
;; bronze boilers, each 10 kg of 20 C water rated to burst at 200 kPa gauge,
;; each over a 20 kW wood fire that hands the water half of it, Q = 10 kW;
;; each loses 2 W/K to the air. Nothing here sets a pressure. By hand:
;;   warming   sealed, M c dT/dt = Q - h (T - 20), so
;;             T(t) = 20 + (Q/h)(1 - exp(-t h / M c)): tau = 20930 s, and
;;             T reaches a pressure P when water boils at P (Antoine turned
;;             round): 100 kPa gauge at 120.54 C, 200 kPa at 133.89 C.
;;   guarded   a valve with an 8 mm bore lifts at 100 kPa, at t = 425.1 s.
;;             From then it vents what the fire brings and the air doesn't
;;             take, (Q - h (T - 20)) / L = 4.34 g/s of steam, and it has
;;             to be open about a third to pass it (a choked nozzle,
;;             12.9 g/s wide open, 10% over its lift): the pressure holds
;;             at 103.4 kPa, 121.07 C, for as long as the water lasts.
;;   unguarded no valve: the pressure climbs on to 200 kPa and the boiler
;;             bursts at t = 482.3 s. The water above 100 C flashes to
;;             steam at once, M c (133.89 - 100) / L = 0.629 kg of it, and
;;             throws the rest out with it.
;; Tie the guarded valve down (guard lift 300) and it bursts too.
(define-machine boiler-safety
  #:source "A boiler with Papin's safety valve holds its pressure; one without it bursts"
  (boiler guarded #:at (0 (cm 25) 0) #:radius (cm 15) #:height (cm 30) #:water 10
          #:burst (kPa 200) #:material bronze)
  (hearth guarded-fire #:at (0 0 0) #:heats guarded #:power (kW 20) #:fuel 3 #:fuel-kind wood #:efficiency 0.5)
  (safety-valve guard #:on guarded #:lift (kPa 100) #:bore (mm 8))

  (boiler unguarded #:at ((m 1.5) (cm 25) 0) #:radius (cm 15) #:height (cm 30) #:water 10
          #:burst (kPa 200) #:material bronze)
  (hearth unguarded-fire #:at ((m 1.5) 0 0) #:heats unguarded #:power (kW 20) #:fuel 3 #:fuel-kind wood #:efficiency 0.5))
