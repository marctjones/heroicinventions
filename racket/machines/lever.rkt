#lang heroic
;; A see-saw: an oak beam hinged at its centre, with a granite block on
;; one end and a lighter cedar block on the other. Nothing computes the
;; torque balance directly — it falls out of Jolt's own contact physics
;; and each material's real density, exactly as a real lever would.

(define-machine lever-demo
  #:source "Classic mechanics demonstration"
  (lever beam #:at (0 0.5 0) #:length (m 1.2) #:material oak)
  ;; Placed in exact resting contact with the level beam (pivot 0.5 + half
  ;; thickness 0.02 + half block 0.07 = 0.59) — any initial drop turns
  ;; into an impact against a beam that's already rotating, which can
  ;; tip a block onto its side instead of letting it ride up smoothly.
  (block heavy #:at ((cm -50) 0.59 0) #:size (cm 14) #:material granite)
  (block light #:at ((cm 50) 0.59 0) #:size (cm 14) #:material cedar))
