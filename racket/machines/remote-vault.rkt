#lang heroic
;; A vault with a battery bank in it and nothing else (issue #208): the bank half of found-electrics, a machine of its own so that
;; a world's wire link (wired-electrics.world) can run to it from a generator in another machine.
;; Exactly found-electrics' bank: 10 Wh (36,000 J) in a heat store of cells at 20 C in a small room, taking charge from 0 to 45 C, and the
;; call on the 03:00 relay pass (the clock starts at 02:55). With no generator of its own the bank is charged only by the wire:
;; 36,000 J / 278.30 W = 129.4 s of the found-electrics windmill's charging, the same as when the generator is in the same machine.
(define-machine remote-vault
  #:source "issue #208: found-electrics' vault and bank as a machine of its own, to be wired to a generator in another machine"
  #:time (/ 175 60.0)
  (post shelf #:at ((m 3.0) 0 (m -1.6)) #:size ((m 1.6) (m 5.4) (m 0.9)) #:material oak)
  (enclosure vault #:at ((m 3.0) (m 5.4) (m -1.6)) #:size ((m 1.5) (m 0.8) (m 0.8)) #:temperature 20)
  (heat-store cells #:at ((m 2.5) (m 5.4) (m -1.6)) #:mass 16 #:contents cells #:temperature 20)
  (battery-bank bank #:at ((m 3.3) (m 5.4) (m -1.6)) #:in cells #:capacity 10 #:charge 0))
