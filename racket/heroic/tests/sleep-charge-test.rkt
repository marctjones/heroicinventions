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

  (test-case "a paused sleep charges the bank at the generator's settled power: P x T within 1%, all of it from the wind"
    (define before (row-at paused 60))
    (define woke (for/first ([f paused] #:when (> (car f) 259)) f))
    (check-= (car before) 60 1e-6)
    (check-= (car woke) 260.0083 0.01 "s, the wake is the first tick past 260")
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

  (test-case "a live sleep is unchanged: every row and field equals the watched run's"
    (check-equal? (length live) (length watched))
    (check-true (equal-runs? watched live) "bit-equal to the watched run")))
