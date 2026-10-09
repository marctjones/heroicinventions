#lang heroic
;; A windmill geared 125:1 up to a generator's speed (issue #187, for #64): a
;; salvaged motor charges only above a cut-in of about 1,500 rpm (157 rad/s),
;; and a windmill turns at about 12. Three 5:1 stages, each a 100-tooth oak
;; wheel driving a 20-tooth iron pinion through a mesh that keeps 0.97 of
;; what it carries, step 13 rpm up to 1,660. The generator's rotor, an iron
;; disc on the last pinion's arbor, is loaded like a millstone, a steady
;; 2 N.m (#64 will put a real generator there, P = tau w eta).
;;
;; The sails: 5 m from hub to tip (a disc of pi x 5^2 = 78.54 m2), 200 kg,
;; in a 3 m/s wind of air at 20 C (1.2041 kg/m3), taking at best Cp 0.3 of
;; it at a tip-speed ratio of 2.5. Their torque falls in a straight line
;; with their speed (Windmill.cs):
;;   tau(w) = tau* (2 - w R / (v lambda*)),
;;   tau* = 1/2 rho A v^2 R Cp / lambda* = 1/2 x 1.2041 x 78.54 x 9 x 5 x 0.3 / 2.5
;;        = 255.34 N.m (at lambda*, w = 2.5 x 3 / 5 = 1.5 rad/s, 14.3 rpm).
;; The train: speed multiplies by 5 at each stage and torque divides by it,
;; and each mesh asks 1/0.97 of what it passes on, so the 2 N.m at the
;; rotor is felt at the sails as
;;   125 x 2 / 0.97^3 = 250 / 0.91267 = 273.92 N.m,
;; and between the stages as 273.92 x 0.97 / 5 = 53.14 N.m on the first
;; pinion, 53.14 x 0.97 / 5 = 10.31 on the second, and 2.00 on the third.
;; The sails settle where they give that:
;;   2 - w R / (v lambda*) = 273.92 / 255.34 = 1.07276,
;;   w = 0.92724 x 2.5 x 3 / 5 = 1.39086 rad/s, 13.28 rpm, so the rotor turns
;;   125 x 1.39086 = 173.86 rad/s, 1,660 rpm: above the cut-in.
;; The torque balance: the sails take Cp = 0.3 x 0.92724 x (2 - 0.92724) =
;; 0.2984 of the wind's 1/2 rho A v^3 = 1,276.7 W, 381.0 W (273.92 x
;; 1.39086); the rotor gets 2 x 173.86 = 347.7 W of it, 0.97^3 = 91.3%.
;; The train seen from the sails is the sails' 200 x 5^2 / 3 = 1,667 kg.m2
;; and the gears' 1,596 (25, 625 and 15,625 times theirs: the rotor's 0.0962
;; is 1,503 of it), and the sails' torque falls 255.34 x 5 / 7.5 = 170 N.m
;; per rad/s: a time constant of 3,263 / 170 = 19 s, up to speed within a
;; part in 10^4 after about 180 s.
;;
;; The meshes are solved together each tick (ShaftLink.StepAll): link by
;; link, the sails' pull reached the far pinions a few percent a sweep, and
;; the first pinion ran at 0.4% of its ratio. The rotor's speed is traced at
;; the start of each physics step, before its load slows it within the step,
;; so it reads 2 N.m x 1/120 s / its 0.098 kg.m2 = 0.17 rad/s (0.1%) over
;; 125 x the sails.
;;
;; Beside it, a pair let go at speed with nothing to slow it: a 100-tooth oak
;; wheel at 300 rpm (I = 0.08774 kg.m2) meshing a 20-tooth iron pinion at
;; 1,500 rpm (I = 0.002211). Its energy, 1/2 x 0.08774 x 31.416^2 + 1/2 x
;; 0.002211 x 157.08^2 = 43.30 + 27.28 = 70.57 J, should stay put.
(require racket/math)

(define md (mm 5))                                     ; module: a 100-tooth wheel's pitch radius is 25 cm, a 20-tooth pinion's 5 cm
(define wheel-100 (spur-gear #:teeth 100 #:module md #:width (cm 2)))
(define pinion-20 (spur-gear #:teeth 20 #:module md #:width (cm 3)))
(define apart (* md (+ 50 10)))                        ; 0.30 m between meshed axles
(define pinion-angle (radians->degrees (mate-angle 100 0 20 0)))
(define rotor (disc-wheel #:radius (cm 10) #:width (cm 8)))

(define hub-y (m 6))
(define eta 0.97)

(define-machine generator-train
  #:source "issue #187: a windmill geared 125:1 up to a generator's 1,500 rpm (#64)"
  (post trestle #:at ((cm 45) 0 (cm -145)) #:size ((m 0.8) (m 5.6) (m 0.8)) #:material oak #:round #t)   ; under the train
  (windmill sails #:at (0 hub-y 0) #:radius (m 5) #:mass 200 #:wind 3 #:cp 0.3 #:tip-speed-ratio 2.5 #:material oak)
  ;; stage 1: the windshaft's 100-tooth wheel into a 20-tooth pinion
  (wheel wheel-a #:shape wheel-100 #:at (0 hub-y (cm -130)) #:material oak)
  (wheel pinion-b #:shape pinion-20 #:at (apart hub-y (cm -130)) #:material iron #:angle-deg pinion-angle)
  ;; stage 2
  (wheel wheel-b #:shape wheel-100 #:at (apart hub-y (cm -140)) #:material oak)
  (wheel pinion-c #:shape pinion-20 #:at ((* 2 apart) hub-y (cm -140)) #:material iron #:angle-deg pinion-angle)
  ;; stage 3
  (wheel wheel-c #:shape wheel-100 #:at ((* 2 apart) hub-y (cm -150)) #:material oak)
  (wheel pinion-d #:shape pinion-20 #:at ((* 3 apart) hub-y (cm -150)) #:material iron #:angle-deg pinion-angle)
  ;; the generator's rotor, loaded like a millstone
  (wheel generator #:shape rotor #:at ((* 3 apart) hub-y (cm -162)) #:material iron #:grind-torque 2)
  (arbor sails wheel-a)
  (arbor pinion-b wheel-b)
  (arbor pinion-c wheel-c)
  (arbor pinion-d generator)
  (mesh wheel-a pinion-b #:efficiency eta)
  (mesh wheel-b pinion-c #:efficiency eta)
  (mesh wheel-c pinion-d #:efficiency eta)

  ;; a free pair let go at speed, nothing to slow it: the energy check
  (wheel free-wheel #:shape wheel-100 #:at ((m 3) (m 1) 0) #:material oak #:start-rpm 300)
  (wheel free-pinion #:shape pinion-20 #:at ((+ (m 3) apart) (m 1) 0) #:material iron #:angle-deg pinion-angle #:start-rpm -1500)
  (mesh free-wheel free-pinion)

  ;; a sleep (#59, #207) that lets the physics engine run: the train settles within a part in 10^4 by 180 s (header), so a sleep to
  ;; 200 s is the same as watching to 200 s (sleep-test.rkt compares them)
  (wake settled #:when ((scene elapsed above 200)) #:limit 600))
