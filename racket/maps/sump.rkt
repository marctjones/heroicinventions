#lang heroic
;; A round hollow 10 m across and 0.5 m deep (a paraboloid), walled in, of
;; loam that soaks up nothing, with a 2 L/s spring 2 m west of its bottom
;; (issue #90). Laid out so that a cell's centre is at the bottom, (0, 0):
;; the cistern-drain machine's grate sits there. 21 x 21 cells of 0.5 m.
(define-map sump
  #:origin (-5.25 -5.25) #:cell 0.5 #:size (21 21)
  #:heights (bowl #:centre '(0 0) #:radius 5 #:depth 0.5)
  #:soil loam
  #:edges closed #:roughness 0.03
  (source spring #:at (-2 0) #:flow 0.002))
