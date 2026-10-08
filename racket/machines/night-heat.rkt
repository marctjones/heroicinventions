#lang heroic
;; Night heat on Mars (issue #71): a battery bank buried in a regolith vault, kept above 0 C through the night by hot
;; rock in a lidded bin. Three vaults and a rock in the sun, from 17:00 (the sun sets at 18:00);
;; 03:00, the relay pass, is 10 local hours (3,699 s each, a sol of 88,775 s in 24) on: 36,990 s.
;;
;; The numbers are scenario numbers: the vault's wall is 0.5 m of regolith (k 0.039 W/(m K), InSight HP3, Grott et al.
;; 2021; rho 1,500, c 800: alpha = 3.25e-8 m2/s, thermal inertia I = sqrt(k rho c) = 216.3 J/(m2 K sqrt(s))), over all six faces of
;; its cavity, starting at the ground's -55 C all through, as a freshly dug one does. The bank is 16 kg of lithium cells
;; (c 1,000 J/(kg K): 16 kJ/K; about 4 kWh at 250 Wh/kg). The rock is basalt (c 840), 200 C when it goes in. In the thin air
;; (610 Pa) heat crosses the cavity by radiation only: each body to the cavity wall, the two-surface exchange
;; Q = sigma (T1^4 - T2^4) / (1/(eps A1) + (1 - eps)/(eps A2)), eps 0.9, a body's surface a cube of its own volume.
;;
;; tight    a 0.5 m cavity (A = 1.5 m2), 40 kg of rock (0.3451 m2, bank 0.2068 m2), lid leaking 0.1 W/K, thermostat 5 / 40 C
;; wide     a 1 m cavity (A = 6 m2, four times the cold wall), 11 kg of rock: the design doc's first estimate
;; leaky    the tight vault with a 0.5 W/K lid
;; sunrock  40 kg of basalt in the open under a 4 m2 heliostat, to be pushed into a bin at dusk
;;
;; Worked before running, each by a time constant first
;;   wall    heat soaks into a fresh wall with the flux q = I dT / sqrt(pi t), 2 I dT sqrt(t / pi) J/m2 by time t: a night
;;           (10 h, dT 60 K) is 2.8 MJ/m2, 16 times the steady loss k dT t / L, and only sqrt(alpha t) = 3.5 cm deep. The steady
;;           k A / L = 0.117 W/K (tight) is reached over L^2 / alpha = 89 days. Seen from the cavity the wall is a conductance
;;           A I / sqrt(pi t): 3.0 W/K after an hour, 1.0 W/K at dawn (1.5 m2).
;;   rock    at the start Q0 = 0.8798 x 0.3451 x sigma (473.15^4 - 218.15^4) = 823.8 W on 33.6 kJ/K: 0.02452 K/s, a time
;;           constant 255 K / 823.8 W x 33.6 kJ/K = 2.89 h that lengthens as the rock cools (radiation goes as T^3).
;;   bank    0.75 W/K to the walls on 16 kJ/K: tau = 5.9 h, so it lags the cavity
;;   night 1 the lumped model (rock and bank joined to a quasi-steady cavity, which feeds a wall of conductance A I / sqrt(pi t)),
;;           integrated by hand at 1 s, against the trace (C#, 5 s steps):
;;                           rock 1 h   rock 5 h   bank 1 h   bank 5 h   bank 03:00
;;             tight  model   144.2      61.6      -39.9      -5.4       -1.3
;;                    trace   142.1      62.3      -43.3      -5.9       +4.3
;;             wide   model   111.6      10.2      -53.9     -51.6      -51.5
;;                    trace   111.3      10.6      -54.1     -51.3      -50.1
;;           (the model counts the wall's late uptake a little high, so the tight vault does better than its -1.3: +4.3 C)
;;           11 kg in the 1 m cavity fails: 1.4 MJ of rock above 50 C against the 6 m2 wall's 1.9 MJ already by 03:00, and
;;           the bank is still at -50 C. About 40 kg in the 0.5 m cavity works from night 1, from a frozen bank: bank +4.3 C.
;;           Below about 36 kg it does not (35 kg: -2.1 C at 03:00, 30 kg: -8.9 C).
;;   lid     shut, the lid is a leak L from the rock to the cavity: tau = m c / L = 336,000 s (3.9 days) at 0.1 W/K, 67,200 s
;;           (18.7 h) at 0.5. With the rock topped up to 200 C each dusk and the bank over 5 C all day (from sol 4), the rock
;;           ends the sol at T_e + (200 - T_e) exp(-88,775 / tau): sol 5, T_e (the mean cavity) 14.1 C and 48.6 C:
;;           156.8 C and 89.0 C, traced 157.0 C and 89.0 C. The leak is what warms the bank, so the lid must leak 0.1 W/K or
;;           less: at 0.1 the bank never passes the thermostat's 40 C (peak 40.4 C on sol 3, 13-19 C from sol 5 to 12);
;;           at 0.5 it is 50 C by sol 5, 62 C on sol 7, 82 C on sol 12, past the 45 C a lithium bank may charge at.
;;   sun     a heliostat's power P on 40 kg of basalt warms it P / (m c): 383.1 W / 33.6 kJ/K = 0.01140 K/s (0.684 K a minute).

(define-machine night-heat
  #:source "Thermal mass: a regolith vault, a rock heat store and a lidded bin"
  #:planet mars
  #:latitude -2 #:day 100 #:time 17
  #:weather (weather #:passes '(3 15) #:pass-minutes 10)
  ;; tight: a 0.5 m cavity, 40 kg of rock
  (enclosure tight #:at ((m -2) 0 0) #:size ((m 0.5) (m 0.5) (m 0.5)) #:pressure 610 #:temperature -55
             #:wall regolith #:wall-thickness 0.5 #:ground -55)
  (heat-store tight-bank #:at ((m -2.12) 0 0) #:mass 16 #:contents cells #:temperature -55)
  (heat-store tight-rock #:at ((m -1.88) 0 0) #:mass 40 #:contents basalt #:temperature 200)
  (heat-bin tight-bin #:at ((m -1.88) 0 0) #:holds tight-rock #:leak 0.1 #:sense tight-bank)
  ;; wide: a 1 m cavity, 11 kg of rock
  (enclosure wide #:at (0 0 0) #:size ((m 1) (m 1) (m 1)) #:pressure 610 #:temperature -55
             #:wall regolith #:wall-thickness 0.5 #:ground -55)
  (heat-store wide-bank #:at ((m -0.3) 0 0) #:mass 16 #:contents cells #:temperature -55)
  (heat-store wide-rock #:at ((m 0.3) 0 0) #:mass 11 #:contents basalt #:temperature 200)
  (heat-bin wide-bin #:at ((m 0.3) 0 0) #:holds wide-rock #:leak 0.1 #:sense wide-bank)
  ;; leaky: the tight vault with a lid that leaks 0.5 W/K
  (enclosure leaky #:at ((m 2) 0 0) #:size ((m 0.5) (m 0.5) (m 0.5)) #:pressure 610 #:temperature -55
             #:wall regolith #:wall-thickness 0.5 #:ground -55)
  (heat-store leaky-bank #:at ((m 1.88) 0 0) #:mass 16 #:contents cells #:temperature -55)
  (heat-store leaky-rock #:at ((m 2.12) 0 0) #:mass 40 #:contents basalt #:temperature 200)
  (heat-bin leaky-bin #:at ((m 2.12) 0 0) #:holds leaky-rock #:leak 0.5 #:sense leaky-bank)
  ;; by day: rock in the open, heated by a heliostat, to be pushed into a bin at dusk
  (heat-store sunrock #:at ((m -4) 0 0) #:mass 40 #:contents basalt #:temperature -24)
  (mirror sun-heliostat #:at ((m -4) (m 1.5) (m -3)) #:area 4 #:onto sunrock))
