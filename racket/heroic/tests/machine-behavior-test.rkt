#lang racket/base
;; Behaviour claims about the compiled machines, checked against the real
;; C# simulation through the headless HeroicInventions.SimHost — not
;; hand-computed expected numbers. These replace
;; tests/HeroicInventions.Sim.Tests/MachineTests.cs's
;; HeronsFountainBlueprintLiftsWaterAboveTheBasin and
;; AeolipileBlueprintSpinsOnceTheWaterBoils, which asserted the same
;; things from C# directly against MachineRuntime. See docs/design.html
;; §III "Machines as tests".
(require rackunit heroic/simhost)

(test-case "Heron's fountain lifts water above its basin, then empties the supply vessel"
  (define run (simulate 'herons-fountain #:seconds 60 #:step 0.05 #:sample-dt 0.5))
  (check-true (> (max-of run '(nozzle jet-height)) 0.0)
              "the jet should rise above the basin at some point during the run")
  ;; herons-fountain.rkt's supply vessel starts holding 1.5 L, and the
  ;; getter reports water in litres (MachineRuntime converts m³ * 1000).
  (check-true (< (final-of run '(supply water)) 1.5)
              "the supply vessel should empty through the nozzle"))

(test-case "The aeolipile spins once its water boils"
  (define run (simulate 'aeolipile #:seconds 200 #:step 0.01 #:sample-dt 1))
  (check-true (>= (final-of run '(kettle temperature)) 100)
              "the boiler should reach the boiling point")
  (check-true (> (final-of run '(ball rpm)) 100)
              "the rotor should be spinning fast once the water is boiling"))

(test-case "The Vitruvian screw carries its pocket volume up every turn"
  ;; the game feeds the screw's real turning speed in; here, 12 rpm
  (define run (simulate 'archimedes-screw #:seconds 20 #:step 0.01 #:sample-dt 1 #:set '((raise rpm 12))))
  (define per-turn (final-of run '(raise per-turn)))          ; litres
  (check-= per-turn 23.2 0.5 "Vitruvius's 4 m screw at a 3-4-5 slope: ~23 L a turn")
  ;; while the intake is fully under water the flow is per-turn × rpm / 60
  (check-= (max-of run '(raise flow)) (/ (* per-turn 12) 60) 0.05)
  ;; and every litre is accounted for: the 900 the pool started with plus
  ;; what the spring fed it (4.5 L/s for 20 s), in the pool, the trough, or
  ;; run on down the channel to the field
  (check-= (+ (final-of run '(pool water)) (final-of run '(trough water)) (final-of run '(field water))) (+ 900 (* 4.5 20)) 1e-6)
  (check-true (> (+ (final-of run '(trough water)) (final-of run '(field water))) 50)))

(test-case "A screw that isn't turning lifts nothing"
  (define run (simulate 'archimedes-screw #:seconds 5 #:step 0.01 #:sample-dt 1))
  (check-= (final-of run '(trough water)) 0 1e-9))

(test-case "Gallery: a pipe settles two tanks to equal surface height, not equal volume"
  ;; upper stands on a 0.6 m pier, both 0.1 m²: 120 L total, surfaces meet
  ;; at 0.6 + a = b  with a + b = 1.2 m of water column -> 30 L above, 90 L below
  (define run (simulate 'component-gallery #:seconds 3000 #:step 0.05 #:sample-dt 500))
  (check-= (final-of run '(upper water)) 30 0.01)
  (check-= (final-of run '(lower water)) 90 0.01))

(test-case "Gallery: the hearth burns exactly its fuel and hands the boiler what it released"
  (define run (simulate 'component-gallery #:seconds 400 #:step 0.05 #:sample-dt 50))
  (check-= (final-of run '(fire burned)) 0.05 1e-6)
  (check-= (final-of run '(fire energy)) (* 0.05 15) 1e-6 "50 g of wood at 15 MJ/kg")
  (check-= (final-of run '(fire fuel)) 0 1e-9)
  (check-true (>= (max-of run '(kettle temperature)) 100)))

(test-case "Gallery: the winding brook carries the spring's flow at the weir-predicted head"
  (define run (simulate 'component-gallery #:seconds 300 #:step 0.05 #:sample-dt 25))
  ;; Q = 1.705 b h^1.5 with b = 0.3 m, Q = 3 L/s  ->  h = 3.25 cm over the 30 cm lip
  (check-= (final-of run '(header level)) 33.25 0.1)
  (check-= (final-of run '(brook flow)) 3.0 1e-6)
  (check-= (final-of run '(tailrace flow)) 3.0 1e-6 "the pond passes on what the brook brings"))

;; ---------------------------------------------------------------------------
;; Machines checked against numbers worked out by hand (each .rkt file's
;; header shows the working). The simhost runs cover the fluid and thermal
;; side; rigid bodies and the noria's drag exist only in the Godot game, so
;; those are checked from its headless HEROIC_DEBUG_PHYSICS trace (skipped
;; where Godot isn't installed).

(require racket/runtime-path racket/system racket/port racket/list racket/string racket/file)

(define-runtime-path game-dir "../../../game")
(define godot-binary "/Applications/Godot_mono.app/Contents/MacOS/Godot")

;; The state lines "[12.50s] ..." of a headless run of a machine in Godot.
(define (godot-trace machine sim-seconds)
  (define out (open-output-string))
  (parameterize ([current-directory game-dir]
                 [current-output-port out]
                 [current-error-port (open-output-nowhere)])
    (putenv "HEROIC_DEBUG_PHYSICS" "1")
    (putenv "HEROIC_AUTORUN" "1")
    (putenv "HEROIC_AUTOSELECT" (symbol->string machine))
    (putenv "HEROIC_QUIT_AFTER_SIM_SECONDS" (number->string sim-seconds))
    (putenv "HEROIC_SPEED" "5")
    (system* godot-binary "--headless" "."))
  (for/list ([l (in-list (string-split (get-output-string out) "\n"))]
             #:when (regexp-match? #px"^\\[[0-9.]+s\\] " l))
    l))
(define (line-at lines t) ; the trace line stamped t seconds
  (or (for/first ([l (in-list lines)] #:when (string-prefix? l (format "[~as]" (real->decimal-string t 2)))) l)
      (error 'line-at "no trace line at ~a s" t)))
(define (field line rx) (string->number (cadr (regexp-match rx line))))

(test-case "Hearth engine: the kettle settles at the boiling point its 2 kW can sustain, and the ball at the rpm air drag allows"
  ;; 60 g wood x 15 MJ/kg = 0.9 MJ at 4 kW x 0.5 efficiency -> 2 kW for 225 s.
  ;; Steady state solved independently (nozzle flow at the pressure where
  ;; steam made = steam let out, rpm where nozzle thrust = air drag):
  ;; 105.928 C, 2742.4 rpm
  (define run (simulate 'hearth-engine #:seconds 200 #:step 0.01 #:sample-dt 25))
  (check-= (final-of run '(kettle temperature)) 105.928 0.005)
  (check-= (final-of run '(ball rpm)) 2742.4 1.0)
  (define done (simulate 'hearth-engine #:seconds 300 #:step 0.01 #:sample-dt 25))
  (check-= (final-of done '(fire energy)) 0.9 1e-6)
  (check-= (final-of done '(fire fuel)) 0 1e-9)
  (check-true (< (final-of done '(kettle temperature)) 100) "the kettle cools once the wood is gone")
  (check-true (< (final-of done '(ball rpm)) 1500) "and the ball slows"))

(test-case "Newcomen engine fed by a coal hearth: 2 kg x 24 MJ/kg x 0.25 heats the boiler, then the fire goes out"
  (define run (simulate 'newcomen-hearth #:seconds 60 #:step 0.01 #:sample-dt 10))
  (check-= (final-of run '(firebox burned)) 2.0 1e-6)
  (check-= (final-of run '(firebox energy)) 48 1e-6 "MJ released")
  (check-= (final-of run '(firebox lit)) 0 1e-9 "out after 2 kg / (1 MW / 24 MJ/kg) = 48 s")
  ;; 12 MJ reach 2000 kg of water at 104 C (4186 J/kg.K), less 2 W/K x 84 K x 48 s of loss
  (check-= (final-of run '(boiler temperature)) (+ 104 (/ (- 12e6 (* 2 84 48)) (* 2000 4186))) 0.01))

(test-case "Newcomen engine fed by a hearth pumps in Godot, and keeps pumping on stored heat after the fire is out"
  (when (file-exists? godot-binary)
    (define lines (godot-trace 'newcomen-hearth 100))
    (define bore-stroke-litres (* 1000 (/ 3.141592653589793 4) 0.185 0.185 1.8)) ; 48.4 L a stroke
    (define at-50 (line-at lines 50)) (define at-100 (line-at lines 100))
    (check-true (> (field at-50 #px"strokes=(\\d+)") 10))
    (check-true (> (field at-100 #px"strokes=(\\d+)") (field at-50 #px"strokes=(\\d+)"))
                "still stroking after the fire went out at 48 s")
    ;; the cistern holds one pump bore x stroke per completed stroke; the
    ;; stroke counter runs up to two ahead of the delivered water (a stroke
    ;; is counted as it starts)
    (define delivered (/ (field at-100 #px"cistern=(\\d+)L") bore-stroke-litres))
    (define strokes (field at-100 #px"strokes=(\\d+)"))
    (check-true (< (- strokes 2.5) delivered (+ strokes 0.5))
                (format "~a strokes but ~a bore-stroke volumes in the cistern" strokes delivered))))

(test-case "Post-and-lintel crane: the 116 kg counterweight (63.7 kg.m) beats the load side (about 46 kg.m) and lifts the load"
  (when (file-exists? godot-binary)
    (define lines (godot-trace 'post-and-lintel-crane 20))
    (define end (last lines))
    ;; the beam runs to its 15 degree stop
    (check-= (field end #px"beam pos=\\([^)]*\\) rotZ=(-?[0-9.]+)") 15.0 0.3)
    ;; the load began 1.11 m up (1 m pivot + 12.5 mm + 10 cm); at the stop it
    ;; can be at most 1.8 sin 15 + 0.109 above the pivot, i.e. 1.575 m, and has
    ;; slid a little inward down the tilt: 1.5 m or more
    (define y (field end #px"load pos=\\([^,]*,([0-9.]+),"))
    (check-true (< 1.45 y 1.58) (format "load height ~a" y))))

(test-case "Water mill race: weir, Manning brook, and the wheel's lift, at the wheel's hand-solved speed"
  ;; 8 L/s over a 30 cm weir: h = (Q / 1.705 b)^(2/3) = 6.254 cm
  ;; brook 10.0 m long, S = 0.0799, n = 0.015: 2.055 cm deep, 1.2977 m/s
  ;; wheel: drag torque balances lift torque at 3.0 rpm (2.9995) -> 4.587 L/s
  (define run (simulate 'water-mill-race #:seconds 100 #:step 0.02 #:sample-dt 20
                        #:set '((raise rpm 2.9995))))
  (check-= (final-of run '(header level)) 36.254 0.05 "30 cm lip + 6.254 cm")
  (check-= (final-of run '(brook flow)) 8.0 1e-4)
  (check-= (final-of run '(brook depth)) 2.055 0.005)
  (check-= (final-of run '(brook velocity)) 1.2977 0.001)
  (check-= (final-of run '(raise flow)) 4.587 0.005)
  (check-= (final-of run '(raise load-torque)) 113.3 0.2 "rho g H V / 2 pi")
  (check-= (final-of run '(pond level)) 42.93 0.05 "tail weir: 40 cm + 2.93 cm over the lip for 3.41 L/s")
  (check-= (final-of run '(tailrace flow)) 3.413 0.02))

(test-case "Water mill race: in Godot the current on the paddles turns the wheel at the solved 3 rpm"
  (when (file-exists? godot-binary)
    (define lines (godot-trace 'water-mill-race 60))
    (define (angle t) (field (line-at lines t) #px"mill-wheel pos=\\([^)]*\\) rotZ=(-?[0-9.]+)"))
    ;; degrees turned in 1 s at 55 s, unwrapped: 3 rpm = 18 deg/s
    (define turned (let ([d (- (angle 56) (angle 55))]) (cond [(< d -180) (+ d 360)] [(> d 180) (- d 360)] [else d])))
    (check-= (/ turned 360 (/ 1 60)) 3.0 0.15)
    (check-= (field (line-at lines 55) #px"raise ([0-9.]+)rpm") 3.0 0.15)))

(test-case "Water clock: a constant-head reservoir makes the receiver rise at a steady 2.967 mm/s"
  ;; surface settles where spill (0.5 - Qout L/s over a 20 cm lip) and
  ;; outflow C (surface - receiver top), C = 1.67e-4, agree: 1.9108 m,
  ;; Qout = 0.1187 L/s; over the 0.04 m2 receiver, 2.967 mm/s
  (define (at seconds) (simulate 'water-clock #:seconds seconds #:step 0.02 #:sample-dt 50))
  (check-= (final-of (at 100) '(reservoir level)) 41.08 0.01 "the head does not sag")
  (check-= (final-of (at 300) '(reservoir level)) 41.08 0.01)
  (check-= (final-of (at 100) '(receiver level)) 29.67 0.05)
  (check-= (final-of (at 300) '(receiver level)) 89.02 0.05 "cm: three times as long, three times as high")
  (check-= (final-of (at 300) '(outflow flow)) 0.1187 1e-4))

(test-case "Castellum aquae: the falling tank shuts the highest pipe first and settles on the priority pipes"
  ;; Level integrated by hand from 1.1 m with 6.7 L/s coming in and pipes of
  ;; conductance 1.0, 0.8, 0.5 L/s per m at ports 10, 50, 90 cm: the houses'
  ;; pipe runs until the level drops through 0.9 m (64.1 L delivered), then
  ;; fountains and baths alone carry the supply, S -> 0.722 m
  (define run (simulate 'castellum-aquae #:seconds 300 #:step 0.02 #:sample-dt 50))
  (check-= (final-of run '(aqueduct flow)) 6.7 1e-3)
  (check-= (final-of run '(houses water)) 64.1 0.5 "litres, and no more after the level passed 0.9 m")
  (check-= (final-of run '(to-houses flow)) 0 1e-9)
  (check-= (final-of run '(fountains water)) 1153.2 2.0)
  (check-= (final-of run '(baths water)) 922.5 2.0)
  (check-= (final-of run '(castellum level)) 77.55 0.2)
  (check-true (> (final-of run '(to-fountains flow)) (final-of run '(to-baths flow))) "the lowest pipe gets most"))
