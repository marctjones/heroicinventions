#lang heroic
;; Newton's cradle: five equal pendulums hung in a touching row. Pull the
;; end one back and let go — momentum and energy transfer through the
;; row via collision, not through any rule we write. All five are the
;; same `pendulum` part used for the single-pendulum demo; the only
;; trick is spacing their pivots so the bobs just touch at rest.

;; BuildPendulum sizes the bob as max(3cm, 8% of length); at this length
;; that's exactly 4cm, so pivots 8cm apart would put resting bobs exactly
;; edge-to-edge. Half a millimetre more gives the physics engine separate
;; collisions to resolve one after another down the row, instead of one
;; five-way contact it would share out among all the balls at once.
;;
;; Hardened steel, as real cradles use: it gives back 95% of the closing
;; speed in a collision. With wrought iron (55%) the momentum still
;; passes down the row, but most of the energy is lost on the way.
(define bob-length (cm 50))
(define spacing (+ (cm 8) (mm 0.5)))
(define (pivot-x i) (* (- i 2) spacing)) ; i=0..4, centred on i=2

(define-machine newtons-cradle
  #:source "Classic mechanics demonstration"
  ;; Negative: a positive angle swings the bob toward +x — into the row.
  ;; The end ball has to be pulled back outward, away from its neighbours.
  (pendulum ball-0 #:at ((pivot-x 0) 0.9 0) #:length bob-length #:material steel #:start-angle-deg -35)
  (pendulum ball-1 #:at ((pivot-x 1) 0.9 0) #:length bob-length #:material steel)
  (pendulum ball-2 #:at ((pivot-x 2) 0.9 0) #:length bob-length #:material steel)
  (pendulum ball-3 #:at ((pivot-x 3) 0.9 0) #:length bob-length #:material steel)
  (pendulum ball-4 #:at ((pivot-x 4) 0.9 0) #:length bob-length #:material steel))
