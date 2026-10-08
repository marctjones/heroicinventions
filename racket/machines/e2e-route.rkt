#lang heroic
;; The fastest route's finished works (issue #96): what the end-to-end test (racket/heroic/tests/e2e-route-test.rkt) stands in the crater
;; once the rover has done what the rover can. docs/e2e-route.md lists, step by step, which parts of the route the game lets a player do
;; and which this machine stands in for (GAPs 2 to 4).
;;
;; One machine, because a generator charges a bank in its own machine (links between machines are pipes and shafts only: GAP 6), and the
;; bank, its generator, the vault and the heat bin are the route's last steps:
;;   vault      a 0.5 m cavity (A = 1.5 m2) in a 0.5 m regolith wall at the ground's -55 C, a 16 kg bank (cells, 16 kJ/K) and 40 kg of
;;              basalt in a lidded bin that leaks 0.1 W/K, a bimetal strip on the bank working the lid: bimetal-night's "strip" vault
;;   rock       heated by a 1.5 m2 heliostat, whose image falls on the rock in the bin all day (the doc has the rover push rock heated
;;              in the open into the bin at dusk, which the game cannot do: GAP 4)
;;   windmill   field-windmill's sails (10 m, Cp 0.3 at lambda 2.5, Mars's air), 400 kg, taking the wind of the map under them, geared up
;;              125:1 by found-electrics' three 5:1 meshes at 0.97, turning a salvaged motor (cut-in 1,500 rpm, 12 N.m at 2,500 rpm, eta 0.8)
;;              that charges the bank through the implicit wire
;;   the call   the bank's own: full, between 0 and 45 C of its cells, in the 10 minutes from 03:00
;; The run starts at 07:00 on sol 1 (latitude -2, day 100: bimetal-night's sun). Its two wakes are for the sleep (#59): pre-dawn, 73,300 s
;; in (02:49), and the call.
;;
;; The scenario number (#60) is the bank's: 25 Wh, where the doc's illustration is 5 kWh. 25 Wh fills in minutes of the windmill's
;; charging; the doc's bank would need 5,000 Wh / 153 W = 33 hours of it. The tuning scales the timings, not the formulas.
;;
;; Worked before running (Mars's air 0.0151833 kg/m3, A = pi x 10^2 = 314.16 m2, R = 10 m, Cp 0.3, lambda* 2.5)
;;   sails     tau(w) = tau* (2 - w R / (v lambda*)), tau* = 1/2 rho A v^2 R Cp / lambda* = 2.8620 v^2 N.m (103.0 N.m at 6 m/s, 183.2 at 8)
;;   train     x 125, over 0.97^3 = 0.912673 seen from the sails; the generator's tau_g = k (w_r - w_cut), k = 12 / (261.80 - 157.08)
;;             = 0.114592 N.m per rad/s above w_cut = 157.08 rad/s, w_r = 125 w
;;   settle    tau*(2 - 10 w / (2.5 v)) = 125 k (125 w - 157.08) / 0.912673; at v = 6:  103.03 (2 - 0.66667 w) = 1961.8 w - 2465.3,
;;             w = 1.3156 rad/s (12.56 rpm), the rotor at 164.45 rad/s (1,570 rpm), 7.37 rad/s over the cut-in: tau_g = 0.8446 N.m, shaft
;;             138.9 W, into the bank 0.8 x 138.9 = 111.1 W (3.97 A at 28 V); the wind's gusts and its hour (6 x corridor 0.86 at the site
;;             x 1 + 0.35 cos(2 pi (hour - 2) / 24), 1.35 at 02:00) move it: the trace runs 105 to 287 W, 153 W on average
;;   flywheel  all night the generator is an open circuit (a bank under 0 C takes nothing) and the sails run free, near 2.5 x 7 / 10 x 2
;;             = 3.5 rad/s: 1/2 (400 x 100 / 3) 3^2 = 60 kJ = 17 Wh at most, which the bank takes in its first seconds, so it is the
;;             wind that fills the rest (traced: 5 Wh in the first minute of the wake, the rest at the wind's rate)
;;   bank      the C# sim's day (traced, 1 s steps): the rock peaks at 118.7 C at 35,200 s and the bank is over 0 C from 37,300 s (17:05),
;;             20.10 C at 73,300 s and 19.93 C at 73,979 s (03:00), the lid about 57% open
;;
;; The vault, the rock and the strip are bimetal-night's (see its header: +4.28 C in the bank at 03:00 from a frozen start at 17:00 with
;; the rock at 200 C). Here the heliostat heats the rock through the day with the lid open, the walls soak by day as well, and the bank is
;; warmer at 03:00 (20 C) than in the doc's vault (4.3 C).
(require racket/math)

(define md (mm 5))
(define wheel-100 (spur-gear #:teeth 100 #:module md #:width (cm 2)))
(define pinion-20 (spur-gear #:teeth 20 #:module md #:width (cm 3)))
(define apart (* md (+ 50 10)))
(define pinion-angle (radians->degrees (mate-angle 100 0 20 0)))
(define rotor (disc-wheel #:radius (cm 10) #:width (cm 8)))

(define hub-y (m 11))
(define eta 0.97)

;; the vault stands behind the train (the sails sweep the plane z = 0)
(define vx (m 6))
(define vz (m -4))

(define-machine e2e-route
  #:source "issue #96: the fastest route's works, stood in the crater world: a windmill, a 125:1 train, a motor and a bank in a tight vault with a heated rock in a bimetal-lidded bin"
  #:planet mars
  #:latitude -2 #:day 100 #:time 7
  #:weather (weather #:passes '(3 15) #:pass-minutes 10)
  ;; ---- the windmill, geared 125:1, and the motor
  (post trestle #:at ((cm 45) 0 (cm -145)) #:size ((m 0.8) (- hub-y 0.4) (m 0.8)) #:material oak #:round #t)
  (windmill sails #:at (0 hub-y 0) #:radius (m 10) #:mass 400 #:wind 6 #:wind-from-map #t #:cp 0.3 #:tip-speed-ratio 2.5 #:material oak)
  (wheel wheel-a #:shape wheel-100 #:at (0 hub-y (cm -130)) #:material oak)
  (wheel pinion-b #:shape pinion-20 #:at (apart hub-y (cm -130)) #:material iron #:angle-deg pinion-angle)
  (wheel wheel-b #:shape wheel-100 #:at (apart hub-y (cm -140)) #:material oak)
  (wheel pinion-c #:shape pinion-20 #:at ((* 2 apart) hub-y (cm -140)) #:material iron #:angle-deg pinion-angle)
  (wheel wheel-c #:shape wheel-100 #:at ((* 2 apart) hub-y (cm -150)) #:material oak)
  (wheel pinion-d #:shape pinion-20 #:at ((* 3 apart) hub-y (cm -150)) #:material iron #:angle-deg pinion-angle)
  (wheel rotor-disc #:shape rotor #:at ((* 3 apart) hub-y (cm -162)) #:material iron)
  (arbor sails wheel-a)
  (arbor pinion-b wheel-b)
  (arbor pinion-c wheel-c)
  (arbor pinion-d rotor-disc)
  (mesh wheel-a pinion-b #:efficiency eta)
  (mesh wheel-b pinion-c #:efficiency eta)
  (mesh wheel-c pinion-d #:efficiency eta)
  (generator motor #:at ((* 3 apart) hub-y (cm -185)) #:on rotor-disc #:charges bank)
  ;; ---- the vault: a tight regolith cavity with the bank, a rock in a lidded bin and the strip that works the lid
  (enclosure vault #:at (vx 0 vz) #:size ((m 0.5) (m 0.5) (m 0.5)) #:pressure 610 #:temperature -55
             #:wall regolith #:wall-thickness 0.5 #:ground -55)
  (heat-store cells #:at ((+ vx (m -0.12)) 0 vz) #:mass 16 #:contents cells #:temperature -55)
  (heat-store rock #:at ((+ vx (m 0.12)) 0 vz) #:mass 40 #:contents basalt #:temperature -55)
  (heat-bin bin #:at ((+ vx (m 0.12)) 0 vz) #:holds rock #:leak 0.1)
  (bimetal strip #:at ((+ vx (m -0.19)) (m 0.46) vz) #:senses cells #:drives bin #:layers (brass steel))
  (battery-bank bank #:at ((+ vx (m -0.6)) (m 0.6) vz) #:in cells #:capacity 25 #:charge 0)
  ;; ---- the heliostat: a 4 m2 mirror north of the vault throwing the sun on the rock
  (mirror heliostat #:at (vx (m 1.5) (+ vz (m -3))) #:area 1.5 #:onto rock)
  ;; ---- what to sleep until: the rock hot (a day's heating), and the call
  (wake pre-dawn #:when ((scene elapsed above 73300)) #:limit 80000)
  (wake call #:when ((bank won above 0.5)) #:limit 120000))
