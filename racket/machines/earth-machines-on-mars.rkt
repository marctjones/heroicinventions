#lang heroic
;; Earth's machines on Mars (issue #38). The same parts, the same formulas,
;; Mars's numbers: g = 3.71 m/s², 610 Pa of air that is 95% CO2 (a mean
;; molar mass of 43.49 g/mol), -63 °C. Each machine fails, or changes, for
;; a reason worked out here before it ran.
;;
;; clock   the iron pendulum of pendulum-demo, 60 cm from 40°, swinging in
;;         Jolt. A pendulum's period goes as 1/√g, whatever its shape, so it
;;         swings √(9.81/3.71) = 1.6261 times as slowly as on Earth. (A
;;         simple 1 m pendulum: 2π√(1/3.71) = 3.262 s, against 2.006 s.)
;; pivot   the frictionless 1 m pendulum of bearing-friction, swung by the
;;         sim: I = 17.36 kg m², m = 18.9 kg, its centre d = 0.936 m down.
;;         Its period 2π√(I/(m g d)) × (1 + θ²/16) from 15° is 1.996 s on
;;         Earth and 3.245 s here.
;; pump    a lift pump whose barrel stands 1 m over a well. The atmosphere
;;         pushes water up a pipe only to (P - P_v)/(ρ g) over the surface:
;;         10.09 m on Earth, but here the air's 610 Pa is barely more than
;;         the vapour pressure of water just above freezing (605.6 Pa at
;;         0 °C by the Antoine equation the sim uses), so the limit is
;;         (610 - 605.6)/(1000 × 3.71) = 1.2 mm. It lifts nothing.
;; mill    a windmill, sails 5 m from hub to tip, in a 10 m/s wind. The wind
;;         carries ½ρAv³ through the sails: air here is
;;         610 × 0.04349 / (8.314 × 210.15) = 0.01518 kg/m³, against Earth's
;;         1.2041 at 20 °C, so 596 W instead of 47.3 kW: 1/79.3 as much.
;; kettle  a pot of 1 kg of water on a 500 W fire. Water boils where its
;;         vapour pressure reaches the air's: at 610 Pa, 0.0995 °C (Antoine).
;;         It starts at 0 °C (water in a -63 °C frost is at best just
;;         thawed) and loses 2 W/K to the air, so it warms at
;;         (500 - 126) / 4186 = 0.089 K/s and is boiling -- its pressure
;;         above the air's -- after about 1.1 s.
;; Drawn (#176, view only): beside the pump's pipe an amber dimension line from the well's surface up to the
;; barrel's foot, "needs 0.90 m", and at the well's surface a red tick named "reach 1.2 mm": the empty column
;; (drawn pale) stands against the line it fell short of by 0.9 m less 1.2 mm.
(define-machine earth-machines-on-mars
  #:source "Earth's machines under Mars's numbers: gravity 3.71, 610 Pa of CO2, -63 °C"
  #:planet mars
  (pendulum clock #:at ((m -3) (m 0.9) 0) #:length (cm 60) #:material iron #:start-angle-deg 40)
  (pendulum pivot #:at ((m -1.5) (m 1.3) 0) #:length (m 1) #:material iron #:start-angle-deg 15
            #:bearing-radius (cm 1.5))
  (tank well #:at (0 0 0) #:area (m2 0.5) #:height (m 1) #:water (L 400) #:material limestone)
  (tank trough #:at ((m 1) 0 0) #:area (m2 0.25) #:height (m 0.4) #:material oak)
  (pump pump #:at ((m 0.5) (m 1.8) 0) #:from well #:to trough #:bore (cm 15) #:stroke (cm 50) #:rpm 20)
  (post mill-trestle #:at ((m 7) 0 (m -5)) #:size ((m 0.8) (m 6) (m 0.8)) #:material oak #:round #t)
  (windmill mill #:at ((m 7) (m 6.5) (m -4)) #:radius (m 5) #:mass 300 #:wind 10 #:material oak)
  (post hob #:at ((m 2.5) 0 (m 2)) #:size ((m 0.5) (m 0.4) (m 0.5)) #:material limestone)
  (boiler kettle #:at ((m 2.5) (m 0.4) (m 2)) #:radius (m 0.08) #:height (m 0.1) #:water (kg 1) #:fire 500 #:material bronze))
