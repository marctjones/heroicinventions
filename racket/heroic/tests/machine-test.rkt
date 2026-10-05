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
    (check-equal? (assq 'props (cdddr block)) '(props (size 0.1) (tilt-deg 0.0) (fast #t) (drag-coefficient #f)))))

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

;; a windmill's numbers are checked when the machine is built, not when it expands
(test-case "a windmill takes no more of the wind than the Betz limit"
  (define (build . clauses)
    (parameterize ([current-namespace (make-base-namespace)])
      (eval `(module mill heroic (define-machine mill ,@clauses)))
      (dynamic-require ''mill #f)))
  (check-exn #rx"#:cp must be above 0 and at most the Betz limit"
             (λ () (build '(windmill sails #:at (0 10 0) #:radius 10 #:mass 1500 #:wind 6 #:cp 0.6))))
  (check-exn #rx"#:wind must be a speed" (λ () (build '(windmill sails #:at (0 10 0) #:radius 10 #:mass 1500 #:wind -1))))
  (check-not-exn (λ () (build '(windmill sails #:at (0 10 0) #:radius 10 #:mass 1500 #:wind 6 #:cp 16/27)))))

(define (run-machine . clauses)   ; expands and runs it: the checks made when the machine is built
  (parameterize ([current-namespace (make-base-namespace)])
    (eval `(module test-machine heroic (define-machine test ,@clauses)))
    (eval '(require 'test-machine))
    (void)))

(test-case "a cart wheel rides on a block"
  (check-exn #rx"must name a block"
    (λ () (run-machine '(post p #:at (0 0 0) #:size (0.1 1 0.1) #:material oak)
                          '(wheel w #:shape (disc-wheel #:radius 0.15 #:width 0.05) #:at (0 0.15 0) #:material oak #:on p))))
  (check-not-exn
    (λ () (expand-machine '(block bed #:at (0 0.15 0) #:size 0.05 #:material oak)
                          '(wheel w #:shape (cart-wheel #:radius 0.15 #:width 0.05) #:at (0.3 0.15 0) #:axis x #:material oak #:on bed #:rolling-resistance 0.02)))))

(test-case "joints: a known kind, between moving parts, with what each kind needs"
  (define rod '(block rod #:at (0 1 0) #:size 0.05 #:material oak))
  (define bob '(block bob #:at (0 0.5 0) #:size 0.1 #:material iron))
  (check-not-exn (λ () (expand-machine rod bob '(joint j #:kind ball #:a rod #:b bob #:at (0 0.75 0)))))
  (check-not-exn (λ () (expand-machine rod '(joint j #:kind 6dof #:a rod #:b world #:at (0 1 0) #:free (y ry)))))
  (check-compile-error #rx"pin, ball, universal or 6dof" (block rod #:at (0 1 0) #:size 0.05 #:material oak)
    (joint j #:kind weld #:a rod #:b world #:at (0 1 0)))
  (check-compile-error #rx"needs the #:axis" (block rod #:at (0 1 0) #:size 0.05 #:material oak)
    (joint j #:kind pin #:a rod #:b world #:at (0 1 0)))
  (check-compile-error #rx"doesn't move" (block rod #:at (0 1 0) #:size 0.05 #:material oak)
    (post p #:at (0 0 0) #:size (0.1 1 0.1) #:material oak)
    (joint j #:kind ball #:a rod #:b p #:at (0 1 0)))
  (check-compile-error #rx"not a shaft" (block rod #:at (0 1 0) #:size 0.05 #:material oak)
    (block bob #:at (0 0.5 0) #:size 0.1 #:material iron)
    (joint j #:kind universal #:a rod #:b bob #:at (0 1 0)))
  (check-compile-error #rx"x y z \\(sliding\\)" (block rod #:at (0 1 0) #:size 0.05 #:material oak)
    (joint j #:kind 6dof #:a rod #:b world #:at (0 1 0) #:free (up))))

(test-case "a rope runs over turning pulleys or fixed bars, not both; a bar is of a known material"
  (check-compile-error #rx"not both"
    (block a #:at (0 0 0) #:size 0.1 #:material oak)
    (block b #:at (1 0 0) #:size 0.1 #:material oak)
    (wheel p #:shape (pulley #:radius 0.1 #:width 0.05) #:at (0.5 1 0) #:material oak)
    (rope r #:from (a 0 0 0) #:to (b 0 0 0) #:length 2 #:over ((0.5 1.1 0)) #:turns p #:bar oak))
  (check-compile-error #rx"unknown bar material silk"
    (block a #:at (0 0 0) #:size 0.1 #:material oak)
    (block b #:at (1 0 0) #:size 0.1 #:material oak)
    (rope r #:from (a 0 0 0) #:to (b 0 0 0) #:length 2 #:over ((0.5 1 0)) #:bar silk))
  (check-compile-error #rx"give #:bar too"
    (block a #:at (0 0 0) #:size 0.1 #:material oak)
    (block b #:at (1 0 0) #:size 0.1 #:material oak)
    (rope r #:from (a 0 0 0) #:to (b 0 0 0) #:length 2 #:mu 0.1)))

(test-case "a capstan's rope is a known material"
  (check-compile-error #rx"unknown rope material silk"
    (capstan post #:at (0 2 0) #:turns 1 #:load 100 #:rope silk)))

(test-case "a bellows forces draught into a hearth"
  (check-compile-error #rx"cask is not a hearth; a bellows forces draught into a hearth"
    (tank cask #:at (0 0 0) #:area 0.25 #:height 1)
    (bellows pump #:at (0 0 0) #:on cask #:airflow 0.005)))

(test-case "only one bellows to a hearth"
  (check-compile-error #rx"fire already has a bellows"
    (boiler kettle #:at (0 0 0) #:radius 0.1 #:height 0.1 #:water 0.3)
    (hearth fire #:at (0 0 0) #:heats kettle #:power 4000 #:fuel 0.06)
    (bellows a #:at (0 0 0) #:on fire #:airflow 0.005)
    (bellows b #:at (0 0 0) #:on fire #:airflow 0.005)))

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

;; ---------------------------------------------------------------------------
;; Planets (issue #38)

(test-case "an unknown planet is a compile-time error naming the planets there are"
  (check-exn (λ (e) (and (exn:fail:syntax? e) (regexp-match? #rx"unknown planet venus; the planets are: earth, mars" (exn-message e))))
             (λ () (parameterize ([current-namespace (make-base-namespace)])
                     (expand '(module m heroic (define-machine m #:planet venus (tank a #:at (0 0 0) #:area 1 #:height 1))))))))

(test-case "a machine on Mars takes Mars's air unless it says otherwise, and writes every number of its planet"
  ;; built and written in a namespace of its own, so its structs are that namespace's
  (define (build . head)
    (parameterize ([current-namespace (make-base-namespace)])
      (eval `(module on-mars heroic (define-machine on-mars ,@head (tank a #:at (0 0 0) #:area 1 #:height 1))
               (provide on-mars)))
      (cddr ((dynamic-require 'heroic/emit 'machine->sexp) (dynamic-require ''on-mars 'on-mars)))))
  (define mars (build '#:planet 'mars))
  (check-equal? (assq 'ambient mars) '(ambient -63.0))
  (define clause (assq 'planet mars))
  (check-equal? (cadr clause) 'mars)
  (check-equal? (assq 'gravity (cddr clause)) '(gravity 3.71))
  (check-equal? (assq 'pressure (cddr clause)) '(pressure 610.0))
  ;; Earth, said or unsaid, writes nothing: every existing scene is unchanged
  (check-false (assq 'planet (build)))
  (check-false (assq 'planet (build '#:planet 'earth)))
  (check-false (assq 'ambient (build '#:planet 'earth)))
  ;; a preset with a number changed, and a new air (which has its own molar mass)
  (define heavy (build '#:planet '(planet mars #:gravity 9.81 #:air '((o2 0.21) (n2 0.79))) '#:ambient 5))
  (define hc (assq 'planet heavy))
  (check-equal? (assq 'gravity (cddr hc)) '(gravity 9.81))
  (check-equal? (assq 'molar-mass (cddr hc)) '(molar-mass #f))
  (check-equal? (assq 'air (cddr hc)) '(air (o2 0.21) (n2 0.79) (co2 0.0) (h2o 0.0) (ar 0.0)))
  (check-equal? (assq 'ambient heavy) '(ambient 5.0))
  (check-exn #rx"#:air's fractions must add up to 1" (λ () (build '#:planet '(planet mars #:air '((o2 0.5)))))))

;; ---------------------------------------------------------------------------
;; Enclosures (issue #39)

(test-case "an enclosure's numbers are checked, and a hearth may heat one"
  (define (build . clauses)
    (parameterize ([current-namespace (make-base-namespace)])
      (eval `(module room heroic (define-machine room ,@clauses)))
      (dynamic-require ''room #f)))
  (check-exn #rx"enclosure hab: #:size must be" (λ () (build '(enclosure hab #:at (0 0 0) #:size (0 2 2)))))
  (check-exn #rx"enclosure hab: #:air's fractions must add up to 1" (λ () (build '(enclosure hab #:at (0 0 0) #:size (2 2 2) #:air '((o2 0.5))))))
  (check-exn #rx"#:coefficient must be in" (λ () (build '(enclosure hab #:at (0 0 0) #:size (2 2 2) #:leak 0.001 #:coefficient 2))))
  (check-not-exn (λ () (build '(enclosure hab #:at (0 0 0) #:size (2 2 2) #:pressure 50000 #:air '((o2 0.21) (n2 0.79)))
                              '(hearth stove #:at (0 0 0) #:heats hab #:power 1000 #:fuel 1)))))

(test-case "a dynamic channel (#36) is cut into 2 to 2000 cells"
  (check-compile-error #rx"#:cells is a whole number from 2 to 2000"
    (tank pond #:at (0 1 0) #:area 1 #:height 1 (port out #:height 0))
    (channel race #:from pond.out #:to off #:end (5 0 0) #:width 0.3 #:dynamic #t #:cells 1)))

(test-case "a door joins enclosures or outside, and two different ones"
  (check-compile-error #rx"shed is not an enclosure; a door joins two enclosures"
    (tank shed #:at (0 0 0) #:area 1 #:height 1)
    (enclosure hab #:at (0 0 0) #:size (2 2 2))
    (door d #:at (1 0 0) #:from hab #:to shed #:area 1.6))
  (check-compile-error #rx"an air-pump joins two different zones"
    (enclosure hab #:at (0 0 0) #:size (2 2 2))
    (air-pump p #:at (1 0 0) #:from hab #:to hab #:speed 0.05))
  (check-not-exn (λ () (expand-machine '(enclosure hab #:at (0 0 0) #:size (2 2 2))
                                       '(door d #:at (1 0 0) #:from hab #:to outside #:area 1.6)))))

(test-case "a pane goes in an enclosure's wall"
  (check-compile-error #rx"box is not an enclosure; a pane is glass in an enclosure's wall"
    (tank box #:at (0 0 0) #:area 1 #:height 1)
    (pane p #:at (0 1 0) #:on box #:side 0.3 #:thickness 0.0075)))
(test-case "a float (#29) rides in a tank, and fits it"
  (check-compile-error #rx"not a tank; a float rides in a tank's water"
    (post p #:at (0 0 0) #:size (1 1 1) #:material oak)
    (float f #:in p #:mass 1 #:area 0.01)))

(test-case "a drain (#90) runs into a tank"
  (check-not-exn
   (λ () (expand-machine '(tank cistern #:at (0 -1 0) #:area 1 #:height 1) '(drain grate #:at (0 0 0) #:into cistern #:perimeter 0.4))))
  (check-compile-error #rx"pier is not a tank; a drain runs into one"
    (post pier #:at (0 0 0) #:size (1 1 1) #:material oak)
    (drain grate #:at (0 0 0) #:into pier))
  (check-compile-error #rx"already a part named cistern"
    (tank cistern #:at (0 -1 0) #:area 1 #:height 1)
    (drain cistern #:at (0 0 0) #:into cistern)))
