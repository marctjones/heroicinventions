#lang heroic
;; A granite block 50 cm on a side, 2 m up. Alone it falls to the flat
;; floor. Placed on a map (game/worlds/ground-check.world, issue #37) it
;; falls onto the ground's height-map collision and comes to rest on the
;; slope: its friction (0.6) is ten times the hill's 1-in-17 grade, so it
;; stays where it lands, its centre 0.25 / cos(3.4°) = 0.2504 m above the
;; ground under it.
(define-machine boulder
  #:source "a block dropped onto the ground"
  (block stone #:at (0 2 0) #:size (m 0.5) #:material granite))
