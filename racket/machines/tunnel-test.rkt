#lang heroic
;; Continuous collision detection (issue #28). Two identical 5 cm lead bolts
;; (sling shot, 1.42 kg) are let fall 299 m onto two identical thin planks, 2 cm
;; of lead sheet floating 1 m up. The engine steps 120 times a second and the
;; bolts arrive at about 76.6 m/s (sqrt(2 g 299); the fall is 7.81 s, the engine
;; no longer damping bodies since #33), so each moves 0.64 m a tick.
;; A plank plus a bolt is 7 cm: a bolt that is only tested where it stands at
;; each tick is on one side of the plank one tick and the other side the next,
;; and never touches it.
;;   fast   bolt-fast is swept along its path each step (the default): it
;;          lands on its plank at 76.5 m/s and bounces. Lead on lead gives back
;;          e = 0.2 of it, 15.3 m/s, so it rises 15.3^2 / 2g = 12.0 m and is back
;;          on the plank 2 (15.3) / g = 3.1 s later; one more hop of 0.5 m and it
;;          lies on its plank from about 12 s, its middle 1.045 m up, never below 1.0 m.
;;   plain  bolt-plain has #:fast #f: it goes straight through its plank (at
;;          0.64 m a tick its middle is seen at 1.26 m, then 0.62 m) and on to
;;          the ground plane at y = 0, a metre below it, where it bounces up at
;;          0.2 x 76.7 = 15.3 m/s. That is still 0.13 m a tick, so whether the plank
;;          stops it from below depends on where the ticks fall: here one puts its
;;          middle at 0.987 m, inside the plank, which turns it back down. It lies on
;;          the ground under its plank from 8.5 s.
;; Lead because it hardly bounces (#192). Iron bolts on stone (e 0.55; Jolt takes the
;; larger of the two surfaces' restitution, so the plank is lead too) went 91 m back up,
;; out of any view of the planks, for 20 s, and their tumbling knocked the swept bolt off
;; its plank to the ground 20 m away while the plain one came to rest on its plank: the
;; opposite of the lesson. The fall itself is the same to the bit; the planks stand
;; 1.2 m apart, not 2, to be framed closer.
(define-machine tunnel-test
  #:source "A bolt too fast for its plank, with and without continuous collision detection"
  (post plank-fast #:at ((cm -60) (m 1) 0) #:size ((m 1) (cm 2) (m 1)) #:material lead)
  (post plank-plain #:at ((cm 60) (m 1) 0) #:size ((m 1) (cm 2) (m 1)) #:material lead)
  (block bolt-fast #:at ((cm -60) (m 300) 0) #:size (cm 5) #:material lead)
  (block bolt-plain #:at ((cm 60) (m 300) 0) #:size (cm 5) #:material lead #:fast #f))
