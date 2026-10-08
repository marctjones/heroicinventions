#lang racket/base
;; The operator log (issue #153): what a person's hand does to a running machine is recorded as
;; (at t (part field value)), saved, replayed and turned into a test. The airlock is the machine: its cycle
;; (pump down, bleed, open the outer door, close it, re-pressurise) is worked by the scripted person below, then
;; every other path -- replay in the game, replay under simulate, the exported test -- has to give the same run.
(require rackunit racket/list racket/file racket/port heroic/machine heroic/emit heroic/simhost heroic/godothost)

(define-namespace-anchor anchor)

(define (value-at run path t)
  (define key (string->symbol (format "~a.~a" (car path) (cadr path))))
  (define frame (for/fold ([best (car run)]) ([f (cdr run)]) (if (< (abs (- (car f) t)) (abs (- (car best) t))) f best)))
  (cadr (assq key (cdr frame))))

;; the scripted person: waits for the sim clock (waitsim), then operates (Main.Operate, as a click or the F key will).
;; The first action, at 10 s, is what takes the control from the blueprint's demo operator, so what follows is all theirs.
(define person
  (string-append "waitsim 10; operate inner open 0; "
                 "waitsim 400; operate bleed open 1; "
                 "waitsim 600; operate bleed open 0; operate outer open 1; "
                 "waitsim 700; operate outer open 0; operate pump speed 0; operate inner open 1"))
;; what it did, worked out beforehand: (time-by-script field value); each lands within a second of its waitsim
(define expected '((10 inner open 0) (400 bleed open 1) (600 bleed open 0) (600 outer open 1)
                   (700 outer open 0) (700 pump speed 0) (700 inner open 1)))

