#lang racket/base
;; Behaviour claims about the compiled machines, checked against the real
;; C# simulation through the headless HeroicInventions.SimHost — not
;; hand-computed expected numbers. These replace
;; tests/HeroicInventions.Sim.Tests/MachineTests.cs's
;; HeronsFountainBlueprintLiftsWaterAboveTheBasin and
;; AeolipileBlueprintSpinsOnceTheWaterBoils, which asserted the same
;; things from C# directly against MachineRuntime. See docs/design.html
;; §III "Machines as tests".
(require rackunit heroic/simhost (only-in racket/math pi sinh cosh sqr) (only-in racket/file make-temporary-directory delete-directory/files))

(test-case "Heron's fountain lifts water above its basin, then empties the supply vessel"
  (define run (simulate 'herons-fountain #:seconds 60 #:step 0.05 #:sample-dt 0.5))
  (check-true (> (max-of run '(nozzle jet-height)) 0.0)
              "the jet should rise above the basin at some point during the run")
  ;; herons-fountain.rkt's supply vessel starts holding 4 L, and the
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

(test-case "Wind heading (#193): a fixed mill 60 degrees off the wind takes cos^3 60 = 1/8, a vane turns a mill square on, a mill edge-on takes nothing"
  ;; four mills, R = 10 m (A = 314.159 m2), 6 m/s, rho = 1.2041 kg/m3: 1/2 rho A v^3 = 40854.77 W
  ;;   square, wind from 90: theta 0, Cp 0.3: 12256.43 W
  ;;   skewed, wind from 30: theta 60, 3 m/s through the sails, 5106.85 W of it, stones for that: 0.3 x 5106.85 = 1532.05 W, 1/8 of square
  ;;   vane,   wind from 30: turns 60 degrees at 2 deg/s, facing 30 after 30 s, then 12256.43 W as square
  ;;   across, wind from 0:  theta 90, cos 90 = 0: 0 W, still
  ;; settled: the skewed mill's time constant is I / (2 tau0 / omega0) = 50000 / 2723.6 = 18.4 s, so 400 s is 21
  (define run (simulate 'wind-heading #:seconds 400 #:step 0.01 #:sample-dt 50))
  (check-= (final-of run '(square power)) 12256.43 0.01)
  (check-= (final-of run '(skewed power)) 1532.05 0.01)
  (check-= (final-of run '(vane power)) 12256.43 0.01)
  (check-= (final-of run '(across power)) 0 1e-6)
  (check-= (final-of run '(across rpm)) 0 1e-9)
  (check-= (final-of run '(vane facing)) 30 1e-6)
  (check-= (final-of run '(skewed facing)) 90 1e-9 "a mill without a vane stays as built")
  (check-= (final-of run '(skewed misalignment)) -60 1e-9 "wind from 30, sails facing 90: the wind is 60 degrees toward +x of the axle")
  (check-= (/ (final-of run '(skewed wind-power)) (final-of run '(square wind-power))) 0.125 1e-9 "cos^3 60")
  (check-= (/ (final-of run '(skewed power)) (final-of run '(square power))) 0.125 1e-6))

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
    ;; The engine steps at 120 Hz by symplectic Euler, and (since #33 took
    ;; Godot's default 0.1/s damping out, leaving air drag to the parts that
    ;; ask for it) damps nothing. The block starts one tick in. So, tick by
    ;; tick, v <- v - g dt, y <- y + v dt: it accelerates at exactly g, and
    ;; stands at 3.7942 m at 0.5 s (plain 5 - g t^2/2 would be 3.7738 m: the
    ;; block starts one tick in, and a tick's fall is 0.0204 m).
    (define dt 1/120)
    (define-values (v y)
      (for/fold ([v 0.0] [y 5.0]) ([n (in-range 2 61)])
        (define v* (- v (* 9.81 dt)))
        (values v* (+ y (* v* dt)))))
    (check-= (value-at run '(drop vy) (* 2 dt)) (- (* 9.81 dt)) 1e-6 "one tick of g: the engine's gravity is the sim's 9.81")
    (check-= (/ (- (value-at run '(drop vy) (* 2 dt)) (value-at run '(drop vy) (* 3 dt))) dt)
             9.81 5e-3 "g, undamped")
    (check-= (value-at run '(drop y) 0.5) y 2e-3)
    (check-= (value-at run '(drop vy) 0.5) v 2e-3)
    ;; The pendulum: I/(m d) = 0.979641 m for a 1 m, 1 cm rod and an 8 cm
    ;; ball of one metal, so 2 pi sqrt(0.979641/9.81) = 1.985541 s for small
    ;; swings, x (1 + theta^2/16) = 1.986486 s from 5 degrees (Huygens). The
    ;; nothing takes any height off a swing now (#33): each peak is the last.
    (define ts (times-of run))
    (define zs (values-of run '(swing rot-z)))
    (define crossings ; downward through the vertical, interpolated between frames
      (for/list ([t0 ts] [t1 (cdr ts)] [a zs] [b (cdr zs)] #:when (and (> a 0) (<= b 0)))
        (+ t0 (* (- t1 t0) (/ a (- a b))))))
    (check-= (- (second crossings) (first crossings)) 1.986486 1e-3 "the first swing's period")
    (define peaks
      (for/list ([a zs] [b (cdr zs)] [c (cddr zs)] #:when (and (>= b a) (> b c) (> b 0))) b))
    (check-= (/ (second peaks) (first peaks)) 1.0 2e-3 "each swing's height, undamped")))

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
  (check-true (> (max-of run '(supply air-pressure)) 4) "and it was squeezed: over 4 kPa (the fall's 0.5 m of head is 4.9 kPa)"))

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

;; boiler-shells.rkt (#139): the same boiler and fire as boiler-safety, three shells of 3 mm over a 15 cm
;; radius. Thin-wall hoop stress: a pot bursts at P = sigma_t t / r, sigma_t from the material table.
(test-case "Boiler shells: lead bursts at 0.24 MPa, 138.2 C, 500.8 s; copper and bronze hold on, and give way when their strength says"
  (define run (simulate 'boiler-shells #:seconds 1200 #:step 0.01 #:sample-dt 1))
  (define (hoop sigma-mpa) (/ (* sigma-mpa 1e6 0.003) 0.15))                ; Pa gauge
  (define (burst-time sigma-mpa) (bs-warm-time bs-m 20 (bs-tsat (+ bs-atm (hoop sigma-mpa)))))
  (check-= (hoop 12) 240e3 1e-6) (check-= (hoop 220) 4.4e6 1e-6) (check-= (hoop 350) 7.0e6 1e-6)
  (check-= (bs-tsat (+ bs-atm (hoop 12))) 138.22 0.01)
  (check-= (burst-time 12) 500.8 0.05) (check-= (burst-time 220) 1016.0 0.05) (check-= (burst-time 350) 1144.2 0.05)
  (check-= (value-at run '(lead-pot limit) 100) 240 1e-6 "a lead pot holds 240 kPa")
  (check-= (value-at run '(bronze-pot limit) 100) 7000 1e-6 "a bronze one 7000")
  ;; before the lead pot's time all three are whole, alongside; the outline margin is the pressure over the limit
  (check-= (value-at run '(lead-pot burst) 499) 0 0)
  (check-= (final-of run '(lead-pot burst)) 1 0)
  (check-= (final-of run '(lead-pot burst-time)) (burst-time 12) 0.05 "lead bursts at 500.8 s")
  (check-= (final-of run '(lead-pot burst-pressure)) 240 0.1 "at 240 kPa")
  (check-= (value-at run '(lead-pot temperature) 400) (bs-sealed-temp 400) 0.01)
  (check-= (value-at run '(copper-pot burst) 900) 0 0 "copper is whole at 900 s")
  (check-= (value-at run '(bronze-pot burst) 900) 0 0 "and bronze")
  (check-= (value-at run '(copper-pot burst) 1000) 0 0)
  (check-= (final-of run '(copper-pot burst-time)) (burst-time 220) 0.05 "copper at 1016.0 s")
  (check-= (final-of run '(copper-pot burst-pressure)) 4400 1 "at 4.4 MPa")
  (check-= (value-at run '(bronze-pot burst) 1140) 0 0 "bronze holds to 1144 s")
  (check-= (final-of run '(bronze-pot burst-time)) (burst-time 350) 0.05 "bronze at 1144.2 s")
  (check-= (final-of run '(bronze-pot burst-pressure)) 7000 2 "at 7.0 MPa"))

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

(test-case "Trebuchet: the machine never gains energy over the whole run, the chain holds the counterweight on it, and the stone flies well clear"
  ;; Issue #45: an uncapped stretch correction in the rope solver kicked the
  ;; arm every time the counterweight's chain snapped taut, and the machine
  ;; climbed to 155% of its starting energy. A passive machine can only lose it.
  ;; Issue #80: with the engine's damping gone (#33) the chain stretched 0.75 m and
  ;; snapped taut again at ~4.8 s, the energy reaching 1462 J (start 856 J). The
  ;; solver misjudged how far its pull moved the hinged arm's short end (reading
  ;; the turn about the arm's centre of mass, not its pivot: 3.3 times too far), so
  ;; the chain never held. Now, worked out beforehand: the chain (0.35 m) holds the
  ;; counterweight's centre at least 1.4 - 0.27 - 0.35 - 0.15 = 0.63 m up, the
  ;; lowest the short arm's end goes, so it never strikes the ground; and the
  ;; energy, spin included, never climbs: no tick ends more than 1% of the
  ;; start above the lowest it has been, and nothing is ever above the start.
  (when (godot-available?)
    (define run (godot-simulate 'trebuchet #:seconds 12 #:sample-dt 1/120 #:set '((arm catch 0))))   ; let go at once (#155)
    (define energy (values-of run '(scene mechanical)))
    (define start (car energy))
    ;; and (#161) the windlass's oak drum, 10.25 L, its axle 0.2 m up: 720 x 0.010254 x 9.81 x 0.2 = 14.49 J
    (check-= start (+ 856.24 14.49) 0.01 "73 kg of granite 0.81 m up on a 7 kg arm, and the windlass drum, at rest")
    (check-true (<= (apply max energy) (+ start 1e-6)) (format "never above the start: peak ~a J against ~a J" (apply max energy) start))
    (define climb (for/fold ([worst 0] [lowest +inf.0] #:result worst) ([e energy]) (values (max worst (- e lowest)) (min lowest e))))
    (check-true (< climb (* 0.01 start)) (format "the most it climbed above its lowest so far: ~a J" climb))
    (check-= (final-of run '(counterweight hits)) 0 0 "the counterweight never reaches the ground")
    (check-true (> (min-of run '(counterweight y)) (- 0.63 0.005)) (format "the counterweight's lowest: ~a m, against 0.63 m on a taut chain" (min-of run '(counterweight y))))
    (check-true (< (max-of run '(cw-chain stretch)) 10) (format "the chain stretched at most ~a mm" (max-of run '(cw-chain stretch))))
    ;; throws toward -X; traced at 13.1 m
    (check-true (< (final-of run '(stone x)) -12) (format "stone landed at x = ~a" (final-of run '(stone x))))))

;; ---------------------------------------------------------------------------
;; Catch and release (issue #155): catches on hinges, pawls, tethers. The working is in each blueprint's header.
;; (x catch 0) at the start is "let go at once"; a release mid-run is the blueprint's own #:release-after, or a timed
;; (x catch 0 t) under #:set, which godot-simulate now honours (#150).

;; How far a body is, flat, from where it started when it first comes back to the ground after a throw: back within
;; 15 cm of its starting height after rising half a metre, a metre or more out (the game's [trail] rule, Main.Trail.cs).
(define (first-touchdown run body)
  (define (v f k) (cadr (assq (string->symbol (format "~a.~a" body k)) (cdr f))))
  (define-values (x0 y0 z0) (values (v (car run) 'x) (v (car run) 'y) (v (car run) 'z)))
  (for/fold ([highest 0] [touchdown #f] #:result touchdown) ([f run])
    (define across (sqrt (+ (sqr (- (v f 'x) x0)) (sqr (- (v f 'z) z0)))))
    (define h (max highest (- (v f 'y) y0)))
    (values h (or touchdown (and (> h 0.5) (< (- (v f 'y) y0) 0.15) (> across 1) across)))))

(test-case "Trebuchet on its catch (#155): held 3 s it carries 95.8 N.m, and let go it throws as one let go at once, within 2%"
  (when (godot-available?)
    (define held (godot-simulate 'trebuchet #:seconds 7 #:sample-dt 1/120 #:set '((arm catch 0 3))))   ; the catch pulled at 3 s, as the blueprint's demo operator does (#157)
    (define at-once (godot-simulate 'trebuchet #:seconds 4 #:sample-dt 1/120 #:set '((arm catch 0))))
    ;; 72.9 kg x 9.81 x 0.27 m x cos 50 = 124.1 N.m from the counterweight, less the beam's own 7.13 x 9.81 x 0.63 x cos 50
    ;; = 28.3 N.m the other way
    (define cw (* 2700 (expt 0.3 3) 9.81 0.27 (cos (* pi 50/180))))
    (define beam (* 720 1.8 0.025 0.22 9.81 0.63 (cos (* pi 50/180))))
    (check-= cw 124.12 0.01)
    (check-= (value-at held '(arm catch-load) 2.9) (- cw beam) (* 0.02 (- cw beam)) "the catch carries the counterweight's moment less the beam's")
    (check-= (value-at held '(arm catch) 2.9) 1 0)
    (check-= (value-at held '(arm catch) 3.1) 0 0 "and lets go at 3 s")
    (check-true (< (abs (- (value-at held '(stone x) 2.9) (value-at held '(stone x) 0))) 1e-3) "nothing moves while it holds")
    (define d-held (first-touchdown held 'stone))
    (define d-once (first-touchdown at-once 'stone))
    (check-not-false (and d-held d-once) "both stones flew and came down")
    (check-= d-held d-once (* 0.02 d-once) (format "first touchdown ~a m out held 3 s, ~a m let go at once" d-held d-once))))

(test-case "Onager and catapulta catches (#155) carry their skeins' pull: k (rest - angle) less the weights on the arm"
  (when (godot-available?)
    ;; onager: 150 x 120 degrees = 314.2 N.m, less the oak arm's 2.59 kg at 0.5 m and the 2.70 kg stone at 1 m
    (define onager (godot-simulate 'torsion-catapult #:seconds 2.5 #:sample-dt 0.1 #:set '((arm catch 0 2))))   ; looses at 2 s, as the demo operator does (#157)
    (define expected (- (* 150 (* pi 120/180)) (* 720 0.06 0.06 1.0 9.81 0.5) (* 2700 0.001 9.81 1.0)))
    (check-= (value-at onager '(arm catch-load) 1.9) expected (* 0.02 expected))
    (check-= (value-at onager '(arm angle) 1.9) 0 0.1 "held where it was winched to")
    (check-true (> (value-at onager '(arm angle) 2.5) 30) "loosed at 2 s, it swings up")
    ;; catapulta, held by command: 300 x (60 + 40) degrees each; its arms turn about the vertical
    (define catapulta (godot-simulate 'vitruvian-catapulta #:seconds 2 #:sample-dt 0.1 #:set '((right-arm catch 1) (left-arm catch 1))))
    (for ([arm '(right-arm left-arm)])
      (check-= (value-at catapulta (list arm 'catch-load) 1.9) (* 300 (* pi 100/180)) 10.0))))

(test-case "Ratchet windlass, its pawl lifted 2 s in (#155): the load lowers at m g / (m + I/r^2) = 7.88 m/s2, as the free one does"
  (when (godot-available?)
    (define run (godot-simulate 'ratchet-windlass #:seconds 2.6 #:sample-dt 1/120 #:set '((lower-pawl pawl 0 2))))   ; lifted at 2 s, as the demo operator does (#157)
    (define I (* 720 6.797550167426925e-5))                   ; the drum's shape, about its axle
    (define m (* 2700 (expt 0.195 3)))
    (define a (/ (* m 9.81) (+ m (/ I (sqr 0.1)))))
    (check-= a 7.883 0.002)
    (check-= (value-at run '(lower-pawl pawl) 1.9) 1 0)
    (check-= (value-at run '(lower-pawl pawl) 2.1) 0 0)
    (check-true (< (abs (- (value-at run '(lower-load y) 1.9) 1.0)) 0.012) "held until then")
    (define measured (/ (- (value-at run '(lower-load vy) 2.15) (value-at run '(lower-load vy) 2.35)) 0.2))
    (check-= measured a (* 0.03 a) (format "lowering at ~a m/s2" measured))
    (define free (/ (- (value-at run '(free-load vy) 0.15) (value-at run '(free-load vy) 0.35)) 0.2))
    (check-= measured free (* 0.02 free) "the same as the windlass with no pawl")))

(test-case "Tethers (#155): trip-sluice's cord drops the weight 1 s in, the trigger fires 0.335 s later; the moored lantern's cord carries lift - weight"
  (when (godot-available?)
    (define sluice (godot-simulate 'trip-sluice #:seconds 2 #:sample-dt 0.05))
    (check-= (value-at sluice '(weight-cord tension) 0.9) (* 7700 0.001 9.81) 1.0 "the cord carries the 7.7 kg weight")
    (check-= (final-of sluice '(tripwire fired-at)) (+ 1 (sqrt (/ (* 2 0.55) 9.81))) 0.012 "1 s + sqrt(2 h / g)")
    (check-= (final-of sluice '(gate opening)) 0.05 1e-9)
    (define lantern (godot-simulate 'kongming-lantern #:seconds 44 #:sample-dt 0.5))
    (define (at k t) (value-at lantern (list 'lantern k) t))
    (check-= (at 'y 33.0) 0.6 0.002 "on the ground until it is light enough")
    (check-= (at 'y 38.0) 0.73 0.003 "then up the 13 cm of slack, and held")
    (check-= (value-at lantern '(mooring tension) 38.0) (- (at 'lift 38.0) (at 'weight 38.0)) 0.003 "the cord carries lift - weight")
    (check-= (value-at lantern '(mooring tether) 39.5) 1 0)
    (check-true (> (at 'y 44.0) 2.0) "let go at 40 s, it climbs")))

(test-case "Vitruvian catapulta: the bolt stays on the ground and comes to rest a sensible distance out"
  ;; The floor used to be 100 m across; the bolt landed ~11 m out, skidded
  ;; off the edge and fell forever. It flies toward +Z.
  (when (godot-available?)
    (define run (godot-simulate 'vitruvian-catapulta #:seconds 20 #:sample-dt 0.5))
    (check-true (> (min-of run '(bolt y)) -0.1) (format "bolt fell to y = ~a" (min-of run '(bolt y))))
    ;; the header's working (#187): it leaves at 33.3 m/s, skips to 22.4 m at 30.2 m/s and slides 103.3 m at mu g
    (define z (final-of run '(bolt z)))
    (check-= z 125.7 (* 0.05 125.7) (format "bolt at rest at z = ~a" z))
    (check-true (< (final-of run '(bolt speed)) 0.1) "and it has stopped")))

(test-case "Roman crane: two walkers in the treadwheel lift the granite block"
  (when (godot-available?)
    (define run (godot-simulate 'roman-crane #:seconds 20 #:sample-dt 1 #:actions '()))   ; no demo operator: the walkers just walk (#157)
    (define rise (- (final-of run '(stone y)) (value-at run '(stone y) 0)))
    (check-true (> rise 0.5) (format "the stone rose ~a m in 20 s" rise))))

;; Issue #20: a rope over fixed bars, the capstan equation in the rope
;; solver. Hemp on oak, mu = sqrt(0.5 x 0.45); the header of each machine
;; works the numbers. Predicted before the first run: 4.438 and 87.40 for the
;; ratios while sliding (the coil's helix makes its traced turn 539.49
;; degrees, not 540, so 87.04), 2.810 and 3.551 m/s2 for the slides (3.569
;; with the traced turn), 2.370 and 64.00 : 1 for the holds, and a lift of
;; 7.85 cm/s by seven walkers with the drum side at 2.908 times the stone's.
(define bar-mu (sqrt (* 0.5 0.45)))

(test-case "Rope over a fixed bar: it slides at the capstan limit e^(mu theta), or holds below it"
  (when (godot-available?)
    (define run (godot-simulate 'rope-over-bars #:seconds 1 #:sample-dt 0.1))
    (define g 9.81)
    (define (mass cm) (* 2700 (expt (/ cm 100) 3)))
    (define big (mass 40))
    (for ([station '(half-slip coil-slip)] [holder-cm '(20 7)])
      (define theta (* (/ pi 180) (value-at run (list station 'wrap-deg) 0.5)))
      (define e (exp (* bar-mu theta)))
      ;; sliding, the tight (load) side carries exactly e^(mu theta) the other
      (check-= (/ (value-at run (list station 'tension-to) 0.5) (value-at run (list station 'tension-from) 0.5))
               e (* 1e-3 e) (format "~a's tension ratio" station))
      ;; and the pair accelerate at g (M - m E) / (M + m E), with no engine
      ;; damping (#33) to add back
      (define m (mass holder-cm))
      (define a0 (/ (* g (- big (* m e))) (+ big (* m e))))
      (define load (string->symbol (format "~a-load" station)))
      (define v3 (value-at run (list load 'vy) 0.3))
      (define v5 (value-at run (list load 'vy) 0.5))
      (define measured (/ (- v3 v5) 0.2))
      (check-= measured a0 (* 0.01 a0) (format "~a accelerates at ~a m/s2, predicted ~a" station measured a0)))
    (check-= (value-at run '(half-slip wrap-deg) 0.5) 180 0.01)
    (check-= (value-at run '(coil-slip wrap-deg) 0.5) 539.49 0.05)
    ;; the holds: nothing moves, and each side carries its own block's weight
    (for ([station '(half-hold coil-hold)] [holder-cm '(30 10)])
      (define load (string->symbol (format "~a-load" station)))
      (check-true (< (abs (- (value-at run (list load 'y) 1.0) (value-at run (list load 'y) 0.1))) 1e-3)
                  (format "~a's load stays put" station))
      (check-= (/ (value-at run (list station 'tension-to) 1.0) (value-at run (list station 'tension-from) 1.0))
               (/ big (mass holder-cm)) (* 2e-3 (/ big (mass holder-cm))) (format "~a's tension ratio" station))
      (check-true (< (abs (value-at run (list station 'slip) 1.0)) 1e-4)))))

(test-case "Roman crane over a fixed bar: two walkers can't lift the stone, seven lift it with the drum side at e^(mu theta) the stone's"
  (when (godot-available?)
    (define run (godot-simulate 'bar-crane #:seconds 10 #:sample-dt 1 #:actions '()))   ; no demo operator (#157)
    (define e (exp (* bar-mu (* (/ pi 180) (value-at run '(seven-hoist wrap-deg) 5)))))
    (check-= e 2.908 0.002 "the rope turns 128.9 degrees over the bar")
    ;; two: the stone stays down; the walkers' 6,180 N reaches it as 6,180 / E
    (check-true (< (max-of run '(two-stone y)) 0.306) "the two-walker crane's stone never rises")
    (check-= (value-at run '(two-hoist tension-from) 8) (/ (* 2 70 9.81 2.25 0.5) 0.25) 30)
    (check-= (/ (value-at run '(two-hoist tension-from) 8) (value-at run '(two-hoist tension-to) 8)) e 0.01)
    ;; seven: rising at 3 rpm on the 25 cm drum, the drum side e^(mu theta) the stone's
    (define rise (- (value-at run '(seven-stone y) 9) (value-at run '(seven-stone y) 4)))
    (check-= (/ rise 5) (* 3 (/ (* 2 pi) 60) 0.25) 0.002 (format "rose ~a m in 5 s" rise))
    (check-= (/ (value-at run '(seven-hoist tension-from) 5) (value-at run '(seven-hoist tension-to) 5)) e 0.003)
    (check-= (value-at run '(seven-hoist tension-to) 5) (* 583.2 9.81) 20 "the stone side carries the stone")))

;; Issue #27: impacts. Predicted before the first run (drop-test.rkt's
;; header): the engine's own tick, v <- v - g dt (no damping since #33),
;; leaves a block dropped 1.25 m 3.3 mm above the floor after 60 ticks, at
;; 60 g dt = 4.905 m/s; the 61st tick strikes it at that speed (the one the
;; solver sees entering the step); it leaves at
;; e times that (the floor gives no restitution of its own and Jolt takes
;; the larger), and the collision takes 1/2 m v^2 (1 - e^2): 72.4, 163.5,
;; 51.1 and 103.1 J for steel, granite, oak and hemp.
(test-case "Drop test: each block strikes the floor at the fallen speed, leaves at e times it, and the strike takes 1/2 m v^2 (1 - e^2)"
  (when (godot-available?)
    (define dt 1/120)
    (define run (godot-simulate 'drop-test #:seconds 1.6 #:sample-dt dt))
    (define (tick v) (- v (* 9.81 dt)))
    (define v-in                       ; falling from 1.25 m, a tick at a time
      (let loop ([v 0.0] [y 1.25])
        (define v* (tick v))
        (if (<= (+ y (* v* dt)) 0) (- v) (loop v* (+ y (* v* dt))))))
    (check-= v-in 4.905 1e-3)
    (define (rise v) (let loop ([v v] [y 0.0]) (define v* (tick v)) (if (<= v* 0) y (loop v* (+ y (* v* dt))))))
    (for ([b '(steel-block granite-block oak-block hemp-bale)]
          [e '(0.95 0.6 0.5 0.1)]
          [m (map (λ (rho) (* rho 0.008)) '(7850 2700 720 1100))])
      (define struck (for/first ([f run] #:when (>= (cadr (assq (string->symbol (format "~a.impacts" b)) (cdr f))) 1)) f))
      (define (at key) (cadr (assq (string->symbol (format "~a.~a" b key)) (cdr struck))))
      (define speed (at 'impact-speed))
      (check-= speed v-in 2e-3 (format "~a strikes at ~a m/s" b speed))
      (check-= (at 'vy) (* e speed) (* 2e-3 e speed) (format "~a leaves at e x its speed" b))
      (check-= (at 'impact-energy) (* 1/2 m speed speed (- 1 (* e e))) (* 5e-3 m speed speed) (format "~a: energy taken" b))
      ;; the impulse that turned it round, and the step's weight
      (check-= (at 'impact-impulse) (* m (+ speed (* e speed) (* 9.81 dt))) (* 0.02 m speed) (format "~a: impulse" b))
      ;; and, from where the bounce left it, it rises as the tick model says
      (unless (< e 0.2)
        (define top (apply max (for/list ([f run] #:when (> (car f) (car struck)) #:when (< (car f) (+ (car struck) 1.2)))
                                 (cadr (assq (string->symbol (format "~a.y" b)) (cdr f))))))
        (check-= top (+ (at 'y) (rise (at 'vy))) 0.003 (format "~a's bounce" b))))))

(test-case "Newton's cradle: every strike down the row is recorded, the first at the swinging ball's speed"
  (when (godot-available?)
    (define run (godot-simulate 'newtons-cradle #:seconds 0.4 #:sample-dt 1/120))
    (define struck (for/first ([f run] #:when (>= (cadr (assq 'ball-0.impacts (cdr f))) 1)) f))
    (define before (for/last ([f run] #:when (< (car f) (car struck))) f))
    (define (at f key) (cadr (assq key (cdr f))))
    ;; the bob's centre is 0.5 m below its pivot: speed = omega x 0.5, a tick before
    (check-= (at struck 'ball-0.impact-speed) (* 0.5 (at before 'ball-0.omega)) 0.02)
    (check-equal? (map (λ (b) (at struck (string->symbol (format "~a.impacts" b)))) '(ball-0 ball-4)) '(1 1))
    (check-true (> (at struck 'ball-4.impact-speed) (* 0.9 (at struck 'ball-0.impact-speed))) "the far ball is struck nearly as hard")))

;; Issue #30: joints. Predicted before the first run (the machines' headers):
;; the crank-slider's piston at y_c - r cos(theta) - sqrt(l^2 - r^2 sin^2(theta))
;; against the traced crank angle -- 0.85 and 1.15 m at the dead centres,
;; 1.01905 m at a quarter turn -- and the universal joint's driven shaft
;; at tan(out) = cos b tan(in), its speed between cos b and 1/cos b.
(test-case "Crank and connecting rod: the piston follows y_c - r cos(theta) - sqrt(l^2 - r^2 sin^2(theta))"
  (when (godot-available?)
    (define run (godot-simulate 'crank-slider #:seconds 2 #:sample-dt 1/120))
    (define (at f key) (cadr (assq key (cdr f))))
    (define (predicted deg)
      (define th (* deg (/ pi 180)))
      (- 1.6 (* 0.15 (cos th)) (sqrt (- (* 0.6 0.6) (* 0.15 0.15 (sin th) (sin th))))))
    ;; Jolt's joints give a little, in step with speed: the loop runs a
    ;; steady ~1.3 degrees behind the crank at 60 rpm (4.3 mm at most off
    ;; the formula, measured; 2.0 mm at 30 rpm)
    (define worst (for/fold ([w 0]) ([f run]) (max w (abs (- (at f 'piston.y) (predicted (at f 'crank.angle)))))))
    (check-true (< worst 0.005) (format "the piston keeps within ~a m of the formula" worst))
    ;; at the dead centres the piston stands still, so only the joints'
    ;; stretch under its turning load is left (1.6 mm measured at the bottom,
    ;; where they pull it up at g + r w^2 (1 + r/l)): 0.85 m and 1.15 m
    (check-= (apply min (map (λ (f) (at f 'piston.y)) run)) 0.85 0.002)
    (check-= (apply max (map (λ (f) (at f 'piston.y)) run)) 1.15 0.002)
    ;; and the give is opposite either side, so a quarter turn and three
    ;; quarters, averaged, stand where the formula says: 1.01905 m, 19 mm
    ;; below mid-stroke, the rod's lean (a plain sine would give 1.0)
    (define (y-crossing deg)
      (for/first ([f run] [g (cdr run)] #:when (and (< (at f 'crank.angle) deg) (>= (at g 'crank.angle) deg)
                                                    (< (- (at g 'crank.angle) (at f 'crank.angle)) 10)))
        (define k (/ (- deg (at f 'crank.angle)) (- (at g 'crank.angle) (at f 'crank.angle))))
        (+ (at f 'piston.y) (* k (- (at g 'piston.y) (at f 'piston.y))))))
    (check-= (/ (+ (y-crossing 90) (y-crossing -90)) 2) (predicted 90) 0.0005)))

(test-case "Universal joint: the driven shaft turns tan(out) = cos 30 tan(in), running 0.866 to 1.155 of the driver's speed"
  (when (godot-available?)
    (define run (godot-simulate 'universal-joint #:seconds 2 #:sample-dt 1/120))
    (define (at f key) (cadr (assq key (cdr f))))
    (define cb (cos (/ pi 6)))
    (define worst   ; once the driven shaft is up to speed (the first 0.1 s jerks it off rest, and it rings a little after)
      (for/fold ([w 0]) ([f run] #:when (> (car f) 0.25))
        (define in (* (at f 'driver.angle) (/ pi 180)))
        (define out (* (/ 180 pi) (atan (* cb (sin in)) (cos in))))
        (define d (- (at f 'driven.angle) out))
        (max w (abs (- d (* 360 (round (/ d 360))))))))
    (check-true (< worst 0.3) (format "the driven shaft's angle is off by at most ~a degrees" worst))
    (define ratios (for/list ([f run] #:when (> (car f) 0.25)) (/ (at f 'driven.omega) (at f 'driver.omega))))
    (check-= (apply min ratios) cb 0.003)
    (check-= (apply max ratios) (/ 1 cb) 0.003)))

;; Issue #25: carts. Predicted before the first run (carts.rkt's header),
;; from the wheels' own meshes: a = g (sin t - C_rr cos t) M / (M + sum I/r^2)
;; down the slope, C_rr g M / (M + sum I/r^2) slowing on the flat.
(test-case "Carts: down a 10 degree slope and along the flat as the wheels' inertia and rolling resistance say; the sledge holds"
  (when (godot-available?)
    (local-require heroic/geometry)
    (define run (godot-simulate 'carts #:seconds 7 #:sample-dt 0.1))
    (define (at f key) (cadr (assq key (cdr f))))
    (define (frame t) (for/first ([f run] #:when (>= (car f) (- t 1e-6))) f))
    (define th (* 10 (/ pi 180)))
    (define g 9.81) (define crr 0.04) (define r 0.15)
    (for ([cart '(disc-cart spoke-cart)]
          [wheel (list (disc-wheel #:radius r #:width 0.05) (cart-wheel #:radius r #:width 0.05))]
          [bed (list (* 720 0.4 0.08 0.8) (* 500 0.4 0.05 0.8))])
      (define m (* 720 (shape-volume wheel)))
      (define i (* 720 (vector-ref (shape-inertia wheel) 2)))
      (define M (+ bed (* 4 m)))
      (define k (/ M (+ M (* 4 (/ i r r)))))
      (define (vz t) (at (frame t) (string->symbol (format "~a.vz" cart))))
      ;; on the slope (0.3 to 1.5 s) its speed along it is vz / cos t
      (define down (/ (- (vz 1.5) (vz 0.3)) 1.2 (cos th)))
      (check-= down (* g (- (sin th) (* crr (cos th))) k) (* 0.01 down) (format "~a down the slope at ~a m/s2" cart down))
      ;; on the flat (3 to 6 s)
      (define slowing (/ (- (vz 3.0) (vz 6.0)) 3.0))
      (check-= slowing (* crr g k) (* 0.02 slowing) (format "~a slows at ~a m/s2" cart slowing)))
    (check-true (< (abs (- (at (frame 7) 'sledge.z) (at (frame 0) 'sledge.z))) 0.001) "the sledge holds on the slope")))

(test-case "Rail wagons: on a 1 degree grade the wagon on iron rails rolls away at g (sin t - 0.002 cos t) k; the one on the road stays"
  ;; predicted before the first run (rail-wagons.rkt): 0.1029 m/s2
  (when (godot-available?)
    (local-require heroic/geometry)
    (define run (godot-simulate 'rail-wagons #:seconds 8 #:sample-dt 1))
    (define (at t key) (cadr (assq key (cdr (for/first ([f run] #:when (>= (car f) (- t 1e-6))) f)))))
    (define th (* 1 (/ pi 180)))
    (define wheel (drum #:radius 0.1 #:length 0.08 #:flange-radius 0.12))
    (define m (* 7700 (shape-volume wheel)))
    (define i (* 7700 (vector-ref (shape-inertia wheel) 2)))
    (define M (+ (* 720 0.5 0.06 0.8) (* 4 m)))
    (define predicted (* 9.81 (- (sin th) (* 0.002 (cos th))) (/ M (+ M (* 4 (/ i 0.01))))))
    (check-= predicted 0.1029 0.0001)
    (define a (/ (- (at 8 'rail-wagon.vz) (at 1 'rail-wagon.vz)) 7 (cos th)))
    (check-= a predicted (* 0.01 predicted) (format "the rail wagon rolls at ~a m/s2" a))
    (check-= (at 8 'rail-wagon.x) (at 0 'rail-wagon.x) 0.01 "and stays on its rails")
    (check-true (< (abs (- (at 8 'road-wagon.z) (at 0 'road-wagon.z))) 0.005) "the road wagon holds")))

(test-case "Gristmill: 267 N.m at 120 rpm is 3,356 W, which sharp stones turn into 181 kg of flour an hour and dull ones 80.5; a weaker wheel stalls"
  ;; predicted before the first run (gristmill.rkt): 54 and 24 kg/kWh
  (when (godot-available?)
    (define run (godot-simulate 'gristmill #:seconds 60 #:sample-dt 10 #:actions '()))   ; no demo operator: the weak stone is left as it is (#157)
    (define (at t key) (cadr (assq key (cdr (for/first ([f run] #:when (>= (car f) (- t 1e-6))) f)))))
    (define w (* 120 (/ (* 2 pi) 60)))
    (check-= (at 60 'sharp.omega) w 0.01 "up to 120 rpm")
    (check-= (at 60 'sharp.grinding-power) (* 267 w) 5)
    (for ([stone '(sharp dull)] [yield '(54 24)])
      (define key (string->symbol (format "~a.flour" stone)))
      (define rate (/ (- (at 60 key) (at 30 key)) 30))
      (check-= rate (* yield (/ (* 267 w) 3.6e6)) (* 0.005 rate) (format "~a grinds ~a kg/h" stone (* 3600 rate))))
    (check-true (< (at 60 'weak.omega) 0.02) "the weak wheel can't turn its stones")
    (check-true (< (at 60 'weak.flour) 0.01) "and grinds next to nothing")))

;; Issue #43: fracture. Predicted before the first run (battering-rams.rkt):
;; a blow at about 2.2 m/s; 50.9, 29.1 and 33.7 MPa per m/s of blow for the
;; slim oak post, the stout one and the limestone column (112, 64 and 74 MPa);
;; the slim post and the column break, the stout one holds; the slim
;; post's break takes 209 J, the column's 1.5 J.
(test-case "Battering rams: a post snaps when F = v sqrt(k m) bends it past its strength, and the ram keeps what the break didn't take"
  (when (godot-available?)
    (define run (godot-simulate 'battering-rams #:seconds 1.5 #:sample-dt 1/120))
    (define (final key) (cadr (assq key (cdr (last run)))))
    ;; the ram: a 1 cm iron rod and a 16 cm ball on it, 2 m below the pivot
    (define L 2.0) (define br (* 0.08 L)) (define rho 7700)
    (define rod (* rho pi 0.0001 L)) (define bob (* rho 4/3 pi (expt br 3)))
    (define m (/ (+ (* rod L L 1/3) (* bob (+ (* L L) (* 0.4 br br)))) (* L L)))   ; I / L^2 at the ball
    (check-= m 134.05 0.05)
    (define h 0.8)
    (define (per-v E I c) (let ([k (/ (* 3 E I) (expt h 3))]) (values k (/ (* (sqrt (* k m)) h c) I))))
    (define-values (k-slim slim) (per-v 11e9 (/ (expt 0.08 4) 12) 0.04))
    (define-values (k-stout stout) (per-v 11e9 (/ (expt 0.14 4) 12) 0.07))
    (define-values (k-col col) (per-v 40e9 (/ (* pi (expt 0.3 4)) 64) 0.15))
    (check-= (/ slim 1e6) 50.9 0.1) (check-= (/ stout 1e6) 29.1 0.1) (check-= (/ col 1e6) 33.7 0.1)
    (define v (final 'slim-oak.break-speed))
    (check-true (< 2.1 v 2.3) (format "the ram strikes at ~a m/s" v))
    (check-= (final 'slim-oak.broken) 1 0)
    (check-= (final 'slim-oak.peak-stress) (* v slim 1e-6) (* 0.005 v slim 1e-6))
    (check-= (final 'column.broken) 1 0)
    (check-= (final 'column.peak-stress) (* (final 'column.break-speed) col 1e-6) (* 0.005 (final 'column.break-speed) col 1e-6))
    (check-= (final 'stout-oak.broken) 0 0 "the stout post holds")
    (check-true (< 60 (final 'stout-oak.peak-stress) 68) (format "at ~a MPa" (final 'stout-oak.peak-stress)))
    ;; the break takes F_b^2 / 2k, F_b the force that reaches 90 MPa (5 for limestone)
    (define (taken strength I c k) (let ([f (/ (* strength I) (* c h))]) (/ (* f f) (* 2 k))))
    (check-= (final 'slim-oak.energy-taken) (taken 90e6 (/ (expt 0.08 4) 12) 0.04 k-slim) 0.5)
    (check-= (final 'column.energy-taken) (taken 5e6 (/ (* pi (expt 0.3 4)) 64) 0.15 k-col) 0.05)))

;; Issue #65: hot-air engines on mirror heat on Mars. Predicted before the
;; run (mars-stirling.rkt), from the traced Q = 1,508.8 W on each receiver:
;; 98.5 / 40.2 / -27.7 C and 73.7 / 111.3 / 64.1 W; 55.8 W for the matched
;; engine with a 20 C cold side.
(test-case "Stirling engines on Mars: each hot end settles where eQ = radiation + K (T - Tc); the matched one gives the most; a warm cold side halves it"
  (define sigma 5.670374e-8) (define e 0.9) (define f 0.35)
  (define (steady Q K tc)                         ; bisection on eQ = e sigma (T^4 - Tc^4) + K (T - Tc), A = 1
    (let loop ([lo tc] [hi 2000.0] [n 0])
      (define mid (/ (+ lo hi) 2))
      (cond [(> n 200) mid]
            [(> (+ (* e sigma (- (expt mid 4) (expt tc 4))) (* K (- mid tc))) (* e Q)) (loop lo mid (add1 n))]
            [else (loop mid hi (add1 n))])))
  (define (power K T tc) (* f (- 1 (/ tc T)) K (- T tc)))
  (for ([ambient '(-63 20)])
    (define run (simulate 'mars-stirling #:seconds 8000 #:step 0.05 #:sample-dt 2000
                          #:set (list '(scene clock-rate 0) (list 'scene 'ambient ambient))))
    (define tc (+ ambient 273.15))
    (define powers
      (for/list ([engine '(small matched large)] [K '(3 9.36 36)])
        (define (at field) (final-of run (list engine field)))
        (define Q (at 'heat))
        (check-= Q 1508.8 0.1 "six heliostats' light on the aperture")
        (define T (steady Q K tc))
        (check-= (at 'hot-temperature) (- T 273.15) 0.05 (format "~a's hot end at ~a C" engine ambient))
        (check-= (at 'shaft-power) (power K T tc) 0.05 (format "~a's shaft power at ~a C" engine ambient))
        (check-= (at 'rpm) (* (/ (power K T tc) 3) (/ 60 (* 2 pi))) 0.2 "turning its 3 N m load at P / 3 rad/s")
        (at 'shaft-power)))
    (when (= ambient -63)
      (check-true (> (second powers) (max (first powers) (third powers))) "the matched engine gives the most")
      (check-= (second powers) 111.29 0.05))
    (when (= ambient 20)
      (check-= (second powers) 55.97 0.1 "a room-warm cold side halves it"))))

(test-case "High-pressure steam on Mars: each stroke does (P_boiler - P_air) A S; exhausting into 610 Pa it does 1.27 times what it does into 101 kPa"
  ;; predicted before the first run (steam-engines-mars.rkt): 186.3 and 146.6 J a stroke
  (when (godot-available?)
    (define run (godot-simulate 'steam-engines-mars #:seconds 8 #:sample-dt 1))
    (define (at t key) (cadr (assq key (cdr (for/first ([f run] #:when (>= (car f) (- t 1e-6))) f)))))
    (define area (/ (* pi 0.05 0.05) 4))
    (define S (- (sqrt (- (* 0.6 0.6) (* 0.05 0.05))) (sqrt (- (* 0.4 0.4) (* 0.05 0.05)))))
    (check-= S 0.20105 0.00001)
    (define works
      (for/list ([e '(open hut)])
        (define (g k) (at 8 (string->symbol (format "~a-cylinder.~a" e k))))
        (define dp (* 1000 (- (g 'pressure) (g 'exhaust-pressure))))
        (check-true (> (g 'strokes) 5) (format "the ~a engine runs" e))
        ;; the traced stroke is the crank's plus the joints' give
        (check-= (g 'stroke-length) S 0.004)
        (check-= (g 'stroke-work) (* dp area (g 'stroke-length)) (* 0.005 dp area S) (format "~a: W = dp A x the stroke it made" e))
        (check-= (g 'stroke-work) (* dp area S) (* 0.015 dp area S) (format "~a: W = dp A S" e))
        (g 'stroke-work)))
    (check-= (/ (first works) (second works)) (/ (- 472.6 0.61) (- 472.6 101.33)) 0.01 "the open engine does 1.271 times as much")
    (check-true (> (at 8 'open-flywheel.omega) (at 8 'hut-flywheel.omega)) "and its flywheel runs faster")))

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

(test-case "Shaduf: a counterweight for a half-full bucket balances it; a full bucket sinks and an empty one rises to the 25 degree stops (#121)"
  ;; counterweight x 1 m = 9.5 kg x 3 m + the 15.84 kg plank x 1 m: 44.34 kg
  (when (godot-available?)
    (define run (godot-simulate 'shaduf #:seconds 10 #:sample-dt 0.5))
    (for ([t '(2 5 10)]) (check-= (value-at run '(half-full angle) t) 0.0 0.3 (format "half full holds level at ~a s" t)))
    (check-= (final-of run '(full angle)) -25.0 0.3 "full bucket down at its stop")
    (check-= (final-of run '(empty angle)) 25.0 0.3 "empty bucket up at its stop")
    ;; the well (#176): the full bucket's underside, 1.632 - 1.2 - 0.185 = 0.247 m, is under the pool's 0.30 m surface
    (for ([t '(3 5 8 10)])
      (define bottom (- (value-at run '(full-bucket y) t) (/ (expt (/ 17 2700.0) 1/3) 2)))
      (check-= bottom 0.247 0.03 (format "full bucket's underside at ~a s" t))
      (check-true (< bottom 0.30) "dipped into the water"))
    (check-= (final-of run '(well water)) 300 1e-6 "litres in the pool")))

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
    (define run (godot-simulate 'torsion-catapult #:seconds 8 #:sample-dt 0.05 #:set '((arm catch 0))))   ; loosed at once (#155)
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

(test-case "Kitchen smoke jack: the chimney's draught carries the fire's waste heat, and the vanes balance its push against the spit"
  (define run (simulate 'kitchen-smoke-jack #:seconds 300 #:step 0.01 #:sample-dt 100))
  (define v (final-of run '(jack jet-speed)))
  (define mdot (final-of run '(jack air-flow)))
  (define dT (final-of run '(jack warming)))
  (check-= (* mdot 1005 dT) 1500 15 "half the 3 kW fire goes up the flue, warming the air")
  (check-= v (* 0.7 (sqrt (/ (* 2 9.81 2 dT) (+ 293.15 dT)))) 0.01 "the stack effect of a 2 m flue")
  ;; mdot (v - w r) r = load + bearing + windage w^2
  (define r 0.12)
  (define drag (* 0.5 1.2041 1.2 6 0.06 0.06 r r r))
  (define a drag) (define b (* mdot r r)) (define c (- 0.005 (* mdot v r)))
  (define w (/ (+ (- b) (sqrt (- (* b b) (* 4 a c)))) (* 2 a)))
  (check-= (final-of run '(jack omega)) w (* 0.01 w)))

(test-case "A world of 50 pendulums: each copy swings exactly as the pendulum does alone (#74)"
  (when (godot-available?)
    (define alone (godot-simulate 'pendulum-demo #:seconds 5 #:sample-dt 0.25))
    (define world (godot-simulate-world 'pendulums-50 #:seconds 5 #:sample-dt 0.25))
    (check-equal? (hash-count world) 50)
    (define (tilts run) (for/list ([f run]) (cadr (assq 'rod.rot-z (cdr f)))))
    (for ([(label run) world])
      (check-equal? (length run) (length alone) (format "~a has a full trace" label))
      (for ([a (tilts alone)] [b (tilts run)])
        (check-= b a 0.01 (format "~a swings as the pendulum alone" label))))))

(test-case "Editing a machine while it runs (#75): a block added to the swinging pendulum at 2 s leaves its swing exactly as it was"
  (when (godot-available?)
    (define alone (godot-simulate 'pendulum-demo #:seconds 5 #:sample-dt 0.25))
    (define world (godot-simulate-world 'live-edit-check #:seconds 5 #:sample-dt 0.25
                    #:env '(("HEROIC_LIVE_EDIT_AFTER" . "2")
                            ("HEROIC_EDITOR_INPUT" . "wait 2; cmd (block extra #:at (1.5 0.05 1.5) #:size 0.1); wait 5"))))
    (define edited (hash-ref world 'swinging))
    (check-equal? (length edited) (length alone) "the trace runs on across the edit")
    (for ([a alone] [b edited])
      (check-= (cadr (assq 'rod.rot-z (cdr b))) (cadr (assq 'rod.rot-z (cdr a))) 0.01
               (format "at ~a s the edited pendulum swings as the untouched one" (car a))))
    ;; and the edit really happened: the new block is in the machine by the end
    (check-not-false (assq 'extra.y (cdr (last edited))) "the added block is part of the running machine")))

;; Timed settings in the game's own run path (issue #150): (target field value at-seconds) used to be applied
;; before the first step by godot-simulate, the time dropped. Now the game queues them, as simhost does.
(test-case "Timed settings under godot-simulate: the thumb goes on the tank-leaks hole at 100 s, as under simulate"
  (when (godot-available?)
    (define plug '((plug-hole area 0 100)))
    (define sim (simulate 'tank-leaks #:seconds 300 #:step 0.01 #:sample-dt 1 #:set plug))
    (define game (godot-simulate 'tank-leaks #:seconds 300 #:sample-dt 1 #:set plug))
    ;; by hand: free until 100 s, then held; the same Torricelli draw-down the simulate test checks
    (define held (* 100 (leak-level 0.10 100)))
    (check-= (value-at game '(plugged level) 100) held 0.1 "the level when the thumb goes on")
    (check-= (value-at game '(plugged level) 300) held 0.1 "and 200 s later it has not moved")
    (check-= (value-at game '(plug-hole area) 99) 5 1e-6 "the hole was open at 99 s (a start-of-run set would have shut it)")
    (check-= (value-at game '(plug-hole area) 101) 0 1e-12)
    (for ([t '(50 100 200 300)])
      (check-= (value-at game '(plugged level) t) (value-at sim '(plugged level) t) 0.1
               (format "Godot and SimHost agree at ~a s" t)))))

(test-case "Timed settings under godot-simulate: the airlock opens its outer door at 600 s, and shuts it at 700 s"
  (when (godot-available?)
    (define cycle '((bleed open 1 400) (bleed open 0 600) (outer open 1 600) (outer open 0 700)
                    (pump speed 0 700) (inner open 1 700)))
    (define game (godot-simulate 'airlock #:seconds 720 #:sample-dt 1 #:set cycle))
    (define sim (simulate 'airlock #:seconds 720 #:step 0.05 #:sample-dt 1 #:set cycle))
    (define (opened run)
      (for/first ([f run] #:when (= 1 (cadr (assq 'outer.open (cdr f))))) (car f)))
    (check-= (opened sim) 600 1)
    (check-= (opened game) 600 1 "the outer door opens when the run reaches 600 s, not before the first step")
    (check-= (value-at game '(outer open) 650) 1 1e-9)
    (check-= (value-at game '(outer open) 710) 0 1e-9)
    (check-= (value-at game '(chamber pressure) 399) (value-at sim '(chamber pressure) 399) 0.05
             "the pump has pumped it down to its 5 kPa switch in both")))

(test-case "Timed settings under godot-simulate (#158): the constant-head tap is opened to 2 cm at 120 s and the float valve holds the head both before and after, as under simulate"
  (when (godot-available?)
    (define opened '((tap opening 0.04 120) (bare-tap opening 0.04 120)))   ; operator action: the same list under both hosts
    (define sim (simulate 'constant-head #:seconds 240 #:step 0.01 #:sample-dt 1 #:set opened))
    (define game (godot-simulate 'constant-head #:seconds 240 #:sample-dt 1 #:set opened))
    (define h1 (held-level 0.01))
    (define h2 (held-level 0.02))
    (check-= (value-at game '(tap opening) 119) 0.02 1e-9 "the tap is still at its starting 1 cm at 119 s: nothing was set before the first step")
    (for ([t '(60 110)]) (check-= (value-at game '(cistern level) t) (* 100 h1) 0.05 (format "game: held at 39.17 cm at ~a s" t)))
    (for ([t '(180 235)]) (check-= (value-at game '(cistern level) t) (* 100 h2) 0.05 (format "game: held at 38.38 cm at ~a s" t)))
    (for ([t '(60 110 180 235)])
      (check-= (value-at game '(cistern level) t) (value-at sim '(cistern level) t) 0.05 (format "Godot and SimHost agree on the level at ~a s" t))
      (check-= (value-at game '(outlet flow) t) (value-at sim '(outlet flow) t) 2e-3 (format "and on the draw at ~a s" t)))))

(test-case "Timed settings under godot-simulate (#158): the boiler-safety valve is tied down at 600 s and the guarded boiler then bursts, as under simulate"
  (when (godot-available?)
    (define tied '((guard lift 300 600)))
    (define sim (simulate 'boiler-safety #:seconds 700 #:step 0.01 #:sample-dt 1 #:set tied))
    (define game (godot-simulate 'boiler-safety #:seconds 700 #:sample-dt 1 #:set tied))
    (check-= (value-at game '(guarded pressure) 599) (/ bs-p-hold 1000) 0.02 "held at the safety valve's pressure until tied down")
    (check-= (value-at game '(guard flow) 601) 0 1e-12 "tied down, it vents nothing")
    (check-= (final-of game '(guarded burst)) 1 0 "and bursts before 700 s")
    (check-= (final-of game '(guarded burst-pressure)) 200 0.5)
    (check-= (final-of game '(guarded burst-time)) (final-of sim '(guarded burst-time)) 1 "Godot and SimHost agree on when")
    (check-= (value-at game '(guarded pressure) 599) (value-at sim '(guarded pressure) 599) 0.02)))

;; The mars-sols and greenhouse timed tests run 270,000 s and 2.7 million s under simulate: far past what the game's
;; 120 Hz physics can do, so the same two operator actions (clean the mirror, stack the harvest on the stove) are
;; checked here a few minutes in, with the sun held still (scenario: clock-rate 0), on both hosts.
(test-case "Timed settings under godot-simulate (#158): a mirror dusted over at 10 s throws e^-0.5 of its light until it is cleaned at 30 s, as under simulate"
  (when (godot-available?)
    (define dusted (- 1 (exp -0.5)))                  ; the fraction of the light the dust takes: 0.3935
    (define dust `((scene clock-rate 0) (mirror dust ,dusted 10) (mirror dust 0 30)))   ; scenario, then the operator's two actions
    (define sim (simulate 'mars-sols #:seconds 60 #:step 0.05 #:sample-dt 1 #:set dust))
    (define game (godot-simulate 'mars-sols #:seconds 60 #:sample-dt 1 #:set dust))
    (for ([run (list sim game)] [host '("SimHost" "Godot")])
      (check-= (value-at run '(mirror dust) 5) 0 0 (format "~a: clean until dusted at 10 s" host))
      (check-= (value-at run '(mirror dust) 20) dusted 1e-9 (format "~a: dusted" host))
      (check-= (value-at run '(mirror dust) 40) 0 0 (format "~a: cleaned at 30 s" host))
      (check-= (/ (value-at run '(mirror power) 20) (value-at run '(mirror power) 5)) (exp -0.5) 1e-6
               (format "~a: a dusted mirror throws e^-0.5 of what it did" host))
      (check-= (value-at run '(mirror power) 40) (value-at run '(mirror power) 5) 1e-6 (format "~a: and cleaned, the same as at first" host)))
    (for ([t '(5 20 40)])
      (check-= (value-at game '(mirror power) t) (value-at sim '(mirror power) t) 1e-3 (format "Godot and SimHost agree on the power at ~a s" t)))))

(test-case "Timed settings under godot-simulate (#158): the greenhouse harvest stacked on the stove at 300 s burns at 1 kW, as under simulate"
  (when (godot-available?)
    (define harvest '((scene clock-rate 0) (trees harvest 1000 300)))     ; scenario, then the operator's harvest
    (define sim (simulate 'greenhouse #:seconds 320 #:step 0.05 #:sample-dt 1 #:set harvest))
    (define game (godot-simulate 'greenhouse #:seconds 320 #:sample-dt 1 #:set harvest))
    ;; the wood grown by 300 s, 3.181e-7 kg/s net, burns at 1 kW / 15 MJ/kg = 6.667e-5 kg/s: 1.43 s
    (define net 3.181e-7)
    (for ([run (list sim game)] [host '("SimHost" "Godot")])
      (check-= (value-at run '(stove fuel) 299) 0 1e-12 (format "~a: nothing on the stove before the harvest" host))
      (check-= (value-at run '(trees wood) 299) (* net 299) 1e-6 (format "~a: the wood grown by then" host))
      (check-= (value-at run '(trees wood) 320) (* net 19.5) 5e-6 (format "~a: the trees start again from nothing" host))
      (check-= (value-at run '(stove fuel) 310) 0 1e-9 (format "~a: burned out" host)))))

(test-case "Timed settings under godot-simulate: a malformed or impossible setting is an error, not a skipped line"
  (when (godot-available?)
    (check-exn #rx"HEROIC_SET" (λ () (godot-simulate 'pendulum-demo #:seconds 1 #:set '((nosuchpart angle 1)))))
    (check-exn #rx"HEROIC_SET" (λ () (godot-simulate 'pendulum-demo #:seconds 1 #:env '(("HEROIC_SET" . "rod only-two")))))
    (check-exn #rx"never applied" (λ () (godot-simulate 'pendulum-demo #:seconds 1 #:set '((scene ambient 5 50)))))
    (check-exn #rx"target field value" (λ () (godot-simulate 'pendulum-demo #:seconds 1 #:set '((scene ambient)))))))

(test-case "Tripwire: a trigger under a falling weight fires when the weight arrives, and only then opens the sluice"
  ;; the weight's middle falls 1.5 m -> 0.95 m, the trigger's top face: h = 0.55 m,
  ;; t = sqrt(2 h / g) = 0.3349 s, or 0.3368 s with the engine's default 0.1/s damping
  ;; (until #33); the physics ticks at 120 Hz, so within a tick or two of either
  (when (godot-available?)
    (define run (godot-simulate 'trip-sluice #:seconds 4 #:sample-dt 0.05 #:set '((weight-cord tether 0))))   ; the cord let go at once (#155)
    (define fired-at (final-of run '(tripwire fired-at)))
    (check-= fired-at (sqrt (/ (* 2 0.55) 9.81)) 0.012 "sqrt(2 h / g)")
    (check-= (final-of run '(tripwire fired)) 1 0)
    ;; the action happened: the gate went from shut to 0.05 ...
    (check-= (min-of run '(gate opening)) 0 0)
    (check-= (final-of run '(gate opening)) 0.05 1e-9)
    ;; ... and the reach, dry until then, is filling after it
    (check-= (min-of run '(reach water)) 0 0)
    (check-true (> (final-of run '(reach water)) 10) "the pool ran down the race into the reach")
    ;; the frame just before the fire still shows a shut gate and a dry reach
    (define times (times-of run))
    (define before (for/last ([t times] [i (in-naturals)] #:when (< t (- fired-at 0.05))) i))
    (check-= (list-ref (values-of run '(gate opening)) before) 0 0)
    (check-= (list-ref (values-of run '(reach water)) before) 0 0)
    ;; and the weight really had arrived: it is at or below the box's top face by then
    (check-true (< (final-of run '(weight y)) 0.95))))

;; ---------------------------------------------------------------------------
;; Earth's machines on Mars (issue #38): the same formulas under Mars's
;; numbers. The working is in racket/machines/earth-machines-on-mars.rkt.

(test-case "On Mars: 3.71 m/s², 610 Pa of 43.49 g/mol air at -63 °C, 0.01518 kg/m³, boiling at 0.0995 °C"
  (define run (simulate 'earth-machines-on-mars #:seconds 12 #:step 0.01 #:sample-dt 0.01))
  (check-= (final-of run '(scene gravity)) 3.71 1e-12)
  (check-= (final-of run '(scene pressure)) 0.61 1e-12)
  (check-= (final-of run '(scene molar-mass)) 43.4887 1e-4)
  (check-= (final-of run '(scene air-density)) 0.0151833 1e-7 "610 x 0.0434887 / (8.314 x 210.15)")
  (check-= (final-of run '(scene boiling-point)) 0.0995 1e-4 "Antoine: 1730.63 / (8.07131 - log10(610 / 133.322)) - 233.426")
  ;; the windmill's wind carries 1/79.3 of Earth's power: 1/2 rho pi 5^2 10^3
  (check-= (final-of run '(mill wind-power)) 596.25 0.01)
  ;; the pump's reach, (610 - 605.58) / (1000 x 3.71): it lifts nothing
  (check-= (final-of run '(pump limit)) 0.0011917 1e-6)
  (check-true (> (final-of run '(pump strokes)) 2) "it is worked")
  (check-= (final-of run '(pump delivered)) 0 1e-12)
  ;; the kettle boils as soon as it passes 0.0995 °C: about 1.1 s at 0.089 K/s
  (define boiling (for/first ([f run] #:when (> (cadr (assq 'kettle.pressure (cdr f))) 0)) f))
  (check-= (car boiling) 1.11 0.03)
  (check-= (cadr (assq 'kettle.temperature (cdr boiling))) 0.0995 0.001)
  ;; the bearing pendulum's period, 2 pi sqrt(I / (m g d)) (1 + theta^2/16) = 3.245 s
  (define angle (for/list ([f run]) (cons (car f) (cadr (assq 'pivot.angle (cdr f))))))
  (define ups (for/list ([a angle] [b (cdr angle)] #:when (and (< (cdr a) 0) (>= (cdr b) 0)))
                (+ (car a) (* (- (car b) (car a)) (/ (- (cdr a)) (- (cdr b) (cdr a)))))))
  (check-true (>= (length ups) 3))
  (for ([a ups] [b (cdr ups)]) (check-= (- b a) 3.245 0.01)))

(test-case "On Mars a Jolt pendulum swings sqrt(9.81 / 3.71) = 1.626 times as slowly as the same one on Earth"
  (when (godot-available?)
    (define (periods run key)
      (define tilt (for/list ([f run]) (cons (car f) (cadr (assq key (cdr f))))))
      (define ups (for/list ([a tilt] [b (cdr tilt)] #:when (and (< (cdr a) 0) (>= (cdr b) 0)))
                    (+ (car a) (* (- (car b) (car a)) (/ (- (cdr a)) (- (cdr b) (cdr a)))))))
      (for/list ([a ups] [b (cdr ups)]) (- b a)))
    (define earth (periods (godot-simulate 'pendulum-demo #:seconds 8 #:sample-dt 0.01) 'rod.rot-z))
    (define mars (periods (godot-simulate 'earth-machines-on-mars #:seconds 8 #:sample-dt 0.01) 'clock.rot-z))
    ;; first swings, from the same 40°; Jolt's per-second damping takes a
    ;; little more amplitude out of Mars's longer swing, so a little less
    ;; than 1.626 (measured 1.622)
    (check-= (/ (car mars) (car earth)) 1.6261 0.01)))

(test-case "Holy water: a coin tips a lever, the plug follows it, the part-opened spout passes Torricelli's flow, and one coin buys a fixed dose"
  ;; the spout is 12 mm across (pi d^2/4 = 1.131 cm2), 5 cm up an urn holding 20 L over 0.05 m2:
  ;; head 0.35 m, wide open Q = 0.6 A sqrt(2 g h) = 0.1778 L/s; below a quarter of the bore
  ;; (3 mm) of plug lift the area is the curtain pi d lift, so Q = 0.6 pi d lift sqrt(2 g h)
  (when (godot-available?)
    (define run (godot-simulate 'holy-water #:seconds 4 #:sample-dt 0.05))
    (define d 0.012)
    (define lifts (values-of run '(spout lift)))     ; mm
    (define heads (values-of run '(spout head)))     ; cm over the hole
    (define flows (values-of run '(spout flow)))     ; L/s
    (define (predicted lift-mm head-cm)
      (define lift (/ lift-mm 1000.0))
      (define area (if (>= lift (/ d 4)) (/ (* pi d d) 4) (* pi d lift)))
      (* 1000 0.6 area (sqrt (* 2 9.81 (/ head-cm 100.0)))))
    ;; in every frame, whatever the plug's lift, the flow is the orifice formula for it
    (for ([lift lifts] [head heads] [flow flows])
      (check-= flow (predicted lift head) 0.003 (format "lift ~a mm, head ~a cm" lift head)))
    ;; the plug was seen part open, not only open or shut, and passed the part-open flow
    (check-true (for/or ([lift lifts]) (< 0.1 lift 2.9)) "some frame has the plug part-way")
    (check-= (max-of run '(spout flow)) 0.1778 0.002 "wide open")
    ;; the coin tipped the lever a long way; it ended up off the pan, the lever back, the plug seated
    (check-true (> (max-of run '(beam angle)) 17) "past the coin's friction angle, atan 0.3 = 16.7 degrees")
    (check-true (< (final-of run '(coin y)) 0.05) "the coin is on the ground")
    (check-= (final-of run '(beam angle)) 0 0.5)
    (check-= (final-of run '(spout lift)) 0 0)
    (check-= (final-of run '(spout flow)) 0 0)
    ;; a fixed dose: what ran out is the flow integrated over the time the plug was open, and it stops
    (define times (times-of run))
    (define dose (for/sum ([t0 times] [t1 (cdr times)] [f0 flows] [f1 (cdr flows)]) (* (- t1 t0) (/ (+ f0 f1) 2))))
    (check-= (final-of run '(spout lost)) dose (* 0.05 dose) "the flow integrated over the open time")
    (check-true (< 0.1 (final-of run '(spout lost)) 0.4) "a fraction of a litre a coin")
    (define lost (values-of run '(spout lost)))
    (check-= (list-ref lost (- (length lost) 1)) (list-ref lost (quotient (length lost) 2)) 1e-9 "and it has stopped")))
;; ---------------------------------------------------------------------------
;; Links between machines in a world (issue #78)

(test-case "A pipe between two machines (#78): the cistern drains into the trough exactly as the same pipe inside one machine does"
  (when (godot-available?)
    ;; cistern-and-trough.rkt: 229.4 L left at 60 s, 122.9 L at 120 s
    (define world (godot-simulate-world 'linked-pipe #:seconds 120 #:sample-dt 10))
    (define-values (a b one) (values (hash-ref world 'tank-a) (hash-ref world 'tank-b) (hash-ref world 'reference)))
    (check-equal? (length a) (length one))
    (for ([fa a] [fb b] [f1 one])
      (define (v frame key) (cadr (assq key (cdr frame))))
      (check-= (v fa 'cistern.water) (v f1 'cistern.water) 1e-6 (format "cistern at ~a s" (car f1)))
      (check-= (v fb 'trough.water) (v f1 'trough.water) 1e-6 (format "trough at ~a s" (car f1))))
    (check-= (value-at a '(cistern water) 60) 229.4 0.5)
    (check-= (value-at a '(cistern water) 120) 122.9 0.5)
    (define links (hash-ref world 'links))
    (check-= (value-at links '(feed flow) 60) (value-at one '(feed flow) 60) 1e-6 "the link's flow is the one pipe's")
    (check-equal? (value-at links '(feed unfinished) 60) 0)))

(test-case "A shaft between two machines (#78): the free sails drive the dry mill's stones at 14.32 rpm with 8171 N·m"
  (when (godot-available?)
    ;; dry-mill.rkt: lambda = 2.5 at the stones' load; settled with a 10 s time constant
    (define world (godot-simulate-world 'linked-shaft #:seconds 120 #:sample-dt 10))
    (define links (hash-ref world 'links))
    (check-= (value-at (hash-ref world 'sails) '(sails rpm) 120) 14.32 0.01)
    (check-= (value-at (hash-ref world 'mill) '(wheel rpm) 120) 14.32 0.01)
    (check-= (value-at links '(drive torque) 120) 8171 5)
    (check-= (value-at links '(drive power) 120) 12260 10)))

(test-case "A shaft between two Jolt machines (#78): the treadwheel turns the separate hoist's drum and lifts the stone, as the one-machine crane does (#191)"
  (when (godot-available?)
    ;; crane-hoist.rkt: the shaft carries the stone's 583.2 x 9.81 x 0.25 = 1430.3 N·m. The
    ;; shaft is locked within Jolt's step (#191), so the pair spins up as the one-machine
    ;; crane's arbor does: wheel 1818.3 + drum 4.5 kg·m², and the stone's m r² = 36.5 felt
    ;; through the rope, 1859.3 kg·m² against the bodies' 0.2/s damping on 1822.8 (Godot's
    ;; default 0.1/s went with #30/#33), a time constant of 1859.3 / (0.2 x 1822.8) = 5.10 s.
    ;; The walkers' surplus over the stone heads the pair past their 3 rpm (0.31416): fitted
    ;; to the one machine's trace (w(5) 0.2007, w(10) 0.2762), for 0.3216 rad/s, which
    ;; reaches the cap at 19.3 s, so at 20 s the shaft turns at 3 rpm and the rope comes in at
    ;; 0.25 x 0.31416 = 7.854 cm/s. (The fit's 117.2 N·m surplus is 2.4 more than 1545.1 -
    ;; 1430.3 leaves, the one machine's as much as the split's; on 114.8 alone the cap would
    ;; come past 30 s, so the surplus is too fine a margin to work by hand: the test compares
    ;; the split with the one machine instead.)
    ;; (Coupled only once a tick, before Jolt's step, the drum ran a tick of the shaft's
    ;; torque behind the walkers' wheel: 2.937 rpm at 20 s and the rope 2.25% slow.)
    (define world (godot-simulate-world 'split-crane #:seconds 20 #:sample-dt 1))
    (define one (godot-simulate 'roman-crane #:seconds 20 #:sample-dt 1 #:actions '()))
    (define-values (links hoist walkers) (values (hash-ref world 'links) (hash-ref world 'hoist) (hash-ref world 'walkers)))
    (check-= (value-at links '(axle torque) 20) 1430.3 1.0)
    (define omega (* (value-at links '(axle rpm) 20) 2 pi 1/60))
    (define cap (* 3 2 pi 1/60))
    (check-= omega cap 1e-5 "at 20 s, the walkers' 3 rpm")
    (check-= (value-at links '(axle driven-rpm) 20) (value-at links '(axle rpm) 20) 1e-9 "one speed both sides")
    ;; the spin-up's time constant. The stone rests on the ground (#148), so the wheel starts at w0 > 0 (it winds in the
    ;; rope's stretch nearly unloaded first) and w(t) = w-inf - (w-inf - w0) e^(-t/tau); three equally spaced samples
    ;; give e^(-5/tau) whatever w0 and w-inf are: (w(15) - w(10)) / (w(10) - w(5)). Measured 0.3748 (tau 5.095 s)
    (define tau (/ 1859.3 (* 0.2 1822.8)))
    (define (w t) (value-at walkers '(tympanus omega) t))
    (check-= (/ (- (w 15) (w 10)) (- (w 10) (w 5))) (exp (/ -5 tau)) 0.003 "e^(-5/tau), tau = 1859.3 / (0.2 x 1822.8) = 5.10 s")
    ;; and the same spin-up as the one machine's, second by second
    (for ([t (in-range 1 21)])
      (check-= (value-at walkers '(tympanus omega) t) (value-at one '(tympanus omega) t) 1e-5
               (format "the treadwheel at ~a s, split as one" t)))
    (define rise-rate (/ (- (value-at hoist '(stone y) 20) (value-at hoist '(stone y) 10)) 10))
    (check-= rise-rate (/ (- (value-at one '(stone y) 20) (value-at one '(stone y) 10)) 10) 1e-5 "the stone rises as in the one machine")))

(test-case "A live edit of a linked machine (#78): the pipe takes hold of the rebuilt cistern and the flow runs on unbroken"
  (when (godot-available?)
    (define world (godot-simulate-world 'linked-pipe #:seconds 30 #:sample-dt 5
                    #:env '(("HEROIC_LIVE_EDIT_AFTER" . "10")
                            ("HEROIC_EDITOR_INPUT" . "wait 2; cmd (block extra #:at (1.5 0.05 1.5) #:size 0.1); wait 5"))))
    (define-values (a one) (values (hash-ref world 'tank-a) (hash-ref world 'reference)))
    (for ([fa a] [f1 one])
      (check-= (cadr (assq 'cistern.water (cdr fa))) (cadr (assq 'cistern.water (cdr f1))) 1e-6
               (format "at ~a s the edited cistern drains as the untouched pair" (car f1))))
    (check-not-false (assq 'extra.y (cdr (last a))) "the edit happened")))


(test-case "Belt drive: a tight belt grips at half speed, a loose one slips at exactly its limit"
  ;; drums of 10 and 20 cm, 60 cm apart, hemp mu 0.5: wrap pi - 2 asin(1/6) = 160.8 degrees;
  ;; the belt carries F = 2 T0 tanh(mu theta / 2) = 1.2109 T0 -- 6.054 N at 5 N, 12.109 N at 10 N --
  ;; against a 1.0 N.m motor on a 10 cm pulley, which needs 10 N: the loose belt cannot carry it
  (when (godot-available?)
    (define run (godot-simulate 'belt-drive #:seconds 30 #:sample-dt 0.25))
    (check-= (final-of run '(tight-belt wrap)) 160.8 0.1)
    (check-= (final-of run '(tight-belt capacity)) 12.109 0.01)
    (check-= (final-of run '(loose-belt capacity)) 6.054 0.01)
    (check-= (/ (final-of run '(tight-belt capacity)) (final-of run '(loose-belt capacity))) 2.0 1e-9
             "twice the tension, twice the force")
    ;; loose: slipping the whole time, and the force it carries is its limit to the newton's hundredth
    (for ([t (in-list '(0.25 0.5 1.0 2.0 3.0))])
      (check-= (value-at run '(loose-belt force) t) 6.054 0.05 (format "loose belt force at ~a s" t))
      (check-true (> (value-at run '(loose-belt slip) t) 0.5) (format "loose belt slipping at ~a s" t)))
    ;; with the engine's default damping gone (#33) each drum has only its bearing's 0.2/s: the small one at
    ;; 428 (1 - e^(-0.2 t)) rad/s until the motor's 2000 rpm, 209.4 rad/s, at 3.36 s, and the big at 41.1 (1 - e^(-0.2 t))
    ;; (20 cm rim): the rims differ by 34.58 (1 - e^(-0.2 t)) m/s, 6.27 at 1 s and 15.60 at 3 s, then by 20.94 - 8.22 (1 -
    ;; e^(-0.2 t)), 12.74 at 30 s. (Until #187 the engine capped every body at 47.1 rad/s, and the small pulley with it.)
    (for ([t (in-list '(1.0 3.0))])
      (check-= (value-at run '(loose-belt slip) t) (* 34.58 (- 1 (exp (* -0.2 t)))) 0.15
               (format "loose belt's slip at ~a s" t)))
    (check-= (value-at run '(loose-driver omega) 3.0) (* 428.4 (- 1 (exp -0.6))) 2.0 "the small pulley racing, 193.1 rad/s")
    (for ([t (in-list '(4.0 30.0))])
      (check-= (value-at run '(loose-driver omega) t) (* 2000 2 pi 1/60) 0.01 (format "at the motor's 2000 rpm at ~a s" t)))
    (check-= (value-at run '(loose-belt slip) 30.0) (- 20.944 (* 8.22 (- 1 (exp -6)))) 0.15 "still slipping at 30 s")
    ;; at 0.25 s: the small pulley at 85.6 rad/s2 less its damping (20.9 at 0.25 s), the
    ;; big one at about 2 rad/s, a ratio of 0.09 and not the 0.5 of a belt that holds
    (check-= (value-at run '(loose-driver omega) 0.25) 21.4 1.0)
    (check-= (value-at run '(loose-driven omega) 0.25) 2.0 0.4)
    (check-true (< (/ (value-at run '(loose-driven omega) 0.25) (value-at run '(loose-driver omega) 0.25)) 0.15))
    ;; tight: never slips, carries less than its limit, and the big pulley turns at half the small one's speed, a
    ;; little under (the belt is a tick behind)
    (for ([t (in-list '(0.5 1.0 1.5 2.0 2.5))])
      (check-= (value-at run '(tight-belt slip) t) 0 1e-9 (format "tight belt slip at ~a s" t))
      (check-true (< (value-at run '(tight-belt force) t) 12.109) (format "tight belt inside its limit at ~a s" t)))
    (for ([t (in-list '(1.0 2.0 3.0))])
      (define ratio (/ (value-at run '(tight-driven omega) t) (value-at run '(tight-driver omega) t)))
      (check-= ratio 0.5 (* 0.5 0.12) (format "tight belt ratio at ~a s" t))
      (check-true (<= ratio 0.5001)))
    (check-= (/ (value-at run '(tight-driven omega) 3.0) (value-at run '(tight-driver omega) 3.0)) 0.5 0.03)
    ;; and they speed up together, near 24.1 rad/s2 less the bearings' drag: 22 rad/s at 1 s, and settle where the
    ;; bearings take the motor's whole 1.0 N.m: 1.0 / (0.2 (I1 + 0.985 I2 / 4)) = 122.3 rad/s, short of its 2000 rpm
    (check-= (value-at run '(tight-driver omega) 1.0) 22.3 1.5)
    (check-= (value-at run '(tight-driver omega) 30.0) (* 122.3 (- 1 (exp -6))) 1.0 "the tight pair's top speed")))
;; ---------------------------------------------------------------------------
;; Two inflated modules on Mars (issue #39). Working in racket/machines/two-modules.rkt.

(test-case "A punctured module's air falls as P0 e^(-t/2513 s) while choked, and keeps its 21% oxygen"
  (define run (simulate 'two-modules #:seconds 3600 #:step 0.05 #:sample-dt 60))
  (define (at t k) (for/first ([f run] #:when (>= (car f) (- t 1e-6))) (cadr (assq k (cdr f)))))
  (check-= (at 1800 'punctured.pressure) (* 50 (exp (/ -1800 2513.0))) 0.02 "24.43 kPa")
  (check-= (at 3600 'punctured.pressure) (* 50 (exp (/ -3600 2513.0))) 0.02 "11.94 kPa")
  (check-= (at 3600 'punctured.choked) 1 0)
  (check-= (at 3600 'punctured.o2) 21 1e-6 "what leaks out is its own mixture")
  (check-= (at 3600 'punctured.temperature) 20 1e-9 "a slow leak: the air left keeps its walls' temperature"))

(test-case "A heated, sealed module settles at T_out + Q/UA = 27 °C with time constant C/UA; its pump reaches 12.85 m"
  (define run (simulate 'two-modules #:seconds 3600 #:step 0.05 #:sample-dt 60))
  (define (at t k) (for/first ([f run] #:when (>= (car f) (- t 1e-6))) (cadr (assq k (cdr f)))))
  ;; 27 - 7 e^(-3600/1283) = 26.58 °C for the module alone; the ~20 mol of
  ;; nitrogen the locker leaks into it adds 1.6% to its heat capacity, 26.56
  (check-= (at 3600 'sealed.temperature) 26.56 0.05)
  ;; the pump inside draws with the module's 50 kPa at 20 °C water: (50000 - 2339) / (1000 x 3.71)
  (check-= (at 0 'pump.limit) 12.849 0.001)
  (check-= (final-of run '(pump delivered)) 125 1e-6 "it filled the 125 L trough, lifting 2.5 m on Mars")
  ;; the locker's nitrogen went into the module round it, not onto Mars: not a gram lost
  (check-= (+ (at 3600 'sealed.mass) (at 3600 'locker.mass)) (+ (at 0 'sealed.mass) (at 0 'locker.mass)) 1e-9)
  (check-= (at 3600 'locker.pressure) (at 3600 'sealed.pressure) 0.01 "the pinhole has let the two come level"))

;; ---------------------------------------------------------------------------
;; Oxygen-limited fire (issue #40). Working in racket/machines/stove-rooms.rkt.

(test-case "A charcoal stove in a sealed room burns its oxygen down to 15% and goes out at 12,924 s; ventilated, it settles at 18.19%"
  (define run (simulate 'stove-rooms #:seconds 28800 #:step 0.05 #:sample-dt 30))
  (define (at t k) (for/first ([f run] #:when (>= (car f) (- t 1e-6))) (cadr (assq k (cdr f)))))
  (define out (for/first ([f run] #:when (= 0 (cadr (assq 'stove.lit (cdr f))))) (car f)))
  (check-= out 12924 30 "the oxygen above 15%, 74.21 mol, at 5.742e-3 mol/s")
  (check-= (at (+ out 30) 'stove.burned) 0.8913 0.001)
  (check-= (at (+ out 30) 'sealed.co2) 5.99 0.01 "each O2 swapped for a CO2")
  (check-= (at (+ out 30) 'sealed.o2) 15 0.01)
  ;; while it burns the room sits at 20 + 2000/50 = 60 °C and 115.15 kPa
  (check-= (at 10000 'sealed.temperature) 60 0.01)
  (check-= (at 10000 'sealed.pressure) 115.15 0.01)
  ;; ventilated: 5 L/s of outside air, 20.95% - 5.742e-3 / 0.2079 = 18.19%
  (check-= (at 28800 'supplied.o2) 18.19 0.05)
  (check-= (at 28800 'stove2.lit) 1 0)
  ;; outdoors: 8 h burn 1.986 kg of the 3
  (check-= (at 28800 'stove3.fuel) 1.0138 0.001))


(test-case "Crate tongs: tongs hold what 2 mu N allows, drop what it doesn't, and let go on cue"
  ;; bronze on iron: mu = min(0.30, 0.40) = 0.30; tongs of N carry 2 mu N -- 60 N at 100 N, 84 N at 140 N.
  ;; light crate 8.7 cm iron = 5.07 kg -> 49.7 N: held. heavy 9.9 cm = 7.47 kg -> 73.3 N: over 60, dropped
  ;; (needs 122 N), but held by the 140 N tongs. The light one is let go after 1 s, 1.2 m up, onto a
  ;; step 0.3 m up: its middle falls 0.8565 m, lands after sqrt(2 h / g) = 0.418 s at sqrt(2 g h) = 4.10 m/s.
  (when (godot-available?)
    ;; a frame a tick (120 Hz): sampled every 0.01 s the last frame before the strike can be two ticks
    ;; early, 0.16 m/s short of the speed it lands at
    (define run (godot-simulate 'crate-tongs #:seconds 3 #:sample-dt 1/120))
    (define (t-first pred path)               ; the first sample time at which pred holds for a field
      (for/first ([t (times-of run)] [v (values-of run path)] #:when (pred v)) t))
    ;; the limits and the loads, in newtons
    (check-= (value-at run '(light-tongs capacity) 0.5) 60.0 0.05)
    (check-= (value-at run '(firm-tongs capacity) 0.5) 84.0 0.05)
    (check-= (value-at run '(light-tongs load) 0.5) (* 5.07 9.81) 0.6 "8.7 cm of iron, 5.07 kg")
    (check-= (value-at run '(firm-tongs load) 0.5) (* 7.47 9.81) 0.9 "9.9 cm of iron, 7.47 kg")
    (check-true (< (value-at run '(light-tongs load) 0.5) (value-at run '(light-tongs capacity) 0.5)) "the light crate is inside the limit")
    (check-true (> (* 7.47 9.81) 60.0) "and the heavy one is past the 100 N tongs' limit")
    ;; the light crate is held until it is let go; the firm tongs never let go; the heavy crate is dropped at once
    (check-= (value-at run '(light-tongs held) 0.5) 1 0)
    (check-= (value-at run '(light-crate y) 0.5) 1.2 0.005)
    (check-= (value-at run '(firm-tongs held) 2.9) 1 0)
    (check-= (min-of run '(firm-crate y)) 1.2 0.005 "held in the air the whole time")
    (check-= (value-at run '(heavy-tongs overloaded) 0.5) 1 0 "the 100 N tongs gave way under the heavy crate")
    (check-= (value-at run '(heavy-tongs held) 0.5) 0 0)
    ;; let go on cue: the trigger fires at 1 s of holding, and the crate falls the predicted height in the predicted time
    (define let-go (final-of run '(let-go fired-at)))
    (check-= let-go 1.0 0.03)
    (define landed (t-first (λ (y) (< y 0.36)) '(light-crate y)))
    (check-= (- landed let-go) (sqrt (/ (* 2 0.8565) 9.81)) 0.04 "the time to fall 0.8565 m")
    (check-= (- (min-of run '(light-crate vy))) (sqrt (* 2 9.81 0.8565)) 0.15 "the speed it lands at")
    ;; the heavy crate falls 0.8505 m the moment the tongs give way
    ;; (the first frame it is moving up again: the first strike leaves its middle above 0.36 m)
    (define heavy-landed (t-first (λ (vy) (> vy 0)) '(heavy-crate vy)))
    (check-= heavy-landed (sqrt (/ (* 2 0.8505) 9.81)) 0.05)
    ;; and comes to rest on the step, its middle half its height up: 0.3 + 0.0495
    (check-= (final-of run '(heavy-crate y)) 0.3495 0.005)
    (check-= (final-of run '(light-crate y)) 0.3435 0.005)))
;; A channel that holds water (issue #36)

(test-case "Dam break (#36): the gate goes at 20 s, the wave reaches the low pond between 33.8 and 52.5 s, and every litre is accounted for"
  ;; dam-break.rkt: 5 m3 at 250 L/s = 20 s to 55 cm; then waves on the race run at
  ;; most 2.91 m/s (u + sqrt(g h) at normal depth) and a kinematic shock at 1.23 m/s
  (define run (simulate 'dam-break #:seconds 120 #:step 0.01 #:sample-dt 1))
  (check-= (for/first ([f run] #:when (= 1 (cadr (assq 'full.fired (cdr f))))) (car f)) 20 1.01 "the gate goes at 20 s")
  (define arrival (final-of run '(race arrival)))
  (check-true (< 33.8 arrival 52.5) (format "the wave reached the low pond at ~a s" arrival))
  (check-= (value-at run '(race depth-at) 22) 0 1e-9 "the race is dry below the head just after the gate goes")
  (for ([f run])
    (define (v k) (cadr (assq k (cdr f))))
    (check-= (+ (v 'millpond.water) (v 'race.stored) (v 'low-pond.water)) (+ 50000 (* 250 (car f))) 1e-3
             (format "at ~a s pond + race + low pond = what there was + the stream's" (car f))))
  ;; the free weir at the head once the gate is up: 1.705 x 0.5 x (level - 0.2 m)^1.5
  (check-= (value-at run '(race flow) 25) (* 1.705 0.5 (expt (- (/ (value-at run '(millpond level) 25) 100) 0.2) 1.5) 1000) 2))


(test-case "Continuous collision detection: a bolt at 77 m/s is stopped by a 2 cm plank, and without it goes through"
  ;; 299 m of fall, with no damping (#33), is 7.81 s and sqrt(2 g 299) = 76.6 m/s: 0.64 m a tick at 120 Hz,
  ;; against a plank 2 cm thick and a bolt 5 cm across (7 cm): a bolt only tested where it stands
  ;; each tick can be on one side of the plank one tick and the far side the next
  (when (godot-available?)
    ;; a frame a tick: the plain bolt is below the plank for three ticks only, at 0.64 m a tick
    (define run (godot-simulate 'tunnel-test #:seconds 12 #:sample-dt 1/120))
    (define fastest (- (min-of run '(bolt-fast vy))))
    (check-= fastest 76.6 2.0 "it arrives at the speed the fall gives")
    (check-true (> (/ fastest 120) 0.07) "each tick it moves further than the plank and the bolt are thick")
    ;; swept: it never gets below the plank (1.0 m up); its middle stays above 1.0 m the whole run
    (check-true (> (min-of run '(bolt-fast y)) 1.0) (format "the swept bolt stayed above its plank (lowest ~a m)" (min-of run '(bolt-fast y))))
    ;; not swept: through the plank and on to the floor, its middle reaching ground level
    (check-true (< (min-of run '(bolt-plain y)) 0.7) (format "the plain bolt went through (lowest ~a m)" (min-of run '(bolt-plain y))))
    ;; the same bolt, the same fall, the same speed: only the sweep differs
    (check-= (- (min-of run '(bolt-plain vy))) fastest 0.5)
    ;; lead on lead (#192): e = 0.2 gives back 15.3 m/s, 12.0 m of rise above the plank (its middle at 1.045 m), and the
    ;; bolts come to rest where the lesson says, in view of the planks: the swept one on its plank, the plain one under it
    (define peak (for/fold ([m 0]) ([t (times-of run)] [y (values-of run '(bolt-fast y))] #:when (> t 8)) (max m y)))
    (check-= peak (+ 1.045 (/ (sqr (* 0.2 76.5)) (* 2 9.81))) 0.3 (format "the swept bolt rose to ~a m" peak))
    (check-= (final-of run '(bolt-fast y)) 1.045 0.01 "at rest on its plank by 12 s")
    (check-= (final-of run '(bolt-plain y)) 0.025 0.01 "at rest on the ground under its plank")
    (check-= (final-of run '(bolt-plain x)) 0.6 0.1 "under its plank, not beside it")))

;; ---------------------------------------------------------------------------
;; An airlock on Mars (issue #41). Working in racket/machines/airlock.rkt.

(test-case "An airlock cycle: pumped down in 368 s for 288.7 kJ, bled to Mars as e^(-t/134 s), losing 0.4157 kg"
  (define run (simulate 'airlock #:seconds 800 #:step 0.05 #:sample-dt 1
                        #:set '((bleed open 1 400) (bleed open 0 600) (outer open 1 600) (outer open 0 700)
                                (pump speed 0 700) (inner open 1 700))))
  (define (at t k) (for/first ([f run] #:when (>= (car f) (- t 1e-6))) (cadr (assq k (cdr f)))))
  (define stop (for/first ([f run] #:when (= 0 (cadr (assq 'pump.running (cdr f))))) (car f)))
  (check-= stop 368.4 1 "8/0.05 x ln 10")
  (check-= (at 399 'chamber.pressure) 5 1e-6 "its pressure switch")
  (check-= (at 399 'habitat.pressure) 56 1e-6 "45 kPa x 8 m3 pushed into 60 m3")
  (check-= (at 399 'pump.work) 288.68 0.3 "integral of V ln(P_hab/P) dP, kJ")
  (check-= (at 500 'chamber.pressure) (* 5 (exp (/ -100 134.03))) 0.005 "choked through the 5 cm2 bleed")
  (check-= (at 699 'chamber.pressure) 0.61 1e-4 "down to Mars's pressure")
  (check-= (+ (at 699 'bleed.passed) (at 699 'outer.passed)) 0.41573 1e-4 "V x (5000 - 610) Pa of air, lost")
  (check-= (at 800 'chamber.pressure) (at 800 'habitat.pressure) 1e-3 "the inner door open, they stand level"))

(test-case "Trip-hammer: four pegs a turn, each drop at sqrt(2 g h), n m g h of work a turn, and a weak wheel stalls on the ramp"
  ;; strong wheel: 30 rpm, 4 pegs -> 8 strikes in 4 s; each lifts 5 kg by 0.1 m: 4.905 J a peg, 19.62 J a turn,
  ;; a mean torque of 19.62 / 2 pi = 3.12 N.m; each drop 0.1 m: sqrt(2 g h) = 1.401 m/s (less the engine's
  ;; default damping). Weak wheel: 5 N.m: stalls where m g y'(phi) = 5, sin(pi phi / alpha) = 5 / 9.81,
  ;; phi = 7.7 degrees in, the hammer 0.70 cm up, never striking.
  (when (godot-available?)
    (define run (godot-simulate 'trip-hammer #:seconds 8 #:sample-dt 0.25))
    (check-= (value-at run '(strong-wheel omega) 4.0) (* 30 (/ (* 2 pi) 60)) 0.05 "30 rpm")
    (check-= (value-at run '(strong-hammer strikes) 4.0) 8 1 "four pegs, two turns")
    (check-= (value-at run '(strong-hammer strikes) 8.0) 16 1 "and eight in 4 s more")
    (check-= (final-of run '(strong-hammer speed)) (sqrt (* 2 9.81 0.1)) 0.03 "sqrt(2 g h)")
    ;; the wheel gives m g h of work a peg
    (define strikes (value-at run '(strong-hammer strikes) 6.0))
    (check-= (/ (value-at run '(strong-hammer work) 6.0) strikes) (* 5 9.81 0.1) 0.15 "4.905 J a peg")
    (define turns (/ (value-at run '(strong-hammer strikes) 6.0) 4))
    (check-= (/ (value-at run '(strong-hammer work) 6.0) (* turns 2 pi)) 3.12 0.08 "mean torque of the follower on the wheel")
    ;; the weak wheel stalls on the ramp, near the angle and height the torque balance gives, and never strikes
    (check-= (final-of run '(weak-hammer strikes)) 0 0)
    (define settled (for/list ([t (times-of run)] [angle (values-of run '(weak-wheel angle))] #:when (> t 2.0)) angle))
    (check-true (for/and ([a settled]) (< 6.0 a 9.0)) (format "stalled near 7.7 degrees: ~a" settled))
    (define heights (for/list ([t (times-of run)] [h (values-of run '(weak-hammer height))] #:when (> t 2.0)) h))
    (check-true (for/and ([h heights]) (< 0.4 h 1.0)) (format "hammer near 0.70 cm: ~a" heights))
    (define torques (for/list ([t (times-of run)] [q (values-of run '(weak-hammer torque))] #:when (> t 2.0)) q))
    (check-true (for/and ([q torques]) (< 4.4 q 5.4)) (format "the follower asks about what the wheel gives, 5 N.m: ~a" torques))))

;; ---------------------------------------------------------------------------
;; Mars time and weather (issue #69). Working in racket/machines/mars-sols.rkt.

(test-case "Three sols at Meridiani: 88,775 s sols, air from -80 to -20 °C, a storm's e^(-10.5 AM), a mirror dusted to e^(-0.5)"
  (define run (simulate 'mars-sols #:seconds 270000 #:step 1 #:sample-dt 60 #:set '((mirror dust 0 180000))))
  (define (at t k) (for/first ([f run] #:when (>= (car f) (- t 1e-6))) (cadr (assq k (cdr f)))))
  (define (first-when k v) (for/first ([f run] #:when (= v (cadr (assq k (cdr f))))) (car f)))
  ;; a sol is 88,775 s: noon comes round again then (sampled 25 s later, 25/3699 of an hour on)
  (check-= (at 88775 'scene.time) (+ 12 (/ 25 3698.958)) 1e-6)
  (check-= (first-when 'scene.sol 2) 44387.5 60 "12 local hours after noon on sol 1")
  (check-= (first-when 'scene.sol 3) 133162.5 60)
  ;; the air on the daily curve
  (check-= (min-of run '(scene ambient)) -80 0.01)
  (check-= (max-of run '(scene ambient)) -20 0.01)
  (check-= (at (* 3 3698.958) 'scene.ambient) -20 0.05 "15:00 on sol 1, the warmest")
  ;; noon on sol 2, in the storm, and on sol 3, clear: S 0.741^AM e^(-(τ - 0.3) AM)
  (define (clear am) (* 586.2 (expt 0.741 am)))
  (define am2 (at 88800 'scene.air-mass))
  (check-= (at 88800 'scene.storm) 1 0)
  (check-= (at 88800 'scene.irradiance) (* (clear am2) (exp (* -10.5 am2))) 1e-4)
  (check-= (at 177600 'scene.irradiance) (clear (at 177600 'scene.air-mass)) 1e-6)
  ;; the mirror after the storm, then cleaned
  (check-= (at 177600 'mirror.dust) (- 1 (exp -0.5)) 1e-9)
  (define dusty (at 177600 'mirror.power))
  (check-= (at 180060 'mirror.dust) 0 0)
  (check-true (> (/ (at 180060 'mirror.power) dusty) 1.6) "cleaned, it throws 1/0.607 as much (less the sun's movement in 41 min)")
  ;; the relay passes at 03:00 local, for ten minutes
  (check-= (at (+ 44387.5 (* 3.05 3698.958)) 'scene.relay) 1 0)
  (check-= (at (+ 44387.5 (* 3.25 3698.958)) 'scene.relay) 0 0))
;; ---------------------------------------------------------------------------
;; Terrain as a map (issue #37)

(test-case "Ground (#37): a boulder dropped on the map's hillside rests on the height map, 0.2504 m above the ground under it"
  (when (godot-available?)
    ;; boulder.rkt: 0.25 / cos(atan 0.06); the ground is flood-plain.rkt's hill, 3 - 0.06 (x + 10) + 0.004 z^2 (no hollow there)
    (define rock (hash-ref (godot-simulate-world 'ground-check #:seconds 6 #:sample-dt 1) 'rock))
    (define-values (x y z) (values (final-of rock '(stone x)) (final-of rock '(stone y)) (final-of rock '(stone z))))
    (check-= (final-of rock '(stone speed)) 0 0.01 "at rest")
    (check-= (- y (+ 3 (* -0.06 (+ x 10)) (* 0.004 z z))) 0.2504 0.01)))

(test-case "Ground (#37): the hillside pond floods the plain; every litre is on the ledger and the water reaches the valley's middle"
  (when (godot-available?)
    ;; hillside-pond.rkt: the clock trips at 10.5 s; 8 m3 all told
    (define world (godot-simulate-world 'flood-plain #:seconds 90 #:sample-dt 5))
    (define-values (pond ground) (values (hash-ref world 'pond) (hash-ref world 'links)))
    (check-= (for/first ([f pond] #:when (= 1 (cadr (assq 'let-go.fired (cdr f))))) (car f)) 15 5.1 "the clock trips between 10 and 15 s (sampled every 5)")
    (for ([p pond] [g ground])
      (define (v f k) (cadr (assq k (cdr f))))
      (check-= (+ (v p 'pond.water) (v p 'clock.water) (v p 'race.stored) (* 1000 (v g 'map.poured))) 8000 1e-3
               (format "at ~a s pond + clock + race + ground = 8000 L" (car p)))
      (check-= (v g 'map.poured) (+ (v g 'map.volume) (v g 'map.infiltrated) (v g 'map.leaked)) 1e-8
               (format "at ~a s the ground's ledger closes" (car g))))
    (check-true (> (value-at ground '(map depth-at) 90) 1) "water standing at the plain's middle, (20, 0), by 90 s")))

(test-case "Ratchet windlass: a pawl holds 20 kg on a 10 cm drum with m g r / R, a crank steps it a tooth at a time, and without a pawl it runs away"
  ;; 20.02 kg on a 10 cm drum: 19.64 N.m; the pawl on a 15 cm circle carries m g r / R = 130.9 N. A crank at
  ;; 6 rpm winds 0.628 rad/s, 6.28 cm/s of rope, 30 degrees a tooth of 12 (a tooth every 0.83 s)
  (when (godot-available?)
    (define run (godot-simulate 'ratchet-windlass #:seconds 10 #:sample-dt 0.25 #:set '((lower-pawl pawl 0 2))))   ; as it was before the demo operator (#157)
    ;; holds: the load stays put, within a centimetre, for the whole run
    (check-true (for/and ([y (values-of run '(hold-load y))]) (< (abs (- y 1.0)) 0.012)) "the held load sags no more than a centimetre")
    (define forces (for/list ([t (times-of run)] [f (values-of run '(hold-pawl force))] #:when (> t 1.0)) f))
    (define mean-force (/ (apply + forces) (length forces)))
    (check-= mean-force (/ (* 20.02 9.81 0.10) 0.15) 14.0 (format "the pawl carries m g r / R, 130.9 N: mean ~a" mean-force))
    (check-= (final-of run '(hold-pawl steps)) 0 0 "a held wheel advances no tooth")
    ;; without a pawl the load runs away: the floor, 90 cm down, in about half a second
    (check-true (< (value-at run '(free-load y) 1.5) 0.3) "with no ratchet the load falls to the floor")
    ;; cranked: winds at r w = 6.28 cm/s, a tooth every 30 degrees of the wheel
    (check-= (value-at run '(wind-pawl angle) 5.0) 180 2.0 "6 rpm: 180 degrees in 5 s")
    (check-= (value-at run '(wind-load y) 5.0) (+ 1.0 (* 0.1 pi)) 0.03 "the rope wound in r x theta = 0.314 m")
    (for ([t (in-list '(1.0 2.0 3.0 4.0 5.0 7.0 10.0))])
      (define angle (value-at run '(wind-pawl angle) t))
      (check-= (value-at run '(wind-pawl steps) t) (floor (/ angle 30)) 0 (format "steps at ~a s: a tooth every 30 degrees (angle ~a)" t angle)))
    (check-= (value-at run '(wind-pawl steps) 10.0) 11 1 "eleven or twelve teeth in 10 s (360 degrees)")
    (check-= (final-of run '(wind-pawl pitch)) 30 1e-9 "360/12")))


;; ---------------------------------------------------------------------------
;; Glassmaking (issue #56). Working in racket/machines/solar-furnace.rkt.

(test-case "A solar furnace: flat heliostats stop short of basalt's melting point; burning mirrors melt basalt in 4,895 s and silica in 930 s"
  (define run (simulate 'solar-furnace #:seconds 6000 #:step 0.05 #:sample-dt 10 #:set '((scene clock-rate 0))))
  (define σ 5.670374e-8)
  (define air (+ 273.15 -63))
  (define (stag flux) (- (expt (+ (/ flux σ) (expt air 4)) 1/4) 273.15))
  ;; the sun at noon through Mars's clear sky
  (define am (final-of run '(scene air-mass)))
  (define dni (* 586.2 (expt 0.741 am)))
  (check-= (final-of run '(scene irradiance)) dni 1e-9)
  ;; flat: ten 1 m² heliostats on a 1 m² spot, C·I the sum of their DNI 0.85 cos(θ/2)
  (define flat-flux (for/sum ([i (in-range 1 11)]) (* dni 0.85 (final-of run (list (string->symbol (format "h~a" i)) 'cosine)))))
  (check-= (final-of run '(flat flux)) flat-flux 1e-6)
  (check-= (final-of run '(flat temperature)) (stag flat-flux) 0.5 "it settles where it re-radiates all it gets")
  (check-true (< (final-of run '(flat temperature)) 1200) "flat mirrors never melt basalt")
  (check-= (final-of run '(flat melted)) 0 0)
  ;; focused: 4,413 W on 50 cm² of 10 kg of basalt
  (check-= (final-of run '(focused flux)) (/ (* dni 12 0.85) 0.005) 1e-3)
  (check-= (final-of run '(focused stagnation)) 1713.2 0.2)
  (check-= (final-of run '(focused melt-time)) 4895 10 "16.63 MJ in, less what the hot face radiates")
  (check-= (final-of run '(focused melted)) 10 1e-9)
  (check-= (final-of run '(focused transmittance)) 0.05 0 "dark basalt glass")
  ;; clear: 2 kg of silica
  (check-= (final-of run '(clear melt-time)) 930 5)
  (check-= (final-of run '(clear transmittance)) 0.9 0 "clear silica glass"))
;; ---------------------------------------------------------------------------
;; Digging (issue #44)

(test-case "Digging (#44): the gang's unshored trench in stiff clay falls in at 3.25 m, past the clay's 3.19 m, after 13 m3 at 48.7 kJ/m3"
  (when (godot-available?)
    ;; trench-crew.rkt: 4c/g tan(45 + phi/2) = 3.193 m; W/V = c + g z (1 + tan phi), z averaging 1.625 m
    (define world (godot-simulate-world 'trench #:seconds 240 #:sample-dt 10))
    (define crew (hash-ref world 'crew))
    (check-equal? (final-of crew '(gang collapsed)) 1)
    (check-= (final-of crew '(gang collapse-depth)) 3.25 1e-9)
    (check-= (final-of crew '(gang dug)) 13 1e-6)
    (check-= (final-of crew '(gang specific-work)) 48.74 0.05)
    (check-true (< 200 (for/first ([f crew] #:when (= 1 (cadr (assq 'gang.collapsed (cdr f))))) (car f)) 230)
                "it fell in about 211 s in (3 kW into 634 kJ)")))


(test-case "Sand timer: grain drains at Beverloo's steady rate, its level falls linearly, water's as a square root, and Mars runs 1.63 times slower"
  ;; 5 kg of 1600 kg/m3 sand, 0.3 mm grains, 10 mm orifice: W = 0.58 x 1600 x sqrt(9.81) x (10 - 0.45 mm)^2.5 = 25.9 g/s,
  ;; empty in 193.0 s, the level falling 1.619 mm/s from 31.25 cm; the water tank, sized to empty in the same time,
  ;; has (sqrt h0 - k t)^2: a quarter left at half time where the sand has half; Mars: sqrt(3.71/9.81) = 0.615 of the flow
  (define run (simulate 'sand-timer #:seconds 200 #:step 0.05 #:sample-dt 8))
  (define (at r t k) (for/first ([f r] #:when (>= (car f) (- t 1e-6))) (cadr (assq k (cdr f)))))
  (define w 0.58)
  (define expected-flow (* 1000 w 1600 (sqrt 9.81) (expt (- 0.010 (* 1.5 0.0003)) 2.5)))    ; g/s
  (check-= (at run 0 'sand.flow) expected-flow 1e-9)
  (check-= (at run 0 'sand.flow) 25.9 0.05 "25.9 g/s")
  ;; steady: the same flow when full, half full and nearly empty
  (for ([t (in-list '(0 40 96 160 184))])
    (check-= (at run t 'sand.flow) expected-flow 1e-9 (format "flow at ~a s" t)))
  ;; the level falls linearly: equal steps in equal times
  (define levels (for/list ([t (in-range 0 185 8)]) (at run t 'sand.level)))
  (define drops (for/list ([a levels] [b (cdr levels)]) (- a b)))
  (check-true (for/and ([d drops]) (< (abs (- d (car drops))) 1e-9)) "a fixed drop every 8 s")
  (check-= (/ (car drops) 8) 0.1619 0.0005 "1.619 mm/s")
  ;; empty in M / W = 193.0 s
  (check-= (at run 0 'sand.mass) 5 0)
  (check-= (at run 192 'sand.empty) 0 0 "not quite empty at 192 s")
  (check-= (at run 200 'sand.empty) 1 0 "empty by 200 s")
  (check-= (at run 200 'sand.drained) 5 1e-9)
  ;; against the water clock: the same time to empty, but at half time 50% against 25%
  (check-= (/ (at run 96 'sand.level) (at run 0 'sand.level)) 0.5 0.01 "sand: half left at half time")
  (check-= (/ (at run 96 'water.level) (at run 0 'water.level)) 0.25 0.01 "water: a quarter left at half time")
  ;; arching: 8 mm through 2 mm grains is 4 grains across, under 5: nothing comes out
  (check-= (at run 96 'arch.arched) 1 0)
  (check-= (at run 200 'arch.flow) 0 0)
  (check-= (at run 200 'arch.mass) 5 0)
  ;; Mars: the flow goes as sqrt(g)
  (define mars (simulate 'sand-timer #:seconds 330 #:step 0.05 #:sample-dt 10 #:set '((scene gravity 3.71))))
  (check-= (/ (at mars 10 'sand.flow) (at run 10 'sand.flow)) (sqrt (/ 3.71 9.81)) 1e-9 "0.615 of the flow")
  (check-= (at mars 10 'sand.flow) 15.93 0.05 "15.9 g/s")
  (check-= (at mars 300 'sand.empty) 0 0 "still running at 300 s")
  (check-= (at mars 320 'sand.empty) 1 0 "empty by 314 s: 1.63 times as long")
  (check-= (/ 313.9 193.0) (sqrt (/ 9.81 3.71)) 0.005 "sqrt(9.81 / 3.71) = 1.626"))


;; ---------------------------------------------------------------------------
;; Glass walls (issue #57). Working in racket/machines/glass-rooms.rkt.

(test-case "Glass walls: a membrane lets in no sun, a glazed roof τ I A, settling at T_out + τ I A/UA; a thin pane cracks at 0.29 q (a/t)^2 = 7 MPa"
  (define run (simulate 'glass-rooms #:seconds 3600 #:step 0.05 #:sample-dt 60 #:set '((scene clock-rate 0))))
  (define roof (* (final-of run '(scene irradiance)) (sin (* (final-of run '(scene sun-elevation)) (/ pi 180)))))
  (check-= roof 427.0 0.05 "432.7 W/m² of beam at 80.7° up")
  (check-= (final-of run '(glazed-roof gain)) (* 0.9 roof 3.24) 1e-6)
  (check-= (final-of run '(dark-roof gain)) (* 0.05 roof 3.24) 1e-6)
  (check-= (final-of run '(membrane temperature)) -63 0.01 "no glass, no sun: it cools to the outside")
  (check-= (final-of run '(glazed temperature)) (+ -63 (/ (* 0.9 roof 3.24) 20)) 0.01 "-0.74 °C")
  (check-= (final-of run '(dark temperature)) (+ -63 (/ (* 0.05 roof 3.24) 20)) 0.01 "-59.5 °C")
  (check-= (final-of run '(glazed-roof crack-pressure)) (/ (* 7e6 (expt (/ 0.0075 0.3) 2)) 0.29 1000) 1e-9 "15.09 kPa")
  (check-= (final-of run '(glazed-roof cracked)) 0 0 "12.89 kPa across it at most: it holds")
  (check-= (final-of run '(thin-roof crack-pressure)) 6.705 0.001)
  (check-= (final-of run '(thin-roof crack-time)) 0.05 1e-9 "12.89 kPa across it from the first step")
  (check-= (final-of run '(thin pressure)) 0.61 1e-6 "and the room's air is gone"))
;; ---------------------------------------------------------------------------
;; Buoyancy and water drag (issue #29)

(test-case "Floats (#29): the cork float rides 5 cm under the draining cistern's surface and grounds at 307.7 s"
  ;; floats.rkt: draft m / (rho A) = 5 cm; Torricelli to 5 cm deep at 307.7 s
  (define run (simulate 'floats #:seconds 330 #:step 0.01 #:sample-dt 1))
  (check-= (final-of run '(bob draft)) 5 1e-9)
  (for ([f run] #:when (< (car f) 300))
    (define (v k) (cadr (assq k (cdr f))))
    (check-= (v 'bob.height) (- (/ (v 'cistern.level) 100) 0.05) 1e-9 (format "riding the surface at ~a s" (car f))))
  (check-= (for/first ([f run] #:when (= 1 (cadr (assq 'bob.grounded (cdr f))))) (car f)) 308 1.01))

(test-case "Floats (#29): blocks in the bath float with rho_block / rho_water of them under; iron sinks"
  (when (godot-available?)
    ;; floats.rkt: 20 cm blocks in 40 cm of water; centres at 0.4 - (rho/1000) 0.2 + 0.1
    (define run (godot-simulate 'floats #:seconds 40 #:sample-dt 0.1))
    (define (mean path)
      (define key (string->symbol (format "~a.~a" (car path) (cadr path))))
      (define vs (for/list ([f run] #:when (>= (car f) 20)) (cadr (assq key (cdr f)))))
      (/ (apply + vs) (length vs)))
    (check-= (mean '(cedar-block y)) 0.424 0.001)
    (check-= (mean '(pine-block y)) 0.400 0.001)
    (check-= (mean '(oak-block y)) 0.356 0.001)
    (check-= (mean '(iron-block y)) 0.100 0.001)))

;; ---------------------------------------------------------------------------
;; Air buoyancy: the Kongming sky lantern (#111, #125)

(test-case "Kongming lantern (#111, #125): it leaves the ground when the air inside passes 32.46 C, 33.40 s into the burn, and climbs where drag balances lift less weight"
  (when (godot-available?)
    ;; kongming-lantern.rkt's working, done before running: rho_out = 101325 / (287.05 x 288.15)
    ;; = 1.2250; Delta rho x 1 m3 = 0.070 kg at rho_in = 1.1550, T = 305.61 K = 32.46 C. The heating
    ;; C(T) dT/dt = 800 - 15 (T - 15 C), C = rho_in V 1005 + 0.05 x 1400, integrated by RK4 at 1 ms
    ;; with the body (drag 1/2 rho 0.8 (V/h = 0.833 m2) v^2 on its 1.2 m height) gives:
    ;;   lift passes weight at 33.40 s; 2.007 m off the ground at 40 s; 4.887 m at 45 s, climbing 0.649 m/s.
    ;; Its centre rests at h / 2 = 0.6 m.
    (define run (godot-simulate 'kongming-lantern #:seconds 50 #:sample-dt 0.1 #:set '((mooring tether 0))))   ; unmoored from the start (#155)
    (define (field f path)
      (define key (string->symbol (format "~a.~a" (car path) (cadr path))))
      (cadr (assq key (cdr f))))
    (define (lifted? f) (>= (field f '(lantern lift)) (field f '(lantern weight))))
    (define lift-off (for/first ([f run] #:when (lifted? f)) f))
    (check-= (car lift-off) 33.40 0.15 "the lift passes the weight when the sim says it does")
    (check-= (field lift-off '(lantern temperature)) 32.463 0.15 "at the temperature the ideal gas law gives")
    (check-= (field lift-off '(lantern lift-off-temperature)) 32.463 0.001)
    (check-= (field lift-off '(lantern weight)) (* 0.070 9.81) 1e-9)
    ;; until then it sits on the ground, after it it climbs
    (check-= (value-at run '(lantern y) 33.0) 0.6 0.002 "still on the ground the second before")
    (check-true (< (value-at run '(lantern y) 33.0) (value-at run '(lantern y) 34.5)) "off it just after")
    (check-= (- (value-at run '(lantern y) 40) 0.6) 2.007 0.06)
    (check-= (- (value-at run '(lantern y) 45) 0.6) 4.887 0.1)
    (check-= (value-at run '(lantern vy) 45) 0.649 0.03 "the climb speed at 37.2 C inside, where drag balances lift less weight")
    ;; the same lantern with no flame stays cold and on the ground
    (check-= (value-at run '(control y) 50) 0.6 0.002)
    (check-= (value-at run '(control temperature) 50) 15 1e-9)))

(test-case "Kongming lantern on Mars (#111, #125): 0.0152 kg/m3 of air can never lift 70 g from a cubic metre"
  (when (godot-available?)
    ;; rho_out g V = 0.0152 x 3.71 = 0.0563 N, against a weight of 0.070 x 3.71 = 0.2597 N, whatever the heat;
    ;; its skin settles at -63 + 800 / 15 = -9.67 C (263.48 K), where the lift is
    ;; rho_out (1 - 210.15 / 263.48) g V = 0.0152 x 0.2024 x 3.71 = 0.0114 N
    (define run (godot-simulate 'kongming-lantern-mars #:seconds 120 #:sample-dt 1))
    (define (all path) (for/list ([f run]) (cadr (assq (string->symbol (format "~a.~a" (car path) (cadr path))) (cdr f)))))
    (check-true (for/and ([y (all '(lantern y))]) (< (abs (- y 0.6)) 0.002)) "it never leaves the ground")
    (check-true (for/and ([l (all '(lantern lift))] [w (all '(lantern weight))]) (< l w)))
    (check-= (final-of run '(lantern weight)) (* 0.070 3.71) 1e-9)
    (check-= (final-of run '(lantern temperature)) (+ -63 (/ 800 15.0)) 0.01)
    (check-true (< (final-of run '(lantern lift-limit)) (final-of run '(lantern weight))))))

;; ---------------------------------------------------------------------------
;; Chains of pinned links (issue #31)

(test-case "A chain of 40 pinned links (#31) hangs on the catenary for its length and span; the hooks carry w a cosh(S/2a)"
  (when (godot-available?)
    ;; hanging-chain.rkt: S 2 m, L 2.5 m, 2a sinh(S/2a) = L; w = 7700 g pi (0.005)^2
    (define a (let loop ([lo 0.01] [hi 100.0] [i 0])
                (define m (/ (+ lo hi) 2))
                (cond [(> i 200) m]
                      [(> (* 2 m (sinh (/ 1 m))) 2.5) (loop m hi (add1 i))]
                      [else (loop lo m (add1 i))])))
    (define w (* 7700 9.81 pi 0.005 0.005))
    (define run (godot-simulate 'hanging-chain #:seconds 20 #:sample-dt 1))
    (define f (cdr (last run)))
    (define (v k i) (cadr (assq (string->symbol (format "chain-~a.~a" i k)) f)))
    (for ([i 40])
      (define x (v 'x i))
      (check-= (v 'y i) (- 2 (* a (- (cosh (/ 1 a)) (cosh (/ x a))))) 0.002 (format "link ~a on the catenary" i)))
    (check-= (min (v 'y 19) (v 'y 20)) (- 2 (* a (- (cosh (/ 1 a)) 1))) 0.002 "the lowest point: the sag a (cosh(S/2a) - 1)")
    ;; the pull at the hook, from the first link's slope there: its vertical part is half the chain's weight
    (define slope (/ (- 2 (v 'y 0)) (- (v 'x 0) -1)))
    (define theta (atan slope))
    (check-= (/ (* w 2.5 1/2) (sin theta)) (* w a (cosh (/ 1 a))) (* 0.02 (* w a (cosh (/ 1 a)))) "tension at the hook")))

(test-case "Sleep until: a filling cistern wakes at volume/rate within a step, the estimate agrees, and the sleep leaves the state real time would have"
  ;; a 500 L cistern, a spring of 2 L/s: 50 L takes 25 s, 20 and 40 L (and) 20 s, (or) 10 s; a condition that cannot be met stops at its limit;
  ;; an event wakes it early. The sleep steps at the machine's own 1/120 s, never bigger.
  (define step 1/120)
  (define filled (sleep-until 'wake-clock 'filled #:step step))
  (check-eq? (hash-ref filled 'reason) 'condition)
  (check-= (hash-ref filled 'elapsed) 25.0 (+ step 1e-9) "50 L at 2 L/s: 25 s, within one step")
  (check-= (hash-ref filled 'predicted) 25.0 0.01 "the estimate from the rate of change")
  (check-= (hash-ref filled 'steps) 3001 1 "one 1/120 s step at a time")
  ;; the state the sleep leaves is the state that running in real time gives
  (define slept-seconds (* (hash-ref filled 'steps) step))
  (define watched (simulate 'wake-clock #:seconds slept-seconds #:step step #:sample-dt slept-seconds))   ; a frame at the start and one at the end
  (define (frame-value frame key) (cadr (assq key (cdr frame))))
  (define woke (hash-ref filled 'frame))
  (check-= (car woke) (car (last watched)) 1e-9 "the same time")
  (for ([key '(cistern.water cistern.level spring.flow)])
    (check-= (frame-value woke key) (frame-value (last watched) key) 1e-9 (format "~a is what watching gives" key)))
  (check-true (>= (frame-value woke 'cistern.water) 50) "it woke with the condition met")
  ;; and, or, an event
  (check-= (hash-ref (sleep-until 'wake-clock 'both #:step step) 'elapsed) 20.0 0.02 "and: the later of 20 L and 40 L: at 40 L, 20 s")
  (check-= (hash-ref (sleep-until 'wake-clock 'either #:step step) 'elapsed) 10.0 0.02 "or: the earlier: at 20 L, 10 s")
  (define guarded (sleep-until 'wake-clock 'guarded #:step step))
  (check-eq? (hash-ref guarded 'reason) 'event)
  (check-= (hash-ref guarded 'elapsed) 15.0 0.02 "woken early at 30 L")
  (check-true (regexp-match? #rx"cistern.water" (hash-ref guarded 'detail)) "and it says what woke it")
  ;; a condition that cannot be met: the estimate says so, and it stops at the limit
  (define never (sleep-until 'wake-clock 'never #:step step))
  (check-eq? (hash-ref never 'reason) 'limit)
  (check-= (hash-ref never 'elapsed) 60.0 0.02 "stopped at its 60 s limit")
  (check-= (hash-ref never 'predicted) 2500.0 1.0 "5000 L at 2 L/s: 2500 s at the current rate")
  (check-true (regexp-match? #rx"beyond the 60" (hash-ref never 'note)) "the estimate said it would not make it")
  ;; a condition of your own, not in the file
  (define adhoc (sleep-until 'wake-clock '((cistern water above 100)) #:step step))
  (check-= (hash-ref adhoc 'elapsed) 50.0 0.02 "100 L at 2 L/s")
  ;; already met: wakes at once
  (check-= (hash-ref (sleep-until 'wake-clock '((cistern water below 1))) 'elapsed) 0.0 0 "it is already empty"))


;; ---------------------------------------------------------------------------
;; Evaporation and condensation (issue #58). Working in racket/machines/rain-house.rkt.

(test-case "A rain-house: 1 kW through a 40 W/K roof rains 1.595 kg/h at a -38 °C dew point, 39.3 kg a sol into a gutter 4.6 m up"
  (define run (simulate 'rain-house #:seconds 88800 #:step 0.1 #:sample-dt 600))
  (define (at t k) (for/first ([f run] #:when (>= (car f) (- t 1e-6))) (cadr (assq k (cdr f)))))
  (define rate (/ 1000 2.257e6))                     ; kg/s a kilowatt condenses
  (check-= (final-of run '(lid rain)) (* rate 3600) 0.002 "1.595 kg an hour")
  (check-= (final-of run '(lid heat)) 1000 1.5 "the heat leaving through the roof is the heater's")
  (check-= (final-of run '(lid dew-point)) -38 0.05 "-63 + 1000/40")
  (check-= (final-of run '(warm temperature)) 50.05 0.02 "p_sat(T_w) = 21.9 + 4.431e-4/3.6e-8 Pa")
  ;; a sol's rain in the gutter, and the water's energy lifted 4.6 m under Mars's gravity
  (define sol-rain (* rate 88800))
  ;; water's getters are in litres: a litre of it a kilogram
  (check-= (final-of run '(gutter water)) sol-rain 0.1 "39.3 kg")
  (check-= (* (final-of run '(gutter water)) 3.71 4.6) (* sol-rain 3.71 4.6) 2 "m g h, 671 J")
  ;; every gram: what the pond lost is in the gutter or the air
  (define vapour-kg (* (/ (* (final-of run '(house h2o-pressure)) 1000 80) (* 8.314 293.15)) 0.018015))
  (check-= (- 200 (final-of run '(basin water))) (+ (final-of run '(gutter water)) vapour-kg) 1e-6))
;; ---------------------------------------------------------------------------
;; Sediment transport (issue #53)

(test-case "Sluice box (#53): at the race's steady flow it keeps grains denser than 7340 kg/m3: the gold, not the sand"
  ;; placer-sluice.rkt: tau = rho g R S at the Manning depth for 2 L/s = 1.46 Pa; cutoff 1000 + tau / (0.047 g d)
  (define run (simulate 'placer-sluice #:seconds 360 #:step 0.01 #:sample-dt 60))
  (check-= (value-at run '(race flow) 300) 2 0.01)
  (check-= (value-at run '(riffles shear) 300) 1.46 0.01)
  (check-= (value-at run '(riffles cutoff) 300) 7340 10)
  (define (gain k) (- (final-of run (list 'riffles k)) (value-at run (list 'riffles k) 300)))
  (check-= (gain 'kept-heavy) (* 0.02 0.1 60) 1e-6 "every gram of gold fed in the last minute kept")
  (check-= (gain 'kept-light) 0 1e-9 "no sand kept at the steady flow")
  (check-= (gain 'passed-heavy) 0 1e-9))

(test-case "Hushing (#53): the pond let go on the sand slope moves sand, and none of it is lost from the ledger"
  (when (godot-available?)
    (define world (godot-simulate-world 'hushing #:seconds 60 #:sample-dt 10))
    (define g (hash-ref world 'links))
    (check-true (> (final-of g '(map poured)) 1) "the pond ran out onto the slope")
    (check-true (> (final-of g '(map bed-moved)) 0.01) "and moved sand")))

(test-case "Loose balls: a solid ball rolls down a slope at (5/7) g sin(theta), does not slip, and arrives at sqrt(10 g h / 7)"
  ;; 15 degrees: a = 0.7143 x 9.81 x 0.2588 = 1.8136 m/s2 (a sliding block: 2.539). 1.5 m along the slope: t = sqrt(2 s / a) = 1.286 s,
  ;; v = 2.332 m/s = sqrt(10 g h / 7), h = 1.5 sin 15 = 0.388 m. The 12 cm iron ball and the 6 cm bronze one keep pace: neither
  ;; size nor mass enters. w = v / r throughout (rolling without slipping); v^2 = (10/7) g (y0 - y) at every point.
  (when (godot-available?)
    (define run (godot-simulate 'ball-ramp #:seconds 1.6 #:sample-dt 0.02))
    (define a-rolling (* 5/7 9.81 (sin (* 15 (/ pi 180)))))
    (check-= a-rolling 1.8136 1e-3 "the prediction, worked out")
    (define (at t k) (value-at run k t))
    ;; the acceleration, from the speed after 0.5 s and 1 s
    (check-= (/ (at 0.5 '(iron-ball speed)) 0.5) a-rolling 0.05 "1.81 m/s2 after 0.5 s")
    (check-= (/ (at 1.0 '(iron-ball speed)) 1.0) a-rolling 0.05 "and after 1 s")
    (check-true (< (/ (at 1.0 '(iron-ball speed)) 1.0) (* 0.75 9.81 (sin (* 15 (/ pi 180))))) "far from the g sin(theta) of a sliding block")
    ;; the big and the small ball run together
    (for ([t (in-list '(0.3 0.5 0.7))])
      (check-= (at t '(iron-ball speed)) (at t '(bronze-ball speed)) (* 0.01 (at t '(iron-ball speed))) (format "same speed at ~a s, whatever the size and mass" t)))
    ;; rolling without slipping: omega r = v
    (for ([t (in-list '(0.3 0.5 0.7 1.0 1.2))])
      (check-= (* 0.06 (at t '(iron-ball omega))) (at t '(iron-ball speed)) (* 0.01 (at t '(iron-ball speed))) (format "iron ball w r = v at ~a s" t)))
    (for ([t (in-list '(0.3 0.5 0.7))])
      (check-= (* 0.03 (at t '(bronze-ball omega))) (at t '(bronze-ball speed)) (* 0.01 (at t '(bronze-ball speed))) (format "bronze ball w r = v at ~a s" t)))
    ;; the energy law of a rolling sphere, v^2 = (10/7) g dy, and not a sliding block's 2 g dy
    (define y0 (at 0 '(iron-ball y)))
    (for ([t (in-list '(0.5 0.7 0.9 1.1 1.25))])
      (define dy (- y0 (at t '(iron-ball y))))
      (define ratio (/ (expt (at t '(iron-ball speed)) 2) (* 10/7 9.81 dy)))
      (check-true (< 0.96 ratio 1.02) (format "v^2 / ((10/7) g dy) at ~a s is ~a" t ratio))
      (check-true (< (expt (at t '(iron-ball speed)) 2) (* 0.8 2 9.81 dy)) (format "well under a sliding block's 2 g dy at ~a s" t)))
    ;; arriving at the foot of the ramp
    (define foot (for/first ([t (times-of run)] [z (values-of run '(iron-ball z))] #:when (> z -0.4)) t))
    (check-= (at foot '(iron-ball speed)) (sqrt (/ (* 10 9.81 (* 1.5 (sin (* 15 (/ pi 180))))) 7)) 0.06 "sqrt(10 g h / 7) = 2.33 m/s where it leaves the ramp")
    (check-true (< (at foot '(iron-ball speed)) (* 0.9 (sqrt (* 2 9.81 (* 1.5 (sin (* 15 (/ pi 180)))))))) "not the sqrt(2 g h) = 2.76 of a block")
    (check-= foot (sqrt (/ (* 2 1.5) a-rolling)) 0.06 "1.286 s to run 1.5 m")))
;; ---------------------------------------------------------------------------
;; Buried bodies and slides (issue #54)

(test-case "Slide (#54): the cliff comes down on the first tick and buries the crate at its foot under 0.33 m, held, 4.77 kN to pull out"
  (when (godot-available?)
    ;; cliff.rkt: a 2 m2/m wedge at repose against the new face; 0.83 m over x = 5.5, the crate's top at 0.5
    (define box (hash-ref (godot-simulate-world 'slide #:seconds 2 #:sample-dt 0.5) 'box))
    (check-= (final-of box '(crate cover)) 0.33 0.01)
    (check-= (final-of box '(crate buried)) 1 0)
    (check-= (final-of box '(crate pull-out)) 4770 60)
    (check-= (final-of box '(crate y)) 0.25 1e-6 "held where it stood")))

(test-case "Digging out (#54): the buried crate takes 14.2 kN to pull, and is freed by the spit that takes the trench to 1 m"
  (when (godot-available?)
    ;; buried-crate.rkt: m g + gamma D s^2 + 4 s (c s + K0 gamma tan phi ((D + s)^2 - D^2) / 2) = 14.2 kN under 1 m
    (define site (hash-ref (godot-simulate-world 'dig-out #:seconds 30 #:sample-dt 0.1) 'site))
    (check-= (value-at site '(crate pull-out) 0.1) 14200 100)
    (define freed (for/first ([f site] #:when (let ([b (assq 'crate.buried (cdr f))]) (and b (zero? (cadr b))) )) (car f)))
    (define third-spit (for/first ([f site] #:when (>= (cadr (assq 'gang.depth (cdr f))) 0.75)) (car f)))
    (check-true (and freed third-spit (> freed third-spit) (< freed 21))
                (format "freed at ~a s, during the fourth spit (the third was done at ~a s; the fourth, at 1 m, by 20.9 s)" freed third-spit))
    ;; freed where it lies (owner question 2026-10-10: it used to be lifted 0.5 m to stand on the trench floor): its lid flush with
    ;; the 1 m trench's floor, its base on the floor of the pit its crust and the soil beside it crumbled off from (MachineView.Burial)
    (check-= (final-of site '(crate y)) -1.25 0.01 "free where it lay, its centre still 1.25 m down, on its pit's floor")
    (check-= (final-of site '(crate buried)) 0 0 "and still free when the gang has finished: the crumbled crust lies clear of the pit's lip")))


;; ---------------------------------------------------------------------------
;; Boulders from terrain collapse (issue #88)

(test-case "Boulders (#88): the talus cliff's collapse of 7.03 m3 leaves 4 granite boulders (0.5 m3) and the ground 0.5 m3 short; they slide down the 35° debris and stop on ground under 31°, 31-35° below where they started"
  (when (godot-available?)
    ;; talus.rkt: V = (3 - 1.244) x 0.5 x 8 = 7.03 m3 (3 %); floor(0.08 V / 0.125) = 4 cubes of 0.5 m; granite on the ground holds to atan 0.6 = 31.0°
    (define g (hash-ref (godot-simulate-world 'talus #:seconds 5 #:sample-dt 1/120) 'links))
    (define (field f k) (let ([e (assq k (cdr f))]) (and e (cadr e))))
    (define start (for/first ([f g] #:when (field f 'boulder-1.x)) f))
    (define end (last g))
    (check-true (< (car start) 0.02) "the boulders come down with the cliff, on the first tick")
    (check-= (field (car g) 'map.ground-volume) 120 1e-9 "10 cells of 3 m, 16 rows, 0.25 m2 each")
    (check-= (field end 'map.collapsed) 7.03 (* 0.03 7.03))
    (check-= (field end 'map.boulders) 4 0)
    (check-= (field end 'map.boulders) (floor (/ (* 0.08 (field end 'map.collapsed)) 0.125)) 0)
    (check-= (field end 'map.boulder-volume) 0.5 1e-12)
    (check-= (- (field (car g) 'map.ground-volume) (field end 'map.ground-volume)) (field end 'map.boulder-volume) 1e-9
             "the ground lost exactly what the boulders hold")
    (for ([i (in-range 1 5)])
      (define (k s) (string->symbol (format "boulder-~a.~a" i s)))
      (define-values (x0 y0 z0 x1 y1 z1)
        (values (field start (k 'x)) (field start (k 'y)) (field start (k 'z)) (field end (k 'x)) (field end (k 'y)) (field end (k 'z))))
      (check-true (> (field start (k 'slope)) 31.0) (format "boulder ~a laid on ~a°, steeper than it holds on" i (field start (k 'slope))))
      (check-true (> (- x1 x0) 0.3) (format "boulder ~a slid down the debris, ~a m" i (- x1 x0)))
      (check-true (< (field end (k 'speed)) 0.005) (format "boulder ~a has stopped" i))
      (check-true (<= (field end (k 'slope)) 31.0) (format "boulder ~a stopped on ~a°, no steeper than atan 0.6 = 31.0°" i (field end (k 'slope))))
      (define reach (* (/ 180 pi) (atan (- y0 y1) (sqrt (+ (expt (- x1 x0) 2) (expt (- z1 z0) 2))))))
      (check-true (<= 30.5 reach 35.0)
                  (format "boulder ~a: the line from where it started to where it stopped is ~a° below level: friction's 31.0° or steeper (losses), never steeper than the 35° debris" i reach)))))

(test-case "Boulders (#88): saved while sliding and loaded, they lie where they were and come to rest as in a run never stopped"
  (when (godot-available?)
    (define dir (make-temporary-directory))
    (define file (path->string (build-path dir "talus.save")))
    (define straight (hash-ref (godot-simulate-world 'talus #:seconds 5 #:sample-dt 1) 'links))
    (godot-simulate-world 'talus #:seconds 1.05 #:sample-dt 1 #:env `(("HEROIC_SAVE" . ,file) ("HEROIC_SAVE_AT" . "1")))
    (check-true (file-exists? file) "the game wrote the save")
    (define saved (cdr (assq 'boulders (cddr (call-with-input-file file read)))))
    (check-equal? (length saved) 4 "the save holds the four boulders")
    (define resumed (hash-ref (godot-simulate-world 'talus #:seconds 5 #:sample-dt 1/120 #:env `(("HEROIC_LOAD" . ,file))) 'links))
    (define (field f k) (let ([e (assq k (cdr f))]) (and e (cadr e))))
    (define first-loaded (for/first ([f resumed] #:when (field f 'boulder-1.x)) f))
    (for ([b saved] [i (in-naturals 1)])
      (define (k s) (string->symbol (format "boulder-~a.~a" i s)))
      (define at (cdr (assq 'at (cddddr b))))
      (check-equal? (cadr b) (string->symbol (format "boulder-~a" i)))
      (check-true (> (cadr (assq 'v (cddddr b))) 0.1) (format "boulder ~a was saved sliding" i))
      ;; within one tick of sliding (under 1 cm) of where the save left it
      (for ([s '(x y z)] [v at]) (check-= (field first-loaded (k s)) v 0.01 (format "boulder ~a ~a as loaded" i s)))
      (for ([s '(x y z)]) (check-= (field (last resumed) (k s)) (field (last straight) (k s)) 0.05 (format "boulder ~a ~a at 5 s" i s)))
      (check-true (< (field (last resumed) (k 'speed)) 0.005) (format "boulder ~a has stopped" i)))
    (check-= (field (last resumed) 'map.ground-volume) 119.5 1e-9 "the ground as it was left, not collapsed again")
    (check-= (field (last resumed) 'map.boulders) 4 0 "no more boulders")
    (delete-directory/files dir)))

;; ---------------------------------------------------------------------------
;; Tanks and the ground's water (issue #90)

(test-case "Spill (#90): a broken butt on a walled slope lets its 1000 L onto the ground; butt and ground always hold 1000 L, 369.6 L in the butt at 30 s"
  (when (godot-available?)
    ;; spill-tank.rkt: sqrt(head) falls 0.013288 a second from sqrt(0.98): 349.6 L over the hole at 30 s, down to it at 74.5 s
    (define w (godot-simulate-world 'spill #:seconds 90 #:sample-dt 5))
    (define-values (barrel ground) (values (hash-ref w 'barrel) (hash-ref w 'links)))
    (for ([b barrel] [g ground])
      (check-= (+ (/ (cadr (assq 'butt.water (cdr b))) 1000) (cadr (assq 'map.volume (cdr g)))) 1.0 1e-9
               (format "butt and ground hold 1000 L at ~a s" (car b))))
    (check-= (value-at barrel '(butt water) 30) 369.6 1.5)
    (check-= (value-at ground '(map volume) 30) 0.6304 0.0015)
    (check-= (final-of barrel '(butt water)) 20 0.01 "down to the hole, 2 cm up")
    (check-= (final-of ground '(map spilled)) 0.98 1e-4)
    (check-= (final-of ground '(map poured)) (final-of ground '(map volume)) 1e-9 "none soaked in, none ran off: the walls held it")))

(test-case "Drain (#90): a grate at the bottom of a hollow fills its cistern at the spring's 2 L/s, the water standing 2.05 cm over it"
  (when (godot-available?)
    ;; cistern-drain.rkt: steady, Q = 1.705 x 0.4 x h^1.5 = 2 L/s, h = (0.002 / 0.682)^(2/3) = 2.048 cm
    (define w (godot-simulate-world 'sump #:seconds 180 #:sample-dt 10))
    (define-values (yard ground) (values (hash-ref w 'yard) (hash-ref w 'links)))
    (for ([y yard] [g ground])
      (check-= (+ (/ (cadr (assq 'cistern.water (cdr y))) 1000) (cadr (assq 'map.volume (cdr g)))) (cadr (assq 'map.poured (cdr g))) 1e-9
               (format "cistern and ground hold what the spring gave at ~a s" (car y))))
    (check-= (final-of ground '(map poured)) 0.36 1e-6 "180 s of 2 L/s")
    (define rate (/ (- (final-of yard '(cistern water)) (value-at yard '(cistern water) 150)) 30))
    (check-= rate 2.0 0.02 (format "the cistern fills at ~a L/s" rate))
    (check-= (final-of yard '(grate flow)) 2.0 0.02)
    (check-= (final-of yard '(grate depth)) (* 100 (expt (/ 0.002 (* 1.705 0.4)) 2/3)) 0.04)
    (check-= (final-of yard '(grate drained)) (final-of yard '(cistern water)) 1e-9)))

;; ---------------------------------------------------------------------------
;; The greenhouse (issue #42). Working in racket/machines/greenhouse.rkt.

(test-case "A greenhouse on Mars: trees grow 3.181e-7 kg/s of wood, the air's O2 rising 1.1841 kg a kg; burned, the harvest gives it all back"
  (define T30 2663250)                                 ; 30 sols
  (define E 2685600)                                   ; a frame after the burn, 746 h in
  (define run (simulate 'greenhouse #:seconds E #:step 5 #:sample-dt 3600
                        #:set `((scene clock-rate 0) (trees harvest 1000 ,T30))))
  (define (at t k) (for/first ([f run] #:when (>= (car f) (- t 1e-6))) (cadr (assq k (cdr f)))))
  (define (kg gas molar t) (* (/ (* (at t (string->symbol (format "house.~a-pressure" gas))) 1000 30) (* 8.314 (+ 273.15 (at t 'house.temperature)))) molar))
  ;; the greenhouse holds about 1.1 kg of O2 to start with
  (check-= (kg 'o2 0.031998 0) 1.117 0.001)
  ;; the light through the glass, and the wood it grows
  (check-= (at 36000 'trees.light) (* 0.9 3.24 (at 36000 'scene.irradiance) (sin (* (at 36000 'scene.sun-elevation) (/ pi 180)))) 1e-6)
  (check-= (at 36000 'house.temperature) (+ -63 (/ (+ 3000 (at 36000 'trees.light)) 50)) 0.01 "held at 21.9 °C")
  (define net (- (/ (* 0.005 (at 36000 'trees.light)) 18e6) (* 10 (/ 0.01e-3 3600))))
  (check-= net 3.181e-7 1e-10)
  (check-= (at (- T30 3600) 'trees.wood) (* net (- T30 3600)) 0.001 "0.846 kg after 30 sols, less an hour")
  (check-= (at (- T30 3600) 'trees.oxygen) (* (/ (* 6 0.031998) 0.16214) (at (- T30 3600) 'trees.wood)) 1e-9 "cellulose: 1.1841 kg of O2 a kg")
  (check-true (> (kg 'o2 0.031998 (- T30 3600)) (* 1.8 (kg 'o2 0.031998 0))) "the greenhouse's oxygen nearly doubles")
  ;; the harvest burned: 15 MJ/kg at 1 kW, and the air as it was but for the wood grown since
  (check-= (at E 'stove.fuel) 0 1e-9 "burned out")
  (define standing (at E 'trees.wood))
  (check-= (- (kg 'o2 0.031998 E) (kg 'o2 0.031998 0)) (* (/ (* 6 0.031998) 0.16214) standing) 1e-6)
  (check-= (- (kg 'co2 0.04401 E) (kg 'co2 0.04401 0)) (* (- (/ (* 6 0.04401) 0.16214)) standing) 1e-6)
  ;; the melter and the electrolyser
  (check-= (at 3600 'drill.rate) (/ 3600000 466300) 1e-6 "7.72 kg/h")
  (check-= (at 3600 'drill.heat-per-kg) 466.3 1e-9)
  (check-= (at 3600 'splitter.rate) (/ (* 350 3600 1000) 17.875e6) 0.01 "70.5 g/h of O2")
  (check-= (/ (at 3600 'splitter.energy) (at 3600 'splitter.oxygen)) (/ 17.875 0.7) 0.01 "25.5 MJ a kg of O2"))


(test-case "Save and load: a machine saved half way and resumed carries on exactly as if it had never stopped"
  ;; every field agrees, after another 30 s, with a run that was never interrupted: the save holds the running state
  ;; (water, heat, grain, clocks), and loading lays it back on a machine rebuilt from its own file
  (for ([name '(sand-timer wake-clock herons-fountain water-clock hama-noria sluice-demo)])
    (define dir (make-temporary-directory))
    (define file (build-path dir "world.save"))
    (define straight (simulate name #:seconds 60 #:step 0.05 #:sample-dt 60))
    (define first-half (simulate name #:seconds 30 #:step 0.05 #:sample-dt 30 #:save (cons file 30)))
    (check-true (file-exists? file) (format "~a: the save was written" name))
    (check-true (regexp-match? #rx"[(]world-save 1" (call-with-input-file file (λ (in) (read-string 4000 in)))) "it is a version 1 world save")
    (define resumed (simulate name #:seconds 30 #:step 0.05 #:sample-dt 30 #:resume file))
    (define end-straight (last straight))
    (define end-resumed (last resumed))
    (check-= (car end-resumed) (car end-straight) 1e-9 (format "~a: it ends on the same clock" name))
    (for ([entry (cdr end-straight)])
      (define other (assq (car entry) (cdr end-resumed)))
      (check-true (and other #t) (format "~a: ~a is in the resumed run" name (car entry)))
      (when other
        (check-= (cadr other) (cadr entry) (* 1e-7 (max 1.0 (abs (cadr entry))))
                 (format "~a: ~a after saving and loading" name (car entry)))))
    ;; and the first half's last frame is the state that was saved
    (check-= (car (last first-half)) 30.0 1e-6)
    (delete-directory/files dir)))

(test-case "Save and load in the game: a trip-hammer saved at 3 s and loaded in a new run goes on as if it had never stopped"
  ;; the strong wheel turns at 30 rpm and strikes twice a second: after saving at 3 s and loading, it has done what an
  ;; uninterrupted run has done by 6 s. The save holds the follower's state and the wheel's pose and speed (the engine's
  ;; own bodies), so nothing restarts. The weak wheel, hunting round its stall, is only held to the same band.
  (when (godot-available?)
    (define dir (make-temporary-directory))
    (define file (path->string (build-path dir "hammer.save")))
    (define straight (godot-simulate 'trip-hammer #:seconds 6 #:sample-dt 1))
    (godot-simulate 'trip-hammer #:seconds 3.05 #:sample-dt 1 #:env `(("HEROIC_SAVE" . ,file) ("HEROIC_SAVE_AT" . "3")))
    (check-true (file-exists? file) "the game wrote the save")
    (define resumed (godot-simulate 'trip-hammer #:seconds 6 #:sample-dt 1 #:env `(("HEROIC_LOAD" . ,file))))
    (check-= (car (last resumed)) (car (last straight)) 1e-6 "ends on the same clock")
    (for ([path '((strong-hammer strikes) (strong-hammer work) (strong-hammer speed) (strong-wheel omega) (strong-hammer fastest))])
      (check-= (final-of resumed path) (final-of straight path) (* 1e-4 (max 1.0 (abs (final-of straight path)))) (format "~a after loading" path)))
    (check-= (final-of resumed '(strong-hammer strikes)) 12 0 "twelve strikes in 6 s, as if never stopped")
    (check-= (final-of resumed '(weak-hammer strikes)) 0 0 "the stalled wheel still never strikes")
    (check-true (< 0.4 (final-of resumed '(weak-hammer height)) 1.0) "and its hammer is still held up about 0.7 cm")
    (delete-directory/files dir)))

;; ---- #13, part 2: friction and wear in the axles and hinges Jolt turns (axle-friction.rkt)
;; Three iron disc flywheels (25 cm radius, 4 cm wide, 2.5 cm bore) let go at
;; 60 rpm on a 2 cm pin; two 1 m iron beams hung 25 cm from one end, let go
;; 15 degrees from hanging, on a 1 cm pin; and the overshot water wheel of
;; water-wheels.rkt on a 3 cm axle. The numbers are worked out here from the
;; shapes and iron's density; the mesh is a 64-sided revolve, so the disc's
;; own mass and inertia come out about 0.5% under the ideal disc's.
(define g 9.81)
(define fly-r 0.25) (define fly-w 0.04) (define fly-b (* 0.1 fly-r)) (define fly-pin 0.02)
(define fly-m (* iron-density pi (- (* fly-r fly-r) (* fly-b fly-b)) fly-w))
(define fly-I (* 1/2 iron-density pi fly-w (- (expt fly-r 4) (expt fly-b 4))))
(define fly-w0 (* 2 pi 60/60))                                   ; rad/s

(test-case "Flywheels on bearings (Jolt): a frictionless one never slows, grease decays it exponentially, dry friction stops it dead and turns its spin to heat"
  (when (godot-available?)
    (define run (godot-simulate 'axle-friction #:seconds 5 #:sample-dt 1))
    ;; perfect: omega0 for ever
    (check-= (final-of run '(perfect omega)) fly-w0 (* 1e-3 fly-w0))
    (check-= (final-of run '(perfect heat)) 0 1e-12)
    ;; greased: omega = omega0 exp(-c t / I)
    (for ([t '(1 2 3 4 5)])
      (define predicted (* fly-w0 (exp (- (/ (* 0.5 t) fly-I)))))
      (check-= (abs (value-at run '(greased omega) t)) predicted (* 0.015 predicted) (format "rad/s at ~a s" t)))
    ;; and the heat is the energy lost: 1/2 I (omega0^2 - omega^2)
    (define w5 (abs (final-of run '(greased omega))))
    (check-= (final-of run '(greased heat)) (* 1/2 fly-I (- (* fly-w0 fly-w0) (* w5 w5))) (* 0.02 (* 1/2 fly-I fly-w0 fly-w0)) "J")
    ;; dry: friction mu m g r_pin at any speed, so a straight-line fall and a dead stop
    (define tau (* 0.4 fly-m g fly-pin))
    (for ([t '(1 2)])
      (define predicted (- fly-w0 (/ (* tau t) fly-I)))
      (check-= (abs (value-at run '(dry omega) t)) predicted (* 0.02 fly-w0) (format "rad/s at ~a s" t)))
    (define stops (/ (* fly-I fly-w0) tau))                       ; 2.53 s
    (check-= stops 2.527 0.005)
    (check-= (final-of run '(dry omega)) 0 1e-9 "rad/s: stopped by 5 s, and stays stopped")
    (check-= (abs (value-at run '(dry omega) 3)) 0 1e-9)
    (define spin-energy (* 1/2 fly-I fly-w0 fly-w0))              ; 37.3 J
    (check-= (final-of run '(dry heat)) spin-energy (* 0.01 spin-energy) "J: all of it, in the pin")
    ;; Archard: V = K N s, s the pin's surface slid, r_pin x the angle turned (omega0^2 I / 2 tau)
    (define turned (/ (* fly-w0 fly-w0 fly-I) (* 2 tau)))
    (define wear (* 1e-4 fly-m g fly-pin turned))
    (check-= (final-of run '(dry wear)) wear (* 0.02 wear) "mm^3 worn")))

;; The beam: m, I about the pivot (a box's own L^2/12 plus m d^2), d from the pivot
(define bar-L 1.0) (define bar-t 0.025) (define bar-d (* 0.25 bar-L))
(define bar-m (* iron-density bar-L bar-t 0.22))
(define bar-I (* bar-m (+ (/ (+ (* bar-L bar-L) (* bar-t bar-t)) 12) (* bar-d bar-d))))
(define bar-mgd (* bar-m g bar-d))
(define bar-pin 0.01)

;; the turning points of a series of angles (deg), in order, after the start
(define (turning-points vals)
  (let loop ([prev (car vals)] [dir 0] [rest (cdr vals)] [out '()])
    (cond [(null? rest) (reverse out)]
          [else
           (define d (let ([x (- (car rest) prev)]) (cond [(> x 1e-9) 1] [(< x -1e-9) -1] [else dir])))
           (loop (car rest) d (cdr rest) (if (and (not (zero? dir)) (not (= d dir))) (cons prev out) out))])))

(test-case "Levers on pins (Jolt): a frictionless pivot keeps its 15 degrees, a dry one loses the same angle every swing (energy balance), stops, and wears"
  (when (godot-available?)
    (define run (godot-simulate 'axle-friction #:seconds 12 #:sample-dt 1/120))
    (define a0 (* 15 (/ pi 180)))
    (define (swings-of name)
      (define vals (values-of run (list name 'angle)))
      (define peaks (turning-points vals))
      ;; the start is 15 degrees from plumb; the first turning point is on the far side of it
      (define plumb (+ (car vals) (* (if (> (car peaks) (car vals)) 15 -15))))
      (values plumb (for/list ([p peaks]) (abs (- p plumb)))))
    (define-values (plumb-free free-peaks) (swings-of 'free))
    (check-true (> (length free-peaks) 6) "swinging all along")
    (for ([a free-peaks] [i (in-naturals 1)]) (check-= a 15 0.2 (format "deg, swing ~a" i)))
    (check-= (final-of run '(free heat)) 0 1e-12)
    ;; dry: mu m g r on the pin, each half swing from A to A' obeys m g d (cos A' - cos A) = tau (A + A')
    (define tau (* 0.4 bar-m g bar-pin))
    (define (next a)
      (let loop ([lo 0.0] [hi a] [i 80])
        (define mid (/ (+ lo hi) 2))
        (define f (- (* bar-mgd (- (cos mid) (cos a))) (* tau (+ a mid))))
        (cond [(zero? i) mid] [(> f 0) (loop mid hi (sub1 i))] [else (loop lo mid (sub1 i))])))
    (define predicted (let loop ([a a0] [out '()])
                        (if (<= (* bar-mgd (sin a)) tau) (reverse out)
                            (let ([a2 (next a)]) (loop a2 (cons a2 out))))))
    (define-values (plumb-worn worn-peaks) (swings-of 'worn))
    (check-= (deg (- a0 (car predicted))) 1.85 0.01 "deg lost in the first half swing: 1.83 by 2 mu r / d, 1.85 exactly")
    (check-= (length worn-peaks) (length predicted) 1 "swings before it stops")
    (for ([a worn-peaks] [p predicted] [i (in-naturals 1)] #:when (<= i 6))
      (check-= a (deg p) 0.15 (format "deg, swing ~a" i)))
    (define rest-angle (abs (- (final-of run '(worn angle)) plumb-worn)))
    (check-true (< rest-angle 0.93) (format "deg off plumb at rest: ~a (within mu r / d = 0.92)" rest-angle))
    (check-= (final-of run '(worn heat)) (* bar-mgd (- (cos (* rest-angle (/ pi 180))) (cos a0))) (* 0.03 (* bar-mgd (- 1 (cos a0)))) "J: its swing, all heat")
    ;; the pin slides r times the angle turned: each half swing from A to A' turns A + A'
    (define sequence (cons a0 predicted))
    (define slid (* bar-pin (for/sum ([a sequence] [b (cdr sequence)]) (+ a b))))
    (check-true (> (final-of run '(worn wear)) 0) "the pin wore")
    (check-= (final-of run '(worn wear)) (* 1e-4 bar-m g slid) (* 0.03 (* 1e-4 bar-m g slid)) "mm^3: V = K N s")))

(test-case "Water wheel axle: the wheel settles at P / (load + mu m g r), the axle takes the rest as heat, and wears"
  ;; 441.45 W whatever its speed; a 300 N m millstone plus 0.4 x 1962 N x 3 cm = 23.54 N m
  (define power (* 1000 g 0.020 1.5 (- 1 (cos (* 120 (/ pi 180))))))
  (define tau (* 0.4 200 g 0.03))
  (define omega (/ power (+ 300 tau)))
  (define run (simulate 'axle-friction #:seconds 300 #:step 0.02 #:sample-dt 50))
  (check-= (rpm->rad (final-of run '(overshot rpm))) omega 1e-4 "rad/s")
  (check-= omega 1.3644 1e-4)
  (check-= (final-of run '(overshot power)) (* 300 omega) 0.05 "W into the millstone")
  ;; heat made over the last 50 s: tau omega each second
  (define dheat (- (final-of run '(overshot heat)) (value-at run '(overshot heat) 250)))
  (check-= dheat (* tau omega 50) 0.5 "J in 50 s")
  (define dwear (- (final-of run '(overshot wear)) (value-at run '(overshot wear) 250)))
  (check-= dwear (* 1e-4 (* 200 g) 0.03 omega 50) (* 0.01 dwear) "mm^3 in 50 s: V = K N s")
  ;; no axle, no bearing getters, and the old speed
  (define bare (simulate 'water-wheels #:seconds 300 #:step 0.02 #:sample-dt 50))
  (check-= (rpm->rad (final-of bare '(overshot rpm))) (/ power 300) 1e-4)
  (check-true (< omega (/ power 300))))

;; ---- #83 rotation and heading for parts (heading-rig.rkt, game/worlds/headings.world)
;; The same rig placed three times, turned 0, 37 and 90 degrees about the vertical
;; through its origin (and standing 0, 30 and 60 m along x). A machine turned by a
;; heading behaves as the unturned one: turn its coordinates back and every body is
;; where the unturned one is, with the same speed, spin and hinge angle. And each
;; part's own direction is worked out beforehand (the machine's header).
(define headings '((straight 0 0 0) (turned 37 30 0) (quarter 90 60 0)))
(define (to-machine-frame h ox oz x z)          ; the inverse of x' = x cos h + z sin h, z' = -x sin h + z cos h
  (define r (degrees->radians* h))
  (define dx (- x ox)) (define dz (- z oz))
  (values (- (* dx (cos r)) (* dz (sin r))) (+ (* dx (sin r)) (* dz (cos r)))))
(define (degrees->radians* d) (* d (/ pi 180)))
(define (field f key) (cadr (assq key (cdr f))))

(test-case "Parts at a heading (Jolt): the rig turned 37 and 90 degrees behaves as the unturned one with its coordinates turned back"
  (when (godot-available?)
    (define world (godot-simulate-world 'headings #:seconds 4 #:sample-dt 1/20))
    (define straight (hash-ref world 'straight))
    (check-true (> (length straight) 70) "a full trace")
    (for ([placement (cdr headings)])
      (define-values (label h ox oz) (apply values placement))
      (define run (hash-ref world label))
      (check-equal? (length run) (length straight) (format "~a has a full trace" label))
      (for ([a straight] [b run])
        (for ([body '(roller side-roller rod rod2 fly)])
          (define-values (xa za) (values (field a (string->symbol (format "~a.x" body))) (field a (string->symbol (format "~a.z" body)))))
          (define-values (xb zb) (to-machine-frame h ox oz (field b (string->symbol (format "~a.x" body))) (field b (string->symbol (format "~a.z" body)))))
          (define (near? what x y tol) (check-= x y tol (format "~a: ~a's ~a at ~a s" label body what (car a))))
          (near? "x" xb xa 0.01) (near? "z" zb za 0.01)
          (near? "y" (field b (string->symbol (format "~a.y" body))) (field a (string->symbol (format "~a.y" body))) 0.003)
          (near? "speed" (field b (string->symbol (format "~a.speed" body))) (field a (string->symbol (format "~a.speed" body))) 0.01))
        (check-= (field b 'fly.omega) (field a 'fly.omega) 0.005 (format "~a: the flywheel's spin at ~a s" label (car a)))
        (check-= (let ([d (- (field b 'fly.angle) (field a 'fly.angle))]) (- d (* 360 (round (/ d 360))))) 0 0.5
                 (format "~a: the flywheel's turn at ~a s" label (car a)))
        (check-= (field b 'rod.angle) (field a 'rod.angle) 0.2 (format "~a: the pendulum's angle at ~a s" label (car a)))))))

(test-case "Parts at a heading (Jolt): a rolling ball goes down a turned slope along the turned fall line at (5/7) g sin(angle), a turned part swings in its turned plane, and the flywheel runs down on 0.2 per second"
  (when (godot-available?)
    (define world (godot-simulate-world 'headings #:seconds 4 #:sample-dt 1/20))
    (define theta (degrees->radians* 35))
    (define g 9.81)
    ;; rolling: a = (5/7) g sin(35) along the slope; by half a second it has gone s = a t^2 / 2
    (define s (* 1/2 (* 5/7 g (sin theta)) 0.25))
    (check-= s 0.5024 1e-4)
    (for ([placement headings])
      (define-values (label h ox oz) (apply values placement))
      (define run (hash-ref world label))
      (define (moved body)
        (define (at k) (value-at run (list body k) 0.5))
        (define (start k) (value-at run (list body k) 0))
        (values (- (at 'x) (start 'x)) (- (at 'y) (start 'y)) (- (at 'z) (start 'z))))
      ;; the first ramp's downhill is (sin h, cos h), the second's, turned a further 90, (sin(h + 90), cos(h + 90))
      (for ([body '(roller side-roller)] [extra '(0 90)])
        (define-values (dx dy dz) (moved body))
        (define along (degrees->radians* (+ h extra)))
        (check-= (+ (* dx (sin along)) (* dz (cos along))) (* s (cos theta)) (* 0.03 s) (format "~a: ~a's way down the slope" label body))
        (check-= (- (* dx (cos along)) (* dz (sin along))) 0 0.003 (format "~a: ~a goes to neither side" label body))
        (check-= (- dy) (* s (sin theta)) (* 0.03 s) (format "~a: ~a's drop" label body)))
      ;; the swing: a compound pendulum, 50 cm of iron rod 1 cm across and a ball 4 cm across, from 10 degrees
      (define rho iron-density) (define len 0.5) (define rod-r 0.01) (define ball-r 0.04)
      (define m-rod (* rho pi rod-r rod-r len)) (define m-ball (* rho 4/3 pi (expt ball-r 3)))
      (define inertia (+ (/ (* m-rod len len) 3) (* m-ball (+ (* len len) (* 0.4 ball-r ball-r)))))
      (define d (/ (+ (* m-rod len 1/2) (* m-ball len)) (+ m-rod m-ball)))
      (define a0 (degrees->radians* 10))
      (define period (* 2 pi (sqrt (/ inertia (* (+ m-rod m-ball) g d))) (+ 1 (/ (* a0 a0) 16))))
      ;; the bob's place in the machine's own frame, so a swing along x is x, and along z is z
      (define in-machine
        (for/list ([f run])
          (define-values (x z) (to-machine-frame h ox oz (field f 'rod.x) (field f 'rod.z)))
          (define-values (x2 z2) (to-machine-frame h ox oz (field f 'rod2.x) (field f 'rod2.z)))
          (list (car f) x z x2 z2)))
      (define (cross-times k level)
        (for/list ([a in-machine] [b (cdr in-machine)] #:when (or (and (< (- (list-ref a k) level) 0) (>= (- (list-ref b k) level) 0))
                                                                   (and (> (- (list-ref a k) level) 0) (<= (- (list-ref b k) level) 0))))
          (+ (car a) (* (- (car b) (car a)) (/ (- level (list-ref a k)) (- (list-ref b k) (list-ref a k)))))))
      (define swings (cross-times 1 -2))                  ; the bob under its pivot, every half period
      (check-true (>= (length swings) 3) (format "~a: swinging" label))
      (for ([t0 swings] [t1 (cdr swings)])
        (check-= (* 2 (- t1 t0)) period (* 0.01 period) (format "~a: the rod's period" label)))
      (for ([row in-machine])
        (check-= (third row) 0 0.003 (format "~a: the rod stays in its plane at ~a s" label (car row)))
        (check-= (fourth row) -3 0.003 (format "~a: the second pendulum, hung at heading 90, swings along z at ~a s" label (car row))))
      (check-true (> (apply max (map fifth in-machine)) 0.06) (format "~a: and does swing along z" label))
      ;; the flywheel: 30 rpm, damped at 0.2 / s
      (for ([t '(1 2 3 4)])
        (define predicted (* pi (exp (* -0.2 t))))
        (check-= (value-at run '(fly omega) t) predicted (* 0.01 predicted) (format "~a: the flywheel's spin at ~a s" label t))))))

;; Machines the game already has, placed unturned and turned 53 degrees (game/worlds/headings-machines.world).
;; Each pair agrees, with the turned one's coordinates turned back, for as long as the motion is a
;; deterministic function of the geometry: a stone that has landed meets the ground with Jolt's friction,
;; applied along two axes picked from the contact's normal, not along the sliding direction, so it ends
;; some centimetres off (0.2 m for the trebuchet's, 0.15 m for the onager's at once, more as it slides);
;; stones are compared only in flight. The trebuchet's arm and counterweight, whose chain went chaotic
;; after its second snap before #80, now agree to a fifth of a millimetre for all six seconds.
(test-case "Existing machines turned 53 degrees (Jolt): cradle, trebuchet, Roman crane, onager, lunar train and wagons behave as unturned"
  (when (godot-available?)
    (define world (godot-simulate-world 'headings-machines #:seconds 9 #:sample-dt 1/4))
    (define (bodies-of frame)
      (remove-duplicates (for/list ([kv (cdr frame)] #:when (regexp-match #rx"[.]x$" (symbol->string (car kv))))
                           (regexp-replace #rx"[.]x$" (symbol->string (car kv)) ""))))
    ;; (machine at-x until position-tolerance always): every body is compared until `until`; the `always` bodies, the
    ;; whole run. The trebuchet and the onager start held on their catches (#155) and let go 3 s and 2 s in, so their
    ;; windows are the 2.0 s and 1.5 s of flight they always had, shifted that much, in a 9 s run
    (for ([spec '((cradle 0 6 0.003 ()) (trebuchet 150 5.0 0.05 (arm counterweight)) (crane 300 6 0.005 ())
                  (onager 450 3.5 0.01 (arm)) (train 600 6 0.003 ()) (wagons 750 6 0.01 ()))])
      (define-values (m ox until tol always) (apply values spec))
      (define a (hash-ref world (string->symbol (format "~a-0" m))))
      (define b (hash-ref world (string->symbol (format "~a-53" m))))
      (check-equal? (length a) (length b))
      (check-true (pair? (bodies-of (car a))) (format "~a has bodies" m))
      (for ([fa a] [fb b])
        (for ([body (bodies-of fa)] #:when (or (<= (car fa) until) (memq (string->symbol body) always)))
          (define (v f k) (field f (string->symbol (format "~a.~a" body k))))
          (define-values (xb zb) (to-machine-frame 53 ox 150 (v fb 'x) (v fb 'z)))
          (define (near? what x y t) (check-= x y t (format "~a: ~a's ~a at ~a s" m body what (car fa))))
          (near? "x" xb (- (v fa 'x) ox) tol) (near? "z" zb (v fa 'z) tol) (near? "y" (v fb 'y) (v fa 'y) tol)
          (when (assq (string->symbol (format "~a.angle" body)) (cdr fa))
            (near? "turn" (let ([d (- (v fb 'angle) (v fa 'angle))]) (- d (* 360 (round (/ d 360))))) 0 0.2)))))))

;; a trace frame list's value of a key at the frame nearest a time, and in its last frame
(define (value-at-key run key t)
  (define frame (for/fold ([best (car run)]) ([f (cdr run)]) (if (< (abs (- (car f) t)) (abs (- (car best) t))) f best)))
  (field frame key))
(define (final-of-key run key) (field (last run) key))

;; ---- #61 the crater world (victoria.rkt, lonely-rover-opening.world, crater-wind.world)
;; The map's own numbers are checked in tests/HeroicInventions.Sim.Tests/CraterTests.cs; here the game
;; plays them: the weakened rim comes down on screen over a few seconds and buries the cargo at its foot,
;; and two mills on the floor each take the wind of the field where they stand.
(test-case "The crater's opening (Jolt): the weakened rim comes down over about four seconds, buries the cargo as deep as its distance from the cliff, and leaves boulders, one over the solar-panels crate and none on the battery bank; the same each run"
  (when (godot-available?)
    (define (run-it) (godot-simulate-world 'lonely-rover-opening #:seconds 40 #:sample-dt 1))
    (define world (run-it))
    (define ground (hash-ref world 'links))
    ;; settling plays out at 20 passes a second: about 80 passes, four seconds, and is still going at 3 s
    (check-= (value-at-key ground 'map.settling 0.5) 1 0)
    (check-= (value-at-key ground 'map.settling 3.0) 1 0)
    (check-= (value-at-key ground 'map.settling 5.0) 0 0)
    (check-= (final-of-key ground 'map.settle-passes) 80 25)
    (check-= (/ (final-of-key ground 'map.settle-passes) 20.0) 4.0 1.2 "seconds")
    (check-true (< 10 (final-of-key ground 'map.settled) 60) "faces of the weakened block that failed")
    ;; each crate: held where the slide left it if more than a quarter of its height (12.5 cm) is over its lid,
    ;; and the pull to free it is its weight, the soil on the lid and the soil's grip on its sides
    (define g 3.71) (define s 0.5)
    (define gamma (* 2700 g)) (define tan-phi 0.60)                      ; rubble over bedrock cells: loose, no cohesion
    (define k0 (- 1 (sin (atan tan-phi))))
    (define (pull cover mass) (+ (* mass g) (* gamma cover s s) (* 4 s k0 gamma tan-phi (/ (- (sqr (+ cover s)) (sqr cover)) 2))))
    (define crates '(battery-bank solar-panels gas-cylinders hand-tools motors))
    (define covers
      (for/list ([c crates])
        (define f (last (hash-ref world c)))
        (define cover (field f 'crate.cover))
        (check-= (field f 'crate.buried) (if (> cover (* 0.25 s)) 1 0) 0 (format "~a: held exactly when more than a quarter of it is covered" c))
        (when (> cover (* 0.25 s))
          (check-= (field f 'crate.pull-out) (pull cover 90) (* 0.01 (pull cover 90)) (format "~a: the pull to free it" c)))
        cover))
    ;; the nearer the cliff's foot, the deeper the rubble over it: the solar-panels crate, moved by the owner's ruling of 2026-10-09 to (265.75, 143.5) under a boulder, is under 2.15 m (2.17 m at (254.6, 147)), the gas cylinders
    ;; (249.4) under 0.29 m; the bank, moved by the owner's ruling of 2026-10-09 to (254.5, 137.9), is under 1.04 m (predicted from a scan of
    ;; crates 1 m apart along the line: the rubble thins 0.39 m per metre, 1.24 m at x = 255 and 0.83 at 254)
    (check-true (> (second covers) (third covers)) (format "covers fall away from the cliff: ~a" covers))
    (check-true (andmap negative? (drop covers 3)) "and the last two crates are bare")
    (check-= (first covers) 1.04 0.2 "the battery bank is buried under about 1 m: held, within a backhoe's reach, and a vault can be built round it where it lies")
    (check-true (> (field (last (hash-ref world 'battery-bank)) 'crate.buried) 0.5) "the battery bank is buried")
    ;; the rock (#88): 2% of what the failed faces lost comes down as 2 m cubes of granite, floor(0.02 V / 8 m3) of them
    (define end (last ground))
    (define collapsed (field end 'map.collapsed))
    (check-= (field end 'map.boulders) (floor (/ (* 0.02 collapsed) 8)) 0 "boulders")
    (check-= (field end 'map.boulders) 18 3)
    (check-= (field end 'map.boulder-volume) (* 8 (field end 'map.boulders)) 1e-9 "m3")
    (check-= (- (field (car ground) 'map.ground-volume) (field end 'map.ground-volume)) (field end 'map.boulder-volume) 1e-6
             "the ground lost exactly what the boulders hold")
    (define n (inexact->exact (field end 'map.boulders)))
    (define (boulder i key) (field end (string->symbol (format "boulder-~a.~a" i key))))
    (for ([i (in-range 1 (+ n 1))])
      (check-true (< (boulder i 'speed) 0.01) (format "boulder ~a has come to rest by 40 s" i)))
    ;; the bank's cover is 1 m, not 4: the boulders stop on the thick rubble, and none rests on the bank now (the nearest is 7.7 m off)
    (define bank (last (hash-ref world 'battery-bank)))
    (define nearest
      (for/fold ([best #f]) ([i (in-range 1 (+ n 1))])
        (define d (sqrt (+ (sqr (- (boulder i 'x) (field bank 'crate.x))) (sqr (- (boulder i 'z) (field bank 'crate.z))))))
        (if (or (not best) (< d (car best))) (cons d i) best)))
    (check-true (> (car nearest) 5.0) (format "boulder ~a lies ~a m from the battery bank: none on it" (cdr nearest) (car nearest)))
    ;; owner ruling 2026-10-09: a boulder rests over a crate the win does not need (not the bank, not the motors), so that undermining it and a
    ;; lever stay reachable: boulder 4 (265.75, 141.72) lies 1.78 m (x-z) from the solar-panels crate (265.75, 143.5), which is held under 2.15 m
    (define solar (last (hash-ref world 'solar-panels)))
    (define (xz-from-solar i) (sqrt (+ (sqr (- (boulder i 'x) (field solar 'crate.x))) (sqr (- (boulder i 'z) (field solar 'crate.z))))))
    (define over-solar (argmin xz-from-solar (range 1 (+ n 1))))
    (check-= (xz-from-solar over-solar) 1.78 0.1 (format "boulder ~a lies over the solar-panels crate" over-solar))
    (check-true (< (xz-from-solar over-solar) 1.8) "its centre within 1.8 m of the crate's")
    (check-= (field solar 'crate.cover) 2.15 0.15 "the crate is under about 2 m, as the cargo was before the move")
    (check-= (field solar 'crate.buried) 1 0 "and held")
    (check-= (* 2700 (expt (boulder over-solar 'size) 3)) 21600 1 "kg: a 21.6 t cube over the crate, too big for a backhoe")
    (for ([c '(gas-cylinders hand-tools motors)])
      (define f (last (hash-ref world c)))
      (check-true (> (for/fold ([m 1e9]) ([i (in-range 1 (+ n 1))]) (min m (sqrt (+ (sqr (- (boulder i 'x) (field f 'crate.x))) (sqr (- (boulder i 'z) (field f 'crate.z)))))))
                     4.0)
                  (format "no boulder rests near ~a" c)))
    (define mass (* 2700 (expt (boulder (cdr nearest) 'size) 3)))
    (check-= mass 21600 1 "kg: too big for a backhoe")
    (check-true (> mass 5000) "far over what a rover-mounted backhoe lifts")
    ;; deterministic: a second run buries them the same way and leaves the boulders in the same places
    (define again (run-it))
    (for ([c crates])
      (check-= (field (last (hash-ref again c)) 'crate.cover) (field (last (hash-ref world c)) 'crate.cover) 1e-6 (format "~a again" c)))
    (for* ([i (in-range 1 (+ n 1))] [key '(x y z)])
      (check-= (field (last (hash-ref again 'links)) (string->symbol (format "boulder-~a.~a" i key))) (boulder i key) 1e-3
               (format "boulder ~a ~a again" i key)))))

(test-case "Two mills on the crater floor (Jolt): each takes the wind of the map's field where it stands, 6 m/s x corridor x daily x gusts, and the power goes as its cube"
  (when (godot-available?)
    (define world (godot-simulate-world 'crater-wind #:seconds 300 #:sample-dt 25))
    (define on (hash-ref world 'in-corridor))
    (define off (hash-ref world 'off-corridor))
    ;; the line from the notch (azimuth 200) through the centre; 0.3 + 0.7 e^-(d/80)^2 across it
    (define (corridor across) (+ 0.3 (* 0.7 (exp (- (sqr (/ across 80)))))))
    (define sol-length (let ([a (car on)] [b (last on)])
                         (/ (- (car b) (car a)) (- (field b 'scene.sols) (field a 'scene.sols)))))
    (check-= sol-length 88775 5 "Mars's solar day, s")
    (for ([a on] [b off])
      (define hour (field a 'scene.time))
      (define seconds (* (field a 'scene.sols) sol-length))
      (define daily (+ 1 (* 0.35 (cos (* 2 pi (/ (- hour 2) 24))))))
      (define gusts (+ 1 (* 0.25 (/ (+ (sin (* 2 pi (/ seconds 37))) (sin (+ (* 2 pi (/ seconds 91)) 1.3))) 2))))
      (check-= (field a 'mill.wind) (* 6 (corridor 0.01) daily gusts) 0.05 (format "in the corridor at ~a s" (car a)))
      (check-= (field b 'mill.wind) (* 6 (corridor 160.04) daily gusts) 0.05 (format "across it at ~a s" (car a)))
      ;; 1/2 rho A v^3 through the sails' 314.16 m2, rho the air's there
      (check-= (field a 'mill.wind-power) (* 1/2 (field a 'scene.air-density) (* pi 100) (expt (field a 'mill.wind) 3))
               (* 1e-3 (field a 'mill.wind-power)) "W"))
    (define last-on (last on)) (define last-off (last off))
    (check-= (/ (field last-off 'mill.wind-power) (field last-on 'mill.wind-power))
             (expt (/ (corridor 160.04) (corridor 0.01)) 3) 0.003 "the same wind weaker by 0.313, a power weaker by its cube, 0.031")))

;; ---- #113: gear trains driven by any shaft, and loaded (geared-brake.rkt)
;; Worked before the first run, in the machine's header: a flywheel geared
;; 10:1 up into a 0.1 N m brake, the train 1 kg m^2 seen from the flywheel,
;; let go at 60 rpm, the issue's case (62.8 rad/s at the brake at 600 rpm: it ran at 36 rpm
;; while Jolt let a body spin only 47.1 rad/s, and since #187 the limit is 314.16). Through a perfect mesh the brake reflects 1 N m and the
;; speed falls in a straight line, 1 rad/s per second, to a stop at
;; I w0 / tau = 6.283 s; through a mesh of efficiency 0.9 at (eta I_1 + n^2 I_2)
;; w0 / (n tau), with I_1 and I_2 the two arbors' own inertia read from the
;; compiled machine. The brake's heat is the energy that reached it: all the
;; train's spin through the perfect mesh, and only 0.9 of the flywheel arbor's
;; through the lossy one.
(require racket/runtime-path (only-in racket/file file->string))
(define-runtime-path compiled-machines "../../../game/machines")
(define (machine-inertia machine part)
  ;; density x inertia-z of a part, from the compiled .machine file
  (define form (call-with-input-file (build-path compiled-machines (format "~a.machine" machine)) read))
  (define p (for/first ([c (cddr form)] #:when (and (pair? c) (eq? (car c) 'part) (eq? (cadr c) part))) c))
  (define (field l k) (for/first ([x l] #:when (and (pair? x) (eq? (car x) k))) (cadr x)))
  (define material (field p 'material))
  (* (material-field (assq material (material-table)) 'density) (field (cdr (assq 'props (cdddr p))) 'inertia-z)))

(test-case "Geared brake (Jolt): a flywheel geared 10:1 into a 0.1 N m brake stops in I w0 / (n tau / eta)"
  (when (godot-available?)
    (define run (godot-simulate 'geared-brake #:seconds 8 #:sample-dt 1/120))
    (define w0 (* 2 pi 60/60))
    (define I1 (+ (machine-inertia 'geared-brake 'plain-flywheel) (machine-inertia 'geared-brake 'plain-gear)))
    (define I2 (+ (machine-inertia 'geared-brake 'plain-brake) (machine-inertia 'geared-brake 'plain-pinion)))
    (check-= (+ I1 (* 100 I2)) 1.0 1e-9 "kg m^2: the train seen from the flywheel")
    (define (stops eta) (/ (* (+ (* eta I1) (* 100 I2)) w0) (* 10 0.1)))
    (check-= (stops 1) 6.283 0.001)
    (check-= (stops 0.9) 5.669 0.001)
    ;; #202: the engine reports a body pinned at Jolt's spin limit (314.16 rad/s); the 600 rpm brake shaft is nowhere near it
    (check-= (final-of run '(spin-limit hits)) 0 0 "bodies that reached the spin limit")
    (for ([name '(plain lossy)] [eta '(1 0.9)])
      (define fly (string->symbol (format "~a-flywheel" name)))
      (define brake (string->symbol (format "~a-brake" name)))
      (define (omega t) (value-at run (list fly 'omega) t))
      (define T (stops eta))
      ;; a straight line: the speed at a quarter, half and three quarters of the way
      (for ([k '(0.25 0.5 0.75)])
        (check-= (omega (* k T)) (* w0 (- 1 k)) (* 0.005 w0) (format "~a rad/s at ~a s" name (* k T))))
      ;; stopped within 2% of the predicted time, and stays stopped
      (define stopped (for/first ([f run] #:when (< (cadr (assq (string->symbol (format "~a.omega" fly)) (cdr f))) 1e-6)) (car f)))
      (check-true (and stopped (< (abs (- stopped T)) (* 0.02 T))) (format "~a stopped at ~a s, predicted ~a s" name stopped T))
      (check-= (final-of run (list fly 'omega)) 0 1e-6)
      ;; the brake shaft turned 10 times as fast as the flywheel, the other way (the trace's omega is a
      ;; magnitude), less at most what one physics step's braking takes from its own light inertia,
      ;; tau dt / I_2 = 3.65 rad/s, before the next tick's exchange puts it back on the ratio
      (check-= (value-at run (list brake 'omega) (/ T 2)) (* 10 (omega (/ T 2))) (/ (* 0.1 1/120) I2))
      ;; the mesh held the flywheel's arbor back with I_1 x its deceleration: n tau / eta of it (less the brake shaft's share)
      (check-= (value-at run (list (string->symbol (format "~a-gear" name)) 'load-torque) (/ T 2)) (* I1 (/ w0 T)) 0.01 "N m")
      ;; the heat: 1/2 (eta I_1 + n^2 I_2) w0^2
      (check-= (final-of run (list brake 'heat)) (* 1/2 (+ (* eta I1) (* 100 I2)) w0 w0) 0.05 "J"))))

;; ---- #187: fast shafts (generator-train.rkt). Worked in the machine's header before the run: a windmill in a
;; 3 m/s wind geared 125:1 through three 0.97 meshes into a 2 N m load settles where its torque, 255.34 (2 - lambda /
;; lambda*), meets 125 x 2 / 0.97^3 = 273.92 N m: 1.39086 rad/s (13.28 rpm), the rotor at 173.86 rad/s (1,660 rpm, over
;; a generator's 1,500 rpm cut-in), Jolt's spin limit raised from 47.1 to 314.16 rad/s (100 pi) for it.
(test-case "Generator train (#187): a windmill geared 125:1 turns a 2 N m load at 1,660 rpm, and the torques balance"
  (when (godot-available?)
    (define run (godot-simulate 'generator-train #:seconds 240 #:sample-dt 1))
    (define (at k) (value-at run k 240))
    (define w-sails (* (at '(sails rpm)) 2 pi 1/60))
    (check-= (at '(sails rpm)) 13.282 0.01 "1.39086 rad/s")
    ;; the rotor at 173.86 rad/s is well under Jolt's 314.16 limit: no body is clamped (#202)
    (check-= (at '(spin-limit hits)) 0 0 "bodies that reached the spin limit")
    ;; read at the start of each step, before the load slows it within it: 2 x (1/120) / 0.0984 = 0.17 rad/s over the ratio
    (check-= (at '(generator omega)) (+ (* 125 w-sails) 0.17) 0.05 "125 x the sails")
    (check-= (at '(generator omega)) 173.86 (* 0.002 173.86) "rad/s")
    (check-true (> (at '(generator omega)) (* 1500 2 pi 1/60)) "over the 1,500 rpm cut-in")
    ;; the torque at each stage: what the next asks, x 5 / 0.97, from the rotor's 2 N m back to the sails' 273.92
    (check-= (abs (at '(pinion-d drive-torque))) 2.000 0.005)
    (check-= (abs (at '(pinion-c drive-torque))) 10.309 0.02)
    (check-= (abs (at '(pinion-b drive-torque))) 53.14 0.1)
    (check-= (at '(wheel-a load-torque)) 273.92 0.3 "N m on the sails: 125 x 2 / 0.97^3")
    (check-= (at '(sails torque)) (at '(wheel-a load-torque)) 0.1 "the sails give what the train takes: steady")
    ;; power: the sails take Cp 0.2984 of the wind's 1,276.7 W, 381.0 W; the load gets 0.97^3 of it
    (check-= (* (at '(sails torque)) w-sails) 381.0 0.6 "W in")
    (check-= (at '(generator grinding-power)) (* 0.97 0.97 0.97 381.0) 1.0 "W out")
    ;; every pair of meshed teeth still in its gaps (half a tooth is 9 degrees on the pinions)
    (for ([g '(pinion-b pinion-c pinion-d)]) (check-true (< (abs (at (list g 'mesh-error))) 1.0) (format "~a's teeth" g)))))

(test-case "Generator train (#187): a geared pair let go at 300 and 1,500 rpm keeps its energy for 240 s"
  ;; 1/2 x 0.08774 x 31.416^2 + 1/2 x 0.002211 x 157.08^2 = 70.57 J, nothing to slow it (a driven train's bodies are undamped)
  (when (godot-available?)
    (define run (godot-simulate 'generator-train #:seconds 240 #:sample-dt 1))
    (define Iw (machine-inertia 'generator-train 'free-wheel))
    (define Ip (machine-inertia 'generator-train 'free-pinion))
    (define (energy f) (let ([w (cadr (assq 'free-wheel.omega (cdr f)))] [p (cadr (assq 'free-pinion.omega (cdr f)))])
                         (* 1/2 (+ (* Iw w w) (* Ip p p)))))
    (define e0 (* 1/2 (+ (* Iw (sqr (* 2 pi 5))) (* Ip (sqr (* 2 pi 25))))))
    (check-= e0 70.57 0.01 "J")
    (for ([f run])
      (check-= (energy f) e0 (* 1e-4 e0) (format "J at ~a s" (car f)))
      (check-= (cadr (assq 'free-pinion.omega (cdr f))) (* 5 (cadr (assq 'free-wheel.omega (cdr f)))) 1e-3 "5 to 1"))
    (check-= (final-of run '(free-pinion omega)) 157.08 0.01 "rad/s, 1,500 rpm, 240 s on")))

;; ---- #122 on #113: the Hierapolis sawmill (hierapolis-sawmill.rkt)
;; Worked before the run (the machine's header): an overshot wheel fed
;; 2.9 L/s gives 42.67 W at any speed; the crank (0.25 m) and 1 m rod draw a
;; 29.57 kg iron frame over limestone (friction 0.4, the smaller of the two),
;; and the frame's friction, pressed harder by the rod's lean while the crank
;; turns clockwise and swung by the frame's own inertia, takes a mean
;; 20.49 N m at any speed: the wheel settles at 19.89 rpm, the blade at a mean
;; 2 w r / pi = 0.332 m/s over a 0.5 m stroke.
(define (saw-power w #:mu [mu 0.4] #:sense [sense -1] #:n [n 20000])
  ;; W the frame's friction takes, the crank turning steadily at w rad/s
  (define g 9.81) (define r 0.25) (define l 1.0)
  (define m-saw (* 7700 0.6 0.08 0.08)) (define m-rod (* 720 1.0 0.04 0.04))
  (define dth (/ (* 2 pi) n))
  (define work
    (for/sum ([i n])
      (define th (* (+ i 0.5) dth))
      (define s (sin th)) (define c (cos th))
      (define root (sqrt (- (* l l) (* r r s s))))
      (define x (+ (* r c) root))                                   ; the wrist, from the axle
      (define dx (- (- (* r s)) (/ (* r r s c) root)))              ; dx/dtheta
      (define d2x (- (- (* r c)) (/ (* r r (- (* c c) (* s s))) root) (/ (* r r r r s s c c) (expt root 3))))
      (define a (* w w d2x))                                        ; the frame's acceleration
      (define sg (if (> (* sense dx) 0) 1 -1))                      ; which way it slides
      (define ux (/ (- (* r c) x) l)) (define uy (/ (* r s) l))     ; the rod, wrist to pin
      ;; m a = R ux - mu N sg and N = m g + (rod's weight)/2 - R uy, solved for N
      (define N (/ (- (+ (* m-saw g) (* 0.5 m-rod g)) (* m-saw a (/ uy ux)))
                   (+ 1 (* mu sg (/ uy ux)))))
      (* mu N (abs dx) dth)))
  (/ (* work w) (* 2 pi)))

(test-case "Hierapolis sawmill (Jolt): the water wheel turns the crank and saws at the speed the frame's friction allows"
  (define water-power (* 1000 9.81 0.0029 1.0 1.5))
  (check-= water-power 42.67 0.01 "W")
  (define torque (/ (saw-power 2.0) 2.0))
  (check-= torque 20.49 0.01 "N m: the same at any speed")
  (check-= (/ (saw-power 3.0) 3.0) torque 1e-6)
  (define w (/ water-power torque))
  (check-= (* w (/ 30 pi)) 19.89 0.01 "rpm")
  (when (godot-available?)
    (define run (godot-simulate 'hierapolis-sawmill #:seconds 150 #:sample-dt 0.1))
    (define late (filter (λ (f) (>= (car f) 110)) run))   ; settled: the spin-up's time constant is about 20 s
    (define (field f k) (cadr (assq k (cdr f))))
    (define (mean k) (/ (for/sum ([f late]) (field f k)) (length late)))
    (define rpm (mean 'wheel.rpm))
    (check-= rpm (* w (/ 30 pi)) (* 0.02 (* w (/ 30 pi))) "rpm, the wheel")
    (check-= (* (mean 'crank.omega) (/ 30 pi)) rpm 0.1 "the crank keeps the wheel's speed")
    (check-= (mean 'wheel.load-torque) torque (* 0.03 torque) "N m the saw takes from the wheel")
    (define xs (map (λ (f) (field f 'saw-frame.x)) late))
    (check-= (- (apply max xs) (apply min xs)) 0.5 0.01 "m, the stroke: twice the crank")
    (define traced-w (* rpm (/ pi 30)))
    (check-= (/ (for/sum ([f late]) (abs (field f 'saw-frame.vx))) (length late)) (/ (* 2 traced-w 0.25) pi) 0.01 "m/s, the blade's mean speed")))

;; ---- #175 world-only parts: what each one shows when it is picked from the machine list

(test-case "Field windmill, alone (#175): a default 6 m/s wind, 515.2 W through the sails, 154.5 W to the stones at 14.03 rpm"
  ;; field-windmill.rkt: 1/2 rho A v^3 = 0.5 x 0.0151833 x 314.159 x 216; the stones are set for rho 0.0155,
  ;; 1.020829 x too stiff for this air, so lambda = 2.5 (2 - 1.020829) = 2.44786, omega = 1.46872 rad/s,
  ;; reached with the time constant I / (load/1.020829 x R / (2.5 v)) = 50000 / 68.69 = 727.9 s
  (define run (simulate 'field-windmill #:seconds 5000 #:step 0.05 #:sample-dt 1200))
  (check-= (final-of run '(mill wind)) 6 1e-9)
  (check-= (final-of run '(mill wind-power)) 515.158 0.01)
  (define (rpm-at t) (* 1.46872 (- 1 (exp (- (/ t 727.9)))) (/ 30 pi)))
  (define (value-at-time t key) (cadr (assq key (cdr (for/first ([f run] #:when (>= (car f) (- t 1e-6))) f)))))
  (check-= (value-at-time 1200 'mill.rpm) (rpm-at 1200) 0.05 "11.33 rpm")
  (check-= (final-of run '(mill rpm)) (rpm-at 4800) 0.05 "settling toward 14.03")
  (check-= (final-of run '(mill power)) (* 105.1805 (final-of run '(mill rpm)) (/ pi 30)) 0.05 "the stones take load x omega"))

;; ---- #176 demos with the part that shows their effect

(test-case "Baghdad battery lamp (#176): the filament glows by the jars' power, 760 C for one jar, 1500 C for ten, cold once the acid is spent"
  ;; filament T = 1773.15 K (P / 649.5 uW)^(1/4): one jar 75 uW -> 1033.6 K = 760.5 C; ten jars 649.5 uW -> 1500.0 C;
  ;; the drops jar (75 uW) is spent at 8.034 C / 0.15 mA = 53,558 s and then gives no power: a cold filament, 20 C
  (define (filament p) (- (* 1773.15 (expt (/ p 649.5) 0.25)) 273.15))
  (check-= (filament 75) 760.5 0.05)
  (check-= (filament 649.5) 1500.0 1e-6)
  (define run (simulate 'baghdad-battery #:seconds 54000 #:step 10 #:sample-dt 6000))
  (define (at t key) (cadr (assq key (cdr (for/first ([f run] #:when (>= (car f) (- t 1e-3))) f)))))
  (for ([t '(0 24000 48000)])
    (check-= (at t 'one-jar.power) 75 1e-6)
    (check-= (at t 'one-jar.filament) (filament 75) 1e-6 (format "one jar at ~a s" t))
    (check-= (at t 'ten-jars.power) 649.5 1e-6)
    (check-= (at t 'ten-jars.filament) (filament 649.5) 1e-6)
    (check-= (at t 'drops.filament) (filament 75) 1e-6 "the drops jar glows the same until it is spent"))
  (check-= (at 54000 'drops.power) 0 1e-9 "spent")
  (check-= (at 54000 'drops.filament) 20 1e-9 "cold")
  (check-= (at 54000 'one-jar.filament) (filament 75) 1e-6 "the full jar still lights it"))

;; ---- #154 driven wheels a person can start, stop, slow, reverse and let go
;; The roman crane (roman-crane.rkt) lifts its granite at 3 rpm x 0.25 m = 7.85 cm/s. The people at the wheel
;; are a hinge motor, and a field set at run time changes it: here the operator log sets it, timed under
;; godot-simulate's #:set, at 15.4 s, when the stone passes 1.2 m (it used to be a test-only copy of the blueprint with
;; a trigger clause, written beside the others and removed; the trigger fired at 15.4 s in every variant).
;; Worked out before the first run, with the bodies' inertias from the blueprint:
;;   m = 2700 x 0.6^3 = 583.2 kg; r = 0.25 m; I_wheel + I_drum = 720 x (2.5254 + 0.0061957) = 1822.7 kg m2;
;;   the pulley (bronze, 5.18e-5 x 8800 = 0.456 kg m2 on 0.15 m) adds 20.3 kg to the stone's;
;;   a = m g r^2 / (m r^2 + I) with the pulley: 583.2 x 9.81 / (583.2 + 1822.7/0.0625 + 20.3) = 0.1922 m/s2
;;   (the issue's formula without the pulley: 0.1923). The axle's 0.2 /s bearing damping slows both wheel and
;;   drum: v' = -a - k v with k = 0.2 x 29,164 / 29,767 = 0.196 /s, and the stone is still rising at 7.85 cm/s
;;   when the walkers let go, so v(0.4 s) = -a/k + (v0 + a/k) e^(-0.4 k) = -0.0014 m/s.
;;   Holding still needs m g r = 1430.3 N m; the walkers give 1545.1. At a torque of 1300 the 130 N m left over,
;;   at 0.25 m, is 520 N on 29,767 kg: 0.0175 m/s2, ~7.9 cm in 3 s less the stopping.
;;   Reversed at -3 rpm it lowers at 3 x 2 pi / 60 x 0.25 = 7.854 cm/s.
(define crane-walkers-act-at 15.4)   ; s: the stone, rising from 0.30 m, reaches the 1.2 m mark (the old test's trigger clause)
(define (crane-run actions) (godot-simulate 'roman-crane #:seconds 24 #:sample-dt 1/120 #:set actions))
(define (first-time run key pred)
  (for/first ([f run] #:when (let ([v (assq key (cdr f))]) (and v (pred (cadr v))))) (car f)))

(test-case "Roman crane: with the walkers let go the stone back-drives the wheel and falls at a = m g / (m + I/r^2); held at 0 rpm it hangs; reversed it lowers at 7.85 cm/s"
  (when (godot-available?)
    (define g 9.81) (define m (* 2700 0.6 0.6 0.6)) (define r 0.25)
    (define I (+ (machine-inertia 'roman-crane 'tympanus) (machine-inertia 'roman-crane 'drum)))
    (define pulley (/ (machine-inertia 'roman-crane 'pulley) (sqr 0.15)))
    (define a (/ (* m g) (+ m (/ I (sqr r)) pulley)))
    (check-= a 0.1922 0.0002 "the hand figure")
    (define k (/ (* 0.2 (/ I (sqr r))) (+ m (/ I (sqr r)) pulley)))
    (define v0 (* 3 (/ (* 2 pi) 60) r))
    (define (y-at run t) (value-at-key run 'stone.y t))
    (define (vy-at run t) (value-at-key run 'stone.vy t))
    ;; --- let go
    (let ()
        (define run (crane-run `((tympanus drive-torque 0.0 ,crane-walkers-act-at))))
        (define t0 (first-time run 'tympanus.drive-torque zero?))
        (check-true (and t0 (< 9 t0 20)) (format "the walkers let go at ~a s, as the stone passes 1.2 m" t0))
        (check-= (vy-at run (- t0 0.2)) v0 0.003 "it was rising at 7.85 cm/s")
        (define predicted (+ (- (/ a k)) (* (+ v0 (/ a k)) (exp (* -0.4 k)))))
        (check-= (vy-at run (+ t0 0.4)) predicted 0.004 (format "0.4 s after it is falling back: v = ~a, predicted ~a" (vy-at run (+ t0 0.4)) predicted))
        (check-true (< (y-at run (+ t0 4)) (- (y-at run t0) 0.5)) "and in four seconds it has dropped more than half a metre")
        ;; the fall is the free-wheel acceleration, damped: v(3) = -a/k (1 - e^-3k) + v0 e^-3k
        (check-= (vy-at run (+ t0 3)) (+ (* (/ a k) (- (exp (* -3 k)) 1)) (* v0 (exp (* -3 k)))) 0.01))
    ;; --- held still
    (let ()
        (define run (crane-run `((tympanus drive-rpm 0.0 ,crane-walkers-act-at))))
        (define t0 (first-time run 'tympanus.drive-rpm zero?))
        (check-true (and t0 (< 9 t0 20)))
        (check-= (y-at run (+ t0 4)) (y-at run (+ t0 0.3)) 0.01 "1545 N m against 1430: the stone hangs still"))
    ;; --- held with less torque than the stone's 1430 N m
    (let ()
        (define run (crane-run `((tympanus drive-rpm 0.0 ,crane-walkers-act-at) (tympanus drive-torque 1300.0 ,crane-walkers-act-at))))
        (define t0 (first-time run 'tympanus.drive-torque (λ (v) (< v 1400))))
        (check-true (> (- (y-at run (+ t0 0.3)) (y-at run (+ t0 3.3))) 0.03)
                    (format "1300 N m is not enough: it slips ~a m in 3 s" (- (y-at run (+ t0 0.3)) (y-at run (+ t0 3.3))))))
    ;; --- reversed
    (let ()
        (define run (crane-run `((tympanus drive-rpm -3.0 ,crane-walkers-act-at))))
        (define t0 (first-time run 'tympanus.drive-rpm (λ (v) (< v 0))))
        (check-= (vy-at run (+ t0 3)) (- v0) 0.002 "lowers at 7.85 cm/s")
        (check-= (- (y-at run (+ t0 3)) (y-at run (+ t0 2))) (- v0) 0.003))))

;; The gristmill's weak wheel gives 200 N m against stones set to 267, so it never turns (its header says to
;; set weak.grind-torque in the console). Set to 150, the 50 N m left over spins the stone up: alpha = 50 / I
;; with I the stone's own inertia from the blueprint (about 175 kg m2, 0.286 rad/s2), so it takes 12.566 / 0.286
;; = 44 s to reach its 120 rpm = 12.566 rad/s; after that the stones grind 150 x 12.566 = 1885 W x 54 kg/kWh =
;; 0.02828 kg/s of flour: 0.283 kg in 10 s.
(test-case "Gristmill: the weak stone's grind-torque set to 150 at run time lets the 200 N m wheel turn it, and it grinds"
  (when (godot-available?)
    (define run (godot-simulate 'gristmill #:seconds 60 #:sample-dt 5 #:set '((weak grind-torque 150))))
    (define w (* 120 (/ (* 2 pi) 60)))
    (define alpha (/ 50 (machine-inertia 'gristmill 'weak)))
    (check-= (value-at-key run 'weak.omega 20) (* alpha 20) (* 0.01 (* alpha 20)) (format "spinning up at ~a rad/s2" alpha))
    (check-= (final-of-key run 'weak.omega) w 0.05 "then up to 120 rpm")
    (check-= (final-of-key run 'weak.grinding-power) (* 150 w) 10)
    (check-= (- (final-of-key run 'weak.flour) (value-at-key run 'weak.flour 50)) (* 10 150 w (/ 54 3.6e6)) 0.005)))
