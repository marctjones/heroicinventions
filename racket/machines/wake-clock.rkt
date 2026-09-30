#lang heroic
;; Sleeping until something happens (issue #59). A 500 L cistern (0.5 m2, 1 m
;; deep) starts empty and a spring of 2 L/s runs into it. The wake conditions
;; are named here so the game's sleep control can offer them:
;;   filled   wake when the cistern holds 50 L: 50 / 2 = 25 s. The estimate made
;;            before sleeping, from the rate of change of the water over a couple
;;            of seconds, is 25 s; the sleep ends at 25.0 s, within one step.
;;   both     'and': 20 L and 40 L both reached: the later, 20 s.
;;   either   'or: 10 s, the earlier, when 20 L is reached.
;;   guarded  wake at 50 L, but early if the cistern passes 30 L (an event): 15 s,
;;            woken early, and it says what woke it.
;;   never    wake when it holds 5,000 L, which a 500 L cistern cannot: the
;;            estimate says beyond the limit, and it stops at its 60 s
;;            limit, and says so.
(define-machine wake-clock
  #:source "A cistern that fills at a known rate, to sleep on"
  (tank cistern #:at (0 0 0) #:area 0.5 #:height (m 1) #:material oak)
  (inflow spring #:into cistern #:flow (L/s 2))
  (wake filled #:when ((cistern water above 50)) #:limit 600)
  (wake both #:when ((cistern water above 20) (cistern water above 40)) #:join and #:limit 600)
  (wake either #:when ((cistern water above 20) (cistern water above 40)) #:join or #:limit 600)
  (wake guarded #:when ((cistern water above 50)) #:limit 600 #:events ((cistern water above 30)))
  (wake never #:when ((cistern water above 5000)) #:limit 60))
