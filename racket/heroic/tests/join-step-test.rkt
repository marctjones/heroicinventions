#lang racket/base
;; Issue #218: the scripted `join A B` step (game/scripts/ScriptedInput.cs, Main.Links.cs JoinStep) and #224's button label, in the real
;; game, headless. `join` is the Join machines gesture without the screen: SetJoining, then the two picks a click on each part makes
;; (Main.Pick, what JoinPickAt ends in), then joining off. Checked here:
;;  - it parses LABEL.PART[.PORT] and joins: after the e2e route's own build (a windmill and a one-stage train, built by `build` steps)
;;    `join built-1.pinion-b motors.rotor` and `join motors.motor battery-bank.bank` make the shaft and the wire the e2e route writes into
;;    its save by hand, link for link (only the shaft's id differs: `shaft-1` where the route's text says `drive-1`);
;;  - a pipe is the same as a click makes it: `join tank-a.cistern.outlet tank-b.trough.inlet` in join-check.world says what the real
;;    clicks said in a real window (the gui run recorded in docs/e2e-route.md: "joined: pipe pipe-1 from tank-a.cistern.outlet to
;;    tank-b.trough.inlet");
;;  - it fails loudly: a name no machine answers to, a part with no drawing, a malformed end and a join the rules refuse each print
;;    an error and end the run with exit code 1, even when a `quit` step follows;
;;  - #225: the click steps' errors (joinclick with joining off, joinlist with no list open, a bad argument): the clicks themselves need a
;;    real window and are checked in route-gui-test.rkt (the pick rule: PickBoxesTests in the C# suite);
;;  - #224: the Join machines button says "Join machines" in the rover game (the rover swallows J) and "Join machines (J)" in a machine
;;    run and in a world with no rover.
;; Skipped when Godot is not installed. About 20 s.
(require rackunit racket/list racket/string racket/system racket/port racket/file racket/runtime-path
         (only-in heroic/godothost godot-available? godot-binary))

(define-runtime-path game-dir "../../../game")

;; (run-game world input [env]) -> (values exit-code output-lines); stdout and stderr together, as the game's log
(define (run-game world input #:env [more '()])
  (define env (environment-variables-copy (current-environment-variables)))
  (environment-variables-set! env #"HEROIC_WORLD" (string->bytes/utf-8 world))
  (environment-variables-set! env #"HEROIC_INPUT" (string->bytes/utf-8 input))
  (environment-variables-set! env #"HEROIC_SAVES_DIR" (string->bytes/utf-8 (path->string (make-temporary-file "join-saves~a" 'directory))))
  (for ([kv more]) (environment-variables-set! env (string->bytes/utf-8 (car kv)) (string->bytes/utf-8 (cdr kv))))
  (define out (open-output-string))
  (define code
    (parameterize ([current-directory game-dir] [current-environment-variables env]
                   [current-output-port out] [current-error-port out])
      (system*/exit-code godot-binary "--headless" "--fixed-fps" "120" "--path" ".")))
  (values code (string-split (get-output-string out) "\n")))

(define (has? rx ls) (for/or ([l ls]) (regexp-match? rx l)))

(when (godot-available?)
  (define dir (make-temporary-file "join-step~a" 'directory))
  (define save (path->string (build-path dir "a.save")))

  (test-case "join after a build makes the e2e route's shaft and wire (the same forms its save carries)"
    ;; the route's build, one stage: sails, a 72-tooth wheel on the sails' arbor, an 18-tooth pinion meshing it
    (define build
      (string-join
       '("build new 180 105" "build (post tower #:at (0.45 0 -1.45) #:size-y 10.6 #:size-x 0.6 #:size-z 0.6)"
         "build (windmill sails #:at (0 11 0))"
         "build (wheel wheel-a #:catalogue involute-gear-m5-72 #:at (0 11 -1.3))" "build (arbor sails wheel-a)"
         "build (wheel pinion-b #:catalogue involute-gear-m5-18 #:at (0.225 11 -1.3))" "build (mesh wheel-a pinion-b)" "build done")
       "; "))
    (define-values (code lines)
      (run-game "lonely-rover-e2e" (string-append "wait 360; " build "; join built-1.pinion-b motors.rotor; join motors.motor battery-bank.bank; wait 5; rover save " save "; quit")))
    (check-equal? code 0 (string-join lines "\n"))
    (check-true (has? #rx"^\\[links\\] joined: shaft shaft-1 from built-1.pinion-b to motors.rotor" lines))
    (check-true (has? #rx"^\\[links\\] joined: wire wire-1 from motors.motor to battery-bank.bank" lines))
    (check-false (has? #rx"^join:" lines) "no error line")
    ;; the save's link list is what e2e-route-test.rkt writes by hand, shaft id aside
    (define saved (file->string save))
    ;; (the save writes the ratio as 1.0; the route's hand-written 1 reads as the same number)
    (check-true (regexp-match? #rx"\\(link shaft-1 shaft \\(from built-1 pinion-b\\) \\(to motors rotor\\) \\(ratio 1(\\.0)?\\)\\)" saved) "the shaft link in the save")
    (check-true (regexp-match? #rx"\\(link wire-1 wire \\(from motors motor\\) \\(to battery-bank bank\\)\\)" saved) "the wire link in the save")
    ;; and a link that is already there is refused, as a second click pair would be
    (define-values (code2 lines2)
      (run-game "lonely-rover-e2e" (string-append "wait 360; " build "; join built-1.pinion-b motors.rotor; join motors.rotor built-1.pinion-b; quit")))
    (check-equal? code2 1)
    (check-true (has? #rx"already on one shaft" lines2)))

  (test-case "a pipe: the same line the real clicks made in a window"
    (define-values (code lines) (run-game "join-check" "wait 20; join tank-a.cistern.outlet tank-b.trough.inlet; quit"))
    (check-equal? code 0)
    (check-true (has? #rx"^\\[links\\] joined: pipe pipe-1 from tank-a.cistern.outlet to tank-b.trough.inlet" lines)))

  (test-case "it fails loudly: a wrong name, no drawing, a malformed end, a refused join: an error line and exit code 1, a quit after it or not"
    (for ([input+rx (in-list
                     (list (cons "wait 20; join motors.nope battery-bank.bank; wait 30; quit" #rx"^join: motors has no drawn part 'nope'")
                           (cons "wait 20; join nobody.rotor battery-bank.bank; quit" #rx"^join: no machine is placed as 'nobody'")
                           (cons "wait 20; join motors battery-bank.bank; quit" #rx"^join: 'motors' is not LABEL.PART")
                           (cons "wait 20; join motors.rotor; quit" #rx"^join: expected 'join LABEL.PART")
                           (cons "wait 20; joinclick motors.motor list; quit" #rx"^join: joinclick: joining is not on")
                           (cons "wait 20; joinlist 1; quit" #rx"^join: no pick list is open")
                           (cons "wait 20; joinclick motors.motor 0; quit" #rx"^join: expected 'joinclick LABEL.PART")
                           (cons "wait 20; join motors.rotor battery-bank.bank; quit" #rx"^join: 'motors.rotor' and 'battery-bank.bank' were not joined")))])
      (define-values (code lines) (run-game "lonely-rover-e2e" (car input+rx)))
      (check-equal? code 1 (car input+rx))
      (check-true (has? (cdr input+rx) lines) (format "~a: ~a" (car input+rx) (string-join (take-right lines (min 6 (length lines))) " | "))))
    (define-values (code lines) (run-game "gallery" "wait 20; join a.b c.d; quit"))
    (check-equal? code 1 "no machine of that name in a world of none of them"))

  (test-case "#224: the button says (J) only where J works"
    (define (label world #:env [env '()])
      (define-values (code lines) (run-game world "wait 20; joinbutton; quit" #:env env))
      (check-equal? code 0)
      (for/first ([l lines] #:when (regexp-match #rx"join button: '([^']*)' visible ([A-Za-z]+) rover ([A-Za-z]+)" l))
        (cdr (regexp-match #rx"join button: '([^']*)' visible ([A-Za-z]+) rover ([A-Za-z]+)" l))))
    (check-equal? (label "lonely-rover-e2e") '("Join machines" "True" "True") "the rover game: J is swallowed, so no (J)")
    (check-equal? (label "lonely-rover-e2e" #:env '(("HEROIC_ROVER" . "0"))) '("Join machines (J)" "True" "False") "the same world with no rover")
    (check-equal? (label "join-check") '("Join machines (J)" "True" "False") "a world with no rover: J joins")))
