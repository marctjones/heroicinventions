#lang heroic
;; The wheel and axle at its largest: the treadwheel (magnus tympanus)
;; of a Roman building crane, as on the Haterii relief (Rome, c. AD 100)
;; and the Capua relief. Men walking inside the 4.5 m wheel turn the
;; axle; a rope winds onto the 20 cm-radius drum on the same axle.
;;
;; Mechanical advantage of a wheel and axle is the ratio of the radii:
;; 2.25 m / 0.2 m ≈ 11, so a 70 kg man's weight on the treads holds
;; about 780 kg on the drum rope — before the crane's pulley blocks
;; multiply it again. Both parts share one axle and turn at walking
;; pace (3 rpm is ~0.7 m/s at the treads). The jib, rope and pulleys
;; need the rope physics, not yet built.

(define axle-height (m 2.6))

(define-machine roman-treadwheel
  #:source "Vitruvius, De Architectura X.2; Haterii relief, c. AD 100"
  (wheel tympanus #:shape (treadwheel #:radius (m 2.25) #:width (m 1.2))
         #:at (0 axle-height 0) #:material oak #:drive-rpm 3)
  (wheel drum #:shape (drum #:radius (cm 20) #:length (cm 70))
         #:at (0 axle-height (cm 105)) #:material oak #:drive-rpm 3))
