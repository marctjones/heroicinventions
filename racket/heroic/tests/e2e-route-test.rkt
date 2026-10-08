#lang racket/base
;; Issue #96: the doc's fastest route as a script, run in the crater world (game/worlds/lonely-rover-e2e.world) in the real game (headless
;; Godot, Jolt at 120 Hz), and the win asserted. docs/e2e-route.md says step by step what the game lets a player do and what a stand-in
;; does; racket/machines/e2e-route.rkt is the works the stand-ins put on the crater floor (its header has the worked numbers).
;;
;; The route (G = done in the game as a player would, S = a stand-in; the GAPs are numbered in docs/e2e-route.md):
;;   1  the slide buries the bank                      G  the world's own opening: the crate is held under about 4 m of rubble
;;   2  free the bank (undermine the boulders)         S  GAP 1: the rover digs "nothing: bedrock under the teeth" on the slide
;;   3  bury it in a tight regolith vault              S  GAP 2: the vault is a placed enclosure with a bank in it
;;   4  a windmill with a geared generator             S  GAP 3: placed; nothing in a rover world builds or places a machine
;;   5  rock heated by heliostats, pushed into the bin S  GAP 4: the heliostat heats the rock where it lies in the bin; nothing pushes rock
;;   6  a bimetal on the lid                           G  the part of the works (the strip works the lid in proportion, #97)
;;   7  sleep until the 03:00 pass, and win            G  the game's sleep (#59), started by the machine's wake through HEROIC_SLEEP (GAP 5: no
;;                                                        scripted sleep step in a world), the windmill charging for the minutes before the pass
;; Predictions (worked before running; the machine's header has the working): from 07:00 on the first sol with the bank, the rock and
;; the walls at the ground's -55 C, a 1.5 m2 heliostat heats the 40 kg rock in the bin (peak 118.7 C at 35,200 s), the lid stays open,
;; the bank passes 0 C at 17:05 and settles near 20 C (simulated by the C# sim: 20.1 C at 02:49, 19.93 C at 03:00). Woken at 73,300 s
;; (02:49) the sails, turning free all night at the corridor's wind, hold a flywheel of 1/2 I w^2; the generator then loads them and the
;; wind carries on, 160 to 280 W into the bank, so a 25 Wh bank is full a few minutes before the pass and the call goes out as the
;; pass opens at 73,979 s (20 local hours after 07:00), on sol 2, not before.
;; Skipped when Godot is not installed.
(require rackunit racket/list racket/math racket/system racket/port racket/string racket/runtime-path
         heroic/godothost)

(define-runtime-path game-dir "../../../game")

(define (at frame key) (cadr (assq key (cdr frame))))
(define (maybe frame key) (let ([p (assq key (cdr frame))]) (and p (cadr p))))
(define (first-frame run pred) (for/first ([f run] #:when (pred f)) f))

(define sol-seconds 88775.0)
(define pass-start (* 20 (/ sol-seconds 24)))   ; 03:00 after a 07:00 start: 20 local hours, 73,979 s
(define wake-at 73300)

;; the route in the crater world, from the machine's own wake (HEROIC_SLEEP) to a little past the pass
(define (route-run #:set [settings #f] #:seconds [seconds (+ pass-start 120)])
  (define world
    (godot-simulate-world 'lonely-rover-e2e #:seconds seconds #:sample-dt 10
                          #:env (append '(("HEROIC_SLEEP" . "pre-dawn"))
                                        (if settings `(("HEROIC_SET" . ,settings)) '()))))
  (values (hash-ref world 'route) world))

(when (godot-available?)
  ;; the three runs are independent processes: start them together
  (define results (make-vector 3 #f))
  (define threads
    (list (thread (λ () (vector-set! results 0 (call-with-values (λ () (route-run)) cons))))
          (thread (λ () (vector-set! results 1 (call-with-values (λ () (route-run #:set "motor cut-in-rpm 4000 0" #:seconds (+ pass-start 60))) cons))))
          (thread (λ () (vector-set! results 2 (call-with-values (λ () (route-run #:set "heliostat area 0 0" #:seconds (+ pass-start 60))) cons))))))
  (for-each thread-wait threads)
  (define run (car (vector-ref results 0)))
  (define world (cdr (vector-ref results 0)))

  (test-case "the sleep ran the day and the night to 02:49 with the vault, the rock and the bank (sim-side), and woke the works there"
    (define woke (second run))
    (check-true (>= (car woke) wake-at) (format "the first frame after the sleep is at ~a s" (car woke)))
    (check-= (at woke 'scene.elapsed) (car woke) 5)
    ;; the rock was heated by day and has cooled since: well under its 118.7 C peak
    (check-true (< 30 (at woke 'rock.temperature) 70) (format "rock ~a C" (at woke 'rock.temperature)))
    ;; the bank was frozen at -55 C at dawn and is at ~20 C: the vault held it above 0 C through the night
    (check-= (at woke 'cells.temperature) 20.1 1.5 "C, bank at 02:49 (the C# sim: 20.10)")
    (check-true (> (at woke 'cells.temperature) 0))
    ;; the lid is half open: the strip holds the bank near 20 C, not shut at 40 C
    (check-true (< 0.3 (at woke 'bin.open) 0.8) (format "lid ~a" (at woke 'bin.open))))

  (test-case "the windmill charges the bank through the 125:1 train over the cut-in, and only the wind has charged it"
    (define charging (filter (λ (f) (> (at f 'motor.power) 0)) run))
    (check-true (pair? charging) "the motor charged")
    ;; over the cut-in, loaded: the rotor above 1,500 rpm (157.08 rad/s), power 0.8 tau w with tau = 0.1146 (w - 157.08) at most 12 N.m
    (for ([f (in-list (take charging (min 200 (length charging))))])
      (check-true (> (at f 'motor.rpm) 1500) (format "charging only over the cut-in, at ~a s" (car f))))
    ;; settled rates the sails give in the corridor at night (traced 105 to 280 W with the wind's gusts)
    (define settled (filter (λ (f) (and (> (at f 'motor.power) 0) (> (car f) (+ wake-at 30)))) run))
    (define mean-w (/ (apply + (map (λ (f) (at f 'motor.power)) settled)) (length settled)))
    (check-true (< 100 mean-w 300) (format "mean ~a W into the bank" mean-w))
    (printf "E2E charge: mean ~a W, peak ~a W over ~a samples; the doc's 5 kWh bank would take ~a h of this\n"
            (round mean-w) (round (apply max (map (λ (f) (at f 'motor.power)) settled))) (length settled) (/ (round (* 10 (/ 5000 mean-w))) 10.0))
    ;; the gears hold the rotor under the physics engine's limit
    (for ([f (in-list run)] #:when (maybe f 'rotor-disc.omega))
      (check-true (<= (at f 'rotor-disc.omega) 314.17)))
    ;; where the charge came from: the bank records it by source, found by walking the train back to the sails
    (define end (last run))
    (check-= (at end 'bank.charge) 25 1e-6 "Wh, full")
    (check-= (at end 'bank.from-wind) 25 1e-6 "Wh from the wind: all of it"))

  (test-case "the bank is full before the pass and warm, and the call goes out as the pass opens at 03:00 on sol 2, not before"
    (define t-full (car (first-frame run (λ (f) (>= (at f 'bank.charge) 24.9999)))))
    (check-true (< wake-at t-full pass-start) (format "full at ~a s, ~a s before the pass" t-full (- pass-start t-full)))
    (define t-won (car (first-frame run (λ (f) (> (at f 'bank.won) 0.5)))))
    (printf "E2E night: woke at ~a s with the bank at ~a C and the rock at ~a C; full at ~a s (~a min before the pass); call at ~a s\n"
            wake-at (at (second run) 'cells.temperature) (at (second run) 'rock.temperature) t-full (/ (round (* 10 (/ (- pass-start t-full) 60))) 10.0) t-won)
    (check-true (<= pass-start t-won (+ pass-start 11)) (format "the call at ~a s; the pass opens at ~a s" t-won pass-start))
    (for ([f (in-list run)] #:when (< (car f) pass-start))
      (check-= (at f 'bank.won) 0 0 (format "no call before the pass, at ~a s" (car f)))
      (check-= (at f 'scene.won) 0 0))
    (define won (first-frame run (λ (f) (> (at f 'bank.won) 0.5))))
    (check-= (at won 'scene.won) 1 0)
    (check-true (<= 3.0 (at won 'scene.time) 3.01) (format "local time ~a h" (at won 'scene.time)))
    (check-= (at won 'scene.sol) 2 0 "sol 2: 20 hours after the 07:00 start of sol 1")
    (check-true (< 0 (at won 'bank.temperature) 45) (format "the bank is ~a C" (at won 'bank.temperature)))
    (check-= (at won 'bank.in-window) 1 0)
    (check-= (at (last run) 'bank.won) 1 0 "and it stays won"))

  (test-case "the opening runs in the e2e world: the slide has come down on the cargo and the bank's crate is held under the rubble"
    (define bank-crate (last (hash-ref world 'battery-bank)))
    (check-= (at bank-crate 'crate.buried) 1 0)
    (check-true (> (at bank-crate 'crate.cover) 1 ) (format "cover ~a m" (at bank-crate 'crate.cover))))

  (test-case "the cold-bank gate: with the heliostat covered the rock stays cold, the bank stays under 0 C, charges nothing, and no call goes out"
    (define cold (car (vector-ref results 2)))
    (for ([f (in-list (cdr cold))])
      (check-= (at f 'bank.charge) 0 1e-12)
      (check-= (at f 'bank.won) 0 0))
    (check-true (< (at (last cold) 'cells.temperature) 0) (format "bank ~a C" (at (last cold) 'cells.temperature)))
    (check-true (> (at (last cold) 'scene.elapsed) pass-start) "it ran through the pass"))

  (test-case "the cut-in gate: a motor that wants 4,000 rpm (the engine caps the rotor at 3,000) charges nothing, and no call goes out"
    (define slow (car (vector-ref results 1)))
    (for ([f (in-list (cdr slow))])
      (check-= (at f 'bank.charge) 0 1e-12)
      (check-= (at f 'bank.won) 0 0)
      (check-= (at f 'motor.power) 0 1e-12))
    (check-true (> (at (last slow) 'scene.elapsed) pass-start))
    (check-true (> (at (last slow) 'cells.temperature) 10) "the bank was warm: only the cut-in stopped it")))
