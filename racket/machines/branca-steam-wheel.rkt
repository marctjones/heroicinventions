#lang heroic
;; Giovanni Branca's steam wheel (Le Machine, Rome, 1629): a pot of water
;; over a fire, a spout on its lid, and a wheel of flat paddles set in the
;; jet. Branca drew it grinding drugs through a pestle; here it drives a
;; small steady load, like a spit.
;;
;; Working it through (checked against the simulation):
;;   heat    a 3 kW wood fire, half of it reaching the pot: 1.5 kW, less
;;           the pot's 2 W/K loss to the air, ~170 W at 105 C. The rest,
;;           ~1.33 kW, boils water into steam at 1330 / 2.24e6 = 0.59 g/s.
;;   jet     the pot's pressure settles where a 2.5 mm spout lets out
;;           exactly that: 21 kPa over the air, a jet of 245 m/s. A smaller
;;           spout makes the same steam come out faster.
;;   push    flat paddles stop the jet relative to them: F = mdot (v - u),
;;           0.14 N at a standstill.
;;   speed   against a 3 mN.m load, 2 mN.m of bearing and the paddles'
;;           windage (1/2 rho Cd n a r^3 w^2: 1.76e-5 w^2 N.m for eight 3 cm
;;           paddles at 15 cm), the wheel runs up until
;;           mdot (v - w r) r = 0.005 + 1.76e-5 w^2: w = 30.4 rad/s, 290 rpm.
;;   power   0.09 W reaches the load from 1.5 kW of fire. Air drag on the
;;           paddles takes most of what the jet gives; Branca's wheel never
;;           became a practical engine.
(define plinth-height (cm 30))
(define-machine branca-steam-wheel
  #:source "Giovanni Branca, Le Machine (1629)"
  (post plinth #:at (0 0 0) #:size ((cm 60) plinth-height (cm 60)) #:material limestone)
  (hearth fire #:at (0 plinth-height 0) #:heats pot #:power (W 3000) #:fuel (kg 2) #:fuel-kind wood #:efficiency 0.5)
  (boiler pot #:at (0 (+ plinth-height (cm 8)) 0) #:radius (cm 12) #:height (cm 20)
          #:water (kg 1) #:fire 0 #:temperature 95 #:material bronze)
  ;; the spout rises from the lid and turns toward the wheel's lowest paddle
  (jetwheel wheel #:at ((cm 30) (+ plinth-height (cm 48)) 0) #:radius (cm 15) #:bore (mm 2.5)
            #:paddles 8 #:width (cm 3) #:mass (kg 0.5) #:load 0.003 #:material bronze)
  (connect pot.steam wheel.steam-in))
