#lang racket/base
;; A client for HeroicInventions.SimHost (src/HeroicInventions.SimHost):
;; runs a compiled .machine file headlessly — no Godot, no rendering — and
;; returns sampled telemetry, so rackunit can assert on real simulated
;; behaviour (a jet rising, a rotor spinning up) instead of hand-computed
;; expected values. See docs/design.html §III "Machines as tests".
;;
;; This only reaches parts MachineRuntime itself simulates: tanks,
;; boilers, rotors and pipes. Blocks, levers, pendulums and ramps are pure
;; Jolt rigid-body physics and only exist inside the Godot game; they stay
;; covered by the HEROIC_AUTORUN headless-Godot smoke test instead.
(require racket/system racket/port racket/runtime-path racket/path
         racket/string racket/list)
(provide simulate max-of min-of final-of)

(define-runtime-path repo "../..")
(define machines-dir (build-path repo "game" "machines"))
(define simhost-dir (build-path repo "src" "HeroicInventions.SimHost"))
(define simhost-csproj (build-path simhost-dir "HeroicInventions.SimHost.csproj"))

(struct link (out in) #:mutable) ; out: the host's stdout (we read); in: its stdin (we write)
(define current-link (make-parameter #f))

;; dotnet build's fixed layout: bin/<Configuration>/<TFM>/<AssemblyName>.dll
(define (built-dll-path)
  (define config-dir (build-path simhost-dir "bin" "Release"))
  (and (directory-exists? config-dir)
       (for/or ([tfm (in-list (sort (directory-list config-dir) path<?))])
         (define dll (build-path config-dir tfm "HeroicInventions.SimHost.dll"))
         (and (file-exists? dll) dll))))

(define (ensure-built!)
  (unless (built-dll-path)
    (define dotnet (or (find-executable-path "dotnet")
                        (error 'simhost "dotnet not found on PATH")))
    (define ok? (parameterize ([current-output-port (open-output-nowhere)])
                  (system* dotnet "build" (path->string simhost-csproj) "-c" "Release" "-v" "quiet")))
    (unless (and ok? (built-dll-path))
      (error 'simhost "dotnet build failed for ~a" simhost-csproj))))

(define (start-link!)
  (ensure-built!)
  (define-values (subproc out in err)
    (subprocess #f #f #f (find-executable-path "dotnet") (path->string (built-dll-path))))
  (file-stream-buffer-mode in 'line)
  (define l (link out in))
  (current-link l)
  l)

(define (ensure-link!) (or (current-link) (start-link!)))

(define (send-command! form)
  (define l (ensure-link!))
  (write form (link-in l))
  (newline (link-in l))
  (flush-output (link-in l))
  (read (link-out l)))

;; (simulate 'herons-fountain #:seconds 60) → an opaque `run`: a list of
;; frames, each (time (target.field value) ...). The real physics steps
;; every #:step seconds; a frame is recorded only every #:sample-dt
;; seconds, so a long or fine-grained run doesn't have to send back one
;; line per physics tick. #:set gives fields a value before the first step,
;; standing in for what the game's engine side supplies: '((lift rpm 12)).
(define (simulate machine-name #:seconds seconds #:step [step 0.01] #:sample-dt [sample-dt step]
                  #:set [settings '()])
  (define path (build-path machines-dir (format "~a.machine" machine-name)))
  (unless (file-exists? path)
    (error 'simulate "no such machine file: ~a (run `racket racket/build.rkt` first?)" path))
  (define reply
    (send-command! (append (list 'simulate (path->string path)
                                 (exact->inexact seconds) (exact->inexact step) (exact->inexact sample-dt))
                           (if (null? settings)
                               '()
                               (list (cons 'set (for/list ([s settings])
                                                  (list (car s) (cadr s) (exact->inexact (caddr s))))))))))
  (cond
    [(and (pair? reply) (eq? (car reply) 'run)) (cdr reply)]
    [(and (pair? reply) (eq? (car reply) 'error)) (error 'simulate "~a" (cadr reply))]
    [else (error 'simulate "unexpected reply from simhost: ~a" reply)]))

;; A field path like '(nozzle jet-height) names the same target.field the
;; live link and MachineRuntime.GetField use — joined here into the one
;; symbol a frame's entries are keyed by.
(define (field-key path) (string->symbol (string-join (map symbol->string path) ".")))

(define (values-of run path)
  (define key (field-key path))
  (for*/list ([frame (in-list run)]
              [entry (in-list (cdr frame))]
              #:when (eq? (car entry) key))
    (cadr entry)))

(define (checked who run path)
  (define vs (values-of run path))
  (when (null? vs) (error who "field ~a never appeared in this run" path))
  vs)

(define (max-of run path) (apply max (checked 'max-of run path)))
(define (min-of run path) (apply min (checked 'min-of run path)))
(define (final-of run path) (last (checked 'final-of run path)))
