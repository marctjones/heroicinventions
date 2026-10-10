#lang racket/base
;; Issues #243 and #244: the ground key on the navigation map and the Driving section's "ground here" line. Each case runs the real game
;; headless (Jolt, 120 Hz) with the game's scripted steps (game/scripts/Main.Markers.cs: "map open", "map print"; Main.Rover.cs: "rover ground").
;; The numbers are worked out beforehand, not read back from the game:
;;   colours   each soil is drawn at 85% of its table colour's saturation, same hue and value (TerrainView.SoilColour -> SoilLook.Drawn). This
;;             file does the same arithmetic itself from racket/heroic/materials.rktd (the table the game's materials.json is made from) and
;;             checks that, for every soil of the crater, the key's swatch, the colour the ground's own cell texture holds in the middle of a
;;             patch of that soil, and the map's texel there are all that colour (to one byte): the key cannot drift from the shader's colour.
;;   under the rover   (180, 140) is the crater floor, basalt sand: "ground here: basalt sand (dark)"; (0, 345) is the wall's foot, bedrock:
;;             "ground here: bedrock (grey-brown)" (the soil table of racket/maps/victoria.rkt, ground-soil, by azimuth and share of the radius).
;;   only the soils the map has   the 1 m dig-bank map has one soil (ice-cemented regolith): its key has that one.
;; Skipped when Godot is not installed.
(require rackunit racket/system racket/port racket/string racket/list racket/file racket/runtime-path racket/math
         (only-in heroic/godothost godot-available? godot-binary))

(define-runtime-path game-dir "../../../game")
(define-runtime-path materials-file "../materials.rktd")

(define (run-game world script)
  (define env (environment-variables-copy (current-environment-variables)))
  (environment-variables-set! env #"HEROIC_WORLD" (string->bytes/utf-8 world))
  (environment-variables-set! env #"HEROIC_INPUT" (string->bytes/utf-8 script))
  (environment-variables-set! env #"HEROIC_HINTS" #"0")
  (define out
    (parameterize ([current-directory game-dir] [current-environment-variables env])
      (with-output-to-string
        (λ () (system* godot-binary "--headless" "--fixed-fps" "120" "--path" ".")))))
  (string-split out "\n"))

;; the table's colour of a material, "#RRGGBB" -> (r g b) in 0..1
(define (table-colour material)
  (define entries (call-with-input-file materials-file (λ (in) (let loop ([acc '()]) (define d (read in)) (if (eof-object? d) (reverse acc) (loop (cons d acc)))))))
  (define entry (for/first ([e entries] #:when (and (pair? e) (eq? (car e) material))) e))
  (define hex (cadr (assq 'color (cdr entry))))
  (for/list ([i '(1 3 5)]) (/ (string->number (substring hex i (+ i 2)) 16) 255.0)))

;; 85% of the saturation, same hue and value
(define (drawn rgb)
  (define-values (r g b) (values (first rgb) (second rgb) (third rgb)))
  (define mx (max r g b))
  (define mn (min r g b))
  (define d (- mx mn))
  (cond
    [(or (<= mx 0) (<= d 0)) rgb]
    [else
     (define s (* (/ d mx) 0.85))
     (define c (* mx s))
     (define m (- mx c))
     (define h (cond [(= mx r) (+ (/ (- g b) d) (if (< g b) 6 0))] [(= mx g) (+ (/ (- b r) d) 2)] [else (+ (/ (- r g) d) 4)]))
     (define x (* c (- 1 (abs (- (modulo* h 2) 1)))))
     (case (inexact->exact (floor h))
       [(0) (list (+ c m) (+ x m) m)] [(1) (list (+ x m) (+ c m) m)] [(2) (list m (+ c m) (+ x m))]
       [(3) (list m (+ x m) (+ c m))] [(4) (list (+ x m) m (+ c m))] [else (list (+ c m) m (+ x m))])]))
(define (modulo* a n) (- a (* n (floor (/ a n)))))

(define (hex->bytes h) (for/list ([i '(1 3 5)]) (string->number (substring h i (+ i 2)) 16)))
(define (->bytes rgb) (for/list ([v rgb]) (inexact->exact (round (* 255 v)))))
(define (close? a b) (for/and ([x a] [y b]) (<= (abs (- x y)) 1)))

;; "[map] key basalt-sand: swatch #474242 ground #474242 map #474242 name "basalt sand" note "..."" -> (material swatch ground map name)
(define (key-line l)
  (define m (regexp-match #rx"^\\[map\\] key ([a-z-]+): swatch (#[0-9A-F]+) ground (#[0-9A-F]+) map (#[0-9A-F]+) name \"([^\"]*)\" note \"([^\"]*)\"" l))
  (and m (list (string->symbol (list-ref m 1)) (list-ref m 2) (list-ref m 3) (list-ref m 4) (list-ref m 5) (list-ref m 6))))

(when (godot-available?)

  (test-case "#243: the key shows each of the crater's six soils in the colour the ground is drawn in, and the line says what is under the rover"
    (define lines
      (run-game "lonely-rover-opening"
                (string-append "wait 240; rover place 180 140 270; wait 60; rover ground; rover place 0 345 180; wait 60; rover ground;"
                               " map open; wait 30; map print; quit")))
    (define grounds (filter (λ (l) (string-prefix? l "[view] ground here")) lines))
    (check-equal? grounds '("[view] ground here: basalt sand (dark)" "[view] ground here: bedrock (grey-brown)"))
    (define keys (filter values (map key-line lines)))
    (check-equal? (sort (map (λ (k) (symbol->string (first k))) keys) string<?)
                  '("basalt-sand" "bedrock" "ice-cemented-regolith" "regolith" "silica-sand" "sublimed-regolith")
                  "all six soils of the crater, no more")
    (for ([k keys])
      (define want (->bytes (drawn (table-colour (first k)))))
      (check-true (close? (hex->bytes (second k)) want) (format "~a swatch ~a, worked out from the table ~a" (first k) (second k) want))
      (check-true (close? (hex->bytes (third k)) want) (format "~a: the ground's own cell colour ~a" (first k) (third k)))
      (check-true (close? (hex->bytes (fourth k)) want) (format "~a: the map's texel ~a" (first k) (fourth k)))
      ;; words for a player: no hyphenated ids, no digits or capitals in what the key says
      (check-false (regexp-match? #rx"[_0-9A-Z]" (string-append (fifth k) (sixth k))) (format "~a: ~a / ~a" (first k) (fifth k) (sixth k)))
      (check-false (regexp-match? #rx"-sand|-regolith|-cemented-" (sixth k)) (format "~a: ~a" (first k) (sixth k)))))

  (test-case "#243: a map with one soil has a key of one"
    (define lines (run-game "rover-dig-bank" "wait 60; rover ground; map open; wait 30; map print; quit"))
    (check-equal? (filter (λ (l) (string-prefix? l "[view] ground here")) lines) '("[view] ground here: ice-cemented soil (pale)"))
    (define keys (filter values (map key-line lines)))
    (check-equal? (map first keys) '(ice-cemented-regolith))))
