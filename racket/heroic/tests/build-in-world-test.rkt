#lang racket/base
;; Issue #204: the player builds in the rover world (lonely-rover-opening), by the game's own build path (scripted build steps, which
;; run build mode's commands as its console and mouse do), headless Godot at 120 Hz with Jolt.
;;  - the found cargo can't be rebuilt: "build edit battery-bank" is refused;
;;  - while build mode is open the rover stands: the up arrow held for a second moves it under 5 cm (driving, it covers 2 m);
;;  - a new machine at (200, 130), 59 m below the rim on the crater floor: a windmill (the palette's: 10 m sails, 1500 kg, 6 m/s, Cp 0.3
;;    at a tip-speed ratio of 2.5) on a post, a three-stage train of 72:18 meshes (module 5 mm, 4:1 a stage, 64:1) and the salvaged motor
;;    on the last pinion. The motor has no bank to charge (the bank is found cargo, and a wire between machines is #208), so it is set
;;    aside unfinished, as build mode sets such a part aside, and the rest runs;
;;  - the world is saved with the machine in it, whole ((build built-1 ...), on Mars: the world's own planet, not the default Earth),
;;    then loaded: all nine parts are back (eight running), and it turns.
;; Worked numbers: on Mars's air (rho = 0.01518 kg/m3 at 610 Pa, -63 C) the sails' torque is tau = 1/2 rho A v^2 R (Cp/l*) (2 - w/w*)
;; = 102.9 (2 - w/1.5) N m, so from rest with I = m R^2 / 3 = 50,000 kg m2, w(t) = 3 (1 - e^(-t/729 s)): 0.237 rad/s at 60 s, 0.275 at 70
;; (the Jolt train adds 0.3% to the inertia). Every pinion turns at 4 times its wheel, the last at 64 times the sails.
;; Skipped when Godot is not installed.
(require rackunit racket/system racket/port racket/string racket/list racket/file racket/math racket/runtime-path
         (only-in heroic/godothost godot-available? godot-binary))

(define-runtime-path game-dir "../../../game")

(define (run-game script #:env [extra '()])
  (define env (environment-variables-copy (current-environment-variables)))
  (environment-variables-set! env #"HEROIC_WORLD" #"lonely-rover-opening")
  (environment-variables-set! env #"HEROIC_INPUT" (string->bytes/utf-8 script))
  (for ([kv extra]) (environment-variables-set! env (string->bytes/utf-8 (car kv)) (string->bytes/utf-8 (cdr kv))))
  (string-split
   (parameterize ([current-directory game-dir] [current-environment-variables env])
     (with-output-to-string (λ () (system* godot-binary "--headless" "--fixed-fps" "120" "--path" "."))))
   "\n"))

(define (rover-lines lines) (filter (λ (l) (string-prefix? l "[view] rover:")) lines))
(define (pos l) (map string->number (cdr (regexp-match #rx"at \\(([-0-9.]+) ([-0-9.]+) ([-0-9.]+)\\)" l))))
(define (frames path)   ; a trace: one hash of field -> number per frame
  (for/list ([l (in-list (file->lines path))] #:when (regexp-match? #rx"pinion-d\\.omega" l))
    (for/hash ([m (in-list (regexp-match* #px"\\(([a-z.-]+) (-?[0-9.eE+-]+)\\)" l #:match-select cdr))])
      (values (car m) (string->number (cadr m))))))

(define build-steps
  (string-join
   '("build (post tower #:at (0.45 0 -1.45) #:size-y 10.6 #:size-x 0.6 #:size-z 0.6)"
     "build (windmill sails #:at (0 11 0))"
     "build (wheel wheel-a #:catalogue involute-gear-m5-72 #:at (0 11 -1.3))" "build (arbor sails wheel-a)"
     "build (wheel pinion-b #:catalogue involute-gear-m5-18 #:at (0.225 11 -1.3))" "build (mesh wheel-a pinion-b)"
     "build (wheel wheel-b #:catalogue involute-gear-m5-72 #:at (0.225 11 -1.4))" "build (arbor pinion-b wheel-b)"
     "build (wheel pinion-c #:catalogue involute-gear-m5-18 #:at (0.45 11 -1.4))" "build (mesh wheel-b pinion-c)"
     "build (wheel wheel-c #:catalogue involute-gear-m5-72 #:at (0.45 11 -1.5))" "build (arbor pinion-c wheel-c)"
     "build (wheel pinion-d #:catalogue involute-gear-m5-18 #:at (0.675 11 -1.5))" "build (mesh wheel-c pinion-d)"
     "build (generator motor #:at (0.675 11 -1.75))" "build (set motor #:on pinion-d)")
   "; "))

(when (godot-available?)
  (define dir (make-temporary-file "build-in-world~a" 'directory))
  (define (p f) (path->string (build-path dir f)))
  (define built
    (run-game (string-append "wait 360; build edit battery-bank; build new 200 130; rover; hold up 1; wait 30; rover; "
                             build-steps "; build done; build list; waitsim 60; rover save " (p "a.save") "; quit")))
  (define loaded
    (run-game (string-append "wait 1200; build list; rover save " (p "b.save") "; quit")
              #:env `(("HEROIC_LOAD" . ,(p "a.save")) ("HEROIC_TRACE" . ,(p "t")) ("HEROIC_TRACE_DT" . "0.5"))))

  (test-case "the found cargo can't be rebuilt; a new machine can be started anywhere"
    (check-true (for/or ([l built]) (regexp-match? #rx"^\\[build\\] refused: battery-bank is found cargo" l)))
    (check-false (for/or ([l built]) (regexp-match? #rx"^\\[build\\] editing battery-bank" l)))
    (check-true (for/or ([l built]) (regexp-match? #rx"^\\[build\\] placed built-1 at \\(200.00 -59.[0-9]+ 130.00\\)" l))))

  (test-case "the rover stands while build mode has the keys"
    (define rs (rover-lines built))
    (define-values (a b) (values (pos (first rs)) (pos (second rs))))
    (check-true (< (sqrt (+ (sqr (- (first a) (first b))) (sqr (- (third a) (third b))))) 0.05) (format "the rover moved from ~a to ~a" a b)))

  (test-case "the build: nine parts, the motor set aside for want of a bank, the rest running"
    (define l (for/first ([l built] #:when (string-prefix? l "[build] built-1: design")) l))
    (check-not-false l)
    (check-regexp-match #rx"sails:windmill" l)
    (check-regexp-match #rx"motor:generator" l)
    (check-regexp-match #rx"running 8 parts; unfinished motor$" l)
    (check-false (for/or ([l built]) (regexp-match? #rx"BuildMode\\] error" l)) "no command was refused"))

  (test-case "the save holds the machine whole, on Mars"
    (define s (file->string (p "a.save")))
    (check-regexp-match #rx"\n  \\(build built-1 \\(at 200.0 0.0 130.0\\) \\(machine built-1 [^\n]*\\(planet mars " s)
    (check-regexp-match #rx"\\(part motor generator " s)
    (check-regexp-match #rx"\n  \\(machine built-1 built-1 " s))

  (test-case "loaded, it is all there and turning: 64 to 1, at the worked speed"
    (check-true (for/or ([l loaded]) (regexp-match? #rx"^\\[build\\] loaded built-1: 8 of 9 parts running" l)))
    (check-false (for/or ([l loaded]) (regexp-match? #rx"found nothing to set" l)) "every entry of the save found its place")
    (check-true (for/or ([l loaded]) (regexp-match? #rx"^\\[build\\] built-1: design .*running 8 parts; unfinished motor$" l)))
    (define fs (frames (p "t.built-1")))
    (check-true (> (length fs) 15) (format "~a frames" (length fs)))
    (define t0 (hash-ref (first fs) "scene.elapsed"))
    (check-true (> t0 55) (format "the machine's clock carried on from the save: ~a s" t0))
    (for ([f fs])
      (define t (hash-ref f "scene.elapsed"))
      (define sails (* (hash-ref f "sails.rpm") (/ pi 30)))
      (define a (hash-ref f "wheel-a.omega"))
      (for ([pair '(("pinion-b" "wheel-a") ("pinion-c" "wheel-b") ("pinion-d" "wheel-c"))])
        (check-= (/ (hash-ref f (string-append (car pair) ".omega")) (hash-ref f (string-append (cadr pair) ".omega"))) 4 0.02
                 (format "~a over ~a at ~a s" (car pair) (cadr pair) t)))
      (check-= (/ (hash-ref f "pinion-d.omega") sails) 64 (if (< t (+ t0 0.5)) 7 0.5) (format "the last pinion over the sails at ~a s" t))
      (check-= (/ sails (* 3 (- 1 (exp (/ (- t) 729))))) 1 0.01 (format "the sails at ~a s: ~a rad/s" t sails)))
    (check-true (> (hash-ref (last fs) "sails.rpm") (hash-ref (first fs) "sails.rpm")) "still speeding up"))

  (test-case "a save after the load still holds the build"
    (check-regexp-match #rx"\n  \\(build built-1 " (file->string (p "b.save")))))
