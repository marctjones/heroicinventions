#lang heroic
;; An Archimedes' screw built to Vitruvius's rules (De Architectura
;; X.6): given only its length, the core beam is length/16 across, eight
;; helical strips wind round it one turn per circumference of core, the
;; strips are built up until the whole is length/8 across, and it is set
;; on a 3-4-5 slope. A man treading it turns it; its lower end stands in a
;; pool and each turn carries water one pitch further up, pouring out
;; into a trough at the top.
;;
;; How much each turn carries comes from the screw's own shape: tilted,
;; each channel dips and rises as it winds, and water pools in each dip
;; up to the crest on its downhill side. At this slope a 4 m Vitruvian
;; screw holds ~2.9 L per pocket, 23 L per turn over its eight channels —
;; a quarter of the channels' volume. As the pool drops below the intake,
;; scoops come up part-full and the flow falls off.
;;
;; Lifting takes work: ρ·g·H·V/2π of torque against the treading, ~75 N·m
;; here, against the ~150 N·m one man gives. Vitruvius planks the screw
;; over into a closed tube; the casing is left off so the helix shows.

(define len (m 4))
(define tilt vitruvian-screw-incline-deg)
(define rise (* (/ len 2) 3/5))           ; half the length's rise at 3-4-5: 1.2 m
(define centre-y (+ (cm 40) rise))

(define-machine archimedes-screw
  #:source "Vitruvius, De Architectura X.6"
  (screw cochlea #:shape (vitruvian-screw #:length len)
         #:at (0 centre-y 0)
         #:material pine #:tilt-deg tilt #:drive-rpm 12 #:drive-torque 150)
  (tank pool #:at ((m -1.9) 0 0) #:area 1.2 #:height (cm 80) #:water (L 900))
  (tank trough #:at ((m 1.9) (m 2.1) 0) #:area 0.8 #:height (cm 50))
  (lift raise #:by cochlea #:from pool #:to trough))
