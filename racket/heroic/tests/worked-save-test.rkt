#lang racket/base
;; Issue #199: the rover's trenches and heaps (#63) survive a save, through the game's own save and load (headless Godot, Jolt, 120 Hz)
;; in the crater world. Predictions: the same dig run straight through (a backhoe cycle, the rover put back on the same spot, a second
;; cycle) and run as cycle, SAVE, quit, LOAD, the rover put on the same spot, a second cycle must leave the same worked ground, node
;; for node (the save writes numbers that read back as the same double, so the difference worked out beforehand is 0): heights, carry
;; ceilings, loose flags, rock and soil. The first cycle digs 0.2 m3 and tips it (dug 0.2, dumped 0.2); the second digs 0.2 more and
;; is refused a dump 0.12 m above where it was dug (the ceiling rule survives): dug 0.4, dumped 0.2. After the load the rover, put
;; on the dug ground, stands tilted otherwise than on the undug ground (pitch 9.4 undisturbed, -2.8 on the hole): the view rebuilt
;; the patch's body.
;; Skipped when Godot is not installed.
(require rackunit racket/system racket/port racket/string racket/list racket/file racket/runtime-path
         (only-in heroic/godothost godot-available? godot-binary))

(define-runtime-path game-dir "../../../game")

(define (run-game script #:env [extra '()])
  (define env (environment-variables-copy (current-environment-variables)))
  (environment-variables-set! env #"HEROIC_WORLD" #"lonely-rover-opening")
  (environment-variables-set! env #"HEROIC_INPUT" (string->bytes/utf-8 script))
  (for ([kv extra]) (environment-variables-set! env (string->bytes/utf-8 (car kv)) (string->bytes/utf-8 (cdr kv))))
  (string-split
   (parameterize ([current-directory game-dir] [current-environment-variables env])
     (with-output-to-string (λ () (system* godot-binary "--headless" "--fixed-fps" "120" "--path" "."))))
   "\n"))

(define (worked-of path)   ; the (worked ...) line of a save
  (define l (for/first ([l (in-list (file->lines path))] #:when (string-prefix? l "  (worked")) l))
  (or l (error 'worked-save "no (worked ...) in ~a" path)))
(define (numbers-after s key)   ; the numbers in the (key n n ...) list
  (map string->number (string-split (cadr (regexp-match (pregexp (string-append "\\(" key " ([^()]*)\\)")) s)))))
(define (field s key) (cadr (regexp-match (pregexp (string-append "\\(" key "((?: [^()\\s]+|\\s*\\([^()]*\\))*)\\)")) s)))
(define (rover-lines lines) (filter (λ (l) (string-prefix? l "[view] rover:")) lines))
(define (pitch l) (string->number (cadr (regexp-match #rx"pitch (-?[0-9.]+)" l))))

(when (godot-available?)
  (define dir (make-temporary-file "worked-save~a" 'directory))
  (define (p f) (path->string (build-path dir f)))
  (define cycle "key b; wait 60; rover until Stowed; wait 60")
  (define put "rover place 190 140 270")

  (define kept (run-game (string-append "wait 120; " put "; wait 120; " cycle "; wait 2400; " put "; wait 120; rover; " cycle "; wait 3000; rover; quit")
                         #:env `(("HEROIC_SAVE" . ,(p "kept.save")) ("HEROIC_SAVE_AT" . "40"))))
  (define first-half (run-game (string-append "wait 120; rover; " put "; wait 120; " cycle "; wait 3000; quit")
                               #:env `(("HEROIC_SAVE" . ,(p "s1.save")) ("HEROIC_SAVE_AT" . "20"))))
  (define loaded (run-game (string-append "wait 120; " put "; wait 120; rover; " cycle "; wait 4000; rover; quit")
                           #:env `(("HEROIC_LOAD" . ,(p "s1.save")) ("HEROIC_SAVE" . ,(p "s2.save")) ("HEROIC_SAVE_AT" . "35"))))

  (test-case "a save made after one cycle carries the worked patch, and loading draws it again"
    (define w1 (worked-of (p "s1.save")))
    (check-= (car (numbers-after w1 "dug")) 0.2 1e-9)
    (check-= (car (numbers-after w1 "dumped")) 0.2 1e-9)
    (check-true (for/or ([l loaded]) (regexp-match? #rx"^\\[terrain\\] 1 worked patch" l)) "the view made the loaded patch's mesh and body")
    ;; every saved entry found its place: the cargo crates' views kept their labels through the reload (they were named
    ;; @Node3D@390.. while the old views still held the names, so no crate's state came back), and the save written after
    ;; the load names them as the world does
    (check-true (for/or ([l loaded]) (regexp-match? #rx"^\\[save\\] loaded " l)) "it loaded")
    (check-false (for/or ([l loaded]) (regexp-match? #rx"found nothing to set" l)) "every entry of the save found its place")
    (check-true (regexp-match? #rx"\\(machine battery-bank " (file->string (p "s2.save"))) "the save after a load keeps the crates' labels"))

  (test-case "the loaded rover stands in the hole, which is not the ground that was there"
    (define dug (pitch (first (rover-lines loaded)))) (define undug (pitch (first (rover-lines first-half))))
    ;; the patch's collision came back with the load: set at the same place, the rover leans 3.4 degrees less on the dug
    ;; ground than on the ground that was there (5.5 against 8.9 since its wheels are sprung, #198; -2.8 against 9.4
    ;; on rigid wheels, which rested on two corners of the hole)
    (check-true (> undug 8) (format "pitch on the undug slope ~a" undug))
    (check-true (< dug (- undug 2)) (format "pitch on the dug ground ~a, against ~a undug" dug undug)))

  (test-case "after a second cycle the loaded run's worked ground is the kept run's, node for node"
    (define a (worked-of (p "kept.save")))
    (define b (worked-of (p "s2.save")))
    (check-= (car (numbers-after a "dug")) 0.4 1e-9)
    (check-= (car (numbers-after a "dumped")) 0.2 1e-9 "the second dump was refused: the ceiling rule")
    (check-equal? (numbers-after b "dug") (numbers-after a "dug"))
    (check-equal? (numbers-after b "dumped") (numbers-after a "dumped"))
    (check-equal? (numbers-after b "heights") (numbers-after a "heights") "heights identical")
    (for ([f '("carry" "rock" "soil" "loose")])
      (check-equal? (field b f) (field a f) f))
    (check-true (for/or ([l (rover-lines loaded)]) (regexp-match? #rx"won't dump" l)) "refused above where it was dug, after the load")))
