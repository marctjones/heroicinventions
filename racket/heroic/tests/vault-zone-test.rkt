#lang racket/base
;; Issue #211: a vault in one machine holds the found bank's cells, a heat store of another, when the crate is pushed into it; pushed out,
;; they leave. Run in the game (headless Godot, Jolt at 120 Hz) in game/worlds/vault-check.world: vault-night (night-heat's tight vault and
;; its 40 kg of rock at 200 C, lid open, from 17:00) at the origin, and found-bank (the crate, its cells and bank) 1.5 m beside it. A hand
;; pushes the crate (a machine run may operate anything; HEROIC_ROVER=0). Skipped when Godot is not installed.
;; Worked before running:
;;  - pushed in at 17:00 with the cells at -55 C, the vault's network is night-heat's tight vault exactly (the same cavity, wall, rock,
;;    lid and 16 kg of cells), so at 03:00 (36,990 s) the bank is night-heat's +4.3 C (its trace; the lumped model -1.3);
;;  - the containment is a matter of where the crate is, so a save made with the crate in the vault and loaded puts the cells back in it;
;;  - pushed out at 20 C, the cells cool in their own machine's air by radiation alone: 0.69 W/K x 83 K / 16 kJ/K = 3.6 mK/s (found-bank-test);
;;  - never pushed in, they sit in the open at -55 C against the found bank's -63 C air all night, and stay under -55.
(require rackunit racket/list racket/system racket/port racket/string racket/file racket/runtime-path
         (only-in heroic/godothost godot-available? godot-binary))

(define-runtime-path game-dir "../../../game")

(define (at frame key) (cadr (assq key (cdr frame))))
(define night 36990.0)   ; 03:00 after a 17:00 start: 10 local hours of 3,699 s

(define (run-game env-list)
  (define env (environment-variables-copy (current-environment-variables)))
  (for ([kv (cons '("HEROIC_WORLD" . "vault-check") (cons '("HEROIC_ROVER" . "0") env-list))])
    (environment-variables-set! env (string->bytes/utf-8 (car kv)) (string->bytes/utf-8 (cdr kv))))
  (string-split
   (parameterize ([current-directory game-dir] [current-environment-variables env])
     (with-output-to-string (λ () (system* godot-binary "--headless" "--fixed-fps" "120" "--path" "."))))
   "\n"))

