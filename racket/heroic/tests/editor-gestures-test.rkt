#lang racket/base
;; Build mode's mouse gestures, run in the real game, headless: the script
;; goes through Godot's own input pipeline (game/scripts/ScriptedInput.cs),
;; the same path a player's mouse and keys take, and build mode prints
;; where every part is and how it is turned ("[BuildMode] state: ...").
;; Skipped where Godot isn't installed (set HEROIC_GODOT).
(require rackunit racket/port racket/string racket/list heroic/godothost)

(define-values (game-dir) (simplify-path (build-path (collection-file-path "godothost.rkt" "heroic") 'up 'up 'up "game")))

;; Runs build mode with an input script; returns the state lines it printed, in order.
(define (run-editor script #:seconds [seconds 8] #:marker [marker "[BuildMode] state:"])
  (define env (environment-variables-copy (current-environment-variables)))
  (for ([kv `(("HEROIC_EDITOR" . "1") ("HEROIC_EDITOR_INPUT" . ,script)
              ("HEROIC_EDITOR_QUIT_AFTER_SECONDS" . ,(number->string seconds)))])
    (environment-variables-set! env (string->bytes/utf-8 (car kv)) (string->bytes/utf-8 (cdr kv))))
  (define out
    (parameterize ([current-environment-variables env] [current-directory game-dir])
      (define-values (p stdout stdin stderr)
        (subprocess #f #f #f godot-binary "--headless" "--resolution" "1280x800" "."))
      (close-output-port stdin)
      (define drain (thread (λ () (port->string stderr))))   ; read both, or a full pipe stalls Godot
      (begin0 (port->string stdout) (subprocess-wait p) (thread-wait drain) (close-input-port stdout) (close-input-port stderr))))
  (for/list ([l (string-split out "\n")] #:when (string-contains? l marker)) l))

;; "ball_2@(-0.2 0.05 -0.1)^330" -> its heading, or #f; id-prefix "ball_" matches ball_ and its number
(define (heading-of state id-prefix)
  (define m (regexp-match (pregexp (string-append (regexp-quote id-prefix) "[0-9]*@\\([^)]*\\)\\^([-0-9.]+)")) state))
  (and m (string->number (cadr m))))
(define (has-part? state id) (string-contains? state (string-append id "@")))

(test-case "A part dragged out of the palette is placed where it is let go, and the list lets go of the mouse"
  (when (godot-available?)
    (define states (run-editor "wait 40; show-all; wait 5; palette-drag boiler 760 420; wait 5; log; palette-drag hearth 600 470; wait 5; log"))
    (check-equal? (length states) 2)
    (check-true (has-part? (first states) "boiler_1") "the boiler is placed")
    (check-true (has-part? (second states) "hearth_2") "a second drag still works: the list didn't keep the mouse")))

(test-case "R-drag turns the selected part: 15 degree steps with grid snap, free without, one undo a turn"
  ;; half a degree a viewport pixel, dragging right turning it clockwise (negative); the script's pixels
  ;; are window pixels, px viewport pixels each (1.25 in a 1280 x 800 window; more headless)
  (when (godot-available?)
    (define states
      (run-editor (string-append "wait 40; palette ball; wait 3; move 600 470; wait 3; down 600 470; up 600 470; wait 5; log; "
                                 "press r; drag 600 470 602 470; release r; wait 5; log; "      ; a little right, grid on
                                 "press r; drag 600 470 596 470; release r; wait 5; log; "      ; a little left
                                 "key g; press r; drag 600 470 601 470; release r; wait 5; log; " ; grid off: free
                                 "key ctrl+z; wait 3; log")))
    (check-equal? (length states) 5)
    (define px (string->number (cadr (regexp-match #px"px=([0-9.]+)" (first states)))))
    (define (h i) (heading-of (list-ref states i) "ball_"))
    (define (snap15 d) (* 15 (round (/ d 15))))
    (define (norm d) (let ([m (- d (* 360 (floor (/ d 360))))]) m))
    (define t1 (snap15 (* -0.5 2 px)))
    (define t2 (snap15 (* -0.5 -4 px)))
    (define t3 (* -0.5 1 px))
    (check-true (not (zero? t1)) (format "the first drag is long enough to turn a step (px ~a)" px))
    (check-= (h 0) 0 1e-6 "placed unturned")
    (check-= (h 1) (norm t1) 1e-3 "a step clockwise")
    (check-= (h 2) (norm (+ t1 t2)) 1e-3 "steps back")
    (check-= (h 3) (norm (+ t1 t2 t3)) 1e-3 "free, by the pixel")
    (check-= (h 4) (norm (+ t1 t2)) 1e-3 "undo takes back the whole free turn")))

;; #178, #179: select a part and the parts it can join light up, each with the dot it would join;
;; the words show beside the selected part's dots and the candidates' dots. The game's own state is
;; printed by the "candidates" script command ("select" is a click on a part, or on a dot: a pipe started).
(test-case "Selecting a tank, a pulley or a pipe end lights what it can join, with words on the dots"
  (when (godot-available?)
    (define setup (string-append "wait 40; cmd (tank t1 #:at (0 0 0)); cmd (tank t2 #:at (1 0 0)); cmd (boiler b1 #:at (0 0 1)); "
                                 "cmd (rotor r1 #:at (1 0 1)); cmd (wheel w1 #:at (0 0 2) #:catalogue pulley-10cm); "
                                 "cmd (wheel w2 #:at (0.3 0 2) #:catalogue pulley-10cm); cmd (block k1 #:at (-1 0 0)); wait 3; "))
    (define lines (run-editor (string-append setup "select k1; candidates; select t1; candidates; select t1.outlet; candidates; "
                                             "select w1; candidates; select b1; candidates; quit")
                              #:marker "[BuildMode] candidates:"))
    (check-equal? (length lines) 5)
    (define (field line name) (cadr (regexp-match (pregexp (string-append name "=(\\S*)")) line)))
    (define (words line) (cadr (regexp-match #px"words=(.*)$" line)))
    (check-equal? (field (first lines) "lit") "" "a block joins nothing by a dot or a tool the lights know of")
    (check-equal? (field (second lines) "lit") "t2" "a selected tank lights the other tank, not the boiler, rotor, pulleys or block")
    (check-equal? (words (second lines)) "water in / out|water in / out" "its own dots and the other tank's say what they are")
    (check-true (regexp-match? #rx"t2[.]pipe[.]inlet" (second lines)) "the dot it would join is named")
    (check-equal? (field (third lines) "lit") "t2" "a pipe started from a dot lights the tank it can run to")
    (check-false (regexp-match? #rx"shared air" (third lines)) "a pipe from a dot offers pipes only")
    (check-equal? (field (fourth lines) "lit") "w2" "a selected pulley lights the other pulley")
    (check-true (regexp-match? #rx"w2[.]gear mesh" (fourth lines)) "and says it could mesh")
    (check-equal? (words (fourth lines)) "" "a pulley has no dots to label")
    (check-equal? (field (fifth lines) "lit") "r1" "a selected boiler lights the rotor")
    (check-equal? (words (fifth lines)) "steam in|steam out")))
