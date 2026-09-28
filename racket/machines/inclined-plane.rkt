#lang heroic
;; Galileo's classic: four blocks of different materials set at rest on
;; the same 25° limestone slope and let go. A block slides when the slope
;; is steeper than its friction can hold — when tan(angle) exceeds its
;; friction coefficient against the stone. tan 25° ≈ 0.47: granite
;; (μ 0.60) holds; cedar (0.40), oak (0.45) and bronze (0.30) slide,
;; bronze fastest. Nothing scripted — it comes from the material table.
(require racket/math racket/list)

(define angle-deg 25)
(define a (degrees->radians angle-deg))
(define ramp-base (list 0 0 (cm -40)))  ; low edge of the ramp, at ground level
(define ramp-half-thickness (cm 2.5))   ; MachineView's ramp slab is 5 cm thick
(define size (cm 12))

;; Where a block of `size` rests flat on the ramp, `along` metres up the
;; slope from the low edge and `x` to the side: up the slope, then out
;; along the surface normal by half the slab plus half the block (plus a
;; millimetre, so it settles rather than starting interpenetrated).
(define (on-ramp x along)
  (define lift (+ ramp-half-thickness (/ size 2) (mm 1)))
  (list x
        (+ (second ramp-base) (* along (sin a)) (* lift (cos a)))
        (+ (third ramp-base) (- (* along (cos a))) (* lift (sin a)))))

(define (slot i) (* i (cm 22)))
(define up-slope (m 1.0))
(define-values (x0 y0 z0) (apply values (on-ramp (slot -1.5) up-slope)))
(define-values (x1 y1 z1) (apply values (on-ramp (slot -0.5) up-slope)))
(define-values (x2 y2 z2) (apply values (on-ramp (slot 0.5) up-slope)))
(define-values (x3 y3 z3) (apply values (on-ramp (slot 1.5) up-slope)))

(define-machine inclined-plane-demo
  #:source "Classic mechanics demonstration"
  (ramp slope #:at (0 0 (cm -40)) #:length (m 1.4) #:width (m 1.0) #:angle-deg angle-deg #:material limestone)
  (block cedar-block   #:at (x0 y0 z0)
         #:size size #:material cedar #:tilt-deg angle-deg)
  (block oak-block     #:at (x1 y1 z1)
         #:size size #:material oak #:tilt-deg angle-deg)
  (block granite-block #:at (x2 y2 z2)
         #:size size #:material granite #:tilt-deg angle-deg)
  (block bronze-block  #:at (x3 y3 z3)
         #:size size #:material bronze #:tilt-deg angle-deg))
