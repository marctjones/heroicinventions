#lang racket/base
;; Issues #222 and #223: a first-time player can find the parts a night-warming vault needs, and can put a part under the ground
;; without typing a depth. Headless Godot at 120 Hz, the game's build mode in the opening world (the rover building: found parts hidden).
;;
;; #222. Predicted: the build-mode parts list opens short (Beam Block Ball Ramp Post Wheel Rope Water tank) with the "more parts" cue up;
;; "show all" lists, first, "Keep things warm" = enclosure, heat store, heat bin, mirror, bimetal (in that order) and "Make power" =
;; windmill, gear, water wheel, Stirling; the generator and the battery bank (found, not built) are in no group.
;; #223. The mouse path (a palette click, a click in the scene, dragging the Depth slider) is tools/hand-vault-gui.sh (opt in with
;; HEROIC_GUI_VAULT=1: it needs a window server). Here the keyboard way to a depth, which is the same (move) command: a room 0.5 m a side
;; put on the surface over the buried bank (the crate 1.04 m under the ground at (254.5, 137.9), its middle about 1.35 m under it), Shift+Page Down
;; twice sinks it 1.0 m, Page Down 0.1 m a press. Predicted: the log reads depth 0.54, 1.04, then 1.11, 1.21, 1.31 (no join), 1.41 (the room's
;; floor passes the crate's middle: "[zones] battery-bank.cells joined built-1.room"), and Page Up, 1.31: "left", Page Down again, 1.41: "joined".
;; Skipped when Godot is not installed.
(require rackunit racket/system racket/port racket/string racket/list racket/runtime-path racket/file
         (only-in heroic/godothost godot-available? godot-binary))

(define-runtime-path game-dir "../../../game")
(define-runtime-path gui-script "../../../tools/hand-vault-gui.sh")

(define (run env-list)
  (define env (environment-variables-copy (current-environment-variables)))
  (for ([kv (append '(("HEROIC_WORLD" . "lonely-rover-opening") ("HEROIC_HINTS" . "0")) env-list)])
    (environment-variables-set! env (string->bytes/utf-8 (car kv)) (string->bytes/utf-8 (cdr kv))))
  (string-split
   (parameterize ([current-directory game-dir] [current-environment-variables env])
     (with-output-to-string (λ () (system* godot-binary "--headless" "--fixed-fps" "120" "--path" "."))))
   "\n"))
(define (find rx ls) (for/or ([l ls]) (let ([m (regexp-match rx l)]) (and m (if (null? (cdr m)) (car m) (cadr m))))))
(define (index-of-line rx ls) (for/first ([l ls] [i (in-naturals)] #:when (regexp-match? rx l)) i))

(when (godot-available?)
  (define lines
    (run `(("HEROIC_INPUT" . "wait 360; build new 254.5 137.9; wait 900; quit")
           ("HEROIC_EDITOR_INPUT" .
            ,(string-append
              "wait 1; palette-list; show-all on; palette-list; "
              "cmd (enclosure room #:at (254.55 -42.88 137.9) #:size-x 0.5 #:size-y 0.5 #:size-z 0.5); select room; wait 5; "
              "key shift+pagedown; wait 5; key shift+pagedown; wait 5; key pagedown; wait 5; key pagedown; wait 5; key pagedown; wait 5; "
              "key pagedown; wait 5; key pageup; wait 5; key pagedown; wait 5")))))

  (test-case "#222: the short list says there is more, and the full list names what a vault is for"
    (define short (find #rx"palette-list: cue=shown showAll=False :: (.*)$" lines))
    (check-true (and short #t) (string-join lines "\n"))
    (check-equal? short "lever | block | ball | ramp | post | cart-wheel | rope | tank")
    (define full (find #rx"palette-list: cue=hidden showAll=True :: (.*)$" lines))
    (check-true (and full #t))
    (define rows (string-split full " | "))
    (check-equal? (take rows 6) '("== Keep things warm" "enclosure" "heat-store" "heat-bin" "mirror" "bimetal"))
    (check-equal? (list-ref rows 6) "== Make power (windmill, gears)")
    (check-equal? (take (drop rows 7) 4) '("windmill" "gear" "waterwheel" "stirling"))
    (check-false (member "generator" rows) "found parts stay out of the game's list")
    (check-false (member "battery-bank" rows)))

  (test-case "#223: Page Down sinks the selected part 0.1 m a press (Shift 0.5); the room joins the bank when its floor passes the crate's middle"
    (define depths (for/list ([l lines] #:when (regexp-match? #rx"^\\[BuildMode\\] depth: room" l)) (cadr (regexp-match #rx"depth: room depth ([0-9.]+) m below the ground$" l))))
    (check-equal? depths '("0.54" "1.04" "1.11" "1.21" "1.31" "1.41" "1.31" "1.41"))
    (define (at rx n) (for/last ([l lines] [i (in-naturals)] #:when (regexp-match? rx l)) i))
    (define joined (for/list ([l lines] [i (in-naturals)] #:when (regexp-match? #rx"battery-bank.cells joined built-1.room" l)) i))
    (define left (for/list ([l lines] [i (in-naturals)] #:when (regexp-match? #rx"battery-bank.cells left built-1.room" l)) i))
    (define (depth-line d) (index-of-line (regexp (string-append "depth: room depth " (regexp-quote d) " m")) lines))
    (check-equal? (length joined) 2)
    (check-equal? (length left) 1)
    (check-true (< (depth-line "1.31") (first joined)) "no join at 1.31 m (the first 1.31 line comes before the join)")
    (check-true (> (first joined) (depth-line "1.31")))
    (check-true (< (first joined) (first left) (second joined)) "Page Up leaves, Page Down joins again")))

(when (and (godot-available?) (getenv "HEROIC_GUI_VAULT"))
  (test-case "the vault built by mouse: no depth, no part name typed; the bank joins it"
    (define out (path->string (make-temporary-directory "hand-vault-gui~a")))
    (define log (with-output-to-string (λ () (system* gui-script out))))
    (check-true (regexp-match? #rx"palette: all parts, grouped by purpose" log))
    (check-true (regexp-match? #rx"\\[zones\\] battery-bank.cells joined built-1.enclosure_1" log) log)
    (check-true (regexp-match? #rx"set heat_bin_3 #:holds heat_store_2" log))))
