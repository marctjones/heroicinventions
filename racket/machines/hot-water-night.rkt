#lang heroic
;; Hot water through a Mars night (issue #71): three little rooms, each losing U = 2 W per kelvin through its walls (and
;; holding C1 = 3,000 J/K of wall and air) to a night held at -80 C, each starting at 0 C with a tank of water at 60 C joined
;; to it by a G = 6 W/K film (a contact film in 13.5 kPa air; radiation left out). The tank holds Cs = m x 4,186 J/K; the three
;; tanks are 10, 33 and 66 kg.
;;
;; Two bodies cooling together:  C1 T1' = -U (T1 - T0) + G (Ts - T1),  Cs Ts' = -G (Ts - T1).  Its eigenvalues give two
;; time constants, a fast one where the room follows the tank (C1 / (U + G) = 375 s) and a slow one where the pair cools
;; into the night:  8.1 h for 10 kg, 25.9 h for 33 kg, 51.5 h for 66 kg (about Cs (U + G) / (U G)). The closed form, at 1, 6 and 12 h:
;;                    room C                   tank C
;;                1 h     6 h    12 h      1 h     6 h    12 h
;;       10 kg   13.05  -29.93  -56.20    42.46  -14.10  -48.67     (but it freezes, below)
;;       33 kg   21.13    3.37  -13.87    54.30   30.72    7.82
;;       66 kg   23.04   13.50    3.21    57.11   44.41   30.73
;;
;; The design doc's arithmetic, 33 kg of water cooling 60 -> 10 C for the 7 MJ of a night at 160 W, holds the room 80 K
;; up only if the heat comes out at 160 W all night; a cooling tank gives less and less. Traced: the 33 kg room is above 0 C for
;; 7 h and ends the night at -13.8 C. To end it at 0 C needs about 56 kg (the closed form, solved for m), nearly twice the arithmetic:
;; 66 kg carries it through (+3.2 C), 33 kg does not.
;;
;; Freezing. The 10 kg tank reaches 0 C where the closed form crosses it, at 15,967 s (4.44 h); then it holds at 0 C while its
;; 334 kJ/kg leaves, the room settling at U T0 / (U + G) = -20 C within its 375 s, the walls taking U x 60 K = 120 W. In the
;; 27,233 s that remain (less the room's own settling, G x 375 s x 0.8 K) that is 120 W x 27,233 s / 3.34 MJ = 97.8% frozen at dawn;
;; traced 97.7%.
(define-machine hot-water-night
  #:source "Newton's law of cooling, two bodies: a tank and the room it warms"
  #:planet mars
  #:ambient -80
  (enclosure ten #:at ((m -2.4) 0 0) #:size ((m 1.5) (m 1.2) (m 1.2)) #:pressure 13500 #:air '((o2 0.21) (n2 0.79)) #:temperature 0
             #:insulation 2 #:heat-capacity 3000)
  (heat-store ten-tank #:at ((m -2.4) 0 0) #:mass 10 #:contents water #:temperature 60 #:area 0 #:conductance 6)
  (enclosure thirty-three #:at (0 0 0) #:size ((m 1.5) (m 1.2) (m 1.2)) #:pressure 13500 #:air '((o2 0.21) (n2 0.79)) #:temperature 0
             #:insulation 2 #:heat-capacity 3000)
  (heat-store thirty-three-tank #:at (0 0 0) #:mass 33 #:contents water #:temperature 60 #:area 0 #:conductance 6)
  (enclosure sixty-six #:at ((m 2.4) 0 0) #:size ((m 1.5) (m 1.2) (m 1.2)) #:pressure 13500 #:air '((o2 0.21) (n2 0.79)) #:temperature 0
             #:insulation 2 #:heat-capacity 3000)
  (heat-store sixty-six-tank #:at ((m 2.4) 0 0) #:mass 66 #:contents water #:temperature 60 #:area 0 #:conductance 6))
