#lang heroic
;; A float valve and the constant head it keeps: Ctesibius' and Philo's
;; answer to a water clock that runs slow as it empties. Two identical
;; cisterns on stone piers, each 0.25 m2, both starting 40 cm full, each
;; drawn from its floor through a tap (a bronze shutter 50 cm tall over a
;; 5 cm wide spout, raised 1 cm) into a receiver below. Only the first is
;; fed: a 2 L/s aqueduct pours in through a mouth that a bronze float
;; riding on its water closes with a conical plug.
;;
;; Nothing here sets a flow or a level. By hand:
;;   tap      an orifice, Q = 0.6 w a sqrt(2 g (h - a/2)), w = 5 cm, a the
;;            slot the shutter leaves, h the cistern's level above the sill.
;;   valve    the plug seats at 40 cm and is clear 2 cm below that, so the
;;            aqueduct passes 2 L/s x (40 cm - h) / 2 cm. The level settles
;;            where that equals the tap's draw, h = 40 - 2 Q / 2 (cm, L/s):
;;              tap raised 1 cm   h = 39.174 cm, 0.8264 L/s through
;;              tap raised 2 cm   h = 38.375 cm, 1.6248 L/s through
;;            Doubling the draw costs 0.80 cm of head, b dQ / Q_feed, and the
;;            level never leaves the 38..40 cm band. The 0.5 m2 receiver
;;            under it rises a steady 1.653 mm/s (3.250 once the tap is
;;            opened further): a clock.
;;   no valve the bare cistern, with the same tap and no feed, drains as
;;            sqrt(h - a/2) = sqrt(h0 - a/2) - (0.6 w a sqrt(2g) / 2A) t:
;;            22.50 cm at 60 s, 10.08 cm at 120 s (and, the tap raised to
;;            2 cm then, 3.01 cm at 150 s); its receiver's rise slows as it goes.
;;   filling  from empty with the tap shut the cistern rises 8 mm/s (2 L/s
;;            over 0.25 m2) to 38 cm at 47.5 s, then closes on 40 cm as
;;            40 - 2 exp(-(t - 47.5) / 2.5 s), the time constant A b / Q_feed,
;;            and never passes it.
(define-machine constant-head
  #:source "Ctesibius' float valve holding a constant head, beside a cistern without one"
  (post pier #:at (0 0 0) #:size ((cm 50) (cm 80) (cm 50)) #:material limestone)
  (inflow aqueduct #:into cistern #:flow (L/s 2))
  (tank cistern #:at (0 (cm 80) 0) #:area 0.25 #:height (cm 60) #:water (L 100) #:material limestone
        (port spout #:height 0))
  (float-valve ball #:on aqueduct #:shut (cm 40) #:travel (cm 2))
  (channel outlet #:from cistern.spout #:to receiver.inlet #:width (cm 5))
  (sluice tap #:on outlet #:height (cm 50) #:opening 0.02 #:material bronze)
  (tank receiver #:at ((m 2) 0 0) #:area 0.5 #:height (cm 75) #:water 0 #:material limestone
        (port inlet #:height (cm 70)))

  (post bare-pier #:at (0 0 (m -1.5)) #:size ((cm 50) (cm 80) (cm 50)) #:material limestone)
  (tank bare #:at (0 (cm 80) (m -1.5)) #:area 0.25 #:height (cm 60) #:water (L 100) #:material limestone
        (port spout #:height 0))
  (channel bare-outlet #:from bare.spout #:to bare-receiver.inlet #:width (cm 5))
  (sluice bare-tap #:on bare-outlet #:height (cm 50) #:opening 0.02 #:material bronze)
  (tank bare-receiver #:at ((m 2) 0 (m -1.5)) #:area 0.5 #:height (cm 75) #:water 0 #:material limestone
        (port inlet #:height (cm 70))))
