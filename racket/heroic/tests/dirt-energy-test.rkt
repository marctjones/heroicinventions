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
;;  - the backhoe's soil goes round the blocks instead (#72, owner decision 2026-10-08: nothing is refused): tipped half over a block
;;    or beside one it piles against it, a pit wall's slump stops at a block's base, and the blocks gain nothing. Only a bucket
;;    held right over a block keeps its load;
;;  - digging a bank out from under a block lets it fall: its energy goes down by m g dh (0.39 m, 1302 J), and never up.
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

  (test-case "the backhoe's soil goes round the blocks: nothing refused but a bucket right over one, and they gain nothing"
    (check-true (string-prefix? (v "around-dump.over.status") "Kept_0.20_m³:_the_bucket_is_over") "the one case: the whole footprint under the block")
    (for ([k '("half-over-0" "half-over-1" "half-over-2" "beside-0" "beside-1" "beside-2")])
      (check-equal? (v (format "around-dump.~a.status" k)) "Dumped_0.20_m³" k)
      (check-= (v (format "around-dump.~a.carried" k)) 0 1e-12 k))
    (check-= (v "around-dump.dumped") 1.2 1e-9)
    (check-= (v "around-dump.net-volume") 0 1e-9 "every m3 is on the ground or in the bucket")
    (check-true (> (v "around-dump.heap-by-under-m") 0.3) "the soil heaped against the block")
    (check-true (string-prefix? (v "around-dig.status") "Dumped") "the dig at the pit's rim went ahead")
    (check-= (v "around-dig.dug") 0.2 1e-9)
    (check-true (> (v "around-dig.floor-across-pit-m") -0.95) "the wall slumped across the floor")
    (for ([k '("around-dump.after.under" "around-dump.after.beside" "around-dig.after.block")])
      (check-true (< (v (string-append k ".peak-gain-j")) none) k)
      (check-true (< (v (string-append k ".gain-j")) none) k)
      (check-true (< (abs (v (string-append k ".rise-m"))) 1e-4) k)))

  (test-case "a block on a bank's edge, the bank dug out from under it, falls as gravity takes it: m g dh down, nothing up"
    (for ([n 3]) (check-equal? (v (format "undermine.cycle-~a.status" n)) "Dumped_0.20_m³"))
    (define m (v "undermine.after.block.mass-kg"))
    (define dh (v "undermine.after.block.rise-m"))
    (check-true (< dh -0.2) (format "it fell ~a m" dh))
    (check-= (v "undermine.after.block.gain-j") (* m 9.81 dh) 1.0 "at rest again: its energy is down by m g dh")
    (check-true (< (v "undermine.after.block.peak-gain-j") none) "and was never above where it began")))
