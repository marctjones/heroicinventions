#lang heroic
;; A slope of loose quartz sand (issue #53): falling 1 in 20 towards +x,
;; gathered into a shallow valley along z = 0, 40 x 30 cells of 1 m, its
;; grains 0.5 mm across. Water running down it fast enough lifts the grains
;; (Shields number over 0.047) and carries them: a jet cuts a gully, and
;; where the flow spreads and slows at the foot the sand drops in a fan.
;; hushing.world lets the hillside pond go onto it.
(define-map sand-slope
  #:origin (-5 -15) #:cell 1 #:size (40 30)
  #:heights (λ (x z) (+ 2 (* -0.05 (+ x 5)) (* 0.002 z z)))
  #:soil sand
  #:grain ((sand 0.0005))
  #:edges open #:roughness 0.03)
