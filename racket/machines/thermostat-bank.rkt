#lang heroic
;; The Thermostat and Harrison achievements (issue #68), on the bimetal-night bench with a battery bank in the cells: bimetal-night's
;; "warm" trio, a bank at +30 C beside 40 kg of basalt at 200 C in a lidded bin, with a brass-steel strip on the bank working the lid
;; (Harrison's H3 clock of 1759 used the same trick). The strip warms with the bank, closes the lid as it nears 40 C, and holds it
;; there: bimetal-night traced the bank to 38.1 C and holding for 12 hours with the lid open 5% and the rock still 130 C.
;; The achievement fires when the bank has stayed between 0 and 45 C, the lid part open (open strictly between 0 and 1), and the rock
;; above 100 C for one hour of the scene's clock: here from the start (the bank is at 30 C and the lid 0.29 open, throttling down),
;; so at 3,600 s. A bench with the strip taken off has the lid fixed shut (open 0): the achievement never fires.
(define-machine thermostat-bank
  #:source "issue #68: a bimetal strip holds a battery bank under 45 C beside a heat store above 100 C"
  #:planet mars
  #:latitude -2 #:day 100 #:time 17
  (enclosure room #:at (0 0 0) #:size ((m 0.5) (m 0.5) (m 0.5)) #:pressure 610 #:temperature 10
             #:wall regolith #:wall-thickness 0.5 #:ground 10)
  (heat-store cells #:at ((m -0.12) 0 0) #:mass 16 #:contents cells #:temperature 30)
  (heat-store rock #:at ((m 0.12) 0 0) #:mass 40 #:contents basalt #:temperature 200)
  (heat-bin bin #:at ((m 0.12) 0 0) #:holds rock #:leak 0.1)
  (bimetal strip #:at ((m -0.19) (m 0.46) 0) #:senses cells #:drives bin #:layers (brass steel))
  (battery-bank bank #:at ((m -0.12) (m 0.2) 0) #:in cells #:capacity 5))
