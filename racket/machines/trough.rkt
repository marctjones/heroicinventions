#lang heroic
;; An empty trough on the ground, with an inlet 10 cm up its wall. Built to
;; be filled from another machine in a world (issue #78): see
;; racket/machines/cistern.rkt and game/worlds/linked-pipe.world.

(define-machine trough
  #:source "a trough, for filling from another machine by a pipe"
  (tank trough #:at (0 0 0) #:area 0.5 #:height 1.0 #:material limestone
        (port inlet #:height (cm 10))))
