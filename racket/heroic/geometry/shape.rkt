#lang racket/base
;; A shape is a generated mesh plus the numbers that describe it. The
;; numbers travel into the .machine file (so the game and later the
;; gear-coupling physics know a gear's pitch radius without re-deriving
;; it) and into the .glb's extras.
(require file/sha1 "mesh.rkt")
(provide (struct-out shape) make-shape shape-prop shape-file-stem wheel-kinds)

;; kind: 'gear | 'pulley | 'drum | 'treadwheel | 'noria | 'screw | 'catapult-frame
;; volume: m³ of material, for mass = density × volume
;; inertia: moments about local X, Y, Z (the axle) per unit density, in
;;   m⁵ — times the material's density gives kg·m²
;; props: (listof (cons symbol (or real symbol)))
(struct shape (kind mesh volume inertia props) #:transparent)

;; Shapes that are a wheel on an axle (as opposed to a screw or a frame).
(define wheel-kinds '(gear pulley drum treadwheel noria disc-wheel cart-wheel))

;; Volume and inertia come from the mesh itself. A shape built from
;; several closed pieces (spokes, rims, buckets) counts each piece in
;; full, so the few cm³ where pieces overlap are counted twice.
(define (make-shape kind m props)
  (define v (mesh-volume m))
  (unless (> v 0) (error 'make-shape "~a mesh encloses no volume (~a); are its faces inside out?" kind v))
  (shape kind m v (mesh-second-moments m) props))

(define (shape-prop s key [default #f])
  (cond [(assq key (shape-props s)) => cdr] [else default]))

;; Same numbers → same file name, so identical parts across machines
;; (and the catalogue) share one mesh file.
(define (shape-file-stem s)
  (define digest (sha1 (open-input-string (format "~s" (cons (shape-kind s) (shape-props s))))))
  (format "~a-~a" (shape-kind s) (substring digest 0 10)))
