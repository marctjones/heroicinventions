#lang heroic
;; A drain at the bottom of a hollow into a cistern below it (issue #90).
;; The grate is 10 cm square, 0.4 m of lip; the cistern 1 m² and 1.4 m deep,
;; sunk beside it. Stood at the bottom of the sump map (game/worlds/sump.world),
;; whose spring runs 2 L/s into the hollow. Worked out beforehand:
;;   the water over the grate pours in as over a broad-crested weir, Q =
;;   1.705 P h^1.5. Once the hollow has filled to steady, the grate takes all
;;   the spring gives, 2 L/s, so the cistern fills at 2 L/s (2 mm a second
;;   up its 1 m²), with the water standing h = (Q / (1.705 x 0.4))^(2/3) =
;;   (0.002 / 0.682)^(2/3) = 2.05 cm deep over the grate. The cistern, the
;;   ground and nothing else hold what the spring has given: loam that soaks
;;   up nothing, walls all round.
(define-machine cistern-drain
  #:source "a grate over a cistern at the bottom of a hollow"
  (drain grate #:at (0 0 0) #:into cistern #:perimeter 0.4)
  (tank cistern #:at ((m 1.5) (m -1) 0) #:area 1 #:height (m 1.4) #:water 0 #:material limestone))
