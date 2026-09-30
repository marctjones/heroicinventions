#lang heroic
;; Hot-air engines on Mars (issue #65). Newcomen's engine is pushed by the
;; outside air, which Mars hardly has; a Stirling engine is driven by heat,
;; with Mars's -63 C air as its cold side. Three engines at Meridiani noon,
;; each under six flat 1 m2 heliostats in a ring (the sun held at noon).
;; The sim's own mirrors put Q = 1,508.8 W on each 1 m2 receiver (traced
;; at the first step; each heliostat 0.58 of its area square to the sun).
;;
;; The hot end settles where what it absorbs, eQ, equals what it
;; re-radiates, e sigma A (T^4 - Tair^4), plus what its heater passes the
;; engine, K (T - Tcold); of that the engine makes f (1 - Tcold/T) into
;; work, f = 0.35 of Carnot. Worked by bisection before running:
;;   small   K = 3 W/K:     sits hot, 98.5 C, but passes little: 73.7 W
;;   matched K = 9.36 W/K:  40.2 C, 111.3 W -- the most these mirrors can
;;           give: K* maximises f (1 - Tc/T)(eQ - e sigma A (T^4 - Ta^4))
;;   large   K = 36 W/K:    draws hard but runs at -27.7 C, barely warmer
;;           than the cold side, where Carnot allows little: 64.1 W
;; Each drives a 3 N m load, so it turns at P / 3 rad/s: 234, 354 and 204
;; rpm. Far below the receiver's 190-odd C stagnation: a hot receiver
;; re-radiates what the mirrors bring. And the cold matters: the same
;; matched engine with a 20 C cold side (set scene.ambient 20) settles at
;; 100.5 C and gives only 55.8 W, half.
(require racket/math)
(define ring-r (m 4))
(define (ring cx k) ; the k-th of six heliostats round an engine at cx
  (define a (* k (/ pi 3)))
  (list (+ cx (* ring-r (cos a))) (m 1.5) (* ring-r (sin a))))

(define-machine mars-stirling
  #:source "Robert Stirling's hot-air engine (1816), on mirror heat at Meridiani"
  #:planet mars
  #:latitude -2 #:day 100 #:time 12
  (stirling small #:at ((m -12) (m 1) 0) #:aperture 1 #:conductance 3 #:load 3)
  (stirling matched #:at (0 (m 1) 0) #:aperture 1 #:conductance 9.36 #:load 3)
  (stirling large #:at ((m 12) (m 1) 0) #:aperture 1 #:conductance 36 #:load 3)
  (mirror s1 #:at ((car (ring -12 0)) (cadr (ring -12 0)) (caddr (ring -12 0))) #:area 1 #:onto small)
  (mirror s2 #:at ((car (ring -12 1)) (cadr (ring -12 1)) (caddr (ring -12 1))) #:area 1 #:onto small)
  (mirror s3 #:at ((car (ring -12 2)) (cadr (ring -12 2)) (caddr (ring -12 2))) #:area 1 #:onto small)
  (mirror s4 #:at ((car (ring -12 3)) (cadr (ring -12 3)) (caddr (ring -12 3))) #:area 1 #:onto small)
  (mirror s5 #:at ((car (ring -12 4)) (cadr (ring -12 4)) (caddr (ring -12 4))) #:area 1 #:onto small)
  (mirror s6 #:at ((car (ring -12 5)) (cadr (ring -12 5)) (caddr (ring -12 5))) #:area 1 #:onto small)
  (mirror m1 #:at ((car (ring 0 0)) (cadr (ring 0 0)) (caddr (ring 0 0))) #:area 1 #:onto matched)
  (mirror m2 #:at ((car (ring 0 1)) (cadr (ring 0 1)) (caddr (ring 0 1))) #:area 1 #:onto matched)
  (mirror m3 #:at ((car (ring 0 2)) (cadr (ring 0 2)) (caddr (ring 0 2))) #:area 1 #:onto matched)
  (mirror m4 #:at ((car (ring 0 3)) (cadr (ring 0 3)) (caddr (ring 0 3))) #:area 1 #:onto matched)
  (mirror m5 #:at ((car (ring 0 4)) (cadr (ring 0 4)) (caddr (ring 0 4))) #:area 1 #:onto matched)
  (mirror m6 #:at ((car (ring 0 5)) (cadr (ring 0 5)) (caddr (ring 0 5))) #:area 1 #:onto matched)
  (mirror l1 #:at ((car (ring 12 0)) (cadr (ring 12 0)) (caddr (ring 12 0))) #:area 1 #:onto large)
  (mirror l2 #:at ((car (ring 12 1)) (cadr (ring 12 1)) (caddr (ring 12 1))) #:area 1 #:onto large)
  (mirror l3 #:at ((car (ring 12 2)) (cadr (ring 12 2)) (caddr (ring 12 2))) #:area 1 #:onto large)
  (mirror l4 #:at ((car (ring 12 3)) (cadr (ring 12 3)) (caddr (ring 12 3))) #:area 1 #:onto large)
  (mirror l5 #:at ((car (ring 12 4)) (cadr (ring 12 4)) (caddr (ring 12 4))) #:area 1 #:onto large)
  (mirror l6 #:at ((car (ring 12 5)) (cadr (ring 12 5)) (caddr (ring 12 5))) #:area 1 #:onto large))
