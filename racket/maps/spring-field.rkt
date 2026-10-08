#lang heroic
;; A spring on level ground, for the rover to dig a channel from (issue #200): the crater's 5 m cells, 24 x 24 of them (120 m a
;; side), of Mars regolith a little cemented (2 kPa, so a backhoe's cut stands a few tens of centimetres before it slumps) that
;; soaks up 1 µm/s. A 6 L/s spring wells up at (-6.6, 0). Left alone its water spreads over the 5 m cells as a sheet; a channel
;; the rover cuts from it along x takes the water in at the dug ground's 0.25 m cells, not the map's 5 m ones, and carries it
;; along until the channel stands full: eighteen bucketfuls, 3.6 m³, ten minutes of the spring. Level, so the backhoe may tip
;; its spoil on either side of the channel (it never tips above where it dug).
(define-map spring-field
  #:origin (-60 -60) #:cell 5 #:size (24 24)
  #:heights (slope #:gradient '(0 0) #:base 0)
  #:soil regolith
  #:infiltration ((regolith 1e-6))
  #:cohesion ((regolith 2000))
  #:edges open #:roughness 0.03
  (source spring #:at (-6.6 0) #:flow 0.006))
