#lang racket/base
;; Issue #206: the rover pushes 40 kg of 200 C basalt into a lidded bin, and from then on it is that bin's store. The real game, headless
;; (Jolt, 120 Hz), world game/worlds/heat-rock-check.world (racket/machines/heat-rock.rkt placed as "rig"), the rover's hands by the game's own
;; scripted steps (game/scripts/Main.RoverHands.cs: "rover near", "rover press", "rover hand", "rover release").
;;
;; Worked before running (heat-rock.rkt's header has the working)
;;   rover   pushes 396 N on Mars (tyre grip) against mu m g = 0.6 x 40 kg x 3.71 = 89 N, so the arm may take it (no "Too heavy" refusal), and
;;           it never lifts it: the rock's base stays on the ground (+5 cm slack)
;;   open    the rock radiates to the air (-24.0 C at 17:00): eps A sigma (T^4 - T_air^4) = 814.8 W on 33.6 kJ/K = 0.02425 K/s, lit by the
;;           heliostat's 472 W at the start (net 0.0102 K/s) until it is pushed 0.5 m from the spot
;;   bin     once its middle is within 0.16 m of the bin's middle it is the bin's store: the lid is open at once (bank -55 C), and it cools at the
;;           cavity's 823.8 W / 33.6 kJ/K = 0.02452 K/s; the temperature does not jump at the change
;;   night   the same moment put into the sim alone (heat-rock with `rock z 0 AT`) gives the bank at 03:00: night-heat's 4.28 C less
;;           0.00887 K per kJ the rock lost in the open before AT (814.8 W x AT, less a little as it cools)
;; Skipped when Godot is not installed.
(require rackunit racket/list racket/string racket/system racket/port racket/runtime-path
         (only-in heroic/godothost godot-available? godot-binary godot-simulate-world final-of)
         (only-in heroic/simhost simulate))

(define-runtime-path game-dir "../../../game")

(define (at frame key) (cadr (assq key (cdr frame))))
(define (frame-near run t) (argmin (λ (f) (abs (- (car f) t))) run))

;; the push: two arm-lengths, the rover driven up to the rock between (the arm reaches 1.8 m from its pivot, the bin's floor is 1.2 m past the rock)
(define push-script
  (string-append "wait 120; rover near rock 1.0; wait 60; rover press rock; rover hand 0 0 -0.75; wait 240; rover release; wait 60;"
                 " rover near rock 1.0; wait 60; rover press rock; rover hand 0 0 -0.8; wait 240; rover release; wait 120"))

(when (godot-available?)
  (define lines
    (let ([env (environment-variables-copy (current-environment-variables))])
      (environment-variables-set! env #"HEROIC_WORLD" #"heat-rock-check")
      (environment-variables-set! env #"HEROIC_INPUT" (string->bytes/utf-8 push-script))
      (environment-variables-set! env #"HEROIC_QUIT_AFTER_SIM_SECONDS" #"40")
      (string-split
       (parameterize ([current-directory game-dir] [current-environment-variables env])
         (with-output-to-string (λ () (system* godot-binary "--headless" "--fixed-fps" "120" "--path" "."))))
       "\n")))
  (define run (hash-ref (godot-simulate-world 'heat-rock-check #:seconds 40 #:sample-dt 0.5
                                              #:env `(("HEROIC_INPUT" . ,push-script)))
                        'rig))
  (define in-bin (filter (λ (f) (= 1 (at f 'rock.in-bin))) run))

  (test-case "the rover takes hold of 40 kg of basalt and is not refused: it is within the arm's reach and force"
    (check-equal? (filter (λ (l) (string-contains? l "refused")) lines) '() "no refusal in the push")
    (check-equal? (length (filter (λ (l) (string-contains? l "press rock: used, hand holds rock")) lines)) 2 "both pushes took hold"))

  (test-case "pushed sideways over the ground into the bin, the rock is in it, the bin's store, and has not been lifted"
    (check-true (pair? in-bin) "the rock was in the bin at some time")
    (define last-frame (last run))
    (check-= (at last-frame 'rock.in-bin) 1 0 "and is in it at the end")
    (check-= (at last-frame 'bin.holding) 1 0 "the bin holds a store")
    (check-= (at last-frame 'bin.temperature) (at last-frame 'rock.temperature) 1e-9 "and its temperature is the rock's")
    (check-= (at last-frame 'rock.x) 0.12 0.16 "inside the cavity (middle within 0.16 m of the bin's middle at x 0.12)")
    (check-= (at last-frame 'rock.z) 0.0 0.16 "and z 0")
    ;; a rover never lifts a load: the hand stops 5 cm above where it took hold (the rock may tip on an edge as it is dragged, and settles again),
    ;; so it ends resting on the ground of the bin, as low as it began
    (check-= (at last-frame 'rock.y) (at (first run) 'rock.y) 0.02 "resting on the floor again, not carried up and over the wall"))

  (test-case "in the open the rock cools by the radiation law; lit by the heliostat until pushed out of the beam; carried into the bin it goes on cooling at the cavity's rate"
    (define t-in (car (first in-bin)))
    (check-true (< 3 t-in 10) (format "in the bin after ~a s" t-in))
    ;; unlit, in the open: the heliostat stops once the rock is 0.5 m from its spot; between that and the bin it is unlit and bare to the air
    (define t-dark (car (first (filter (λ (f) (= 0 (at f 'heliostat.power))) run))))
    (check-true (< 0 t-dark t-in) (format "unlit from ~a s, in the bin from ~a s" t-dark t-in))
    (define (rate a b) (/ (- (at (frame-near run a) 'rock.temperature) (at (frame-near run b) 'rock.temperature)) (- (car (frame-near run b)) (car (frame-near run a)))))
    (check-= (rate 0 1) 0.01019 0.002 "lit in the open: (814.6 - 472.2) W / 33.6 kJ/K = 0.0102 K/s")
    (check-= (rate (+ t-dark 0.5) (- t-in 0.5)) 0.02425 0.0012 "unlit in the open: 814.8 W / 33.6 kJ/K")
    (check-= (rate (+ t-in 5) 40) 0.02448 0.0007 "in the bin: 823.8 W at 200 C initially (0.02452 K/s), a little less as the rock cools and the cavity wall warms (traced 0.02407)")
    ;; no jump at the change of zone
    (check-= (- (at (frame-near run (- t-in 0.5)) 'rock.temperature) (at (frame-near run (+ t-in 0.5)) 'rock.temperature)) 0.0245 0.01 "no jump in 1 s across it"))

  (test-case "the night after the push: the sim alone, with the rock put in at the moment Jolt had it there, gives the bank night-heat's +4.3 C at 03:00"
    (define t-in (car (first in-bin)))
    (define night
      (last (simulate 'heat-rock #:seconds 36990 #:step 5 #:sample-dt 36990 #:set `((heliostat dust 1 0) (rock z 0 ,t-in)))))
    ;; the rock lost 814.8 W x t-in less a little in the open before it was in: 0.00887 K per kJ off night-heat's +4.28 C
    (define predicted (- 4.28 (* 0.00887 (/ (* 814.8 t-in) 1000))))
    (check-= (at night 'bank.temperature) predicted 0.2 (format "pushed in at ~a s: worked ~a C" t-in predicted))
    (check-= (at night 'bank.temperature) 4.3 0.4 "night-heat's +4.3 C")))
