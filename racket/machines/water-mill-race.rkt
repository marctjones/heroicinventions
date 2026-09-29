#lang heroic
;; A mill race: a spring fills a header tank on a stone pier, spills over
;; its lip into a winding stone brook (three bends, #:via) that drops into a
;; millpond, and the pond's tail weir carries what is left away. In the
;; pond stands a small noria on its own axle posts; the brook's current on
;; its paddles turns it, and its buckets lift water to a flume on a pier.
;;
;; Nothing here sets a speed or a level. Working through it by hand for
;; the 8 L/s spring:
;;   header   weir h = (Q / 1.705 b)^(2/3) = 6.25 cm over the 30 cm lip
;;   brook    10.0 m long (path through the bends less the tanks' half-walls),
;;            falls 0.80 m, S = 0.0799; Manning, n = 0.015, 30 cm wide:
;;            2.05 cm deep, running 1.30 m/s
;;   wheel    drag on the paddles 1/2 rho Cd A (v - u)^2 against the lift
;;            torque rho g H V / 2 pi (V = 91.8 L a turn, H = 0.79 m) balance
;;            at 3.0 rpm, lifting 4.6 L/s
;;   pond     the other 3.4 L/s leaves over the tail weir, which holds the
;;            pond 42.9 cm deep (lip 40 cm + 2.9 cm)
(define pond-x (m 10))
(define wheel-y (cm 72))

(define-machine water-mill-race
  #:source "A small overshot-fed mill race with a water-lifting wheel"
  (post header-pier #:at (0 0 0) #:size ((cm 70) (m 1) (cm 70)) #:material limestone)
  (inflow spring #:into header #:flow (L/s 8))
  (tank header #:at (0 (m 1) 0) #:area 0.5 #:height (cm 50) #:water (L 181)
        (port spill #:height (cm 30)))
  (tank pond #:at (pond-x 0 0) #:area 9 #:height (cm 60) #:water (L 3863)
        (port inlet #:height (cm 50))
        (port tail #:height (cm 40)))
  (channel brook #:from header.spill #:to pond.inlet
           #:via (((m 3) 1.5) ((m 6) -1.5))
           #:width (cm 30))
  (channel tailrace #:from pond.tail #:to off #:end ((m 14) (cm 30) 0) #:width (cm 40))
  ;; the wheel stands in the pond; its axle posts are built for it
  (wheel mill-wheel #:shape (noria #:radius (cm 50) #:width (cm 40) #:buckets 12
                                   #:bucket-depth (cm 12) #:paddle-depth (cm 10))
         #:at (pond-x wheel-y 0) #:material oak)
  (post flume-pier #:at ((- pond-x (cm 60)) 0 (cm -45)) #:size ((cm 40) (m 1) (cm 40)) #:material limestone)
  (tank flume #:at ((- pond-x (cm 60)) (m 1) (cm -45)) #:area 0.25 #:height (cm 30) #:water (L 10.8)
        (port spill #:height 0))
  (channel flume-run #:from flume.spill #:to off #:end ((- pond-x (m 1.6)) (cm 90) (cm -45)) #:width (cm 30))
  (lift raise #:by mill-wheel #:from pond #:to flume #:current-from brook))
