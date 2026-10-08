#lang racket/base
;; Hands-on actions (issue #162): turn a vessel over, aim a mirror by hand. Each is a timed action, (target field value at),
;; the form the operator log (#153) writes, so a click or a drag in the game and this test do the same thing.
;; (Picking where a digger works is a ground test in tests/HeroicInventions.Sim.Tests/HandsOnTests.cs: it needs a map.)
(require rackunit heroic/simhost)

(define (at run t k)
  (define f (for/first ([f run] #:when (>= (car f) (- t 1e-6))) f))
  (cadr (assq k (cdr f))))

(test-case "A heliostat aimed off its receiver delivers nothing there and the cosine-law power comes back when it is re-aimed"
  ;; heliostats.rkt, sun held at noon: north-mirror keeps 0.832349 of 0.5 m2 of a 950.6588 W/m2 beam x 0.85 = 336.294 W.
  ;; Aim it at 10 s at a point 3 m east of the boiler (aim-dx 3: aims are offsets from the mirror, which stands at x -1.5): the receiver is no longer lit and its power is 0;
  ;; the plate still catches sunlight (a different cosine) but not onto the boiler. Re-aim at 40 s: 336.294 W again.
  ;; The boiler (1 kg, 2 W/K to 20 C air, 4186 J/K) warms by x' = (P - 2x)/4186, x = T - 20: 336.294 W for 10 s,
  ;; then none for 30 s (it cools slightly), then 336.294 W again; closed form per phase below.
  (define run (simulate 'heliostats #:seconds 60 #:step 0.01 #:sample-dt 10
                        #:set '((scene clock-rate 0) (north-mirror aim-dx 3 10) (north-mirror aim-dx 0 40))))
  (check-= (at run 0 'north-mirror.power) 336.294015 1e-4 "on its receiver: the header's cosine-law power")
  (check-= (at run 0 'north-mirror.hit) 1 0)
  (check-= (at run 20 'north-mirror.power) 0 1e-12 "aimed off: nothing reaches the receiver")
  (check-= (at run 20 'north-mirror.hit) 0 0)
  (check-true (> (at run 20 'north-mirror.sunlit) 100) "it still catches the sun, onto the wrong place")
  (check-= (at run 50 'north-mirror.power) 336.294015 1e-4 "re-aimed: the same power")
  (check-= (at run 50 'north-mirror.hit) 1 0)
  (define (phase x p dt) (+ (/ p 2) (* (- x (/ p 2)) (exp (/ (* -2 dt) 4186)))))
  (define x10 (phase 0 336.294015 10))
  (define x40 (phase x10 0 30))
  (define x60 (phase x40 336.294015 20))
  (check-= (at run 10 'north-lit.temperature) (+ 20 x10) 2e-3)
  (check-= (at run 40 'north-lit.temperature) (+ 20 x40) 2e-3)
  (check-= (at run 60 'north-lit.temperature) (+ 20 x60) 2e-3)
  (check-= (at run 40 'south-lit.temperature) (+ 20 (phase 0 303.023514 40)) 2e-3 "the other boiler is not touched"))

(test-case "A mirror picked onto another part lights that part's place, not its own receiver"
  ;; heliostats.rkt parts in order: stand-a 0, north-lit 1, north-mirror 2, stand-b 3, south-lit 4. Aimed at south-lit
  ;; from (-1.5, 0.3, -3) to (1.5, 1.1, 0): the vector (3, 0.8, 3), 4.3174 m, sun at noon (0, 0.99066, 0.13647):
  ;; s.t = (0.8 x 0.99066 + 3 x 0.13647) / 4.3174 = 0.27731, cos(theta/2) = sqrt(1.27731 / 2) = 0.79916.
  (define run (simulate 'heliostats #:seconds 5 #:step 0.01 #:sample-dt 5
                        #:set '((scene clock-rate 0) (north-mirror aim-part 4 0))))
  (check-= (at run 5 'north-mirror.aim-dx) 3 1e-12)
  (check-= (at run 5 'north-mirror.aim-dy) 0.8 1e-12)
  (check-= (at run 5 'north-mirror.aim-dz) 3 1e-12)
  (check-= (at run 5 'north-mirror.cosine) 0.799159 1e-5)
  (check-= (at run 5 'north-mirror.power) 0 1e-12 "its own boiler gets none"))

(test-case "A mirror left still (tracking off) stops following the sun, and its spot walks off the receiver"
  ;; south-mirror at (1.5, 0.3, 3) onto (1.5, 1.1, 0), noon: the face it holds is n = (0, 0.83235, -0.55425). At 15:00
  ;; (set at 5 s) the sun is 49.5548 deg up at azimuth 270.383, s = (-0.64871, 0.76103, -0.00434): n.s = 0.63585, so it still
  ;; catches 886.269 x 0.5 x 0.85 x 0.63585 = 239.500 W, but the reflected ray (0.6487, 0.2975, -0.7005) passes 2.04 m from the
  ;; boiler, past its 0.5 m, so none reaches it. A tracking mirror (north) keeps the cosine law at 15:00: 290.776 W.
  (define run (simulate 'heliostats #:seconds 10 #:step 0.01 #:sample-dt 5
                        #:set '((scene clock-rate 0) (south-mirror track 0 0) (scene time 15 5))))
  (check-= (at run 0 'south-mirror.power) 303.023514 1e-4 "still at noon: it was facing right")
  (check-= (at run 10 'south-mirror.track) 0 0)
  (check-= (at run 10 'south-mirror.sunlit) 239.5004 1e-3)
  (check-= (at run 10 'south-mirror.power) 0 1e-12)
  (check-= (at run 10 'north-mirror.power) 290.775853 1e-4 "a tracking mirror keeps its power"))

(test-case "A sand timer turned at 30 s runs for the time it had run, and turned again when empty it runs its full time"
  ;; sand-timer.rkt: 5 kg through a 10 mm orifice at 25.9 g/s (0.58 x 1600 x sqrt 9.81 x (10 - 0.45 mm)^2.5).
  ;; At 30 s 0.777 kg have run out and 4.223 kg remain; turned, 0.777 kg are above, and they are gone 30 s later, at
  ;; 60 s. Turned again at 100 s (all 5 kg below) it runs the full 5 / 0.0259 = 193.0 s: empty at 293 s.
  (define flow (* 0.58 1600 (sqrt 9.81) (expt (- 0.010 (* 1.5 0.0003)) 2.5)))
  (define run (simulate 'sand-timer #:seconds 300 #:step 0.05 #:sample-dt 1
                        #:set '((sand turn 1 30) (sand turn 1 100))))
  (check-= (at run 29 'sand.mass) (- 5 (* 29 flow)) 2e-3)
  (check-= (at run 31 'sand.mass) (* 29 flow) 2e-2 "just after the turn: the 30 s that had run, less 1 s of running")
  (check-= (at run 31 'sand.turns) 1 0)
  (check-= (at run 59 'sand.empty) 0 0)
  (check-= (at run 61 'sand.empty) 1 0 "empty about 30 s after the turn")
  (check-= (at run 101 'sand.turns) 2 0)
  (check-= (at run 101 'sand.mass) (- 5 flow) 2e-2 "all of it above again")
  (check-= (at run 290 'sand.empty) 0 0 "not yet at 290 s")
  (check-= (at run 295 'sand.empty) 1 0 "empty by 295 s: 193.0 s after the turn"))
