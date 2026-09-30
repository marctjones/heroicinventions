#lang heroic
;; Heron's coin-operated holy water (Pneumatica I.21). A coin resting on the
;; pan at the end of a lever tips it; the lever's other arm lifts a plug out
;; of the spout of an urn, and the water runs until the tipping pan slides
;; the coin off, when the lever swings back and the plug seats again.
;;
;; The plug follows the lever continuously: 10 cm from the pivot, it stands
;; off its seat by 0.1 m x the angle, 1.745 mm a degree. The spout is 12 mm
;; across, so the water passes the curtain pi d lift (37.7 mm x lift) until
;; the plug is a quarter of the bore (3 mm) clear, which is at 1.72 degrees,
;; and from there the whole bore, pi d^2/4 = 1.131 cm2.
;;
;; Predicted, before running:
;;   wide open  the urn holds 20 L over a 0.05 m2 floor, 40 cm deep, and the
;;              spout is 5 cm up: head 0.35 m. Q = Cd A sqrt(2 g h) =
;;              0.6 x 1.131e-4 x sqrt(2 x 9.81 x 0.35) = 0.1778 L/s.
;;   part open  at 1 mm lift the area is 37.7 mm2, a third of the bore:
;;              0.0593 L/s. In general Q = 0.6 pi d lift sqrt(2 g h) below 3 mm.
;;   the coin   bronze on oak: it slides when tan(angle) exceeds mu = 0.30,
;;              at 16.7 degrees (the engine takes the lower coefficient).
;;   shut       once the coin is off, the lever returns, the plug seats, and
;;              the flow stops: a coin buys a fixed dose, not a running tap.
;; Seen when run: the pan swings on past 16.7 degrees to about 47 degrees,
;; the coin leaves between 16 and 26 degrees, and the lever is back and the
;; plug seated about 1.5 s after the coin was put down, 0.2 L poured.
(define-machine holy-water
  #:source "Hero of Alexandria, Pneumatica I.21"
  (post pier #:at ((m 1.2) 0 0) #:size ((cm 40) (cm 50) (cm 40)) #:material limestone)
  (tank urn #:at ((m 1.2) (cm 50) 0) #:area 0.05 #:height (cm 60) #:water (L 20) #:material bronze)
  ;; the jet leaves at v = sqrt(2 g h) = 2.62 m/s and falls 0.55 m in 0.335 s: it lands 0.88 m out
  (tank basin #:at ((m 2.1) 0 0) #:area 0.2 #:height (cm 30) #:material bronze)
  (leak spout #:on urn #:height (cm 5) #:bore (cm 1.2) #:into basin)
  ;; a stand under the beam, clear of it: a beam that touches its stand gets wedged
  (post stand #:at (0 0 0) #:size ((cm 8) (cm 65) (cm 8)) #:material oak)
  (lever beam #:at (0 (cm 70) 0) #:length (m 1) #:material oak #:limit-deg 45 #:spring-stiffness 8)
  (block coin #:at ((cm -35) (cm 74.6) 0) #:size (cm 5) #:material bronze)
  (follow plug #:lever beam #:from 0 #:to 5.73 #:set (spout lift) #:low 0 #:high 10))
