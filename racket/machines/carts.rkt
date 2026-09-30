#lang heroic
;; Why the wheel: two handcarts and an oak sledge let go side by side on
;; a 10 degree limestone slope, 4 m long, that runs out onto the flat.
;; The sledge doesn't move: oak on limestone grips at mu = 0.52, and the
;; slope needs only tan 10 = 0.18 to hold it. The carts roll.
;;
;; A cart of mass M whose wheels turn with inertia I each (about their
;; axles, radius r) runs down at
;;   a = g (sin t - C_rr cos t) M / (M + sum I / r^2)
;; -- the wheels' own turning soaks up part of the pull -- and on the flat
;; it slows at C_rr g M / (M + sum I / r^2). Rolling resistance on stone,
;; C_rr = 0.04 (a stage coach on a dirt road, 0.0385-0.073).
;;   an oak bed (18.4 kg) on solid disc wheels, 30 cm, 2.51 kg each,
;;     I = 0.504 m r^2 (the bore): M = 28.48 kg, sum I/r^2 = 5.06 kg:
;;     down the slope at 1.118 m/s2, then slowing on the flat at 0.333
;;   a light pine bed (8.0 kg) on spoked wheels, same size, 1.06 kg each,
;;     I = 0.572 m r^2: M = 12.22 kg, sum I/r^2 = 2.41 kg: 1.100 and 0.328
;; (worked from the wheels' own meshes, as the test does). The spoked
;; wheels are light, but so is the bed: a quarter of that cart turns.
;;
;; Keep a wheel's mass above an eighth of its bed's or so: Jolt lets a body
;; sink up to 2 cm into another before it pushes back, and a light wheel
;; under a heavy bed rides that low and rolls on a smaller radius (a
;; spoked wheel under the oak bed slowed 6% too fast).
(require racket/math racket/list)

(define slope-deg 10)
(define t (degrees->radians slope-deg))
(define ramp-length (m 4))
(define base-z (m 2))                       ; the ramp's low edge, at the ground; it rises toward -z
(define half-slab (cm 2.5))                 ; MachineView's ramp slab is 5 cm thick
(define r (cm 15))                          ; wheel radius
(define up (m 3.0))                         ; carts start this far up the slope
(define wheel-w (cm 5))
(define chassis '(0.4 0.08 0.8))            ; x (across), y, z (along)

;; A point on the slope `along` metres up from the low edge, `x` across,
;; `lift` out from the slab's top along its normal.
(define (on-slope x along lift)
  (define n (+ half-slab lift (mm 1)))
  (list x (+ (* along (sin t)) (* n (cos t))) (+ base-z (- (* along (cos t))) (* n (sin t)))))
(define (px p) (first p)) (define (py p) (second p)) (define (pz p) (third p))

(define (wheel-at cx side end)                ; side: -1 or 1 across; end: -1 or 1 along
  (on-slope (+ cx (* side (+ (/ (first chassis) 2) (/ wheel-w 2) (cm 1)))) (+ up (* end (cm 30))) r))
(define disc-x (m -0.9))
(define spoke-x (m 0.9))
(define c-disc (on-slope disc-x up r))
(define c-spoke (on-slope spoke-x up r))
(define c-sledge (on-slope 0 up (cm 10)))
(define-values (d1 d2 d3 d4) (values (wheel-at disc-x -1 -1) (wheel-at disc-x -1 1) (wheel-at disc-x 1 -1) (wheel-at disc-x 1 1)))
(define-values (s1 s2 s3 s4) (values (wheel-at spoke-x -1 -1) (wheel-at spoke-x -1 1) (wheel-at spoke-x 1 -1) (wheel-at spoke-x 1 1)))
(define disc (disc-wheel #:radius r #:width wheel-w))
(define spoked (cart-wheel #:radius r #:width wheel-w))

(define-machine carts
  #:source "the wheel (Mesopotamia, c. 3500 BC); rolling resistance"
  (ramp slope #:at (0 0 base-z) #:length ramp-length #:width (m 3) #:angle-deg slope-deg #:material limestone)
  (block sledge #:at ((px c-sledge) (py c-sledge) (pz c-sledge)) #:size (cm 20) #:dimensions ((cm 40) (cm 20) (cm 60))
         #:material oak #:tilt-deg slope-deg)
  (block disc-cart #:at ((px c-disc) (py c-disc) (pz c-disc)) #:size (cm 8)
         #:dimensions ((first chassis) (second chassis) (third chassis)) #:material oak #:tilt-deg slope-deg)
  (wheel disc-1 #:shape disc #:at ((px d1) (py d1) (pz d1)) #:axis x #:material oak #:on disc-cart)
  (wheel disc-2 #:shape disc #:at ((px d2) (py d2) (pz d2)) #:axis x #:material oak #:on disc-cart)
  (wheel disc-3 #:shape disc #:at ((px d3) (py d3) (pz d3)) #:axis x #:material oak #:on disc-cart)
  (wheel disc-4 #:shape disc #:at ((px d4) (py d4) (pz d4)) #:axis x #:material oak #:on disc-cart)
  (block spoke-cart #:at ((px c-spoke) (py c-spoke) (pz c-spoke)) #:size (cm 5)
         #:dimensions ((first chassis) (cm 5) (third chassis)) #:material pine #:tilt-deg slope-deg)
  (wheel spoke-1 #:shape spoked #:at ((px s1) (py s1) (pz s1)) #:axis x #:material oak #:on spoke-cart)
  (wheel spoke-2 #:shape spoked #:at ((px s2) (py s2) (pz s2)) #:axis x #:material oak #:on spoke-cart)
  (wheel spoke-3 #:shape spoked #:at ((px s3) (py s3) (pz s3)) #:axis x #:material oak #:on spoke-cart)
  (wheel spoke-4 #:shape spoked #:at ((px s4) (py s4) (pz s4)) #:axis x #:material oak #:on spoke-cart))
