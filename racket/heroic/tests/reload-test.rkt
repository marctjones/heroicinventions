#lang racket/base
;; Holding, re-spanning and reloading the throwing machines (issues #189, #161), run in the real game headless.
;; Each prediction is worked by hand in the comment above its check, before the run.
(require rackunit heroic/godothost racket/list racket/math racket/string)

;; A run's value of target.field in the frame nearest t seconds.
(define (value-at run path t)
  (define key (string->symbol (format "~a.~a" (car path) (cadr path))))
  (define frame (for/fold ([best (car run)]) ([f (cdr run)])
                  (if (< (abs (- (car f) t)) (abs (- (car best) t))) f best)))
  (cadr (assq key (cdr frame))))

;; #189. The catapulta's 0.693 m oak bolt, 2.5 cm square: 720 x 0.025^2 x 0.693 = 0.312 kg, 3.06 N. Laid in the channel
;; (top 0.67375 m) its centre is at 0.67375 + 0.0125 = 0.68625 m, and there it stays while the arms are held: the frame
;; carries the 3.06 N. Before #189 it fell straight through (Jolt: a body is pushed off only by layers in its own mask).
(test-case "Catapulta (#189): the bolt lies in its channel while the arms are held, and is shot when they are let go"
  (when (godot-available?)
    (define run (godot-simulate 'vitruvian-catapulta #:seconds 20 #:sample-dt 0.1))
    (for ([t '(0.5 1.0 1.9)])
      (check-= (value-at run '(bolt y) t) 0.68625 0.001 (format "at ~a s" t))
      (check-= (value-at run '(bolt vy) t) 0 1e-3))
    (check-= (value-at run '(right-arm catch) 1.9) 1 0)
    (check-= (value-at run '(right-arm catch) 2.1) 0 0 "let go at 2 s")
    (check-true (> (max-of run '(bolt speed)) 15) "shot")
    (check-true (< 10 (final-of run '(bolt z)) 100) (format "at rest at z = ~a" (final-of run '(bolt z))))))

;; ---------------------------------------------------------------------------
;; #161: span, load, loose, three times. The working is in each blueprint's header.

(define (field f k) (cadr (assq k (cdr f))))
(define (frame-at run t) (for/first ([f run] #:when (>= (car f) (- t 1e-6))) f))
(define (wrap-deg d) (- d (* 360 (round (/ d 360)))))

;; How far a body is, flat, from (x0 z0) when it first comes back down after a throw from time `from`: risen half a
;; metre over y0, then back within 15 cm of it, a metre or more out (the game's [trail] rule, as machine-behavior-test's).
(define (touchdown-after run body from x0 y0 z0)
  (define (v f k) (field f (string->symbol (format "~a.~a" body k))))
  (for/fold ([highest 0] [td #f] #:result td) ([f run] #:when (>= (car f) from))
    (define across (sqrt (+ (sqr (- (v f 'x) x0)) (sqr (- (v f 'z) z0)))))
    (define h (max highest (- (v f 'y) y0)))
    (values h (or td (and (> h 0.5) (< (- (v f 'y) y0) 0.15) (> across 1) across)))))

(define (peak-speed run body a b)
  (for/fold ([m 0]) ([f run] #:when (< a (car f) b)) (max m (field f (string->symbol (format "~a.speed" body))))))

;; The work the windlass's rope does on the arm from `from` until the arm first reaches `catch-deg` (its angle, traced
;; from where it started, offset by `start-deg`): the drum's turning times the rope's pull, less what went into
;; stretching the rope.
(define (work-to-catch run from drum-r start-deg catch-deg sense)
  (let loop ([fs (filter (λ (f) (>= (car f) from)) run)] [w 0])
    (define f (car fs)) (define f2 (cadr fs))
    (if (<= (* sense (- (+ start-deg (field f 'arm.angle)) catch-deg)) 0)
        w
        (loop (cdr fs)
              (+ w (* (field f 'span.tension)
                      (- (* drum-r (degrees->radians (wrap-deg (- (field f2 'windlass.angle) (field f 'windlass.angle)))))
                         (* 0.001 (- (field f2 'span.stretch) (field f 'span.stretch))))))))))

(test-case "Trebuchet (#161): spanned by its windlass and loaded three times, it throws alike every time"
  ;; Spanning lifts the 72.9 kg counterweight 0.27 (sin 90 + sin 50) = 0.4768 m: 341.0 J; the beam's middle comes down
  ;; 0.63 (1 + sin 50) = 1.1126 m, 77.8 J; so the windlass does 263.2 J. Every throw first touches down 9.53 m from
  ;; where the stone lay, within 2%. Before each span a hand steadies the counterweight hanging from the thrown arm, and
  ;; after it the one on the catch (left swinging, the next throw went 5-7% further); the second stone is carried back
  ;; and let go into the pouch by hand, the third laid there by (sling-rope load 1).
  (when (godot-available?)
    (define (cycle t0 load?)
      `((arm catch 1 ,t0) (windlass drive-torque 100 ,t0) (windlass drive-rpm 15 ,t0)
        (windlass drive-rpm -30 ,(+ t0 14)) (windlass drive-rpm 0 ,(+ t0 21))
        ,@(if load? `((sling-rope load 1 ,(+ t0 21.5))) '()) (arm catch 0 ,(+ t0 24))))
    (define (hang-still t0) (format "drag counterweight 0 0 0 at ~a; to 0 0.6267 0 at ~a; release at ~a" (- t0 5) (- t0 4) (- t0 0.5)))
    (define (steady t0) (format "drag counterweight 0 0 0 at ~a; to -0.17365 1.1035 0 at ~a; release at ~a" (+ t0 15) (+ t0 16) (+ t0 18)))
    (define (carry t0) (format "drag stone 0 0 0 at ~a; to 2.1687 0.15 0 at ~a; release at ~a" (+ t0 18.5) (+ t0 22) (+ t0 23)))
    (define run (godot-simulate 'trebuchet #:seconds 106 #:sample-dt 1/120
                                #:set (append '((arm catch 0 3)) (cycle 12 #t) (cycle 45 #f) (cycle 78 #t))   ; the first throw at 3 s, as the demo looses it (#157)
                                #:env `(("HEROIC_DRAG" . ,(string-join (list (hang-still 12) (steady 12) (hang-still 45) (steady 45) (carry 45)
                                                                             (hang-still 78) (steady 78))
                                                                       "; ")))))
    (define x0 (field (car run) 'stone.x))
    (define rest-y (field (frame-at run 11.9) 'counterweight.y))
    (define caught-y (field (frame-at run 33.9) 'counterweight.y))
    (check-= (- caught-y rest-y) 0.4768 (* 0.01 0.4768) (format "the counterweight rose ~a m" (- caught-y rest-y)))
    (define w (work-to-catch run 12 0.1 -50 -50 1))
    (check-= w 263.2 (* 0.03 263.2) (format "the windlass did ~a J on the arm" w))
    (for ([t '(35.9 68.9 101.9)])
      (check-= (field (frame-at run t) 'arm.catch-load) 95.8 1.0 (format "on the catch at ~a s" t))
      (check-= (field (frame-at run t) 'stone.x) x0 0.002 (format "the stone back in the pouch at ~a s" t)))
    (define throws (for/list ([t '(3 36 69 102)]) (touchdown-after run 'stone t x0 0.04 0)))
    (check-true (andmap real? throws) (format "every throw came down: ~a" throws))
    (for ([d throws]) (check-= d 9.53 (* 0.02 9.53) (format "first touchdowns ~a" throws)))))

(test-case "Onager (#161): winched down and loaded three times, the stone leaves as fast every time"
  ;; The skein, twisted 120 degrees at the hook, stores 1/2 x 150 x 2.0944^2 = 329.0 J and gives 301.0 J of it between
  ;; the hook and the crossbeam. Each time the arm is back on the hook it carries 150 x 2.0944 less the arm's 12.7 =
  ;; 301.5 N.m, and with the stone in the sling 275.0; and the stone leaves at the first throw's 12.42 m/s.
  (when (godot-available?)
    (define (cycle t0)
      `((arm catch 1 ,t0) (windlass drive-torque 100 ,t0) (windlass drive-rpm 15 ,t0)
        (windlass drive-rpm -30 ,(+ t0 11.8)) (windlass drive-rpm 0 ,(+ t0 17.7))
        (sling-rope load 1 ,(+ t0 18.5)) (arm catch 0 ,(+ t0 20))))
    (define run (godot-simulate 'torsion-catapult #:seconds 74 #:sample-dt 1/120 #:set (append '((arm catch 0 2)) (cycle 5) (cycle 28) (cycle 51))))
    (define-values (x0 y0) (values (field (car run) 'stone.x) (field (car run) 'stone.y)))
    (for ([t0 '(5 28 51)])
      (check-= (field (frame-at run (+ t0 18.3)) 'arm.catch-load) 301.5 3.0 (format "back on the hook after ~a s" t0))
      (check-= (field (frame-at run (+ t0 19.9)) 'arm.catch-load) 275.0 2.8 "and with the stone in the sling"))
    (define speeds (for/list ([t '(2 25 48 71)]) (peak-speed run 'stone t (+ t 1))))
    (for ([v speeds]) (check-= v 12.42 (* 0.005 12.42) (format "stone speeds ~a" speeds)))
    (define throws (for/list ([t '(2 25 48 71)]) (touchdown-after run 'stone t x0 y0 0)))
    (for ([d throws]) (check-= d (car throws) (* 0.02 (car throws)) (format "first touchdowns ~a" throws)))))

(test-case "Catapulta (#161): drawn by its windlass and a bolt nocked three times, it shoots alike every time"
  (when (godot-available?)
    (define (cycle t0)
      `((right-arm catch 1 ,t0) (left-arm catch 1 ,t0) (windlass drive-torque 400 ,t0) (windlass drive-rpm 15 ,t0)
        (windlass drive-rpm -30 ,(+ t0 15.6)) (windlass drive-rpm 0 ,(+ t0 23.4))
        (string-right load 1 ,(+ t0 24)) (right-arm catch 0 ,(+ t0 25)) (left-arm catch 0 ,(+ t0 25))))
    (define run (godot-simulate 'vitruvian-catapulta #:seconds 95 #:sample-dt 1/120 #:set (append (cycle 5) (cycle 35) (cycle 65))))
    (for ([t '(29.9 59.9 89.9)])
      (check-= (field (frame-at run t) 'bolt.y) 0.68625 0.001 (format "a bolt on the trough at ~a s" t))
      (check-= (field (frame-at run t) 'right-arm.catch-load) 523.6 5.0 "the arms drawn back on their catches"))
    (define speeds (for/list ([t '(2 30 60 90)]) (peak-speed run 'bolt t (+ t 1))))
    (for ([v speeds]) (check-= v (car speeds) (* 0.02 (car speeds)) (format "bolt speeds ~a" speeds)))
    (define out (for/list ([t '(5 33 63 93)]) (field (frame-at run t) 'bolt.z)))
    (for ([z out]) (check-= z (car out) (* 0.02 (car out)) (format "3 s after each shot the bolt is at z ~a" out)))))

(test-case "Catapulta (#161): a bolt can't be nocked while the arms are forward: its strings don't reach"
  (when (godot-available?)
    (check-exn #rx"doesn't reach where its load lies"
               (λ () (godot-simulate 'vitruvian-catapulta #:seconds 6 #:sample-dt 1 #:set '((string-right load 1 5)))))))

(test-case "Battering ram (#161): hauled back 30 degrees by hand and let go, it strikes as hard as the first time"
  ;; From 30 degrees on its 2 m arm the ram arrives at sqrt(2 g L_eq (1 - cos 30)) = 2.30 m/s (the header's working).
  ;; The hand's spring holds it 1 cm short of where it started (its weight's pull along the swing over 20^2 rad2/s2).
  (when (godot-available?)
    (define run (godot-simulate 'battering-rams #:seconds 13 #:sample-dt 1/120
                                #:env '(("HEROIC_DRAG" . "drag stout-ram 0 -2 0 at 6; to -1.0 1.0679 0 at 8; release at 11"))))
    (define first-blow (field (frame-at run 1.0) 'stout-ram.impact-speed))
    (define again (field (frame-at run 12.5) 'stout-ram.impact-speed))
    (check-= first-blow 2.30 (* 0.02 2.30))
    (check-= (field (frame-at run 12.5) 'stout-ram.impacts) 4 0 "one more blow")
    (check-= again first-blow (* 0.02 first-blow) (format "first ~a m/s, hauled back ~a m/s" first-blow again))
    (check-= (field (frame-at run 12.5) 'stout-oak.broken) 0 0 "the stout post still stands")))
