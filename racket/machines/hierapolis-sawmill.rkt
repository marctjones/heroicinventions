#lang heroic
;; The Hierapolis sawmill: a water wheel turning a crank, a connecting rod
;; from the crank to a saw frame, the frame drawn back and forth across a
;; block of stone. The earliest known crank and connecting rod.
;;
;; Sources: the relief on the sarcophagus of Marcus Aurelius Ammianus at
;; Hierapolis in Phrygia (3rd century AD), published by K. Grewe and T.
;; Ritti; Ritti, Grewe and Kessener, "A relief of a water-powered stone saw
;; mill on a sarcophagus at Hierapolis and its implications", Journal of
;; Roman Archaeology 20 (2007), 138-163. The relief shows a water wheel fed
;; by a race, a gear train, and two frame saws worked by connecting rods,
;; which need cranks, though the cranks themselves are not carved. Here the
;; crank sits on the wheel's own axle (no gears) and drives one saw; the
;; saw's teeth are left out, and the frame's friction on the stone stands in
;; for the cutting.
;;
;; The wheel: overshot, 1 m to the buckets, fed 2.9 L/s. In steady running
;; it gives rho g Q r (1 - cos 120) = 1000 x 9.81 x 0.0029 x 1 x 1.5 =
;; 42.67 W whatever its speed (water-wheels.rkt), and the speed settles
;; where the saw takes that much.
;;
;; The crank: 0.25 m, so a 0.5 m stroke, on the wheel's axle; a 1 m oak rod
;; to a 60 x 8 x 8 cm iron frame (29.57 kg) that slides on a limestone block,
;; held level by a guide (6dof, free in x and y). Jolt in Godot takes the
;; smaller of two surfaces' friction (the inclined plane relies on it: oak,
;; 0.45, slides on limestone at tan 25 = 0.47), so iron on limestone is 0.4.
;; Carrying its weight and half the rod's, the frame drags
;; D = 0.4 x 30.14 kg x 9.81 = 118.3 N; it travels four crank radii a turn,
;; so it takes a mean 2 D r / pi = 18.83 N m. But the rod leans as the pin
;; goes round: the wheel turns the crank clockwise (seen from +z), so the
;; rod pushes the frame out while the pin is above it and pulls it back
;; while the pin is below, and either way presses the frame onto the stone
;; (D tan phi). With that, and the force that swings the frame back and
;; forth, worked through the turn (the test's saw-power), the mean torque
;; is 20.49 N m, at any speed: the wheel settles at 42.67 / 20.49 =
;; 2.083 rad/s, 19.89 rpm -- as many strokes a minute each way, a peak blade
;; speed of w r = 0.52 m/s and a mean of 2 w r / pi = 0.332 m/s, the frame
;; taking D x 0.332 m/s on average and the rod's lean the rest. (Turned the
;; other way the rod would lift the frame, and the same water would run it
;; at 23.4 rpm.)
;;
;; First worked with sqrt(0.4 x 0.6) = 0.49 for the friction, which
;; predicted 17.6 rpm at this flow; the trace ran 22% faster, and the
;; inclined plane showed why.
(require racket/math)

(define axle-y (m 1.5))
(define crank-z (cm -60))                    ; the crank, beyond the wheel's near post
(define rod-z (cm -70))                      ; the rod, in front of the crank disc
(define saw-z (cm -88))                      ; the frame and its stone, in front of the rod
(define r (cm 25))
(define l (m 1))
(define frame-length (cm 60))
(define wrist-x (+ r l))                     ; the pin starts pointing +x: the frame at its far dead centre

(define-machine hierapolis-sawmill
  #:source "Hierapolis sawmill relief, sarcophagus of M. Aurelius Ammianus (3rd c. AD); Ritti, Grewe & Kessener, JRA 20 (2007)"
  ;; the race: a spring into a header tank, and a chute onto the top of the wheel
  (post header-pier #:at ((m -1.7) 0 0) #:size ((cm 60) (m 2.6) (cm 60)) #:material limestone)
  (inflow spring #:into header #:flow (L/s 2.9))
  (tank header #:at ((m -1.7) (m 2.6) 0) #:area 0.36 #:height (cm 40) #:water (L 40) #:material limestone
        (port race #:height (cm 10)))
  (channel race #:from header.race #:to off #:end ((m -0.25) (m 2.65) 0) #:width (cm 30) #:onto wheel)
  (waterwheel wheel #:at (0 axle-y 0) #:radius (m 1) #:width (cm 40) #:mass 200 #:load 0
              #:buckets 24 #:bucket-volume (L 10) #:tail tail-pool)
  (tank tail-pool #:at ((m -1.2) 0 (m 1.4)) #:area 2 #:height (cm 50) #:water (L 300) #:material limestone
        (port tail #:height (cm 20)))
  (channel tailrace #:from tail-pool.tail #:to off #:end ((m -3) (cm 5) (m 1.4)) #:width (cm 30))

  ;; the crank on the wheel's axle, the rod, and the saw frame on its stone
  (wheel crank #:shape (disc-wheel #:radius (cm 30) #:width (cm 8)) #:at (0 axle-y crank-z) #:material iron)
  (arbor wheel crank)
  (block rod #:at ((+ r (/ l 2)) axle-y rod-z) #:size (cm 4) #:dimensions (l (cm 4) (cm 4)) #:material oak)
  (block saw-frame #:at ((+ wrist-x (/ frame-length 2)) axle-y saw-z) #:size (cm 8)
         #:dimensions (frame-length (cm 8) (cm 8)) #:material iron)
  (post stone #:at ((m 1.5) 0 saw-z) #:size ((m 1.2) (- axle-y (cm 4)) (cm 12)) #:material limestone)
  (joint crank-pin #:kind ball #:a crank #:b rod #:at (r axle-y rod-z))
  (joint wrist-pin #:kind pin #:a rod #:b saw-frame #:at (wrist-x axle-y (cm -78)) #:axis (0 0 1))
  (joint guide #:kind 6dof #:a saw-frame #:b world #:at ((+ wrist-x (/ frame-length 2)) axle-y saw-z) #:free (x y)))
