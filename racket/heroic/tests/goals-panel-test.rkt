#lang racket/base
;; Issue #230: the Routes block of the goals panel (game/scripts/Main.Routes.cs RoutesPanelText, Main.Goals.cs GoalsPanel), in the real game,
;; headless, read from the text dump HEROIC_ROUTES_PANEL turns on: "[routes-panel] begin level=L t=T", one "[routes-panel] KIND: text" line per
;; line of the block (Header, Final, Route, Step, StepDim, Reason), "[routes-panel] end", printed when the block's shape changes.
;; HEROIC_ROUTES_PANEL=1 (or next) is the next-steps level, outline is the outline level, 0 (or off) is off; a scripted run with the
;; switch unset is off and prints nothing.
;; Predicted before running, in lonely-rover-e2e (the bank at -63 C, 25 Wh):
;;  (a) fresh opening, next steps: the header, the final goal "[ ]  Call Earth with a full, warm bank" and its reason ("holds 0 of 25 Wh at -63 C");
;;      NO route line and nothing that names a route (the windmiller is not started: the warm bank step is shared and starts nothing).
;;  (b) a windmill and a four-stage train built (4 x 4:1 = 256:1), shaft joined to the salvaged motor's rotor, then the wire to the bank:
;;      after the build "The windmiller: 1 of 6 steps" (the ratio is read at a generator, so the train alone is not yet "gear-up"); after the
;;      shaft 2 of 6 (windmill, gear-up) with Next "Get the generator over its cut-in" (the rotor's rpm, 1,500) and Then "Wire the generator to
;;      the bank"; after the wire 3 of 6, "[x] Wire the generator to the bank" (the last met), Next the cut-in, Then "Keep the bank warm"
;;      (shared, unmet at -63 C, with the temperature in its reason). The 256:1 never shows: gear-up is met as soon as the ratio passes 100.
;;  (c) outline: the same route lines ("N of 6 steps") and no step or reason line, not even the final goal's reason; off: no line at all.
;; Skipped when Godot is not installed. About a minute per case.
(require rackunit racket/list racket/string racket/system racket/port racket/file racket/runtime-path
         (only-in heroic/godothost godot-available? godot-binary))

(define-runtime-path game-dir "../../../game")

(define (run-game input panel)
  (define env (environment-variables-copy (current-environment-variables)))
  (define settings (make-temporary-file "panel~a.cfg"))
  (delete-file settings)
  (for ([kv `(("HEROIC_WORLD" . "lonely-rover-e2e") ("HEROIC_INPUT" . ,input) ("HEROIC_ROUTES_PANEL" . ,panel)
              ("HEROIC_SETTINGS" . ,(path->string settings))
              ("HEROIC_SAVES_DIR" . ,(path->string (make-temporary-file "panel-saves~a" 'directory))))])
    (environment-variables-set! env (string->bytes/utf-8 (car kv)) (string->bytes/utf-8 (cdr kv))))
  (define out (open-output-string))
  (define code
    (parameterize ([current-directory game-dir] [current-environment-variables env]
                   [current-output-port out] [current-error-port out])
      (system*/exit-code godot-binary "--headless" "--fixed-fps" "120" "--path" ".")))
  (values code (string-split (get-output-string out) "\n")))

;; the dump as a list of blocks: (level (kind . text) ...)
(define (blocks lines)
  (let loop ([ls lines] [cur #f] [acc '()])
    (cond
      [(null? ls) (reverse acc)]
      [(regexp-match #rx"^\\[routes-panel\\] begin level=([A-Za-z]+)" (car ls)) => (λ (m) (loop (cdr ls) (list (cadr m)) acc))]
      [(and cur (regexp-match? #rx"^\\[routes-panel\\] end" (car ls))) (loop (cdr ls) #f (cons (reverse cur) acc))]
      [(and cur (regexp-match #rx"^\\[routes-panel\\] ([A-Za-z]+): (.*)$" (car ls))) => (λ (m) (loop (cdr ls) (cons (cons (cadr m) (caddr m)) cur) acc))]
      [else (loop (cdr ls) cur acc)])))
(define (texts block kind) (for/list ([l (cdr block)] #:when (equal? (car l) kind)) (cdr l)))
(define (has-text? block kind rx) (for/or ([t (texts block kind)]) (regexp-match? rx t)))

(define (stage name-w name-p x z prev)
  (list (format "build (wheel ~a #:catalogue involute-gear-m5-72 #:at (~a 11 ~a))" name-w x z)
        (format "build (arbor ~a ~a)" prev name-w)
        (format "build (wheel ~a #:catalogue involute-gear-m5-18 #:at (~a 11 ~a))" name-p (+ x 0.225) z)
        (format "build (mesh ~a ~a)" name-w name-p)))
(define build
  (string-join
   (append '("build new 180 105" "build (post tower #:at (0.45 0 -1.45) #:size-y 10.6 #:size-x 0.6 #:size-z 0.6)" "build (windmill sails #:at (0 11 0))")
           (stage "wheel-a" "pinion-b" 0 -1.3 "sails") (stage "wheel-b" "pinion-c" 0.225 -1.4 "pinion-b")
           (stage "wheel-c" "pinion-d" 0.45 -1.5 "pinion-c") (stage "wheel-d" "pinion-e" 0.675 -1.6 "pinion-d")
           '("build done"))
   "; "))
(define route-script
  (string-append "wait 360; " build "; wait 60; join built-1.pinion-e motors.rotor; wait 60; join motors.motor battery-bank.bank; wait 60; quit"))

(when (godot-available?)
  (test-case "(a) a fresh opening: the final goal only, no route, none named"
    (define-values (code lines) (run-game "wait 400; quit" "1"))
    (check-equal? code 0)
    (define bs (blocks lines))
    (check-true (pair? bs))
    (for ([b bs])
      (check-equal? (car b) "NextSteps")
      (check-equal? (texts b "Header") '("Routes to the call"))
      (check-equal? (texts b "Final") '("[ ]  Call Earth with a full, warm bank"))
      (check-equal? (texts b "Route") '() "no route is shown before one is started")
      (check-equal? (texts b "Step") '()))
    (check-true (has-text? (last bs) "Reason" #rx"holds 0 of 25 Wh at -6[0-9.]+ °C") (format "~a" (last bs)))
    (check-false (for/or ([l lines]) (regexp-match? #rx"windmiller|The windmiller" l)) "no line names the route"))

  (define-values (code lines) (run-game route-script "1"))
  (define bs (blocks lines))
  (test-case "(b) next steps: the windmiller appears when its first step is met and its next steps follow the build, the shaft and the wire"
    (check-equal? code 0 (string-join (take-right lines (min 8 (length lines))) " | "))
    (define progress (for/list ([b bs] #:when (pair? (texts b "Route"))) (car (texts b "Route"))))
    (check-equal? (remove-duplicates progress)
                  '("The windmiller: 1 of 6 steps" "The windmiller: 2 of 6 steps" "The windmiller: 3 of 6 steps"))
    (define after-build (for/first ([b bs] #:when (member "The windmiller: 1 of 6 steps" (texts b "Route"))) b))
    (check-true (has-text? after-build "Step" #rx"^\\[ \\]  Next: Turn the slow shaft much faster"))
    (check-true (has-text? after-build "Reason" #rx"yours is not there yet:1"))
    (define after-wire (last bs))
    (check-equal? (texts after-wire "Route") '("The windmiller: 3 of 6 steps"))
    (check-equal? (texts after-wire "Step") '("[ ]  Next: Get the generator over its cut-in" "[x]  Wire the generator to the bank"))
    (check-equal? (texts after-wire "StepDim") '("[ ]  Then: Keep the bank warm"))
    (check-true (has-text? after-wire "Reason" #rx"The rotor turns at [0-9.]+ rpm and charges only above 1,500"))
    (check-true (has-text? after-wire "Reason" #rx"The bank is at -6[0-9.]+ °C"))
    (check-true (has-text? after-wire "Reason" #rx"holds 0 of 25 Wh"))
    (check-equal? (length (texts after-wire "Route")) 1 "one route only: nothing about any other, no count of routes"))

  (test-case "(c) outline: route names and how far along, no steps and no reasons; off: nothing at all"
    (define-values (code lines) (run-game route-script "outline"))
    (check-equal? code 0)
    (define bs (blocks lines))
    (check-true (pair? bs))
    (for ([b bs])
      (check-equal? (car b) "Outline")
      (check-equal? (texts b "Step") '())
      (check-equal? (texts b "StepDim") '())
      (check-equal? (texts b "Reason") '()))
    (check-equal? (texts (last bs) "Route") '("The windmiller: 3 of 6 steps"))
    (define-values (code0 lines0) (run-game route-script "0"))
    (check-equal? code0 0)
    (define b0 (blocks lines0))
    (check-true (pair? b0))
    (for ([b b0]) (check-equal? b '("Off") "the Off block is empty"))))
