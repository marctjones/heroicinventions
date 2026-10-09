#lang racket/base
;; Issue #98: the first-run hints of the rover game (game/scripts/Hints.cs). Each case runs lonely-rover-opening headless at 120 Hz with
;; HEROIC_HINTS=1 and HEROIC_HINTS_RATE=30 (the hints' waiting clocks run 30 times faster) and reads the "[hints] showing ID" lines.
;; Worked beforehand (the waits are in held seconds, counted only while no page is up and no other hint is showing; a hint stays up 40 s):
;;   nothing done  drive at 20 s, speed at 150 + 40, log at 270 + 80, build at 390 + 120 (= 510 s, 8.5 min): in that order, each once.
;;   doing things  an up arrow held 1 s drives (no drive hint); the log opened (no log hint); the game started at 5x (no speed hint):
;;                 only the build hint is left.
;;   hints off     HEROIC_HINTS=0: none.
;; #216 added four: rover-goals (F2; waits 200), rover-bank-cold (the Bank is below 0 C; 280), rover-join (J; two machines or more; 330) and
;;   rover-sleep-paused (after a paused sleep of a Jolt-driven machine: 2 s), and rover-build now waits 350 (was 390). Every wait is held seconds
;;   (while the hint's condition holds and no other hint is up); a shown hint stays up 40 s unless retired. Nothing done, all ignored, the opening world
;;   (a bank at -55 C, five machines): drive at 20, speed at 150 + 40 = 190, goals at 200 + 80 = 280, log at 250 + 120 = 370, bank at 280 + 160 = 440,
;;   join (the left panel's button; the J key does nothing in the rover game) at 330 + 200 = 530, build at 350 + 240 = 590 s: all inside ten minutes, in that order.
;;   REAL THRESHOLDS: a run at HEROIC_HINTS_RATE=1 counts the same seconds (a fixed-fps run hands the game 1/120 s a frame whatever the wall clock),
;;   so "at rate 1" is honest game time. Run: 18 s of it, no hint; 22 s, rover-drive only. The whole 590 s path at rate 1 was TRIED (75,000 ticks) and
;;   abandoned after 20 minutes of wall time (the headless game crawls once the world has run a while, with other suites running): it is not in the
;;   suite. The order and the arithmetic above are checked at rate 30, which only scales the waits.
;;   Doing things: F2 pressed (no goals hint); a paused sleep (rover-sleep-hint-check.world: found-electrics, a Jolt-driven
;;   windmill, a sleep of 9,000 s with the engine paused) shows rover-sleep-paused once, after the wake.
;; Skipped when Godot is not installed.
(require rackunit racket/system racket/port racket/string racket/list racket/file racket/runtime-path
         (only-in heroic/godothost godot-available? godot-binary))

(define-runtime-path game-dir "../../../game")

(define (hints-shown script #:env [extra '()] #:world [world "lonely-rover-opening"])
  (define settings (make-temporary-file "hints~a.cfg"))
  (delete-file settings)   ; a fresh player: nothing retired yet
  (define env (environment-variables-copy (current-environment-variables)))
  (for ([kv (append `(("HEROIC_WORLD" . ,world) ("HEROIC_HINTS" . "1") ("HEROIC_HINTS_RATE" . "30")
                      ("HEROIC_SAVES_DIR" . ,(path->string (find-system-path 'temp-dir)))
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
  (test-case "nothing done: the seven pointers come in order, each once, and no sandbox hint among them"
    (define shown (hints-shown "frontend skip; wait 4000; quit"))
    (check-equal? (rover-only shown) '("rover-drive" "rover-speed" "rover-goals" "rover-log" "rover-bank-cold" "rover-join" "rover-build"))
    (check-false (member "camera" shown))
    (check-false (member "speed" shown)))
  (test-case "driving, reading the log and playing at speed retire their pointers: goals, the cold bank, joining and building are left"
    (define shown (hints-shown "frontend skip; hold up 1; wait 130; frontend log; wait 20; frontend log; wait 4000; quit"
                               #:env '(("HEROIC_SPEED" . "5"))))
    (check-equal? (rover-only shown) '("rover-goals" "rover-bank-cold" "rover-join" "rover-build")))
  (test-case "F2 opens the goals panel: no goals pointer"
    (define shown (hints-shown "frontend skip; wait 30; key f2; wait 4000; quit"))
    (check-false (member "rover-goals" shown))
    (check-not-false (member "rover-join" shown)))
  (test-case "a paused sleep of a Jolt-driven machine is followed by the Keep machines turning pointer, once, after the wake"
    (define shown (hints-shown "frontend skip; hold up 1; wait 130; sleep until scene.elapsed above 9000 limit 9500 paused; wait 600; quit"
                               #:world "rover-sleep-hint-check"))
    (check-equal? (count (λ (id) (equal? id "rover-sleep-paused")) shown) 1)
    (check-false (member "rover-sleep-paused"
                         (hints-shown "frontend skip; hold up 1; wait 130; sleep until scene.elapsed above 100 limit 200 live; wait 600; quit"
                                      #:world "rover-sleep-hint-check"))
                 "a live sleep gives no pause pointer"))
  (test-case "REAL thresholds (rate 1): nothing before 20 s, the drive pointer after, and the rest by arithmetic"
    (define rate1 '(("HEROIC_HINTS_RATE" . "1")))
    (check-equal? (rover-only (hints-shown "frontend skip; wait 2200; quit" #:env rate1)) '())          ; 18 s
    (check-equal? (rover-only (hints-shown "frontend skip; wait 2650; quit" #:env rate1)) '("rover-drive")))   ; 22 s
  (test-case "with hints off, none appears"
    (check-equal? (hints-shown "frontend skip; wait 4000; quit" #:env '(("HEROIC_HINTS" . "0"))) '())))
