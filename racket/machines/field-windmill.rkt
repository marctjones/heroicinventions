#lang heroic
;; A post mill that takes its wind from the map under it (issue #61): the wind field of
;; racket/maps/victoria.rkt, v = 6 m/s x corridor x daily x gusts, at the mill's own
;; place and time of day. Four sails 10 m from hub to tip, 1500 kg of stocks and sails,
;; taking at best 0.3 of the wind's 1/2 rho A v^3 (Mars's air, 0.0155 kg/m3 at -63 C), the
;; stones set for the best a 6 m/s wind gives. Placed on the crater floor in
;; game/worlds/crater-wind.world: one on the line from the rim's notch through the
;; centre, one 160 m off it.
(define pi (acos -1))
(define sail-radius (m 10))
(define (best-load wind density)
  (* 1/2 density pi sail-radius sail-radius wind wind sail-radius (/ 0.3 2.5)))

(define-machine field-windmill
  #:source "a post mill on the crater floor, taking the wind of the map it stands on"
  #:planet mars
  (post trestle #:at (0 0 -2.3) #:size ((m 1.2) (m 8) (m 1.2)) #:material oak #:round #t)
  (post buck #:at (0 8 -2.3) #:size ((m 3) (m 4.5) (m 4)) #:material oak)
  (windmill mill #:at (0 10.5 0) #:radius sail-radius #:mass 1500 #:wind-from-map #t
            #:load (best-load 6 0.0155) #:cp 0.3 #:tip-speed-ratio 2.5 #:material oak))
