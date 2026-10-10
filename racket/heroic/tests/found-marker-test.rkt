#lang racket/base
;; Owner decision on #240 ("Show the rough area."): build mode's buried marker gives the bank's place and depth only once the rover has
;; found it (game/scripts/BuildMode.Buried.cs, the rule in src/HeroicInventions.Sim/Game/CargoFind.cs; its unit tests CargoFindTests.cs).
;; Headless Godot at 120 Hz on the Lonely Rover's opening, where the rim has buried the bank's crate 1.04 m deep at (254.5, 137.9).
;; Predicted:
;;  - before any digging, build mode over the bank logs one marker line, the rough area: "the battery bank lies somewhere in here, under
;;    the rubble: dig to find it", with no number in it (no depth, no place); and the bank is not found (no "[found] battery-bank" line);
;;    the two crates that lie on top of the rubble (hand tools, motors) are found as soon as the ground has settled: their tops show;
;;  - once found (here by the scripted step "marker found battery-bank", the crate still 1.04 m down) the marker is exact again:
;;    "found bank lies here, 1.04 m down: build the vault round it" (today's line, #213);
;;  - a save made after the find has (found-cargo 1.0 (crate battery-bank crate) ...), and the run that loads it shows the exact marker
;;    from the start: a reload does not hide it again.
;; After freeing, the crate stands on the floor of its pit (cover below 0), so build mode draws no buried marker at all, as before. The
;; natural finds (the top showing, freed, the teeth striking it) are seen in the director's solve, which digs the bank free in Step 4:
;; opt in with HEROIC_SLOW_FOUND=1 (about four minutes) to check that the "[found] battery-bank.crate" line comes during Step 4's digging
;; and not before. Skipped when Godot is not installed.
(require rackunit racket/system racket/port racket/string racket/list racket/file racket/runtime-path
         (only-in heroic/godothost godot-available? godot-binary))

(define-runtime-path game-dir "../../../game")
(define-runtime-path solve-steps "../../../game/routes/lonely-rover-opening/solve.steps")

(define (run-game env-list #:fps [fps "120"])
  (define env (environment-variables-copy (current-environment-variables)))
  (for ([kv (append '(("HEROIC_WORLD" . "lonely-rover-opening") ("HEROIC_HINTS" . "0")) env-list)])
    (environment-variables-set! env (string->bytes/utf-8 (car kv)) (string->bytes/utf-8 (cdr kv))))
  (string-split
   (parameterize ([current-directory game-dir] [current-environment-variables env])
     (with-output-to-string (λ () (system* godot-binary "--headless" "--fixed-fps" fps "--path" "."))))
   "\n"))

(define (markers ls) (for/list ([l ls] #:when (string-prefix? l "[BuildMode] buried marker battery-bank: ")) (substring l 40)))
(define (index-of-line rx ls) (for/first ([l ls] [i (in-naturals)] #:when (regexp-match? rx l)) i))
(define rough "the battery bank lies somewhere in here, under the rubble: dig to find it")
(define exact-rx #rx"^found bank lies here, 1\\.0[0-9] m down: build the vault round it$")

(when (godot-available?)
  (define dir (make-temporary-file "found-marker~a" 'directory))
  (define save (path->string (build-path dir "found.save")))
  (define first-run
    (run-game `(("HEROIC_INPUT" . ,(string-append "wait 360; build new 254.5 137.9; wait 120; build done; wait 30; "
                                                   "marker found battery-bank; wait 30; build new 254.5 137.9; wait 120; build done; wait 30; "
                                                   "rover save " save "; wait 10; quit")))))
  (define loaded-run
    (run-game `(("HEROIC_LOAD" . ,save) ("HEROIC_INPUT" . "wait 360; build new 254.5 137.9; wait 120; quit"))))

  (test-case "before it is found the marker is the rough area, with no depth; after, the exact place and depth"
    (define ms (markers first-run))
    (check-equal? (length ms) 2 (string-join first-run "\n"))
    (check-equal? (first ms) rough)
    (check-false (regexp-match? #rx"[0-9]" (first ms)) "no depth, no place: no number at all")
    (check-true (regexp-match? exact-rx (second ms)) (second ms))
    (define found-at (index-of-line #rx"^\\[found\\] battery-bank\\.crate at t [0-9.]+: marked by a scripted step$" first-run))
    (check-true (and found-at #t))
    (check-equal? (index-of-line #rx"^\\[found\\] battery-bank" first-run) found-at "nothing found the bank before the step")
    (check-true (< (index-of-line #rx"lies somewhere in here" first-run) found-at (index-of-line #rx"found bank lies here" first-run)))
    (check-true (and (index-of-line #rx"^\\[found\\] hand-tools\\.crate at t [0-9.]+: its top shows" first-run) #t) "the hand tools lie in sight"))

  (test-case "the find is in the save, and a reload keeps the marker exact"
    (check-true (regexp-match? #rx"\n  \\(found-cargo 1\\.0 \\(crate battery-bank crate\\)" (file->string save)))
    (define ms (markers loaded-run))
    (check-true (pair? ms) (string-join loaded-run "\n"))
    (check-true (for/and ([m ms]) (regexp-match? exact-rx m)) (format "~s" ms))
    (check-true (and (index-of-line #rx"^\\[found\\] battery-bank\\.crate at t [0-9.]+: found before the save$" loaded-run) #t))))

(when (and (godot-available?) (getenv "HEROIC_SLOW_FOUND"))
  (test-case "the director's solve finds the bank by digging, in Step 4, not before"
    (define steps (file->lines solve-steps))
    (define upto (for/first ([l steps] [i (in-naturals)] #:when (regexp-match? #rx"^caption Step 5/15" l)) i))
    (define dir (make-temporary-file "found-solve~a" 'directory))
    (define short (build-path dir "to-step-5.steps"))
    (display-lines-to-file (append (take steps (add1 upto)) '("wait 30" "quit")) short)
    (define ls (run-game `(("HEROIC_INPUT_FILE" . ,(path->string short))) #:fps "30"))   ; the director's own rate
    (define found (index-of-line #rx"^\\[found\\] battery-bank\\.crate" ls))
    (printf "SOLVE FOUND: ~a\n" (and found (list-ref ls found)))
    (check-true (and found #t) (string-join (take-right ls 40) "\n"))
    (check-true (< (index-of-line #rx"caption: Step 4/15" ls) found (index-of-line #rx"caption: Step 5/15" ls)))))
