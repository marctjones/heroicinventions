#lang racket/base
;; Unit helpers. Every quantity in a machine is SI (metres, kilograms,
;; seconds, watts); these just make the source read like a drawing.
(provide m cm mm m2 cm2 L L/s kg g W kW)

(define (m x) x)
(define (cm x) (/ x 100))
(define (mm x) (/ x 1000))
(define (m2 x) x)
(define (cm2 x) (/ x 10000))
(define (L x) (/ x 1000))   ; litres → m³
(define (L/s x) (/ x 1000)) ; litres a second → m³/s
(define (kg x) x)
(define (g x) (/ x 1000))
(define (W x) x)
(define (kW x) (* x 1000))
