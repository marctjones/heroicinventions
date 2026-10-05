#lang heroic
;; Four 20 cm cubes let fall from 1.25 m onto the stone floor: hardened
;; steel, granite, oak and a bale of hemp. Every strike is recorded -- how
;; fast the faces met, the impulse, the energy the collision took -- and
;; flashes where it lands.
;;
;; Falling from h, a block meets the floor at sqrt(2 g h) = 4.95 m/s. The
;; engine takes no damping off a body (#33 turned Godot's default 0.1/s
;; off, leaving air drag to blocks that ask for it), and steps 120 times a
;; second: v <- v - g dt. That leaves them 3.3 mm up after 60 ticks and the
;; 61st tick strikes them at 60 g dt = 4.905 m/s. A block leaves again at e times that, e its material's
;; restitution (the floor gives none of its own, and Jolt takes the larger
;; of the two), so it rises to about e^2 h, and the collision takes
;; 1/2 m v^2 (1 - e^2):
;;   steel   62.8 kg, e 0.95: back up at 4.66 m/s to 1.087 m;   73.7 J lost
;;   granite 21.6 kg, e 0.60: 2.94 m/s, 0.429 m;               166.3 J
;;   oak      5.8 kg, e 0.50: 2.45 m/s, 0.296 m;                52.0 J
;;   hemp     8.8 kg, e 0.10: 0.49 m/s, 1 cm -- it thuds;      104.8 J
;; (rises worked tick by tick; e^2 h would be 1.128, 0.450, 0.313 and
;; 0.013 m.)
(define drop (m 1.25))
(define side (cm 20))
(define (at x) (list x (+ drop (/ side 2)) 0))

(define-machine drop-test
  #:source "restitution: Newton, Principia (1687), Scholium to the laws of motion"
  (block steel-block   #:at ((m -1.5) (+ drop (/ side 2)) 0) #:size side #:material steel)
  (block granite-block #:at ((m -0.5) (+ drop (/ side 2)) 0) #:size side #:material granite)
  (block oak-block     #:at ((m 0.5)  (+ drop (/ side 2)) 0) #:size side #:material oak)
  (block hemp-bale     #:at ((m 1.5)  (+ drop (/ side 2)) 0) #:size side #:material hemp))
