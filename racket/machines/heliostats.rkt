#lang heroic
;; Two bronze boilers of a litre of water, each heated by a 0.5 m2 heliostat
;; -- a flat mirror turned through the day to keep throwing the sun onto
;; it -- at Alexandria (31.2 N) on midsummer's day, from noon.
;;
;; At noon the sun stands 82.25 degrees up in the south, and its direct beam
;; through the air (Meinel's clear sky) is 950.7 W/m2. A mirror sending that
;; light to its boiler must face halfway between the two, so it shows only
;; cos(theta/2) of itself to the sun, theta the angle between sun and boiler
;; seen from the mirror. The mirror north of its boiler looks back towards
;; the sun and keeps 0.832 of its area: 336 W. The one south of its boiler
;; stands between the boiler and the sun and keeps 0.750: 303 W. Low winter
;; sun makes the difference far larger -- at midwinter noon, 35 degrees up,
;; 0.984 against 0.425 -- which is why heliostat fields stand on the side of
;; the tower away from the equator.
(define-machine heliostats
  #:source "heliostats; the legend of Archimedes' burning mirrors"
  #:latitude 31.2 #:day 172 #:time 12
  (post stand-a #:at (-1.5 0 0) #:size ((m 0.3) (m 1) (m 0.3)) #:material limestone)
  (boiler north-lit #:at (-1.5 1 0) #:radius (m 0.1) #:height (m 0.2) #:water (kg 1) #:fire 0 #:material bronze)
  (mirror north-mirror #:at (-1.5 0.3 -3) #:area 0.5 #:onto north-lit #:reflectivity 0.85 #:material bronze)

  (post stand-b #:at (1.5 0 0) #:size ((m 0.3) (m 1) (m 0.3)) #:material limestone)
  (boiler south-lit #:at (1.5 1 0) #:radius (m 0.1) #:height (m 0.2) #:water (kg 1) #:fire 0 #:material bronze)
  (mirror south-mirror #:at (1.5 0.3 3) #:area 0.5 #:onto south-lit #:reflectivity 0.85 #:material bronze))
