#lang heroic
;; A counterweight trebuchet: the same lever mechanism as the see-saw,
;; but pivoted near one end instead of the centre. The granite
;; counterweight hangs from the short arm's tip on a free hinge (a real
;; trebuchet's counterweight basket does the same — it stays hanging
;; straight down as the arm swings, instead of just resting on it), so
;; falling it flings the long arm around. The payload hangs from the long
;; arm's tip the same way, and releases once the arm has swung 40° from
;; level — a rough stand-in for a sling's release hook, not a modelled
;; rope, but enough to launch the payload cleanly instead of watching it
;; slide or tumble off partway through the swing.

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
         #:size (cm 22) #:material granite #:hang-from arm)
  ;; Hangs from the long arm's tip; releases once the arm passes 40° —
  ;; the launch.
  (block payload #:at (long-arm beam-surface 0)
         #:size (cm 8) #:material cedar #:hang-from arm #:release-past-deg 40))
