#lang heroic
;; A clepsydra: an outflow water clock, after the ones the Greeks and Egyptians
;; kept. The trouble with a plain draining vessel is that it runs slower as
;; it empties, because the head falls. The cure, used since Ctesibius, is a
;; constant-head reservoir: a spring keeps the upper tank brimming over a
;; lip, the surplus spilling away, so the water pushing through the outlet
;; never changes. The outlet fills a tall receiver whose level therefore
;; climbs at a steady rate, and the level is the time.
;;
;; The pipe enters the receiver at its very top, so its far-end head is
;; fixed too, and the flow is C x (reservoir surface - top of receiver).
;; By hand, with Q_in = 0.5 L/s, C = 1.67e-4 m3/s per m, a 20 cm wide lip and the
;; reservoir base at 1.5 m: the surface settles where the weir spill
;; (Q_in - Q_out = 1.705 b h^1.5) and the outlet flow agree, at 1.9108 m
;; (h = 1.08 cm over the lip), so Q_out = 0.1187 L/s, and the 0.04 m2
;; receiver rises 2.967 mm/s, about 1.78 cm a minute.
;;
;; Not modelled: a float and indicator rod. Nothing here makes a block
;; float on water, so the level is read off the receiver's water itself.
(define-machine water-clock
  #:source "Ctesibius' constant-head clepsydra"
  (post leg-a #:at ((cm -19) 0 (cm -19)) #:size ((cm 12) (cm 150) (cm 12)) #:material oak)
  (post leg-b #:at ((cm 19) 0 (cm -19)) #:size ((cm 12) (cm 150) (cm 12)) #:material oak)
  (post leg-c #:at ((cm -19) 0 (cm 19)) #:size ((cm 12) (cm 150) (cm 12)) #:material oak)
  (post leg-d #:at ((cm 19) 0 (cm 19)) #:size ((cm 12) (cm 150) (cm 12)) #:material oak)
  (inflow spring #:into reservoir #:flow (L/s 0.5))
  (tank reservoir #:at (0 (cm 150) 0) #:area 0.25 #:height (cm 60) #:water (L 102.7)
        (port outlet #:height 0)
        (port spill #:height (cm 40)))
  (channel overflow #:from reservoir.spill #:to off #:end ((m -1.6) (cm 160) 0) #:width (cm 20))
  (tank receiver #:at ((m 1) 0 0) #:area 0.04 #:height (cm 120) #:water 0
        (port inlet #:height (cm 120)))
  (pipe outflow reservoir.outlet receiver.inlet #:conductance 1.67e-4))
