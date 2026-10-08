#lang racket/base
;; "Test it" in build mode says what rose, what turned, where the water went
;; and which ropes broke (#182), and runs until things settle instead of a
;; fixed 4 s. Each design is built by the editor's own commands (the "cmd"
;; step of the scripted input), Test is pressed, and the numbers the run
;; prints ("[BuildMode] facts: ...", "[BuildMode] test: ...") are compared
;; with numbers worked out before running. Skipped where Godot isn't installed.
(require rackunit racket/port racket/string racket/list racket/math heroic/godothost)

(define-values (game-dir) (simplify-path (build-path (collection-file-path "godothost.rkt" "heroic") 'up 'up 'up "game")))

;; Runs build mode headless with an input script; returns (values sentence facts-line).
(define (run-editor script #:max-seconds [max-seconds #f])
  (define env (environment-variables-copy (current-environment-variables)))
  (for ([kv `(("HEROIC_EDITOR" . "1") ("HEROIC_EDITOR_INPUT" . ,script)
              ("HEROIC_EDITOR_QUIT_AFTER_SECONDS" . "120")
              ,@(if max-seconds `(("HEROIC_TEST_SECONDS" . ,(number->string max-seconds))) '()))])
    (environment-variables-set! env (string->bytes/utf-8 (car kv)) (string->bytes/utf-8 (cdr kv))))
  (define out
    (parameterize ([current-environment-variables env] [current-directory game-dir])
      (define-values (p stdout stdin stderr)
        (subprocess #f #f #f godot-binary "--headless" "--resolution" "1280x800" "."))
      (close-output-port stdin)
      (define drain (thread (λ () (port->string stderr))))
      (begin0 (port->string stdout) (subprocess-wait p) (thread-wait drain) (close-input-port stdout) (close-input-port stderr))))
  (define lines (string-split out "\n"))
  (define (after prefix) (for/first ([l lines] #:when (string-prefix? l prefix)) (substring l (string-length prefix))))
  (define finished   ; "test: started" comes first; the one that ends the run carries the sentence
    (for/last ([l lines] #:when (and (string-prefix? l "[BuildMode] test: ") (not (string-suffix? l "started"))))
      (substring l (string-length "[BuildMode] test: "))))
  (values (and finished (string-trim (car (string-split finished " | "))))
          (after "[BuildMode] facts: ")))

;; the number after "key=" in a facts line, or #f
(define (fact facts key)
  (define m (regexp-match (pregexp (string-append (regexp-quote key) "=(-?[0-9.]+)")) facts))
  (and m (string->number (cadr m))))

(define g 9.81)

;; The pulley rig: a bronze 10 cm pulley 2 m up, a hemp rope over it, a 10 cm granite block (2.7 kg) hanging
;; 0.6 m up on one side and a 10 cm oak block (0.72 kg) on the ground on the other. The rope runs from the
;; granite's top, up its side of the pulley, over, and down to the oak's top; 1.35 + 0.3061 + 1.9 = 3.5561 m.
(define (pulley-rig diameter)
  (string-append
   "wait 40; cmd (wheel pul #:catalogue pulley-10cm #:at (0 2 0) #:material bronze); "
   "cmd (block a #:at (-0.1 0.6 0) #:size 0.1 #:material granite); "
   "cmd (block b #:at (0.1 0.05 0) #:size 0.1 #:material oak); "
   "cmd (rope r #:from (a 0 0.05 0) #:to (b 0 0.05 0) #:length 3.5561 "
   "#:over ((-0.1 2.0 0) (-0.07071 2.07071 0) (0 2.1 0) (0.07071 2.07071 0) (0.1 2.0 0)) "
   "#:turns pul #:material hemp #:diameter " diameter "); test; wait-test; quit"))

(test-case "A block lifted by a rope over a pulley: it rose as far as the weight fell, and the pulley turned that far round its rim"
  (when (godot-available?)
    ;; the granite falls from its centre 0.6 m to rest on the ground, 0.05 m: 0.55 m; the rope is that much shorter
    ;; on the oak's side, so the oak ends 0.55 m up, whatever it did on the way (it overshot to 0.82 m and fell back).
    ;; The pulley's rim keeps pace with the rope: 0.55 / (2 pi 0.1 m) = 0.8754 turns.
    (define-values (sentence facts) (run-editor (pulley-rig "0.01")))
    (check-= (fact facts "rise b") 0.55 0.01 "the oak rose as far as the granite fell")
    (check-= (fact facts "rise a") -0.55 0.01 "the granite fell 0.55 m")
    (check-= (fact facts "turns pul") (/ 0.55 (* 2 pi 0.1)) 0.01 "the pulley turned 0.8754 of a turn")
    (check-true (string-contains? sentence "The granite block fell to the ground.") sentence)
    (check-true (string-contains? sentence "The oak block rose 0.55 m") sentence)
    (check-true (string-contains? sentence "The bronze pulley turned 0.87 of a turn.") sentence)
    (check-true (string-contains? facts "broke=no"))))

(test-case "A rope too thin for the pull breaks, and the report says what it held and what pulled"
  (when (godot-available?)
    ;; hemp 0.4 mm across: 60 MPa x pi (0.2 mm)^2 = 7.5 N. While the granite pulls the oak up, the rope carries
    ;; 2 m1 m2 g / (m1 + m2) = 2 x 2.7 x 0.72 x 9.81 / 3.42 = 11.15 N, more than that: it parts in the first ticks.
    (define-values (sentence facts) (run-editor (pulley-rig "0.0004")))
    (check-true (string-contains? facts "broke=yes") facts)
    (check-= (fact facts "r max") (/ (* 2 2.7 0.72 g) (+ 2.7 0.72)) 0.3 "the pull that broke it")
    (check-= (fact facts "strength") 7.54 0.1 "what it could hold")
    (check-true (regexp-match? #px"The rope r broke after 0\\.0[0-9]* s: it holds 7\\.5 N and the pull reached 11\\.[0-9] N\\." sentence) sentence)
    (check-true (< (or (fact facts "rise b") 0) 0.01) "the oak was not lifted")))

(test-case "A rope that holds carries exactly the weight hung from it"
  (when (godot-available?)
    ;; a 2.7 kg granite block hung from a point 2 m up: 2.7 x 9.81 = 26.49 N, in a 1 mm hemp rope good for 47 N
    (define-values (sentence facts)
      (run-editor (string-append "wait 40; cmd (block a #:at (0 1.0 0) #:size 0.1 #:material granite); "
                                 "cmd (rope r #:from (world 0 2.0 0) #:to (a 0 0.05 0) #:length 0.95 #:material hemp #:diameter 0.001); "
                                 "test; wait-test; quit")))
    (check-= (fact facts "r max") (* 2.7 g) 0.1)
    (check-true (string-contains? facts "broke=no") facts)
    (check-true (string-contains? sentence "Nothing moved") sentence)))

;; A 0.05 m2 tank with 15 L (0.3 m) of water, a 10 cm2 hole at its floor draining into a lower tank, Cd 0.6:
;; Torricelli, sqrt(h) falls at k = Cd a / A sqrt(g/2) = 0.026576 per second, so it is empty at sqrt(0.3) / k = 20.61 s.
(define tank-rig
  (string-append "wait 40; cmd (tank up #:at (0 1 0) #:area 0.05 #:height 0.3 #:water 0.015 #:material limestone); "
                 "cmd (tank low #:at (0 0 0) #:area 0.1 #:height 0.5 #:water 0 #:material limestone); "
                 "cmd (leak hole #:at (0 1 0) #:on up #:height 0 #:area 0.001 #:into low); test; wait-test; quit"))
(define k (* (/ (* 0.6 0.001) 0.05) (sqrt (/ g 2))))
(define (torricelli-litres-moved t) (* 1000 0.05 (- 0.3 (expt (max 0 (- (sqrt 0.3) (* k t))) 2))))

(test-case "A tank draining into a lower one: the test runs until it is empty, and says how many litres moved"
  (when (godot-available?)
    (define-values (sentence facts) (run-editor tank-rig))
    (define t (fact facts "t"))
    (check-= (/ (sqrt 0.3) k) 20.61 0.01 "the worked time to empty")
    (check-true (< (/ (sqrt 0.3) k) t (+ (/ (sqrt 0.3) k) 2)) (format "ran until it was empty, and a second after (~a s)" t))
    (check-true (string-contains? facts "by=settled") facts)
    (check-= (fact facts "water up") -15 0.1)
    (check-= (fact facts "water low") 15 0.1)
    (check-true (string-contains? sentence "15.0 litres ran from up into low.") sentence)))

(test-case "A test stopped at a limit reports what Torricelli says has moved by then"
  (when (godot-available?)
    (define-values (sentence facts) (run-editor tank-rig #:max-seconds 10))
    (define t (fact facts "t"))
    (check-true (string-contains? facts "by=limit") facts)
    (check-= t 10 0.05)
    (check-= (fact facts "water low") (torricelli-litres-moved t) 0.15 "11 L of the 15 at 10 s")))
