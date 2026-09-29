#lang racket/base
(require rackunit racket/list heroic/machine heroic/emit)

;; Expands a one-machine heroic module and returns nothing; errors propagate.
(define (expand-machine . clauses)
  (parameterize ([current-namespace (make-base-namespace)])
    (expand `(module test-machine heroic
               (define-machine test ,@clauses)))
    (void)))

(define-syntax-rule (check-compile-error rx clause ...)
  (check-exn (λ (e) (and (exn:fail:syntax? e) (regexp-match? rx (exn-message e))))
             (λ () (expand-machine 'clause ...))))

(define good-boiler
  '(boiler kettle #:at (0 0 0) #:radius 0.1 #:height 0.1 #:water 0.3 #:fire 3000))
(define good-rotor
  '(rotor ball #:at (0 0.4 0) #:radius 0.06 #:material bronze #:bore 0.002 #:arm 0.08))

(test-case "a well-formed machine expands"
  (check-not-exn
   (λ () (expand-machine good-boiler good-rotor '(connect kettle.steam ball.steam-in)))))

(test-case "unknown material is a compile-time error"
  (check-compile-error #rx"unknown material unobtainium"
    (block cube #:at (0 0 0) #:size 0.1 #:material unobtainium)))

(test-case "duplicate part names are rejected"
  (check-compile-error #rx"already a part named cube"
    (block cube #:at (0 0 0) #:size 0.1 #:material oak)
    (block cube #:at (1 0 0) #:size 0.1 #:material oak)))

(test-case "referring to a missing port lists the real ports"
  (check-compile-error #rx"kettle is a boiler with no port named spout; its ports are: steam"
    (boiler kettle #:at (0 0 0) #:radius 0.1 #:height 0.1 #:water 0.3)
    (rotor ball #:at (0 0.4 0) #:radius 0.06 #:material bronze #:bore 0.002 #:arm 0.08)
    (connect kettle.spout ball.steam-in)))

(test-case "a steam port cannot join a water port"
  (check-compile-error #rx"cannot join kettle.steam \\(steam port\\) to vat.drain \\(water port\\)"
    (boiler kettle #:at (0 0 0) #:radius 0.1 #:height 0.1 #:water 0.3)
    (tank vat #:at (0 0 0) #:area 0.1 #:height 0.2 (port drain #:height 0))
    (connect kettle.steam vat.drain)))

(test-case "a pipe only carries water"
  (check-compile-error #rx"a pipe carries water, but kettle.steam is a steam port"
    (boiler kettle #:at (0 0 0) #:radius 0.1 #:height 0.1 #:water 0.3)
    (tank vat #:at (0 0 0) #:area 0.1 #:height 0.2 (port drain #:height 0))
    (pipe p vat.drain kettle.steam #:conductance 1e-4)))

(test-case "a rotor without steam is rejected"
  (check-compile-error #rx"rotor ball has no steam supply"
    (rotor ball #:at (0 0.4 0) #:radius 0.06 #:material bronze #:bore 0.002 #:arm 0.08)))

(test-case "a missing required option is reported by syntax-parse"
  (check-compile-error #rx"#:material"
    (block cube #:at (0 0 0) #:size 0.1)))

(test-case "sealed air only groups tanks"
  (check-compile-error #rx"kettle is not a tank"
    (boiler kettle #:at (0 0 0) #:radius 0.1 #:height 0.1 #:water 0.3)
    (sealed-air (kettle))))

(define-namespace-anchor anchor)

(test-case "parameters are ordinary Racket expressions"
  ;; Share this test's instance of heroic/machine so the machine registers where we can see it.
  (define ns (make-base-namespace))
  (namespace-attach-module (namespace-anchor->namespace anchor) 'heroic/machine ns)
  (parameterize ([current-namespace ns])
    (eval '(module computed heroic
             (define (slot i) (* i 0.25))
             (define-machine row
               (block a #:at ((slot 1) 0 0) #:size (cm 10) #:material oak))))
    (eval '(require 'computed))
    (define m (last (take-registered-machines)))
    (define sexp (machine->sexp m))
    (check-equal? (cadr sexp) 'row)
    (define block (assq 'part (cddr sexp)))
    (check-equal? (assq 'at (cdddr block)) '(at 0.25 0.0 0.0))
    (check-equal? (assq 'props (cdddr block)) '(props (size 0.1) (tilt-deg 0.0)))))

(test-case "a pendulum's bearing friction needs a pin radius"
  (check-compile-error #rx"needs a #:bearing-radius"
    (pendulum bob #:at (0 1 0) #:length 0.5 #:material iron #:bearing-mu 0.4)))
