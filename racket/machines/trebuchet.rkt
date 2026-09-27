#lang heroic
;; A counterweight trebuchet: the same lever mechanism as the see-saw,
;; but pivoted near one end instead of the centre. The granite
;; counterweight hangs from the short arm's tip on a free hinge (a real
;; trebuchet's counterweight basket does the same — it stays hanging
;; straight down as the arm swings, instead of just resting on it), so
;; falling it flings the long arm — and the payload — up and around.
;; The payload still just rests on the arm: a real trebuchet releases it
;; from a sling at the top of the swing, which needs a rope/release
;; mechanism this project doesn't have yet, so expect it to slide or
;; tumble off rather than being thrown cleanly.

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
  ;; Hangs from the short arm's tip — stays vertical under gravity as
  ;; the arm rotates, instead of sliding off like loose cargo.
  (block counterweight #:at ((- short-arm) beam-surface 0)
         #:size (cm 18) #:material granite #:hang-from arm)
  ;; Payload, near the far end of the long arm.
  (block payload #:at ((- long-arm (cm 10)) (+ beam-surface (cm 4)) 0)
         #:size (cm 8) #:material cedar))
