#lang heroic
;; A catapulta — the two-armed torsion bolt-shooter — proportioned
;; entirely from Vitruvius's table (De Architectura X.10). Everything
;; scales from one number: the spring hole is a ninth of the bolt's
;; length. This is the common 3-span field piece (a 69 cm bolt), so the
;; hole is 7.7 cm and every other part follows: arms 7 holes, the
;; channel 19 holes, the column 8 holes, and so on (see
;; heroic/geometry/catapult.rkt for each rule and where the text is
;; corrupt and emended).
;;
;; The frame, the two twisted-sinew springs and the arms at rest. Drawing
;; it back and loosing needs the bowstring, i.e. rope physics, not yet
;; built; the single-armed torsion-catapult machine shows a launch.

(define bolt (* 3 greek-span))

(define-machine vitruvian-catapulta
  #:source "Vitruvius, De Architectura X.10"
  (fixture frame   #:shape (catapulta-frame #:bolt-length bolt)       #:at (0 0 0) #:material oak)
  (fixture springs #:shape (catapulta-springs #:bolt-length bolt)     #:at (0 0 0) #:material hemp)
  (fixture left-arm  #:shape (catapulta-arm #:bolt-length bolt #:side -1) #:at (0 0 0) #:material oak)
  (fixture right-arm #:shape (catapulta-arm #:bolt-length bolt #:side 1)  #:at (0 0 0) #:material oak))
