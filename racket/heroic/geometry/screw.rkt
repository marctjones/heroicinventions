#lang racket/base
;; The Archimedes' screw (Vitruvius's cochlea, De Architectura X.6).
;;
;; A core beam with helical blades wound round it; set on a slope with
;; its lower end in water and turned, each turn carries a pocket of
;; water one pitch further up. Axle along Z, centred on the origin.
(require racket/math "mesh.rkt" "shape.rkt")
(provide archimedes-screw vitruvian-screw vitruvian-screw-incline-deg)

;; Vitruvius sets the screw so its rise is 3 for a length of 5 — "the
;; right-angled triangle of Pythagoras", a 3-4-5 triangle.
(define vitruvian-screw-incline-deg (radians->degrees (asin 3/5))) ; ≈ 36.87°

;; Vitruvius's proportions, from nothing but the length:
;;  - the core beam is as many digits thick as it is feet long, and a
;;    Roman foot is 16 digits, so its diameter is length/16;
;;  - its circumference is divided into eight, and eight helical strips
;;    are laid on, each advancing one eighth of the circumference for
;;    every eighth of the circumference along the beam — so one full
;;    turn per circumference of length (the helix crosses the core's
;;    surface at 45°);
;;  - strips are built up "till the thickness of the whole be equal to
;;    one eighth part of the length": outer diameter = length/8.
;; He then planks it over and binds it with iron hoops; the casing is
;; left off here so the helix stays visible.
(define (vitruvian-screw #:length len)
  (define core-d (/ len 16))
  (archimedes-screw #:length len #:radius (/ len 16) #:core-radius (/ core-d 2)
                    #:pitch (* pi core-d) #:starts 8))

(define (archimedes-screw #:length len #:radius outer #:core-radius core
                          #:pitch pitch #:starts [starts 1]
                          #:blade-thickness [blade #f]
                          #:segments-per-turn [per-turn 32])
  (unless (< core outer) (error 'archimedes-screw "core radius ~a must be less than the radius ~a" core outer))
  (define thick (or blade (/ pitch starts 5)))
  (define half (/ len 2))
  (define inner (* 0.97 core)) ; blades root a little inside the core, so no seam shows
  (define k (/ (* 2 pi) pitch))
  (define steps (max 8 (inexact->exact (ceiling (* per-turn (/ len pitch))))))
  (define radii (list inner outer)) ; a helicoid is straight along its radius
  (define blades
    (for/list ([b (in-range starts)])
      (define phase (/ (* 2 pi b) starts))
      (define (theta s) (+ phase (* k s)))
      (define (pt r s dz) (v3 (* r (cos (theta s))) (* r (sin (theta s))) (+ s dz)))
      (define (sheet-normal r s up?) ; ∂P/∂r × ∂P/∂s = (sin θ, −cos θ, r·k)
        (define n (v3 (sin (theta s)) (- (cos (theta s))) (* r k)))
        (if up? n (v* n -1)))
      (define (radial s sign) (v3 (* sign (cos (theta s))) (* sign (sin (theta s))) 0))
      (define bld (make-builder))
      (for ([j (in-range steps)])
        (define s0 (+ (- half) (* len (/ j steps))))
        (define s1 (+ (- half) (* len (/ (add1 j) steps))))
        ;; upper and lower faces of the blade
        (for ([dz (list (/ thick 2) (- (/ thick 2)))] [up? '(#t #f)])
          (for ([r0 radii] [r1 (cdr radii)])
            (builder-quad! bld (pt r0 s0 dz) (pt r1 s0 dz) (pt r1 s1 dz) (pt r0 s1 dz)
                           (sheet-normal r0 s0 up?) (sheet-normal r1 s0 up?)
                           (sheet-normal r1 s1 up?) (sheet-normal r0 s1 up?))))
        ;; outer and inner edges
        (for ([r (list outer inner)] [sign '(1 -1)])
          (define h (/ thick 2))
          (builder-quad! bld (pt r s0 (- h)) (pt r s1 (- h)) (pt r s1 h) (pt r s0 h)
                         (radial s0 sign) (radial s1 sign) (radial s1 sign) (radial s0 sign))))
      ;; the two cut ends of the blade
      (for ([s (list half (- half))] [sign '(1 -1)])
        (define h (/ thick 2))
        (define n (v3 (* sign (- (sin (theta s)))) (* sign (cos (theta s))) 0))
        (for ([r0 radii] [r1 (cdr radii)])
          (builder-quad! bld (pt r0 s (- h)) (pt r1 s (- h)) (pt r1 s h) (pt r0 s h) n n n n)))
      (builder->mesh bld)))
  (define core-mesh (cylinder-mesh core len #:segments 32))
  ;; stub axles the screw turns on, a tenth of its length each end
  (define stubs (for/list ([side '(1 -1)])
                  (mesh-translate (cylinder-mesh (* 0.35 core) (* 0.1 len) #:segments 16)
                                  (v3 0 0 (* side (+ half (* 0.05 len)))))))
  (define parts (append (list core-mesh) stubs blades))
  (make-shape 'screw (apply mesh-append parts)
              `((length . ,len) (radius . ,outer) (core-radius . ,core) (pitch . ,pitch) (starts . ,starts))))
