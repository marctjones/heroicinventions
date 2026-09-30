#lang heroic
;; Water holding things up (issue #29).
;;
;; Left, a cistern (0.1 m², 40 cm of water) draining through a 1 cm² hole
;; in its floor, with a cork float (0.5 kg on 0.01 m²) riding it. Afloat
;; the float draws m / (rho A) = 0.5 / (1000 x 0.01) = 5 cm and rides that
;; far below the surface, going down with it. The water falls by
;; Torricelli: sqrt(h) = sqrt(0.4) - Cd a sqrt(2g) / (2A) t, with
;; Cd a sqrt(2g) / (2A) = 0.6 x 1e-4 x 4.429 / 0.2 = 1.329e-3, so it is
;; 5 cm deep at (0.6325 - 0.2236) / 1.329e-3 = 307.7 s. Then the float
;; sits on the floor.
;;
;; Right, a bath (1 m², 40 cm of water) with 20 cm blocks dropped into it.
;; A block lighter than water floats with rho_block / rho_water of it under:
;; cedar (380 kg/m³) 7.6 cm, pine (500) 10 cm, oak (720) 14.4 cm, so their
;; centres settle at 42.4, 40.0 and 35.6 cm. Iron (7700) sinks to the floor,
;; its centre at 10 cm.

(define-machine floats
  #:source "Archimedes, On Floating Bodies"
  (tank cistern #:at (0 0 0) #:area 0.1 #:height (cm 50) #:water (L 40) #:material limestone
        (port drain #:height 0))
  (leak tap #:on cistern #:height 0 #:area (cm2 1))
  (float bob #:in cistern #:mass 0.5 #:area 0.01 #:height (cm 10) #:material cedar)

  (tank bath #:at ((m 2) 0 0) #:area 1 #:height (cm 60) #:water (L 400) #:material limestone)
  (block cedar-block #:at ((m 1.7) (cm 70) 0) #:size (cm 20) #:material cedar)
  (block pine-block #:at ((m 2) (cm 70) (cm -30)) #:size (cm 20) #:material pine)
  (block oak-block #:at ((m 2.3) (cm 70) 0) #:size (cm 20) #:material oak)
  (block iron-block #:at ((m 2) (cm 70) (cm 30)) #:size (cm 20) #:material iron))
