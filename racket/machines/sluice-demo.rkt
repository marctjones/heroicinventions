#lang heroic
;; A sluice gate. A 20 L/s spring fills a head pool on a stone pier; the
;; pool's water leaves down a stone race whose head is shut by a wooden
;; gate 1 m tall, raised a few centimetres. Below the race lies a reach (a
;; short pound on a lower pier) that drains over its floor-level outfall
;; into a pond, which spills away over a tail weir. If the gate can't pass
;; the spring, the pool rises to its waste weir and the rest spills there.
;;
;; Nothing here sets a flow or a level. Working through it by hand:
;;   gate     slot a = opening x 1 m, 30 cm wide; Q = 0.6 w a sqrt(2 g h), h
;;            the pool's surface above the slot's middle. All 20 L/s must go
;;            under it, so h = (Q / (0.6 w a))^2 / 2g:
;;              opening 0.08   h =  9.83 cm   (pool 13.8 cm over the sill)
;;              opening 0.05   h = 25.17 cm   (pool 27.7 cm over the sill)
;;              opening 0.03   h = 69.91 cm   (pool 71.4 cm over the sill)
;;            In each the pool stands above the plate's lower edge and the
;;            orifice passes less than the open lip would as a weir, so the
;;            gate, not the lip, holds the water back.
;;   backs up opening 0.02 would need h = 1.57 m, above the waste weir
;;            (80 cm over the sill), so the pool rises until the gate and
;;            the waste weir share the spring: 0.0036 sqrt(2g (s - 0.01)) +
;;            1.705 (0.3) (s - 0.80)^1.5 = 0.020 gives s = 84.8 cm, the gate
;;            passing 14.6 L/s and the waste 5.4 L/s.
;;   reach    20 L/s over its 30 cm floor-level outfall stands 11.52 cm deep
;;            ((Q / 1.705 b)^(2/3)). Shut the gate and the race runs dry at
;;            once (a channel holds no water of its own); the reach, 1 m2,
;;            drains as dh/dt = -(1.705 b / A) h^1.5, so
;;            1/sqrt(h) = 1/sqrt(h0) + 0.2558 t: 3.30 cm at 10 s, 0.89 cm
;;            at 30 s, 1 mm at 112 s, its outfall falling from 20 L/s to
;;            0.43 L/s by 30 s.
(define-machine sluice-demo
  #:source "A sluice gate on a mill race, with the reach it feeds"
  (post pool-pier #:at (0 0 0) #:size ((cm 75) (cm 60) (cm 75)) #:material limestone)
  (inflow spring #:into pool #:flow (L/s 20))
  (tank pool #:at (0 (cm 60) 0) #:area 0.5 #:height (m 1.2) #:water (L 100) #:material limestone
        (port race-head #:height (cm 10))
        (port waste #:height (cm 90)))
  (channel race #:from pool.race-head #:to reach.inlet #:width (cm 30))
  (sluice gate #:on race #:height (m 1) #:opening 0.05)
  (channel waste-weir #:from pool.waste #:to off #:end (0 (cm 120) (m -3)) #:width (cm 30))
  (post reach-pier #:at ((m 4) 0 0) #:size ((m 1) (cm 30) (m 1)) #:material limestone)
  (tank reach #:at ((m 4) (cm 30) 0) #:area 1 #:height (cm 40) #:water (L 115) #:material limestone
        (port inlet #:height (cm 35))
        (port outfall #:height 0))
  (tank pond #:at ((m 7.5) 0 0) #:area 2 #:height (cm 40) #:water (L 430) #:material limestone
        (port inlet #:height (cm 25))
        (port tail #:height (cm 10)))
  (channel run #:from reach.outfall #:to pond.inlet #:width (cm 30))
  (channel tailrace #:from pond.tail #:to off #:end ((m 10.5) (cm 5) 0) #:width (cm 30)))
