#lang heroic
;; Before pulleys, a rope ran over a fixed bar -- a beam end, a greased log
;; -- and dragged on it. Four oak bars, 60 cm round, each with a 40 cm
;; granite block (172.8 kg) hanging on one side of a hemp rope and a
;; smaller granite block on the other. The rope turns through half a turn
;; (pi) over the first two bars and a turn and a half (3 pi) over the
;; other two.
;;
;; Where rope slides on a bar, friction takes off tension in proportion to
;; the tension there, so the tight side can carry up to e^(mu theta) the
;; slack side (the capstan equation; Euler 1762). Hemp on oak, mu =
;; sqrt(0.5 x 0.45) = 0.4743:
;;   half a turn:      e^(mu pi)   =  4.438
;;   a turn and a half: e^(3 mu pi) = 87.40
;; So a block of m holds the 172.8 kg one while 172.8 <= m e^(mu theta);
;; past that, the rope slides, the tight side carries exactly e^(mu theta)
;; the slack side, and the pair accelerate at
;;   a = g (M - m E) / (M + m E)    (E = e^(mu theta), before damping)
;;
;;   half-slip:  pi,   m = 21.6 kg (20 cm):  m E = 95.9 < 172.8, slides at 2.810 m/s2
;;   half-hold:  pi,   m = 72.9 kg (30 cm):  m E = 323.5, holds; tensions 2.370 : 1
;;   coil-slip:  3 pi, m = 0.926 kg (7 cm):  m E = 80.9 < 172.8, slides at 3.551 m/s2
;;   coil-hold:  3 pi, m = 2.70 kg (10 cm):  m E = 236.0, holds; tensions 64.00 : 1
;; A 2.7 kg stone holding 173 kg: why a sailor takes turns round a bollard.
;; Try a smaller holding block, or #:mu 0.1 on a rope for a greased bar.
(require racket/math racket/list)

(define bar-y (m 5))
(define bar-r (cm 30))
(define load-size (cm 40))
(define load-top (m 3))                  ; its bottom 2.6 m above the ground
(define holder-top (m 1))
(define (dist p q) (sqrt (for/sum ([a p] [b q]) (sqr (- a b)))))

