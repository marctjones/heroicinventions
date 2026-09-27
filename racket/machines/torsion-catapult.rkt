#lang heroic
;; A Greek/Roman torsion catapult (an onager-style single-arm engine, the
;; kind Vitruvius's Book X gives proportion tables for): a bundle of
;; twisted sinew rope at the arm's short end stores energy by torsion,
;; the same way twisting a rubber band does. Released, it snaps the long
;; arm upward until it slams into the frame's crossbar — modelled here
;; as a hard stop (#:limit-deg) — and the stone in its sling keeps
;; going on its own momentum.
;;
;; Jolt has no torsion-spring joint to model the twisted skein directly,
;; so the release is modelled at the moment it happens: the arm simply
;; starts already spinning at the speed the skein would have given it
;; (#:initial-spin-deg-per-sec) — the energy is real, just not the spring
;; that produced it.

(define arm-length (m 0.7))
(define pivot-fraction 0.1)   ; the skein anchors near one end, not the centre
(define short-arm (* pivot-fraction arm-length))
(define long-arm (- arm-length short-arm))
(define pivot-y (m 0.8))
(define beam-surface (+ pivot-y (cm 1.25))) ; pivot + half beam thickness

(define-machine torsion-catapult
  #:source "Vitruvius, De Architectura Book X; Philo of Byzantium, Belopoeica"
  (lever arm #:at (0 pivot-y 0) #:length arm-length #:material oak
         #:pivot-fraction pivot-fraction #:limit-deg 80 #:damping 1.0
         #:initial-spin-deg-per-sec 480)
  ;; The stone, in a sling near the arm's far end — releases as the arm
  ;; nears the frame's stop, same mechanism as the trebuchet's payload.
  (block stone #:at ((- long-arm (cm 8)) beam-surface 0)
         #:size (cm 10) #:material granite #:hang-from arm #:release-past-deg 65))
