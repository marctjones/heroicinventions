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

(test-case "a float valve throttles a feed into a tank"
  (check-compile-error #rx"cistern is not an inflow, pipe or channel"
    (tank cistern #:at (0 0 0) #:area 0.25 #:height 0.6 (port spout #:height 0))
    (float-valve ball #:on cistern #:shut 0.4 #:travel 0.02))
  (check-compile-error #rx"spill runs off the scene"
    (tank cistern #:at (0 0 0) #:area 0.25 #:height 0.6 (port spout #:height 0))
    (channel spill #:from cistern.spout #:to off #:end (2 0 0) #:width 0.1)
    (float-valve ball #:on spill #:shut 0.4 #:travel 0.02))
  (check-compile-error #rx"aqueduct already has a float valve"
    (tank cistern #:at (0 0 0) #:area 0.25 #:height 0.6)
    (inflow aqueduct #:into cistern #:flow 0.002)
    (float-valve ball #:on aqueduct #:shut 0.4 #:travel 0.02)
    (float-valve cork #:on aqueduct #:shut 0.3 #:travel 0.02)))

(test-case "a leak is a hole in a tank's wall"
  (check-compile-error #rx"pier is not a tank"
    (post pier #:at (0 0 0) #:size (1 1 1) #:material oak)
    (leak hole #:on pier #:height 0.1 #:area 0.0005))
  (check-compile-error #rx"jar is not a tank; a leak can only run #:into a tank"
    (tank cask #:at (0 0 0) #:area 0.25 #:height 1)
    (post jar #:at (1 0 0) #:size (1 1 1) #:material oak)
    (leak hole #:on cask #:height 0.1 #:area 0.0005 #:into jar))
  (check-compile-error #rx"cannot run into its own tank"
    (tank cask #:at (0 0 0) #:area 0.25 #:height 1)
    (leak hole #:on cask #:height 0.1 #:area 0.0005 #:into cask)))

;; a leak's numbers are checked when the machine is built, not when it expands
(test-case "a leak's height must be in the wall and it must leak something"
  (define (build . clauses)
    (parameterize ([current-namespace (make-base-namespace)])
      (eval `(module leaky heroic (define-machine leaky ,@clauses)))
      (dynamic-require ''leaky #f)))
  (define cask '(tank cask #:at (0 0 0) #:area 0.25 #:height 1))
  (check-exn #rx"height 1.5 is not in cask's wall" (λ () (build cask '(leak hole #:on cask #:height 1.5 #:area 0.0005))))
  (check-exn #rx"leaks nothing" (λ () (build cask '(leak hole #:on cask #:height 0.1))))
  (check-not-exn (λ () (build cask '(leak hole #:on cask #:height 0.1 #:evaporation 0.00005)))))

(test-case "a safety valve sits in a boiler's lid"
  (check-compile-error #rx"cask is not a boiler; a safety valve sits in a boiler's lid"
    (tank cask #:at (0 0 0) #:area 0.25 #:height 1)
    (safety-valve guard #:on cask #:lift 100000 #:bore 0.008)))

;; a safety valve's numbers are checked when the machine is built, not when it expands
(test-case "a safety valve must lift below its boiler's rating"
  (define (build . clauses)
    (parameterize ([current-namespace (make-base-namespace)])
      (eval `(module papin heroic (define-machine papin ,@clauses)))
      (dynamic-require ''papin #f)))
  (define (k burst) `(boiler k #:at (0 0 0) #:radius 0.15 #:height 0.3 #:water 10 #:burst ,burst))
  (check-exn #rx"lifts at 200000 Pa, but k bursts at 150000 Pa"
             (λ () (build (k 150000) '(safety-valve guard #:on k #:lift 200000 #:bore 0.008))))
  (check-exn #rx"#:bore must be a length above 0" (λ () (build (k 0) '(safety-valve guard #:on k #:lift 100000 #:bore 0))))
  (check-exn #rx"#:burst must be a gauge pressure" (λ () (build (k -1))))
  (check-not-exn (λ () (build (k 0) '(safety-valve guard #:on k #:lift 100000 #:bore 0.008)))))

(test-case "a pump draws from one tank into another"
  (check-compile-error #rx"k is not a tank; a pump draws from a tank and pours into another"
    (tank well #:at (0 0 0) #:area 1 #:height 1)
    (boiler k #:at (1 0 0) #:radius 0.15 #:height 0.3 #:water 10)
    (pump p #:at (0 5 0) #:from well #:to k #:bore 0.15 #:stroke 0.5))
  (check-compile-error #rx"a pump cannot pour into the tank it draws from"
    (tank well #:at (0 0 0) #:area 1 #:height 1)
    (pump p #:at (0 5 0) #:from well #:to well #:bore 0.15 #:stroke 0.5)))

;; a pump's numbers are checked when the machine is built; standing past the suction limit is allowed
(test-case "a pump's bore, stroke and efficiency must make sense"
  (define (build . clauses)
    (parameterize ([current-namespace (make-base-namespace)])
      (eval `(module lifter heroic (define-machine lifter
                                    (tank well #:at (0 0 0) #:area 1 #:height 1)
                                    (tank cistern #:at (1 12 0) #:area 1 #:height 1)
                                    ,@clauses)))
      (dynamic-require ''lifter #f)))
  (check-exn #rx"pump p: #:bore must be a length above 0" (λ () (build '(pump p #:at (0 11 0) #:from well #:to cistern #:bore 0 #:stroke 0.5))))
  (check-exn #rx"#:efficiency must be in \\(0, 1\\]" (λ () (build '(pump p #:at (0 11 0) #:from well #:to cistern #:bore 0.15 #:stroke 0.5 #:efficiency 1.2))))
  (check-exn #rx"#:force must be a force above 0" (λ () (build '(pump p #:at (0 11 0) #:from well #:to cistern #:bore 0.15 #:stroke 0.5 #:force 0))))
  (check-not-exn (λ () (build '(pump p #:at (0 11 0) #:from well #:to cistern #:bore 0.15 #:stroke 0.5 #:rpm 20)))))
