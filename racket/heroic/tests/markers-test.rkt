#lang racket/base
;; Issues #235-#239 with the owner decision of #240 (the rover knows ROUGHLY where each crate landed, never its depth): the cargo's rough
;; areas, the bearing and distance readout, the navigation map. Each case runs the real game headless (Jolt, 120 Hz) on the Lonely Rover's
;; opening and uses the game's scripted steps (game/scripts/Main.Markers.cs: "marker", "marker place X Z", "map open", "map print").
;; The numbers are worked out beforehand:
;;   areas     the cargo crates settle within 8 s of the rim coming down, at (254.49 137.90) bank, (265.75 143.50) solar panels,
;;             (249.38 144.00) gas cylinders, (241.30 139.74) hand tools, (233.71 134.99) motors (a 120 s trace of the opening). Each
;;             disc has radius 12 and its centre is the crate's place plus an offset of 12/6 to 12/3 = 2 to 4 m from FNV-1a of the
;;             crate's label, the same in every run: centres (251.776 139.095) (261.850 143.438) (249.827 146.457) (243.441 141.448)
;;             (234.678 132.198), worked out by an independent FNV-1a, and pinned in tests/HeroicInventions.Sim.Tests/MarkerTests.cs.
;;   readout   rover at (200, 140) facing east (compass 090): a marker at (230, 140) is 30.0 m away, bearing 090, dead ahead (0);
;;             at (200, 110) 30.0 m, bearing 000, 90 to the left (-90); at (230, 170) sqrt(1800) = 42.43 m, bearing 135, 45 to the right.
;;             North is -z, east +x, bearings clockwise from north.
;;   map       a place's map position is its world position through one transform (world -> screen -> world is the identity), and the map
;;             opens without pausing the game: the arrow keys still drive the rover (it covers more than 1 m in a second).
;; Skipped when Godot is not installed.
(require rackunit racket/system racket/port racket/string racket/list racket/file racket/runtime-path
         (only-in heroic/godothost godot-available? godot-binary))

(define-runtime-path game-dir "../../../game")

