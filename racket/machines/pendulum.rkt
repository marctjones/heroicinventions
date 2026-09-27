#lang heroic
;; A classic physics-text demo: a compound pendulum, released from 40°
;; off vertical. Not historically ancient, but the archetypal machine
;; every dynamics course opens with — good ground truth for the sim,
;; since its behaviour is analytically well known.

(define-machine pendulum-demo
  #:source "Classic mechanics demonstration"
  (pendulum rod #:at (0 0.9 0) #:length (cm 60) #:material iron #:start-angle-deg 40))
