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
(define r (m 3))
(define axle-y (m 3.5))                   ; paddle tips just clear the river bed; bucket rims dip 0.35 m

(define-machine hama-noria
  #:source "The norias of Hama, Syria (Orontes river)"
  (wheel naura #:shape (noria #:radius r #:width (cm 40) #:buckets 24
                               #:bucket-depth (cm 15) #:paddle-depth (cm 45))
         #:at (0 axle-y 0) #:material oak)
  (tank river #:at (0 0 0) #:area 9 #:height (m 1.0) #:water (L 7650))   ; 85 cm deep
  ;; The aqueduct runs alongside the wheel, just behind it, catching the
  ;; buckets as they tip; a channel carries the water off to the fields
  ;; (here, a cistern), so the wheel keeps working instead of filling it.
  (tank aqueduct #:at ((m -1.2) (m 5.9) (cm -45)) #:area 1.0 #:height (cm 40)
        (port channel #:height 0))
  (tank fields #:at ((m -3.8) 0 0) #:area 4 #:height (m 1.0)
        (port inlet #:height (m 1.0)))
  (pipe channel aqueduct.channel fields.inlet #:conductance 2e-3)
  (lift raise #:by naura #:from river #:to aqueduct #:current 1.5))
