#lang heroic
;; One crate of the Lonely Rover's cargo (issue #61): an oak box 50 cm on a side, 90 kg,
;; standing on Mars. The opening world (game/worlds/lonely-rover-opening.world) places
;; five of them, each named for what it holds, at the foot of the crater's weakened
;; rim section; the rim's collapse buries whichever it reaches.
(define-machine cargo-crate
  #:source "a crate of the rover's cargo, on Mars"
  #:planet mars
  (block crate #:at (0 (cm 25) 0) #:size (cm 50) #:material oak))
