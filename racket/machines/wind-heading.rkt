#lang heroic
;; Windmills and the wind's heading (issue #193). Four identical post mills, sails
;; 10 m from hub to tip, all in the same 6 m/s wind, all built facing +z (azimuth 90,
;; azimuths measured from +x toward +z). They differ only in where the wind comes from
;; and whether the mill can turn to meet it. Only the part of the wind along the axle
;; goes through the sails, v cos(theta) for theta the angle between the two, so
;; a mill that stays put takes 1/2 rho A v^3 cos^3(theta): cos theta in the speed and
;; the cube of it in the power.
;;
;;   square  wind from 90: theta = 0,  cos^3 = 1
;;   skewed  wind from 30: theta = 60, cos^3 = (1/2)^3 = 1/8, fixed, stones set for the
;;                         3 m/s it actually gets
;;   vane    wind from 30: the same wind, but a tail vane turns the sails 60 degrees at
;;                         2 degrees a second; stones set for the full 6 m/s, which they
;;                         can start against once cos^2 theta > 1/2, theta < 45, at 15 s
;;   across  wind from 0:  theta = 90, cos = 0, the sails turn edge-on and take nothing
;;
;; Air at 20 C: rho = 1.2041 kg/m3, A = pi x 10^2 = 314.16 m2.
;;   1/2 rho A v^3 at 6 m/s = 40854.77 W; x Cp 0.3 = 12256.43 W (square, and vane once turned, 30 s)
;;   skewed: 40854.77 / 8 = 5106.85 W through the sails; x 0.3 = 1532.05 W
;;           (stones tau0 = 1/2 rho A v_eff^2 R Cp/lambda* = 2042.7 N.m at lambda* x 3/10 = 0.75 rad/s)
;;   across: 0 W
(define pi (acos -1))
(define air-density (/ 101325 (* 287.05 293.15)))
(define sail-radius (m 10))
(define (best-load wind)
  (* 1/2 air-density pi sail-radius sail-radius wind wind sail-radius (/ 0.3 2.5)))

(define-machine wind-heading
  #:source "four post mills in one wind: square on, skewed, with a tail vane, and edge-on"
  (post square-trestle #:at (-39 0 0) #:size ((m 1.2) (m 8) (m 1.2)) #:material oak #:round #t)
  (windmill square #:at (-39 10.5 2.8) #:radius sail-radius #:mass 1500 #:wind 6
            #:load (best-load 6) #:material oak)

  (post skewed-trestle #:at (-13 0 0) #:size ((m 1.2) (m 8) (m 1.2)) #:material oak #:round #t)
  (windmill skewed #:at (-13 10.5 2.8) #:radius sail-radius #:mass 1500 #:wind 6 #:wind-from-deg 30
            #:load (best-load 3) #:material oak)

  (post vane-trestle #:at (13 0 0) #:size ((m 1.2) (m 8) (m 1.2)) #:material oak #:round #t)
  (windmill vane #:at (13 10.5 2.8) #:radius sail-radius #:mass 1500 #:wind 6 #:wind-from-deg 30
            #:vane #t #:load (best-load 6) #:material oak)

  (post across-trestle #:at (39 0 0) #:size ((m 1.2) (m 8) (m 1.2)) #:material oak #:round #t)
  (windmill across #:at (39 10.5 2.8) #:radius sail-radius #:mass 1500 #:wind 6 #:wind-from-deg 0
            #:load (best-load 6) #:material oak))
