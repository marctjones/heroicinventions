#lang heroic
;; A post-and-lintel crane: a trilithon (two limestone posts capped by a
;; lintel) carries the axle of a long oak beam, and a granite counterweight
;; on the short arm lifts a granite load on the long arm -- the shaduf of
;; Egypt and Mesopotamia, built the way Stonehenge was.
;;
;; Torque about the pivot, from the densities (granite 2700 kg/m3 as the
;; counterweight 35 cm cube = 116 kg at 0.55 m, the load 20 cm cube = 21.6 kg
;; at 1.8 m, the 3 m oak beam's own weight acting 0.6 m along the long arm):
;;   short side 116 x 0.55 = 63.7 kg.m > long side 21.6 x 1.8 + beam x 0.6
;; so the counterweight drops, the beam turns to its +/-15 degree stop, and
;; the load rises. Nothing computes that; Jolt's contacts do it.
(define pivot-y (m 1))
(define beam-top (+ pivot-y (mm 12.5)))

(define-machine post-and-lintel-crane
  #:source "Shaduf-style counterweight lever on a trilithon"
  (post post-near #:at (0 0 (cm 40)) #:size ((cm 30) (cm 140) (cm 30)) #:material limestone)
  (post post-far  #:at (0 0 (cm -40)) #:size ((cm 30) (cm 140) (cm 30)) #:material limestone)
  (block lintel #:at (0 (+ (cm 140) (cm 7.5) (mm 1)) 0) #:size (cm 10)
         #:dimensions ((cm 40) (cm 15) (cm 110)) #:material limestone)
  (lever beam #:at (0 pivot-y 0) #:length (m 3) #:pivot-fraction 0.3 #:material oak
         #:limit-deg 15)
  (block counterweight #:at ((cm -55) (+ beam-top (cm 17.5) (mm 1)) 0) #:size (cm 35) #:material granite)
  (block load #:at ((cm 180) (+ beam-top (cm 10) (mm 1)) 0) #:size (cm 20) #:material granite))
