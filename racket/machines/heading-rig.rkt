#lang heroic
;; Parts at a heading (#83). The same rig is placed three times in
;; game/worlds/headings.world, turned 0, 37 and 90 degrees about the
;; vertical through its origin, and a machine turned by any heading must
;; behave as it did unturned: every body in the same place with its
;; coordinates turned, x' = x cos h + z sin h, z' = -x sin h + z cos h, and
;; its speeds, spins and hinge angles the same. Four things happen in it,
;; each with its direction, its speed or its period worked out beforehand.
;;
;; An iron ball on a 35 degree limestone slope, let go at rest 0.9 m up it. The
;; ramp rises toward -z, so downhill is +z: (0, 0, 1). Turned by h it is
;; (sin h, 0, cos h). The ball rolls, so it gains speed at (5/7) g sin 35
;; = 4.02 m/s^2 along the slope, s = 1/2 a t^2: 0.50 m by half a second, 0.41 m
;; of it across the ground and 0.29 m of it down, along that line and never to
;; either side. (A block would slide instead, and Jolt's friction acts along two
;; axes chosen from the contact's normal, not along the sliding direction, so
;; a block sliding on a slope turned off the axes drifts a few centimetres
;; sideways; a rolling ball's grip is static and doesn't.)
;;
;; The same on a second ramp that carries its own #:heading-deg 90: its
;; downhill is (sin 90, 0, cos 90) = +x, and with the machine turned by h too,
;; (sin(h + 90), 0, cos(h + 90)).
;;
;; A 50 cm iron pendulum let go from 10 degrees swings in the plane its hinge
;; (axis z, turned by h) leaves it: its bob moves along (cos h, 0, -sin h) and
;; not across it; a second hung with #:heading-deg 90 swings along z instead.
;; Both keep a compound pendulum's period, worked out in the test.
;;
;; A flywheel let go at 30 rpm on an axle along x, turned by h to
;; (cos h, 0, -sin h), runs down on the engine's own 0.2 per second of axle
;; damping, omega = omega0 e^(-0.2 t), whichever way its axle points.
(require racket/math racket/list)

(define slope-deg 35)
(define a (degrees->radians slope-deg))
(define radius (cm 6))
(define lift (+ (cm 2.5) radius (mm 1)))        ; half the slab, the ball's radius, a millimetre to settle
(define up-slope (m 0.9))

;; where the ball rests on a ramp whose low edge is at (bx 0 bz), turned by hd degrees: up the
;; slope, then out along the surface normal. In the ramp's own frame that is
;; (0, along sin a + lift cos a, -(along cos a) + lift sin a), turned about its foot.
(define (on-ramp bx bz hd)
  (define h (degrees->radians hd))
  (define local-z (+ (- (* up-slope (cos a))) (* lift (sin a))))
  (list (+ bx (* local-z (sin h)))
        (+ (* up-slope (sin a)) (* lift (cos a)))
        (+ bz (* local-z (cos h)))))

(define-values (sx sy sz) (apply values (on-ramp 0 (cm -40) 0)))
(define-values (tx ty tz) (apply values (on-ramp 3 0 90)))

(define-machine heading-rig
  #:source "Parts at a heading: slopes, pendulums and a flywheel that must behave the same however they are turned"
  (ramp slope #:at (0 0 (cm -40)) #:length (m 1.6) #:width (m 0.8) #:angle-deg slope-deg #:material limestone)
  (ball roller #:at (sx sy sz) #:radius radius #:material iron)

  (ramp side-slope #:at (3 0 0) #:length (m 1.6) #:width (m 0.8) #:angle-deg slope-deg #:material limestone #:heading-deg 90)
  (ball side-roller #:at (tx ty tz) #:radius radius #:material iron)

  (pendulum rod #:at (-2 1.2 0) #:length (cm 50) #:material iron #:start-angle-deg 10)
  (pendulum rod2 #:at (-3 1.2 0) #:length (cm 50) #:material iron #:start-angle-deg 10 #:heading-deg 90)

  (wheel fly #:shape (disc-wheel #:radius (cm 25) #:width (cm 4)) #:at (-5 1.2 0) #:material iron #:axis x
         #:start-rpm 30))
