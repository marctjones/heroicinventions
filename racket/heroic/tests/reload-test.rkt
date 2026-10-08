#lang racket/base
;; Holding, re-spanning and reloading the throwing machines (issues #189, #161), run in the real game headless.
;; Each prediction is worked by hand in the comment above its check, before the run.
(require rackunit heroic/godothost racket/list racket/math)

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
