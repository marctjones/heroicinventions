#lang racket/base
;; The bimetal strip (issue #97) on the heat bin's lid, traced through a Mars night against numbers worked out before the run
;; (racket/machines/bimetal-night.rkt has the working).
(require rackunit racket/list racket/math heroic/simhost)

(define (at frame key) (cadr (assq key (cdr frame))))
(define (frame-near run t) (argmin (λ (f) (abs (- (car f) t))) run))
(define (value-at run key t) (at (frame-near run t) key))

(define hour 3699.0)

(test-case "Strip vault: a brass-steel strip senses the bank, trails it by r tau, and holds the lid wide open all night, as the ideal switch does"
  (define run (simulate 'bimetal-night #:seconds (* 10 hour) #:step 5 #:sample-dt 600))
  (define last-frame (last run))
  ;; the strip's own numbers: tau = C / (h A) = 3.544 / (10 x 2.22e-3) = 159.6 s; 2.1 mm of travel / 60.12 um per K = 34.9 K
  (check-= (at last-frame 'strip-strip.tau) 159.6 0.1)
  (check-= (at last-frame 'strip-strip.span) 34.94 0.02)
  ;; at the start the strip is at the bank's -55 C: 75 K under flat, its tip 4.51 mm back from flat, and the lid it works wide open
  (check-= (value-at run 'strip-strip.deflection 0) -4.51 0.02)
  (check-= (value-at run 'strip-bin.open 0) 1 0)
  ;; it trails a bank that is warming at r by r tau: 13.9 K/h at 1 h is 0.62 K
  (define rate (/ (- (value-at run 'strip-bank.temperature 3900) (value-at run 'strip-bank.temperature 3300)) 600.0))
  (check-= (- (value-at run 'strip-strip.temperature 3600) (value-at run 'strip-bank.temperature 3600)) (* -1 rate 159.6) 0.1
           "the strip trails the bank by r tau")
  ;; bank at 03:00: the strip's lid is open all night, like the ideal switch's, so the banks are the same, +4.3 C (night-heat's trace +4.28), a bank that charges (0-45 C)
  (define bank (at last-frame 'strip-bank.temperature))
  (check-= bank 4.3 0.2)
  (check-= bank (at last-frame 'ideal-bank.temperature) 0.05 "the strip does what the ideal switch does here")
  (check-true (<= 0 bank 45) (format "the bank at 03:00 is ~a C, within the 0-45 C it can charge at" bank))
  ;; the tip at 03:00: 60.12 um/K x (4.27 - 20) = -0.95 mm
  (check-= (at last-frame 'strip-strip.deflection) -0.95 0.03)
  (check-= (at last-frame 'strip-strip.deflection) (* 0.06012 (- (at last-frame 'strip-strip.temperature) 20)) 0.01)
  ;; and the lid never closed: wide open at every sample, the tip always on the open side of its 5.06 C travel end
  (for ([f run])
    (check-= (at f 'strip-bin.open) 1 1e-9 (format "lid open at ~a h" (/ (car f) hour)))
    (check-true (< (at f 'strip-strip.temperature) 5.06) "under the temperature at which the lid is wide open"))
  ;; the deflection rises monotonically toward the shut end as the bank warms
  (for ([a run] [b (cdr run)])
    (check-true (>= (at b 'strip-strip.deflection) (- (at a 'strip-strip.deflection) 1e-9)))))

(test-case "Warm vault: the strip shuts the lid as the bank warms toward 40 C and holds it there, well under the 45 C a bank may charge at"
  (define run (simulate 'bimetal-night #:seconds (* 12 hour) #:step 5 #:sample-dt 600))
  ;; lid starts (40 - 30) / 34.94 = 29% open
  (check-= (value-at run 'warm-strip.open 0) 0.286 0.005)
  (define banks (map (λ (f) (at f 'warm-bank.temperature)) run))
  (check-true (< (apply max banks) 40) (format "the bank peaked at ~a C: the lid is shut at 40 C and a shut lid leaks only 0.1 W/K" (apply max banks)))
  ;; worked: 40 - 0.074 x 34.94 = 37.4 C at 5 h (lid 7.4% open); traced 38.0 C
  (check-= (value-at run 'warm-bank.temperature (* 5 hour)) 37.4 1.0)
  (check-= (value-at run 'warm-strip.open (* 5 hour)) 0.074 0.03)
  ;; the lid follows the strip, which follows the bank: open = (40 - T_strip) / span at every sample, and the strip trails the bank
  (for ([f run])
    (define expect (max 0 (min 1 (/ (- 40 (at f 'warm-strip.temperature)) (at f 'warm-strip.span)))))
    (check-= (at f 'warm-strip.open) expect 0.005 (format "lid against strip at ~a h" (/ (car f) hour)))
    (check-= (at f 'warm-bin.open) (at f 'warm-strip.open) 1e-9 "the bin's lid is the strip's opening"))
  ;; the strip's tip, in mm, is +60.12 um per K over its flat 20 C: positive (toward the steel) once the bank is over 20 C
  (check-= (value-at run 'warm-strip.deflection (* 5 hour)) (* 0.06012 (- (value-at run 'warm-strip.temperature (* 5 hour)) 20)) 0.01))

(test-case "The adjusting screw moves the temperature at which the lid shuts, and an operator may set it"
  ;; shut at 30 C instead of 40, the warm bank's lid is open by (30 - T) / span, not (40 - T) / span: the same strip temperature gives 10 K / 34.94 K = 0.286 less
  (define run (simulate 'bimetal-night #:seconds (* 3 hour) #:step 5 #:sample-dt 600 #:set '((warm-strip shut-at 30))))
  (check-= (value-at run 'warm-strip.shut-at 0) 30 1e-9)
  (for ([f run])
    (define expect (max 0 (min 1 (/ (- 30 (at f 'warm-strip.temperature)) (at f 'warm-strip.span)))))
    (check-= (at f 'warm-strip.open) expect 0.005))
  (check-true (< (value-at run 'warm-bank.temperature (* 3 hour)) 31) "and the bank is held lower, near 30 C"))
