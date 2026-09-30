#lang heroic
;; A gang of twenty labourers (about 150 W each, 3 kW together) cutting a
;; trench 4 m long and 1 m wide, meaning to go 4 m down, unshored, a spit
;; (25 cm) at a time, throwing the spoil in a ridge 5 m to one side
;; (issue #44). Stood on the clay-pit map (game/worlds/trench.world).
;;
;; Worked out beforehand, for clay with c = 10 kPa, tan φ = 0.35 and
;; γ = 1800 x 9.81 = 17658 N/m³:
;; - the walls stand to 4c/γ · tan(45° + φ/2) = 3.19 m, so the spit that
;;   takes the trench to 3.25 m is the one they fall in at, and the gang
;;   stops there;
;; - each cubic metre costs the clay's shear strength where it is cut plus
;;   the lift out, c + γ z (1 + tan φ), z the slab's depth; over the whole
;;   cut to 3.25 m, z averages 1.625 m: 10000 + 17658 x 1.35 x 1.625 =
;;   48.7 kJ/m³, 13 m³ out, 634 kJ, 211 s at 3 kW;
;; - the loose spoil (no cohesion) slumps to its angle of repose, 19.3°.
;; Alone, with no ground under it, the gang has nothing to dig.

(define-machine trench-crew
  #:source "a gang digging an unshored trench"
  (digger gang #:at (0 0 0) #:length (m 4) #:width (m 1) #:depth (m 4) #:power 3000 #:spoil (m 5)))
