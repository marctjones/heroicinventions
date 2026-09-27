#lang heroic
;; A counterweight trebuchet: the same lever mechanism as the see-saw,
;; but pivoted near one end instead of the centre. A heavy granite
;; counterweight on the short arm falls, flinging the long arm — and
;; whatever light block sits on its far end — up and around. No sling
;; release here (that's a mechanism of its own), so watch the "payload"
;; block get thrown rather than smoothly carried, same as a real
;; trebuchet without one.

(define total-length (m 1.8))
(define pivot-fraction 0.15)          ; short arm = 15% of the beam
(define short-arm (* pivot-fraction total-length))
(define long-arm (- total-length short-arm))
(define pivot-y (m 1.0))
(define beam-surface (+ pivot-y (cm 2))) ; pivot + half beam thickness

(define-machine trebuchet
  #:source "Classic mechanics demonstration (not ancient, but Hero and Vitruvius's torsion catapults are its cousins)"
  (lever arm #:at (0 pivot-y 0) #:length total-length #:material oak
         #:pivot-fraction pivot-fraction #:limit-deg 70 #:damping 5.0)
  ;; Counterweight, on the short arm, in resting contact with the level beam.
  (block counterweight #:at ((* -0.7 short-arm) (+ beam-surface (cm 9)) 0)
         #:size (cm 18) #:material granite)
  ;; Payload, near the far end of the long arm.
  (block payload #:at ((- long-arm (cm 10)) (+ beam-surface (cm 4)) 0)
         #:size (cm 8) #:material cedar))
