#lang racket/base
;; The jib of a Roman building crane: two legs leaning out from the base
;; into an A-frame, a crosspiece at the top for the hoist pulley's axle,
;; and a strut behind. (Vitruvius X.2; the Haterii relief.) Origin on the
;; ground at the middle of the legs' feet; the jib leans toward +X, its
;; legs spread along Z.
(require racket/math "mesh.rkt" "shape.rkt")
(provide crane-jib)

;; A square beam of side s from point p to point q.
(define (beam p q s)
  (define d (v- q p))
  (define len (vlen d))
  (define dir (vnorm d))
  ;; box-mesh is long along Z; turn Z onto dir
  (define z (v3 0 0 1))
  (define axis (vcross z dir))
  (define rot
    (if (< (vlen axis) 1e-9)
        (rot-x (if (< (vdot z dir) 0) pi 0))
        (axis-angle (vnorm axis) (acos (max -1.0 (min 1.0 (vdot z dir)))))))
  (mesh-transform (box-mesh s s len) rot (v* (v+ p q) 0.5)))

;; Rodrigues: the rotation by angle a about unit axis k, as three rows.
(define (axis-angle k a)
  (define-values (x y z) (values (vector-ref k 0) (vector-ref k 1) (vector-ref k 2)))
  (define c (cos a)) (define s (sin a)) (define t (- 1 c))
  (vector (v3 (+ (* t x x) c)       (- (* t x y) (* s z)) (+ (* t x z) (* s y)))
          (v3 (+ (* t x y) (* s z)) (+ (* t y y) c)       (- (* t y z) (* s x)))
          (v3 (- (* t x z) (* s y)) (+ (* t y z) (* s x)) (+ (* t z z) c))))

(define (crane-jib #:height h #:reach reach #:spread spread)
  (define s (/ h 40)) ; beam section
  (define top-z (* 0.12 spread))
  (define pieces
    (list (beam (v3 0 0 (/ spread -2)) (v3 reach h (- top-z)) s)
          (beam (v3 0 0 (/ spread 2)) (v3 reach h top-z) s)
          (beam (v3 reach h (- (* 1.6 top-z))) (v3 reach h (* 1.6 top-z)) s)       ; crosspiece
          (beam (v3 (* -0.5 reach) 0 0) (v3 (* 0.9 reach) (* 0.9 h) 0) (* 0.8 s))  ; back strut
          (beam (v3 0 (* 0.35 h) (/ spread -2.9)) (v3 0 (* 0.35 h) (/ spread 2.9)) (* 0.8 s)))) ; brace
  (make-shape 'crane-jib (apply mesh-append pieces)
              `((height . ,h) (reach . ,reach) (spread . ,spread))))
