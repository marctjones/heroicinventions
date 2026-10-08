#lang heroic
;; Found electrics, end to end (issue #64): generator-train's windmill, geared 125:1, turns a salvaged motor that charges a
;; battery bank in a vault, and the bank, full and warm, makes the call on the 03:00 relay pass. The windmill, the three 5:1
;; meshes at 0.97 and the rotor are generator-train's (see its header for the sails: tau(w) = tau* (2 - w R / (v lambda*)),
;; tau* = 255.34 N.m, 3 m/s, 5 m); the millstone's 2 N.m is gone, and in its place a generator part loads the rotor.
;;
;; The generator   Cut-in 1,500 rpm (157.08 rad/s), rated 2,500 rpm (261.80) at 12 N.m, eta 0.8:
;;   tau_g(w) = k (w - w_cut) above the cut-in, k = 12 / (261.80 - 157.08) = 0.114592 N.m per rad/s (a permanent-magnet
;;   machine into a battery of fixed voltage, current (K w - V)/R: see Electrics/Generator.cs), and 0 below. It charges the bank
;;   with P = eta tau w.
;; Where the windmill settles   The sails' torque meets the generator's, seen through the train (x 125, over 0.97^3 = 0.912673):
;;   255.34 (2 - w / 1.5) = 125 k (125 w - 157.08) / 0.912673,   so   w = 1.39583 rad/s (13.33 rpm),
;;   the rotor at 125 w = 174.479 rad/s (1,666.1 rpm), over the cut-in by 17.40 rad/s, and
;;   tau_g = 0.114592 x 17.40 = 1.9938 N.m (the 2 N.m load of generator-train, nearly), reflected to the sails as 273.07 N.m.
;;   Unloaded the same sails run free at lambda 5, w = 3.0 rad/s (28.6 rpm); the generator slows them by 1.604 rad/s (53%):
;;   the windmill's speed drops by the load torque over the slope, the reflected 273.07 N.m over 255.34 / 1.5 + 125^2 k / 0.912673.
;; Power   shaft power into the generator tau_g w = 347.87 W, of the sails' 381.16 W (273.07 x 1.39583, Cp 0.2986 of the wind's 1,276.7 W):
;;   0.97^3 = 91.27% arrives (three meshes), and the bank is charged with 0.8 x 347.87 = 278.30 W (9.94 A at the pack's nominal 28 V).
;;   Gearing it only 25:1 (two stages) would turn the rotor at 25 x 3.0 = 75 rad/s (716 rpm) free, under the cut-in: nothing. The ratio
;;   needed to reach 1,500 rpm from the loaded sails' 13.33 rpm is 112.5:1; from the free sails' 28.6 rpm, 52.4:1. Three 5:1 stages (125) clear it.
;; The bank   10 Wh (36,000 J: a scenario number, small so a run can fill it) in a vault at 20 C, taking charge from 0 to 45 C. It fills in
;;   36,000 / 278.30 = 129.4 s of charging. The sails reach the cut-in's w = 157.08/125 = 1.2566 rad/s from rest in about
;;   -19.2 s x ln(1 - 1.2566/3) = 10.4 s, so it is full at about 140 s (traced 143 s).
;; The call   The run starts at 02:55 on Earth's 3,600 s hour: 03:00 is 300 s in. The bank is full from about 140 s and warm all the way,
;;   but the pass opens at 03:00: not won at 02:59 (240 s), won by 305 s. The window is 10 minutes (to 03:10).
(require racket/math)

(define md (mm 5))
(define wheel-100 (spur-gear #:teeth 100 #:module md #:width (cm 2)))
(define pinion-20 (spur-gear #:teeth 20 #:module md #:width (cm 3)))
(define apart (* md (+ 50 10)))
(define pinion-angle (radians->degrees (mate-angle 100 0 20 0)))
(define rotor (disc-wheel #:radius (cm 10) #:width (cm 8)))

(define hub-y (m 6))
(define eta 0.97)

(define-machine found-electrics
  #:source "issue #64: a windmill geared 125:1 turns a salvaged motor that charges the found battery bank; the call on the 03:00 pass wins"
  #:time (/ 175 60.0)
  (post trestle #:at ((cm 45) 0 (cm -145)) #:size ((m 0.8) (m 5.6) (m 0.8)) #:material oak #:round #t)
  (windmill sails #:at (0 hub-y 0) #:radius (m 5) #:mass 200 #:wind 3 #:cp 0.3 #:tip-speed-ratio 2.5 #:material oak)
  (wheel wheel-a #:shape wheel-100 #:at (0 hub-y (cm -130)) #:material oak)
  (wheel pinion-b #:shape pinion-20 #:at (apart hub-y (cm -130)) #:material iron #:angle-deg pinion-angle)
  (wheel wheel-b #:shape wheel-100 #:at (apart hub-y (cm -140)) #:material oak)
  (wheel pinion-c #:shape pinion-20 #:at ((* 2 apart) hub-y (cm -140)) #:material iron #:angle-deg pinion-angle)
  (wheel wheel-c #:shape wheel-100 #:at ((* 2 apart) hub-y (cm -150)) #:material oak)
  (wheel pinion-d #:shape pinion-20 #:at ((* 3 apart) hub-y (cm -150)) #:material iron #:angle-deg pinion-angle)
  ;; the generator's rotor: an iron disc on the last pinion's arbor; the motor can sits behind it
  (wheel rotor-disc #:shape rotor #:at ((* 3 apart) hub-y (cm -162)) #:material iron)
  (arbor sails wheel-a)
  (arbor pinion-b wheel-b)
  (arbor pinion-c wheel-c)
  (arbor pinion-d rotor-disc)
  (mesh wheel-a pinion-b #:efficiency eta)
  (mesh wheel-b pinion-c #:efficiency eta)
  (mesh wheel-c pinion-d #:efficiency eta)
  (generator motor #:at ((* 3 apart) hub-y (cm -185)) #:on rotor-disc #:charges bank)
  ;; the vault: the bank's cells, a heat store at 20 C, in a small room, on a shelf beside the motor (the wire is implicit)
  (post shelf #:at ((m 3.0) 0 (m -1.6)) #:size ((m 1.6) (m 5.4) (m 0.9)) #:material oak)
  (enclosure vault #:at ((m 3.0) (m 5.4) (m -1.6)) #:size ((m 1.5) (m 0.8) (m 0.8)) #:temperature 20)
  (heat-store cells #:at ((m 2.5) (m 5.4) (m -1.6)) #:mass 16 #:contents cells #:temperature 20)
  (battery-bank bank #:at ((m 3.3) (m 5.4) (m -1.6)) #:in cells #:capacity 10 #:charge 0))
