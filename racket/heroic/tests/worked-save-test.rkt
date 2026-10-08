#lang racket/base
;; Issue #199: the rover's trenches and heaps (#63) survive a save, through the game's own save and load (headless Godot, Jolt, 120 Hz)
;; in the crater world. Predictions: the same dig run straight through (a backhoe cycle, the rover put back on the same spot, a second
;; cycle) and run as cycle, SAVE, quit, LOAD, the rover put on the same spot, a second cycle must leave the same worked ground, node
;; for node (the save writes numbers that read back as the same double, so the difference worked out beforehand is 0): heights, carry
;; ceilings, loose flags, rock and soil. The first cycle digs 0.2 m3 and tips it (dug 0.2, dumped 0.2); the second digs 0.2 more and
;; is refused a dump 0.12 m above where it was dug (the ceiling rule survives): dug 0.4, dumped 0.2. After the load the rover, put
;; on the dug ground, stands tilted otherwise than on the undug ground (pitch 9.4 undisturbed, -2.8 on the hole): the view rebuilt
;; the patch's body.
;;
;; Issue #201: the save also holds the rover. A save made mid-carry (the arm Swinging with 0.2 m3 in the bucket, the soil already
;; scraped from the ground) and loaded must finish the dump as the run that kept going: the same worked ground node for node (0),
;; the rover on the same spot (the loaded one lacks only Jolt's warm-start impulses, so its pose agrees to within the rover's own
;; settling creep, 1e-3 m; measured 1.1e-4), the same totals. Ground plus bucket is conserved: dug - dumped = carried at the save,
;; and the nodes' heights (x 0.25 m cell squared) gain exactly the 0.2 m3 the dump puts back, in the kept run and the loaded run.
;; Loading and saving at once writes the rover's section back as it was. A save without a (rover ...) section loads with the
;; world file's rover.
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
    (check-true (for/or ([l (rover-lines loaded)]) (regexp-match? #rx"won't dump" l)) "refused above where it was dug, after the load"))

  ;; ---- #201: the rover itself ----
  (define (rover-of path)
    (or (for/first ([l (in-list (file->lines path))] #:when (string-prefix? l "  (rover ")) l)
        (error 'worked-save "no (rover ...) in ~a" path)))
  (define (rnum s key) (string->number (cadr (regexp-match (pregexp (string-append "\\(" key " ([^() ]+)\\)")) s))))
  (define (xf-of s tag) (map string->number (string-split (cadr (regexp-match (pregexp (string-append "\\(" tag " \\(xf ([^()]*)\\)")) s)))))
  (define (height-sum path) (for/sum ([h (in-list (numbers-after (worked-of path) "heights"))]) h))
  (define fine-area (* 0.25 0.25))
  (define run201 "wait 120; rover place 190 140 270; wait 120; key b; rover until Swinging; wait 30; ")
  (define kept2 (run-game (string-append run201 "rover save " (p "mid.save") "; rover until Stowed; wait 600; rover save " (p "kept2.save") "; rover; quit")))
  (define relo (run-game (string-append "rover save " (p "re.save") "; quit") #:env `(("HEROIC_LOAD" . ,(p "mid.save")))))
  (define loaded2 (run-game (string-append "rover until Stowed; wait 600; rover save " (p "loaded2.save") "; rover; quit") #:env `(("HEROIC_LOAD" . ,(p "mid.save")))))
  ;; (the rover's line is the last in a save, and ends with the save's own close paren)
  (define old-text (string-join (for/list ([l (in-list (file->lines (p "mid.save")))]) (if (string-prefix? l "  (rover ") ")" l)) "\n"))
  (with-output-to-file (p "old.save") #:exists 'truncate (λ () (display old-text) (newline)))
  (define plain (run-game "wait 120; rover; quit"))
  (define old-loaded (run-game "wait 120; rover; quit" #:env `(("HEROIC_LOAD" . ,(p "old.save")))))

  (test-case "a save made mid-carry holds the rover: pose, bucket load, carry ceiling and the arm's place in its cycle"
    (define r (rover-of (p "mid.save")))
    (check-= (rnum r "carried") 0.2 1e-9 "0.2 m3 in the bucket")
    (check-true (< (rnum r "ceiling") -60 ) "a finite carry ceiling, the level of the soil it took")
    (check-equal? (rnum r "step") 4.0 "Swinging is the fifth step of the cycle")
    (check-true (> (rnum r "time") 0.2) "and the arm is a quarter second into it")
    (check-equal? (length (regexp-match* #rx"\\(wheel " r)) 6 "six wheels")
    (check-= (list-ref (xf-of r "chassis") 9) 189.977 1e-2 "the chassis is where it was placed")
    (define w (worked-of (p "mid.save")))
    (check-= (- (car (numbers-after w "dug")) (car (numbers-after w "dumped"))) (rnum r "carried") 1e-9 "ground lost = bucket held"))

  (test-case "loading and saving at once writes the rover as it was, and keeps the world's other entries"
    ;; the bodies are put back as they were; by the time the script's first step runs, Jolt has stepped them once without the
    ;; constraints' warm-start impulses, so they agree to the rover's own settling creep. Drive, bucket and arm are numbers: exact.
    (define (section r key) (cadr (regexp-match (pregexp (string-append "(\\(" key " .*)$")) r)))
    (define re (rover-of (p "re.save"))) (define mid (rover-of (p "mid.save")))
    (check-equal? (section re "drive") (section mid "drive") "drive, bucket and arm exactly")
    (for ([tag '("chassis" "wheel 0.0" "wheel 5.0")])
      (define d (apply max (map (λ (x y) (abs (- x y))) (xf-of re tag) (xf-of mid tag))))
      (check-true (< d 1e-3) (format "~a back to within ~a" tag d)))
    (check-equal? (worked-of (p "re.save")) (worked-of (p "mid.save")))
    (check-true (regexp-match? #rx"\\(machine battery-bank " (file->string (p "re.save"))))
    (check-false (for/or ([l relo]) (regexp-match? #rx"found nothing to set|could not load" l))))

  (test-case "a save made mid-carry, loaded, finishes the dump as the run that kept going"
    (define a (worked-of (p "kept2.save"))) (define b (worked-of (p "loaded2.save")))
    (check-= (car (numbers-after a "dumped")) 0.2 1e-9 "the kept run tipped it")
    (check-equal? (numbers-after b "dumped") (numbers-after a "dumped"))
    (check-equal? (numbers-after b "dug") (numbers-after a "dug"))
    (check-equal? (numbers-after b "heights") (numbers-after a "heights") "heights identical, node for node")
    (for ([f '("carry" "rock" "soil" "loose")]) (check-equal? (field b f) (field a f) f))
    (define ra (rover-of (p "kept2.save"))) (define rb (rover-of (p "loaded2.save")))
    (for ([tag '("chassis" "wheel 0.0" "wheel 1.0" "wheel 2.0" "wheel 3.0" "wheel 4.0" "wheel 5.0")])
      (define d (apply max (map (λ (x y) (abs (- x y))) (xf-of ra tag) (xf-of rb tag))))
      (check-true (< d 1e-3) (format "~a: pose differs by ~a" tag d)))
    (for ([key '("carried" "dug" "dumped" "cycles" "step")]) (check-equal? (rnum rb key) (rnum ra key) key))
    (check-true (for/or ([l loaded2]) (regexp-match? #rx"arm: Dumped 0.20 m³, bucket 0.00 m3" l)) "the loaded arm dumped it"))

  (test-case "ground plus bucket volume is conserved across the load"
    (define fell (* fine-area (- (height-sum (p "kept2.save")) (height-sum (p "mid.save")))))
    (define fell2 (* fine-area (- (height-sum (p "loaded2.save")) (height-sum (p "mid.save")))))
    (check-= fell 0.2 1e-6 "the dump puts back exactly the bucket's 0.2 m3 (kept)")
    (check-= fell2 0.2 1e-6 "and in the loaded run")
    (check-= (height-sum (p "re.save")) (height-sum (p "mid.save")) 0 "loading itself moves no earth")
    (check-= (rnum (rover-of (p "re.save")) "carried") (rnum (rover-of (p "mid.save")) "carried") 0 "nor the bucket"))

  (test-case "an older save with no rover section loads with the world file's rover"
    (define (at l) (cadr (regexp-match #rx"at \\(([^)]*)\\)" l)))
    (check-true (for/or ([l old-loaded]) (regexp-match? #rx"^\\[save\\] loaded " l)))
    (check-equal? (at (first (rover-lines old-loaded))) (at (first (rover-lines plain))) "it stands where the world puts it")))
