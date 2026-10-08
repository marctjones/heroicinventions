#lang racket/base
;; Lesson 1, the see-saw (#166), played through in the real game, headless:
;; the palette picks, clicks and the drag go through Godot's own input
;; pipeline (game/scripts/ScriptedInput.cs), the two Tests run the design in
;; Jolt, and the lesson prints each step as it completes ("[Lesson] ...").
;;
;; Worked by hand (game/lessons/01-see-saw.lesson): a 10 cm granite cube is
;; 2.7 kg, an oak one 0.72 kg. At 0.2 m each side the granite turns 0.54
;; against 0.144, so the beam tips to its 18° stop; the oak balances it at
;; 0.54 / 0.72 = 0.75 m, and there the beam's traced tilt must stay within 3°.
;; Skipped where Godot isn't installed (set HEROIC_GODOT).
(require rackunit racket/port racket/string racket/list racket/file heroic/godothost)

(define-values (game-dir) (simplify-path (build-path (collection-file-path "godothost.rkt" "heroic") 'up 'up 'up "game")))

;; Runs build mode with an input script and a fresh lesson folder; returns the lines it printed.
(define (run-editor script #:seconds [seconds 40])
  (define lessons (make-temporary-file "heroic-lessons-~a" 'directory))
  (define env (environment-variables-copy (current-environment-variables)))
  (for ([kv `(("HEROIC_EDITOR" . "1") ("HEROIC_EDITOR_INPUT" . ,script)
              ("HEROIC_EDITOR_QUIT_AFTER_SECONDS" . ,(number->string seconds))
              ("HEROIC_LESSONS_DIR" . ,(path->string lessons)))])
    (environment-variables-set! env (string->bytes/utf-8 (car kv)) (string->bytes/utf-8 (cdr kv))))
  (define out
    (parameterize ([current-environment-variables env] [current-directory game-dir])
      (define-values (p stdout stdin stderr)
        (subprocess #f #f #f godot-binary "--headless" "--resolution" "1280x800" "."))
      (close-output-port stdin)
      (define drain (thread (λ () (port->string stderr))))
      (begin0 (port->string stdout) (subprocess-wait p) (thread-wait drain) (close-input-port stdout) (close-input-port stderr))))
  (delete-directory/files lessons #:must-exist? #f)
  (string-split out "\n"))

(define (lines-with ls s) (filter (λ (l) (string-contains? l s)) ls))
(define (number-after l rx) (cond [(regexp-match rx l) => (λ (m) (string->number (cadr m)))] [else #f]))

;; Lesson 1, the way a newcomer plays it: pick the lit entry, click on the green target,
;; press Test, drag the light block out, and let the lesson run its own check.
(define playthrough
  (string-append "wait 30; lesson see-saw; wait 5; "
                 "palette lever; click-at 0.1 0 0.05; wait 5; "            ; step 1: the beam, a little off its target
                 "palette block; click-at -0.2 0.42 0; wait 5; "           ; step 2: granite (the block's own material)
                 "palette block; click-at 0.2 0.42 0; wait 5; "            ; step 3: oak (the lesson picks it on the card)
                 "test; wait 5; wait-test; "                               ; step 4: the granite end goes down
                 "drag-to block_3 0.72 0.42 0; wait 5; wait-test; wait 5; " ; step 5, then the lesson's own run (step 6)
                 "log"))

(test-case "Lesson 1 plays through: every step completes and the lesson's own run finds the beam balanced"
  (when (godot-available?)
    (define out (run-editor playthrough))
    (define done (lines-with out "[Lesson] step "))
    (for ([n (in-range 1 7)])
      (check-true (ormap (λ (l) (regexp-match? (pregexp (format "\\[Lesson\\] step ~a done" n)) l)) done)
                  (format "step ~a completes" n)))
    ;; steps 1-3 and 5 set a close placement exactly on its target
    (check-true (ormap (λ (l) (string-contains? l "(move block_3 (0.75 0.4625 0))")) out) "the oak block is set on 0.75 m")
    ;; step 4's Test: the granite end went down to the stop, as worked out
    (define tip (findf (λ (l) (string-contains? l "test seen at step 4")) out))
    (check-true (and tip #t) "the first Test is seen at step 4")
    (check-true (> (number-after tip #px"lever_1 most ([0-9.]+)") 15) (format "the beam tipped to its stop: ~a" tip))
    (check-true (string-contains? tip "block_2=2.7kg") "the sim's granite block weighs 2.7 kg")
    (check-true (string-contains? tip "block_3=0.72kg") "the sim's oak block weighs 0.72 kg")
    (check-true (ormap (λ (l) (string-contains? l "The beam tipped 18° down on the left, on the granite block's side.")) out)
                "Test says what happened in a sentence")
    ;; step 6: the lesson ran the machine and the traced tilt stayed within 3°
    (define check (findf (λ (l) (string-contains? l "step 6 done: balanced")) out))
    (check-true (and check #t) "the final check passes")
    (check-true (<= (number-after check #px"most tilt ([0-9.]+)") 3) (format "within 3°: ~a" check))
    (check-true (ormap (λ (l) (string-contains? l "[Lesson] finished see-saw")) out) "the lesson says it is done")))

(test-case "A lesson left part-way is taken up again at the same step, with its design"
  (when (godot-available?)
    (define lessons (make-temporary-file "heroic-lessons-~a" 'directory))
    (define (run script)
      (define env (environment-variables-copy (current-environment-variables)))
      (for ([kv `(("HEROIC_EDITOR" . "1") ("HEROIC_EDITOR_INPUT" . ,script) ("HEROIC_EDITOR_QUIT_AFTER_SECONDS" . "12")
                  ("HEROIC_LESSONS_DIR" . ,(path->string lessons)))])
        (environment-variables-set! env (string->bytes/utf-8 (car kv)) (string->bytes/utf-8 (cdr kv))))
      (parameterize ([current-environment-variables env] [current-directory game-dir])
        (define-values (p stdout stdin stderr) (subprocess #f #f #f godot-binary "--headless" "--resolution" "1280x800" "."))
        (close-output-port stdin)
        (define drain (thread (λ () (port->string stderr))))
        (begin0 (string-split (port->string stdout) "\n") (subprocess-wait p) (thread-wait drain) (close-input-port stdout) (close-input-port stderr))))
    (run "wait 30; lesson see-saw; wait 5; palette lever; click-at 0 0 0; wait 5; palette block; click-at -0.2 0.42 0; wait 5; lesson-leave")
    (define again (run "wait 30; lesson-resume see-saw; wait 5; log"))
    (delete-directory/files lessons #:must-exist? #f)
    (check-true (ormap (λ (l) (string-contains? l "[Lesson] started see-saw at step 3")) again) "it carries on at step 3")
    (check-true (ormap (λ (l) (regexp-match? #px"state: .*lever_1@\\(0 0.4 0\\).*block_2@\\(-0.2 0.463 0\\)" l)) again)
                "with the beam and the granite block back where they were")))
