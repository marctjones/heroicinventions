#lang heroic
;; A dam break (issue #36). A millpond 10 m across is held by a shut sluice
;; while a stream fills it; when it stands 55 cm deep a trigger draws the
;; gate right up, and the pent water runs down a dry 40 m race, falling 1 in
;; 200, into a low pond. The race holds its water along its length (a
;; dynamic channel, 80 cells of 50 cm): the wave takes time to get down it,
;; its front thin and fast, the reach behind it filling.
;;
;; Worked out beforehand. The stream brings 250 L/s into 100 m², raising
;; the pond from 50 to 55 cm in 5 m³ / 0.25 = 20 s: the gate goes then.
;; The water then stands 35 cm over the race's lip: at most the free weir's
;; 1.705 x 0.5 x 0.35^1.5 = 176.5 L/s (less once the race below backs up
;; against it). Running at that, the race's normal (Manning) depth is 28.7 cm
;; at 1.23 m/s, and waves on it run at 1.23 + sqrt(9.81 x 0.287) = 2.91 m/s.
;; So the front should reach the low pond no sooner than 40 / 2.91 = 13.8 s
;; after the gate goes and no later than 40 / 1.23 = 32.5 s (a kinematic
;; shock at the normal speed): between 33.8 and 52.5 s. Every litre is
;; accounted for: pond + race + low pond = what there was + the stream's.
;;
;; By hand. The gate is a sluice (click it: "Open the gate", opening 1; the
;; slider draws it part way), so a person can draw it before the trigger does.
;; The demo operator does, at 15 s, when the pond stands 50 + 250 x 15 / 100 =
;; 53.75 cm: 33.75 cm over the lip, at most 1.705 x 0.5 x 0.3375^1.5 = 167.2 L/s
;; at first, and the front is at the low pond 13.8 to 32.5 s later, 28.8 to 47.5 s
;; in. The stream still brings more (250 L/s) than the gate passes (168 L/s), so
;; the pond goes on rising by 82 L/s and reaches 55 cm at about 15 + 1.25 m3 /
;; 0.078 m3/s = 31 s, where the trigger finds the gate open and has nothing left to do.

(define-machine dam-break
  #:source "a millpond's sluice drawn, and the wave down its race"
  (inflow stream #:into millpond #:flow (L/s 250))
  (post pond-bank #:at (0 0 0) #:size ((m 10) (m 1) (m 10)) #:material limestone)
  (tank millpond #:at (0 (m 1) 0) #:area 100 #:height (m 1.2) #:water 50 #:material limestone
        (port race-head #:height (cm 20)))
  (channel race #:from millpond.race-head #:to low-pond.inlet #:width (cm 50) #:length (m 40)
           #:dynamic #t #:cells 80)
  (sluice gate #:on race #:height (m 1) #:opening 0)
  (trigger full #:when (millpond level above 55) #:do ((gate opening 1)))
  (tank low-pond #:at ((m 47.75) 0 0) #:area 30 #:height (m 1.5) #:material limestone
        (port inlet #:height (m 1)))
  ;; the gate drawn by hand at 15 s, five seconds before the trigger would (issue #157); taking any control stops it
  (operator (at 15 (gate opening 1))))
