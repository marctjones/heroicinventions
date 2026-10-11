#lang racket/base
;; Meshes off their centre distance run as they stand (owner, 2026-10-11; racket/machines/mesh-faults.rkt, whose header
;; works the numbers before the run). Module 10 mm, a 20 meshing a 40: true at 0.30 m, loose at 0.31 (the tips reach to
;; 0.32), not touching at 3 m, jammed at 0.25.
;;   true, loose: the 40 turns at 30 x 20 / 40 = 15 rpm, 1.5708 rad/s, its teeth in the 20's gaps (mesh-error ~0).
;;   far (cranked): the 40 stays at 0 while the crank turns its 20 at 3.1416 rad/s.
;;   jam (cranked, 5 N.m): neither turns (< 0.001 rad/s).
;;   mill-jam: a windmill whose 40 is jammed stands (< 0.03 rpm: the wind's 0.00255 rad/s a tick, taken back each tick).
;;   mill-far: a windmill whose 40 is 3 m from its 20 runs up unloaded, 3 (1 - e^(-t/9.79)) rad/s: 28.59 rpm at 60 s
;;     (59 s: 28.58); its 20 stays at 0.
;;   a belt (a pass after the gear trains, once skipped by a bad mesh): the 20 cm pulley turns at about half the 10 cm's
;;     6.283 rad/s, 3.14 less the belt's slip.
;; And the log: each bad mesh once, "[gears] far-a+far-b: not touching (3000.0 mm apart; to mesh 300.0 mm): ...", and no
;; "unfinished".
(require rackunit racket/system racket/port racket/string racket/list racket/runtime-path
         heroic/godothost)

(define-runtime-path game-dir "../../../game")

(when (godot-available?)
  (define run (godot-simulate 'mesh-faults #:seconds 60 #:sample-dt 1))
  (define (at k t) (for/first ([f run] #:when (>= (car f) (- t 1e-6))) (cadr (assq k (cdr f)))))
  (define (each k) (for/list ([f run]) (cadr (assq k (cdr f)))))

  (test-case "a true pair and a loose one turn the 40 at 15 rpm, teeth in the gaps"
    (for ([g '(true-b loose-b)] [a '(true-a loose-a)])
      (for ([t '(5 30 59)])
        (check-= (at (string->symbol (format "~a.omega" a)) t) 3.1416 0.001)
        (check-= (at (string->symbol (format "~a.omega" g)) t) 1.5708 0.001 (format "~a at ~a s" g t)))
      (check-true (< (abs (at (string->symbol (format "~a.mesh-error" g)) 59)) 0.5) (format "~a's teeth" g))))

  (test-case "a pair 3 m apart: the crank turns its 20, the 40 never moves"
    (check-= (at 'far-a.omega 30) 3.1416 0.001)
    (check-true (andmap (λ (w) (< (abs w) 1e-6)) (each 'far-b.omega)) "far-b at 0 throughout"))

  (test-case "a jammed pair on a crank: neither turns"
    (check-true (andmap (λ (w) (< (abs w) 1e-3)) (each 'jam-a.omega)) "the crank stalls")
    (check-true (andmap (λ (w) (< (abs w) 1e-3)) (each 'jam-b.omega)) "nor does its partner"))

  (test-case "a windmill whose 40 is jammed: the train is held, the sails stand"
    (check-true (andmap (λ (r) (< (abs r) 0.03)) (each 'mill-jam.rpm)) "sails under 0.03 rpm")
    (for ([g '(mill-jam-wheel.omega mill-jam-pinion.omega)])
      (check-true (andmap (λ (w) (< (abs w) 1e-3)) (each g)) (format "~a" g))))

  (test-case "a windmill whose 40 is 3 m from its 20: the sails run up unloaded, the 20 stays put"
    (check-= (at 'mill-far.rpm 59) 28.58 0.1 "3 (1 - e^(-59/9.79)) rad/s")
    (check-true (andmap (λ (w) (< (abs w) 1e-6)) (each 'mill-far-pinion.omega)) "the far 20 at 0"))

  (test-case "the passes after the gear trains are built: the belt turns its pulley"
    (check-= (at 'belt-driver.omega 30) 6.2832 0.001)
    (check-= (at 'belt-driven.omega 30) 3.1416 0.1 "half, less the slip"))

  (test-case "the log: each bad mesh once, what it does; the trains not unfinished"
    (define env (environment-variables-copy (current-environment-variables)))
    (for ([kv '(("HEROIC_AUTORUN" . "1") ("HEROIC_AUTOSELECT" . "mesh-faults") ("HEROIC_QUIT_AFTER_SIM_SECONDS" . "1"))])
      (environment-variables-set! env (string->bytes/utf-8 (car kv)) (string->bytes/utf-8 (cdr kv))))
    (define lines
      (string-split
       (parameterize ([current-directory game-dir] [current-environment-variables env])
         (with-output-to-string (λ () (parameterize ([current-error-port (current-output-port)])
                                        (system* godot-binary "--headless" "--fixed-fps" "120" "--path" ".")))))
       "\n"))
    (define gears (filter (λ (l) (string-prefix? l "[gears] ")) lines))
    (define (once rx) (check-equal? (length (filter (λ (l) (regexp-match? rx l)) gears)) 1 (format "~a in ~s" rx gears)))
    (once #rx"^\\[gears\\] far-a\\+far-b: not touching \\(3000\\.0 mm apart; to mesh 300\\.0 mm\\): the tips are 2680\\.0 mm short")
    (once #rx"^\\[gears\\] jam-a\\+jam-b: jammed \\(250\\.0 mm apart; to mesh 300\\.0 mm\\): 50\\.0 mm too close")
    (once #rx"^\\[gears\\] mill-jam-wheel\\+mill-jam-pinion: jammed \\(250\\.0 mm apart")
    (once #rx"^\\[gears\\] mill-far-wheel\\+mill-far-pinion: not touching \\(3000\\.0 mm apart")
    (once #rx"^\\[gears\\] loose-a\\+loose-b: loose \\(310\\.0 mm apart; to mesh 300\\.0 mm\\)")
    (check-false (ormap (λ (l) (regexp-match? #rx"true-a\\+true-b|unfinished" l)) gears) "nothing said of the true pair; nothing unfinished")))
