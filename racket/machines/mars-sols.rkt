#lang heroic
;; Three sols at Meridiani Planum (issue #69): Opportunity's plain, 2° south,
;; starting at noon on sol 1, with a dust storm blowing all of sol 2.
;; Worked out before running:
;;
;; sols     a Mars solar day is 24 h 39 m 35 s = 88,775 s, cut into 24 local
;;          hours of 3,699 s each: sol 2 begins 12 local hours in, at
;;          44,387.5 s, and sol 3 at 133,162.5 s.
;; air      the planet's daily curve (scenario numbers): -80 °C before dawn,
;;          -20 °C in the afternoon, warmest at 15:00, coldest at 03:00:
;;          T = -50 + 30 cos(2π(h - 15)/24).
;; storm    all of sol 2 the dust's optical depth is 10.8, Opportunity's last
;;          storm (NASA/JPL, June 2018), against a clear sky's 0.3: the sun's
;;          beam at the ground loses a further e^(-10.5 AM), AM ≈ 1/sin α the
;;          air mass. At noon, with the sun high, that leaves about 1/40,000
;;          of the clear beam: effectively dark.
;; mirror   a 1 m² heliostat onto a pot. Dust settles on it through the storm,
;;          taking half of what it still reflects each sol: after the storm's
;;          one sol it reflects e^(-0.5) = 0.607 of what it did clean, until
;;          the rover cleans it (mirror.dust 0) and it is whole again.
;; relay    the orbiter passes at 03:00 and 15:00 local time, for 10 minutes.
(define-machine mars-sols
  #:source "Sols, cold nights and a dust storm at Meridiani Planum"
  #:planet mars
  #:latitude -2 #:day 100 #:time 12
  #:weather (weather #:passes '(3 15) #:pass-minutes 10
                     #:storms (list (storm #:sol 2 #:hour 0 #:tau 10.8 #:sols 1 #:settle 0.5)))
  (post hob #:at (0 0 0) #:size ((m 0.5) (m 0.4) (m 0.5)) #:material limestone)
  (boiler pot #:at (0 (m 0.4) 0) #:radius (m 0.15) #:height (m 0.3) #:water (kg 5) #:fire 0 #:material bronze)
  (mirror mirror #:at (0 (m 1.5) (m -3)) #:area (m2 1) #:onto pot))
