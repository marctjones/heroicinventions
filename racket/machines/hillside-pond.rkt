#lang heroic
;; A pond on a hillside, and a water clock that lets it go (issue #37).
;; Standing on the flood-plain map (game/worlds/flood-plain.world), the pond
;; holds 8 m³, a metre deep, behind a shut sluice. A thin pipe from its
;; floor fills a small cup, a clepsydra; when the cup stands 10 cm deep a
;; trigger draws the sluice, and the pond empties down a short race whose
;; spout pours onto the hillside. The water runs down the valley and pools
;; in the hollow at its foot.
;;
;; Worked out beforehand: the cup (0.01 m²) fills through the pipe
;; (1e-4 m³/s per metre of head) from a pond a metre deep, h = 1 - e^(-t/100),
;; reaching 10 cm at 100 ln(1/0.9) = 10.5 s. Then the 7.2 m³ above the race's
;; lip goes, at first the free weir's 1.705 x 0.3 x 0.9^1.5 = 437 L/s.
;; Alone (no map), the race just pours off the scene: the valley it runs down is the map's, so
;; the machine menu lists the pond under Parts for Worlds and opens flood-plain.world (#175, #176).

(define-machine hillside-pond
  #:source "a hillside pond let go by a water clock"
  (post footing #:at (0 0 0) #:size ((m 2.9) (cm 50) (m 2.9)) #:material limestone)
  (tank pond #:at (0 (cm 50) 0) #:area 8 #:height (m 1.2) #:water 8 #:material limestone
        (port race-head #:height (cm 10))
        (port clock-feed #:height 0))
  (tank clock #:at (0 (cm 50) (m 2)) #:area 0.01 #:height (cm 30) #:material bronze
        (port inlet #:height 0))
  (pipe drip pond.clock-feed clock.inlet #:conductance 1e-4)
  (channel race #:from pond.race-head #:to off #:end ((m 6) (cm 30) 0) #:width (cm 30) #:dynamic #t)
  (sluice gate #:on race #:height (m 1.2) #:opening 0)
  (trigger let-go #:when (clock level above 10) #:do ((gate opening 1))))
