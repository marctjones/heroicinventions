#lang heroic
;; A gallery of the basic components, one bench each, left to right. Each
;; is the simplest honest use of its part; nothing is scripted, so each
;; does what its numbers say.
;;
;;   1. tank + pipe + post   two vessels joined by a pipe: the water finds
;;                           its level (120 L settles with the low tank's
;;                           surface level with the high one's, which stands on a 60 cm post pier
;;   2. post + block         a trilithon: two columns and a lintel bear a
;;                           granite block; posts are ground that doesn't move
;;   3. hearth + boiler + rotor   a fire of 50 g of wood burns down and
;;                           goes out; only 25% of its heat reaches the water
;;   4. inflow + channel     a spring fills a header tank on a pier, whose
;;                           overflow winds round three bends to a pond (#:via), which spills off the scene
;;   5. pendulum, lever, ramp, block   pure Jolt rigid-body physics

;; --- 1. a pier, two tanks, a pipe ------------------------------------------
(define x1 (m -9))

;; --- 3. a hearth heating a boiler ------------------------------------------
(define x3 (m -1))
(define sphere-radius (cm 6))

;; --- 4. a winding stream ----------------------------------------------------
(define x4 (m 3))

;; --- 5. rigid bodies --------------------------------------------------------
(define x5 (m 13))

(define-machine component-gallery
  #:source "Heroic Inventions: the basic components, one bench each"

  ;; 1 ----
  (post pier #:at (x1 0 0) #:size ((cm 40) (cm 60) (cm 40)) #:material limestone)
  (tank upper #:at (x1 (cm 60) 0) #:area 0.1 #:height (cm 50) #:water (L 100)
        (port outlet #:height 0))
  (tank lower #:at ((+ x1 (m 1.3)) 0 0) #:area 0.1 #:height (m 1.2) #:water (L 20)
        (port inlet #:height 0))
  (pipe drain upper.outlet lower.inlet #:conductance 4e-4)

  ;; 2 ----
  (post column-a #:at ((m -5.4) 0 0) #:size ((cm 30) (m 1) (cm 30)) #:material limestone #:round #t)
  (post column-b #:at ((m -4.2) 0 0) #:size ((cm 30) (m 1) (cm 30)) #:material limestone #:round #t)
  (block lintel #:at ((m -4.8) (+ (m 1) (cm 7.5) (mm 1)) 0) #:size (cm 10) #:dimensions ((m 1.6) (cm 15) (cm 40))
         #:material limestone)
  (block load #:at ((m -4.8) (+ (m 1) (cm 15) (cm 15) (mm 3)) 0) #:size (cm 30) #:material granite)

  ;; 3 ----
  (boiler kettle #:at (x3 (cm 8) 0) #:radius (cm 12) #:height (cm 16)
          #:water (kg 0.3) #:material bronze)
  (hearth fire #:at (x3 0 0) #:heats kettle #:power (W 3000) #:fuel (g 50) #:fuel-kind wood #:efficiency 0.25)
  (rotor ball #:at (x3 (cm 45) 0) #:radius sphere-radius #:wall (mm 1)
         #:material bronze #:nozzles 2 #:bore (mm 2) #:arm (+ sphere-radius (cm 2)))
  (connect kettle.steam ball.steam-in)

  ;; 4 ----
  (post header-pier #:at (x4 0 0) #:size ((cm 70) (m 1) (cm 70)) #:material limestone)
  (inflow spring #:into header #:flow (L/s 3))
  (tank header #:at (x4 (m 1) 0) #:area 0.5 #:height (cm 50) #:water (L 100)
        (port spill #:height (cm 30)))
  (tank pond #:at ((+ x4 (m 8)) 0 0) #:area 1 #:height (cm 80) #:water (L 300)
        (port inlet #:height (cm 50))
        (port spill #:height (cm 60)))
  (channel brook #:from header.spill #:to pond.inlet
           #:via (((+ x4 (m 2)) 2) ((+ x4 (m 4)) -1.5) ((+ x4 (m 6)) 1))
           #:width (cm 30))
  (channel tailrace #:from pond.spill #:to off #:end ((+ x4 (m 10)) (cm 30) 0) #:width (cm 30))

  ;; 5 ----
  (pendulum bob #:at (x5 (m 1.2) 0) #:length (cm 70) #:material iron #:start-angle-deg 35)
  (lever seesaw #:at ((+ x5 (m 2.5)) (cm 60) 0) #:length (m 1.2) #:material oak #:limit-deg 10)
  (post fulcrum #:at ((+ x5 (m 2.5)) 0 0) #:size ((cm 10) (cm 58) (cm 10)) #:material oak)
  (block heavy #:at ((+ x5 (m 2.5) (cm -50)) (+ (cm 60) (cm 2) (cm 7) (mm 1)) 0) #:size (cm 14) #:material granite)
  (block light #:at ((+ x5 (m 2.5) (cm 50)) (+ (cm 60) (cm 2) (cm 7) (mm 1)) 0) #:size (cm 14) #:material cedar)
  (ramp slope #:at ((+ x5 (m 5)) 0 (cm 40)) #:length (m 1.2) #:width (m 0.8) #:angle-deg 20 #:material limestone))
