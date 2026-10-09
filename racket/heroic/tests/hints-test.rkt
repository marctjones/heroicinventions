#lang racket/base
;; Issue #98: the first-run hints of the rover game (game/scripts/Hints.cs). Each case runs lonely-rover-opening headless at 120 Hz with
;; HEROIC_HINTS=1 and HEROIC_HINTS_RATE=30 (the hints' waiting clocks run 30 times faster) and reads the "[hints] showing ID" lines.
;; Worked beforehand (the waits are in held seconds, counted only while no page is up and no other hint is showing; a hint stays up 40 s):
;;   nothing done  drive at 20 s, speed at 150 + 40, log at 270 + 80, build at 390 + 120 (= 510 s, 8.5 min): in that order, each once.
;;   doing things  an up arrow held 1 s drives (no drive hint); the log opened (no log hint); the game started at 5x (no speed hint):
;;                 only the build hint is left.
;;   hints off     HEROIC_HINTS=0: none.
;; Skipped when Godot is not installed.
(require rackunit racket/system racket/port racket/string racket/list racket/file racket/runtime-path
         (only-in heroic/godothost godot-available? godot-binary))

(define-runtime-path game-dir "../../../game")

(define (hints-shown script #:env [extra '()])
  (define settings (make-temporary-file "hints~a.cfg"))
  (delete-file settings)   ; a fresh player: nothing retired yet
  (define env (environment-variables-copy (current-environment-variables)))
  (for ([kv (append `(("HEROIC_WORLD" . "lonely-rover-opening") ("HEROIC_HINTS" . "1") ("HEROIC_HINTS_RATE" . "30")
                      ("HEROIC_SETTINGS" . ,(path->string settings)) ("HEROIC_INPUT" . ,script))
                    extra)])
    (environment-variables-set! env (string->bytes/utf-8 (car kv)) (string->bytes/utf-8 (cdr kv))))
  (define out
    (parameterize ([current-directory game-dir] [current-environment-variables env])
      (with-output-to-string
        (λ () (system* godot-binary "--headless" "--fixed-fps" "120" "--path" ".")))))
  (when (file-exists? settings) (delete-file settings))
  (for/list ([l (string-split out "\n")] #:when (string-prefix? l "[hints] showing "))
    (substring l (string-length "[hints] showing "))))

(define (rover-only ids) (filter (λ (id) (string-prefix? id "rover-")) ids))

(when (godot-available?)
  (test-case "nothing done: the four pointers come in order, each once, and no sandbox hint among them"
    (define shown (hints-shown "frontend skip; wait 4000; quit"))
    (check-equal? (rover-only shown) '("rover-drive" "rover-speed" "rover-log" "rover-build"))
    (check-false (member "camera" shown))
    (check-false (member "speed" shown)))
  (test-case "driving, reading the log and playing at speed retire their pointers: only the build one is left"
    (define shown (hints-shown "frontend skip; hold up 1; wait 130; frontend log; wait 20; frontend log; wait 4000; quit"
                               #:env '(("HEROIC_SPEED" . "5"))))
    (check-equal? (rover-only shown) '("rover-build")))
  (test-case "with hints off, none appears"
    (check-equal? (hints-shown "frontend skip; wait 4000; quit" #:env '(("HEROIC_HINTS" . "0"))) '())))
