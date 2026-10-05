#lang heroic
;; Friction and wear in the axles and hinges Jolt turns (#13, part 2): the
;; same bearing a pendulum has (bearing-friction.rkt), on flywheels, on a
;; lever's pin and on a water wheel's axle. Each carries its own weight
;; N = m g on a pin of radius r, and loses Coulomb μ N r (dry metal, the same
;; at any speed) and viscous c ω (grease); friction's work is heat and the
;; pin wears by Archard, V = K N s. The engine's own damping is off for them,
;; so what they lose is what is written here.
;;
;; Flywheels. Three identical wrought-iron discs, 25 cm radius, 4 cm wide,
;; 2.5 cm bore: m = 59.9 kg, I = 1/2 rho pi w (r^4 - b^4) = 1.890 kg m^2.
;; All three are let go at 60 rpm (6.283 rad/s) on a 2 cm pin.
;;   perfect  no friction. It turns at 60 rpm for ever.
;;   greased  c = 0.5 N m s/rad. omega = omega0 exp(-c t / I): a time constant
;;            I/c = 3.78 s, so 36.8% of its speed is left at 3.78 s.
;;   dry      mu = 0.4. Friction is mu m g r = 4.70 N m at any speed, so the
;;            speed falls in a straight line, omega = omega0 - 4.70 t / I,
;;            and it stops dead at t = I omega0 / 4.70 = 2.53 s (the mass
;;            cancels: t = r^2 omega0 / (2 mu g r_pin)). Its 37.3 J of spin
;;            all becomes heat in the pin.
;;
;; Levers. Two 1 m iron beams hung from a pin 25 cm from one end, so each
;; is a physical pendulum with its centre of mass d = 0.25 m from the pivot
;; (I = m (L^2 / 12 + d^2) = 6.18 kg m^2), let go 15 degrees from hanging.
;;   free     a frictionless pivot: it swings between plus and minus 15
;;            degrees, a 1.53 s period, as long as you care to watch.
;;   dry      mu = 0.4 on a 1 cm pin. Each half swing loses the same angle,
;;            about 2 mu r / d = 1.83 degrees for small swings (1.85 from the
;;            energy balance cos a2 - cos a1 = (mu r / d)(a1 + a2) at 15), so
;;            the swings shrink in a straight line and it stops within
;;            mu r / d = 0.92 degrees of plumb.
;;
;; Water wheel. The overshot wheel of water-wheels.rkt (20 L/s onto a 3 m
;; wheel, 441.5 W whatever its speed, a 300 N m millstone) on a 3 cm iron
;; axle, mu = 0.4: its 200 kg weigh 1962 N on it, so the axle takes
;; 0.4 x 1962 x 0.03 = 23.5 N m on top of the millstone's 300, and the wheel
;; settles at omega = 441.5 / (300 + 23.5) = 1.364 rad/s instead of 1.472:
;; 7.3% of the power goes to heating the axle, 32.1 W, and its wear is
;; K N s with K = 1e-4 mm^3 per N m.
(define-machine axle-friction
  #:source "Coulomb and viscous friction and wear in the axles and hinges of flywheels, levers and a water wheel"
  (wheel perfect #:shape (disc-wheel #:radius (cm 25) #:width (cm 4)) #:at ((m -1.2) 1 0) #:material iron
         #:start-rpm 60 #:bearing-radius (cm 2))
  (wheel greased #:shape (disc-wheel #:radius (cm 25) #:width (cm 4)) #:at (0 1 0) #:material iron
         #:start-rpm 60 #:bearing-radius (cm 2) #:bearing-drag 0.5)
  (wheel dry #:shape (disc-wheel #:radius (cm 25) #:width (cm 4)) #:at ((m 1.2) 1 0) #:material iron
         #:start-rpm 60 #:bearing-radius (cm 2) #:bearing-mu 0.4 #:bearing-wear 1e-4)

  (lever free #:at ((m 3.5) (m 1.5) 0) #:length (m 1) #:material iron #:pivot-fraction 1/4
         #:start-angle-deg -75 #:limit-deg 179 #:bearing-radius (cm 1))
  (lever worn #:at ((m 4.7) (m 1.5) 0) #:length (m 1) #:material iron #:pivot-fraction 1/4
         #:start-angle-deg -75 #:limit-deg 179 #:bearing-radius (cm 1) #:bearing-mu 0.4 #:bearing-wear 1e-4)

  (post header-pier #:at ((m 8) 0 0) #:size ((cm 70) (m 3.2) (cm 70)) #:material limestone)
  (inflow spring #:into header #:flow (L/s 20))
  (tank header #:at ((m 8) (m 3.2) 0) #:area 0.5 #:height (cm 40) #:water (L 50) #:material limestone
        (port race #:height (cm 10)))
  (channel race #:from header.race #:to off #:end ((m 8.75) (m 3.25) 0) #:width (cm 30) #:onto overshot)
  (waterwheel overshot #:at ((m 9) (m 1.7) 0) #:radius (m 1.5) #:width (cm 40) #:mass 200 #:load 300
              #:buckets 24 #:bucket-volume (L 10) #:tail tail-pool
              #:bearing-radius (cm 3) #:bearing-mu 0.4 #:bearing-wear 1e-4)
  (tank tail-pool #:at ((m 11.2) 0 0) #:area 2 #:height (cm 50) #:water (L 400) #:material limestone
        (port tail #:height (cm 20)))
  (channel tailrace #:from tail-pool.tail #:to off #:end ((m 13.5) (cm 5) 0) #:width (cm 30)))
