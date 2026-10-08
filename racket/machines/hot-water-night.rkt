#lang heroic
;; Hot water through a Mars night (issue #71): three little rooms, each losing 2 W per kelvin through its walls
;; (UA = 2 W/K, 3,000 J/K of wall and air) to a night held at -80 C, each holding a tank of water at 60 C joined to it
;; by a 6 W/K film. (header to be written from the trace)
(define-machine hot-water-night
  #:source "Newton's law of cooling, two bodies: a tank and the room it warms"
  #:planet mars
  #:ambient -80
  (enclosure ten #:at ((m -3) 0 0) #:size ((m 1.5) (m 1.2) (m 1.2)) #:pressure 13500 #:air '((o2 0.21) (n2 0.79)) #:temperature 0
             #:insulation 2 #:heat-capacity 3000)
  (heat-store ten-tank #:at ((m -3) 0 0) #:mass 10 #:contents water #:temperature 60 #:area 0 #:conductance 6)
  (enclosure thirty-three #:at (0 0 0) #:size ((m 1.5) (m 1.2) (m 1.2)) #:pressure 13500 #:air '((o2 0.21) (n2 0.79)) #:temperature 0
             #:insulation 2 #:heat-capacity 3000)
  (heat-store thirty-three-tank #:at (0 0 0) #:mass 33 #:contents water #:temperature 60 #:area 0 #:conductance 6)
  (enclosure sixty-six #:at ((m 3) 0 0) #:size ((m 1.5) (m 1.2) (m 1.2)) #:pressure 13500 #:air '((o2 0.21) (n2 0.79)) #:temperature 0
             #:insulation 2 #:heat-capacity 3000)
  (heat-store sixty-six-tank #:at ((m 3) 0 0) #:mass 66 #:contents water #:temperature 60 #:area 0 #:conductance 6))
