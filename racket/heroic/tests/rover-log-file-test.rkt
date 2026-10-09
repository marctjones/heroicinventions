#lang racket/base
;; Issue #217: the rover log is written to a file in the saves folder (game/scripts/Main.RoverLog.cs, WriteLogFile).
;; Predicted beforehand, for lonely-rover-opening with HEROIC_SAVES_DIR=<tmp> and a scripted wait:
;;   <tmp>/lonely-rover-opening.rover-log.txt exists with a "# rover log of lonely-rover-opening, session begun ..." header, then
;;   "sol 1 HH:MM t=T.Ts sol: The log begins on sol 1." (T small, the scene's clock), flushed before the game quits.
;;   A second run into the same folder appends a second header and a second "begins" line (older sessions are kept).
;;   Nothing else is written there by the log (the autosave is not made by a scripted run without a saves folder either).
;; Skipped when Godot is not installed.
(require rackunit racket/system racket/port racket/string racket/list racket/file racket/runtime-path
         (only-in heroic/godothost godot-available? godot-binary))

(define-runtime-path game-dir "../../../game")

(define (run-game! saves-dir script)
  (define env (environment-variables-copy (current-environment-variables)))
  (for ([kv `(("HEROIC_WORLD" . "lonely-rover-opening") ("HEROIC_SAVES_DIR" . ,(path->string saves-dir)) ("HEROIC_INPUT" . ,script))])
    (environment-variables-set! env (string->bytes/utf-8 (car kv)) (string->bytes/utf-8 (cdr kv))))
  (parameterize ([current-directory game-dir] [current-environment-variables env])
    (with-output-to-string (λ () (system* godot-binary "--headless" "--fixed-fps" "120" "--path" ".")))))

(when (godot-available?)
  (test-case "the log is written to the saves folder, with the scene's clock, and kept across sessions"
    (define dir (make-temporary-file "rover-log~a" 'directory))
    (define file (build-path dir "lonely-rover-opening.rover-log.txt"))
    (define out (run-game! dir "frontend skip; wait 600; quit"))
    (check-true (file-exists? file) "the log file is written")
    (define lines (file->lines file))
    (check-true (string-prefix? (first lines) "# rover log of lonely-rover-opening, session begun "))
    (define begins (filter (λ (l) (regexp-match? #px"^sol 1 \\d\\d:\\d\\d t=\\d+\\.\\ds sol: The log begins on sol 1\\.$" l)) lines))
    (check-equal? (length begins) 1 (format "lines: ~a" lines))
    ;; what the game printed is what the file holds
    (check-true (string-contains? out "[log] sol 1 "))
    (run-game! dir "frontend skip; wait 600; quit")
    (define again (file->lines file))
    (check-equal? (length (filter (λ (l) (string-prefix? l "# rover log of")) again)) 2 "a second session appends")
    (check-equal? (take again (length lines)) lines "the first session's lines are kept")
    (delete-directory/files dir)))
