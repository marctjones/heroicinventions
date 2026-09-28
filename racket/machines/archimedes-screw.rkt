#lang heroic
;; An Archimedes' screw built to Vitruvius's rules (De Architectura
;; X.6): given only its length, the core beam is length/16 across, eight
;; helical strips wind round it one turn per circumference of core, the
;; strips are built up until the whole is length/8 across, and it is set
;; on a 3-4-5 slope. Men treading turn it; each turn carries a pocket of
;; water one pitch further up.
;;
;; This one is 4 m long: 50 cm across, eight blades at 78.5 cm pitch.
;; Vitruvius planks the screw over into a closed tube; the casing is left
;; off so the helix shows. It turns at a treading pace — lifting water
;; with it needs the screw's water-transport physics, not yet built.

(define len (m 4))
(define tilt vitruvian-screw-incline-deg)

(define-machine archimedes-screw
  #:source "Vitruvius, De Architectura X.6"
  (screw cochlea #:shape (vitruvian-screw #:length len)
         #:at (0 (+ (cm 40) (* (/ len 2) 3/5)) 0) ; lower end 40 cm off the ground
         #:material pine #:tilt-deg tilt #:drive-rpm 12))
