#lang racket/base
;; #186: a ball set down on flat ground stays where it is. It used to roll: the ground was a 2 km box, and
;; at 1 km from its centre the float error in Jolt's support points tilted a resting contact's normal by a few
;; hundredths of a radian (0.05 m/s^2 along the floor, 1.5 m in 10 s, in a direction that depended on where the ball
;; stood and how big the floor was). The ground is a plane now. Prediction: on a level floor a ball at rest has
;; no force along it, so after 10 s it is within 1 mm of where it was put, and not spinning.
;; The machine is written to game/machines for the run and removed after.
(require rackunit racket/file racket/path racket/runtime-path
         heroic/godothost heroic/machine heroic/emit)

(define-runtime-path game-machines "../../../game/machines")

(define (field f key) (cadr (assq key (cdr f))))
(define run
  (and (godot-available?)
       (let ([src (make-temporary-file "ball-at-rest-~a.rkt")]
             [dest (build-path game-machines "ball-at-rest-test.machine")])
         (dynamic-wind
          void
          (λ ()
            (with-output-to-file src #:exists 'truncate/replace
              (λ () (write-string "#lang heroic
(define-machine ball-at-rest-test
  #:source \"A ball on flat ground\"
  (ball iron-ball #:at (1.5 0.05 0.5) #:radius (cm 5) #:material iron)
  (ball bronze-ball #:at (-40 0.03 7) #:radius (cm 3) #:material bronze))
")))
            (dynamic-require src #f)
            (write-machine-file (car (take-registered-machines)) dest)
            (godot-simulate 'ball-at-rest-test #:seconds 10 #:sample-dt 0.5))
          (λ () (delete-file src) (when (file-exists? dest) (delete-file dest)))))))

(test-case "Balls set down on flat ground stay within 1 mm over 10 s, near the origin and 40 m out"
  (when run
    (define last-frame (car (reverse run)))
    (for ([id '(iron-ball bronze-ball)] [at '((1.5 0.5) (-40 7))])
      (define (v k) (field last-frame (string->symbol (format "~a.~a" id k))))
      (check-= (v 'x) (car at) 0.001 (format "~a x" id))
      (check-= (v 'z) (cadr at) 0.001 (format "~a z" id))
      (check-= (v 'speed) 0 0.001 (format "~a speed" id))
      (check-= (v 'omega) 0 0.01 (format "~a not spinning" id)))))
