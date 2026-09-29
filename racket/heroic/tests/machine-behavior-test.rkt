#lang racket/base
;; Behaviour claims about the compiled machines, checked against the real
;; C# simulation through the headless HeroicInventions.SimHost — not
;; hand-computed expected numbers. These replace
;; tests/HeroicInventions.Sim.Tests/MachineTests.cs's
;; HeronsFountainBlueprintLiftsWaterAboveTheBasin and
;; AeolipileBlueprintSpinsOnceTheWaterBoils, which asserted the same
;; things from C# directly against MachineRuntime. See docs/design.html
;; §III "Machines as tests".
(require rackunit heroic/simhost)

(test-case "Heron's fountain lifts water above its basin, then empties the supply vessel"
  (define run (simulate 'herons-fountain #:seconds 60 #:step 0.05 #:sample-dt 0.5))
  (check-true (> (max-of run '(nozzle jet-height)) 0.0)
              "the jet should rise above the basin at some point during the run")
  ;; herons-fountain.rkt's supply vessel starts holding 1.5 L, and the
  ;; getter reports water in litres (MachineRuntime converts m³ * 1000).
  (check-true (< (final-of run '(supply water)) 1.5)
              "the supply vessel should empty through the nozzle"))

(test-case "The aeolipile spins once its water boils"
  (define run (simulate 'aeolipile #:seconds 200 #:step 0.01 #:sample-dt 1))
  (check-true (>= (final-of run '(kettle temperature)) 100)
              "the boiler should reach the boiling point")
  (check-true (> (final-of run '(ball rpm)) 100)
              "the rotor should be spinning fast once the water is boiling"))

(test-case "The Vitruvian screw carries its pocket volume up every turn"
  ;; the game feeds the screw's real turning speed in; here, 12 rpm
  (define run (simulate 'archimedes-screw #:seconds 20 #:step 0.01 #:sample-dt 1 #:set '((raise rpm 12))))
  (define per-turn (final-of run '(raise per-turn)))          ; litres
  (check-= per-turn 23.2 0.5 "Vitruvius's 4 m screw at a 3-4-5 slope: ~23 L a turn")
  ;; while the intake is fully under water the flow is per-turn × rpm / 60
  (check-= (max-of run '(raise flow)) (/ (* per-turn 12) 60) 0.05)
  ;; and every litre is accounted for: the 900 the pool started with plus
  ;; what the spring fed it (4.5 L/s for 20 s), in the pool, the trough, or
  ;; run on down the channel to the field
  (check-= (+ (final-of run '(pool water)) (final-of run '(trough water)) (final-of run '(field water))) (+ 900 (* 4.5 20)) 1e-6)
  (check-true (> (+ (final-of run '(trough water)) (final-of run '(field water))) 50)))

(test-case "A screw that isn't turning lifts nothing"
  (define run (simulate 'archimedes-screw #:seconds 5 #:step 0.01 #:sample-dt 1))
  (check-= (final-of run '(trough water)) 0 1e-9))
