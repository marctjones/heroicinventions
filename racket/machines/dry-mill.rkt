#lang heroic
;; A water mill whose stream has dried up: an overshot wheel with empty
;; buckets, set to grind against its stones' 8171 N·m, standing still.
;; Built to be driven by another machine's shaft in a world (issue #78):
;; game/worlds/linked-shaft.world joins it to the free sails
;; (racket/machines/free-sails.rkt).
;;
;; The load is the one at which those sails, in their 6 m/s breeze, turn at
;; their best tip-speed ratio, 2.5: tau0 = 1/2 rho pi R^2 v^2 R Cp/lambda*
;; = 8171 N·m. Joined by a plain shaft, the pair should settle where the
;; sails' torque, tau0 (2 - lambda/2.5), equals that load: lambda = 2.5,
;; omega = 2.5 x 6 / 10 = 1.5 rad/s, 14.32 rpm on both, the shaft carrying
;; 8171 N·m and 12.26 kW, as the windmills scene's breeze mill does with its
;; own stones. Sails and wheel together, 50000 + 4500 kg·m², against the
;; sails' torque falling 5447 N·m per rad/s: settled within 10 s or so.

(define pi (acos -1))
(define air-density (/ 101325 (* 287.05 293.15)))
(define best-load (* 1/2 air-density pi 10 10 6 6 10 (/ 0.3 2.5)))

(define-machine dry-mill
  #:source "a water mill whose stream has dried up"
  (post pier #:at ((m -1.9) 0 0) #:size ((cm 50) (m 1.7) (cm 50)) #:material limestone)
  (waterwheel wheel #:at (0 (m 1.7) 0) #:radius (m 1.5) #:width (cm 40) #:mass 2000
              #:load best-load #:buckets 24 #:bucket-volume (L 10)))
