#lang racket/base
;; Issue #220: the Stirling route in the crater, built and measured against the design doc's 320 W at noon. In the real game (headless
;; Godot, Jolt at 120 Hz) in lonely-rover-e2e: a Stirling engine on a 1.5 m post and ten 1 m2 heliostats north of it, BUILT by the game's
;; scripted build steps (as e2e-route-test.rkt builds its windmill), the engine joined by a shaft to the salvaged motor of the `motors`
;; crate (found, not built) and the motor wired to the found bank, by the `join` step (the gesture's own path; a Stirling is a shaft-link
;; end since #220). The plan, with every number worked before running: docs/plans/stirling-route-plan.md.
;;
;; Worked before running (W in the plan; Sun.cs, Mirror.cs, StirlingEngine.cs and Generator.cs re-done in python and checked against
;; mars-stirling.rkt's traced numbers):
;;  - the sun: latitude 31.2 N, day 172 (the opening's clock: noon at the start), so the noon sun is 35.9 deg up, the beam 351.9 W/m2
;;    (Meridiani's, where mars-stirling stands: 432.7). The air is the weather's daily curve: -28.8 C at noon, -20 at 15:00.
;;  - the mirrors: ten 1 m2 flat heliostats at 0.85 (the build's default), rows at z -3.2 and -4.7 on the far side from the sun, sum of
;;    cos(theta/2) 9.582 at noon: 2,866 W on the 1 m2 aperture.
;;  - the engine (the build's template: K 14 W/K, 35% of Carnot, C 10,000 J/K; #:load 0, the motor its load): settled at noon its hot end
;;    is 98.75 C, it radiates 794 W, draws 1,785 W at an efficiency of 0.120, and makes 214.3 W at the shaft. Started cold at noon it
;;    peaks at 12:39 on 204.5 W (99.1 C; the air warming and the sun 0.8% down). The doc's 320 W fits Meridiani's sun and a -63 C sink.
;;  - the motor: the engine's power has no speed in it, so through a plain shaft the rotor settles where k (X - 157.08) X = P (k 0.114592):
;;    1,606 rpm and 1.27 N.m at 214 W (171 W of charge at eta 0.8), 1,602 rpm and 1.22 N.m at 204.5 W (164 W). A gear stage would change
;;    only the engine's own speed, not the charge.
;;  - unloaded (the bank cold), the engine and rotor run up as sqrt(2 P t / I) to Jolt's 3,000 rpm clamp in about 140 s.
;; Measured (2026-10-17, the first runs; the plan's "Measured"): A 213.9 W at 12:01, 1,606 rpm, 170.9 W of charge; B 204.5 W at 12:39,
;; 1,602 rpm, 163.4 W of charge; C at the clamp by 140 s, nothing charged.
;; Since #269 (docs/plans/stirling-drag-plan.md, "Decided"): the template engine has its own drag, a torque k w^2 (k 1.38e-5 N m s2),
;; and 0.4375 of Carnot for its gas cycle, so 0.35 at the shaft at 1,500 rpm. The hot end's balance has no fraction in it (98.75 C at
;; noon, as before); the indicated power is 1.25 x #220's: 267.4 W at 12:01, 255.6 W at 12:39. Worked before running (time-stepped):
;;  A  the rotor where 267.4 = 1.38e-5 X^3 + 0.114592 (X - 157.08) X: 1,601 rpm, 1.21 N m, 202 W at the shaft (65 W of drag), 162 W of charge
;;  B  asleep on its 2 N m: 255.6 = 2 w + k w^3, 1,115 rpm; joined, 1,595 rpm, 191 W at the shaft, 152.9 W of charge, 11.3 Wh by 2,700 s
;;  C  open circuit: from rest toward the free speed (P / k)^(1/3), 2,521 rpm at 200 s, 2,560 at 400 s: under the 3,000 rpm clamp
;;
;; The runs (three processes at once, about 2 minutes; each ends on HEROIC_QUIT_AFTER_SIM_SECONDS so the traces' last block is written):
;;  A (gate)    the engine built with its hot end GIVEN at its settled 98.75 C (#:temperature), joined at once; the cells set to 25 C (GIVEN:
;;              the found bank is -63 C at noon on sol 1 and passes 0 C near 19:00) so the motor is loaded. Checks the sim's balance.
;;  B (primary) the engine built cold on its own 2 N.m load (#:load 2, a player's setting), a paused sleep to 12:39, the load taken off at
;;              the wake, then the joins: the power at noon EARNED from a cold start. The engine's power has no speed in it, so warming
;;              on its own load is exactly the watched thermal history; the load keeps it from storing its work as speed in the sleep.
;;  C (gate)    A with the bank cold: the motor is an open circuit, nothing is charged, and the engine and rotor run to Jolt's clamp.
;; The engine's machine (built-1) keeps its own clock from its build, about 4 s into the world; the sun and the air are the world's. At 20x
;; the same run gives the same power for the same hot end, but the ten mirror commands land a frame apart (a 20x frame is 1/6 s), so the
;; engine starts with part of its heat and its hot end runs 0.9 K low for minutes: these runs are at 1x.
;; Skipped when Godot is not installed.
(require rackunit racket/list racket/string racket/system racket/port racket/file racket/runtime-path
         (only-in heroic/godothost godot-available? godot-binary))

(define-runtime-path game-dir "../../../game")

(define (at frame key) (let ([p (assq key (cdr frame))]) (if p (cadr p) (error 'at "no field ~a at ~a s" key (car frame)))))
(define (near run t) (argmin (λ (f) (abs (- (car f) t))) run))
(define (from run t) (filter (λ (f) (>= (car f) t)) run))

(define (run-game env-list)
  (define env (environment-variables-copy (current-environment-variables)))
  (environment-variables-set! env #"HEROIC_WORLD" #"lonely-rover-e2e")
  (for ([kv env-list]) (environment-variables-set! env (string->bytes/utf-8 (car kv)) (string->bytes/utf-8 (cdr kv))))
  (string-split
   (parameterize ([current-directory game-dir] [current-environment-variables env])
     (with-output-to-string (λ () (system* godot-binary "--headless" "--fixed-fps" "120" "--path" "."))))
   "\n"))

(define (trace-frames path) (if (file-exists? path) (call-with-input-file path (λ (in) (for/list ([f (in-port read in)]) f))) '()))

(define dir (make-temporary-file "stirling-route~a" 'directory))

;; the player's build, by the game's build steps (docs/plans/stirling-route-plan.md section 6): the engine before the mirrors (#:onto)
(define (build-steps engine-options)
  (string-join
   (append
    (list "build new 220 126"
          "build (post stand #:at (0 0 0) #:size-x 0.6 #:size-y 1.5 #:size-z 0.6)"
          (format "build (stirling eng #:at (0 1.5 0) #:aperture 1 #:conductance 14 ~a)" engine-options))
    (for/list ([x '(-2.4 -1.2 0 1.2 2.4 -2.4 -1.2 0 1.2 2.4)] [i (in-naturals 1)])
      (format "build (mirror m~a #:at (~a ~a ~a) #:area 1 #:onto eng)" i x (if (<= i 5) 0.7 1.3) (if (<= i 5) -3.2 -4.7)))
    (list "build done"))
   "; "))
(define joins "join built-1.eng motors.rotor; join motors.motor battery-bank.bank")

;; one run: its log lines and the traces of the engine's machine, the motor's, the bank's and the world's links
(define (run name input settings seconds dt)
  (define trace (path->string (build-path dir name)))
  (define lines
    (run-game `(("HEROIC_INPUT" . ,input) ("HEROIC_QUIT_AFTER_SIM_SECONDS" . ,(number->string seconds))
                ("HEROIC_TRACE" . ,trace) ("HEROIC_TRACE_DT" . ,(number->string dt))
                ,@(if settings `(("HEROIC_SET" . ,settings)) '()))))
  (define (t suffix) (trace-frames (string-append trace "." suffix)))
  (hash 'lines lines 'engine (t "built-1") 'motor (t "motors") 'bank (t "battery-bank") 'links (t "links")))

(define WAKE 2400)   ; s after the noon start: 12:38:55, the first afternoon's peak

(when (godot-available?)
  (define plans
    (list (λ () (run "A" (string-append "wait 360; " (build-steps "#:load 0 #:temperature 98.75") "; " joins "; waitsim 400")
                     "battery-bank:cells temperature 25" 400 1))
          (λ () (run "B" (string-append "wait 360; " (build-steps "#:load 2") (format "; sleep until scene.elapsed above ~a limit 3000 paused; " WAKE) joins "; waitsim 2700")
                     (format "battery-bank:cells temperature 25; built-1:eng load 0 ~a" WAKE) 2700 10))
          (λ () (run "C" (string-append "wait 360; " (build-steps "#:load 0 #:temperature 98.75") "; " joins "; waitsim 400")
                     #f 400 1))))
  (define results (make-vector 3 #f))
  (for-each thread-wait (for/list ([p plans] [i (in-naturals)]) (thread (λ () (vector-set! results i (p))))))
  (define-values (A B C) (values (vector-ref results 0) (vector-ref results 1) (vector-ref results 2)))
  (define (has? rx r) (for/or ([l (hash-ref r 'lines)]) (regexp-match? rx l)))

  (test-case "the player builds the engine and ten heliostats, and joins the engine to the found motor and the motor to the found bank"
    (for ([r (list A B C)])
      (check-true (has? #rx"^\\[build\\] placed built-1 at \\(220.00 -[0-9.]+ 126.00\\)" r))
      (check-false (has? #rx"BuildMode\\] error" r) "no command was refused")
      (check-true (has? #rx"^\\[links\\] joined: shaft shaft-1 from built-1.eng to motors.rotor$" r) "a Stirling is a shaft's end (#220)")
      (check-true (has? #rx"^\\[links\\] joined: wire wire-1 from motors.motor to battery-bank.bank$" r))
      (check-false (has? #rx"^join: " r) "no join was refused")))

  (test-case "A: with the hot end at its settled 98.75 C the engine makes the worked 214 W at noon, and the motor charges 171 W at 1,606 rpm"
    (define e (near (hash-ref A 'engine) 60))   ; 12:01
    (printf "STIRLING A (12:01, hot end given): on the aperture ~a W, hot end ~a C, air ~a C, radiated ~a W, drawn ~a W, efficiency ~a, shaft ~a W\n"
            (at e 'eng.heat) (at e 'eng.hot-temperature) (at e 'eng.cold-temperature) (at e 'eng.radiation) (at e 'eng.heat-drawn)
            (at e 'eng.efficiency) (at e 'eng.shaft-power))
    (check-= (at e 'eng.heat) 2866 15 "W on the aperture: 351.9 W/m2 x 0.85 x the ten mirrors' 9.582")
    (check-= (at e 'eng.hot-temperature) 98.75 0.3)
    (check-= (at e 'eng.cold-temperature) -28.7 0.1 "C: the weather's noon air")
    (check-= (at e 'eng.radiation) 794 5)
    (check-= (at e 'eng.heat-drawn) 1784 10)
    (check-= (at e 'eng.efficiency) 0.1499 0.001 "0.4375 (1 - Tc/Th) (#269: 0.35 at the shaft at 1,500 rpm)")
    (check-= (at e 'eng.indicated-power) 267.4 2.5 "W the gas cycle makes at noon (#220's 213.9 x 1.25)")
    (check-= (at e 'eng.shaft-power) 202.3 2.5 "W at the shaft at noon, after 65 W of drag at 1,601 rpm (#220 measured 213.9 before the drag)")
    (for ([m (from (hash-ref A 'motor) 100)] [l (from (hash-ref A 'links) 100)])
      (check-= (at m 'motor.rpm) 1601 15 (format "rotor rpm at ~a s" (car m)))
      (check-= (at m 'motor.torque) 1.21 0.04)
      (check-= (at l 'shaft-1.power) 202 3)
      (check-= (at l 'wire-1.power) 162 3 "W of charge: 0.8 of the shaft's")
      (check-= (at l 'wire-1.open-circuit) 0 0)
      (check-= (at l 'wire-1.from-stirling) (at l 'wire-1.charge) 1e-9 "the bank files it all under the Stirling"))
    (for ([e (from (hash-ref A 'engine) 100)]) (check-= (at e 'eng.rpm) 1601 15 "the engine turns with the rotor: ratio 1"))
    (define b2 (near (hash-ref A 'bank) 200))
    (define b3 (near (hash-ref A 'bank) 300))
    (define slope (/ (* 3600 (- (at b3 'bank.charge) (at b2 'bank.charge))) (- (car b3) (car b2))))
    (printf "STIRLING A: rotor ~a rpm, ~a N.m, charge ~a W (the bank's slope 200-300 s: ~a W)\n"
            (at (near (hash-ref A 'motor) 200) 'motor.rpm) (at (near (hash-ref A 'motor) 200) 'motor.torque) (at (near (hash-ref A 'links) 200) 'wire-1.power) slope)
    (check-= slope 162 5)
    (check-= (at (last (hash-ref A 'motor)) 'spin-limit.hits) 0 0 "loaded, the rotor never reaches Jolt's clamp")
    (check-= (at (near (hash-ref A 'engine) 390) 'eng.indicated-power) 266.0 2.5 "the sun and air drift by 12:07")
    (check-= (at (near (hash-ref A 'engine) 390) 'eng.shaft-power) 201.0 3 "less the drag at 1,601 rpm"))

  (test-case "B: started cold, the engine earns 204.5 W by 12:39, the first afternoon's peak; joined then, it charges the bank 164 W at 1,602 rpm"
    (define asleep (filter (λ (f) (< (car f) WAKE)) (hash-ref B 'bank)))
    (for ([f asleep]) (check-= (at f 'bank.charge) 0 0 (format "nothing charged before the join, at ~a s" (car f))))
    (define e (near (hash-ref B 'engine) WAKE))
    (printf "STIRLING B (~a s, 12:39, earned from a cold start): on the aperture ~a W, hot end ~a C, air ~a C, shaft ~a W, the engine ~a rpm on its own 2 N.m load\n"
            (car e) (at e 'eng.heat) (at e 'eng.hot-temperature) (at e 'eng.cold-temperature) (at e 'eng.shaft-power)
            (at (last (filter (λ (f) (< (car f) WAKE)) (hash-ref B 'engine))) 'eng.rpm))
    (check-= (at e 'eng.indicated-power) 255.6 3 "W the gas cycle makes at 12:39 (#220's 204.5 x 1.25)")
    (check-= (at e 'eng.hot-temperature) 99.1 1)
    (check-= (at e 'eng.heat) 2831 15)
    (check-= (at e 'eng.cold-temperature) -25.5 0.2)
    (check-= (at (last (filter (λ (f) (< (car f) WAKE)) (hash-ref B 'engine))) 'eng.rpm) 1115 30 "rpm on its own load in the sleep: 255.6 = 2 w + k w^3")
    (for ([m (from (hash-ref B 'motor) (+ WAKE 50))] [l (from (hash-ref B 'links) (+ WAKE 50))])
      (check-= (at m 'motor.rpm) 1595 15 (format "rotor rpm at ~a s" (car m)))
      (check-= (at l 'wire-1.power) 152.9 3 "W of charge")
      (check-= (at l 'shaft-1.power) 191 3 "W at the shaft after the drag"))
    (define end (last (hash-ref B 'bank)))
    (printf "STIRLING B: rotor ~a rpm, charge ~a W; the bank ~a Wh at ~a s, the cells ~a C\n"
            (at (last (hash-ref B 'motor)) 'motor.rpm) (at (last (hash-ref B 'links)) 'wire-1.power) (at end 'bank.charge) (car end) (at end 'cells.temperature))
    ;; time-stepped before running: the join near 2,400 s at a shared 98 rad/s, the cut-in about 20 s later, 152.9 W to 2,700 s: 11.3 Wh
    (check-= (at end 'bank.charge) 11.3 1.5 "Wh after about 280 s of charging")
    (check-true (< 0 (at end 'cells.temperature) 45)))

  (test-case "C: the bank cold, the motor is an open circuit: nothing is charged, and the unloaded engine and rotor settle at the engine's free speed, under Jolt's 3,000 rpm clamp"
    (for ([l (from (hash-ref C 'links) 10)]) (check-= (at l 'wire-1.power) 0 0))   ; (joined in the first seconds)
    (for ([b (hash-ref C 'bank)]) (check-= (at b 'bank.charge) 0 0))
    (check-= (at (near (hash-ref C 'engine) 60) 'eng.indicated-power) 267.4 2.5 "the engine makes its power whatever loads it")
    (define m (near (hash-ref C 'motor) 200))
    (define m4 (last (hash-ref C 'motor)))
    (printf "STIRLING C (cold bank): rotor ~a rpm at 200 s, ~a rpm at ~a s, spin-limit hits ~a; the engine's free speed ~a rpm, ~a W left at its shaft\n"
            (at m 'motor.rpm) (at m4 'motor.rpm) (car m4) (at m4 'spin-limit.hits)
            (at (last (hash-ref C 'engine)) 'eng.free-rpm) (at (last (hash-ref C 'engine)) 'eng.shaft-power))
    (check-= (at m 'motor.rpm) 2521 50 "rpm at 200 s: toward the free speed (#269's drag)")
    (check-= (at m4 'motor.rpm) 2560 40 "rpm at the end: the free speed (P / k)^(1/3)")
    (check-= (at m4 'spin-limit.hits) 0 0 "never at Jolt's clamp: the drag gives the engine a free speed of its own")
    (check-true (< (at (last (hash-ref C 'engine)) 'eng.shaft-power) 8) "at its free speed the drag takes nearly all the engine makes")))
