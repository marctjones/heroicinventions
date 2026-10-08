#lang heroic
;; Night heat on Mars (issue #71): a battery bank buried in a regolith vault, kept above 0 C through the night
;; by hot rock in a lidded bin. (header to be written from the trace)
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
  (heat-store sunrock #:at ((m -5) 0 0) #:mass 40 #:contents basalt #:temperature -24)
  (mirror sun-heliostat #:at ((m -5) (m 1.5) (m 3)) #:area 4 #:onto sunrock))
