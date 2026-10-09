#lang heroic
;; A salvaged motor, as found in the Lonely Rover's cargo (owner ruling 2026-10-09: generators are found, never built): a cargo crate
;; (cargo-crate's oak box, 50 cm, 90 kg, on Mars) with the motor out of it standing on the crate's lid as a 10 cm iron rotor on its own
;; axle, with the real generator part on that rotor: found-electrics' motor, the same part and numbers, cut-in 1,500 rpm (157.08 rad/s),
;; rated 2,500 rpm at 12 N.m, eta 0.8 (tau_g = 0.114592 (w - 157.08) N.m above the cut-in; charging P = eta tau w).
;; It is a machine of its own, placed in the world like the found bank (found-bank.rkt). To use it the player joins a shaft from a
;; turning part of the machine they built (a geared-up pinion) to its rotor (the world's J join, a shaft link), and a wire from its motor
;; to the bank (#208). The shaft link carries the rotor at the pinion's speed, so the motor loads the player's train exactly as a generator
;; built on that pinion did. Alone, it turns nothing and charges nothing.
(define-machine found-motor
  #:source "a salvaged motor in a cargo crate on Mars: a crate, and the motor's rotor and generator beside it, for the player to join to a shaft and a bank"
  #:planet mars
  (block crate #:at (0 (cm 25) 0) #:size (cm 50) #:material oak)
  (wheel rotor #:shape (disc-wheel #:radius (cm 10) #:width (cm 8)) #:at (0 (cm 62) 0) #:material iron)
  (generator motor #:at (0 (cm 62) (cm -12)) #:on rotor))
