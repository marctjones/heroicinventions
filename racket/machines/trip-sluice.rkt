#lang heroic
;; A tripwire (issue #32). A 10 cm iron weight hangs in the air 1.5 m up,
;; over a stone catch. Below it, 95 cm up, is a trigger: a box 10 cm deep
;; that fires, once, when the weight's middle enters it. What it does is
;; open a sluice that was shut: a pool of 300 L stands behind the gate,
;; nothing flows until the weight falls, and then the pool runs down the
;; race into the reach.
;;
;; Working it through:
;;   fall    the weight's middle drops from 1.5 m to the trigger's top face,
;;           0.95 m: h = 0.55 m, so t = sqrt(2 h / g) = sqrt(1.1 / 9.81)
;;           = 0.3349 s. (The engine's default damping of 0.1 per second,
;;           which stretched that to 0.3368 s, went with #33.)
;;           The physics steps at 120 Hz, so it fires within a tick (8.3 ms)
;;           of either.
;;   effect  at that instant the gate's opening goes from 0 to 0.05, and
;;           not before: the reach holds no water until then. With the gate
;;           at 0.05 and the pool 0.6 m over the race, the slot passes
;;           Q = 0.6 w a sqrt(2 g h) = 0.6 (0.3)(0.05) sqrt(2 (9.81)(0.6))
;;           about 30 L/s at first.
(define-machine trip-sluice
  #:source "A tripwire: a falling weight opens a sluice"
  (post pool-pier #:at (0 0 0) #:size ((cm 75) (cm 60) (cm 75)) #:material limestone)
  (tank pool #:at (0 (cm 60) 0) #:area 0.5 #:height (m 1.2) #:water (L 300) #:material limestone
        (port race-head #:height (cm 10)))
  (channel race #:from pool.race-head #:to reach.inlet #:width (cm 30))
  (sluice gate #:on race #:height (m 1) #:opening 0)
  (post reach-pier #:at ((m 4) 0 0) #:size ((m 1) (cm 30) (m 1)) #:material limestone)
  (tank reach #:at ((m 4) (cm 30) 0) #:area 1 #:height (cm 40) #:water 0 #:material limestone
        (port inlet #:height (cm 35)))
  (post catch #:at ((m 2) 0 0) #:size ((cm 40) (cm 30) (cm 40)) #:material limestone)
  (block weight #:at ((m 2) (m 1.5) 0) #:size (cm 10) #:material iron)
  (trigger tripwire #:at ((m 2) (cm 90) 0) #:size ((cm 50) (cm 10) (cm 50)) #:body weight
           #:do ((gate opening 0.05))))
