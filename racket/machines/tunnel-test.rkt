#lang heroic
;; Continuous collision detection (issue #28). Two identical 5 cm iron bolts
;; are let fall 299 m onto two identical thin planks, 2 cm of stone floating
;; 1 m up. The engine steps 120 times a second and the bolts arrive at
;; about 76.6 m/s (sqrt(2 g 299); the fall is 7.81 s, the engine no longer
;; damping bodies since #33), so each moves 0.64 m a tick.
;; A plank plus a bolt is 7 cm: a bolt that is only tested where it stands at
;; each tick is on one side of the plank one tick and the other side the next,
;; and never touches it.
;;   fast   bolt-fast is swept along its path each step (the default): it
;;          lands on its plank, bounces, and stays above it, its middle never
;;          below 1.0 m.
;;   plain  bolt-plain has #:fast #f: it goes straight through its plank and
;;          falls on to the ground plane at y = 0, a metre below it (at
;;          0.64 m a tick its middle is seen at 0.62 m, then 0.34 m), where it bounces.
(define-machine tunnel-test
  #:source "A bolt too fast for its plank, with and without continuous collision detection"
  (post plank-fast #:at (-1 (m 1) 0) #:size ((m 1) (cm 2) (m 1)) #:material limestone)
  (post plank-plain #:at (1 (m 1) 0) #:size ((m 1) (cm 2) (m 1)) #:material limestone)
  (block bolt-fast #:at (-1 (m 300) 0) #:size (cm 5) #:material iron)
  (block bolt-plain #:at (1 (m 300) 0) #:size (cm 5) #:material iron #:fast #f))
