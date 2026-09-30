#lang heroic
;; Four 20 cm cubes let fall from 1.25 m onto the stone floor: hardened
;; steel, granite, oak and a bale of hemp. Every strike is recorded -- how
;; fast the faces met, the impulse, the energy the collision took -- and
;; flashes where it lands.
;;
;; Falling from h, a block meets the floor at sqrt(2 g h) = 4.95 m/s. The
;; engine takes 0.1/s damping off every body (Godot's default; #33 is to
;; replace it with air drag), and steps 120 times a second:
;; v <- v (1 - c dt) - g dt. That brings them down on the 61st tick at
;; 4.864 m/s. A block leaves again at e times that, e its material's
;; restitution (the floor gives none of its own, and Jolt takes the larger
;; of the two), so it rises to about e^2 h, and the collision takes
;; 1/2 m v^2 (1 - e^2):
;;   steel   62.8 kg, e 0.95: back up at 4.62 m/s to 1.036 m;   72.4 J lost
;;   granite 21.6 kg, e 0.60: 2.92 m/s, 0.413 m;               163.5 J
;;   oak      5.8 kg, e 0.50: 2.43 m/s, 0.286 m;                51.1 J
;;   hemp     8.8 kg, e 0.10: 0.49 m/s, 1 cm -- it thuds;      103.1 J
;; (rises worked tick by tick with the damping; e^2 h would be 1.128,
;; 0.450, 0.313 and 0.013 m.)
(define drop (m 1.25))
(define side (cm 20))
(define (at x) (list x (+ drop (/ side 2)) 0))

(define-machine drop-test
  #:source "restitution: Newton, Principia (1687), Scholium to the laws of motion"
  (block steel-block   #:at ((m -1.5) (+ drop (/ side 2)) 0) #:size side #:material steel)
  (block granite-block #:at ((m -0.5) (+ drop (/ side 2)) 0) #:size side #:material granite)
  (block oak-block     #:at ((m 0.5)  (+ drop (/ side 2)) 0) #:size side #:material oak)
  (block hemp-bale     #:at ((m 1.5)  (+ drop (/ side 2)) 0) #:size side #:material hemp))
