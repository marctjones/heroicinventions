#lang heroic
;; Galileo's classic: four blocks of different materials released from
;; the same height on the same 25° ramp. The one that starts moving —
;; and how far each one slides — comes straight from the material
;; table's real friction coefficients, nothing scripted.

(define (slot i) (* i (cm 20)))

;; The ramp's surface tops out around y=0.62 at its highest (far) end
;; (length·sin(angle) + half-thickness); dropping blocks from well above
;; that, onto a *static* ramp, is simple and reliable — unlike a block
;; resting on a moving surface (see lever.rkt), a falling block landing on
;; fixed geometry has no timing race to get wrong.
(define-machine inclined-plane-demo
  #:source "Classic mechanics demonstration"
  (ramp slope #:at (0 0 (cm -40)) #:length (m 1.4) #:width (m 1.0) #:angle-deg 25 #:material limestone)
  (block cedar-block   #:at ((slot -1.5) (m 1.1) (cm -90)) #:size (cm 12) #:material cedar)
  (block oak-block     #:at ((slot -0.5) (m 1.1) (cm -90)) #:size (cm 12) #:material oak)
  (block granite-block #:at ((slot 0.5)  (m 1.1) (cm -90)) #:size (cm 12) #:material granite)
  (block bronze-block  #:at ((slot 1.5)  (m 1.1) (cm -90)) #:size (cm 12) #:material bronze))
