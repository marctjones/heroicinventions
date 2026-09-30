#lang racket/base
;; Runs a machine in the real game, headless, and returns its trace in the
;; same shape heroic/simhost's `simulate` does -- a list of frames, each
;; (time (target.field value) ...) -- so rackunit checks Jolt-side behaviour
;; (bodies falling, swinging, colliding; ropes pulling) the way it checks
;; the C# sim, with the same final-of / max-of / min-of.
;;
;; A frame holds every sim field (as simhost's do) and, for each Jolt body,
;; x y z (its centre of mass), vx vy vz speed, rot-x rot-y rot-z (degrees),
;; omega (rad/s), angle (degrees turned about its hinge, for a hinged body),
;; contacts (bodies touching now) and hits (contacts begun so far); each
;; rope's tension, released and broken; and scene.kinetic, scene.potential
;; and scene.mechanical (J). See game/scripts/MachineView.Trace.cs.
;;
;; Godot runs with --fixed-fps at its own physics rate, so the run steps
;; exactly as in play but as fast as the machine allows, not in real time.
(require racket/system racket/port racket/runtime-path racket/file racket/string
         "simhost.rkt")
(provide godot-simulate godot-simulate-world godot-available? godot-binary
         max-of min-of final-of values-of times-of)

(define-runtime-path game-dir "../../game")
(define godot-binary
  (or (getenv "HEROIC_GODOT")
      "/Applications/Godot_mono.app/Contents/MacOS/Godot"))
(define (godot-available?) (file-exists? godot-binary))

(define physics-ticks-per-second 120) ; game/project.godot, physics/common/physics_ticks_per_second

;; (godot-simulate 'trebuchet #:seconds 10 #:sample-dt 0.5) -> a run.
;; #:set gives sim fields a value before the first step: '((tap opening 0.04)).
(define (godot-simulate machine-name #:seconds seconds #:sample-dt [sample-dt 0.1]
                        #:set [settings '()])
  (unless (godot-available?)
    (error 'godot-simulate "Godot not found at ~a (set HEROIC_GODOT)" godot-binary))
  (unless (file-exists? (build-path game-dir "machines" (format "~a.machine" machine-name)))
    (error 'godot-simulate "no such machine file: ~a.machine (run `racket racket/build.rkt` first?)" machine-name))
  (define trace (make-temporary-file "heroic-~a.trace"))
  (define env (environment-variables-copy (current-environment-variables)))
  (define (env! k v) (environment-variables-set! env (string->bytes/utf-8 k) (string->bytes/utf-8 v)))
  (env! "HEROIC_AUTORUN" "1")
  (env! "HEROIC_AUTOSELECT" (format "~a" machine-name))
  (env! "HEROIC_QUIT_AFTER_SIM_SECONDS" (number->string (exact->inexact seconds)))
  (env! "HEROIC_TRACE" (path->string trace))
  (env! "HEROIC_TRACE_DT" (number->string (exact->inexact sample-dt)))
  (env! "HEROIC_SET" (string-join (for/list ([s settings])
                                    (format "~a ~a ~a" (car s) (cadr s) (exact->inexact (caddr s))))
                                  "; "))
  (define errors (open-output-string))
  (define ok?
    (parameterize ([current-directory game-dir]
                   [current-environment-variables env]
                   [current-output-port (open-output-nowhere)]
                   [current-error-port errors])
      (system* godot-binary "--headless" "--fixed-fps" (number->string physics-ticks-per-second) ".")))
  (define frames (call-with-input-file trace (λ (in) (for/list ([f (in-port read in)]) f))))
  (delete-file trace)
  (when (null? frames)
    (error 'godot-simulate "~a produced no trace~a~a" machine-name (if ok? "" " (Godot failed)")
           (let ([e (get-output-string errors)]) (if (string=? e "") "" (string-append ":\n" e)))))
  frames)

;; The times of a run's frames, in order.
(define (times-of run) (map car run))

;; Runs a world (game/worlds/<name>.world, or 'gallery for every machine)
;; headless and returns each placed machine's trace, by placement label:
;; (hash label frames ...). Traces come from the game's own per-machine
;; files, <trace>.<label>.
(define (godot-simulate-world world-name #:seconds seconds #:sample-dt [sample-dt 0.1] #:env [extra-env '()])
  (unless (godot-available?)
    (error 'godot-simulate-world "Godot not found at ~a (set HEROIC_GODOT)" godot-binary))
  (define world-file (build-path game-dir "worlds" (format "~a.world" world-name)))
  (define labels
    (for/list ([form (cddr (call-with-input-file world-file read))]
               #:when (and (pair? form) (eq? (car form) 'place)))
      (cadr form)))
  (define trace (make-temporary-file "heroic-world-~a.trace"))
  (define env (environment-variables-copy (current-environment-variables)))
  (define (env! k v) (environment-variables-set! env (string->bytes/utf-8 k) (string->bytes/utf-8 v)))
  (env! "HEROIC_WORLD" (format "~a" world-name))
  (env! "HEROIC_QUIT_AFTER_SIM_SECONDS" (number->string (exact->inexact seconds)))
  (env! "HEROIC_TRACE" (path->string trace))
  (env! "HEROIC_TRACE_DT" (number->string (exact->inexact sample-dt)))
  (for ([kv extra-env]) (env! (car kv) (cdr kv)))   ; e.g. HEROIC_LIVE_EDIT_AFTER, HEROIC_EDITOR_INPUT
  (parameterize ([current-directory game-dir]
                 [current-environment-variables env]
                 [current-output-port (open-output-nowhere)]
                 [current-error-port (open-output-nowhere)])
    (system* godot-binary "--headless" "--fixed-fps" (number->string physics-ticks-per-second) "."))
  (for/hash ([label labels])
    (define file (format "~a.~a" (path->string trace) label))
    (values label
            (if (file-exists? file)
                (begin0 (call-with-input-file file (λ (in) (for/list ([f (in-port read in)]) f)))
                        (delete-file file))
                '()))))
