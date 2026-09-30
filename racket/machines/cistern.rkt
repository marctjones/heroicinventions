#lang heroic
;; A raised cistern: 400 L standing a metre off the ground, with an outlet
;; at its floor and nowhere for it to go. Built to be joined to another
;; machine in a world (issue #78): game/worlds/linked-pipe.world runs a
;; pipe from its outlet to the trough (racket/machines/trough.rkt) placed
;; 4 m away. Alone it just stands full.

(define-machine cistern
  #:source "a raised cistern, for joining to another machine by a pipe"
  (tank cistern #:at (0 1.0 0) #:area 0.5 #:height 1.0 #:water (L 400) #:material limestone
        (port outlet #:height 0)))
