#lang racket/base
;; A sleep that charges (owner's ruling, 2026-10-09), in the game's own physics (Jolt, 120 Hz); skipped when Godot is not installed. The
;; sim-side rules (full, 0-45 C, the per-source record, not settled) are tests/HeroicInventions.Sim.Tests/PausedSleepChargeTests.cs.
;; The machine is found-electrics (its header works the numbers): a windmill geared 125:1 to a generator on a rigid rotor, 278.30 W into
;; the bank once settled (by about 30 s), the bank sized to 1,000 Wh here so it does not fill.
;;
;; Worked before the runs:
;;  - paused: watched to 60 s (the generator's power steady for the last ten whole seconds: its settled power, the 278.3 W the header
;;    gives), then "sleep until scene.elapsed above 260 ... paused". The engine stops and the rotor with it; the generator is held at its
;;    settled power, so the bank gains settled x (wake - 60 s) = 278.3 x 200 = 55,660 J = 15.46 Wh between the row at 60 s and the wake,
;;    within 1%. (Before this ruling a paused sleep gained nothing here.) All of it is filed under the wind.
;;  - estimated (owner's ruling on a generator with no steady rate): the cells set to -2 C at the start, so the bank refuses charge and the
;;    generator never settles; watched 30 s, then slept paused until the 20 C vault has warmed the cells past 0 C (the sim's 16 kg of cells:
;;    about 2,400 s), then slept paused again until the bank holds 30 Wh. The second sleep holds the generator at an ESTIMATE worked from the
;;    sails' torque curve and the 125:1 train at 0.97^3: the header's 278.30 W. 30 Wh = 108,000 J at 278.30 W is 388 s. The rate it charged
;;    at is within 1% of the estimate and within 5% of the watched run's settled rate (278.5 W above); nothing at all while the cells were cold.
;;  - live: the same sleep with the engine running ("live") leaves exactly what watching leaves, row for row and field for field: the hold
;;    is only ever put on a paused sleep. Aimed at: bit-equal.
(require rackunit racket/list heroic/godothost)

(define (at f k) (let ([p (assq k (cdr f))]) (and p (cadr p))))
(define (row-at run t) (argmin (λ (f) (abs (- (car f) t))) run))

(define (equal-runs? a b)
  (and (= (length a) (length b))
       (for/and ([x a] [y b])
         (and (< (abs (- (car x) (car y))) 1e-9)
              (for/and ([p (cdr x)])
                (define q (assq (car p) (cdr y)))
                (and q (<= (abs (- (cadr p) (cadr q))) (* 1e-9 (+ 1 (abs (cadr p)))))))))))

(when (godot-available?)
  (define (run [script #f])
    (godot-simulate 'found-electrics #:seconds 300 #:sample-dt 1 #:set '((bank capacity 1000))
                    #:env (if script `(("HEROIC_INPUT" . ,script)) '())))
  (define watched (run))
  (define paused (run "waitsim 60; sleep until scene.elapsed above 260 limit 600 paused"))
  (define live (run "waitsim 60; sleep until scene.elapsed above 260 limit 600 live"))
  (define cold (godot-simulate 'found-electrics #:seconds 3600 #:sample-dt 1 #:set '((bank capacity 1000) (cells temperature -2))
                               #:env '(("HEROIC_INPUT" . "waitsim 30; sleep until bank.temperature above 0 limit 20000 paused; sleep until bank.charge above 30 limit 20000 paused"))))

  (test-case "a paused sleep charges the bank at the generator's settled power: P x T within 1%, all of it from the wind"
    (define before (row-at paused 60))
    (define woke (for/first ([f paused] #:when (> (car f) 259)) f))
    (check-= (car before) 60 1e-6)
    (check-true (< 260 (car woke) 260.03) (format "the wake row at ~a s is just past 260 (the first tick past it, and the trace's next row)" (car woke)))
    (check-true (null? (filter (λ (f) (< 61 (car f) 259)) paused)) "the sleep was paused: no rows inside it")
    (define p (at before 'motor.settled-power))
    (check-= p 278.3 0.6 "W, settled: the header's operating point")
    (define gained (* 3600 (- (at woke 'bank.charge) (at before 'bank.charge))))     ; J
    (define dt (- (car woke) (car before)))
    (printf "sleep-charge: settled ~a W, ~a s asleep (rows ~a to ~a), gained ~a J = ~a Wh; P x T = ~a J\n"
            p dt (car before) (car woke) gained (/ gained 3600) (* p dt))
    (check-= gained (* p dt) (* 0.01 p dt) "J: P x T within 1%")
    (check-= (/ gained 3600) 15.46 0.16 "Wh, the worked 278.3 W x 200 s")
    (check-= (at woke 'bank.from-wind) (at woke 'bank.charge) 1e-6 "all of it from the wind")
    (check-= (at woke 'motor.held) 0 0 "released on waking")
    ;; and the watched run, for scale: it charged at the same rate over the same 200 s
    (define watched-gain (* 3600 (- (at (row-at watched 260) 'bank.charge) (at (row-at watched 60) 'bank.charge))))
    (check-= gained watched-gain (* 0.01 watched-gain) "J: what watching would have charged, within 1%"))

  (test-case "a generator with no steady rate (its bank was cold) is held at the estimate from the sails and the train: 278.3 W once the vault has warmed the cells"
    (define watched-30 (row-at cold 30))
    (check-true (< (at watched-30 'bank.temperature) 0) "cold while watched")
    (check-= (at watched-30 'motor.settled-power) 0 0 "W: never settled (0 until it has one)")
    (check-= (at watched-30 'motor.estimated-power) 278.3 0.9 "W, the estimate the field reads while watching")
    (define asleep (filter (λ (f) (> (car f) 31)) cold))
    (define warm (first asleep))                                  ; the wake of the first sleep: the cells just past 0 C
    (define full (for/first ([f asleep] #:when (>= (at f 'bank.charge) 30)) f))
    (check-true (>= (at warm 'bank.temperature) 0))
    (check-true (< (at warm 'bank.charge) 0.05) (format "Wh: nothing while cold (~a Wh at the warm wake, ~a s)" (at warm 'bank.charge) (car warm)))
    (define rate (/ (* 3600 (- (at full 'bank.charge) (at warm 'bank.charge))) (- (car full) (car warm))))
    (printf "sleep-charge estimated: cells past 0 C at ~a s; 30 Wh at ~a s; charged at ~a W (estimate ~a W)\n"
            (car warm) (car full) rate (at watched-30 'motor.estimated-power))
    (check-= rate (at watched-30 'motor.estimated-power) (* 0.01 rate) "W: at the estimate, within 1%")
    (check-= rate 278.55 (* 0.05 278.55) "W: within 5% of the watched run's settled rate")
    (check-= (at full 'bank.from-wind) (at full 'bank.charge) 1e-6 "all of it from the wind"))

  (test-case "a live sleep is unchanged: every row and field equals the watched run's"
    (check-equal? (length live) (length watched))
    (check-true (equal-runs? watched live) "bit-equal to the watched run")))
