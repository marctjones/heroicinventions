#lang heroic
;; A post mill that takes its wind from the map under it (issue #61): the wind field of
;; racket/maps/victoria.rkt, v = 6 m/s x corridor x daily x gusts, at the mill's own
;; place and time of day. Four sails 10 m from hub to tip, 1500 kg of stocks and sails,
;; taking at best 0.3 of the wind's 1/2 rho A v^3 (Mars's air, 0.0155 kg/m3 at -63 C), the
;; stones set for the best a 6 m/s wind gives. Placed on the crater floor in
;; game/worlds/crater-wind.world: one on the line from the rim's notch through the
;; centre, one 160 m off it.
;;
;; Run alone from the machine list there is no map under it, so it takes a default wind
;; of 6 m/s (#175); on a map with a wind field the map's wind replaces it. Worked out
;; beforehand for that 6 m/s on Mars's air (rho = 610 Pa x 0.0434887 kg/mol /
;; (8.314 x 210.15 K) = 0.0151833 kg/m3):
;;   the wind carries 1/2 rho A v^3 = 0.5 x 0.0151833 x 314.159 x 216 = 515.2 W through the sails;
;;   the stones are set for rho = 0.0155, 2.1% more than this air gives, so the sails settle
;;   a little slow of tip-speed ratio 2.5, where torque tau0 (2 - lambda/2.5) = load, tau0 =
;;   load x 0.0151833/0.0155: lambda = 2.5 (2 - 1.020829) = 2.4479, 14.03 rpm, and the
;;   stones take load x omega = 105.18 N.m x 1.4688 rad/s = 154.5 W (Cp 0.2999 of the 515.2 W).
;;   Getting there is slow on Mars's thin air: the sails (I = 1500 x 10^2 / 3 = 50000 kg.m2)
;;   gain speed against a torque that falls 68.7 N.m for each rad/s, a time constant of
;;   727.9 s, so omega(t) = 1.4688 (1 - e^(-t/727.9)): 11.33 rpm at 1200 s, 14.01 rpm at 5000 s.
(define pi (acos -1))
(define sail-radius (m 10))
(define (best-load wind density)
  (* 1/2 density pi sail-radius sail-radius wind wind sail-radius (/ 0.3 2.5)))

(define-machine field-windmill
  #:source "a post mill on the crater floor, taking the wind of the map it stands on"
  #:planet mars
  (post trestle #:at (0 0 -2.3) #:size ((m 1.2) (m 8) (m 1.2)) #:material oak #:round #t)
  (post buck #:at (0 8 -2.3) #:size ((m 3) (m 4.5) (m 4)) #:material oak)
  (windmill mill #:at (0 10.5 0) #:radius sail-radius #:mass 1500 #:wind 6 #:wind-from-map #t
            #:load (best-load 6 0.0155) #:cp 0.3 #:tip-speed-ratio 2.5 #:material oak))