(define dir (make-temporary-file "operator-~a" 'directory))
(define (in-dir name) (path->string (build-path dir name)))
(define (read-log file) (call-with-input-file file (λ (in) (for/list ([f (in-port read in)]) f))))

(when (godot-available?)
  (define log-file (in-dir "airlock.actions"))
  (define test-file (in-dir "airlock-test.rkt"))
  (define recorded
    (godot-simulate 'airlock #:seconds 720 #:sample-dt 1
                    #:env `(("HEROIC_INPUT" . ,person) ("HEROIC_ACTIONS_OUT" . ,log-file) ("HEROIC_TEST_OUT" . ,test-file))))
  (define log (read-log log-file))

  (test-case "Record: each hand action is one (at t (part field value)), in order, once"
    (check-equal? (length log) (length expected) "the demo operator stopped at the first action: nothing of its own is in the log")
    (for ([entry log] [want expected])
      (check-equal? (car entry) 'at)
      (check-= (cadr entry) (car want) 1 "the action lands when the script's clock wait ended")
      (check-equal? (caaddr entry) (cadr want))
      (check-equal? (cadr (caddr entry)) (caddr want))
      (check-= (caddr (caddr entry)) (cadddr want) 0))
    (check-= (value-at recorded '(outer open) 650) 1 1e-9 "and the actions did their work")
    (check-= (value-at recorded '(outer open) 710) 0 1e-9)
    (check-= (value-at recorded '(chamber pressure) 399) 5 0.05 "pumped down to its 5 kPa switch before the bleed"))

  (test-case "Replay in the game: the log gives back the recorded run, frame for frame, and logs itself again"
    (define replay-log (in-dir "replayed.actions"))
    (define replayed (godot-simulate 'airlock #:seconds 720 #:sample-dt 1 #:actions log-file
                                     #:env `(("HEROIC_ACTIONS_OUT" . ,replay-log))))
    (check-equal? (length replayed) (length recorded))
    (for ([a recorded] [b replayed])
      (check-= (car a) (car b) 1e-9)
      (for ([ea (cdr a)] [eb (cdr b)])
        (check-equal? (car ea) (car eb))
        (check-= (cadr ea) (cadr eb) 1e-9 (format "~a at ~a s" (car ea) (car a)))))
    (check-equal? (read-log replay-log) log "the replay's log is the recording's"))

  (test-case "Replay under simulate matches the game's recorded run at the sampled times"
    (define sim (simulate 'airlock #:seconds 720 #:step 0.05 #:sample-dt 1 #:actions log-file))
    (for ([t '(100 399 450 599 650 699 710 719)])
      (check-= (value-at sim '(chamber pressure) t) (value-at recorded '(chamber pressure) t) 0.05 (format "chamber pressure at ~a s" t))
      (check-= (value-at sim '(habitat pressure) t) (value-at recorded '(habitat pressure) t) 0.05 (format "habitat pressure at ~a s" t)))
    (for ([t '(399 450 650 710)])
      (check-= (value-at sim '(outer open) t) (value-at recorded '(outer open) t) 1e-9)
      (check-= (value-at sim '(bleed open) t) (value-at recorded '(bleed open) t) 1e-9))
    (check-exn #rx"no such operator log" (λ () (simulate 'airlock #:seconds 1 #:actions "/no/such/log")))
    (check-exn #rx"an action is" (λ () (simulate 'airlock #:seconds 1 #:actions '((at 1 (bleed open)))))))

  (test-case "Copy as test: the exported simulate form runs, and says what the person did"
    (define form (call-with-input-file test-file read))
    (check-equal? (car form) 'simulate)
    (define sim (parameterize ([current-namespace (namespace-anchor->namespace anchor)]) (eval form)))
    ;; the same settings as the log, read back from the form itself
    (define settings (cadr (cadr (memq '#:set form))))
    (check-equal? (length settings) (length log))
    (for ([s settings] [entry log])
      (check-equal? (take s 2) (list (car (caddr entry)) (cadr (caddr entry))))
      (check-= (list-ref s 3) (cadr entry) 0 "the same time to the last digit"))
    (check-= (value-at sim '(outer open) 650) 1 1e-9)
    (check-= (value-at sim '(outer open) 710) 0 1e-9)
    (check-= (value-at sim '(chamber pressure) 399) 5 0.05)
    (check-= (value-at sim '(chamber pressure) 719) (value-at recorded '(chamber pressure) 719) 0.05
             "the test the console offers ends where the run did"))

  (test-case "Save and load: the log goes into the save file and comes back, and the demo does not start again"
    (define save-file (in-dir "airlock.save"))
    (define out-1 (in-dir "saved-run.actions"))
    (godot-simulate 'airlock #:seconds 660 #:sample-dt 10
                    #:env `(("HEROIC_INPUT" . ,person) ("HEROIC_SAVE" . ,save-file) ("HEROIC_SAVE_AT" . "650")
                            ("HEROIC_ACTIONS_OUT" . ,out-1)))
    (define saved-text (file->string save-file))
    (check-regexp-match #rx"\\(operator-log" saved-text)
    ;; by 650 s the person had done four of the seven things
    (define at-650 (take log 4))
    (define out-2 (in-dir "loaded-run.actions"))
    (define loaded (godot-simulate 'airlock #:seconds 655 #:sample-dt 1
                                   #:env `(("HEROIC_LOAD" . ,save-file) ("HEROIC_ACTIONS_OUT" . ,out-2))))
    (define back (read-log out-2))
    (check-equal? (length back) 4 "the four actions are in the loaded run's log, and nothing was added")
    (for ([b back] [a at-650])
      (check-= (cadr b) (cadr a) 1 "the same times (the two recordings are separate runs)")
      (check-equal? (caddr b) (caddr a)))
    (check-= (value-at loaded '(outer open) 652) 1 1e-9 "the machine was put back as it was")
    (check-= (value-at loaded '(bleed open) 652) 0 1e-9))

  (test-case "Demo operator: opening the airlock with no one at the controls shows the whole cycle"
    ;; Main's demo is the blueprint's (operator ...): bleed at 400 s, outer door 600-700 s, inner door at 700 s
    (define demo (godot-simulate 'airlock #:seconds 720 #:sample-dt 1))
    (check-= (value-at demo '(chamber pressure) 399) 5 0.05 "pumped down")
    (check-= (value-at demo '(bleed open) 500) 1 1e-9 "bled")
    (check-= (value-at demo '(outer open) 650) 1 1e-9 "outer door open onto Mars")
    (check-= (value-at demo '(chamber pressure) 650) 0.61 0.15 "the chamber is at Mars's 610 Pa")
    (check-= (value-at demo '(outer open) 710) 0 1e-9 "closed again")
    (check-= (value-at demo '(inner open) 710) 1 1e-9 "inner door open")
    (check-true (> (value-at demo '(chamber pressure) 719) 40) "and the chamber re-pressurised from the habitat")
    ;; a test setting means someone else is acting: the demo stays out of it
    (define alone (godot-simulate 'airlock #:seconds 450 #:sample-dt 50 #:set '((scene ambient 20 1))))
    (check-= (value-at alone '(bleed open) 449) 0 1e-9 "HEROIC_SET present: no demo operator")))

(test-case "The operator form compiles into the machine and out into the .machine file"
  (define ns (make-base-namespace))
  (namespace-attach-module (namespace-anchor->namespace anchor) 'heroic/machine ns)
  (define (machine-of . clauses)
    (parameterize ([current-namespace ns])
      (eval `(module opdemo heroic (define-machine opdemo ,@clauses)))
      (eval '(require 'opdemo)))
    (car (take-registered-machines)))
  (define m (machine-of '(tank t #:at (0 0 0) #:area 1 #:height 1) '(operator (at 5 (t water 0.5)) (at (* 2 1) (t water 0.1)))))
  (check-equal? (map (λ (a) (take a 4)) (operator-spec-actions (car (machine-operators m))))
                '((5 t water 0.5) (2 t water 0.1)))
  (check-regexp-match #rx"\\(operator \\(at 5.0 \\(t water 0.5\\)\\) \\(at 2.0 \\(t water 0.1\\)\\)"
                      (with-output-to-string (λ () (write (machine->sexp m)))))
  (check-exn exn:fail:syntax? (λ () (machine-of '(tank t #:at (0 0 0) #:area 1 #:height 1) '(operator (at 5 (t water)))))))

(delete-directory/files dir)
