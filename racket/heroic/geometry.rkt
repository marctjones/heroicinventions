#lang racket/base
;; Parts whose shape follows from a few numbers: gears, Archimedes'
;; screws, pulleys, drums, treadwheels, norias and Vitruvian catapults.
;; Each generator returns a `shape` (a mesh plus its describing numbers)
;; that a wheel, screw or fixture clause in define-machine can use.
(require "geometry/mesh.rkt" "geometry/shape.rkt" "geometry/gltf.rkt"
         "geometry/gear.rkt" "geometry/screw.rkt" "geometry/wheels.rkt"
         "geometry/catapult.rkt" "geometry/catalogue.rkt")
(provide (all-from-out "geometry/shape.rkt")
         (all-from-out "geometry/gltf.rkt")
         (all-from-out "geometry/gear.rkt")
         (all-from-out "geometry/screw.rkt")
         (all-from-out "geometry/wheels.rkt")
         (all-from-out "geometry/catapult.rkt")
         (all-from-out "geometry/catalogue.rkt")
         mesh-volume mesh-bounds mesh-triangle-count mesh-vertex-count)
