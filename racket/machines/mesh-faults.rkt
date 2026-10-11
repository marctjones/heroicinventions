#lang heroic
;; Meshes off their centre distance run as they stand (owner, 2026-10-11): a poor design stays possible and fails
;; honestly. Module 10 mm: a 20-tooth pinion (pitch radius 0.10 m, tips 0.11) and a 40-tooth wheel (0.20, tips
;; 0.21): to mesh 0.30 m apart (within 2 %, 6 mm: true); the tips reach to 0.32 m (past 0.306 and short of 0.32:
;; loose, the teeth in each other's gaps with play); apart from 0.32 m they don't touch; nearer than 0.294 m the teeth
;; dig in (jammed).
;;
;; Five pairs, each pinion turned at 30 rpm by a crank (#:drive-rpm, its hinge motor) or by a windmill:
;;   true:   0.30 m apart: the wheel turns at 30 x 20 / 40 = 15 rpm (1.5708 rad/s), the pinion 3.1416 rad/s.
;;   loose:  0.31 m apart (10 mm out, tips overlapping 10 mm): the same 15 rpm.
;;   far:    3 m apart: the wheel is never touched: 0 rad/s throughout, the crank still 3.1416.
;;   jam:    0.25 m apart (50 mm too close), the crank's most torque 5 N.m: neither turns, 0 rad/s (< 0.001).
;;   mill-jam: a windmill in a 3 m/s wind (generator-train's sails: 5 m, 200 kg, Cp 0.3 at a tip-speed ratio of
;;     2.5: torque at rest 2 x 255.34 = 510.7 N.m, inertia 200 x 25 / 3 = 1,667 kg.m2) with the 40 on its shaft,
;;     jammed 0.25 m from a 20: the train is held, so the sails stand. Each 1/120 s tick the wind gives them
;;     510.7 / 120 / 1,667 = 0.00255 rad/s (0.024 rpm) before the teeth take it back: sails under 0.03 rpm, the
;;     gears under 0.001 rad/s.
;;   mill-far: the same windmill with its 40 three metres from a 20: nothing loads the sails, which run up to
;;     where their torque is 0, w = 2 x 2.5 x 3 / 5 = 3 rad/s (28.65 rpm), with a time constant of 1,667 /
;;     (255.34 x 5 / 7.5) = 9.79 s: 3 (1 - e^(-60/9.79)) = 2.9935 rad/s, 28.59 rpm, at 60 s; the far 20 at 0.
;; And a belt, built after the gear trains (when the view threw on a bad mesh, every pass after the gear trains was skipped):
;; a 10 cm pulley cranked at 60 rpm (6.283 rad/s) turns a 20 cm one at about half that, 3.14 rad/s, less the belt's slip.
(require racket/math)

(define md (mm 10))
(define pinion-20 (spur-gear #:teeth 20 #:module md #:width (cm 3)))
(define wheel-40 (spur-gear #:teeth 40 #:module md #:width (cm 3)))
(define true-apart (* md (+ 10 20)))                    ; 0.30 m
(define wheel-angle (radians->degrees (mate-angle 20 0 40 0)))   ; the 40 +x of its 20, a tooth in a gap
(define hub-y (m 6))

(define-machine mesh-faults
  #:source "owner, 2026-10-11: meshes off their centre distance run as they stand (true, loose, not touching, jammed)"
  ;; true, at the centre distance
  (wheel true-a #:shape pinion-20 #:at (0 (m 1) 0) #:material iron #:drive-rpm 30 #:drive-torque 50)
  (wheel true-b #:shape wheel-40 #:at (true-apart (m 1) 0) #:material iron #:angle-deg wheel-angle)
  (mesh true-a true-b)
  ;; loose: 10 mm out
  (wheel loose-a #:shape pinion-20 #:at (0 (m 2) 0) #:material iron #:drive-rpm 30 #:drive-torque 50)
  (wheel loose-b #:shape wheel-40 #:at ((+ true-apart (mm 10)) (m 2) 0) #:material iron #:angle-deg wheel-angle)
  (mesh loose-a loose-b)
  ;; not touching: 3 m apart
  (wheel far-a #:shape pinion-20 #:at (0 (m 3) 0) #:material iron #:drive-rpm 30 #:drive-torque 50)
  (wheel far-b #:shape wheel-40 #:at ((m 3) (m 3) 0) #:material iron)
  (mesh far-a far-b)
  ;; jammed: 50 mm too close, a crank of 5 N.m on it
  (wheel jam-a #:shape pinion-20 #:at (0 (m 4) 0) #:material iron #:drive-rpm 30 #:drive-torque 5)
  (wheel jam-b #:shape wheel-40 #:at ((m 0.25) (m 4) 0) #:material iron)
  (mesh jam-a jam-b)
  ;; a windmill whose 40 is jammed against a 20
  (windmill mill-jam #:at ((m 12) hub-y 0) #:radius (m 5) #:mass 200 #:wind 3 #:cp 0.3 #:tip-speed-ratio 2.5 #:material oak)
  (wheel mill-jam-wheel #:shape wheel-40 #:at ((m 12) hub-y (cm -130)) #:material iron)
  (wheel mill-jam-pinion #:shape pinion-20 #:at ((+ (m 12) (m 0.25)) hub-y (cm -130)) #:material iron)
  (arbor mill-jam mill-jam-wheel)
  (mesh mill-jam-wheel mill-jam-pinion)
  ;; a windmill whose 40 is three metres from its 20
  (windmill mill-far #:at ((m 26) hub-y 0) #:radius (m 5) #:mass 200 #:wind 3 #:cp 0.3 #:tip-speed-ratio 2.5 #:material oak)
  (wheel mill-far-wheel #:shape wheel-40 #:at ((m 26) hub-y (cm -130)) #:material iron)
  (wheel mill-far-pinion #:shape pinion-20 #:at ((+ (m 26) (m 3)) hub-y (cm -130)) #:material iron)
  (arbor mill-far mill-far-wheel)
  (mesh mill-far-wheel mill-far-pinion)
  ;; a belt, built in a pass after the gear trains (a bad mesh once skipped it): a 10 cm pulley cranked at 60 rpm drives a 20 cm one
  (wheel belt-driver #:shape (pulley #:radius (cm 10) #:width (cm 5)) #:at (0 (m 5) 0) #:material oak #:drive-rpm 60 #:drive-torque 50)
  (wheel belt-driven #:shape (pulley #:radius (cm 20) #:width (cm 10)) #:at ((cm 60) (m 5) 0) #:material oak)
  (belt belt-1 belt-driver belt-driven #:tension 50))
