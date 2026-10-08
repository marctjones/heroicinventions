#lang heroic
;; A cart on the flat, for a hand to push (#159). The oak-bed handcart of carts.rkt (disc wheels, 30 cm, 2.51 kg each; bed
;; 18.4 kg; M = 28.48 kg, sum I/r^2 = 5.06 kg) stands on the ground and nothing else moves it. A person who pushes it up to
;; 1 m/s and lets go finds it slowing at the rolling resistance's a = C_rr g M / (M + sum I/r^2) = 0.04 (9.81)(0.849) = 0.333
;; m/s2 (C_rr = 0.04, as in carts.rkt), so it coasts v^2 / 2a = 1.50 m from 1 m/s and stops.
;; Drag it by its bed: HEROIC_DRAG="drag disc-cart 0 0 0 at 0.5; to 0 0.151 2 at 2.5; release at 2.5" moves the hand
;; 1 m/s along the track for two seconds.
(require racket/list)
(define r (cm 15))
(define wheel-w (cm 5))
(define chassis '(0.4 0.08 0.8))
(define y (+ r (mm 1)))
(define disc (disc-wheel #:radius r #:width wheel-w))
(define (wheel-at side end) (list (* side (+ (/ (first chassis) 2) (/ wheel-w 2) (cm 1))) y (* end (cm 30))))

(define-machine cart-push
  #:source "the wheel; a hand pushing a handcart"
  (block disc-cart #:at (0 y 0) #:size (cm 8) #:dimensions ((first chassis) (second chassis) (third chassis)) #:material oak)
  (wheel disc-1 #:shape disc #:at ((first (wheel-at -1 -1)) y (third (wheel-at -1 -1))) #:axis x #:material oak #:on disc-cart)
  (wheel disc-2 #:shape disc #:at ((first (wheel-at -1 1)) y (third (wheel-at -1 1))) #:axis x #:material oak #:on disc-cart)
  (wheel disc-3 #:shape disc #:at ((first (wheel-at 1 -1)) y (third (wheel-at 1 -1))) #:axis x #:material oak #:on disc-cart)
  (wheel disc-4 #:shape disc #:at ((first (wheel-at 1 1)) y (third (wheel-at 1 1))) #:axis x #:material oak #:on disc-cart))
