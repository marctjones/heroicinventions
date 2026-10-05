#lang heroic
;; A plain sloping down 5 cm a metre towards +x, walled in (closed edges), of
;; sand that soaks up nothing (issue #90): every litre a tank lets go of onto
;; it stays on it, running down to the low wall and pooling there. 20 m by
;; 8 m in 0.5 m cells. The world spill (game/worlds/spill.world) stands the
;; spill-tank barrel on it.
(define-map spill-slope
  #:origin (-4 -4) #:cell 0.5 #:size (40 16)
  #:heights (slope #:gradient '(-0.05 0) #:base 1)
  #:soil sand
  #:edges closed #:roughness 0.03)
