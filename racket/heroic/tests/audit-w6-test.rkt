#lang racket/base
;; Machine-audit remainder (#148): the bellows-forge crucibles' shell, the crane-hoist stone at rest, and the
;; cargo crate on its own. Each prediction is worked in the comment above its check, before the run.
(require rackunit heroic/simhost heroic/godothost racket/math)

;; ---------------------------------------------------------------------------
;; (a) The bellows-forge crucibles are 3 mm bronze shells over a 7 cm radius: thin-wall hoop stress
;; sigma = P r / t, so each holds sigma_t t / r = 350e6 x 0.003 / 0.07 = 15.0 MPa gauge, which is steam at
;; 342.4 C (Antoine, 99-374 C set: log10(P/133.322 Pa) = 8.14019 - 1810.94 / (244.485 + T)). They are sealed with
;; 50 g of water, so they now burst. Heating the water alone from 20 to 342.4 C takes 0.05 x 4186 x 322.4 =
;; 67.5 kJ: at the bare fire's 5 kW x 0.5 = 2.5 kW that is 27.0 s, at the blown fire's 4.0103 x 2.5 = 10.03 kW
;; 6.73 s; the burst comes after that, the steam's latent heat on top. Measured: 31.21 s and 6.96 s.
(test-case "Bellows forge crucibles: 3 mm bronze holds 15.0 MPa (342 C of steam); the sealed pots burst after the water's heat-up time"
  (define run (simulate 'bellows-forge #:seconds 60 #:step 0.01 #:sample-dt 1))
  (define (hoop sigma-pa t r) (/ (* sigma-pa t) r))
  (check-= (hoop 350e6 0.003 0.07) 15e6 1e-6)
  (define (antoine-t p-abs-pa)
    (define mmhg (/ p-abs-pa 133.322))
    (- (/ 1810.94 (- 8.14019 (/ (log mmhg) (log 10)))) 244.485))
  (check-= (antoine-t (+ 101325 15e6)) 342.4 0.1)
  (define heat-j (* 0.05 4186 (- 342.4 20)))
  (check-= heat-j 67.5e3 100)
  (define bare-s (/ heat-j 2500.0))
  (define blown-s (/ heat-j (* 4.010296 2500.0)))
  (check-= bare-s 27.0 0.05) (check-= blown-s 6.73 0.01)
  (for ([c '(bare-crucible blown-crucible)])
    (check-= (final-of run (list c 'limit)) 15000 0.01 "kPa, from the table's tensile strength and the wall")
    (check-= (final-of run (list c 'burst)) 1 0)
    (check-= (final-of run (list c 'burst-pressure)) 15000 60 "kPa it gave way at"))
  (define bare-t (final-of run '(bare-crucible burst-time)))
  (define blown-t (final-of run '(blown-crucible burst-time)))
  (check-true (and (> bare-t bare-s) (< bare-t (* 1.2 bare-s))) (format "bare bursts after ~a s, just over the heat-up" bare-t))
  (check-true (and (> blown-t blown-s) (< blown-t (* 1.1 blown-s))) (format "blown bursts after ~a s" blown-t))
  (check-= bare-t 31.21 0.05 "measured")
  (check-= blown-t 6.96 0.05 "measured")
  (check-true (< blown-t (/ bare-t 4)) "the forced draught's fire gets there more than four times sooner"))

;; ---------------------------------------------------------------------------
;; (b) The crane-hoist stone is a 0.6 m granite cube: resting on the floor its middle is 0.30 m up from the first
;; frame to the last, nothing turns the drum (it used to start 5 mm up and settle to 0.300 in the first second).
(define (field f k) (cadr (assq k (cdr f))))
(define (value-at run path t)
  (define key (string->symbol (format "~a.~a" (car path) (cadr path))))
  (define frame (for/fold ([best (car run)]) ([f (cdr run)])
                  (if (< (abs (- (car f) t)) (abs (- (car best) t))) f best)))
  (cadr (assq key (cdr frame))))

(test-case "Crane hoist: the stone rests on the ground from the first frame, its middle 0.30 m up"
  (when (godot-available?)
    (define run (godot-simulate 'crane-hoist #:seconds 5 #:sample-dt 0.25))
    (check-= (field (car run) 'stone.y) 0.3 1e-4 "the first frame")
    (check-= (max-of run '(stone y)) 0.3 5e-4 "never higher")
    (check-= (min-of run '(stone y)) 0.3 5e-4 "never lower")
    (check-= (final-of run '(stone speed)) 0 1e-3)))

;; ---------------------------------------------------------------------------
;; (f) cargo-crate on its own. Its header: an oak box 50 cm a side, 90 kg (720 x 0.5^3), on Mars (g 3.71, 610 Pa).
;; Standing, its middle is 0.25 m up and stays. A hand lifts it a metre and lets go (the hand holds it ~1.24 m up);
;; it falls h = 1.24 - 0.25 m on Mars: v = sqrt(2 g h) = 2.71 m/s (on Earth's 9.81 it would be 4.41), after 0.73 s.
;; The collision loses 1/2 m v^2 (1 - e^2) of energy, e = 0.5 (oak): so m = 2 E / (v^2 x 0.75) = 90 kg.
(test-case "Cargo crate: 90 kg of oak standing on Mars, falling at 3.71 m/s2"
  (when (godot-available?)
    (define run (godot-simulate 'cargo-crate #:seconds 6 #:sample-dt 1/60
                                #:env '(("HEROIC_DRAG" . "drag crate 0 0 0 at 1; to 0 1.25 0 at 2; release at 3"))))
    (check-= (field (car run) 'scene.gravity) 3.71 1e-9)
    (check-= (field (car run) 'scene.pressure) 0.61 1e-9 "kPa")
    (check-= (value-at run '(crate y) 0.5) 0.25 1e-3 "standing")
    (check-= (value-at run '(crate speed) 0.5) 0 1e-3)
    (define h (- (value-at run '(crate y) 2.95) 0.25))
    (check-true (> h 0.9) (format "lifted ~a m" h))
    (define v (max-of run '(crate impact-speed)))
    (check-= v (sqrt (* 2 3.71 h)) (* 0.02 v) (format "landed at ~a m/s from ~a m: Mars's g, not 4.4" v h))
    (define e (max-of run '(crate impact-energy)))
    (check-= (/ (* 2 e) (* v v 0.75)) 90 0.9 "kg, from the energy the collision took")
    (check-= (final-of run '(crate y)) 0.25 2e-3 "standing again")))
