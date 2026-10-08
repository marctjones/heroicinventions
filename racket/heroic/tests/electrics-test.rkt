#lang racket/base
;; The found electrics (issue #64) traced against numbers worked out before the run (each machine's header has the working):
;;   racket/machines/bank-bench.rkt      the bank's charging window and the call, sim-side (small windmills straight on bench motors)
;;   racket/machines/found-electrics.rkt generator-train's windmill geared 125:1 to a generator, charging a bank, and the call; Jolt-side
(require rackunit racket/list racket/math heroic/simhost heroic/godothost)

(define (at frame key) (cadr (assq key (cdr frame))))
(define (frame-near run t) (argmin (λ (f) (abs (- (car f) t))) run))
(define (value-at run key t) (at (frame-near run t) key))
(define (first-time run pred) (for/first ([f run] #:when (pred f)) (car f)))

;; ---- the bench: the generator and the bank's window ----
;; Each windmill: 2 m, 30 kg, 5 m/s, tau* = 45.394 N.m, w* = 6.25 rad/s (free 12.5, 119.4 rpm); the bench motor's k = 3.1831 above 3.1416 rad/s.
;;   w = (2 tau* + k w_cut) / (tau* / w* + k) = 9.6484 rad/s (92.13 rpm), tau_g = 20.712 N.m, 199.83 W of shaft, 159.87 W into the bank.
(test-case "Bench: a loaded windmill slows from its free 119.4 rpm to 92.1 rpm by the generator's torque, and charges at eta tau w = 159.9 W"
  (define run (simulate 'bank-bench #:seconds 60 #:step 0.1 #:sample-dt 10))
  (check-= (value-at run 'w-warm.rpm 0) 0 1e-9)
  (check-= (value-at run 'w-warm.rpm 60) 92.135 0.05 "rpm")
  (check-= (value-at run 'g-warm.torque 60) 20.712 0.01 "N m")
  (check-= (value-at run 'w-warm.torque 60) (value-at run 'g-warm.torque 60) 0.01 "the sails give what the generator takes")
  (check-= (value-at run 'g-warm.power 60) 159.87 0.1 "W")
  (check-= (value-at run 'g-warm.shaft-power 60) 199.83 0.1 "W of shaft")
  (check-= (value-at run 'g-warm.current 60) 5.7095 0.002 "A at 28 V")
  (check-= (/ (value-at run 'g-warm.power 60) (value-at run 'g-warm.shaft-power 60)) 0.8 1e-9 "eta")
  ;; the cold and hot banks refuse it: the windmills run free (119.4 rpm), their generators loading them with nothing
  (check-= (value-at run 'w-cold.rpm 60) 119.366 0.01 "free: no load while the bank is cold")
  (check-= (value-at run 'g-cold.torque 60) 0 1e-9)
  (check-= (value-at run 'w-hot.rpm 60) 119.366 0.01))

(test-case "Bench: a 5 Wh bank fills in 18,000 J / 159.87 W = 112.6 s of charging; the cold one waits for 0 C at 1,297.5 s, the hot one for 45 C at 583.4 s"
  (define run (simulate 'bank-bench #:seconds 1500 #:step 0.5 #:sample-dt 5))
  ;; warm: full a little after 112.6 s + the sails' spin-up
  (define t-warm (first-time run (λ (f) (>= (at f 'warm.charge) 4.9999))))
  (check-true (< 112.6 t-warm 122) (format "warm full at ~a s" t-warm))
  ;; cold: its cells warm as 20 - 30 exp(-t / 3200): no charge before 1,297.5 s, and it resumes
  (define t-cold (first-time run (λ (f) (> (at f 'cold.charge) 0))))
  (check-= t-cold 1297.5 6 "s: the first sample after 0 C")
  (for ([f run] #:when (< (at f 'cold.temperature) 0)) (check-= (at f 'cold.charge) 0 1e-12 (format "cold bank at ~a s" (car f))))
  (check-= (value-at run 'cold.temperature 1295) (- 20 (* 30 (exp (/ -1295 3200.0)))) 0.05 "C")
  (check-true (> (value-at run 'cold.charge 1500) 4.9) "charging resumed and nearly filled (1,500 - 1,297.5 = 202 s)")
  ;; hot: 20 + 30 exp(-t / 3200) falls to 45 C at 583.4 s
  (define t-hot (first-time run (λ (f) (> (at f 'hot.charge) 0))))
  (check-= t-hot 583.4 6 "s")
  (for ([f run] #:when (> (at f 'hot.temperature) 45)) (check-= (at f 'hot.charge) 0 1e-12 (format "hot bank at ~a s" (car f))))
  (check-= (value-at run 'hot.charge 800) 5 1e-6 "and filled")
  ;; what each was charged by, from the generators' own names
  (check-= (value-at run 'warm.from-wind 1500) 5 1e-6 "Wh from the wind")
  (check-= (value-at run 'hot.from-falling-weight 1500) 5 1e-6 "Wh, as its generator is named"))

(test-case "Bench: the call goes out only in the 10 minutes from 03:00 with a full bank between 0 and 45 C (not 02:59, not 03:11)"
  ;; the clock is held (clock-rate 0) and moved by hand at the times shown
  (define run (simulate 'bank-bench #:seconds 500 #:step 1 #:sample-dt 10
                        #:set '((scene clock-rate 0 0) (scene time 2.98333 0)           ; 02:59
                                (scene time 3.0 100)                                     ; 03:00: the pass opens
                                (scene time 3.18333 200) (late charge 5 200)             ; 03:11: late fills too late
                                (scene time 3.1 300)                                     ; 03:06 on the next try
                                (scene time 9 400))))
  (check-= (value-at run 'ok.won 90) 0 1e-9 "02:59: not yet")
  (check-= (value-at run 'ok.won 110) 1 1e-9 "03:00: the call goes out")
  (check-= (value-at run 'ok.won 450) 1 1e-9 "and it stays won")
  (check-= (value-at run 'late.full 210) 1 1e-9 "late is full at 03:11")
  (check-= (value-at run 'late.won 210) 0 1e-9 "but the pass has shut: no call")
  (check-= (value-at run 'late.won 310) 1 1e-9 "03:06: it goes")
  (check-= (value-at run 'ok.in-window 90) 0 1e-9)
  (check-= (value-at run 'ok.in-window 150) 1 1e-9)
  ;; 50 C and -5 C never call, full as they are
  (for ([f run])
    (check-= (at f 'hot-w.won) 0 1e-9 (format "50 C bank at ~a s" (car f)))
    (check-= (at f 'cold-w.won) 0 1e-9 (format "-5 C bank at ~a s" (car f))))
  (check-= (value-at run 'hot-w.full 150) 1 1e-9)
  ;; the easy setting wins the first step, at any hour
  (check-= (value-at run 'easy.won 10) 1 1e-9)
  (check-= (value-at run 'scene.won 10) 1 1e-9 "(the easy bank)"))

;; ---- the whole route: found-electrics ----
;; Worked in the header: the windmill settles at 1.39583 rad/s (13.33 rpm), the rotor at 174.479 rad/s (1,666.1 rpm) and 1.9938 N.m,
;; 278.30 W into the bank (9.94 A), 0.97^3 = 91.27% of the sails' 381.16 W reaching the generator; a 10 Wh bank fills in 129.4 s of charging;
;; the run starts at 02:55 and the call goes out at 03:00, 300 s in.
(test-case "Found electrics: a 125:1 windmill train turns the generator at 1,666 rpm over its 1,500 cut-in, loaded, charging 278 W, and the call goes out at 03:00"
  (when (godot-available?)
    (define run (godot-simulate 'found-electrics #:seconds 330 #:sample-dt 1))
    (define (v k t) (value-at run k t))
    ;; before the cut-in: from rest the sails reach 1.2566 rad/s (the rotor 157.08) at about 10 s; nothing is charged until then
    (check-= (v 'motor.torque 5) 0 1e-9 "under the cut-in: no load")
    (check-= (v 'bank.charge 5) 0 1e-9 "and no charge")
    (check-true (< (v 'rotor-disc.omega 5) 157.08) "the rotor is under its 1,500 rpm")
    ;; settled
    (check-= (v 'sails.rpm 60) 13.329 0.03 "rpm: the windmill slowed from its free 28.6 by the load")
    (check-= (v 'rotor-disc.omega 60) 174.479 0.05 "rad/s: 125 x the sails")
    (check-= (v 'motor.rpm 60) 1666.1 0.6 "rpm: over the 1,500 cut-in")
    (check-= (v 'motor.torque 60) 1.9938 0.004 "N m")
    (check-= (v 'motor.power 60) 278.3 0.5 "W into the bank: eta tau w")
    (check-= (v 'motor.current 60) 9.94 0.02 "A")
    ;; the torque the sails feel through the train, 125 tau_g / 0.97^3 = 273.07 N m, which is what they give
    (check-= (v 'sails.torque 60) 273.07 0.4 "N m")
    (check-= (v 'wheel-a.load-torque 60) (v 'sails.torque 60) 0.2)
    ;; the power: the generator's shaft gets 0.97^3 of what the sails take
    (define sails-w (* (v 'sails.rpm 60) 2 pi 1/60))
    (check-= (/ (v 'motor.shaft-power 60) (* (v 'sails.torque 60) sails-w)) 0.9127 0.004 "three meshes at 0.97")
    ;; the bank: 10 Wh at 278.3 W: 129.4 s of charging, from about 10 s: full at about 140 s
    (define t-full (first-time run (λ (f) (>= (at f 'bank.charge) 9.9999))))
    (check-true (< 139 t-full 147) (format "full at ~a s" t-full))
    (check-= (v 'bank.from-wind 330) 10 1e-6 "Wh, all of it from the wind (the prime mover is found by walking the train back to the sails)")
    (check-= (v 'bank.temperature 100) 20 1e-6 "C")
    ;; open circuit once full: no load, the unloaded train races, and the engine's 3,000 rpm limit (314.16 rad/s) caps it
    (check-= (v 'motor.torque 250) 0 1e-9)
    (for ([f run]) (check-true (<= (at f 'rotor-disc.omega) 314.17) (format "rotor under the spin limit at ~a s" (car f))))
    ;; the call: 02:59 no, 03:00 yes
    (check-= (v 'scene.time 240) (+ (/ 175 60.0) (/ 240 3600.0)) 1e-3 "h: 02:59")
    (check-= (v 'bank.full 240) 1 1e-9 "the bank is full a minute before the pass")
    (check-= (v 'bank.won 240) 0 1e-9 "02:59: no call")
    (check-= (v 'bank.won 295) 0 1e-9)
    (check-= (v 'bank.won 305) 1 1e-9 "03:00: the call goes out")
    (check-= (v 'scene.won 330) 1 1e-9)))

;; A salvaged motor with a higher cut-in has a steeper curve to its rated point (k = 12 / (261.80 - w_cut)) and the sails settle where they meet it:
;; at 1,700 rpm (178.02 rad/s) k = 0.14323; 255.34 (2 - w / 1.5) = 125 k (125 w - 178.02) / 0.912673 gives w = 1.5264 rad/s (14.58 rpm),
;; the rotor at 190.8 rad/s (1,822 rpm), tau_g = 0.14323 x (190.80 - 178.02) = 1.830 N.m, and 0.8 x 1.830 x 190.80 = 279.3 W into the bank.
(test-case "Found electrics: raising the cut-in to 1,700 rpm moves the operating point to 14.58 rpm of sails and 1,822 rpm of rotor, 279 W"
  (when (godot-available?)
    (define run (godot-simulate 'found-electrics #:seconds 60 #:sample-dt 5 #:set '((motor cut-in-rpm 1700))))
    (check-= (value-at run 'sails.rpm 60) 14.58 0.04 "rpm")
    (check-= (value-at run 'motor.rpm 60) 1822 2 "rpm")
    (check-= (value-at run 'motor.torque 60) 1.830 0.006 "N m")
    (check-= (value-at run 'motor.power 60) 279.3 0.7 "W")))
