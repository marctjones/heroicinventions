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
;;
;; Poured again. Once the receiver is full (1.2 L: area 0.02 x 6 cm) the jet
;; dies away to nothing, with the supply down to 0.40 L, the basin 3.90 L, and
;; the air squeezed from 0.50 + 1.2 + 0.04 = 1.74 L into 2 - 0.40 + 0.04 = 1.64 L:
;; 101.325 x (1.74 / 1.64 - 1) = 6.18 kPa (Boyle), the 6.18 kPa it stands at.
;; To work it again somebody tips out the receiver, pours the supply back up to
;; 1.5 L and fills the basin to 4 L. The demo operator does all three at 36 s
;; (Shift+click the receiver: "Empty the tank"; set supply.water to 1.5 and
;; basin.water to 4 in the right-click list, since "Pour 10 L" fills a tank to
;; its brim, 2 L in the supply, and it would then jet harder). Everything is where it was at 0 s, so the
;; second jet is the first again: the same 41 cm rise at the start, the same
;; end state 28 s on, supply 0.40 L, receiver 1.2 L, 6.18 kPa.

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
  (sealed-air (supply receiver) #:tube (L 0.04))
  ;; emptied, refilled and poured again once the jet has died (issue #157); taking any control stops it
  (operator (at 36 (receiver water 0)) (at 36 (supply water 1.5)) (at 36 (basin water 4))))
