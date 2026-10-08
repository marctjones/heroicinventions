#lang racket/base
;; A person's hand on a running machine (#159 dragging, #160 hooking), in the real game (headless, Jolt).
;; A drag is HEROIC_DRAG text -- the part grabbed, the point of it held, the hand's keyframed positions --
;; applied through the same spring the mouse uses. Each prediction below is worked out before the run.
(require rackunit racket/list racket/math heroic/godothost heroic/geometry)

(define (field f key) (cadr (assq key (cdr f))))
(define (frame-at run t) (for/first ([f run] #:when (>= (car f) (- t 1e-6))) f))
(define (value-at run key t) (field (frame-at run t) key))

(define (drag . items) (list (cons "HEROIC_DRAG" (apply string-append (add-between items "; ")))))

;; ---- #159 pull the pendulum aside and let it go
;; fall-and-swing's pendulum (1 m iron rod, 8 cm ball) swings like a point of length I/(m d) = 0.979641 m:
;; T0 = 2 pi sqrt(0.979641 / 9.81) = 1.985541 s, and from theta, T = T0 (1 + theta^2/16) (Huygens). The hand
;; holds the ball 1 m below the pivot (2, 1.5) and takes it to 30 degrees: (2 + sin 30, 1.5 - cos 30) =
;; (2.5, 0.634013); the spring leaves it a little short (it carries the ball's weight), so the angle at release is
;; read off the trace, and T worked out from that: 29.3 degrees gives 2.0180 s, 30 degrees 2.0196 s.
(test-case "Drag: the pendulum pulled to 30 degrees and released swings with Huygens' period, within 1%"
  (when (godot-available?)
    (define run
      (godot-simulate 'fall-and-swing #:seconds 14 #:sample-dt (/ 1 120)
                      #:env (drag "drag swing 0 -1 0 at 0.5" "to 2.5 0.6340127 0 at 2" "release at 5")))
    (define theta (value-at run 'swing.rot-z 4.9))
    (check-= theta 30.0 1.5 "held near 30 degrees")
    (check-= (field (frame-at run 4.9) 'swing.omega) 0.0 1e-3 "at rest when it is let go")
    (define t0 (* 2 pi (sqrt (/ 0.979641 9.81))))
    (check-= t0 1.985541 1e-5)
    (define predicted (* t0 (+ 1 (/ (sqr (degrees->radians theta)) 16))))
    (define after (filter (λ (f) (> (car f) 5.1)) run))
    (define zs (map (λ (f) (field f 'swing.rot-z)) after))
    (define crossings   ; downward through the vertical, interpolated between frames
      (for/list ([a after] [b (cdr after)] [x zs] [y (cdr zs)] #:when (and (> x 0) (<= y 0)))
        (+ (car a) (* (- (car b) (car a)) (/ x (- x y))))))
    (check-true (>= (length crossings) 3))
    (define period (- (second crossings) (first crossings)))
    (check-= period predicted (* 0.01 predicted) (format "period ~a s, predicted ~a s" period predicted))
    (check-= period 2.0196 (* 0.01 2.0196) "and within 1% of the 30 degree figure too")))

;; ---- #159 push a cart to 1 m/s and let it coast
;; cart-push's oak handcart: M = 18.43 kg bed + 4 disc wheels, a = C_rr g M / (M + sum I/r^2) with C_rr = 0.04
;; (carts.rkt header): 0.333 m/s2, so from the speed it has at release it stops in v^2 / 2a (1.50 m from 1 m/s).
(test-case "Drag: a cart pushed to 1 m/s coasts to a stop in the distance its rolling resistance gives"
  (when (godot-available?)
    (define wheel (disc-wheel #:radius 0.15 #:width 0.05))
    (define m (* 720 (shape-volume wheel)))
    (define i (* 720 (vector-ref (shape-inertia wheel) 2)))
    (define bed (* 720 0.4 0.08 0.8))
    (define M (+ bed (* 4 m)))
    (define a (* 0.04 9.81 (/ M (+ M (* 4 (/ i 0.15 0.15))))))
    (check-= a 0.333 0.001)
    (define run
      (godot-simulate 'cart-push #:seconds 10 #:sample-dt 0.05
                      #:env (drag "drag disc-cart 0 0 0 at 0.5" "to 0 0.151 2 at 2.5" "release at 2.5")))
    (define v (value-at run 'disc-cart.vz 2.5))
    (check-= v 1.0 0.02 "pushed to 1 m/s")
    (define z-release (value-at run 'disc-cart.z 2.5))
    (define predicted (/ (* v v) (* 2 a)))
    (define went (- (value-at run 'disc-cart.z 10.0) z-release))
    (check-= went predicted (* 0.03 predicted) (format "coasted ~a m, predicted ~a m" went predicted))
    (check-= (value-at run 'disc-cart.vz 10.0) 0.0 0.01 "and stopped")))

;; ---- #159 a recorded drag replays to the same trace
;; A drag made with the mouse (tools/gui-check.sh, cart-push, press on the cart's bed, drag 100 px to the right across the view, hold, let go)
;; printed the line below ([hand] HEROIC_DRAG=...). Replayed headless it takes the cart to the same places the live drag did:
;; -0.74823 m at 1 s, -0.69563 m at 2 s (read from the live run's trace).
(test-case "Drag: a drag recorded from the mouse, replayed, reproduces the live trace"
  (when (godot-available?)
    (define run (godot-simulate 'cart-push #:seconds 4 #:sample-dt 0.25
                                #:env (drag "drag disc-cart 0.149232 0.0399997 1.50703e-07 at 0.75"
                                  "to 0.149252 0.189992 6.37186e-07 at 0.783333"
                                  "to 0.149252 0.189992 -0.421861 at 0.791667"
                                  "to 0.149252 0.189992 -0.421861 at 0.916667"
                                  "to 0.149252 0.189992 -0.843722 at 0.925"
                                  "to 0.149252 0.189992 -0.843722 at 1.3"
                                  "release at 1.3")))
    (check-= (value-at run 'disc-cart.z 1.0) -0.74823 1e-3)
    (check-= (value-at run 'disc-cart.z 2.0) -0.69563 1e-3)))

(test-case "Drag: the same drag text twice gives the same trace"
  (when (godot-available?)
    (define (go) (godot-simulate 'fall-and-swing #:seconds 8 #:sample-dt 0.1
                                 #:env (drag "drag swing 0 -1 0 at 0.5" "to 2.5 0.6340127 0 at 2" "release at 4")))
    (check-equal? (go) (go))))

(test-case "Drag: a drag that cannot happen fails the run instead of passing silently"
  (when (godot-available?)
    (check-exn #rx"HEROIC_SET" (λ () (godot-simulate 'cart-push #:seconds 1 #:env (drag "drag no-such-body 0 0 0 at 0.2" "release at 0.6"))))
    (check-exn #rx"HEROIC_SET" (λ () (godot-simulate 'cart-push #:seconds 1 #:env (drag "drag disc-cart 0 0 0 at 0.2"))))
    (check-exn #rx"HEROIC_SET" (λ () (godot-simulate 'cart-push #:seconds 1 #:env (drag "drag disc-cart 0 0 0 at 5" "release at 6"))))))
