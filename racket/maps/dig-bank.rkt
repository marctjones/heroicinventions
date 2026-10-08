#lang heroic
;; A bank to build a ramp up (issue #63): level ground of ice-cemented regolith, 60 x 60 cells of 1 m, with a bank 1 m high at 45
;; degrees across it at x = 30 (the cells' centres 29.5 and 30.5 are 0 and 1 m). Ice-cemented soil (40 kPa) stands as a cut face to
;; 56 m, so the bank holds at an angle no loose soil would, and the rover, which climbs 41 degrees on a height map and no more,
;; can't climb it. rover-dig-bank.world stands the rover in front of it.
(define-map dig-bank
  #:origin (0 0) #:cell 1 #:size (60 60)
  #:heights (λ (x z) (if (< x 30) 0 1))
  #:soil ice-cemented-regolith
  #:cohesion ((ice-cemented-regolith 40000))
  #:edges closed)
