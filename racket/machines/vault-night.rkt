#lang heroic
;; A vault for another machine's store (issue #211): night-heat's tight vault, as a machine of its own, with no bank in it. The found
;; bank's crate (racket/machines/found-bank.rkt, a machine of its own) pushed into it puts its cells in this vault's air: an enclosure holds
;; any machine's heat store whose point (the cells' #:at, carried with the crate) lies in its box (WorldZones, #211). So the bank pushed in
;; at 17:00 is night-heat's tight-bank exactly, and must come out at 03:00 as that one does: +4.3 C.
;;   vault   a 0.5 m cavity (A = 1.5 m2) in a 0.5 m regolith wall at the ground's -55 C (night-heat's tight, verbatim)
;;   rock    40 kg of basalt at 200 C in a lidded bin that leaks 0.1 W/K; the lid open (night-heat's thermostat opened it at the first step,
;;           the bank being under 5 C, and it stayed open all night: here it is simply open, as a thermostat on another machine's cells
;;           is not a part this machine can have)
;; Clock: 17:00 at latitude -2 on day 100 (night-heat's); 03:00 is 10 local hours of 3,699 s on: 36,990 s. The wake `night' sleeps to it.
;; Worked: with the cells at -55 C at 17:00 the network is night-heat's tight vault, so the bank is +4.3 C at 03:00 (night-heat's trace;
;; its lumped model -1.3, which over-counts the wall's late uptake) and the rock 32 C or so; with the crate left outside, the cells cool in
;; their own machine's air, by radiation alone (found-bank-test.rkt: 3.6 mK/s at 83 K over the air).
(define-machine vault-night
  #:source "issue #211: a vault built round another machine's store; night-heat's tight vault with nothing of its own but the rock"
  #:planet mars
  #:latitude -2 #:day 100 #:time 17
  #:weather (weather #:passes '(3 15) #:pass-minutes 10)
  (enclosure vault #:at (0 0 0) #:size ((m 0.5) (m 0.5) (m 0.5)) #:pressure 610 #:temperature -55
             #:wall regolith #:wall-thickness 0.5 #:ground -55)
  (heat-store rock #:at ((m 0.12) 0 0) #:mass 40 #:contents basalt #:temperature 200)
  (heat-bin bin #:at ((m 0.12) 0 0) #:holds rock #:leak 0.1 #:open 1)
  (wake night #:when ((scene elapsed above 36990)) #:limit 40000))
