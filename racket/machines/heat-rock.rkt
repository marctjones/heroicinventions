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
;;   in the open the rock radiates to the air as a body in a room much bigger than itself: Q = eps A sigma (T^4 - T_air^4), eps 0.9. At
;;   17:00 the air is at the scene's ambient (traced below), so it loses about 860 W at 200 C: dT/dt = Q / 33.6 kJ/K = 0.0256 K/s.
;;   Pushed in at once (T = 5 s, 4.3 kJ lost) the night is night-heat's: bank +4.3 C at 03:00 (36,990 s). Every kJ the rock loses in the open
;;   costs the bank at 03:00 about 0.0092 K (night-heat: 35 kg of rock, 0.69 MJ less to give, was 6.4 K lower), so a push that takes 10
;;   minutes, with the rock cooling by the lumped open-air law on the way, leaves it lower by 0.0092 K/kJ x what the rock lost (see the test).
;;   Never pushed in, the rock cools in the open and the bank stays near the ground's -55 C all night (it has no heat to get).
;;   The heliostat (4 m2, 17:00 sun 463.9 W as in night-heat's sunrock) lights the rock only while the rock stands in its spot: pushed
;;   0.5 m or more from it, nothing arrives.

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
