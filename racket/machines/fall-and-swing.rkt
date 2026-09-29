#lang heroic
;; The two oldest checks in mechanics, run in the game's rigid-body engine
;; (Jolt) rather than the sim core: a block let fall, and a pendulum let
;; swing a little.
;;
;; The block, a 10 cm iron cube with its middle 5 m up, falls freely
;; (Galileo): g = 9.81 m/s2 from the first tick, less the engine's default
;; damping of 0.1 per second -- a stand-in for air drag, not physics, until
;; #33 replaces it -- so it is at 3.8145 m at 0.5 s where 5 - g t^2/2 gives
;; 3.7738 m.
;;
;; The pendulum is a 1 m iron rod (1 cm radius) with an 8 cm iron ball on
;; the end, let go from 5 degrees. It is a physical pendulum: for small
;; swings its period is 2 pi sqrt(I / (m g d)), I its moment of inertia
;; about the pivot and d its centre of mass below it -- the same as a
;; simple pendulum of length I/(m d) = 0.97964 m, so 1.98554 s, and 5
;; degrees lengthens it by (1 + theta^2/16): 1.98649 s (Huygens, 1673).
(define-machine fall-and-swing
  #:source "Galileo's falling bodies; Huygens' pendulum (1673)"
  (block drop #:at (0 5 0) #:size (m 0.1) #:material iron)
  (pendulum swing #:at (2 1.5 0) #:length (m 1) #:start-angle-deg 5 #:material iron))
