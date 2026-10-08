#lang racket/base
;; A person's hand on a running machine (#159 dragging, #160 hooking), in the real game (headless, Jolt).
;; A drag is HEROIC_DRAG text -- the part grabbed, the point of it held, the hand's keyframed positions --
;; applied through the same spring the mouse uses. Each prediction below is worked out before the run.
(require rackunit racket/list racket/math heroic/godothost heroic/geometry)

(define (field f key) (cadr (assq key (cdr f))))
(define (frame-at run t) (for/first ([f run] #:when (>= (car f) (- t 1e-6))) f))
(define (value-at run key t) (field (frame-at run t) key))

(define (drag . items) (list (cons "HEROIC_DRAG" (apply string-append (add-between items "; ")))))

;; (a field a frame may not have: a rope's hook exists only while the rope has let its load go)
(define (maybe f key) (let ([p (assq key (cdr f))]) (and p (cadr p))))
(define (window run from to) (filter (λ (f) (<= from (car f) to)) run))
(define (mean xs) (/ (apply + xs) (length xs)))

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
;; ---- #160 hook and unhook loads
;; roman-crane-reload.rkt is the Roman crane with a second 583 kg stone (stone-b) and a 900 kg one (stone-c) on the ground
;; beside the first. Timeline (sim s): the walkers turn back at 4 s (tympanus drive-rpm -3) and stop at 8.5 s, the first stone
;; down since about 6 s; "hoist hook 0" at 8.5 lets it go; a hand takes the hook (grabbed at 9 s) over the other stone's top
;; (z 0.8 m to one side) and lets go at 11.5 s; the walkers turn forward again at 12 s.
(define (reload-run machine stone-z stone-top)
  (godot-simulate machine #:seconds 30 #:sample-dt 0.5
                  #:set '((tympanus drive-rpm -3 4) (tympanus drive-rpm 0 8.5) (hoist hook 0 8.5) (tympanus drive-rpm 3 12))
                  #:env (drag "drag hoist.hook 0 0 0 at 9"
                              (format "to 3.35 ~a ~a at 11" (+ stone-top 0.045) stone-z)
                              "release at 11.5")))

;; Three walkers (roman-crane-reload-gang: 3 x 772.5 = 2318 N.m, more than any load here) lift stone-b as the header of
;; roman-crane says a stone is lifted: the drum at 3 rpm winds the rope in at 2 pi (0.25)(3/60) = 7.85 cm/s, the rope
;; carries 583.2 x 9.81 = 5721 N, which is 1430 N.m at the 25 cm drum.
(test-case "Hook: after setting the first stone down and unhooking, the crane hooks a second 583 kg stone and lifts it at 7.85 cm/s with 1430 N.m at the drum"
  (when (godot-available?)
    (define run (reload-run 'roman-crane-reload-gang 1.85 0.605))
    (check-= (value-at run 'stone.y 8.0) 0.3 0.01 "the first stone is down")
    (check-not-false (maybe (frame-at run 9.5) 'hoist-hook.y) "the rope's end hangs on a hook of its own while it is unhooked")
    (check-= (value-at run 'hoist-hook.z 11.0) 1.85 0.01 "and the hand has taken it over the second stone")
    (check-false (maybe (frame-at run 12.0) 'hoist-hook.y) "let go over the stone, the hook is gone: the rope is tied to the stone")
    (check-= (value-at run 'stone.y 30.0) 0.3 0.001 "the first stone stays where it was")
    (define late (window run 21 30))
    (define rise (/ (- (field (last late) 'stone-b.y) (field (first late) 'stone-b.y)) (- (car (last late)) (car (first late)))))
    (check-= rise (* 3 (/ (* 2 pi) 60) 0.25) (* 0.03 0.0785) (format "stone-b rises at ~a m/s" rise))
    (define tension (mean (map (λ (f) (field f 'hoist.tension)) late)))
    (check-= tension (* 583.2 9.81) (* 0.02 5721) (format "the rope carries ~a N" tension))
    (check-= (* tension 0.25) 1430 30 "N.m at the drum")
    (check-= (field (last run) 'hoist.broken) 0 1e-9)))

;; Two walkers (roman-crane-reload) give 2 x 772.5 = 1545 N.m. stone-c's 900.2 kg is 2207 N.m at the drum: more. The drive
;; pulls the rope to what it can, 1545 / 0.25 = 6180 N, the stone does not rise, and the wheel stands still.
(test-case "Hook: a 900 kg stone is beyond two walkers: it does not rise and the wheel stalls with the rope at 6180 N"
  (when (godot-available?)
    (define run (reload-run 'roman-crane-reload 0.25 0.6984))
    (define settled (value-at run 'stone-c.y 14.0))
    (check-= (* 900.2 9.81 0.25) 2207 1 "the load at the drum")
    (check-= (value-at run 'stone-c.y 30.0) settled 0.01 "the stone is where it was")
    (check-= (field (last run) 'tympanus.omega) 0.0 0.005 "the wheel has stalled")
    (define tension (mean (map (λ (f) (field f 'hoist.tension)) (window run 27 30))))
    (check-= tension 6180 (* 0.01 6180) (format "the rope holds ~a N, what 1545 N.m can pull" tension))
    (check-= (field (last run) 'hoist.broken) 0 1e-9)))

(test-case "Hook: `hoist hook 1` hooks the hook to what lies within reach of it, and unhooking again lets the load go"
  (when (godot-available?)
    ;; the walkers have lifted the stone a few centimetres when it is unhooked at 2 s (it falls back); it is hooked again at
    ;; 4 s, the hook lying 0.07 m above its top, within the 15 cm reach; lifted; unhooked once more at 11 s
    (define run (godot-simulate 'roman-crane-reload-gang #:seconds 14 #:sample-dt 0.5
                                #:set '((hoist hook 0 2) (tympanus drive-rpm 0 2) (hoist hook 1 4) (tympanus drive-rpm 3 4) (hoist hook 0 11))))
    (check-= (value-at run 'stone.y 3.5) 0.3 0.01 "unhooked, it is back on the ground")
    (check-not-false (maybe (frame-at run 3.0) 'hoist-hook.y) "on a hook of its own")
    (check-false (maybe (frame-at run 5.0) 'hoist-hook.y) "hooked again: the hook is gone")
    (define up (value-at run 'stone.y 10.5))
    (check-true (> up 0.4) (format "and the walkers lift it again, to ~a m" up))
    (check-= (value-at run 'stone.y 14.0) 0.3 0.01 "unhooked once more, it falls back")
    (check-not-false (maybe (frame-at run 13.0) 'hoist-hook.y))))

(test-case "Hook: unhooking works on every rope crane, and a load let go falls"
  (when (godot-available?)
    (for ([machine '(bar-crane bar-crane crane-hoist)] [rope '(seven-hoist two-hoist hoist)] [stone '(seven-stone two-stone stone)])
      (define run (godot-simulate machine #:seconds 9 #:sample-dt 1 #:set (list (list rope 'hook 0 3))))
      (check-not-false (maybe (frame-at run 4.0) (string->symbol (format "~a-hook.y" rope))) (format "~a is on a hook of its own" rope))
      (check-= (value-at run (string->symbol (format "~a.y" stone)) 9.0) 0.3 0.01 (format "~a lies on the ground, let go" stone)))))

;; ---- #160 tongs close on whatever lies between their jaws
;; crate-tongs: firm-tongs (140 N, reach 15 cm) hold a 7.47 kg iron crate 1.2 m up. Opened at 1 s the crate falls to the step. A
;; hand lifts it back to the jaws (2, 1.2) between 2 and 3.5 s; closed at 3.7 s the tongs take it, and the hand lets go at 4 s.
;; Held, it hangs at the place the hand left it (the hand's spring carries the crate's weight, a sag of g/w^2 = 2.5 cm: 1.175 m).
(test-case "Tongs: closed on a crate set between the jaws they hold it; closed with nothing in reach they take what is brought"
  (when (godot-available?)
    (define lift (drag "drag firm-crate 0 0 0 at 2" "to 2 1.2 0 at 3.5" "release at 4"))
    (define run (godot-simulate 'crate-tongs #:seconds 8 #:sample-dt 0.5 #:set '((firm-tongs closed 0 1) (firm-tongs closed 1 3.7)) #:env lift))
    (check-= (value-at run 'firm-crate.y 3.5) 1.17 0.03 "the hand has the crate at the jaws")
    (check-= (value-at run 'firm-tongs.held 3.5) 0 1e-9 "the tongs are open: not held")
    (check-= (value-at run 'firm-tongs.held 5.0) 1 1e-9 "closed, they hold it")
    (check-= (value-at run 'firm-crate.y 8.0) 1.175 0.03 "and it stays where it was, hand gone")
    (define closed-first (godot-simulate 'crate-tongs #:seconds 8 #:sample-dt 0.5 #:set '((firm-tongs closed 0 1) (firm-tongs closed 1 1.8)) #:env lift))
    (check-= (value-at closed-first 'firm-tongs.held 1.9) 0 1e-9 "closed with the crate on the step, beyond reach: nothing held")
    (check-true (< (value-at closed-first 'firm-crate.y 1.9) 0.7) "the crate lies on the step")
    (check-= (value-at closed-first 'firm-tongs.held 5.0) 1 1e-9 "brought between the jaws, it is taken")
    (check-true (< (abs (- (value-at closed-first 'firm-crate.y 8.0) 1.2)) 0.15) "and it hangs in the jaws (they took it as it came within their 15 cm reach)")))
