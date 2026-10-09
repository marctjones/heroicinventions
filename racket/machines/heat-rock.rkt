#lang heroic
;; A heat store you can move (issue #206): the rock of night-heat's tight vault as a body. Hot basalt lies in the open 1.2 m in front of the
;; vault; a rover (or a hand) pushes it sideways into the lidded bin, and from that moment it is the bin's store: the lid, the thermostat and
;; the room's walls act on it, and the bank at 03:00 comes out as night-heat's. Nothing in this machine's physics is new: the vault, bank, bin
;; and rock are night-heat's "tight" ones (see its header for the wall, the radiation and the lumped model), same sizes, same numbers.
;;
;;   rock    40 kg of basalt (c 840: 33.6 kJ/K), 200 C, a cube of its volume (0.2398 m at 2,900 kg/m3, surface 0.3451 m2)
;;   bin     opens its front (+z) to the rock; cavity 0.3198 m across (the rock's side and 8 cm, 4 cm each side for a push that is a little askew), thermostat on the bank 5 / 40 C, lid 0.1 W/K
;;   push    the rock's middle at z = 1.2 m; the bin's floor middle is at z = 0, so the rock has to travel 1.2 m, and is in the bin once its
;;           middle is within 0.1599 m of the bin's middle. A rover pushes with 396 N on Mars (tyre grip) against the rock's mu m g = 0.6 x 40
;;           x 3.71 = 89 N (basalt on loam), 4.4 times to spare; the arm reaches 1.8 m from its pivot.
;;
;; Worked before running (night-heat's, with the rock outside for T seconds first)
;;   in the open the rock radiates to the air as a body in a room much bigger than itself: Q = eps A sigma (T^4 - T_air^4), eps 0.9. The air is
;;   -63 C in the first instant and -24.0 C once the weather's daily curve takes over at 17:00 (traced), so at 200 C the rock loses
;;   0.9 x 0.3451 x 5.670374e-8 x (473.15^4 - 249.13^4) = 814.8 W: dT/dt = 814.8 W / 33.6 kJ/K = 0.02425 K/s (traced 0.02424).
;;   Pushed in at once (T = 5 s, 4.07 kJ lost) the night is night-heat's: bank +4.28 C at 03:00 (36,990 s), less 0.036 K. Every kJ the rock
;;   loses in the open costs the bank at 03:00 about 0.00887 K (night-heat: 35 kg of rock, 0.72 MJ less above its 03:00 temperature, was 6.4 K
;;   lower, from -2.1 against +4.28), so: pushed in after 5 s, 4.24 C (traced 4.245); after 600 s the open-air law gives 186.35 C and 458.6 kJ
;;   gone, so 4.28 - 0.00887 x 458.6 = +0.21 C (traced +0.02: the coefficient under-predicts the late loss, 0.0093 K/kJ in the trace).
;;   Never pushed in, the rock cools in the open and the bank stays at the ground's -55 C all night (it has no heat to get).
;;   The heliostat (4 m2: 472 W at 17:00 in this scene's sun, traced) lights the rock only while the rock stands within 0.5 m of its spot:
;;   pushed further than that, nothing arrives and the mirror's spot turns red.

(define-machine heat-rock
  #:source "Thermal mass: hot rock that a rover can push into the lidded bin"
  #:planet mars
  #:latitude -2 #:day 100 #:time 17
  #:weather (weather #:passes '(3 15) #:pass-minutes 10)
  (enclosure vault #:at (0 0 0) #:size ((m 0.5) (m 0.5) (m 0.5)) #:pressure 610 #:temperature -55
             #:wall regolith #:wall-thickness 0.5 #:ground -55)
  (heat-store bank #:at ((m -0.12) 0 0) #:mass 16 #:contents cells #:temperature -55)
  (heat-store rock #:at ((m 0.12) 0 (m 1.2)) #:mass 40 #:contents basalt #:temperature 200 #:movable #t)
  (heat-bin bin #:at ((m 0.12) 0 0) #:leak 0.1 #:sense bank)
  (mirror heliostat #:at ((m 0.12) (m 1.5) (m -3)) #:area 4 #:onto rock))
