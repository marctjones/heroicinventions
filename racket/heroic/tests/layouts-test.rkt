#lang racket/base
;; Five demos laid out to show what they do (#192), checked against the compiled
;; machines and the real game run headless. The physics proofs (before/after
;; traces) are in the commit messages; these pin what the new layouts promise.
(require rackunit racket/list racket/runtime-path racket/math heroic/godothost)

(define-runtime-path machines-dir "../../../game/machines")

(define (machine-forms name)
  (cddr (call-with-input-file (build-path machines-dir (format "~a.machine" name)) read)))
(define (parts-of forms kind)
  (for/list ([f forms] #:when (and (pair? f) (eq? (car f) 'part) (eq? (caddr f) kind))) f))
(define (at-of part) (cdr (assq 'at (cdddr part))))
(define (value-at run path t)                ; the frame nearest t
  (define key (string->symbol (format "~a.~a" (car path) (cadr path))))
  (define frame (for/fold ([best (car run)]) ([f (cdr run)]) (if (< (abs (- (car f) t)) (abs (- (car best) t))) f best)))
  (cadr (assq key (cdr frame))))
(define (prop part key) (let ([p (assq key (cdr (assq 'props (cdddr part))))]) (and p (cadr p))))

;; Wheels on one axle line are drawn as one axle, with one label of every name on it stacked in a column (MachineView's
;; BuildAxleSupports): trip-hammer's two wheels and ratchet-windlass's four drums stood one behind the other along their
;; axles, and read as one machine. Each now has an axle line of its own.
(test-case "Layouts (#192): no two wheels of trip-hammer or ratchet-windlass share an axle line"
  (for ([name '(trip-hammer ratchet-windlass)])
    (define wheels (parts-of (machine-forms name) 'wheel))
    (check-true (>= (length wheels) 2))
    (for ([w wheels]) (check-eq? (prop w 'axis) 'z (format "~a: ~a turns about z" name (cadr w))))
    ;; about z, an axle line is where it pierces the x-y plane
    (define lines (for/list ([w wheels]) (take (at-of w) 2)))
    (check-equal? (length (remove-duplicates lines)) (length lines) (format "~a: axle lines ~a" name lines))))

;; falling-stones.rkt: rho 1.2041, Cd 0.47, A = pi (0.05)^2; v = vt tanh(g t / vt), vt = sqrt(2 m g / (rho Cd A)).
(test-case "Falling stones (#192): the air catches the cedar ball, slows the iron one, and not the bare one, 100 m apart"
  (when (godot-available?)
    (define run (godot-simulate 'falling-stones #:seconds 15 #:sample-dt 1/120))
    (define A (* pi (sqr 0.05)))
    (define (vt density) (sqrt (/ (* 2 (* density 4/3 pi (expt 0.05 3)) 9.81) (* 1.2041 0.47 A))))
    (define (v-at density t) (let ([v (vt density)]) (* v (tanh (/ (* 9.81 t) v)))))
    (check-= (vt 380) 29.64 0.01)
    (check-= (vt 7700) 133.4 0.1)
    (check-= (- (value-at run '(cedar-ball vy) 6.0)) (v-at 380 6) 0.1 "cedar: 96% of its terminal speed at 6 s")
    (check-= (- (value-at run '(iron-ball vy) 6.0)) (v-at 7700 6) 0.1 "iron: 55.3 m/s at 6 s")
    (check-= (- (value-at run '(vacuum-ball vy) 6.0)) (* 9.81 6) 0.1 "the bare ball: g t")
    ;; the landings: the bare ball at sqrt(2 g h) from its 349.95 m (its middle 5 cm up when it touches)
    (define (first-impact ball)
      (for/first ([t (times-of run)] [s (values-of run (list ball 'impact-speed))] #:when (> s 0)) (cons t s)))
    (check-= (cdr (first-impact 'vacuum-ball)) (sqrt (* 2 9.81 349.95)) 0.2)
    (check-= (car (first-impact 'vacuum-ball)) (sqrt (/ (* 2 349.95) 9.81)) 0.02)
    (check-= (cdr (first-impact 'cedar-ball)) (vt 380) 0.05 "the cedar ball lands at its terminal speed")
    (check-true (< (car (first-impact 'vacuum-ball)) (car (first-impact 'iron-ball)) (car (first-impact 'cedar-ball))))
    ;; each falls straight down its own lane
    (for ([ball '(cedar-ball iron-ball vacuum-ball)] [x '(-100 0 100)])
      (check-= (value-at run (list ball 'x) 8.0) x 1e-6))))
