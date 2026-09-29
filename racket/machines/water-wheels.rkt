#lang heroic
;; Two water wheels, each grinding against a millstone.
;;
;; Overshot (left). A 20 L/s spring fills a header on a pier; its race
;; pours onto the top of a 3 m wheel whose buckets carry the water down the
;; far side until they have turned 120 degrees and tip it out. Water spread
;; over that arc turns the wheel with its weight, and in steady running the
;; wheel gives rho g Q r (1 - cos 120) = 1000 x 9.81 x 0.020 x 1.5 x 1.5 =
;; 441 W whatever its speed. Against a 300 N m millstone that is
;;   omega = 441.5 / 300 = 1.47 rad/s, 14.1 rpm,
;; with 28.5 kg of water on the wheel (rho Q theta / omega). The race's lip
;; stands 3.1 m above the wheel's bottom, so of rho g Q H = 608 W the
;; millstone gets 72.6% - Smeaton measured 63-78% on his overshot models.
;; Its 24 buckets of 10 L hold at most 80 kg on the loaded arc, which turns
;; 562 N m: load it past that and it stalls, buckets brimming.
;;
;; Undershot (right). A 150 L/s river spills from a mill pond down a steep
;; stone race; a wheel stands in it with 30 cm paddles. The current v pushes
;; the paddles with rho A (v - u)^2, u their own speed, so against a load
;; tau they settle at u = v - sqrt(tau / (rho A r)); most power, at
;; u = v/3, is 8/27 of the kinetic energy the race carries through them.
(define-machine water-wheels
  #:source "An overshot and an undershot water wheel, each driving a millstone"
  ;; overshot
  (post header-pier #:at ((m -1) 0 0) #:size ((cm 70) (m 3.2) (cm 70)) #:material limestone)
  (inflow spring #:into header #:flow (L/s 20))
  (tank header #:at ((m -1) (m 3.2) 0) #:area 0.5 #:height (cm 40) #:water (L 50) #:material limestone
        (port race #:height (cm 10)))
  (channel race #:from header.race #:to off #:end ((cm -25) (m 3.25) 0) #:width (cm 30) #:onto overshot)
  (waterwheel overshot #:at (0 (m 1.7) 0) #:radius (m 1.5) #:width (cm 40) #:mass 200 #:load 300
              #:buckets 24 #:bucket-volume (L 10) #:tail tail-pool)
  (tank tail-pool #:at ((m 2.2) 0 0) #:area 2 #:height (cm 50) #:water (L 400) #:material limestone
        (port tail #:height (cm 20)))
  (channel tailrace #:from tail-pool.tail #:to off #:end ((m 4.5) (cm 5) 0) #:width (cm 30))

  ;; undershot
  (post pond-pier #:at ((m 7) 0 0) #:size ((m 2) (cm 50) (m 2)) #:material limestone)
  (inflow river #:into mill-pond #:flow (L/s 150))
  (tank mill-pond #:at ((m 7) (cm 50) 0) #:area 4 #:height (cm 60) #:water (L 1600) #:material limestone
        (port head #:height (cm 30)))
  (channel mill-race #:from mill-pond.head #:to lower-pool.inlet #:width (cm 60))
  (waterwheel undershot #:at ((m 10.5) (m 1.45) 0) #:radius (m 1) #:width (cm 60) #:mass 150 #:load 40
              #:race mill-race #:paddle-depth (cm 30))
  (tank lower-pool #:at ((m 14) 0 0) #:area 4 #:height (cm 50) #:water (L 1400) #:material limestone
        (port inlet #:height (cm 30))
        (port tail #:height (cm 20)))
  (channel outfall #:from lower-pool.tail #:to off #:end ((m 17) (cm 5) 0) #:width (cm 60)))
