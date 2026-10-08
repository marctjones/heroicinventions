#lang racket/base
;; Wave-4 demo fixes (#176, #190), checked against the compiled machines and
;; the real game run headless.
(require rackunit racket/list racket/runtime-path racket/math heroic/godothost)

(define-runtime-path machines-dir "../../../game/machines")

(define (machine-forms name)
  (cddr (call-with-input-file (build-path machines-dir (format "~a.machine" name)) read)))
(define (form-of forms kind id)
  (for/first ([f forms] #:when (and (pair? f) (eq? (car f) kind) (eq? (cadr f) id))) f))
(define (field form key) (for/first ([x (cdr form)] #:when (and (pair? x) (eq? (car x) key))) (cdr x)))
(define (prop form key) (cadr (assq key (field form 'props))))

(test-case "Trip-sluice (#176): the cord's anchor (2, 2, 0) lies under the arm, which rests on its post, clear of the weight's fall"
  (define forms (machine-forms 'trip-sluice))
  (define cord (form-of forms 'rope 'weight-cord))
  (define arm (form-of forms 'part 'cord-arm))
  (define post (form-of forms 'part 'cord-post))
  (check-equal? (field cord 'from) '(world 2.0 2.0 0.0) "the anchor is where it was")
  ;; Godot's yaw: local x turns to (cos h, -sin h) in (x, z)
  (define-values (ax ay az) (apply values (field arm 'at)))
  (define h (degrees->radians (prop arm 'heading-deg)))
  (define-values (dx dz) (values (- 2.0 ax) (- 0.0 az)))
  (define along (+ (* dx (cos h)) (* dz (- (sin h)))))
  (define across (+ (* dx (sin h)) (* dz (cos h))))
  (check-true (< (abs along) (/ (prop arm 'size-x) 2)) (format "anchor ~a m along the arm, within its half-length" along))
  (check-true (< (abs across) (/ (prop arm 'size-z) 2)) (format "anchor ~a m across the arm, within its half-width" across))
  (check-= ay 2.0 1e-9 "the arm's underside is at the anchor's height")
  (define-values (px py pz) (apply values (field post 'at)))
  (check-= (+ py (prop post 'size-y)) ay 1e-9 "the arm rests on the post's top")
  ;; the post's foot is clear of the 10 cm weight's column, the 50 cm tripwire and the 30 cm race at z = 0
  (check-true (> (- px (/ (prop post 'size-x) 2)) 2.25))
  (check-true (> (- (abs pz) (/ (prop post 'size-z) 2)) 0.15)))

(test-case "Trip-sluice (#176): nothing touches the falling weight: it drops straight, and fires the tripwire at 1 s + sqrt(2 x 0.55 / g)"
  (when (godot-available?)
    (define run (godot-simulate 'trip-sluice #:seconds 2 #:sample-dt 1/120))
    (define (v f k) (cadr (assq k (cdr f))))
    (for ([f run])
      (check-= (v f 'weight.x) 2 1e-6 (format "x at ~a s" (car f)))
      (check-= (v f 'weight.z) 0 1e-6 (format "z at ~a s" (car f))))
    (check-= (v (last run) 'tripwire.fired-at) (+ 1 (sqrt (/ (* 2 0.55) 9.81))) (/ 1 120.0))))

(test-case "Buried crate (#190, #148): in its world (dig-out) it rests where it lies, held by 14.2 kN, until the fourth spit frees it; it never falls"
  (when (godot-available?)
    ;; buried-crate.rkt: 883 + 3433 + 2 x (2500 + 2446) = 14.2 kN under 1 m; freed once its cover is under 12.5 cm, in the
    ;; fourth spit (the third ends at 0.75 m), by 20.9 s, the whole 2 m3 at 15.6 kJ/m3 and 1.5 kW
    (define site (hash-ref (godot-simulate-world 'dig-out #:seconds 25 #:sample-dt 0.1) 'site))
    (define (v f k) (let ([e (assq k (cdr f))]) (and e (cadr e))))
    (define held (filter (λ (f) (eqv? 1 (v f 'crate.buried))) site))
    (check-= (v (car held) 'crate.pull-out) 14200 100)
    (for ([f held]) (check-= (v f 'crate.y) -1.25 1e-6 (format "held where it lies at ~a s" (car f))))
    (define freed (for/first ([f site] #:when (eqv? 0 (v f 'crate.buried))) f))
    (check-true (and freed (>= (v freed 'gang.depth) 0.75) (< (car freed) 20.9)) "freed during the fourth spit")
    (for ([f site]) (check-true (> (v f 'crate.y) -1.26) (format "never below where it lay, at ~a s" (car f))))
    (check-= (v (last site) 'crate.speed) 0 0.01 "at rest at the end")))
