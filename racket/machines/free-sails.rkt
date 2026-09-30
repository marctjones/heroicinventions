#lang heroic
;; A post mill with its stones taken out: 10 m sails in a 6 m/s breeze and
;; nothing to grind, so alone it races (tip-speed ratio 5, 28.6 rpm, the
;; sails taking nothing from the wind). Built to drive another machine by
;; a shaft across a world (issue #78): game/worlds/linked-shaft.world
;; joins its windshaft to the dry mill (racket/machines/dry-mill.rkt),
;; whose millstones then take the sails' torque.

(define-machine free-sails
  #:source "a post mill with no stones of its own, for driving another machine"
  (post trestle #:at (0 0 0) #:size ((m 1.2) (m 8) (m 1.2)) #:material oak #:round #t)
  (post buck #:at (0 8 0.5) #:size ((m 3) (m 4.5) (m 4)) #:material oak)
  (windmill sails #:at (0 10.5 2.8) #:radius (m 10) #:mass 1500 #:wind 6
            #:cp 0.3 #:tip-speed-ratio 2.5 #:material oak))
