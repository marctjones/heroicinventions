#lang heroic
;; Heron's fountain. Water poured into the basin drains into the sealed
;; receiver, compressing the air it shares with the supply vessel. That
;; pressure drives the supply's water up the nozzle, above the basin.
;;
;; Sized deliberately small: a fountain scaled for a real courtyard would
;; take many real minutes to visibly drain even sped up, since a large
;; sealed air volume barely compresses for the same litre of water moved.
;; This one holds only what it needs to make the whole draw-down-and-slow
;; story finish in well under a minute, even before the speed control.

(define vessel-area 0.02) ; m², both sealed vessels

(define-machine herons-fountain
  #:source "Hero of Alexandria, Pneumatica"
  (tank basin #:at (0 1.0 0) #:area 0.05 #:height (cm 10) #:water (L 4)
        (port drain #:height 0)
        (port jet #:height (cm 15)))
  (tank supply #:at ((cm 20) 0.5 0) #:area vessel-area #:height (cm 10) #:water (L 1.5)
        (port outlet #:height 0))
  (tank receiver #:at ((cm -20) 0 0) #:area vessel-area #:height (cm 6)
        (port inlet #:height (cm 6)))
  ;; Conductance up substantially from the original draft (2e-4/1e-4): a
  ;; smaller receiver alone still left the drain as the bottleneck, and
  ;; even a fast-filling receiver barely moves the jet if water reaches
  ;; it too slowly to matter within a normal watching window.
  (pipe drain basin.drain receiver.inlet #:conductance 1.5e-3)
  (pipe nozzle supply.outlet basin.jet #:conductance 1e-4 #:jet #t)
  ;; The air tube joins the tops of the two sealed vessels. Small on
  ;; purpose — less spare air volume means the same litre of draining
  ;; water raises pressure more, and faster.
  (sealed-air (supply receiver) #:tube (L 0.04)))
