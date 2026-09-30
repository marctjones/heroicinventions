#lang heroic
;; Three pairs of 48-inch millstones, each runner stone turned at 120 rpm
;; from below (as a water wheel's gearing would), each set by its miller
;; to resist with 267 N.m -- 4.5 horsepower, 3,356 W, at that speed.
;;
;; A pair of 48-inch French burr stones at 120-125 rpm grinds 400 lb of
;; flour an hour on 4.5 hp when freshly dressed, and needs up to 10 hp for
;; the same when dull (The Art of the Millstones, millrestoration). So
;; sharp stones make 54 kg of flour per kWh, dull ones 24.
;;   sharp: 3,356 W x 54 kg/kWh = 181 kg an hour, 3.02 kg a minute
;;   dull:  the same power, 24 kg/kWh = 80.5 kg an hour, 1.34 kg a minute
;;   the third's wheel gives only 200 N.m: less than the stones' 267, so
;;   they never turn, and it grinds nothing. Lighten the stones (set
;;   weak.grind-torque below 200 in the console) and it will.
(define r (cm 61))
(define y (cm 75))                          ; the runner's middle; the bed stone's top is 2 cm below its face
(define stone (disc-wheel #:radius r #:width (cm 30) #:bore (cm 8)))
(define (bed x) (list x 0 0))

(define-machine gristmill
  #:source "The Art of the Millstones (millrestoration); Oliver Evans, The Young Mill-wright (1795)"
  (post sharp-bed #:at (-3 0 0) #:size ((* 2 r) (cm 58) (* 2 r)) #:material granite #:round #t)
  (wheel sharp #:shape stone #:at (-3 y 0) #:axis y #:material granite
         #:drive-rpm 120 #:drive-torque 400 #:grind-torque 267 #:yield 54)
  (post dull-bed #:at (0 0 0) #:size ((* 2 r) (cm 58) (* 2 r)) #:material granite #:round #t)
  (wheel dull #:shape stone #:at (0 y 0) #:axis y #:material granite
         #:drive-rpm 120 #:drive-torque 400 #:grind-torque 267 #:yield 24)
  (post weak-bed #:at (3 0 0) #:size ((* 2 r) (cm 58) (* 2 r)) #:material granite #:round #t)
  (wheel weak #:shape stone #:at (3 y 0) #:axis y #:material granite
         #:drive-rpm 120 #:drive-torque 200 #:grind-torque 267 #:yield 54))
