#lang heroic
;; A placer miner's sluice box (issue #53). A 2 L/s spring fills a head pool
;; whose race runs 5 m down a 1-in-100 fall and off the scene; a riffled
;; box on the race is fed 0.1 kg/s of crushed ore, grains 0.5 mm across,
;; 2% of them gold (19300 kg/m³), the rest quartz sand (2650).
;;
;; Worked out beforehand. Once the pool has risen to pass the spring's
;; 2 L/s, the race runs at its Manning depth for that flow on a 30 cm bed
;; falling 1 in 100: 16.6 mm, hydraulic radius R = 14.9 mm, and drags on
;; its floor with tau = rho g R S = 9810 x 0.0149 x 0.01 = 1.46 Pa. A grain
;; stays behind a riffle if the flow can't lift it, Shields number
;; tau / ((rho_s - rho) g d) under 0.047: every grain denser than
;; 1000 + 1.46 / (0.047 x 9.81 x 0.0005) = 7340 kg/m³. So the box keeps all
;; the gold (19300) and washes all the sand (2650) on: 2 g of gold a second.
(define-machine placer-sluice
  #:source "a placer miner's sluice box and riffles"
  (inflow spring #:into head-pool #:flow (L/s 2))
  (post pier #:at (0 0 0) #:size ((cm 70) (cm 50) (cm 70)) #:material limestone)
  (tank head-pool #:at (0 (cm 50) 0) #:area 0.4 #:height (cm 40) #:water (L 20) #:material limestone
        (port race #:height (cm 5)))
  (channel race #:from head-pool.race #:to off #:end ((m 5.3) (cm 50) 0) #:length (m 5) #:width (cm 30))
  (sluice-box riffles #:on race #:at ((m 2.5) (cm 52) 0) #:feed 0.1 #:grain (mm 0.5)
              #:heavy-density 19300 #:heavy-fraction 0.02 #:light-density 2650))
