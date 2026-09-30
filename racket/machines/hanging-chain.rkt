#lang heroic
;; A chain hung between two hooks (issue #31): 2.5 m of 1 cm iron rod in 40
;; links pinned end to end, from hooks 2 m apart at the same height. It
;; sags into the catenary y = a cosh(x/a): a is fixed by the chain's length
;; over its span, 2a sinh(S/2a) = L, so for S = 2 m, L = 2.5 m, sinh(1/a) =
;; 1.25/a, a = 0.8455 m (by bisection). Its lowest point hangs
;; a (cosh(S/2a) - 1) = 0.664 m below the hooks; at the hooks it leaves at
;; atan(sinh(S/2a)) = 55.9° below the level. It weighs w = 7700 x 9.81 x
;; pi (0.005)² = 5.93 N a metre, so each hook carries w a cosh(S/2a) =
;; 8.95 N along the chain: 5.01 N pulling inward (w a) and 7.41 N up (half
;; its weight).
(define-machine hanging-chain
  #:source "the catenary (Leibniz, Huygens and Johann Bernoulli, 1691)"
  (post left-hook #:at (-1.1 0 0) #:size (0.1 2.05 0.1) #:material oak)
  (post right-hook #:at (1.1 0 0) #:size (0.1 2.05 0.1) #:material oak)
  (rope chain #:from (world -1 2 0) #:to (world 1 2 0) #:length 2.5 #:links 40
        #:material iron #:diameter (cm 1)))
