#lang heroic
;; A crank and connecting rod: the way a turning wheel drives a piston up
;; and down (or, the other way round, the way a steam engine's piston turns
;; its flywheel). A 40 cm iron crank disc turning at 60 rpm, its pin 15 cm
;; from the axle; a 60 cm oak rod from the pin down to a piston in its
;; cylinder. The rod hangs from the pin on a ball joint and holds the piston
;; by a pin (hinge) through its wrist.
;;
;; With the crank turned theta from where its pin points straight down, the
;; piston's wrist stands at
;;   y = y_c - r cos(theta) - sqrt(l^2 - r^2 sin^2(theta))
;; (y_c 1.6 m, r 0.15 m, l 0.6 m): 0.85 m at theta = 0, 1.019 m at 90
;; degrees -- not halfway, 1.0 m, because the rod leans -- and 1.15 m at
;; 180. Nothing here computes that: the joints hold the rod to the pin and
;; the piston, and the piston's cylinder keeps it upright.
(define axle-y (m 1.6))
(define r (cm 15))
(define l (cm 60))
(define z (cm 6))                       ; the rod and piston stand in front of the disc
(define wrist-y (- axle-y r l))         ; the pin starts pointing down

(define-machine crank-slider
  #:source "crank and connecting rod (the Hierapolis sawmill, 3rd century AD)"
  (wheel crank #:shape (drum #:radius (cm 20) #:length (cm 6)) #:at (0 axle-y 0) #:material iron #:drive-rpm 60)
  (block rod #:at (0 (- axle-y r (/ l 2)) z) #:size (cm 3) #:dimensions ((cm 3) l (cm 3)) #:material oak)
  (piston piston #:at (0 (- wrist-y (cm 10)) z) #:bore (cm 10) #:stroke (cm 50) #:start 0.2 #:material iron)
  (joint crank-pin #:kind ball #:a crank #:b rod #:at (0 (- axle-y r) z))
  (joint wrist-pin #:kind pin #:a rod #:b piston #:at (0 wrist-y z) #:axis (0 0 1)))
