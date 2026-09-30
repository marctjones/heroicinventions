#lang heroic
;; Level loam (issue #54): 20 x 12 m in half-metre cells, holding together by
;; 5 kPa of cohesion (so a cut face stands to 4c/γ · tan(45° + φ/2) =
;; 4 x 5000 / 13734 x 1.69 = 2.46 m), with a crate buried in it (see
;; racket/machines/buried-crate.rkt).
(define-map buried-field
  #:origin (-10 -6) #:cell 0.5 #:size (40 24)
  #:heights (λ (x z) 0)
  #:soil loam
  #:cohesion ((loam 5000))
  #:edges closed)
