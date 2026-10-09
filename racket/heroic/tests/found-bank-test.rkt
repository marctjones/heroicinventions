#lang racket/base
;; Issue #209: the bank found in the crater is the real battery-bank part, built into its crate (racket/machines/found-bank.rkt).
;; Run in the game's own physics (Jolt, 120 Hz); skipped when Godot is not installed. The sim-side numbers (capacity, tuning, save and
;; load, the goals) are tests/HeroicInventions.Sim.Tests/FoundBankTests.cs. Worked out beforehand:
;;  - the opening: the rim comes down and buries the crate as before (cover about 3.98 m, held), and the bank inside it is the
;;    electrics' own: 4,000 Wh (16 kg of cells at 250 Wh/kg), empty, at Mars's ambient -63 C, so refusing charge (it is under 0 C);
;;  - a push: on level ground, warmed to 20 C with 1,000 Wh in it, a hand (a machine run may operate anything) drags the crate
;;    1.5 m sideways; its charge is the 1,000 Wh before, during and after, and its temperature is cooling by radiation alone,
;;    exactly as it does in a run where nobody touches it: the cells' 16 kJ/K through 0.69 W/K (e sigma A (T1^2 + T2^2)(T1 + T2), eps 0.9,
;;    A = 6 V^(2/3) of the 16 kg of cells) from 83 K above the air: 83 x 0.69 / 16,000 = 3.6 mK/s, 29 mK over the 8 s.
(require rackunit racket/list
         (only-in heroic/godothost godot-available? godot-simulate-world))

(define (at frame key) (cadr (assq key (cdr frame))))

(when (godot-available?)
  (test-case "the opening buries the real bank in its crate: 4,000 Wh, empty, at the air's -63 C, refusing charge"
    (define world (godot-simulate-world 'lonely-rover-opening #:seconds 40 #:sample-dt 10))
    (define f (last (hash-ref world 'battery-bank)))
    (check-= (at f 'crate.buried) 1 0)
    (check-= (at f 'crate.cover) 3.98 0.2)
    (check-= (at f 'bank.capacity) 5000 1e-9 "Wh")   ; the design doc's 5 kWh
    (check-= (at f 'bank.charge) 0 0)
    (check-= (at f 'bank.temperature) -63 1e-6)
    (check-= (at f 'bank.accepting) 0 0 "under 0 C")
    (check-= (at f 'scene.won) 0 0))

  (test-case "pushing the crate 1.5 m carries its charge and temperature: the charge is 1,000 Wh throughout and the cooling is the same as unpushed"
    (define (run drag?)
      (hash-ref (godot-simulate-world 'found-bank-check #:seconds 8 #:sample-dt 1
                                      #:env `(("HEROIC_ROVER" . "0")
                                              ("HEROIC_SET" . "cells temperature 20 0; bank charge 1000 0")
                                              ,@(if drag? '(("HEROIC_DRAG" . "drag crate 0 0.25 0 at 3; to 0 2.4 1.5 at 4; release at 5")) '())))
                'bank))
    (define pushed (run #t))
    (define still (run #f))
    (check-= (at (last pushed) 'crate.z) 1.5 0.15 "the crate moved 1.5 m")
    (check-true (< (abs (at (last still) 'crate.z)) 0.05))
    (for ([f pushed]) (check-= (at f 'bank.charge) 1000 1e-9 (format "charge at ~a s" (car f))))
    (check-= (at (last pushed) 'bank.temperature) (at (last still) 'bank.temperature) 1e-6 "its temperature is the bank's, not the crate's")
    (check-= (- 20 (at (last pushed) 'bank.temperature)) 0.029 0.006 "K lost to the air in 8 s")))
