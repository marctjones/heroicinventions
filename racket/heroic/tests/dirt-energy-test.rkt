#lang racket/base
;; Issue #72's energy audit of the rover's dirt, in the game's own physics (Jolt, 120 Hz), by game/scenes/DirtEnergyEval.tscn,
;; which prints "EVAL <name> <value>" lines. The rover may move soil anywhere (owner decision 2026-10-08); dirt must not hand
;; energy to a body through a quirk of the model. Granite blocks of 0.5 m (337.5 kg) rest on the ground while it is changed round
;; them; each block's energy, 1/2 m v^2 + 1/2 I w^2 + m g y at g = 9.81, is read against its energy at rest. The floor of any
;; reading is Jolt's float32 pose, one unit in the last place of y = 0.25 m (3e-8 m, 1e-4 J): a gain under 0.01 J (3 microns) is
;; none. Predictions worked out first:
;;  - a block left alone gains nothing (the floor);
;;  - replacing the worked ground's body on every dig and dump (#188: a new body is a physical event for what rests on it) gives a
;;    block 4 m and more off, on the level or on a 16.7 degree slope, nothing over 100 digs and dumps: the patch is the same ground
;;    under it, and a body that slips on a new surface only loses height;
;;  - the fine patch laid under a block on lumpy map ground is the map's own drawn surface to 1e-12 m, so it gives nothing;
;;  - the ground model alone (no check) does give energy: a heap grown under a block leaves it inside the ground (it falls through
;;    and is lost), a heap's skirt run under a block's edge lifts it (m g dh, 0.11 m and some 380 J for three bucketfuls), and a
;;    pit's wall slumping onto its floor lifts a block standing there. That is the rover lifting a load by the back door;
;;  - the backhoe's check refuses those same tips and that dig, and the blocks gain nothing; a tip well clear of them goes.
;; Skipped when Godot is not installed. The water's side (soil tipped into a pond) is DirtEnergyTests.cs.
(require rackunit racket/system racket/port racket/string racket/runtime-path
         (only-in heroic/godothost godot-available? godot-binary))

(define-runtime-path game-dir "../../../game")

(define (run-eval)
  (define out
    (parameterize ([current-directory game-dir])
      (with-output-to-string
        (λ () (system* godot-binary "--headless" "--fixed-fps" "120" "--path" "." "res://scenes/DirtEnergyEval.tscn")))))
  (for/hash ([line (in-list (string-split out "\n"))]
             #:when (string-prefix? line "EVAL "))
    (define parts (string-split line))
    (values (cadr parts)
            (or (string->number (caddr parts) 10 'read 'decimal-as-inexact) (caddr parts)))))

(define none 0.01)   ; J: under this a gain is the float32 floor

(when (godot-available?)
  (define e (run-eval))
  (define (v k) (hash-ref e k (λ () (error 'dirt-energy "no measurement ~a" k))))

  (test-case "a block left alone gains nothing"
    (check-true (< (v "rest.after-25s.flat.peak-gain-j") none)))

  (test-case "100 digs and dumps, each a new body for the worked ground, give a resting block nothing, on the level or a slope"
    (for ([b '("flat" "slope")])
      (check-true (< (v (format "reshape.after-100.~a.peak-gain-j" b)) none) (format "~a: no gain at any tick" b))
      (check-true (< (v (format "reshape.after-100.~a.gain-j" b)) none) (format "~a: none at the end" b))
      ;; nothing builds up: the hundredth reshape leaves it no higher than the tenth
      (check-true (<= (v (format "reshape.after-100.~a.rise-m" b)) (+ (v (format "reshape.after-10.~a.rise-m" b)) 1e-6))))
    (check-= (v "reshape.cycles") 100 0))

  (test-case "the fine patch laid under a resting block is the ground that was there, and gives it nothing"
    (check-= (v "patch-made.patch-covers-block") 1 0)
    (check-true (< (abs (v "patch-made.surface-difference-m")) 1e-12))
    (check-true (< (v "patch-made.after.lumpy.peak-gain-j") none)))

  (test-case "the ground model alone gives a body energy: soil heaped or slid under it (what the check must stop)"
    (check-true (< (v "heap-under.after-3.block.rise-m") -1) "a heap grown under a block leaves it inside the ground, and it falls through")
    (check-true (> (v "heap-under.after-3.block.peak-kinetic-j") 1000))
    (define m (v "heap-beside.after-3.block.mass-kg"))
    (define dh (v "heap-beside.after-3.block.rise-m"))
    (check-true (> dh 0.05) "the heap's skirt lifts the block")
    (check-= (v "heap-beside.after-3.block.gain-j") (* m 9.81 dh) 1.0 "by m g dh, at rest again")
    (check-true (> (v "slide-under.after.block.gain-j") 50) "a pit's slumping wall lifts a block on its floor"))

  (test-case "the backhoe refuses a tip under or beside a block and a dig whose slide would reach one; the blocks gain nothing"
    (check-true (string-prefix? (v "guarded-dump.on-block.status") "Kept_0.20"))
    (check-true (string-prefix? (v "guarded-dump.beside-block.status") "Kept_0.20"))
    (check-true (string-prefix? (v "guarded-dig.status") "Dug_nothing:_the_slide"))
    (check-equal? (v "guarded-dump.clear.status") "Dumped_0.20_m³" "a tip well clear of them goes")
    (check-= (v "guarded-dump.clear.carried") 0 1e-12)
    (check-= (v "guarded-dump.net-volume") 0 1e-9)
    (for ([k '("guarded-dump.after.under" "guarded-dump.after.beside" "guarded-dig.after.block")])
      (check-true (< (v (string-append k ".peak-gain-j")) none) k)
      (check-true (< (abs (v (string-append k ".rise-m"))) 1e-6) k))))
