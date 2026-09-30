#lang heroic
;; Level ground of stiff clay (issue #44): 30 x 20 m in half-metre cells,
;; holding together by 10 kPa of cohesion, its friction (tan φ = 0.35,
;; φ = 19.3°) and density (1800 kg/m³) the material table's. A cut face in it
;; stands to 4c/γ · tan(45° + φ/2) = 4 x 10000 / 17658 x 1.410 = 3.19 m.
(define-map clay-pit
  #:origin (-15 -10) #:cell 0.5 #:size (60 40)
  #:heights (λ (x z) 0)
  #:soil clay
  #:infiltration ((clay 1e-7))
  #:cohesion ((clay 10000))
  #:edges closed)