(define (trace-frames path) (if (file-exists? path) (call-with-input-file path (λ (in) (for/list ([f (in-port read in)]) f))) '()))
(define (has? rx ls) (for/or ([l ls]) (regexp-match? rx l)))
(define (after rx ls) (let ([m (memf (λ (l) (regexp-match? rx l)) ls)]) (if m (cdr m) '())))

(define dir (make-temporary-file "vault-zone~a" 'directory))
(define (p f) (path->string (build-path dir f)))
(define in-save (p "in.save"))

(when (godot-available?)
  ;; the crate pushed into the vault at 3-5 s (its centre from x 1.5 to about 0.1), the world saved at 8 s
  (define pushed
    (run-game `(("HEROIC_SET" . "bank:cells temperature -55 0")
                ("HEROIC_DRAG" . "drag crate 0 0.25 0 at 3; to 0 2.81 0 at 4.5; release at 5.5")
                ("HEROIC_SAVE" . ,in-save) ("HEROIC_SAVE_AT" . "8") ("HEROIC_QUIT_AFTER_SIM_SECONDS" . "9")
                ("HEROIC_TRACE" . ,(p "a")) ("HEROIC_TRACE_DT" . "1"))))
  (define pushed-run (trace-frames (p "a.bank")))

  ;; then three runs at once: the save loaded and slept to 03:00; the save loaded and the crate pushed out; the world with no push, slept
  (define results (make-vector 3 #f))
  (define (job i env trace) (thread (λ () (vector-set! results i (list (run-game (append env `(("HEROIC_TRACE" . ,(p trace))))) trace)))))
  (for-each thread-wait
            (list (job 0 `(("HEROIC_LOAD" . ,in-save) ("HEROIC_SLEEP" . "night") ("HEROIC_QUIT_AFTER_SIM_SECONDS" . "36995") ("HEROIC_TRACE_DT" . "600")) "b")
                  (job 1 `(("HEROIC_LOAD" . ,in-save) ("HEROIC_SET" . "bank:cells temperature 20")
                           ("HEROIC_DRAG" . "drag crate 0 0.25 0 at 10; to 1.5 2.81 0 at 11.5; release at 12.5")
                           ("HEROIC_QUIT_AFTER_SIM_SECONDS" . "22") ("HEROIC_TRACE_DT" . "1")) "c")
                  (job 2 `(("HEROIC_SET" . "bank:cells temperature -55 0") ("HEROIC_SLEEP" . "night")
                           ("HEROIC_QUIT_AFTER_SIM_SECONDS" . "36995") ("HEROIC_TRACE_DT" . "600")) "d")))
  (define (lines i) (first (vector-ref results i)))
  (define (frames i suffix) (trace-frames (p (string-append (second (vector-ref results i)) "." suffix))))

  (test-case "pushed into the vault, the found bank's cells join its air: the crate's centre is inside the 0.5 m box"
    (check-true (has? #rx"^\\[zones\\] bank.cells joined vault.vault at -55.00 C" pushed) (string-join pushed "\n"))
    (define f (last pushed-run))
    (check-true (< (abs (at f 'crate.x)) 0.25) (format "crate at x ~a" (at f 'crate.x)))
    (check-true (< (abs (at (first pushed-run) 'crate.x) ) 2) "and it started beside it")
    (check-true (> (at (first pushed-run) 'crate.x) 0.25) "outside the box"))

  (test-case "saved and loaded with the crate in the vault, the cells are held again, and at 03:00 the bank is night-heat's +4.3 C"
    (define ls (lines 0))
    (check-true (has? #rx"^\\[save\\] loaded vault-check" ls))
    (check-true (has? #rx"^\\[zones\\] bank.cells joined vault.vault" (after #rx"^\\[save\\] loaded" ls)) "held again after the load")
    (check-true (has? #rx"^\\[sleep\\] woke after [0-9.]+ s: scene.elapsed" ls))
    (define bank (last (frames 0 "bank")))
    (define vault (last (frames 0 "vault")))
    (check-true (>= (car bank) night) (format "the last frame is at ~a s" (car bank)))
    (check-true (<= 3.0 (at vault 'scene.time) 3.01) (format "the vault's clock: ~a h" (at vault 'scene.time)))
    (printf "VAULT ZONE: at 03:00 the found bank's cells are ~a C (night-heat's tight-bank: +4.3), the rock ~a C, the vault ~a C\n"
            (at bank 'cells.temperature) (at vault 'rock.temperature) (at vault 'vault.temperature))
    (check-= (at bank 'cells.temperature) 4.3 1.0 "C: night-heat's trace +4.3 (the same network)")
    (check-true (< 0 (at bank 'cells.temperature) 45) "the bank may charge"))

  (test-case "pushed out again, the cells leave the vault and cool in the open as found-bank-test's do: 3.6 mK/s at 20 C"
    (define ls (lines 1))
    (check-true (has? #rx"^\\[zones\\] bank.cells left vault.vault at 19.9[0-9] C" ls) (string-join ls "\n"))
    (define run (frames 1 "bank"))
    (define out (filter (λ (f) (and (>= (car f) 13.5) (> (at f 'crate.x) 0.5))) run))
    (check-true (>= (length out) 5) "frames with the crate out")
    (define rate (/ (- (at (first out) 'cells.temperature) (at (last out) 'cells.temperature)) (- (car (last out)) (car (first out)))))
    (check-= rate 0.0036 0.0003 "K/s lost to the found bank's own air")
    ;; and the cooling in the open is exactly the open-air law: G (T - T_air) / C
    (define f (last out))
    (check-= (* rate 16000) (* (at f 'cells.conductance) (- (at f 'cells.temperature) -63)) 0.5 "W"))

  (test-case "never pushed in, the cells stay in the open all night, under -55 C"
    (define ls (lines 2))
    (check-false (has? #rx"^\\[zones\\]" ls) "nothing joined")
    (define bank (last (frames 2 "bank")))
    (check-true (>= (car bank) night))
    (check-true (< -63 (at bank 'cells.temperature) -55) (format "cells ~a C" (at bank 'cells.temperature)))))
