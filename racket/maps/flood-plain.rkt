#lang heroic
;; A hillside and the hollow at its foot (issue #37): ground falling 6 cm
;; a metre towards +x, gathered gently into a valley along x = 0 (rising
;; 0.004 z² to either side), with a round hollow 9 m across and 1.5 m deep
;; dug into it at (30, 0). Water let go at the top of the hill (the
;; hillside-pond machine does) runs down the valley, fans out and pools in
;; the hollow; what gets past the hollow's low lip runs on off the map's
;; open edge. Loam, soaking up 2 µm/s. 60 x 40 cells of 1 m.
(define hill (λ (x z) (+ 3 (* -0.06 (+ x 10)) (* 0.004 z z))))

(define-map flood-plain
  #:origin (-10 -20) #:cell 1 #:size (60 40)
  #:heights (ground+ hill (bowl #:centre '(30 0) #:radius 9 #:depth 1.5))
  #:soil loam
  #:infiltration ((loam 2e-6))
  #:edges open #:roughness 0.03)
