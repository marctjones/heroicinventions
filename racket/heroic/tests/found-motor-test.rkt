#lang racket/base
;; The generator is found, not built (owner ruling 2026-10-09). In the game (the rover building) the parts list has neither the
;; generator nor the battery bank, and has the windmill; in the sandbox (the workshop's build mode) it has all three. The game's
;; crates hold a salvaged motor instead (racket/machines/found-motor.rkt, placed as `motors` in the three Lonely Rover worlds): it
;; is bare of rubble (the last two crates are), so the rover can reach it without digging; that it charges the bank when a shaft
;; turns it is e2e-route-test.rkt (the windmill's train on the motor's rotor, the wire to the bank, 90 W, the call at 03:00).
;; Predictions: in the game the three `palette-has` lines read no, no, yes; in the sandbox yes, yes, yes.
;; Skipped when Godot is not installed.
(require rackunit racket/system racket/port racket/string racket/runtime-path
         (only-in heroic/godothost godot-available? godot-binary))

(define-runtime-path game-dir "../../../game")

(define (run env-list args)
  (define env (environment-variables-copy (current-environment-variables)))
  (for ([kv env-list]) (environment-variables-set! env (string->bytes/utf-8 (car kv)) (string->bytes/utf-8 (cdr kv))))
  (string-split
   (parameterize ([current-directory game-dir] [current-environment-variables env])
     (with-output-to-string (λ () (apply system* godot-binary "--headless" args))))
   "\n"))

(define (answer lines key)
  (for/or ([l lines])
    (define m (regexp-match (regexp (string-append "^\\[BuildMode\\] palette-has " key " (yes|no)")) l))
    (and m (cadr m))))

(when (godot-available?)
  (define probe "palette-has generator; palette-has battery-bank; palette-has windmill")
  (test-case "in the game's build mode the generator and the bank are not in the parts list; the windmill is"
    (define lines (run `(("HEROIC_WORLD" . "lonely-rover-opening") ("HEROIC_INPUT" . "wait 360; build new 200 130; wait 60; quit")
                         ("HEROIC_EDITOR_INPUT" . ,(string-append "wait 1; " probe)))
                       '("--fixed-fps" "120" "--path" ".")))
    (check-true (and (member "[build] new machine built-1 at (200.00 -59.22 130.00)" lines) #t) "the game's build mode opened")
    (check-equal? (answer lines "generator") "no")
    (check-equal? (answer lines "battery-bank") "no")
    (check-equal? (answer lines "windmill") "yes")
    (check-true (for/or ([l lines]) (regexp-match? #rx"InGame True" l))))
  (test-case "in the sandbox's build mode the generator and the bank stay in the parts list"
    (define lines (run `(("HEROIC_EDITOR" . "1") ("HEROIC_EDITOR_INPUT" . ,(string-append probe "; quit"))) '("--path" ".")))
    (check-equal? (answer lines "generator") "yes")
    (check-equal? (answer lines "battery-bank") "yes")
    (check-equal? (answer lines "windmill") "yes")
    (check-true (for/or ([l lines]) (regexp-match? #rx"InGame False" l)))))
