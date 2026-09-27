#lang heroic
;; Newton's cradle: five equal pendulums hung in a touching row. Pull the
;; end one back and let go — momentum and energy transfer through the
;; row via collision, not through any rule we write. All five are the
;; same `pendulum` part used for the single-pendulum demo; the only
;; trick is spacing their pivots so the bobs just touch at rest.

;; BuildPendulum sizes the bob as max(3cm, 8% of length); at this length
;; that's exactly 4cm, so pivots 8cm apart put resting bobs edge-to-edge.
(define bob-length (cm 50))
(define spacing (cm 8))
(define (pivot-x i) (* (- i 2) spacing)) ; i=0..4, centred on i=2

(define-machine newtons-cradle
  #:source "Classic mechanics demonstration"
  (pendulum ball-0 #:at ((pivot-x 0) 0.9 0) #:length bob-length #:material iron #:start-angle-deg 35)
  (pendulum ball-1 #:at ((pivot-x 1) 0.9 0) #:length bob-length #:material iron)
  (pendulum ball-2 #:at ((pivot-x 2) 0.9 0) #:length bob-length #:material iron)
  (pendulum ball-3 #:at ((pivot-x 3) 0.9 0) #:length bob-length #:material iron)
  (pendulum ball-4 #:at ((pivot-x 4) 0.9 0) #:length bob-length #:material iron))
