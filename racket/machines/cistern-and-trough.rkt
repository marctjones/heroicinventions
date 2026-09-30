#lang heroic
;; The cistern and the trough as one machine, joined by a pipe inside it:
;; what the two separate machines joined by a pipe across the world
;; (game/worlds/linked-pipe.world, issue #78) must do exactly.
;;
;; Flow = conductance × head difference, 2 L/s per metre. At first the
;; trough's water is below its inlet (10 cm up), so the cistern's surface
;; (1.8 m) works against the inlet's height: dV/dt = -k(0.9 + 2V) with V
;; the cistern's water (m³, area 0.5 m²), until the trough holds 50 L,
;; after ln(0.85/0.8)/0.004 = 15.2 s. Then the two surfaces, 1 + 2V and
;; 2(0.4 - V), close as -k(0.2 + 4V): V = -0.05 + 0.4 e^(-0.008 (t - 15.2)).
;; At 60 s the cistern holds 229 L; at 120 s, 123 L; it runs dry at 275 s,
;; leaving the trough 400 L deep at 80 cm, still below the cistern's floor.

(define-machine cistern-and-trough
  #:source "a raised cistern and a trough, joined by a pipe"
  (tank cistern #:at (0 1.0 0) #:area 0.5 #:height 1.0 #:water (L 400) #:material limestone
        (port outlet #:height 0))
  (tank trough #:at (4 0 0) #:area 0.5 #:height 1.0 #:material limestone
        (port inlet #:height (cm 10)))
  (pipe feed cistern.outlet trough.inlet #:conductance 2e-3))
