#lang heroic
;; A crate: an oak box 50 cm on a side, 90 kg. Standing at the foot of the
;; cliff map (game/worlds/slide.world, issue #54), the cliff's collapse
;; buries it.
(define-machine crate
  #:source "a crate at a cliff's foot"
  (block crate #:at (0 (cm 25) 0) #:size (cm 50) #:material oak))