;; The rope's points on a bar at x, going round from its right-hand side
;; (where the holder's rope comes up) over the top and down its left side,
;; a quarter turn per point; a coil drifts along the bar (z) as it goes.
(define (round-bar x quarters drift)
  (for/list ([k (in-range (add1 quarters))])
    (define a (* k (/ pi 2)))
    (list (+ x (* bar-r (cos a))) (+ bar-y (* bar-r (sin a)))
          (* drift (- (/ k quarters) 1/2)))))

;; Each station's rope runs exactly as long as its path, so it starts taut.
(define (rope-length x quarters drift)
  (define pts (round-bar x quarters drift))
  (+ (- bar-y holder-top) (for/sum ([p pts] [q (cdr pts)]) (dist p q)) (- bar-y load-top)))
(define (first-z quarters drift) (caddr (first (round-bar 0 quarters drift))))
(define (last-z quarters drift) (caddr (last (round-bar 0 quarters drift))))
(define (pt x quarters drift k axis) (list-ref (list-ref (round-bar x quarters drift) k) axis))
(define post-h (+ bar-y bar-r))

(define-machine rope-over-bars
  #:source "the capstan equation (Euler, 1762) on a fixed bar"
  ;; half-slip
  (post half-slip-post-a #:at (-3 0 (cm -45)) #:size ((cm 10) post-h (cm 10)) #:material oak)
  (post half-slip-post-b #:at (-3 0 (cm 45)) #:size ((cm 10) post-h (cm 10)) #:material oak)
  (block half-slip-load #:at ((- -3 bar-r) (- load-top (/ load-size 2)) (last-z 2 0)) #:size load-size #:material granite)
  (block half-slip-holder #:at ((+ -3 bar-r) (- holder-top (/ (cm 20) 2)) (first-z 2 0)) #:size (cm 20) #:material granite)
  (rope half-slip #:from (half-slip-holder 0 (/ (cm 20) 2) 0) #:to (half-slip-load 0 (/ load-size 2) 0)
        #:length (rope-length -3 2 0)
        #:over (((pt -3 2 0 0 0) (pt -3 2 0 0 1) (pt -3 2 0 0 2)) ((pt -3 2 0 1 0) (pt -3 2 0 1 1) (pt -3 2 0 1 2)) ((pt -3 2 0 2 0) (pt -3 2 0 2 1) (pt -3 2 0 2 2)))
        #:bar oak #:diameter (cm 3))
  ;; half-hold
  (post half-hold-post-a #:at (-1 0 (cm -45)) #:size ((cm 10) post-h (cm 10)) #:material oak)
  (post half-hold-post-b #:at (-1 0 (cm 45)) #:size ((cm 10) post-h (cm 10)) #:material oak)
  (block half-hold-load #:at ((- -1 bar-r) (- load-top (/ load-size 2)) (last-z 2 0)) #:size load-size #:material granite)
  (block half-hold-holder #:at ((+ -1 bar-r) (- holder-top (/ (cm 30) 2)) (first-z 2 0)) #:size (cm 30) #:material granite)
  (rope half-hold #:from (half-hold-holder 0 (/ (cm 30) 2) 0) #:to (half-hold-load 0 (/ load-size 2) 0)
        #:length (rope-length -1 2 0)
        #:over (((pt -1 2 0 0 0) (pt -1 2 0 0 1) (pt -1 2 0 0 2)) ((pt -1 2 0 1 0) (pt -1 2 0 1 1) (pt -1 2 0 1 2)) ((pt -1 2 0 2 0) (pt -1 2 0 2 1) (pt -1 2 0 2 2)))
        #:bar oak #:diameter (cm 3))
  ;; coil-slip
  (post coil-slip-post-a #:at (1 0 (cm -45)) #:size ((cm 10) post-h (cm 10)) #:material oak)
  (post coil-slip-post-b #:at (1 0 (cm 45)) #:size ((cm 10) post-h (cm 10)) #:material oak)
  (block coil-slip-load #:at ((- 1 bar-r) (- load-top (/ load-size 2)) (last-z 6 0.12)) #:size load-size #:material granite)
  (block coil-slip-holder #:at ((+ 1 bar-r) (- holder-top (/ (cm 7) 2)) (first-z 6 0.12)) #:size (cm 7) #:material granite)
  (rope coil-slip #:from (coil-slip-holder 0 (/ (cm 7) 2) 0) #:to (coil-slip-load 0 (/ load-size 2) 0)
        #:length (rope-length 1 6 0.12)
        #:over (((pt 1 6 0.12 0 0) (pt 1 6 0.12 0 1) (pt 1 6 0.12 0 2)) ((pt 1 6 0.12 1 0) (pt 1 6 0.12 1 1) (pt 1 6 0.12 1 2)) ((pt 1 6 0.12 2 0) (pt 1 6 0.12 2 1) (pt 1 6 0.12 2 2)) ((pt 1 6 0.12 3 0) (pt 1 6 0.12 3 1) (pt 1 6 0.12 3 2)) ((pt 1 6 0.12 4 0) (pt 1 6 0.12 4 1) (pt 1 6 0.12 4 2)) ((pt 1 6 0.12 5 0) (pt 1 6 0.12 5 1) (pt 1 6 0.12 5 2)) ((pt 1 6 0.12 6 0) (pt 1 6 0.12 6 1) (pt 1 6 0.12 6 2)))
        #:bar oak #:diameter (cm 3))
  ;; coil-hold
  (post coil-hold-post-a #:at (3 0 (cm -45)) #:size ((cm 10) post-h (cm 10)) #:material oak)
  (post coil-hold-post-b #:at (3 0 (cm 45)) #:size ((cm 10) post-h (cm 10)) #:material oak)
  (block coil-hold-load #:at ((- 3 bar-r) (- load-top (/ load-size 2)) (last-z 6 0.12)) #:size load-size #:material granite)
  (block coil-hold-holder #:at ((+ 3 bar-r) (- holder-top (/ (cm 10) 2)) (first-z 6 0.12)) #:size (cm 10) #:material granite)
  (rope coil-hold #:from (coil-hold-holder 0 (/ (cm 10) 2) 0) #:to (coil-hold-load 0 (/ load-size 2) 0)
        #:length (rope-length 3 6 0.12)
        #:over (((pt 3 6 0.12 0 0) (pt 3 6 0.12 0 1) (pt 3 6 0.12 0 2)) ((pt 3 6 0.12 1 0) (pt 3 6 0.12 1 1) (pt 3 6 0.12 1 2)) ((pt 3 6 0.12 2 0) (pt 3 6 0.12 2 1) (pt 3 6 0.12 2 2)) ((pt 3 6 0.12 3 0) (pt 3 6 0.12 3 1) (pt 3 6 0.12 3 2)) ((pt 3 6 0.12 4 0) (pt 3 6 0.12 4 1) (pt 3 6 0.12 4 2)) ((pt 3 6 0.12 5 0) (pt 3 6 0.12 5 1) (pt 3 6 0.12 5 2)) ((pt 3 6 0.12 6 0) (pt 3 6 0.12 6 1) (pt 3 6 0.12 6 2)))
        #:bar oak #:diameter (cm 3)))
