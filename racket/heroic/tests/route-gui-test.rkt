#lang racket/base
;; Issue #218: tools/route-gui.sh, the fastest route played through real mouse and key events in a real (hidden) window, and the win read
;; from its trace. Needs a macOS window server (tools/gui-check.sh opens the app with `open -g -j`), so it is OPT IN:
;;     HEROIC_GUI_ROUTE=1 raco test racket/heroic/tests/route-gui-test.rkt        # about 3.5 minutes; takes no focus
;; Without HEROIC_GUI_ROUTE (or without Godot) it is skipped, so the 20-minute suite does not need a window.
;;
;; Predicted before the run (from e2e-route-test.rkt, the automated route, same world and wakes):
;;  - the found bank's cells 24.5 C, the rock 92 C, the vault's lid 37% open at the wake (54,800 s); the bank full by then (the sleep charges the
;;    motor at an estimate: it has never charged a warm bank), so `bank.charge` 25 in the first frame after it;
;;  - the call at the pass: the rover log's win line at 55,484 s (03:00, sol 2), a frame with bank.won 1 by 55,495 s, none before 55,484 s;
;;  - the real gesture does not reach the train's last pinion, the motor or the bank (docs/e2e-route.md): the pinion click lands on the sails
;;    (the nearest box of four crossed), and the two links are made by the `join` step.
(require rackunit racket/list racket/string racket/port racket/file racket/system racket/runtime-path
         (only-in heroic/godothost godot-available?))

(define-runtime-path here "../../..")
(define script (build-path here "tools" "route-gui.sh"))

(define (frames path)   ; (list (t (field value) ...) ...), one frame to a line
  (if (file-exists? path) (call-with-input-file path (λ (in) (for/list ([f (in-port read in)]) f))) '()))
(define (at f key) (cadr (assq key (cdr f))))

(when (and (godot-available?) (getenv "HEROIC_GUI_ROUTE") (file-exists? script))
  (define out (make-temporary-file "route-gui~a" 'directory))
  (define summary (with-output-to-string (λ () (system* (path->string script) (path->string out)))))
  (define log (file->string (build-path out "run.log")))
  (define (has? rx) (regexp-match? rx log))
  (define bank (frames (build-path out "route.battery-bank")))
  (define vault (frames (build-path out "route.vault")))

  (test-case "the route is played: drive, build mode by its button, the palette's post, the build, Leave"
    (check-true (has? #rx"\\[build\\] new machine built-1 at \\(180\\.[0-9]+ -[0-9.]+ 10[45]\\.[0-9]+\\)") "the windmill's place is the automated route's (180, 105) to a metre")
    (check-true (has? #rx"\\[BuildMode\\] > \\(post post_1 #:at") "the post was placed by a palette click")
    (check-true (has? #rx"\\[build\\] built-1: design post_1:post sails:windmill wheel-a:wheel pinion-b:wheel .* pinion-e:wheel; running 10 parts") "the train is built")
    (check-false (has? #rx"BuildMode\\] error") "no command was refused"))

  (test-case "the real gesture does not reach the train, the motor or the bank, and the links are made by name"
    (check-true (has? #rx"the click crossed 4 parts' boxes, nearest first: built-1.sails") "the click meant for pinion-e lands on the sails")
    (check-true (has? #rx"\\[links\\] joined: shaft shaft-1 from built-1.pinion-e to motors.rotor"))
    (check-true (has? #rx"\\[links\\] joined: wire wire-1 from motors.motor to battery-bank.bank"))
    (check-false (has? #rx"\n(\\[Main\\] )?join: ") "no join error"))

  (test-case "the sleep, from the panel: the same wake as the automated route, and the same state at it"
    (check-true (has? #rx"\\[sleep\\] Machines paused: generators charge at their last steady rate .*motor: [0-9.]+ W into bank \\(estimated"))
    (check-true (has? #rx"\\[sleep\\] woke after [0-9.]+ s: scene.elapsed ≥ 54800"))
    (define woke (first (filter (λ (f) (>= (at f 'scene.elapsed) 54800)) bank)))
    (check-= (at woke 'bank.charge) 25 1e-6 "Wh: full at the wake")
    (check-= (at woke 'cells.temperature) 24.5 1.5 "C, the cells at 02:49")
    (define vwoke (first (filter (λ (f) (>= (at f 'scene.elapsed) 54800)) vault)))
    (check-= (at vwoke 'rock.temperature) 92.1 3 "C, the rock")
    (check-= (at vwoke 'bin.open) 0.37 0.1 "the lid"))

  (test-case "the win, from the trace and the rover log: 03:00 on sol 2, not before the pass"
    (for ([f (in-list bank)] #:when (< (at f 'scene.elapsed) 55484)) (check-= (at f 'bank.won) 0 0))
    (define won (for/first ([f (in-list bank)] #:when (> (at f 'bank.won) 0.5)) f))
    (check-true (and won #t) "a frame with bank.won 1")
    (when won
      (check-true (<= 55484 (at won 'scene.elapsed) 55495) (format "won at ~a s" (at won 'scene.elapsed)))
      (check-= (at won 'scene.sol) 2 0)
      (check-= (at won 'scene.won) 1 0)
      (check-true (<= 3.0 (at won 'scene.time) 3.01)))
    (define rover-log (apply string-append (for/list ([f (directory-list (build-path out "saves") #:build? #t)] #:when (regexp-match? #rx"rover-log.txt$" (path->string f))) (file->string f))))
    (check-true (regexp-match? #rx"sol 2 03:00 t=5548[4-9]\\.[0-9]s win: The call went out from bank on sol 2 at 03:00" rover-log))
    (check-true (has? #rx"\\[frontend\\] ending: bank sol 2 at 03:00, 25.00 of 25.00 Wh at [0-9.]+ C, sources 1"))
    (check-true (has? #rx"\\[frontend\\] ending source wind: 25.000 Wh"))
    (printf "ROUTE-GUI: shots and trace in ~a\n" out)))
