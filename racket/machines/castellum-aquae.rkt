#lang heroic
;; A Roman castellum aquae, as Vitruvius describes it (De architectura VIII.6):
;; the aqueduct ends in a distribution tank, and from it three sets of pipes
;; leave at three different heights -- public fountains lowest, baths in the
;; middle, private houses highest. In a drought the level in the tank falls
;; below the highest pipes first, so the houses go dry while the fountains
;; and baths keep running: the town's water is rationed by plumbing alone.
;;
;; The supply is 6.7 L/s, less than the 9+ L/s all three pipes could carry.
;; The tank (0.4 m2, base 4 m) starts brimming at 1.1 m with everything
;; running. The pipes are linear (flow = C x head difference, C = 1.0, 0.8
;; and 0.5 L/s per m), each ending at the top of a 1 m receiving tank.
;; Integrated by hand (see the behaviour test), the level falls, passes the
;; houses' pipe at 0.9 m (which has delivered about 64 L by then), and settles
;; where fountains and baths alone carry the whole supply:
;;   (1.0 + 0.8) x 10^-3 x (4 + S - 1) = 6.7 x 10^-3  =>  S = 0.722 m
;; with the fountains at 3.72 L/s, the baths at 2.98 L/s and the houses at 0.
(define-machine castellum-aquae
  #:source "Vitruvius' castellum aquae, De architectura VIII.6"
  (post header-pier #:at (0 0 0) #:size ((cm 70) (cm 550) (cm 70)) #:material limestone)
  (post arch-a #:at ((m 5) 0 0) #:size ((cm 60) (cm 550) (cm 60)) #:material limestone)
  (post arch-b #:at ((m 10) 0 0) #:size ((cm 60) (cm 535) (cm 60)) #:material limestone)
  (post arch-c #:at ((m 15) 0 0) #:size ((cm 60) (cm 520) (cm 60)) #:material limestone)
  (post castellum-pier #:at ((m 20) 0 0) #:size ((cm 90) (m 4) (cm 90)) #:material limestone)
  (inflow spring #:into head-tank #:flow (L/s 6.7))
  (tank head-tank #:at (0 (cm 550) 0) #:area 0.5 #:height (cm 60) #:water (L 177.8)
        (port spill #:height (cm 30)))
  (tank castellum #:at ((m 20) (m 4) 0) #:area 0.4 #:height (cm 120) #:water (L 440)
        (port inlet #:height (cm 115))
        (port fountains-out #:height (cm 10))
        (port baths-out #:height (cm 50))
        (port houses-out #:height (cm 90)))
  (channel aqueduct #:from head-tank.spill #:to castellum.inlet #:width (cm 30))
  (tank fountains #:at ((m 24) 0 (m -3)) #:area 4 #:height (m 1)
        (port inlet #:height (m 1)))
  (tank baths #:at ((m 24) 0 0) #:area 4 #:height (m 1)
        (port inlet #:height (m 1)))
  (tank houses #:at ((m 24) 0 (m 3)) #:area 4 #:height (m 1)
        (port inlet #:height (m 1)))
  (pipe to-fountains castellum.fountains-out fountains.inlet #:conductance 1.0e-3)
  (pipe to-baths castellum.baths-out baths.inlet #:conductance 0.8e-3)
  (pipe to-houses castellum.houses-out houses.inlet #:conductance 0.5e-3))
