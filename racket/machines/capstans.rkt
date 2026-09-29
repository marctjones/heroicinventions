#lang heroic
;; Three oak bollards, each with a 200 kg load hanging a metre below it on a
;; hemp rope, and a sailor holding the other end with 100 N -- the pull of
;; about 10 kg in the hand. What differs is how many times the rope goes
;; round the post.
;;
;; Where rope slides on a post, friction takes off tension in proportion to
;; the tension there, so round the post it falls off exponentially: the
;; tight end can carry e^(mu theta) times the slack end (the capstan
;; equation; Euler 1762). Hemp on oak: mu = sqrt(0.5 x 0.45) = 0.4743.
;;   half a turn, e^(mu pi)  =   4.44: 100 N holds 444 N, not the load's
;;                                     1962 N -- it runs out and falls
;;   one turn,    e^(2 mu pi) = 19.70: 100 N holds 1970 N -- just enough
;;   two turns,   e^(4 mu pi) = 387.9: 5.06 N would do
(define-machine capstans
  #:source "a sailor's turns round a bollard (Euler, 1762)"
  (capstan half-turn #:at (-2 2.5 0) #:turns 1/2 #:load 200 #:hold 100 #:drop 1 #:radius 0.15 #:rope hemp #:material oak)
  (capstan one-turn  #:at ( 0 2.5 0) #:turns 1   #:load 200 #:hold 100 #:drop 1 #:radius 0.15 #:rope hemp #:material oak)
  (capstan two-turns #:at ( 2 2.5 0) #:turns 2   #:load 200 #:hold 100 #:drop 1 #:radius 0.15 #:rope hemp #:material oak))
