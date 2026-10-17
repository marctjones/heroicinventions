#lang racket/base
;; Issue #269, slice 0: a Stirling engine drives the gears on its arbor, as a windmill does. Before, the view counted only water wheels,
;; windmills and jet wheels as parts the sim turns (MachineView.Gears.IsSimTurned), so an arbor holding an engine looked for a Jolt body
;; named after it and the machine's view failed to build (`KeyNotFoundException: The given key 'eng' was not present`, in BuildArbors),
;; the engine turning alone and its gears still. An engine's crankshaft runs along X through its flywheel, 0.3 m over the engine's foot
;; (MachineView.Stirlings draws it there), so the arbor's wheels go on that line, about X; its forward turn is a positive turn about +X.
;;
;; In lonely-rover-e2e: #220's engine and ten heliostats (stirling-route-test.rkt), its hot end GIVEN at its settled 98.75 C, #:load 0;
;; a 72-tooth wheel on the engine's crankshaft meshing an 18-tooth pinion (4:1 up, mesh efficiency 1, as a typed mesh is), the pinion
;; joined by the join step to the salvaged motor's rotor and the motor to the found bank; the cells set to 25 C (GIVEN, as in #220's run A).
;; Worked before running: the engine's power has no speed in it (P = f (1 - Tc/Th) K (Th - Tc) = 214.3 W at noon) and the meshes and the
;; shaft lose nothing, so the rotor settles where the motor's k (X - 157.08) X = P whatever the gearing: 168.2 rad/s, 1,606 rpm, 1.27 N.m
;; and 171 W of charge, as on #220's plain shaft; the gearing sets only the engine's own speed, 1,606 / 4 = 401.5 rpm, where it turns the
;; 72 at its own speed and the 18 at four times it. (Friction, #269's next slice, will make the engine's speed matter: then the stage pays.)
;; About 2 minutes. Skipped when Godot is not installed.
(require rackunit racket/list racket/string racket/system racket/port racket/file racket/math racket/runtime-path
         (only-in heroic/godothost godot-available? godot-binary))

(define-runtime-path game-dir "../../../game")

(define (at frame key) (let ([p (assq key (cdr frame))]) (if p (cadr p) (error 'at "no field ~a at ~a s" key (car frame)))))
(define (from run t) (filter (λ (f) (>= (car f) t)) run))
(define (rpm w) (* w (/ 60 (* 2 pi))))

(define (trace-frames path) (if (file-exists? path) (call-with-input-file path (λ (in) (for/list ([f (in-port read in)]) f))) '()))

(define dir (make-temporary-file "stirling-gear~a" 'directory))
(define trace (path->string (build-path dir "G")))

(define build-steps
  (string-join
   (append
    (list "build new 220 126"
          "build (post stand #:at (0 0 0) #:size-x 0.6 #:size-y 1.5 #:size-z 0.6)"
          "build (stirling eng #:at (0 1.5 0) #:aperture 1 #:conductance 14 #:load 0 #:temperature 98.75)")
    (for/list ([x '(-2.4 -1.2 0 1.2 2.4 -2.4 -1.2 0 1.2 2.4)] [i (in-naturals 1)])
      (format "build (mirror m~a #:at (~a ~a ~a) #:area 1 #:onto eng)" i x (if (<= i 5) 0.7 1.3) (if (<= i 5) -3.2 -4.7)))
    ;; the crankshaft: along X, 0.3 m over the engine's foot (1.5 + 0.3 = 1.8), the 72 beside the flywheel, the 18 on its pitch circle
    (list "build (wheel wheel-a #:catalogue involute-gear-m5-72 #:at (0.6 1.8 0))"
          "build (set wheel-a #:axis x)"
          "build (arbor eng wheel-a)"
          "build (wheel pinion-b #:catalogue involute-gear-m5-18 #:at (0.6 1.8 0.225))"
          "build (set pinion-b #:axis x)"
          "build (mesh wheel-a pinion-b)"
          "build done"
          "join built-1.pinion-b motors.rotor"
          "join motors.motor battery-bank.bank"
          "waitsim 300"))
   "; "))

(when (godot-available?)
  (define env (environment-variables-copy (current-environment-variables)))
  (for ([kv `(("HEROIC_WORLD" . "lonely-rover-e2e") ("HEROIC_INPUT" . ,(string-append "wait 360; " build-steps))
              ("HEROIC_SET" . "battery-bank:cells temperature 25") ("HEROIC_QUIT_AFTER_SIM_SECONDS" . "300")
              ("HEROIC_TRACE" . ,trace) ("HEROIC_TRACE_DT" . "1"))])
    (environment-variables-set! env (string->bytes/utf-8 (car kv)) (string->bytes/utf-8 (cdr kv))))
  (define lines
    (string-split
     (parameterize ([current-directory game-dir] [current-environment-variables env])
       (with-output-to-string (λ () (system* godot-binary "--headless" "--fixed-fps" "120" "--path" "."))))
     "\n"))
  (define (has? rx) (for/or ([l lines]) (regexp-match? rx l)))
  (define engine (trace-frames (string-append trace ".built-1")))
  (define motor (trace-frames (string-append trace ".motors")))
  (define links (trace-frames (string-append trace ".links")))

  (test-case "the engine on its arbor builds and drives its train; the pinion is joined to the found motor"
    (check-false (has? #rx"KeyNotFoundException") "the view builds the arbor (before #269 it looked for a Jolt body named eng)")
    (check-false (has? #rx"BuildMode\\] error") "no command was refused")
    (check-false (has? #rx"\\[gears\\].*unfinished") "the gear train is built")
    (check-true (has? #rx"\\[BuildMode\\] train: eng -> pinion-b, ratio 4:1 \\(1 mesh\\)") "build mode's live ratio")
    (check-true (has? #rx"^\\[links\\] joined: shaft shaft-1 from built-1.pinion-b to motors.rotor$"))
    (check-true (has? #rx"^\\[links\\] joined: wire wire-1 from motors.motor to battery-bank.bank$")))

  (test-case "the engine turns the 72 at its own speed and the 18 at four times it; the rotor settles at the worked 1,606 rpm, the engine at 401.5"
    (define e (last (from engine 150)))
    (define m (last (from motor 150)))
    (printf "STIRLING GEAR (~a s): engine ~a rpm at ~a W; wheel-a ~a rpm, pinion-b ~a rpm (x~a); rotor ~a rpm; charge ~a W\n"
            (car e) (at e 'eng.rpm) (at e 'eng.shaft-power) (rpm (at e 'wheel-a.omega)) (rpm (at e 'pinion-b.omega))
            (/ (at e 'pinion-b.omega) (at e 'wheel-a.omega)) (at m 'motor.rpm) (at (last links) 'wire-1.power))
    (for ([f (from engine 150)])
      (define w (* (at f 'eng.rpm) (/ (* 2 pi) 60)))
      (check-= (at f 'wheel-a.omega) w (* 0.01 w) (format "the 72 turns with the engine, at ~a s" (car f)))
      (check-= (at f 'pinion-b.omega) (* 4 (at f 'wheel-a.omega)) (* 0.01 (at f 'pinion-b.omega)) "the 18 at 72/18 = 4 times the 72")
      (check-= (at f 'eng.rpm) 401.5 5 "rpm: the rotor's 1,606 over the stage's 4")
      (check-= (at f 'eng.shaft-power) 214.0 2 "W: the engine at its settled noon balance (#220)"))
    (for ([f (from motor 150)] [l (from links 150)])
      (check-= (at f 'motor.rpm) 1606 15 (format "rotor rpm at ~a s: the same as on #220's plain shaft" (car f)))
      (check-= (at l 'wire-1.power) 171 3 "W of charge: 0.8 of the engine's 214 W (the meshes and the shaft lose nothing)")
      (check-= (at f 'spin-limit.hits) 0 0))))