(define (run-game world script #:env [extra '()])
  (define env (environment-variables-copy (current-environment-variables)))
  (environment-variables-set! env #"HEROIC_WORLD" (string->bytes/utf-8 world))
  (environment-variables-set! env #"HEROIC_INPUT" (string->bytes/utf-8 script))
  (for ([kv extra]) (environment-variables-set! env (string->bytes/utf-8 (car kv)) (string->bytes/utf-8 (cdr kv))))
  (define out
    (parameterize ([current-directory game-dir] [current-environment-variables env])
      (with-output-to-string
        (λ () (system* godot-binary "--headless" "--fixed-fps" "120" "--path" ".")))))
  (string-split out "\n"))

(define (lines-with lines prefix) (filter (λ (l) (string-prefix? l prefix)) lines))
(define (nums s) (map string->number (regexp-match* #rx"-?[0-9]+[.][0-9]+|-?[0-9]+" s)))
;; "[marker] target test: distance 30.01 m bearing 90.0 deg heading 90.2 deg relative -0.2 deg" -> (distance bearing relative)
(define (reading line)
  (define m (regexp-match #rx"distance (-?[0-9.]+) m bearing (-?[0-9.]+) deg heading (-?[0-9.]+) deg relative (-?[0-9.]+) deg" line))
  (map string->number (list (list-ref m 1) (list-ref m 2) (list-ref m 4))))

(define labels '("battery-bank" "solar-panels" "gas-cylinders" "hand-tools" "motors"))
(define settled   ; crate places at 40 s
  (hash "battery-bank" '(254.49 137.90) "solar-panels" '(265.75 143.50) "gas-cylinders" '(249.38 144.00)
        "hand-tools" '(241.30 139.74) "motors" '(233.71 134.99)))
(define centres
  (hash "battery-bank" '(251.776 139.095) "solar-panels" '(261.850 143.438) "gas-cylinders" '(249.827 146.457)
        "hand-tools" '(243.441 141.448) "motors" '(234.678 132.198)))

(when (godot-available?)

  (test-case "#239: distance, bearing and the arrow to a marker at a known offset (to 0.1 m and 1 degree)"
    (define lines
      (run-game "lonely-rover-opening"
                (string-append "wait 240; rover place 200 140 270; wait 60;"
                               " marker place 230 140; marker; marker place 200 110; marker; marker place 230 170; marker; quit")))
    (define targets (map reading (lines-with lines "[marker] target test")))
    (check-equal? (length targets) 3)
    (for ([t targets] [want '((30.0 90.0 0.0) (30.0 0.0 -90.0) (42.43 135.0 45.0))])
      (check-= (first t) (first want) 0.1 (format "distance ~a" t))
      (check-= (second t) (second want) 1.0 (format "bearing ~a" t))
      (check-= (third t) (third want) 1.0 (format "relative angle ~a" t)))
    ;; the panel's own words
    (define text (string-join (lines-with lines "[marker] target test") "\n"))
    (check-true (string-contains? text "bearing 090° E · dead ahead") text)
    (check-true (string-contains? text "bearing 000° N · 90° left") text)
    (check-true (string-contains? text "bearing 135° SE · 45° right") text))

  (test-case "#236: five rough areas of radius 12, the crate inside each, the centre never the spot, matching the trace after 40 s, no depth said"
    (define dir (make-temporary-file "markers~a" 'directory))
    (define trace (path->string (build-path dir "t")))
    (define lines
      (run-game "lonely-rover-opening" "wait 4850; marker; quit"
                #:env `(("HEROIC_TRACE" . ,trace) ("HEROIC_TRACE_DT" . "1"))))
    (define areas (lines-with lines "[marker] "))
    (check-equal? (length (filter (λ (l) (regexp-match? #rx"crate \\(" l)) areas)) 5)
    (for ([label labels])
      (define l (for/first ([l areas] #:when (string-prefix? l (string-append "[marker] " label ":"))) l))
      (check-true (string? l) label)
      (define m (regexp-match #rx"centre \\(([-0-9.]+) ([-0-9.]+)\\) radius ([0-9.]+) crate \\(([-0-9.]+) ([-0-9.]+)\\) offset ([0-9.]+) inside (yes|no) t ([0-9.]+)" l))
      (define-values (cx cz r crx crz off inside t) (apply values (cdr m)))
      (check-= (string->number r) 12.0 1e-9 "the radius")
      (check-equal? inside "yes" (format "~a: the crate lies inside its own disc" label))
      (check-true (<= 2.0 (string->number off) 4.001) (format "~a: offset ~a is a sixth to a third of the radius" label off))
      (check-= (string->number cx) (first (hash-ref centres label)) 0.05 (format "~a centre x" label))
      (check-= (string->number cz) (second (hash-ref centres label)) 0.05 (format "~a centre z" label))
      ;; the marker follows its crate: the place the disc was made from is the crate's trace position (within 0.5 m)
      (check-= (string->number crx) (first (hash-ref settled label)) 0.5 (format "~a crate x" label))
      (check-= (string->number crz) (second (hash-ref settled label)) 0.5 (format "~a crate z" label))
      (define trace-line (last (filter (λ (s) (string-contains? s "(crate.x ")) (file->lines (string-append trace "." label)))))
      (define tx (string->number (cadr (regexp-match #rx"[(]crate[.]x ([-0-9.e]+)[)]" trace-line))))
      (define tz (string->number (cadr (regexp-match #rx"[(]crate[.]z ([-0-9.e]+)[)]" trace-line))))
      (check-= (string->number crx) tx 0.5 (format "~a: the marker's crate against the trace at t ~a" label t))
      (check-= (string->number crz) tz 0.5 (format "~a: same, z" label)))
    ;; nothing says how deep a crate lies or under what
    (check-false (for/or ([l areas]) (regexp-match? #rx"(?i:down|cover|deep|depth|buried)" l)) "no depth in what the markers say"))

  (test-case "#236: markers can be turned off (the Driving line says so)"
    (define lines
      (run-game "lonely-rover-opening"
                "wait 240; marker choose battery-bank; marker; markers off; marker; quit"))
    (define targets (lines-with lines "[marker] target"))
    (check-equal? (length targets) 2)
    (check-true (string-contains? (first targets) "marker: battery bank") (first targets))
    (check-true (string-contains? (second targets) "marker: off") (second targets)))

  (test-case "#237: a place's map position is its world position, the map fits the clear area and does not pause the game"
    (define lines
      (run-game "lonely-rover-opening"
                (string-append "wait 240; map open; wait 30; map print; rover; hold up 1; wait 130; rover; map print; quit")))
    (define prints (lines-with lines "[map] "))
    (check-true (for/or ([l prints]) (string-prefix? l "[map] open")) "the map opened")
    (define rect (nums (cadr (regexp-match #rx"rect [(]([-0-9. ]+)[)]" (first (lines-with lines "[map] open"))))))
    (check-true (and (>= (first rect) 296) (<= (third rect) 1220) (>= (second rect) 0) (<= (fourth rect) 1000))
                (format "inside the clear area between the panels (296 to 1220): ~a" rect))
    (define crates (filter (λ (l) (regexp-match? #rx"^\\[map\\] (battery-bank|solar-panels|gas-cylinders|hand-tools|motors):" l)) prints))
    (check-equal? (length crates) 10)   ; five, printed twice
    (for ([l crates])
      (define n (map string->number (cdr (regexp-match #rx"world [(]([-0-9.]+) ([-0-9.]+)[)] map [(]([-0-9.]+) ([-0-9.]+)[)] back [(]([-0-9.]+) ([-0-9.]+)[)]" l))))
      (check-= (first n) (fifth n) 1e-3 l)
      (check-= (second n) (sixth n) 1e-3 l))
    (define rovers (lines-with lines "[view] rover:"))
    (define (rover-x l) (string->number (cadr (regexp-match #rx"at [(]([-0-9.]+) " l))))
    (check-true (> (abs (- (rover-x (second rovers)) (rover-x (first rovers)))) 1.0)
                "the map is open and the up arrow still drives the rover: the game runs on")))
