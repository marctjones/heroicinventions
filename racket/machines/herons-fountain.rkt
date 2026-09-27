#lang heroic
;; Heron's fountain. Water poured into the basin drains into the sealed
;; receiver, compressing the air it shares with the supply vessel. That
;; pressure drives the supply's water up the nozzle, above the basin.

(define vessel-area 0.02) ; m², both sealed vessels

(define-machine herons-fountain
  #:source "Hero of Alexandria, Pneumatica"
  (tank basin #:at (0 1.0 0) #:area 0.05 #:height (cm 10) #:water (L 4)
        (port drain #:height 0)
        (port jet #:height (cm 15)))
  (tank supply #:at ((cm 20) 0.5 0) #:area vessel-area #:height (cm 30) #:water (L 5)
        (port outlet #:height 0))
  (tank receiver #:at ((cm -20) 0 0) #:area vessel-area #:height (cm 30)
        (port inlet #:height (cm 30)))
  (pipe drain basin.drain receiver.inlet #:conductance 2e-4)
  (pipe nozzle supply.outlet basin.jet #:conductance 1e-4 #:jet #t)
  ;; The air tube joins the tops of the two sealed vessels.
  (sealed-air (supply receiver) #:tube (L 0.2)))
