#lang racket/base
;; Issue #96: the doc's fastest route as a script, run in the crater world (game/worlds/lonely-rover-e2e.world) in the real game (headless
;; Godot, Jolt at 120 Hz), and the win asserted. docs/e2e-route.md says step by step what the game lets a player do and what a stand-in
;; does. Since #204, #208 and #209 the windmill, its gear train and the generator are BUILT, by scripted build steps (the game's own build
;; path, as build-in-world-test.rkt does), and wired to the found bank, which is the bank that charges and wins.
;;
;; The route (G = done in the game as a player would, S = a stand-in; the GAPs are numbered in docs/e2e-route.md):
;;   1  the slide buries the bank                      G  the world's opening: the crate is held under about 4 m of rubble
;;   2  free the bank                                  S  GAP 1: e2e-rover-test.rkt digs at the slide (the dirt rule lets the backhoe dig now);
;;                                                        the sleep run below does not free the crate
;;   3  bury it in a tight vault                       S  GAP 2: the vault is round the bank's cells in the bank's machine (an enclosure
;;                                                        contains by position inside its own machine), a placed enclosure
;;   4  a windmill with a geared generator             G  built in build mode: 10 m sails on a post, four 72:18 meshes (4:1 each, 256:1) and the
;;                                                        salvaged motor on the last pinion; saved with the world and loaded
;;   5  wire the generator to the bank                 S  the world link (from built-1 motor) (to battery-bank bank) put into the saved world's
;;                                                        link list (the join gesture is clicked on screen: it needs a real window, GAP 6)
;;   6  rock heated by heliostats, pushed into the bin S  GAP 4: the heliostat heats the rock in the bin
;;   7  a bimetal on the lid                           G  the strip works the lid in proportion
;;   8  sleep until 02:49, windmill tops up, call      G  the game's own sleep (HEROIC_SLEEP=pre-dawn on the bank's machine, whose clock is the world's),
;;                                                        then real time; GAP 5 (no scripted sleep step, so the world is built, saved and loaded asleep)
;; Predictions (worked before running):
;;  - the train: the sails' torque is tau* (2 - w R / (v l*)), tau* = 1/2 rho A v^2 R Cp / l* = 104.0 N.m at the world's air (0.015325 kg/m3)
;;    and the palette windmill's 6 m/s, so at the rotor X = 256 w it balances the motor's torque k (X - 157.08) (k = 0.114592 N.m per rad/s above
;;    the 1,500 rpm cut-in) at 256 k (X - 157.08) = 2 tau* - 0.2708 X: X = 162.7 rad/s (1,553 rpm), 5.6 rad/s over the cut-in, tau_g = 0.64 N.m,
;;    104 W of shaft and 83 W into the bank (the sim: 1,557 rpm, 90 W; the answer is a small difference of two large numbers). Three stages (64:1)
;;    also pass the cut-in at 6 m/s (161.5 rad/s, 1,542 rpm) but only above 4.9 m/s of wind (free-running, 3 rad/s x 64 must pass 157.08);
;;    four pass it above 1.2 m/s.
;;  - the sails turn free in the sleep to near their free-running speed (about 2.8 rad/s) and hold 1/2 I w^2 (I = m R^2 / 3 = 50,000 kg m2):
;;    a few Wh go into the bank in the first minute, the wind carries on at 90 W, and the 25 Wh bank (the scenario's 0.005 of 5 kWh) is full a
;;    few seconds before the pass opens at 73,979 s, 03:00 on sol 2, and the call goes out as the pass opens, not before.
;;  - the vault holds the bank at the sim's 21.9 C at 02:49 (the same vault and heliostat as the earlier route's, whose 20.1 C was from a
;;    bank 25 Wh in a different heat store: the cells here are the found bank's 16 kg).
;; Skipped when Godot is not installed.
(require rackunit racket/list racket/match racket/math racket/system racket/port racket/string racket/file racket/runtime-path
         (only-in heroic/godothost godot-available? godot-binary))

(define-runtime-path game-dir "../../../game")

(define (at frame key) (cadr (assq key (cdr frame))))
(define (maybe frame key) (let ([p (assq key (cdr frame))]) (and p (cadr p))))
(define (first-frame run pred) (for/first ([f run] #:when (pred f)) f))

(define sol-seconds 88775.0)
(define pass-start (* 20 (/ sol-seconds 24)))   ; 03:00 after a 07:00 start: 20 local hours, 73,979 s
(define wake-at 73300)

(define (run-game env-list)
  (define env (environment-variables-copy (current-environment-variables)))
  (environment-variables-set! env #"HEROIC_WORLD" #"lonely-rover-e2e")
  (for ([kv env-list]) (environment-variables-set! env (string->bytes/utf-8 (car kv)) (string->bytes/utf-8 (cdr kv))))
  (string-split
   (parameterize ([current-directory game-dir] [current-environment-variables env])
     (with-output-to-string (λ () (system* godot-binary "--headless" "--fixed-fps" "120" "--path" "."))))
   "\n"))

(define (trace-frames path) (if (file-exists? path) (call-with-input-file path (λ (in) (for/list ([f (in-port read in)]) f))) '()))

(define dir (make-temporary-file "e2e-route~a" 'directory))
(define (p f) (path->string (build-path dir f)))

;; the player's build (step 4), by the game's build steps; the post and the sails 11 m up as the build-in-world test has them
(define (stage name-w name-p x z prev)   ; a 72-tooth wheel on the previous pinion's arbor and a 18-tooth pinion meshing it
  (list (format "build (wheel ~a #:catalogue involute-gear-m5-72 #:at (~a 11 ~a))" name-w x z)
        (format "build (arbor ~a ~a)" prev name-w)
        (format "build (wheel ~a #:catalogue involute-gear-m5-18 #:at (~a 11 ~a))" name-p (+ x 0.225) z)
        (format "build (mesh ~a ~a)" name-w name-p)))
(define build-steps
  (string-join
   (append '("build (post tower #:at (0.45 0 -1.45) #:size-y 10.6 #:size-x 0.6 #:size-z 0.6)" "build (windmill sails #:at (0 11 0))")
           (stage "wheel-a" "pinion-b" 0 -1.3 "sails")
           (stage "wheel-b" "pinion-c" 0.225 -1.4 "pinion-b")
           (stage "wheel-c" "pinion-d" 0.45 -1.5 "pinion-c")
           (stage "wheel-d" "pinion-e" 0.675 -1.6 "pinion-d")
           '("build (generator motor #:at (0.9 11 -1.85))" "build (set motor #:on pinion-e)"))
   "; "))

(define wire "(link wire-1 wire (from built-1 motor) (to battery-bank bank))")

(define (sleep-run save #:set [settings #f] #:seconds [seconds (+ pass-start 120)])
  (define trace (p (format "t~a" (random 1000000))))
  (define lines
    (run-game `(("HEROIC_LOAD" . ,save) ("HEROIC_SLEEP" . "pre-dawn") ("HEROIC_QUIT_AFTER_SIM_SECONDS" . ,(number->string (exact->inexact seconds)))
                ("HEROIC_TRACE" . ,trace) ("HEROIC_TRACE_DT" . "10")
                ,@(if settings `(("HEROIC_SET" . ,settings)) '()))))
  (list lines (trace-frames (string-append trace ".battery-bank")) (trace-frames (string-append trace ".built-1"))))

(when (godot-available?)
  ;; step 4: the player builds the windmill, its train and the generator, and the world is saved with them
  (define built
    (run-game `(("HEROIC_INPUT" . ,(string-append "wait 360; build new 180 105; " build-steps "; build done; build list; waitsim 30; rover save " (p "a.save") "; quit")))))
  ;; step 5: the wire, as a world link in the save's link list (the unwired save is the gate)
  (define wired-save (p "wired.save"))
  (define saved (file->string (p "a.save")))
  (display-to-file (string-replace saved "(links 1.0)" (string-append "(links 1.0 " wire ")")) wired-save)

  ;; the three runs are independent processes: start them together
  (define results (make-vector 3 #f))
  (define threads
    (list (thread (λ () (vector-set! results 0 (sleep-run wired-save))))
          (thread (λ () (vector-set! results 1 (sleep-run wired-save #:set "heliostat area 0 0" #:seconds (+ pass-start 60)))))
          (thread (λ () (vector-set! results 2 (sleep-run (p "a.save") #:seconds (+ pass-start 60)))))))
  (for-each thread-wait threads)
  (match-define (list lines run built-run) (vector-ref results 0))
  (define (has? rx ls) (for/or ([l ls]) (regexp-match? rx l)))

  (test-case "the player builds the windmill, a four-stage train and the generator in build mode, and the world is saved with them"
    (check-true (has? #rx"^\\[build\\] placed built-1 at \\(180.00 -[0-9.]+ 105.00\\)" built))
    (check-true (has? #rx"^\\[build\\] built-1: design .*sails:windmill .*pinion-e:wheel motor:generator; running 11 parts$" built))
    (check-false (has? #rx"BuildMode\\] error" built) "no command was refused")
    (check-regexp-match #rx"\n  \\(build built-1 \\(at 180.0 0.0 105.0\\) \\(machine built-1 " saved)
    (check-regexp-match #rx"\\(part motor generator " saved))

  (test-case "loaded asleep, the wire is back, and the game's sleep runs the day and the night to 02:49 with the vault, the rock and the bank"
    (check-true (has? #rx"^\\[build\\] loaded built-1: 11 of 11 parts running" lines))
    (check-true (has? #rx"^\\[links\\] restored 1 link\\(s\\) from the save: wire-1 \\(wire\\)" lines))
    (check-true (has? #rx"^\\[sleep\\] woke after [0-9.]+ s: scene.elapsed" lines))
    (define woke (second run))
    (check-true (>= (car woke) wake-at) (format "the first frame after the sleep is at ~a s" (car woke)))
    (check-true (< 30 (at woke 'rock.temperature) 70) (format "rock ~a C" (at woke 'rock.temperature)))
    ;; the bank was frozen at -55 C at dawn; the vault round its cells held it above 0 C through the night
    (check-= (at woke 'cells.temperature) 21.9 1.5 "C, the found bank's own cells at 02:49 (the sim: 21.89)")
    (check-true (< 0.3 (at woke 'bin.open) 0.8) (format "lid ~a" (at woke 'bin.open))))

  (test-case "the built windmill charges the found bank over the wire, through the train, over the cut-in; only the wind has charged it"
    (define charging (filter (λ (f) (> (at f 'motor.power) 0)) built-run))
    (check-true (pair? charging) "the motor charged")
    (for ([f (in-list (take charging (min 200 (length charging))))])
      (check-true (> (at f 'motor.rpm) 1500) (format "charging only over the cut-in, at ~a s" (at f 'scene.elapsed))))
    ;; settled: the sails' flywheel is spent after about a minute and the wind alone carries the rest
    (define settled (filter (λ (f) (and (> (at f 'motor.power) 0) (> (at f 'scene.elapsed) (+ wake-at 200)))) built-run))
    (define mean-w (/ (apply + (map (λ (f) (at f 'motor.power)) settled)) (length settled)))
    (check-true (< 75 mean-w 100) (format "mean ~a W into the bank (worked 83, sim 90)" mean-w))
    (define rpm (/ (apply + (map (λ (f) (at f 'motor.rpm)) settled)) (length settled)))
    (check-= rpm 1553 15 "rpm of the rotor at the balance (worked 1,553)")
    (printf "E2E charge: mean ~a W, rotor ~a rpm over ~a samples; the doc's 5 kWh bank would take ~a h of this\n"
            (round mean-w) (round rpm) (length settled) (/ (round (* 10 (/ 5000 mean-w))) 10.0))
    (check-true (has? #rx"^\\[frontend\\] ending source wind: 25.000 Wh" lines) "the bank's record: 25 Wh, all of it wind")
    (check-true (has? #rx"^\\[frontend\\] ending: bank sol 2 at 03:00, 25.00 of 25.00 Wh at 21.[0-9] C, sources 1" lines)))

  (test-case "the found bank is full before the pass and warm, and the call goes out as the pass opens at 03:00 on sol 2, not before"
    (define t-full (car (first-frame run (λ (f) (>= (at f 'bank.charge) 24.9999)))))
    (define t-won (car (first-frame run (λ (f) (> (at f 'bank.won) 0.5)))))
    (printf "E2E night: woke at ~a s with the bank at ~a C and the rock at ~a C; full at ~a s (~a s before the pass); call at ~a s\n"
            (car (second run)) (at (second run) 'cells.temperature) (at (second run) 'rock.temperature) t-full (round (- pass-start t-full)) t-won)
    (check-true (< wake-at t-full pass-start) (format "full at ~a s, ~a s before the pass" t-full (- pass-start t-full)))
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
    (check-= (at (last run) 'bank.won) 1 0 "and it stays won")
    (check-true (has? #rx"win: The call went out from bank on sol 2 at 03:00" lines)))

  (test-case "the bank charged is the found bank in its crate, held under the slide: the opening ran"
    (define f (second run))   ; the first frame is the load; the cover is read from the second
    (check-= (at f 'crate.buried) 1 0)
    (check-true (> (at f 'crate.cover) 1) (format "cover ~a m" (at f 'crate.cover)))
    (check-= (at f 'bank.capacity) 25 1e-9 "Wh: the 5 kWh bank at the scenario's 0.005"))

  (test-case "the cold-bank gate: with the heliostat covered the bank stays under 0 C, charges nothing, and no call goes out"
    (define cold (second (vector-ref results 1)))
    (for ([f (in-list (cdr cold))])
      (check-= (at f 'bank.charge) 0 1e-12)
      (check-= (at f 'bank.won) 0 0))
    (check-true (< (at (last cold) 'cells.temperature) 0) (format "bank ~a C" (at (last cold) 'cells.temperature)))
    (check-true (> (car (last cold)) pass-start) "it ran through the pass"))

  (test-case "the unwired gate: the same windmill, bank warm, no wire: the generator is an open circuit, nothing is charged, no call goes out"
    (define unwired (vector-ref results 2))
    (check-true (has? #rx"^\\[links\\] restored 0 link\\(s\\)" (first unwired)))
    (for ([f (in-list (cdr (second unwired)))])
      (check-= (at f 'bank.charge) 0 1e-12)
      (check-= (at f 'bank.won) 0 0))
    (for ([f (in-list (third unwired))]) (check-= (at f 'motor.power) 0 1e-12))
    (check-true (> (at (last (second unwired)) 'cells.temperature) 10) "the bank was warm: only the missing wire stopped it")
    (check-true (> (car (last (second unwired))) pass-start))))
