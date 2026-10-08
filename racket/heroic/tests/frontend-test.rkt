#lang racket/base
;; Issue #95: the front end of the Lonely Rover game. Each case runs the real game headless (120 Hz) with HEROIC_FRONTEND=1 (the title page,
;; as a plain launch shows it) and drives it with the game's scripted steps (game/scripts/Main.FrontEnd.cs: "frontend press TEXT",
;; "frontend until SCREEN", "frontend skip", "frontend save", "frontend menu", "frontend log"), which go through the same handlers a click
;; or a key reaches. Worked beforehand:
;;   new game      New game -> the scenario page lists "The Lonely Rover" (the built-in scenario until #60's) -> Start -> the opening: the rover is
;;                 held (an up arrow held for 1 s moves it under 0.05 m) -> a key (space) skips it -> the same hold drives it: the game's speed
;;                 is RoverSpec.GameSpeed, so a second's hold moves it well over 0.5 m.
;;   the opening   plays out on its own: 4 captions of 7.5 s each, so it finishes after 30.0 s (and the world is released at caption 3, 15 s).
;;   the win       front-end-check: a bank of 10 Wh set to 9 Wh with the call at any hour: the generator has to put in 1 Wh. The windmill takes about
;;                 10 s to reach the generator's cut-in, then 1 Wh / 278 W = 13 s: the call goes out about 23 s after 02:55, on sol 1, at "02:55"; the
;;                 bank's record is 1.000 Wh from the wind (10 - 9), and nothing else.
;;   continue      a save of front-end-check, then the title page's Continue and Load game go to the rover with no opening.
;; Skipped when Godot is not installed.
(require rackunit racket/system racket/port racket/string racket/list racket/file racket/runtime-path
         (only-in heroic/godothost godot-available? godot-binary))

(define-runtime-path game-dir "../../../game")

(define (run-game script #:env [extra '()])
  (define env (environment-variables-copy (current-environment-variables)))
  (environment-variables-set! env #"HEROIC_FRONTEND" #"1")
  (environment-variables-set! env #"HEROIC_INPUT" (string->bytes/utf-8 script))
  (for ([kv extra]) (environment-variables-set! env (string->bytes/utf-8 (car kv)) (string->bytes/utf-8 (cdr kv))))
  (define out
    (parameterize ([current-directory game-dir] [current-environment-variables env])
      (with-output-to-string
        (λ () (system* godot-binary "--headless" "--fixed-fps" "120" "--path" ".")))))
  (string-split out "\n"))

(define (lines-with lines prefix) (filter (λ (l) (string-prefix? l prefix)) lines))
(define (screens lines) (map (λ (l) (substring l (string-length "[frontend] screen: "))) (lines-with lines "[frontend] screen: ")))
(define (rover-xz lines)   ; every "[view] rover: at (x y z)" -> (x z)
  (for/list ([l (lines-with lines "[view] rover: at")])
    (define m (regexp-match #rx"at \\(([-0-9.]+) ([-0-9.]+) ([-0-9.]+)\\)" l))
    (list (string->number (cadr m)) (string->number (cadddr m)))))
(define (dist a b) (sqrt (+ (expt (- (first a) (first b)) 2) (expt (- (second a) (second b)) 2))))

(when (godot-available?)

  (test-case "a plain launch opens on the title page, New game lists the scenario, Start begins the opening, a key skips it and the rover drives"
    (define lines
      (run-game (string-append "wait 5; frontend press New game; wait 3; frontend screen;"
                               " frontend press Start The Lonely Rover; wait 60; rover; hold up 1; wait 3; rover;"
                               " key space; wait 5; frontend screen; rover; hold up 1; wait 3; rover; quit")))
    (check-equal? (take (screens lines) 4) '("Title" "Scenario" "None" "Opening") "title, scenario page, (the page comes down), the opening")
    (check-not-false (member "[frontend] press: Start The Lonely Rover" lines) "the scenario is offered by its title")
    (check-equal? (length (lines-with lines "[frontend] opening skipped")) 1 "skipped by the key")
    (check-equal? (last (screens lines)) "None" "after the skip the rover has control")
    (define p (rover-xz lines))
    (check-equal? (length p) 4)
    (check-true (< (dist (first p) (second p)) 0.05) (format "held in the opening: moved ~a m" (dist (first p) (second p))))
    (check-true (> (dist (third p) (fourth p)) 0.5) (format "driving after the skip: moved ~a m" (dist (third p) (fourth p)))))

  (test-case "the opening plays out on its own: four captions, finished after 30.0 s, the world let go at the third"
    (define lines (run-game "wait 5; frontend press New game; wait 3; frontend press Start; frontend until None; quit"))
    (check-equal? (length (lines-with lines "[frontend] caption")) 4)
    (check-true (string-prefix? (first (lines-with lines "[frontend] caption 1 of 4")) "[frontend] caption 1 of 4: A dust storm"))
    (define done (lines-with lines "[frontend] opening finished"))
    (check-equal? (length done) 1)
    (check-= (string->number (cadr (regexp-match #rx"after ([0-9.]+) s" (first done)))) 30.0 0.1 "4 captions x 7.5 s"))

  (test-case "a forced win raises the ending with the sol, the hour and the bank's record: 1.000 Wh from the wind"
    (define lines
      (run-game "frontend until Ending; frontend screen; wait 30; frontend press Keep playing; wait 5; frontend screen; frontend log; wait 5; quit"
                #:env '(("HEROIC_WORLD" . "front-end-check") ("HEROIC_SET" . "bank charge 9; bank call-any-time 1"))))
    (define ending (lines-with lines "[frontend] ending: "))
    (check-equal? (length ending) 1 "raised once")
    (check-true (regexp-match? #rx"^\\[frontend\\] ending: bank sol 1 at 02:55, 10.00 of 10.00 Wh" (first ending)) (first ending))
    (define sources (lines-with lines "[frontend] ending source "))
    (check-equal? (length sources) 1 "one source")
    (check-true (string-prefix? (first sources) "[frontend] ending source wind: 1.000 Wh") (first sources))
    (check-equal? (take-right (screens lines) 3) '("Ending" "None" "Log") "the ending, Keep playing, the log")
    (check-not-false (pair? (lines-with lines "[log] sol 1 02:55 win: The call went out from bank on sol 1"))))

  (test-case "the rover log: a refusal and the bank's full line are written down, in order"
    (define lines
      (run-game "frontend until Ending; wait 5; quit"
                #:env '(("HEROIC_WORLD" . "front-end-check") ("HEROIC_SET" . "bank charge 9; bank call-any-time 1"))))
    (define log (lines-with lines "[log] "))
    (check-true (string-contains? (first log) "The log begins on sol 1"))
    (define full (index-where log (λ (l) (string-contains? l "is full (10.0 Wh)"))))
    (define won (index-where log (λ (l) (string-contains? l "win: The call went out"))))
    (check-true (and full won (< full won)) "full, then won")
    (check-false (ormap (λ (l) (string-contains? l "% charged")) log) "a bank found at 90 % logs no milestone it did not cross"))

  (test-case "Continue and Load game take up a save with no opening"
    (define lines
      (run-game (string-append "wait 20; frontend save; frontend menu; wait 3; frontend press Continue; wait 20; frontend screen; rover;"
                               " frontend menu; wait 3; frontend press Load game; wait 3; frontend press front-end-check; wait 20; frontend screen; rover; quit")
                #:env '(("HEROIC_WORLD" . "front-end-check"))))
    ;; clean up what the run wrote into the user's saves
    (for ([l (lines-with lines "[save] saved front-end-check to ")])
      (define path (cadr (regexp-match #rx" to (.*)$" l)))
      (for ([f (list path (string-replace path ".save" ".autosave.save"))]) (when (file-exists? f) (delete-file f))))
    (check-equal? (length (lines-with lines "[save] loaded front-end-check")) 2 "Continue, then Load game")
    (check-equal? (length (lines-with lines "[frontend] caption")) 0 "no opening")
    (check-equal? (screens lines) '("Title" "None" "Title" "Load" "None") "the title page, the save loaded; again via the list")
    (check-equal? (length (lines-with lines "[view] rover: at")) 2 "the rover is there each time")))
