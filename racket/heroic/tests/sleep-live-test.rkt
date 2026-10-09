#lang racket/base
;; Issue #207: a scripted sleep that keeps a Jolt-driven machine working, stepped at speed with the same result as watching, a trace that
;; keeps its dt after a sleep, and a test sleep that writes nothing into the player's saves. Headless Godot at 120 Hz; skipped when Godot is
;; not installed. The machine is generator-train (a 5 m windmill geared 125:1 up to a generator; its header works out the settling).
;;
;; Worked before the runs:
;;  - sails 200 kg, 3 m/s wind, three 5:1 meshes at 0.97, rotor loaded 2 N.m: settles at 125 x 1.39086 = 173.86 rad/s, within a part in 10^4
;;    of it by 180 s; the trace reads the rotor's speed at the start of a step, 0.17 rad/s over it (header): 174.03 rad/s expected at 200 s and after.
;;  - the sleep to 200 s keeps the engine running, so the tick-for-tick trace of a sleep EQUALS the watched run's: the same 241 rows at dt 1 over
;;    240 s, every field equal (Jolt is deterministic; the sleep takes the same 1/120 s steps and only leaves the redrawing out). Aimed at: bit-equal;
;;    the bar the conventions set for an engine change is 1e-3 and 1%.
;;  - the same sleep with the engine paused ("paused") runs the sim alone: one row at the wake (200.0167 s, the first tick past 200), then a row a
;;    second to 240 s: 1 (t = 0) + 1 + 40 = 42 rows, none inside the sleep, not the 24,000 a tick-a-row catch-up would write (one per 1/120 s).
;;    And the Jolt side is where it was left: the rotor reads 0 rad/s at the wake, which is the flywheel dump the live form removes.
;;  - "sleep until scene.elapsed above 50" wakes on the first tick past 50 s: a clock of 50 + 1/120 = 50.0083 s in the autosave it writes.
(require rackunit racket/list racket/file racket/string racket/runtime-path
         heroic/godothost)

(define-runtime-path saves-user "~/Library/Application Support/Godot/app_userdata/Heroic Inventions/saves")

(define (at f k) (let ([p (assq k (cdr f))]) (and p (cadr p))))

(define (equal-runs? a b)
  (and (= (length a) (length b))
       (for/and ([x a] [y b])
         (and (< (abs (- (car x) (car y))) 1e-9)
              (for/and ([p (cdr x)])
                (define q (assq (car p) (cdr y)))
                (and q (<= (abs (- (cadr p) (cadr q))) (* 1e-9 (+ 1 (abs (cadr p)))))))))))

(define (listing d) (if (directory-exists? d) (for/list ([f (directory-list d)]) (list f (file-or-directory-modify-seconds (build-path d f)))) '()))

(when (godot-available?)
  (define (run env #:seconds [seconds 240]) (godot-simulate 'generator-train #:seconds seconds #:sample-dt 1 #:env env))
  (define watched (run '()))
  (define live (run '(("HEROIC_SLEEP" . "settled"))))
  (define paused (run '(("HEROIC_SLEEP" . "settled") ("HEROIC_SLEEP_PHYSICS" . "0"))))

  (test-case "a sleep that keeps the machine turning leaves exactly what watching leaves"
    (check-equal? (length live) 241 "a row a second over the sleep too")
    (check-true (equal-runs? watched live) "every field of every row equals the watched run's")
    (check-= (at (last live) 'generator.omega) 174.03 1.75 "rad/s: the worked 173.86 + 0.17, within 1%")
    (check-= (at (list-ref live 200) 'generator.omega) 174.03 1.75))

  (test-case "traces after a sleep keep their dt"
    (define times (map car paused))
    (check-equal? (length paused) 42 "t = 0, the wake, then 40 s at dt 1")
    (check-true (null? (filter (λ (t) (< 1 t 199)) times)) "no frame inside the sleep")
    (check-= (cadr times) 200.0167 0.01 "s, the first row after the sleep is the wake")
    (check-= (- (last times) (cadr times)) 40 1 "s, then one row a second")
    (for ([a (cddr times)] [b (cdddr times)]) (check-= (- b a) 1.0 1e-6))
    (check-= (at (cadr paused) 'generator.omega) 0 1e-9 "rad/s: the paused engine left the rotor still at the wake"))

  (test-case "a script sleeps until a condition and then carries on; its autosave goes to the run's own folder"
    (define dir (make-temporary-file "heroic-saves-~a" 'directory))
    (define before (listing saves-user))
    (define r (run `(("HEROIC_INPUT" . "waitsim 10; sleep until scene.elapsed above 50 limit 600; wait 1") ("HEROIC_SAVES_DIR" . ,(path->string dir))) #:seconds 120))
    (check-equal? (length r) 121 "the run goes on to 120 s at a row a second")
    (check-true (equal-runs? (take r 100) (take watched 100)) "a sleep to 50 s equals watching to 100 s")
    (define save (build-path dir "generator-train.autosave.save"))
    (check-true (file-exists? save) "the wake autosaved (#67) into the run's folder")
    (define clock (string->number (cadr (regexp-match #rx"[(]clock ([0-9.]+)[)]" (file->string save)))))
    (check-= clock 50.0083 0.0001 "s: woke on the first tick past 50")
    (delete-directory/files dir)
    (check-equal? (listing saves-user) before "the player's saves folder is as it was"))

  (test-case "a scripted run with a sleep and no saves folder of its own writes no autosave at all"
    (define before (listing saves-user))
    (run '(("HEROIC_SLEEP" . "settled") ("HEROIC_SAVES_DIR" . "")) #:seconds 210)
    (check-equal? (listing saves-user) before))

  (test-case "a sleep no machine can wake from fails the run, saying so"
    (check-exn #rx"no machine has a wake called nope"
               (λ () (run '(("HEROIC_INPUT" . "sleep nope")) #:seconds 5)))))
