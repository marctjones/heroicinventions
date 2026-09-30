#lang racket/base
;; Behaviour claims about the compiled machines, checked against the real
;; C# simulation through the headless HeroicInventions.SimHost — not
;; hand-computed expected numbers. These replace
;; tests/HeroicInventions.Sim.Tests/MachineTests.cs's
;; HeronsFountainBlueprintLiftsWaterAboveTheBasin and
;; AeolipileBlueprintSpinsOnceTheWaterBoils, which asserted the same
;; things from C# directly against MachineRuntime. See docs/design.html
;; §III "Machines as tests".
(require rackunit heroic/simhost (only-in racket/math pi))

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
;; side; rigid bodies, ropes and the noria's drag exist only in the Godot
;; game, so those are checked through heroic/godothost, which runs the real
;; game headless and returns frames of the same shape (skipped where Godot
;; isn't installed).

(require heroic/godothost racket/list racket/string)

;; A run's value of target.field in the frame nearest t seconds.
(define (value-at run path t)
  (define key (string->symbol (format "~a.~a" (car path) (cadr path))))
  (define frame (for/fold ([best (car run)]) ([f (cdr run)])
                  (if (< (abs (- (car f) t)) (abs (- (car best) t))) f best)))
  (cadr (assq key (cdr frame))))

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

(test-case "Bellows forge: forced draught burns a fire faster in exact proportion, but hands over no more heat"
  ;; Natural draught (no bellows): a 5 kW wood fire's own steady burn,
  ;; 5000 / 15e6 = 1/3000 kg/s, draws 1/3000 x 6 (wood's air-fuel ratio)
  ;; = 1/500 kg/s of air on its own. The bellows forces in 5 L/s, which at
  ;; air's density (Patm / (Rair x 293.15 K) = 101325 / (287.05 x 293.15)
  ;; = 1.204118 kg/m3) is 1.204118 x 0.005 = 0.0060206 kg/s more -- draught
  ;; (1/500 + 0.0060206) / (1/500) = 4.010296 times the air, so 4.010296
  ;; times the burn rate too.
  ;;
  ;; Early (t=100 s, both still burning): bare has burned 100/3000 =
  ;; 0.033333 kg; blown, 4.010296x that, 0.133677 kg.
  (define early (simulate 'bellows-forge #:seconds 100 #:step 0.01 #:sample-dt 10))
  (check-= (final-of early '(blown draught)) 4.010296 5e-6)
  (check-= (final-of early '(bare draught)) 1 1e-9 "no bellows on it, so no forced draught at all")
  (check-= (final-of early '(bare burned)) (/ 100.0 3000) 1e-6)
  (check-= (final-of early '(blown burned)) (* (/ 100.0 3000) 4.010296) 1e-5)
  (check-= (final-of early '(bare lit)) 1 1e-9)
  (check-= (final-of early '(blown lit)) 1 1e-9)

  ;; Run both out entirely: bare takes 3000 x 15e6 / 5000 = 3000 s exactly;
  ;; blown, 3000 / 4.010296 = 748.07 s. Both give up the same 1 kg x 15 MJ/kg
  ;; = 15 MJ, whichever forge burns it and however long that takes.
  (define done (simulate 'bellows-forge #:seconds 3200 #:step 0.01 #:sample-dt 100))
  (check-= (final-of done '(bare burned)) 1.0 1e-6)
  (check-= (final-of done '(blown burned)) 1.0 1e-6)
  (check-= (final-of done '(bare energy)) 15 1e-6 "MJ")
  (check-= (final-of done '(blown energy)) 15 1e-6 "MJ, same as the bare forge")
  (check-= (final-of done '(bare lit)) 0 1e-9 "out at 3000 s")
  (check-= (final-of done '(blown lit)) 0 1e-9 "out long before, at 748.07 s"))

(test-case "Windmills: at their best load the sails take Cp of the wind, and power goes as the wind speed cubed"
  ;; R = 10 m, A = 314.159 m2, air 1.204118 kg/m3, Cp* = 0.3 at lambda* = 2.5.
  ;; Each mill's stones are set to tau0 = 1/2 rho A v^2 R Cp*/lambda*, where
  ;; the sails' torque tau0 (2 - lambda/lambda*) balances them at lambda*:
  ;;   breeze, 6 m/s: omega = 2.5 x 6 / 10 = 1.5 rad/s = 14.3239 rpm;
  ;;                  1/2 rho A v^3 = 40854.77 W, x 0.3 = 12256.43 W
  ;;   gale,   9 m/s: omega = 2.25 rad/s = 21.4859 rpm; 137884.86 W x 0.3 = 41365.46 W
  ;; and 41365.46 / 12256.43 = (9/6)^3 = 3.375.
  (define run (simulate 'windmills #:seconds 200 #:step 0.01 #:sample-dt 10))
  (check-= (final-of run '(breeze rpm)) 14.3239 1e-4)
  (check-= (final-of run '(gale rpm)) 21.4859 1e-4)
  (check-= (final-of run '(breeze power)) 12256.43 0.01)
  (check-= (final-of run '(gale power)) 41365.46 0.01)
  (check-= (final-of run '(breeze cp)) 0.3 1e-6)
  (check-= (final-of run '(gale cp)) 0.3 1e-6)
  (check-= (/ (final-of run '(gale power)) (final-of run '(breeze power))) 3.375 1e-6 "(9/6)^3")
  (check-true (<= (max-of run '(breeze cp)) 16/27) "never above the Betz limit")
  (check-true (<= (max-of run '(gale cp)) 16/27) "never above the Betz limit"))

(test-case "Capstans: a pull holds e^(mu theta) times itself -- half a turn slips, one turn just holds, two turns barely need a hand"
  ;; hemp on oak, mu = sqrt(0.5 x 0.45) = 0.474342; the load weighs 200 x 9.81 = 1962 N.
  ;;   half turn: e^(mu pi) = 4.437931, 100 N holds 443.79 N < 1962 N: the load
  ;;              runs out at 443.79/200 - 9.81 = -7.591035 m/s2, 0.94888 m in 0.5 s
  ;;   one turn:  e^(2 mu pi) = 19.695230; least hold 1962/19.695 = 99.618 N <= 100: held
  ;;   two turns: e^(4 mu pi) = 387.902089; least hold 5.057977 N
  (define run (simulate 'capstans #:seconds 0.5 #:step 0.001 #:sample-dt 0.05))
  (check-= (final-of run '(half-turn mu)) 0.474342 1e-6)
  (check-= (final-of run '(half-turn ratio)) 4.437931 1e-6)
  (check-= (final-of run '(one-turn ratio)) 19.695230 1e-6)
  (check-= (final-of run '(two-turns ratio)) 387.902089 1e-5)
  (check-= (final-of run '(half-turn held)) 0 1e-9)
  (check-= (final-of run '(half-turn load-tension)) 443.7931 1e-4 "the load's end is the tight one")
  (check-= (final-of run '(half-turn speed)) (* -7.591035 0.5) 1e-5)
  (check-= (final-of run '(half-turn lowered)) 0.948879 1e-5)
  (check-= (final-of run '(one-turn held)) 1 1e-9)
  (check-= (final-of run '(one-turn least-hold)) 99.618029 1e-5)
  (check-= (final-of run '(one-turn lowered)) 0 1e-12)
  (check-= (final-of run '(two-turns least-hold)) 5.057977 1e-6)
  (check-= (final-of run '(two-turns held)) 1 1e-9)
  ;; the half-turn load lands at t = sqrt(2 x 1.5 / 7.591) = 0.6287 s and stays down
  (define later (simulate 'capstans #:seconds 2 #:step 0.001 #:sample-dt 0.5))
  (check-= (final-of later '(half-turn grounded)) 1 1e-9)
  (check-= (final-of later '(half-turn lowered)) 1.5 1e-9))

(test-case "Capstans: the same friction fights a haul -- in over half a turn takes e^(mu pi) times the weight"
  ;; Hauling in, the sailor's end is the tight one: the load feels 10000 /
  ;; 4.437931 = 2253.30 N, rising at 2253.30/200 - 9.81 = 1.456512 m/s2, 0.728256 m in 1 s.
  ;; And the one-turn sailor letting go to 99 N (below its 99.618 N least hold) loses the load.
  (define run (simulate 'capstans #:seconds 1 #:step 0.001 #:sample-dt 0.1
                        #:set '((half-turn hold 10000) (one-turn hold 99))))
  (check-= (final-of run '(half-turn load-tension)) 2253.3024 1e-3)
  (check-= (final-of run '(half-turn speed)) 1.456512 1e-5)
  (check-= (final-of run '(half-turn hauled)) 0.728256 1e-5)
  ;; one turn at 99 N: the load's end tight at 99 x 19.695230 = 1949.8278 N,
  ;; creeping out at 1949.8278/200 - 9.81 = -0.060861 m/s2
  (check-= (final-of run '(one-turn held)) 0 1e-9)
  (check-= (final-of run '(one-turn speed)) -0.060861 1e-6))

(test-case "Winter night: a copper cools to the ambient by Newton's law, and ice grows as the square root of time"
  ;; C = 5 x 4186 = 20930 J/K, k = 2 W/K, tau = C/k = 10465 s:
  ;;   T = -10 + 100 exp(-3600/10465) = 60.8926 C after an hour
  ;; Stefan: h = sqrt(2 x 2.22 x 10 x 3600 / (917 x 334000)) = 22.8447 mm,
  ;; taking 22.8447 x 0.917 = 20.9486 L of the cistern's 500 L: 479.0514 L left.
  (define run (simulate 'winter-night #:seconds 3600 #:step 0.1 #:sample-dt 600))
  (check-= (final-of run '(scene ambient)) -10 1e-12)
  (check-= (final-of run '(scene air-density)) 1.341392 1e-6 "101325 / (287.05 x 263.15)")
  (check-= (final-of run '(copper temperature)) 60.8926 1e-3)
  (check-= (final-of run '(cistern ice)) 22.8447 1e-3)
  (check-= (final-of run '(cistern water)) 479.0514 1e-3)
  (check-= (final-of run '(seep evaporated)) 0 1e-12 "iced over: nothing evaporates")
  ;; four hours: twice as thick (sqrt 4)
  (define later (simulate 'winter-night #:seconds 14400 #:step 0.5 #:sample-dt 3600))
  (check-= (final-of later '(cistern ice)) (* 2 22.8447) 2e-3))

(test-case "Winter night, moved indoors and into summer: the same copper in a 20 C room, the seep at 30 C"
  ;; at 20 C: T = 20 + 70 exp(-3600/10465) = 69.6248 C; no ice.
  (define room (simulate 'winter-night #:seconds 3600 #:step 0.1 #:sample-dt 600 #:set '((scene ambient 20))))
  (check-= (final-of room '(copper temperature)) 69.6248 1e-3)
  (check-= (final-of room '(cistern ice)) 0 1e-12)
  (check-= (final-of room '(seep evaporated)) 3.6 1e-6 "0.001 L/s at 20 C, for an hour")
  ;; at 30 C the seep runs as water's vapour pressure: p_sat(30)/p_sat(20) = 1.816500 (Antoine)
  (define summer (simulate 'winter-night #:seconds 1000 #:step 0.1 #:sample-dt 500 #:set '((scene ambient 30))))
  (check-= (final-of summer '(seep evaporated)) 1.816500 1e-5))

(test-case "Heliostats: the sun's place and beam, and a mirror's cos(theta/2)"
  ;; Alexandria 31.2 N, day 172: declination 23.449783; at noon elevation
  ;; 90 - 31.2 + 23.4498 = 82.249783 deg due south (azimuth 180); air mass
  ;; (Kasten-Young) 1.008882; beam 1361 x 0.7^(1.008882^0.678) = 950.6588 W/m2.
  ;; The mirrors' cosines from their positions: north 0.832349, south 0.750003;
  ;; power 950.6588 x 0.5 x 0.85 x cos = 336.2940 and 303.0235 W. With the sun
  ;; held still the boilers (1 kg, 2 W/K to 20 C air) warm as
  ;; 20 + (P/2)(1 - exp(-2 t / 4186)): 24.75183 and 24.28172 C at 60 s.
  (define run (simulate 'heliostats #:seconds 60 #:step 0.01 #:sample-dt 10 #:set '((scene clock-rate 0))))
  (check-= (final-of run '(scene sun-elevation)) 82.249783 1e-5)
  (check-= (final-of run '(scene sun-azimuth)) 180 1e-6)
  (check-= (final-of run '(scene irradiance)) 950.658756 1e-4)
  (check-= (final-of run '(north-mirror cosine)) 0.832349 1e-6)
  (check-= (final-of run '(south-mirror cosine)) 0.750003 1e-6)
  (check-= (final-of run '(north-mirror power)) 336.294015 1e-4)
  (check-= (final-of run '(south-mirror power)) 303.023514 1e-4)
  (check-= (final-of run '(north-lit temperature)) 24.751832 1e-4)
  (check-= (final-of run '(south-lit temperature)) 24.281720 1e-4))

(test-case "Heliostats: morning, night, and the clock"
  ;; 9:00 solar time: hour angle -45 deg, elevation 49.554775 deg nearly due east
  ;; (azimuth 89.616622), air mass 1.312800, beam 886.2691 W/m2; the mirrors now
  ;; see the sun from the side: 290.7759 and 291.7973 W. At 22:00 the sun is down.
  (define morning (simulate 'heliostats #:seconds 1 #:step 0.01 #:sample-dt 1 #:set '((scene clock-rate 0) (scene time 9))))
  (check-= (final-of morning '(scene sun-elevation)) 49.554775 1e-5)
  (check-= (final-of morning '(scene sun-azimuth)) 89.616622 1e-5)
  (check-= (final-of morning '(scene irradiance)) 886.269073 1e-4)
  (check-= (final-of morning '(north-mirror power)) 290.775853 1e-4)
  (check-= (final-of morning '(south-mirror power)) 291.797264 1e-4)
  (define night (simulate 'heliostats #:seconds 1 #:step 0.01 #:sample-dt 1 #:set '((scene clock-rate 0) (scene time 22))))
  (check-= (final-of night '(scene irradiance)) 0 1e-12)
  (check-= (final-of night '(north-mirror power)) 0 1e-12)
  ;; left to run, the sun's clock keeps the simulation's time: an hour after noon is 13:00
  (define hour (simulate 'heliostats #:seconds 3600 #:step 0.5 #:sample-dt 600))
  (check-= (final-of hour '(scene time)) 13 1e-9))

(test-case "Windmills: stones set for a lighter wind let the sails run fast and take less of it"
  ;; The gale mill ground against the breeze mill's 8170.95 N m, a 4/9 of
  ;; its own tau0: lambda = 2.5 (2 - 4/9) = 3.8889, omega = 3.8889 x 9/10 =
  ;; 3.5 rad/s = 33.4225 rpm; Cp = 0.3 (lambda/lambda*)(2 - lambda/lambda*)
  ;; = 0.3 x 1.5556 x 0.4444 = 0.207407, 28598.34 W -- more power than the
  ;; breeze mill gets, but a smaller share of a stronger wind.
  (define run (simulate 'windmills #:seconds 200 #:step 0.01 #:sample-dt 10
                        #:set '((gale load 8170.9543946348695))))
  (check-= (final-of run '(gale tsr)) 3.888889 1e-5)
  (check-= (final-of run '(gale rpm)) 33.4225 1e-4)
  (check-= (final-of run '(gale cp)) 0.207407 1e-6)
  (check-= (final-of run '(gale power)) 28598.34 0.01))

(test-case "Newcomen engine fed by a hearth pumps in Godot, and keeps pumping on stored heat after the fire is out"
  (when (godot-available?)
    (define run (godot-simulate 'newcomen-hearth #:seconds 100 #:sample-dt 10))
    (define bore-stroke-litres (* 1000 (/ 3.141592653589793 4) 0.185 0.185 1.8)) ; 48.4 L a stroke
    (check-true (> (value-at run '(cylinder strokes) 50) 10))
    (check-true (> (final-of run '(cylinder strokes)) (value-at run '(cylinder strokes) 50))
                "still stroking after the fire went out at 48 s")
    ;; the cistern holds one pump bore x stroke per completed stroke; the
    ;; stroke counter runs up to two ahead of the delivered water (a stroke
    ;; is counted as it starts)
    (define delivered (/ (final-of run '(cistern water)) bore-stroke-litres))
    (define strokes (final-of run '(cylinder strokes)))
    (check-true (< (- strokes 2.5) delivered (+ strokes 0.5))
                (format "~a strokes but ~a bore-stroke volumes in the cistern" strokes delivered))))

(test-case "Post-and-lintel crane: the 116 kg counterweight (63.7 kg.m) beats the load side (about 46 kg.m) and lifts the load"
  (when (godot-available?)
    (define run (godot-simulate 'post-and-lintel-crane #:seconds 20 #:sample-dt 1))
    ;; the beam runs to its 15 degree stop
    (check-= (final-of run '(beam rot-z)) 15.0 0.3)
    ;; the load began 1.11 m up (1 m pivot + 12.5 mm + 10 cm); at the stop it
    ;; can be at most 1.8 sin 15 + 0.109 above the pivot, i.e. 1.575 m, and has
    ;; slid a little inward down the tilt: 1.5 m or more
    (define y (final-of run '(load y)))
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
  (when (godot-available?)
    (define run (godot-simulate 'water-mill-race #:seconds 60 #:sample-dt 5))
    ;; 3 rpm = 0.31416 rad/s
    (check-= (* (value-at run '(mill-wheel omega) 55) (/ 60 (* 2 pi))) 3.0 0.15)
    (check-= (value-at run '(raise rpm) 55) 3.0 0.15)))

(test-case "Fall and swing, in Jolt: a block falls at g, and a pendulum keeps Huygens' time"
  (when (godot-available?)
    (define run (godot-simulate 'fall-and-swing #:seconds 10.5 #:sample-dt (/ 1 120)))
    ;; The engine steps at 120 Hz by symplectic Euler, and damps every body
    ;; by 0.1 per second (Godot's default; not physics -- #33 is to replace
    ;; it with air drag). The block starts one tick in. So, tick by tick,
    ;; v <- v (1 - 0.1 dt) - g dt, y <- y + v dt: after the first tick it
    ;; accelerates at g(1 - c dt) = 9.8018 m/s2, and stands at 3.8134 m at
    ;; 0.5 s (plain 5 - g t^2/2 would be 3.7738 m, the damping costing 4 cm).
    (define dt 1/120)
    (define-values (v y)
      (for/fold ([v 0.0] [y 5.0]) ([n (in-range 2 61)])
        (define v* (- (* v (- 1 (* 0.1 dt))) (* 9.81 dt)))
        (values v* (+ y (* v* dt)))))
    (check-= (value-at run '(drop vy) (* 2 dt)) (- (* 9.81 dt)) 1e-6 "one tick of g: the engine's gravity is the sim's 9.81")
    (check-= (/ (- (value-at run '(drop vy) (* 2 dt)) (value-at run '(drop vy) (* 3 dt))) dt)
             (* 9.81 (- 1 (* 0.1 dt))) 5e-3 "g less a tick's damping")
    (check-= (value-at run '(drop y) 0.5) y 2e-3)
    (check-= (value-at run '(drop vy) 0.5) v 2e-3)
    ;; The pendulum: I/(m d) = 0.979641 m for a 1 m, 1 cm rod and an 8 cm
    ;; ball of one metal, so 2 pi sqrt(0.979641/9.81) = 1.985541 s for small
    ;; swings, x (1 + theta^2/16) = 1.986486 s from 5 degrees (Huygens). The
    ;; damping takes e^(-0.1 T / 2) = 0.9055 off each swing's height.
    (define ts (times-of run))
    (define zs (values-of run '(swing rot-z)))
    (define crossings ; downward through the vertical, interpolated between frames
      (for/list ([t0 ts] [t1 (cdr ts)] [a zs] [b (cdr zs)] #:when (and (> a 0) (<= b 0)))
        (+ t0 (* (- t1 t0) (/ a (- a b))))))
    (check-= (- (second crossings) (first crossings)) 1.986486 1e-3 "the first swing's period")
    (define peaks
      (for/list ([a zs] [b (cdr zs)] [c (cddr zs)] #:when (and (>= b a) (> b c) (> b 0))) b))
    (check-= (/ (second peaks) (first peaks)) (exp (* -0.1 1.9865 1/2)) 2e-3 "each swing's height, damped")))

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

(test-case "Sluice gate: all of the spring runs under it, at the orifice head Q = 0.6 w a sqrt(2gh) predicts"
  ;; 20 L/s through a 30 cm wide slot a = opening x 1 m needs
  ;; h = (Q / 0.6 w a)^2 / 2g above the slot's middle (sluice-demo.rkt).
  ;; Levels are read just after each step's outflow, Q dt / A lower than
  ;; the surface the gate sees: 0.04 cm in the 0.5 m2 pool.
  (for ([opening '(0.08 0.05 0.03)] [head '(9.832 25.170 69.916)])
    (define run (simulate 'sluice-demo #:seconds 600 #:step 0.02 #:sample-dt 100 #:set `((gate opening ,opening))))
    (define q (final-of run '(gate flow)))
    (define h (final-of run '(gate head)))
    (check-= q 20.0 1e-3 (format "opening ~a: the whole spring passes the gate" opening))
    (check-= h head 0.01 (format "opening ~a: predicted head over the slot" opening))
    (check-= (final-of run '(pool level)) (+ 10 (* 100 opening 1/2) head -0.04) 0.01 "sill 10 cm up the pool")
    (check-= q (* 1000 0.6 0.3 opening (sqrt (* 2 9.81 (/ h 100)))) 1e-3 "flow and traced head obey the orifice law")
    (check-= (final-of run '(waste-weir flow)) 0 1e-9 "nothing spills to waste")
    (check-= (final-of run '(race flow)) 20.0 1e-3)))

(test-case "Sluice gate: cracked to 2 cm it can't pass the spring, and the pool backs up over its waste weir"
  ;; 0.0036 sqrt(2g (s - 0.01)) + 1.705 (0.3) (s - 0.80)^1.5 = 0.020 at s = 84.81 cm over the sill
  (define run (simulate 'sluice-demo #:seconds 600 #:step 0.02 #:sample-dt 100 #:set '((gate opening 0.02))))
  (check-= (final-of run '(gate flow)) 14.60 0.01)
  (check-= (final-of run '(waste-weir flow)) 5.40 0.01)
  (check-= (final-of run '(gate head)) 83.81 0.05)
  (check-= (final-of run '(pool level)) (- 94.81 0.04) 0.05))

(test-case "Sluice gate: shut, the race runs dry at once and the reach below drains as 1/sqrt(h) = 1/sqrt(h0) + 0.2558 t"
  ;; the reach starts at its 20 L/s depth, 11.52 cm; its 1 m2 drains over
  ;; a 30 cm floor-level outfall, dh/dt = -(1.705 b / A) h^1.5
  (define run (simulate 'sluice-demo #:seconds 120 #:step 0.01 #:sample-dt 1
                        #:set '((gate opening 0) (reach water 115.201))))
  (check-= (max-of run '(gate flow)) 0 1e-12 "nothing passes a shut gate")
  (check-= (max-of run '(race flow)) 0 1e-12 "the race below it never runs")
  (check-= (max-of run '(race depth)) 0 1e-12 "and stands dry")
  (check-= (value-at run '(reach level) 10) 3.301 0.01)
  (check-= (value-at run '(reach level) 30) 0.887 0.005)
  (check-= (value-at run '(run flow) 10) 3.068 0.01 "L/s over the outfall")
  (check-= (value-at run '(run flow) 30) 0.427 0.005)
  (check-true (> (value-at run '(reach level) 110) 0.1) "not yet down to 1 mm at 110 s")
  (check-true (< (value-at run '(reach level) 115) 0.1) "below 1 mm by 115 s (predicted 112 s)")
  ;; and the pool backs up to the waste weir: 80 cm + 11.52 cm over the sill, 20 L/s spilling
  (check-= (final-of run '(waste-weir flow)) 20.0 0.01)
  (check-= (final-of run '(pool level)) (- 101.52 0.04) 0.02))

;; ---- #10 quenching and #11 feed water (fire-and-water.rkt)
(define quench-heat (+ (* 4186 80) 2.257e6))   ; J/kg: 20 C water warmed and boiled away

;; The first time t in a run at which target.field reaches at least v.
(define (first-time-at-least run path v)
  (define key (string->symbol (format "~a.~a" (car path) (cadr path))))
  (for/first ([f run] #:when (>= (cadr (assq key (cdr f))) v)) (car f)))

(test-case "Quench: water faster than a fire can boil it off soaks in and drowns it"
  (define q 0.020) (define p 20000) (define fuel0 2) (define density 15e6)
  ;; what the cistern holds over its 5 cm lip while passing q: h = (q / 1.705 b)^(2/3)
  (define stored (* 0.01 (expt (/ (/ q 1000) (* 1.705 0.05)) 2/3) 1000))
  (define boil-rate (/ p quench-heat))
  (define predicted (/ (+ fuel0 stored) (+ (- q boil-rate) (/ p density))))
  (define run (simulate 'fire-and-water #:seconds 200 #:step 0.01 #:sample-dt 0.5))
  (define out (first-time-at-least run '(campfire drowned) 1))
  (check-= out predicted (* 0.01 predicted) (format "drowned at ~a s, predicted ~a s" out predicted))
  ;; drowned, it burns no more: fuel burned is what p/density burns until then
  (check-= (final-of run '(campfire burned)) (* (/ p density) out) 0.005)
  ;; everything it boiled off took all its heat: the pot never got warmer than it cools
  (check-true (< (max-of run '(pot temperature)) 20.5))
  (check-= (final-of run '(campfire boiled)) (/ (* p out) quench-heat) 0.02))

(test-case "Quench: a fire hot enough to boil off all that arrives stays lit, heating with what's left"
  (define run (simulate 'fire-and-water #:seconds 100 #:step 0.01 #:sample-dt 5 #:set '((campfire power 60000))))
  (check-= (final-of run '(campfire drowned)) 0 1e-9)
  (check-= (final-of run '(campfire soak)) 0 (* 0.020 0.01) "it boils every drop, within the one step's water landing after it burns")
  (check-= (final-of run '(pot fire)) (* (- 60000 (* 0.020 quench-heat)) 0.3) 1.0 "W to the pot")
  (check-= (final-of run '(campfire burned)) (* (/ 60000 15e6) 100) 1e-6))

(test-case "Feed water mixes into a boiler by energy balance"
  ;; stove out: 4 kg at 90 C takes the feed tank's 2 L (all it holds over its lip) at 20 C
  (define c 4186)
  (define run (simulate 'fire-and-water #:seconds 60 #:step 0.01 #:sample-dt 5 #:set '((stove fuel 0))))
  (define fed (final-of run '(copper fed)))
  (check-= fed 2.0 0.002 "kg: the feed tank empties to its lip")
  (define mixed (/ (+ (* 4 90) (* 2 20)) 6))
  ;; the copper's only other exchange is what it lost to the air
  (define lost-k (/ (* 1000 (final-of run '(copper lost))) (* (+ 4 fed) c)))
  (check-= (+ (final-of run '(copper temperature)) lost-k) mixed 0.01
           (format "mixes to ~a C, less the ~a K lost to the air" mixed lost-k))
  (check-true (< lost-k 0.5)))

(test-case "Feed water: the fire needs the heat to warm it before the copper boils again"
  ;; 6 kg from 20 C to 100 C, less the 4 kg at 90 C it had: 837.2 kJ, at 5 kW x 0.4 = 2 kW
  (define c 4186)
  (define need (- (* 6 c 80) (* 4 c 70)))
  (define run (simulate 'fire-and-water #:seconds 600 #:step 0.01 #:sample-dt 1))
  (define t100 (first-time-at-least run '(copper temperature) 100))
  (define lost (* 1000 (final-of (simulate 'fire-and-water #:seconds t100 #:step 0.01 #:sample-dt 1) '(copper lost))))
  (check-= t100 (/ (+ need lost) 2000) 1.0 (format "boils again at ~a s" t100))
  (check-true (< (/ need 2000) t100 (/ need (- 2000 (* 2 80)))) "between no loss and the worst-case loss"))

;; ---- #12 water wheels (water-wheels.rkt)
(define (rpm->rad rpm) (/ (* rpm 2 pi) 60))

(test-case "Overshot wheel: the water's weight gives rho g Q r (1 - cos theta), and the millstone sets the speed"
  (define q 0.020) (define r 1.5) (define theta (* 2/3 pi)) (define load 300)
  (define power (* 1000 9.81 q r (- 1 (cos theta))))
  (define omega (/ power load))
  (define run (simulate 'water-wheels #:seconds 300 #:step 0.02 #:sample-dt 50))
  (check-= (final-of run '(race flow)) 20 1e-3 "the header passes the spring")
  (check-= (rpm->rad (final-of run '(overshot rpm))) omega 1e-4)
  (check-= (final-of run '(overshot power)) power 0.05 "W")
  ;; steady water on the arc, rho Q theta / omega, sampled after the step's
  ;; buckets tip: one substep's pour (q x 10 ms) lower
  (check-= (final-of run '(overshot water)) (- (/ (* 1000 q theta) omega) (* 1000 q 0.01)) 0.01)
  (check-= (final-of run '(overshot overflow)) 0 1e-9 "the buckets never brim")
  ;; efficiency against the whole fall: race lip 3.3 m, wheel bottom 0.2 m
  (define efficiency (/ power (* 1000 9.81 q (- 3.3 0.2))))
  (check-= efficiency (/ (* r (- 1 (cos theta))) 3.1) 1e-9)
  (check-true (< 0.63 efficiency 0.78) "within Smeaton's measured overshot range"))

(test-case "Overshot wheel: loaded past what brimming buckets can turn, it stalls"
  ;; 24 buckets x 10 L over 120 of 360 degrees: 80 kg, turning 80 g r (1 - cos theta) / theta
  (define theta (* 2/3 pi))
  (define most (/ (* 80 9.81 1.5 (- 1 (cos theta))) theta))
  (define run (simulate 'water-wheels #:seconds 120 #:step 0.02 #:sample-dt 10 #:set `((overshot load ,(+ most 20)))))
  (check-= (final-of run '(overshot rpm)) 0 1e-9)
  (check-= (final-of run '(overshot water)) 80 1e-6 "kg: every bucket on the arc full")
  (check-= (final-of run '(overshot torque)) most 1e-3)
  (check-true (> (final-of run '(overshot overflow)) 100) "the rest spills as it arrives"))

(test-case "Undershot wheel: the current's push settles it at u = v - sqrt(tau / rho A r)"
  (define run (simulate 'water-wheels #:seconds 300 #:step 0.02 #:sample-dt 50))
  (define v (final-of run '(mill-race velocity)))
  (define area (* 0.6 (/ (final-of run '(mill-race depth)) 100)))   ; paddles deeper than the race
  (define u (- v (sqrt (/ 40 (* 1000 area 1.0)))))
  (check-= (rpm->rad (final-of run '(undershot rpm))) u 1e-4))

(test-case "Undershot wheel: at its best load it takes 8/27 of the race's kinetic energy through its paddles"
  (define probe (simulate 'water-wheels #:seconds 60 #:step 0.02 #:sample-dt 30))
  (define v (final-of probe '(mill-race velocity)))
  (define area (* 0.6 (/ (final-of probe '(mill-race depth)) 100)))
  (define best (* 1000 area (expt (* 2/3 v) 2) 1.0))                 ; tau at u = v/3
  (define run (simulate 'water-wheels #:seconds 300 #:step 0.02 #:sample-dt 50 #:set `((undershot load ,best))))
  (define kinetic (* 1/2 1000 area (expt v 3)))
  (check-= (final-of run '(undershot power)) (* 8/27 kinetic) 0.5 "W")
  (check-= (rpm->rad (final-of run '(undershot rpm))) (/ v 3) 1e-4))

;; ---- #23 heated sealed air: Heron's temple doors (heron-temple-doors.rkt)
(define p0 101325.0)
(define r-air 287.05)
(define (kelvin c) (+ c 273.15))
(define v0 (+ 0.075 0.020 0.0005))                          ; m3: altar + globe's air + tube
(define air-mass (/ (* p0 v0) (* r-air (kelvin 20))))
(define altar-c (+ 1900 (* air-mass 718)))                  ; J/K
(define tau (/ altar-c 3))                                  ; s
(define heat-in (* 150 0.6))                                ; W

(test-case "Temple doors: sealed air warms toward 20 + Q/h and its pressure rises as T at fixed volume"
  (define run (simulate 'heron-temple-doors #:seconds 250 #:step 0.05 #:sample-dt 50))
  (for ([t '(100 200 250)])
    (define predicted (+ 20 (* (/ heat-in 3) (- 1 (exp (- (/ t tau)))))))
    (check-= (value-at run '(altar air-temperature) t) predicted 0.005 (format "T at ~a s" t))
    ;; no water has moved yet (it needs 3.9 kPa), so V is still V0
    (check-= (value-at run '(altar air-pressure) t) (/ (* p0 (- (/ (kelvin predicted) (kelvin 20)) 1)) 1000) 0.005 "kPa")))

(test-case "Temple doors: at 50 C the air holds up the water it balances, and the doors stand open"
  ;; plenty of fuel, a long run: the altar settles at 20 + 90/3 = 50 C
  (define run (simulate 'heron-temple-doors #:seconds 7000 #:step 0.05 #:sample-dt 500 #:set '((offering fuel 1))))
  (define t-steady (+ 20 (/ heat-in 3)))
  (check-= (final-of run '(altar air-temperature)) t-steady 0.01)
  ;; bucket water dv (m3) where the air's gauge pressure equals the column
  ;; from the globe's surface up to the bucket's (the bucket let down r x 90 deg)
  (define bucket-base (- 0.6 (* 0.05 (/ pi 2))))
  (define (imbalance dv)
    (- (- (/ (* air-mass r-air (kelvin t-steady)) (+ v0 dv)) p0)
       (* 1000 9.81 (- (+ bucket-base (/ dv 0.04)) (- 0.2 (/ dv 0.1))))))
  (define dv (let loop ([lo 0.0] [hi 0.02] [n 60])
               (define mid (/ (+ lo hi) 2))
               (cond [(zero? n) mid] [(> (imbalance mid) 0) (loop mid hi (sub1 n))] [else (loop lo mid (sub1 n))])))
  (check-= (final-of run '(bucket water)) (* 1000 dv) 0.01 (format "L in the bucket, predicted ~a" (* 1000 dv)))
  (check-= (final-of run '(doors angle)) 90 1e-9)
  (check-true (> (* 1000 dv) 2) "more than the 2 kg the counterweight outweighs the empty bucket by"))

(test-case "Temple doors: they open only once the bucket outweighs the counterweight, and shut when the fire dies"
  (define run (simulate 'heron-temple-doors #:seconds 6000 #:step 0.05 #:sample-dt 10))
  ;; up to the first moment they move, the bucket never held less than the 2 kg it takes
  (define first-open (for/first ([f run] #:when (> (cadr (assq 'doors.angle (cdr f))) 0)) f))
  (check-true (>= (cadr (assq 'bucket.water (cdr first-open))) 2.0)
              (format "first moved at ~a s" (car first-open)))
  (for ([f run] #:when (< (car f) (car first-open)))
    (check-true (< (cadr (assq 'bucket.water (cdr f))) 2.0) "and before then it was still lighter"))
  (check-true (> (max-of run '(doors angle)) 89.99) "they swing wide open")
  ;; 30 g of wood at 150 W: out at 30e-3 x 15e6 / 150 = 3000 s; then all the water runs back
  (check-= (final-of run '(offering lit)) 0 1e-9)
  (check-= (final-of run '(bucket water)) 0 1e-6)
  (check-= (final-of run '(doors angle)) 0 1e-9)
  (check-true (< (final-of run '(altar air-temperature)) 21)))

(test-case "Heron's fountain: its unheated air keeps P V constant (Boyle), as sealed"
  (define run (simulate 'herons-fountain #:seconds 60 #:step 0.05 #:sample-dt 5))
  (define (pv f) (* (+ p0 (* 1000 (cadr (assq 'supply.air-pressure (cdr f))))) (cadr (assq 'supply.air-volume (cdr f)))))
  (define start (pv (car run)))
  (for ([f run]) (check-= (pv f) start (* 1e-9 start) (format "P V at ~a s" (car f))))
  (check-true (> (max-of run '(supply air-pressure)) 1) "and it was squeezed: over 1 kPa"))

;; ---- #13 friction and wear in a pendulum's bearing (bearing-friction.rkt)
;; All three pendulums are the same: 1 m of iron rod (1 cm radius) with an
;; 8 cm ball, on a 1.5 cm pin, let go from 15 degrees. Their mass, moment of
;; inertia and centre of mass, worked out here from that shape and iron's
;; density in the material table.
(require (only-in heroic/materials material-table material-field))
(define iron-density (material-field (assq 'iron (material-table)) 'density))
(define pend-len 1.0)
(define pin 0.015)
(define rod-mass (* iron-density pi 0.01 0.01 pend-len))
(define ball-r (max 0.03 (* 0.08 pend-len)))
(define ball-mass (* iron-density 4/3 pi (expt ball-r 3)))
(define pend-m (+ rod-mass ball-mass))
(define pend-I (+ (/ (* rod-mass pend-len pend-len) 3) (* ball-mass (+ (* pend-len pend-len) (* 0.4 ball-r ball-r)))))
(define pend-d (/ (+ (* rod-mass pend-len 1/2) (* ball-mass pend-len)) pend-m))
(define mgd (* pend-m 9.81 pend-d))
(define a0 (* 15 (/ pi 180)))
(define (deg rad) (* rad (/ 180 pi)))
(define swing-energy (* mgd (- 1 (cos a0))))

(test-case "Frictionless pin: the pendulum keeps its 15 degrees, swinging at the compound pendulum's period"
  (define run (simulate 'bearing-friction #:seconds 60 #:step 0.01 #:sample-dt 10))
  (check-= (final-of run '(perfect amplitude)) 15 1e-3 "deg")
  (check-= (final-of run '(perfect heat)) 0 1e-12)
  ;; T = 2 pi sqrt(I / m g d) (1 + A^2/16 + 11 A^4/3072) for a swing of amplitude A
  (define half-period (* pi (sqrt (/ pend-I mgd)) (+ 1 (/ (* a0 a0) 16) (/ (* 11 (expt a0 4)) 3072))))
  (check-= (/ (final-of run '(perfect peak-time)) (final-of run '(perfect swings))) half-period 1e-4 "s per swing"))

(test-case "Greased pin: viscous drag c w dies the swing away inside the envelope A0 exp(-c t / 2I)"
  (define c 0.6)
  (define run (simulate 'bearing-friction #:seconds 60 #:step 0.01 #:sample-dt 10))
  (define t (final-of run '(greased peak-time)))
  (define predicted (* 15 (exp (- (/ (* c t) (* 2 pend-I))))))
  (check-true (> (final-of run '(greased swings)) 50) "still swinging after a minute")
  (check-= (final-of run '(greased amplitude)) predicted (* 0.01 predicted) (format "deg at ~a s" t))
  (check-= (+ (final-of run '(greased heat)) (final-of run '(greased energy))) swing-energy (* 0.002 swing-energy)
           "J: every joule the swing loses is heat in the pin"))

(test-case "Dry pin: Coulomb friction takes the same angle every swing, stops it, and wears the pin by Archard's law"
  (define mu 0.4) (define k 1e-4)                      ; k: mm^3 per N m
  (define n-load (* pend-m 9.81))
  (define tau (* mu n-load pin))
  ;; each half swing from A to A' on the other side: m g d (cos A' - cos A) = tau (A + A');
  ;; it stops at a turning point once m g d sin A can't overcome tau
  (define (next a)
    (let loop ([lo 0.0] [hi a] [i 80])
      (define mid (/ (+ lo hi) 2))
      (define f (- (* mgd (- (cos mid) (cos a))) (* tau (+ a mid))))
      (cond [(zero? i) mid] [(> f 0) (loop mid hi (sub1 i))] [else (loop lo mid (sub1 i))])))
  (define-values (swings rest travelled)
    (let loop ([a a0] [n 0] [s 0.0])
      (if (<= (* mgd (sin a)) tau) (values n a s)
          (let ([a2 (next a)]) (loop a2 (add1 n) (+ s a a2))))))
  (define run (simulate 'bearing-friction #:seconds 40 #:step 0.01 #:sample-dt 10))
  (check-equal? (final-of run '(dry swings)) swings)
  (check-= (final-of run '(dry stopped)) 1 0)
  (check-= (abs (final-of run '(dry angle))) (deg rest) 0.01 "deg: where the pin holds it")
  (check-= (final-of run '(dry heat)) (* mgd (- (cos rest) (cos a0))) 0.01 "J: its swing, all turned to heat")
  (check-= (final-of run '(dry sliding)) (* 1000 pin travelled) 0.05 "mm slid")
  (define wear (* k n-load pin travelled))
  (check-= (final-of run '(dry wear)) wear (* 0.002 wear) "mm^3 worn: V = K N s"))

;; constant-head.rkt: a 0.25 m2 cistern fed 2 L/s through a float valve that
;; seats at 40 cm with 2 cm of travel, drawn through a tap (an orifice 5 cm
;; wide on its floor) into a 0.5 m2 receiver; beside it an identical cistern
;; and tap with no feed at all.
(define ch-g 9.81)
(define ch-area 0.25)
(define ch-feed 0.002)      ; m3/s, the aqueduct wide open
(define ch-shut 0.40)
(define ch-travel 0.02)
(define (tap-flow h slot) (* 0.6 0.05 slot (sqrt (* 2 ch-g (- h (/ slot 2))))))
;; where the valve's feed, Q_feed (shut - h) / travel, equals the tap's draw
(define (held-level slot)
  (for/fold ([h ch-shut]) ([_ 200]) (- ch-shut (* ch-travel (/ (tap-flow h slot) ch-feed)))))
;; the bare cistern: sqrt(h - a/2) falls at 0.6 w a sqrt(2g) / 2A
(define (drained-level h0 slot t)
  (+ (/ slot 2) (expt (- (sqrt (- h0 (/ slot 2))) (* (/ (* 0.6 0.05 slot (sqrt (* 2 ch-g))) (* 2 ch-area)) t)) 2)))

(test-case "Float valve: the cistern holds the head where feed and draw balance, and a doubled draw costs it b dQ / Q_feed"
  ;; the tap raised 1 cm, then at 120 s to 2 cm. The valve sees the level
  ;; before each step's outflow, Q dt / A (< 0.1 mm) above what is read, and
  ;; passes 1 L/s per cm of level: flows agree to a few 1e-4 L/s
  (define run (simulate 'constant-head #:seconds 240 #:step 0.01 #:sample-dt 1
                        #:set '((tap opening 0.04 120) (bare-tap opening 0.04 120))))
  (define h1 (held-level 0.01))
  (define h2 (held-level 0.02))
  (define q1 (* 1000 (tap-flow h1 0.01)))
  (define q2 (* 1000 (tap-flow h2 0.02)))
  (for ([t '(60 110)])
    (check-= (value-at run '(cistern level) t) (* 100 h1) 0.005 (format "held at 39.17 cm at ~a s" t))
    (check-= (value-at run '(aqueduct flow) t) q1 5e-4 "the valve lets in 0.826 L/s")
    (check-= (value-at run '(outlet flow) t) q1 5e-4 "and the tap draws the same"))
  (for ([t '(180 235)])
    (check-= (value-at run '(cistern level) t) (* 100 h2) 0.005 (format "held at 38.38 cm at ~a s" t))
    (check-= (value-at run '(aqueduct flow) t) q2 5e-4 "the valve lets in 1.625 L/s")
    (check-= (value-at run '(outlet flow) t) q2 5e-4))
  (check-= (- (value-at run '(cistern level) 110) (value-at run '(cistern level) 235))
           (* 100 ch-travel (/ (- q2 q1) 1000 ch-feed)) 0.005 "the draw nearly doubles; the head gives 0.80 cm")
  (check-true (<= (max-of run '(cistern level)) (* 100 ch-shut)) "never above the seat")
  (check-true (>= (min-of run '(cistern level)) (* 100 (- ch-shut ch-travel))) "never below the band")
  ;; a clock: the receiver rises Q / 0.5 m2 at a steady rate
  (check-= (/ (- (value-at run '(receiver level) 110) (value-at run '(receiver level) 60)) 50)
           (/ (/ q1 1000) 0.5 1/100) 1e-4 "1.653 mm/s, in cm/s")
  (check-= (/ (- (value-at run '(receiver level) 235) (value-at run '(receiver level) 180)) 55)
           (/ (/ q2 1000) 0.5 1/100) 1e-4 "3.250 mm/s, in cm/s")
  ;; the same cistern and tap with no float valve feeding it drains away
  (define b120 (drained-level 0.40 0.01 120))
  (for ([t '(30 60 120)])
    (check-= (value-at run '(bare level) t) (* 100 (drained-level 0.40 0.01 t)) 0.005 (format "bare at ~a s" t))
    (check-= (value-at run '(bare-receiver level) t) (* 100 (/ (* ch-area (- 0.40 (drained-level 0.40 0.01 t))) 0.5)) 0.005))
  (check-= (value-at run '(bare level) 150) (* 100 (drained-level b120 0.02 30)) 0.005 "3.01 cm at 150 s")
  (check-true (< (value-at run '(bare level) 120) 10.1) "a quarter of the head it started with"))

(test-case "Float valve: filling from empty, the cistern rises at Q_feed / A to the band, then closes on the seat with tau = A b / Q_feed"
  (define run (simulate 'constant-head #:seconds 80 #:step 0.01 #:sample-dt 0.5
                        #:set '((tap opening 0) (cistern water 0))))
  (define rise (/ ch-feed ch-area))                         ; 8 mm/s
  (define t1 (/ (- ch-shut ch-travel) rise))                ; 47.5 s to reach the band
  (define tau (/ (* ch-area ch-travel) ch-feed))            ; 2.5 s
  (check-= (value-at run '(cistern level) 30) (* 100 rise 30) 0.01 "24 cm at 30 s: the valve wide open")
  (check-= (value-at run '(ball opening) 30) 1.0 1e-12)
  (check-= (value-at run '(cistern level) 45) (* 100 rise 45) 0.01)
  (for ([dt '(2.5 5 10)])
    (check-= (value-at run '(cistern level) (+ t1 dt)) (* 100 (- ch-shut (* ch-travel (exp (- (/ dt tau)))))) 0.01
             (format "~a s into the band" dt))
    (check-= (value-at run '(aqueduct flow) (+ t1 dt)) (* 1000 ch-feed (exp (- (/ dt tau)))) 0.002))
  (check-true (< (max-of run '(cistern level)) (* 100 ch-shut)) "it never overfills")
  (check-= (final-of run '(receiver water)) 0 1e-12 "the tap stays shut"))

;; tank-leaks.rkt: 0.25 m2 barrels 80 cm full with a 5 cm2 hole (Cd 0.6)
;; in the wall at 10 cm ("low") or 40 cm ("high"); "plugged" is a low hole
;; into a 0.5 m2 catch tank, stopped at 100 s; "seep" loses 0.05 L/s from a
;; 50 cm surface.
(define lk-g 9.81)
(define lk-area 0.25)
(define lk-hole-area 5e-4)
(define lk-h0 0.80)
;; sqrt(h - hole) falls at Cd a sqrt(2g) / 2A
(define lk-rate (/ (* 0.6 lk-hole-area (sqrt (* 2 lk-g))) (* 2 lk-area)))
(define (leak-level hole t)
  (define u (- (sqrt (- lk-h0 hole)) (* lk-rate t)))
  (+ hole (if (> u 0) (* u u) 0)))
(define (leak-flow head) (* 1000 0.6 lk-hole-area (sqrt (* 2 lk-g head))))   ; L/s
;; the issue's T = (A_t / Cd A_o) sqrt(2H/g), H the water above the hole
(define (drain-time hole) (* (/ lk-area (* 0.6 lk-hole-area)) (sqrt (/ (* 2 (- lk-h0 hole)) lk-g))))

(test-case "Tank leaks: the level follows Torricelli's draw-down and stops at the hole, a lower hole leaking faster and further"
  (define run (simulate 'tank-leaks #:seconds 400 #:step 0.01 #:sample-dt 1 #:set '((plug-hole area 0 100))))
  (check-= (drain-time 0.10) 314.8 0.1 "the hand formula gives 314.8 s")
  (check-= (drain-time 0.40) 238.0 0.1)
  ;; outflow at the starting head (read after the first second, 1 mm lower)
  (check-= (value-at run '(low-hole flow) 1) (leak-flow (- (leak-level 0.10 1) 0.10)) 0.005 "1.11 L/s under 70 cm")
  (check-= (value-at run '(high-hole flow) 1) (leak-flow (- (leak-level 0.40 1) 0.40)) 0.005 "0.84 L/s under 40 cm")
  (for ([t '(50 100 200 300)])
    (check-= (value-at run '(low level) t) (* 100 (leak-level 0.10 t)) 0.02 (format "low at ~a s" t))
    (check-= (value-at run '(high level) t) (* 100 (leak-level 0.40 t)) 0.02 (format "high at ~a s" t)))
  ;; the outflow follows sqrt(head) and the head is read off the level
  (check-= (value-at run '(low-hole flow) 200) (leak-flow (- (leak-level 0.10 200) 0.10)) 0.01)
  ;; the same hole higher up leaks slower at the start, and at 100 s the lower barrel is lower
  (check-true (> (value-at run '(low-hole flow) 1) (value-at run '(high-hole flow) 1)))
  (check-true (< (value-at run '(low level) 100) (value-at run '(high level) 100)))
  ;; both stop at their holes and stay there
  (define t-low (drain-time 0.10))
  (check-= (value-at run '(low level) (+ t-low 30)) 10 0.05 "held at the hole once it gets there")
  (check-= (value-at run '(low level) 400) 10 0.05)
  (check-= (value-at run '(low-hole flow) 400) 0 0.01 "no flow once the level reaches the hole")
  (check-= (value-at run '(high level) 400) 40 0.05)
  (check-true (> (value-at run '(low level) (- t-low 20)) 10.2) "still above the hole 20 s earlier")
  ;; litres out = litres in the barrel lost, and the lower hole drains more
  (check-= (final-of run '(low-hole lost)) (* 1000 lk-area (- lk-h0 0.10)) 0.05 "175 L")
  (check-= (final-of run '(high-hole lost)) (* 1000 lk-area (- lk-h0 0.40)) 0.05 "100 L"))

(test-case "Tank leaks: a leak into a catch tank is conserved, and a plugged hole holds the level"
  (define run (simulate 'tank-leaks #:seconds 300 #:step 0.01 #:sample-dt 1 #:set '((plug-hole area 0 100))))
  (define held (* 100 (leak-level 0.10 100)))
  (for ([t '(50 100)])
    (check-= (value-at run '(plugged level) t) (* 100 (leak-level 0.10 t)) 0.02)
    (check-= (value-at run '(catch level) t) (* 100 (/ (* lk-area (- lk-h0 (leak-level 0.10 t))) 0.5)) 0.02
             (format "the catch tank holds what left the barrel at ~a s" t)))
  (check-= (value-at run '(plugged level) 300) held 0.02 "plugged at 100 s: it stays put")
  (check-= (value-at run '(plug-hole flow) 200) 0 1e-9)
  (check-= (value-at run '(plug-hole area) 200) 0 1e-12)
  (check-= (+ (final-of run '(plugged water)) (* 1000 0.5 (/ (final-of run '(catch level)) 100)))
           (* 1000 lk-area lk-h0) 1e-6 "every litre is somewhere"))

(test-case "Tank leaks: a seep takes the same volume off the surface at any level"
  (define run (simulate 'tank-leaks #:seconds 300 #:step 0.01 #:sample-dt 50))
  (for ([t '(0 100 300)])
    (check-= (value-at run '(seep level)  t) (* 100 (- 0.50 (/ (* 5e-5 t) lk-area))) 1e-6 "0.2 mm/s"))
  (check-= (final-of run '(seep-hole evaporated)) 15 1e-6 "0.05 L/s for 300 s"))

;; boiler-safety.rkt: two boilers of 10 kg of 20 C water, rated to burst at
;; 200 kPa gauge, each given Q = 20 kW x 0.5 by its fire and losing 2 W/K;
;; "guarded" has a safety valve lifting at 100 kPa through an 8 mm bore
;; (Cd 0.8, fully lifted 10% over), "unguarded" has none.
(define bs-q 10000.0) (define bs-h 2.0) (define bs-m 10.0) (define bs-c 4186.0) (define bs-l 2.257e6)
(define bs-atm 101325.0)
(define (bs-psat t)   ; Antoine, as Boiler.SaturationPressure
  (define-values (a b c) (if (< t 100) (values 8.07131 1730.63 233.426) (values 8.14019 1810.94 244.485)))
  (* 133.322 (expt 10 (- a (/ b (+ c t))))))
(define (bs-tsat p)   ; turned round: the boiling point under absolute p
  (define-values (a b c) (if (< p (bs-psat 100)) (values 8.07131 1730.63 233.426) (values 8.14019 1810.94 244.485)))
  (- (/ b (- a (log (/ p 133.322) 10))) c))
(define bs-t-inf (+ 20 (/ bs-q bs-h)))
;; sealed, M c dT/dt = Q - h (T - 20): the time to warm from t0 to t1 with m kg
(define (bs-warm-time m t0 t1) (* (/ (* m bs-c) bs-h) (log (/ (- bs-t-inf t0) (- bs-t-inf t1)))))
(define (bs-sealed-temp t) (- bs-t-inf (* (- bs-t-inf 20) (exp (- (/ (* t bs-h) (* bs-m bs-c)))))))
(define bs-t-lift (bs-tsat (+ bs-atm 100e3)))
(define bs-t-burst (bs-tsat (+ bs-atm 200e3)))
;; what the valve must carry away at temperature t: m L = Q - h (T - 20)
(define (bs-vent-rate t) (/ (- bs-q (* bs-h (- t 20))) bs-l))
;; a choked (or not) nozzle, k = 1.3, R = 8.314 / 0.018015
(define (bs-capacity p t)
  (define k 1.3) (define r-steam (/ 8.314 0.018015)) (define area (* pi 0.008 0.008 1/4))
  (define r (max (/ bs-atm p) (expt (/ 2 (+ k 1)) (/ k (- k 1)))))
  (* 0.8 area p (sqrt (* (/ (* 2 k) (* (- k 1) r-steam (+ t 273.15))) (- (expt r (/ 2 k)) (expt r (/ (+ k 1) k)))))))
;; the gauge pressure the valve holds: open just enough, (Pg - lift) / (0.1 lift), to pass the vent rate
(define bs-p-hold
  (let loop ([lo 100e3] [hi 110e3] [n 60])
    (define mid (/ (+ lo hi) 2))
    (define t (bs-tsat (+ bs-atm mid)))
    (define passes (* (min 1 (/ (- mid 100e3) 10e3)) (bs-capacity (+ bs-atm mid) t)))
    (cond [(zero? n) mid]
          [(> passes (bs-vent-rate t)) (loop lo mid (sub1 n))]
          [else (loop mid hi (sub1 n))])))
(define bs-t-hold (bs-tsat (+ bs-atm bs-p-hold)))

(test-case "Boiler safety: the valve lifts at 100 kPa and holds the pressure there, venting what the fire brings"
  (define run (simulate 'boiler-safety #:seconds 900 #:step 0.01 #:sample-dt 1))
  (check-= bs-t-lift 120.536 0.001) (check-= (bs-warm-time bs-m 20 bs-t-lift) 425.13 0.01)
  (check-= (/ bs-p-hold 1000) 103.37 0.01) (check-= (* 1000 (bs-vent-rate bs-t-hold)) 4.341 0.001)
  ;; sealed, both boilers warm alike along the same curve
  (for ([t '(100 300 420)])
    (check-= (value-at run '(guarded temperature) t) (bs-sealed-temp t) 0.01 (format "guarded at ~a s" t))
    (check-= (value-at run '(unguarded temperature) t) (bs-sealed-temp t) 0.01 (format "unguarded at ~a s" t)))
  (check-= (value-at run '(guard flow) 420) 0 1e-12 "seated below its lift")
  (let ([t (first-time-at-least run '(guarded pressure) 100)])     ; sampled each second
    (check-true (<= (- (bs-warm-time bs-m 20 bs-t-lift) 1) t (+ (bs-warm-time bs-m 20 bs-t-lift) 1)) "reaches the lift at 425.1 s"))
  ;; then it holds: pressure, temperature and vented steam steady at the balance
  (for ([t '(500 700 900)])
    (check-= (value-at run '(guarded pressure) t) (/ bs-p-hold 1000) 0.02 (format "103.37 kPa at ~a s" t))
    (check-= (value-at run '(guarded temperature) t) bs-t-hold 0.002)
    (check-= (value-at run '(guard flow) t) (* 1000 (bs-vent-rate bs-t-hold)) 0.002 "4.34 g/s")
    (check-= (value-at run '(guard opening) t) (/ (- bs-p-hold 100e3) 10e3) 0.002))
  (check-true (< (max-of run '(guarded pressure)) (+ (/ bs-p-hold 1000) 0.02)) "it never overshoots the hold")
  (check-= (final-of run '(guarded burst)) 0 0 "and never bursts")
  ;; mass: every kilogram vented is gone from the water; heat: over 600-900 s
  ;; at a steady temperature, what the fire brings = what the air takes + L x vented
  (define vented (- (value-at run '(guard vented) 900) (value-at run '(guard vented) 600)))
  (check-= vented (* 300 (bs-vent-rate bs-t-hold)) 0.001 "1.302 kg in 5 minutes")
  (check-= (final-of run '(guarded water)) (- bs-m (final-of run '(guard vented))) 1e-9)
  (check-= (- (- (value-at run '(guarded heat) 900) (value-at run '(guarded heat) 600))
              (- (value-at run '(guarded lost) 900) (value-at run '(guarded lost) 600)))
           (/ (* vented bs-l) 1000) 1 "kJ: heat in less heat lost is latent heat vented")
  (check-true (> (final-of run '(guarded-fire fuel)) 0) "the fire burns steadily throughout"))

(test-case "Boiler safety: without a valve the boiler bursts at its 200 kPa rating, when the warming curve says"
  (define run (simulate 'boiler-safety #:seconds 600 #:step 0.01 #:sample-dt 1))
  (define t-burst (bs-warm-time bs-m 20 bs-t-burst))
  (check-= bs-t-burst 133.893 0.001) (check-= t-burst 482.27 0.01)
  (check-= (final-of run '(unguarded burst)) 1 0)
  (check-= (final-of run '(unguarded burst-time)) t-burst 0.02 "bursts at 482.3 s")
  (check-= (final-of run '(unguarded burst-pressure)) 200 0.05 "at its rating, to within one step's rise")
  (check-= (final-of run '(unguarded flashed)) (/ (* bs-m bs-c (- bs-t-burst 100)) bs-l) 0.001 "0.629 kg flashes to steam")
  (check-= (value-at run '(unguarded burst) 481) 0 0 "still whole a second before")
  (check-true (> (value-at run '(unguarded pressure) 481) 195))
  (check-= (value-at run '(unguarded pressure) 483) 0 0 "open to the air after")
  (check-= (final-of run '(unguarded water)) 0 0 "and empty")
  (check-= (final-of run '(guarded burst)) 0 0 "the guarded one, alongside, holds"))

(test-case "Boiler safety: tie the valve down and the guarded boiler bursts too, on the sealed curve from where it held"
  (define run (simulate 'boiler-safety #:seconds 700 #:step 0.01 #:sample-dt 1 #:set '((guard lift 300 600))))
  ;; held from 425.1 s to 600 s, venting 4.34 g/s (less a few grams while it settled)
  (define m-tied (- bs-m (* (bs-vent-rate bs-t-hold) (- 600 (bs-warm-time bs-m 20 bs-t-lift)))))
  (define t-burst (+ 600 (bs-warm-time m-tied bs-t-hold bs-t-burst)))
  (check-= (value-at run '(guarded pressure) 599) (/ bs-p-hold 1000) 0.02)
  (check-= (value-at run '(guard flow) 601) 0 1e-12 "tied down, it vents nothing")
  (check-= (final-of run '(guarded burst-time)) t-burst 0.3 "bursts 50.7 s after it was tied down")
  (check-= (final-of run '(guarded burst-pressure)) 200 0.05)
  (check-= (final-of run '(guarded flashed)) (/ (* m-tied bs-c (- bs-t-burst 100)) bs-l) 0.002))

;; suction-limit.rkt: three lift pumps, each a 15 cm bucket over a 50 cm
;; stroke at 20 strokes a minute (a stroke every 3 s, upstroke first),
;; efficiency 0.8, drawing 20 C water. The limit is where the pressure
;; under the bucket falls to the water's vapour pressure.
(define sl-rho-g (* 1000 9.81)) (define sl-atm 101325.0)
(define sl-pv (* 133.322 (expt 10 (- 8.07131 (/ 1730.63 (+ 233.426 20))))))   ; Antoine, as Boiler.SaturationPressure
(define sl-limit (/ (- sl-atm sl-pv) sl-rho-g))
(define sl-area (* pi 0.15 0.15 1/4)) (define sl-stroke 0.5) (define sl-eta 0.8)
(define sl-per-stroke (* sl-area sl-stroke sl-eta))                           ; m3
(define sl-period (/ 60 20.0))

(test-case "Suction limit: a pump 6 m over its water delivers its swept volume x efficiency every stroke"
  (define run (simulate 'suction-limit #:seconds 300 #:step 0.01 #:sample-dt 1.5))
  (check-= sl-limit 10.0913 0.0001 "10.09 m of water at 20 C")
  (check-= (* 1000 sl-per-stroke) 7.0686 0.0001)
  ;; the well (4 m2) sinks k S a stroke, and within a stroke k x, k = A eta / 4
  (define k (/ (* sl-area sl-eta) 4))
  (define (surface n) (- 0.8 (* n k sl-stroke)))
  (define spout (+ 6.8 sl-stroke))
  ;; work on the rod in stroke n: rho g A (spout - surface) over the stroke
  (define (work-through n)
    (for/sum ([i n]) (* sl-rho-g sl-area (+ (* (- spout (surface i)) sl-stroke) (* k sl-stroke sl-stroke 1/2)))))
  (for ([n '(1 10 50 100)])
    (define t (* n sl-period))
    (check-= (value-at run '(short strokes) t) n 0)
    (check-= (value-at run '(short delivered) t) (* 1000 n sl-per-stroke) 1e-6 (format "~a strokes" n))
    (check-= (/ (value-at run '(short delivered) t) t) (* 1000 sl-per-stroke (/ 20 60.0)) 1e-6 "2.36 L/s averaged over whole strokes")
    (check-= (value-at run '(short work) t) (/ (work-through n) 1000) 1e-3 "kJ done on the rod")
    ;; the water gains rho g V (spout - surface): eta of the work
    (check-= (/ (value-at run '(short lifted) t) (value-at run '(short work) t)) sl-eta 1e-9))
  (check-= (value-at run '(short work) sl-period) 0.5635 0.0001 "the first stroke, 563.5 J")
  ;; the pull grows as the well sinks under it: the most, at the top of the first stroke
  (check-= (value-at run '(short max-pull) sl-period) (* sl-rho-g sl-area (- spout (surface 1))) 0.001 "1127 N on the rod")
  (check-= (value-at run '(short column) 0) 6.5 1e-9 "the water reaches the barrel's top")
  (check-= (value-at run '(short broken) 300) 0 0)
  ;; and every litre it lifts is gone from the well
  (check-= (+ (final-of run '(short-well water)) (final-of run '(short-cistern water))) 3200 1e-6))

(test-case "Suction limit: a pump 11 m over its water lifts nothing, however hard it is worked"
  (define run (simulate 'suction-limit #:seconds 60 #:step 0.01 #:sample-dt 1.5))
  (define cap (* sl-area (- sl-atm sl-pv)))                                  ; N: the most the column can bear
  (check-= cap 1749.39 0.01)
  (check-= (final-of run '(tall strokes)) 20 0 "it is worked, stroke after stroke")
  (check-= (final-of run '(tall delivered)) 0 0 "and nothing comes out")
  (check-= (final-of run '(tall-cistern water)) 0 0)
  (check-= (final-of run '(tall-well water)) 800 0 "nor leaves the well")
  (check-= (final-of run '(tall column)) sl-limit 1e-9 "the water stands 10.09 m up the pipe")
  (check-= (final-of run '(tall lift)) 11 1e-9)
  (check-= (final-of run '(tall broken)) 1 0)
  (check-= (final-of run '(tall max-pull)) cap 1e-6 "the pull tops out at A (P_atm - P_v), not the drive's 10 kN")
  ;; the vacuum drawn going up is handed back coming down: no net work over whole strokes
  (for ([n '(1 10 20)]) (check-= (value-at run '(tall work) (* n sl-period)) 0 1e-9))
  ;; ten times the drive's force changes nothing
  (define harder (simulate 'suction-limit #:seconds 60 #:step 0.01 #:sample-dt 1.5 #:set '((tall force 100000))))
  (check-= (final-of harder '(tall delivered)) 0 0)
  (check-= (final-of harder '(tall max-pull)) cap 1e-6))

(test-case "Suction limit: a pump draws its well down until the lift reaches 10.09 m, then stops"
  (define run (simulate 'suction-limit #:seconds 600 #:step 0.01 #:sample-dt 1.5))
  ;; d, the surface's height over its last level (bucket foot - limit), falls
  ;; k S a stroke while the whole stroke fills, k = A eta / 0.25; once d is
  ;; under (1 + k) S the column breaks part way up and each stroke leaves
  ;; d / (1 + k): the water follows the bucket d / (1 + k) as it falls k per metre.
  (define k (/ (* sl-area sl-eta) 0.25))
  (define floor-level (- 10.8 sl-limit))                                     ; 0.709 m over the well's bottom
  (define d0 (- 2.8 floor-level))
  (define full (add1 (floor (/ (- d0 (* (+ 1 k) sl-stroke)) (* k sl-stroke)))))   ; strokes that fill
  (check-= full 56 0)
  (define (d n) (if (<= n full) (- d0 (* n k sl-stroke)) (/ (d full) (expt (+ 1 k) (- n full)))))
  (for ([n '(10 30 56 60 80 100 150 200)])
    (check-= (value-at run '(drawing lift) (* n sl-period)) (- sl-limit (d n)) 0.001 (format "lift after ~a strokes" n))
    (check-= (value-at run '(drawing delivered) (* n sl-period)) (* 1000 0.25 (- d0 (d n))) 1 (format "litres after ~a strokes" n)))
  (check-= (value-at run '(drawing delivered) (* 56 sl-period)) (* 56 1000 sl-per-stroke) 1e-6 "full strokes to begin with")
  (check-= (value-at run '(drawing broken) (* 55 sl-period)) 0 0)
  (check-= (value-at run '(drawing broken) (* 57 sl-period)) 1 0)
  (check-true (< (- sl-limit (final-of run '(drawing lift))) 0.001) "the lift ends within 1 mm of the limit")
  (check-true (< (- (final-of run '(drawing delivered)) (value-at run '(drawing delivered) 540)) 0.1) "and it has all but stopped lifting")
  (check-= (final-of run '(deep-well level)) (* 100 floor-level) 0.1 "the well stands 10.09 m below the bucket"))

(test-case "Suction limit: a drive too weak for the column stalls the pump"
  ;; the short pump needs rho g A (spout - surface) = 1127 N; 1000 N stops it at its next upstroke
  (define run (simulate 'suction-limit #:seconds 60 #:step 0.01 #:sample-dt 1.5 #:set '((short force 1000 30))))
  (check-= (value-at run '(short delivered) 30) (* 10 1000 sl-per-stroke) 1e-6)
  (check-= (final-of run '(short delivered)) (* 10 1000 sl-per-stroke) 1e-6 "not a drop more")
  (check-= (final-of run '(short stalled)) 1 0)
  (check-= (final-of run '(short strokes)) 10 0))

(test-case "Trebuchet: after the sling lets go (~0.9 s) the machine never gains energy, and the stone flies well clear"
  ;; Issue #45: an uncapped stretch correction in the rope solver kicked the
  ;; arm every time the counterweight's chain snapped taut, and the machine
  ;; climbed to 155% of its starting energy. A passive machine can only lose it.
  (when (godot-available?)
    (define run (godot-simulate 'trebuchet #:seconds 12 #:sample-dt 0.1))
    (define start (value-at run '(scene mechanical) 0))
    (define after (for/list ([f run] #:when (>= (car f) 1.0))
                    (cadr (assq 'scene.mechanical (cdr f)))))
    (check-true (<= (apply max after) (* 1.02 start))
                (format "peak ~a J after release against ~a J at the start" (apply max after) start))
    ;; throws toward -X; traced at about 20 m
    (check-true (< (final-of run '(stone x)) -12) (format "stone landed at x = ~a" (final-of run '(stone x))))))

(test-case "Vitruvian catapulta: the bolt stays on the ground and comes to rest a sensible distance out"
  ;; The floor used to be 100 m across; the bolt landed ~11 m out, skidded
  ;; off the edge and fell forever. It flies toward +Z.
  (when (godot-available?)
    (define run (godot-simulate 'vitruvian-catapulta #:seconds 20 #:sample-dt 0.5))
    (check-true (> (min-of run '(bolt y)) -0.1) (format "bolt fell to y = ~a" (min-of run '(bolt y))))
    (define z (final-of run '(bolt z)))
    (check-true (< 10 z 100) (format "bolt at rest at z = ~a" z))
    (check-true (< (final-of run '(bolt speed)) 0.1) "and it has stopped")))

(test-case "Roman crane: two walkers in the treadwheel lift the granite block"
  (when (godot-available?)
    (define run (godot-simulate 'roman-crane #:seconds 20 #:sample-dt 1))
    (define rise (- (final-of run '(stone y)) (value-at run '(stone y) 0)))
    (check-true (> rise 0.5) (format "the stone rose ~a m in 20 s" rise))))

;; ---------------------------------------------------------------------------
;; One real-game check for each remaining rigid-body machine, each against
;; the prediction in its .rkt header. Measured 2026-09-29 before writing;
;; the note on each says what was traced.

;; The value of target.field in every frame between two times.
(define (values-between run path from to)
  (define key (string->symbol (format "~a.~a" (car path) (cadr path))))
  (for/list ([f run] #:when (<= from (car f) to)) (cadr (assq key (cdr f)))))

(test-case "Pendulum: a 60 cm iron pendulum from 40 degrees swings with a period between its rod and point-mass limits"
  ;; point mass at 0.6 m: 2 pi sqrt(0.6/9.81) = 1.554 s, x 1.031 for a 40 degree
  ;; swing = 1.60 s; a uniform rod: 2 pi sqrt(2 x 0.6 / 3 x 9.81) x 1.031 = 1.31 s.
  ;; Traced: 1.53-1.55 s. (Its swing also decays, 40 to 23 degrees in 9 s: the
  ;; engine's default damping, which #13 and #33 replace.)
  (when (godot-available?)
    (define run (godot-simulate 'pendulum-demo #:seconds 8 #:sample-dt 0.01))
    (define tilt (map (λ (f) (cons (car f) (cadr (assq 'rod.rot-z (cdr f))))) run))
    (define ups (for/list ([a tilt] [b (cdr tilt)] #:when (and (< (cdr a) 0) (>= (cdr b) 0))) (car b)))
    (check-true (>= (length ups) 3) "it swings back and forth")
    (for ([a ups] [b (cdr ups)])
      (check-true (< 1.31 (- b a) 1.60) (format "a period of ~a s" (- b a))))))

(test-case "Lever: the granite end sinks to the 10 degree stop, 1.0 m x sin 10 = 0.174 m below the cedar end"
  (when (godot-available?)
    (define run (godot-simulate 'lever-demo #:seconds 5 #:sample-dt 0.1))
    (check-= (abs (final-of run '(beam angle))) 10.0 0.3)
    (check-= (- (final-of run '(light y)) (final-of run '(heavy y))) 0.174 0.02)))

(test-case "Material samples: all four dropped cubes come to rest on the floor, centres 7.5 cm up"
  (when (godot-available?)
    (define run (godot-simulate 'material-samples #:seconds 5 #:sample-dt 0.1))
    (for ([c '(cedar-cube oak-cube granite-cube bronze-cube)])
      (check-= (final-of run (list c 'y)) 0.075 0.003 (format "~a" c))
      (check-true (< (final-of run (list c 'speed)) 0.01) (format "~a at rest" c)))))

(test-case "Inclined plane: at 25 degrees granite (mu 0.60) holds; cedar, oak and bronze slide, bronze (mu 0.30) fastest"
  (when (godot-available?)
    (define run (godot-simulate 'inclined-plane-demo #:seconds 6 #:sample-dt 0.1))
    (define (moved block t)
      (define (at t k) (value-at run (list block k) t))
      (let ([dx (- (at t 'x) (at 0 'x))] [dy (- (at t 'y) (at 0 'y))]) (sqrt (+ (* dx dx) (* dy dy)))))
    (check-true (< (moved 'granite-block 6) 0.01) "granite holds")
    (for ([b '(cedar-block oak-block bronze-block)])
      (check-true (> (moved b 6) 0.3) (format "~a slides" b)))
    (check-true (> (moved 'bronze-block 0.8) (max (moved 'cedar-block 0.8) (moved 'oak-block 0.8)))
                "bronze is furthest down the slope at 0.8 s")))

(test-case "Antikythera lunar train: e2 turns 64/38 x 48/24 x 127/32 = 254/19 times per turn of b2"
  (when (godot-available?)
    (define run (godot-simulate 'antikythera-lunar-train #:seconds 5 #:sample-dt 1))
    (check-= (/ (final-of run '(e2 omega)) (final-of run '(b2 omega))) (/ 254.0 19) 0.01)))

(test-case "Noria of Hama: the river turns it at about 1.2 rpm and it waters the fields"
  ;; traced: 1.29 rpm and ~1000 L in the fields after two minutes
  (when (godot-available?)
    (define run (godot-simulate 'hama-noria #:seconds 120 #:sample-dt 10))
    (check-= (final-of run '(raise rpm)) 1.2 0.2)
    (check-true (> (final-of run '(fields water)) 500) "the fields are filling")))

(test-case "Newcomen engine: every stroke lifts one pump barrel, 18.5 cm bore x 1.8 m = 48.4 L"
  (when (godot-available?)
    (define run (godot-simulate 'newcomen-engine #:seconds 100 #:sample-dt 10))
    (define strokes (final-of run '(cylinder strokes)))
    (define delivered (/ (final-of run '(cistern water)) 48.4))
    (check-true (> strokes 20) (format "~a strokes in 100 s" strokes))
    (check-true (< (- strokes 2.5) delivered (+ strokes 0.5))
                (format "~a strokes but ~a barrels in the cistern" strokes delivered))))

(test-case "Archimedes' screw: trodden at 12 rpm, it keeps pace with the 4.5 L/s spring"
  (when (godot-available?)
    (define run (godot-simulate 'archimedes-screw #:seconds 60 #:sample-dt 10))
    (check-= (final-of run '(cochlea omega)) (/ (* 12 2 3.141592653589793) 60) 0.01)
    (check-= (final-of run '(raise flow)) 4.5 0.4)))

(test-case "Onager: the stone flies toward -X and lands ~19 m out, and the machine never gains energy"
  (when (godot-available?)
    (define run (godot-simulate 'torsion-catapult #:seconds 8 #:sample-dt 0.05))
    (define start (value-at run '(scene mechanical) 0))
    (check-true (<= (apply max (values-between run '(scene mechanical) 0.5 8)) (* 1.02 start)))
    (check-true (< (final-of run '(stone x)) -15) (format "stone at x = ~a" (final-of run '(stone x))))))

(test-case "Newton's cradle: the swing passes down the row; the far ball swings out while the middle three stay nearly still"
  ;; ball-0 is pulled back to -35 degrees. Traced over the first second: the
  ;; far ball reaches 30 degrees, the middle three move under 1.2 degrees.
  (when (godot-available?)
    (define run (godot-simulate 'newtons-cradle #:seconds 1 #:sample-dt 0.02))
    (define (reach ball) (apply max (map abs (values-between run (list ball 'rot-z) 0 1))))
    (check-true (> (reach 'ball-4) 20) (format "the far ball reaches ~a degrees" (reach 'ball-4)))
    (for ([b '(ball-1 ball-2 ball-3)])
      (check-true (< (reach b) 3) (format "~a moves ~a degrees" b (reach b))))))

(test-case "Branca's steam wheel: steam = the pot's net heat / latent heat, and the wheel balances jet push against load, bearing and windage"
  ;; see racket/machines/branca-steam-wheel.rkt for the working
  (define run (simulate 'branca-steam-wheel #:seconds 600 #:step 0.01 #:sample-dt 100))
  (define T (final-of run '(pot temperature)))
  (define latent (* 1000 (- 2501 (* 2.361 T))))              ; J/kg, water's latent heat near 100 C
  (define mdot (/ (final-of run '(wheel steam-flow)) 1000))   ; kg/s
  (check-= mdot (/ (- (* 0.5 3000) (* 2 (- T 20))) latent) (* 0.02 mdot) "heat in, less the pot's loss, becomes steam")
  (define v (final-of run '(wheel jet-speed)))
  (define r 0.15)
  (define drag (* 0.5 1.2041 1.2 8 0.03 0.03 r r r))          ; windage of eight 3 cm paddles at 15 cm
  ;; mdot (v - w r) r = load + bearing + drag w^2, solved for w
  (define a drag) (define b (* mdot r r)) (define c (- (+ 0.003 0.002) (* mdot v r)))
  (define w (/ (+ (- b) (sqrt (- (* b b) (* 4 a c)))) (* 2 a)))
  (check-= (final-of run '(wheel omega)) w (* 0.01 w)))

(test-case "Solar steam wheel: four mirrors boil the pot with no fire at all, and its steam turns the wheel"
  (define run (simulate 'solar-steam-wheel #:seconds 900 #:step 0.01 #:sample-dt 100))
  (check-true (> (final-of run '(pot temperature)) 100) "the pot boils")
  (check-true (> (final-of run '(wheel rpm)) 300) (format "the wheel turns at ~a rpm" (final-of run '(wheel rpm)))))
