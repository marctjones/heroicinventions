#lang heroic
;; Four cubes of the same size dropped side by side. Their mass and
;; friction in the physics engine come from the material table.

(define size (cm 15))
(define (slot i) (+ -0.35 (* i 0.25)))  ; x position of the i-th cube
(define (drop i) (+ 1.2 (* i 0.2)))     ; staggered drop heights

(define-machine material-samples
  (block cedar-cube   #:at ((slot 0) (drop 0) 0.3) #:size size #:material cedar)
  (block oak-cube     #:at ((slot 1) (drop 1) 0.3) #:size size #:material oak)
  (block granite-cube #:at ((slot 2) (drop 2) 0.3) #:size size #:material granite)
  (block bronze-cube  #:at ((slot 3) (drop 3) 0.3) #:size size #:material bronze))
