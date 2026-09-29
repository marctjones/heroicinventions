#lang heroic
;; Two identical post mills: four sails 10 m from hub to tip (a disc of
;; pi x 10^2 = 314.16 m2), 1500 kg of stocks and sails, the sails taking at
;; best 0.3 of the wind when their tips run 2.5 times as fast as it. The
;; left one stands in a 6 m/s breeze, the right in a 9 m/s wind, and each
;; miller has set the stones for the best that wind can give: the sails'
;; torque, 1/2 rho A v^2 R x Cp/lambda* x (2 - lambda/lambda*), is set
;; against the stones at lambda = lambda*, where it is 1/2 rho A v^2 R x
;; Cp/lambda*. Then each turns at 2.5 v / R and takes 0.3 of the wind's
;; 1/2 rho A v^3: 12.26 kW in the breeze, 41.37 kW in the wind -- (9/6)^3
;; = 3.375 times as much for half again the wind speed.
;;
;; Air at 20 C: 101325 Pa / (287.05 J/kg.K x 293.15 K) = 1.2041 kg/m3.
(define pi (acos -1))
(define air-density (/ 101325 (* 287.05 293.15)))
(define sail-radius (m 10))
(define (best-load wind)
  (* 1/2 air-density pi sail-radius sail-radius wind wind sail-radius (/ 0.3 2.5)))

(define-machine windmills
  #:source "two post mills, in a breeze and in a wind"
  (post breeze-trestle #:at (-12 0 0) #:size ((m 1.2) (m 8) (m 1.2)) #:material oak #:round #t)
  (post breeze-buck #:at (-12 8 0.5) #:size ((m 3) (m 4.5) (m 4)) #:material oak)
  (windmill breeze #:at (-12 10.5 2.8) #:radius sail-radius #:mass 1500 #:wind 6
            #:load (best-load 6) #:cp 0.3 #:tip-speed-ratio 2.5 #:material oak)

  (post wind-trestle #:at (12 0 0) #:size ((m 1.2) (m 8) (m 1.2)) #:material oak #:round #t)
  (post wind-buck #:at (12 8 0.5) #:size ((m 3) (m 4.5) (m 4)) #:material oak)
  (windmill gale #:at (12 10.5 2.8) #:radius sail-radius #:mass 1500 #:wind 9
            #:load (best-load 9) #:cp 0.3 #:tip-speed-ratio 2.5 #:material oak))
