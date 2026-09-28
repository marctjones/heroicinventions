#lang heroic
;; A noria: a water-lifting wheel turned by the river it stands in. The
;; current pushes on paddles dipping into the water; buckets built into
;; the rim fill at the bottom, ride up, and tip out near the top into an
;; aqueduct. No animal, no man: the river lifts its own water. Norias
;; like this watered the fields of Hama on the Orontes from Roman times;
;; the survivors there reach 20 m across. This one is 6 m.
;;
;; Its speed isn't set anywhere. The river's drag on the submerged
;; paddles, ½·ρ·Cd·A·(v − u)², falls as the paddles speed up toward the
;; current; the torque of lifting the water, ρ·g·H·V/2π, doesn't. The
;; wheel speeds up until the two balance — about 1.2 rpm here, typical
;; of real norias.
;;
;; Nor is the current set. The Orontes arrives from upstream (half a cubic
;; metre a second — the river beyond the scene isn't modelled, only what
;; it brings), fills a weir pool, and spills over the lip of its sluice
;; into a stone race. How fast the race runs is Manning's equation: its
;; 1.2 cm fall over 2.5 m, against the stone's roughness, carries the flow
;; 28 cm deep at 1.5 m/s — and that is the water the paddles stand in.
;; Past the wheel, the river leaves over the tailrace weir, which holds the
;; wheel's basin 85 cm deep. Choke the river (set upstream.flow) and the
;; race runs slower and shallower, and the wheel slows with it.
(define r (m 3))
(define axle-y (m 3.5))                   ; paddle tips just clear the river bed; bucket rims dip 0.35 m

(define-machine hama-noria
  #:source "The norias of Hama, Syria (Orontes river)"
  (wheel naura #:shape (noria #:radius r #:width (cm 40) #:buckets 24
                               #:bucket-depth (cm 15) #:paddle-depth (cm 45))
         #:at (0 axle-y 0) #:material oak)
  (inflow upstream #:into weir-pool #:flow (L/s 500))
  (tank weir-pool #:at ((m 6.25) 0 0) #:area 4 #:height (m 1.6) #:water (L 5412)
        (port sluice #:height (cm 96.2)))
  (channel race #:from weir-pool.sluice #:to river.inlet #:width (m 1.2))
  ;; The wheel's basin: 5.5 m across, so the race and tailrace troughs
  ;; meet it beyond the wheel's rim instead of running through it.
  (tank river #:at (0 0 0) #:area 30.25 #:height (m 1.0) #:water (L 25712)   ; 85 cm deep
        (port inlet #:height (cm 95))
        (port tail #:height (cm 46.4)))
  (channel tailrace #:from river.tail #:to off #:end ((m -9) (cm 40) 0) #:width (m 1.2))
  ;; The aqueduct runs alongside the wheel, just behind it, catching the
  ;; buckets as they tip; a conduit carries the water off to the fields
  ;; (here, a cistern beside the river), so the wheel keeps working
  ;; instead of filling it.
  (tank aqueduct #:at ((m -1.2) (m 5.9) (cm -45)) #:area 1.0 #:height (cm 40)
        (port channel #:height 0))
  (tank fields #:at ((m -2) 0 (m -5)) #:area 4 #:height (m 1.0)
        (port inlet #:height (m 1.0)))
  (pipe conduit aqueduct.channel fields.inlet #:conductance 2e-3)
  (lift raise #:by naura #:from river #:to aqueduct #:current-from race))
