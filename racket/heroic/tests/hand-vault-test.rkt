#lang racket/base
;; Issue #213: a vault built by hand round the found bank where it lies under about 1 m of rubble (lonely-rover-opening: the crate at
;; (254.5, 137.9), 1.04 m of cover), by the game's own build path (scripted build steps, which run build mode's commands as its console
;; does), headless Godot at 120 Hz. What it checks:
;;  - build mode places a room below the ground: "build new 254.5 137.9" opens on the surface there (y = the rubble's top), a room with
;;    #:at (0 -1.54 0) has its floor 1.54 m down, and its box (0.5 m) holds the crate's middle (1.04 m of cover + 0.25 m) at y -1.29:
;;    the log says "[zones] battery-bank.cells joined built-1.vault";
;;  - on Mars (the planet's air is -63 C, below freezing) an enclosure placed with no #:wall is a regolith vault: the command build mode
;;    runs ends "#:wall regolith #:insulation 0" (the DSL's rule: with a wall the fixed 2 W/K insulation is 0 unless given), while a
;;    #:wall #f puts the 2 W/K back and a wall again takes it off (the player can change it, nothing is forbidden);
;;  - the vault works as the route's does. Worked before running (vault-zone-test.rkt, night-heat's tight vault): a 0.5 m cavity in a 0.5 m
;;    regolith wall at -55 C, 40 kg of basalt at 200 C in an open bin leaking 0.1 W/K, the cells at -55 C: the bank is +4.28 C after 10 local
;;    hours (36,990 s). Without the insulation rule the same hand-built room leaks 2 W/K into the -63 C night and the bank is -27 C.
;; Skipped when Godot is not installed.
(require rackunit racket/system racket/port racket/string racket/list racket/file racket/runtime-path
         (only-in heroic/godothost godot-available? godot-binary))

(define-runtime-path game-dir "../../../game")

(define (run-game script env-list)
  (define env (environment-variables-copy (current-environment-variables)))
  (for ([kv (append '(("HEROIC_WORLD" . "lonely-rover-opening") ("HEROIC_HINTS" . "0")) env-list)])
    (environment-variables-set! env (string->bytes/utf-8 (car kv)) (string->bytes/utf-8 (cdr kv))))
  (environment-variables-set! env #"HEROIC_INPUT" (string->bytes/utf-8 script))
  (string-split
   (parameterize ([current-directory game-dir] [current-environment-variables env])
     (with-output-to-string (λ () (system* godot-binary "--headless" "--fixed-fps" "120" "--path" "."))))
   "\n"))

(define (last-field path key)
  (define l (last (file->lines path)))
  (string->number (cadr (regexp-match (pregexp (string-append "\\(" (regexp-quote key) " (-?[0-9.eE+-]+)\\)")) l))))
(define (has? rx ls) (for/or ([l ls]) (regexp-match? rx l)))

(when (godot-available?)
  (define dir (make-temporary-file "hand-vault~a" 'directory))
  (define (p f) (path->string (build-path dir f)))
  (define common `(("HEROIC_SAVES_DIR" . ,(p "saves"))))
  (define stand "waitsim 8; rover place 246.5 137.9 270; wait 60; build new; wait 30; ")
  (define room "build (enclosure vault #:at (0 -1.54 0) #:size-x 0.5 #:size-y 0.5 #:size-z 0.5 #:pressure 610 #:temperature -55 #:ground -55); ")
  (define results (make-vector 2 #f))
  (define (job i script env) (thread (λ () (vector-set! results i (run-game script (append common env))))))
  (for-each thread-wait
            (list
             ;; the night: room, 40 kg of rock at 200 C in an open bin, the cells at -55 C, a sleep to 03:00 after the build
             (job 0 (string-append stand room
                                   "build (heat-store rock #:at (0.12 -1.54 0) #:mass 40 #:contents basalt #:temperature 200); "
                                   "build (heat-bin bin #:at (0.12 -1.54 0) #:holds rock #:open 1 #:leak 0.1); "
                                   "wait 60; build done; wait 30; sleep until scene.elapsed above 37000 limit 40000 paused; wait 2000")
                  `(("HEROIC_SET" . "battery-bank:cells temperature -55 0") ("HEROIC_TRACE" . ,(p "n")) ("HEROIC_TRACE_DT" . "600")
                    ("HEROIC_QUIT_AFTER_SIM_SECONDS" . "37020")))
             ;; the defaults and their changes: a room with no #:wall, the wall taken off, put back as another material
             (job 1 (string-append stand
                                   "build (enclosure vault #:at (0 -1.54 0) #:size-x 0.5 #:size-y 0.5 #:size-z 0.5); wait 30; "
                                   "build (set vault #:wall #f); wait 30; build (set vault #:wall granite); wait 30; build done; wait 120")
                  `(("HEROIC_TRACE" . ,(p "d")) ("HEROIC_TRACE_DT" . "1") ("HEROIC_QUIT_AFTER_SIM_SECONDS" . "14")))))
  (define night (vector-ref results 0))
  (define defaults (vector-ref results 1))

  (test-case "build mode places a room 1.54 m down and it holds the buried crate's middle"
    (check-true (has? #rx"^\\[build\\] new machine built-1 at \\(254\\.4[0-9] -4[0-9]\\.[0-9]+ 137\\.[89][0-9]\\)" night) (string-join night "\n"))
    (check-true (has? #rx"^\\[zones\\] battery-bank.cells joined built-1.vault" night)))

  (test-case "on Mars a room placed with no wall gets a regolith wall and no fixed insulation; the player can change both"
    (check-true (has? #rx"\\[BuildMode\\] > \\(enclosure vault #:at \\([^)]*\\) #:size-x 0.5 #:size-y 0.5 #:size-z 0.5 #:wall regolith #:insulation 0\\)" defaults)
                (string-join defaults "\n"))
    (check-true (has? #rx"\\[BuildMode\\] > \\(set vault #:insulation 2\\)" defaults) "wall off: the 2 W/K of the template is back")
    (check-true (has? #rx"\\[BuildMode\\] > \\(set vault #:insulation 0\\)" defaults) "a wall again: the insulation is the wall's")
    (check-true (has? #rx"\\[BuildMode\\] > \\(set vault #:wall granite\\)" defaults)))

  (test-case "the hand-built vault keeps the found bank warm as the route's does: +4.28 C at 03:00"
    (define t (last-field (p "n.battery-bank") "cells.temperature"))
    (define rock (last-field (p "n.built-1") "rock.temperature"))
    (define ua (last-field (p "n.built-1") "vault.insulation"))
    (printf "HAND VAULT: at 03:00 the found bank's cells are ~a C (vault-zone-test: +4.28), the rock ~a C, insulation ~a W/K\n" t rock ua)
    (check-= ua 0 1e-9)
    (check-= t 4.28 0.3 "C: vault-zone-test's night, the same network")
    (check-true (< 0 t 45) "the bank may charge")))
