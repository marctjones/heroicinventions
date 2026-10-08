#lang heroic
;; The shaduf, the counterweighted well sweep: Egypt and Mesopotamia from
;; about 2000 BC (Akkadian seals; Egyptian tomb paintings), still in use.
;; A long pole pivots on a post; a bucket hangs from the long end on a rope,
;; a lump of stone or dried mud from the short end. The lifter pulls the
;; empty bucket down into the water; the counterweight lifts it out full.
;;
;; Three sweeps side by side, alike but for their buckets, each an oak
;; plank 4 m long (2.5 x 22 cm, 15.84 kg) pivoted 1 m from its short end:
;; the counterweight hangs 1 m from the pivot, the bucket 3 m.
;; The counterweight is set so the sweep balances with its bucket half
;; full, as shadufs are: then the lifter works equally hard both ways.
;;   bucket 2 kg + 15 L of water full (17 kg), half (9.5 kg), empty (2 kg)
;;   counterweight x 1 m = 9.5 kg x 3 m + the plank's 15.84 kg x 1 m
;;     (its middle is 1 m out on the bucket side): 44.34 kg
;; Prediction: the half-full sweep stays level; the full bucket sinks and
;; the empty one rises, each to the 25 degree stop. The lifter's push is
;; (17 x 3 - 9.5 x 3) g / 3 = 73.6 N on a full bucket, the same pull
;; down on an empty one; without the counterweight, 167 N to lift it full.
;; Buckets and counterweight are granite blocks of those masses. The
;; poles are undamped (#:damping 0): with a lever's default damping a rope
;; under-holds its load and a balanced pole creeps (issue #138).
;;
;; The well (#176): a 1 m wide pool of 300 L, its surface 30 cm up, stands under the full
;; bucket where the sweep ends. The sweep turns through 25 degrees, so the bucket's rope
;; hangs from the long end at x = 3 cos 25 = 2.719 m, y = 2.9 - 3 sin 25 = 1.632 m; the
;; rope 1.2 m and the 17 kg granite block 0.185 m on a side put its underside at
;; 1.632 - 1.2 - 0.185 = 0.247 m, 5.3 cm under the water: it dips. The pool is only drawn
;; (no buoyancy: the buckets are granite blocks of the stated masses), so none of the
;; balance above changes. The other two buckets do not reach it: the half-full one hangs
;; level at 1.5 m, the empty one is lifted to 3 m.
(require racket/math)

(define plank-mass (* 4 0.025 0.22 720))                           ; 15.84 kg of oak
(define (granite-side kg) (expt (/ kg 2700) 1/3))                   ; m, a cube of that mass
(define counterweight-kg (+ (* 9.5 3) (* plank-mass 1)))           ; 44.34 kg
(define pivot-y (m 2.9))                                            ; high enough that a full bucket meets the stop, not the ground
(define cw-rope (m 0.4))
(define bucket-rope (m 1.2))

(define-machine shaduf
  #:source "Egypt and Mesopotamia, c. 2000 BC (tomb paintings, Akkadian seals)"
  (lever half-full #:at (0 pivot-y 0) #:length (m 4) #:material oak #:pivot-fraction 0.25 #:limit-deg 25 #:damping 0)
  (block half-cw #:at ((m -1) (- pivot-y cw-rope (/ (granite-side counterweight-kg) 2)) 0)
         #:size (granite-side counterweight-kg) #:material granite)
  (rope half-cw-rope #:from (half-full -1 0 0) #:to (half-cw 0 (/ (granite-side counterweight-kg) 2) 0) #:length cw-rope)
  (block half-bucket #:at ((m 3) (- pivot-y bucket-rope (/ (granite-side 9.5) 2)) 0)
         #:size (granite-side 9.5) #:material granite)
  (rope half-bucket-rope #:from (half-full 3 0 0) #:to (half-bucket 0 (/ (granite-side 9.5) 2) 0) #:length bucket-rope)

  (lever full #:at (0 pivot-y (m 1.5)) #:length (m 4) #:material oak #:pivot-fraction 0.25 #:limit-deg 25 #:damping 0)
  (block full-cw #:at ((m -1) (- pivot-y cw-rope (/ (granite-side counterweight-kg) 2)) (m 1.5))
         #:size (granite-side counterweight-kg) #:material granite)
  (rope full-cw-rope #:from (full -1 0 0) #:to (full-cw 0 (/ (granite-side counterweight-kg) 2) 0) #:length cw-rope)
  (block full-bucket #:at ((m 3) (- pivot-y bucket-rope (/ (granite-side 17) 2)) (m 1.5))
         #:size (granite-side 17) #:material granite)
  (rope full-bucket-rope #:from (full 3 0 0) #:to (full-bucket 0 (/ (granite-side 17) 2) 0) #:length bucket-rope)

  (tank well #:at ((* 3 (cos (degrees->radians 25))) 0 (m 1.5)) #:area 1 #:height (m 0.6) #:water (L 300) #:material limestone)

  (lever empty #:at (0 pivot-y (m -1.5)) #:length (m 4) #:material oak #:pivot-fraction 0.25 #:limit-deg 25 #:damping 0)
  (block empty-cw #:at ((m -1) (- pivot-y cw-rope (/ (granite-side counterweight-kg) 2)) (m -1.5))
         #:size (granite-side counterweight-kg) #:material granite)
  (rope empty-cw-rope #:from (empty -1 0 0) #:to (empty-cw 0 (/ (granite-side counterweight-kg) 2) 0) #:length cw-rope)
  (block empty-bucket #:at ((m 3) (- pivot-y bucket-rope (/ (granite-side 2) 2)) (m -1.5))
         #:size (granite-side 2) #:material granite)
  (rope empty-bucket-rope #:from (empty 3 0 0) #:to (empty-bucket 0 (/ (granite-side 2) 2) 0) #:length bucket-rope))
