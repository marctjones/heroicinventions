#lang heroic
;; The Lonely Rover's battery bank, as found (issue #209): the cargo crate that holds it (cargo-crate's oak box, 50 cm, 90 kg,
;; on Mars) with the real battery-bank part built into it, so that the bank the slide buries, the rover digs out and pushes,
;; and the goals find and free, is the bank the electrics charge and the win reads.
;;   crate   the rigid body: what the rubble buries and the backhoe frees (the same block as cargo-crate, so the opening buries it
;;           exactly as before: 3.98 m).
;;   cells   16 kg of lithium cells (c = 1,000 J/(kg K): 16 kJ/K), a heat store, in the open at the scene's ambient, exchanging
;;           with the air by radiation alone in Mars's 610 Pa (conductance 0). Its temperature is the bank's own.
;;   bank    4 kWh (16 kg at 250 Wh/kg; the scenario's bank-capacity multiplier, #60, applies), empty, charging only from 0 to 45 C of
;;           the cells, #:on the crate: the view draws its charge on the crate, so it travels with the crate.
;; The state (charge, per-source record, the cells' heat, the win) is the machine's and not the body's, so freeing the crate and
;; pushing it carries all of it. A generator charges it when a wire joins them (#208); in this machine alone nothing does.
(define-machine found-bank
  #:source "the Lonely Rover's battery bank in its crate on Mars: a block the ground buries and the rover moves, with the electrics' bank built into it"
  #:planet mars
  (block crate #:at (0 (cm 25) 0) #:size (cm 50) #:material oak)
  (heat-store cells #:at (0 (cm 25) 0) #:mass 16 #:contents cells)
  (battery-bank bank #:at (0 (cm 25) 0) #:in cells #:on crate #:capacity 4000 #:charge 0))
