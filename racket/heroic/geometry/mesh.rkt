#lang racket/base
;; Triangle meshes for generated part geometry.
;;
;; Convention: every generated shape that turns has its axle along local
;; +Z, centred on the origin. The game decides where the axle points, so
;; no generator ever has to.
;;
;; Winding is never chosen by hand. Every triangle is given with the
;; normals it should have, and builder-tri! orders its corners so the
;; geometric normal agrees — the one mistake that makes a mesh render
;; inside-out can't be made by a generator.
(require racket/math racket/list racket/vector)
(provide (struct-out mesh)
         v3 v+ v- v* vdot vcross vlen vnorm
         make-builder builder-tri! builder-quad! builder->mesh
         mesh-append mesh-transform mesh-translate rot-x rot-y rot-z
         mesh-volume mesh-second-moments mesh-bounds mesh-triangle-count mesh-vertex-count
         box-mesh cylinder-mesh revolve-profile extrude-star)

;; positions, normals: vectors of v3; indices: vector of vertex numbers,
;; three per triangle.
(struct mesh (positions normals indices) #:transparent)

;; ---------------------------------------------------------------------------
;; 3-vectors as (vector x y z) of flonums

(define (v3 x y z) (vector (exact->inexact x) (exact->inexact y) (exact->inexact z)))
(define (vx v) (vector-ref v 0))
(define (vy v) (vector-ref v 1))
(define (vz v) (vector-ref v 2))
(define (v+ a b) (vector (+ (vx a) (vx b)) (+ (vy a) (vy b)) (+ (vz a) (vz b))))
(define (v- a b) (vector (- (vx a) (vx b)) (- (vy a) (vy b)) (- (vz a) (vz b))))
(define (v* a k) (vector (* (vx a) k) (* (vy a) k) (* (vz a) k)))
(define (vdot a b) (+ (* (vx a) (vx b)) (* (vy a) (vy b)) (* (vz a) (vz b))))
(define (vcross a b)
  (vector (- (* (vy a) (vz b)) (* (vz a) (vy b)))
          (- (* (vz a) (vx b)) (* (vx a) (vz b)))
          (- (* (vx a) (vy b)) (* (vy a) (vx b)))))
(define (vlen a) (sqrt (vdot a a)))
(define (vnorm a)
  (define l (vlen a))
  (if (< l 1e-15) (vector 0.0 0.0 1.0) (v* a (/ 1.0 l))))

;; ---------------------------------------------------------------------------
;; Builder

(struct builder ([tris #:mutable])) ; tris: list of (vector p0 p1 p2 n0 n1 n2), newest first

(define (make-builder) (builder '()))

;; Adds one triangle, reordering its corners so its geometric normal
;; points the same way as the given vertex normals. Triangles too small
;; to have a direction (zero area) are dropped: they carry no surface,
;; and a zero-length edge would only confuse the closed-surface checks.
(define (builder-tri! b p0 p1 p2 n0 n1 n2)
  (define g (vcross (v- p1 p0) (v- p2 p0)))
  (define scale (max (vdot (v- p1 p0) (v- p1 p0)) (vdot (v- p2 p0) (v- p2 p0)) 1e-300))
  (unless (< (vdot g g) (* 1e-24 scale scale))
    (define t (if (< (vdot g (v+ n0 (v+ n1 n2))) 0.0)
                  (vector p0 p2 p1 (vnorm n0) (vnorm n2) (vnorm n1))
                  (vector p0 p1 p2 (vnorm n0) (vnorm n1) (vnorm n2))))
    (set-builder-tris! b (cons t (builder-tris b)))))

;; p0..p3 go round the quad in order (either direction).
(define (builder-quad! b p0 p1 p2 p3 n0 n1 n2 n3)
  (builder-tri! b p0 p1 p2 n0 n1 n2)
  (builder-tri! b p0 p2 p3 n0 n2 n3))

;; Corners with the same position and normal share one vertex.
(define (builder->mesh b)
  (define index (make-hash))
  (define positions '())
  (define normals '())
  (define count 0)
  (define (key p n)
    (define (q x) (inexact->exact (round (* x 1e9))))
    (list (q (vx p)) (q (vy p)) (q (vz p)) (q (* (vx n) 1e-3)) (q (* (vy n) 1e-3)) (q (* (vz n) 1e-3))))
  (define (vertex! p n)
    (hash-ref! index (key p n)
               (λ ()
                 (set! positions (cons p positions))
                 (set! normals (cons n normals))
                 (begin0 count (set! count (add1 count))))))
  (define indices
    (for*/list ([t (in-list (reverse (builder-tris b)))]
                [i (in-range 3)])
      (vertex! (vector-ref t i) (vector-ref t (+ i 3)))))
  (mesh (list->vector (reverse positions)) (list->vector (reverse normals)) (list->vector indices)))

;; ---------------------------------------------------------------------------
;; Whole-mesh operations

(define (mesh-vertex-count m) (vector-length (mesh-positions m)))
(define (mesh-triangle-count m) (quotient (vector-length (mesh-indices m)) 3))

(define (mesh-append . ms)
  (define-values (ps ns is _)
    (for/fold ([ps '()] [ns '()] [is '()] [offset 0]) ([m (in-list ms)])
      (values (cons (mesh-positions m) ps)
              (cons (mesh-normals m) ns)
              (cons (for/vector ([i (in-vector (mesh-indices m))]) (+ i offset)) is)
              (+ offset (mesh-vertex-count m)))))
  (mesh (apply vector-append (reverse ps)) (apply vector-append (reverse ns)) (apply vector-append (reverse is))))

;; rot: three row vectors; the mesh is rotated then moved by `offset`.
(define (mat* rot v) (vector (vdot (vector-ref rot 0) v) (vdot (vector-ref rot 1) v) (vdot (vector-ref rot 2) v)))
(define (mesh-transform m rot [offset (v3 0 0 0)])
  (mesh (for/vector ([p (in-vector (mesh-positions m))]) (v+ (mat* rot p) offset))
        (for/vector ([n (in-vector (mesh-normals m))]) (vnorm (mat* rot n)))
        (mesh-indices m)))
(define (mesh-translate m offset) (mesh-transform m (rot-z 0) offset))

(define (rot-x a) (vector (v3 1 0 0) (v3 0 (cos a) (- (sin a))) (v3 0 (sin a) (cos a))))
(define (rot-y a) (vector (v3 (cos a) 0 (sin a)) (v3 0 1 0) (v3 (- (sin a)) 0 (cos a))))
(define (rot-z a) (vector (v3 (cos a) (- (sin a)) 0) (v3 (sin a) (cos a) 0) (v3 0 0 1)))

;; Signed volume by the divergence theorem. Exact for a closed surface
;; whose triangles all face outward; positive iff they do.
(define (mesh-volume m)
  (define ps (mesh-positions m))
  (define is (mesh-indices m))
  (/ (for/sum ([t (in-range 0 (vector-length is) 3)])
       (vdot (vector-ref ps (vector-ref is t))
             (vcross (vector-ref ps (vector-ref is (+ t 1)))
                     (vector-ref ps (vector-ref is (+ t 2))))))
     6.0))

;; Moments of inertia about the X, Y and Z axes through the origin, for a
;; solid of unit density: ∫(y²+z²), ∫(x²+z²), ∫(x²+y²) over the volume.
;; Each triangle and the origin make a tetrahedron; for one with corners
;; 0, a, b, c and signed volume det/6, ∫x² dV = det/60 · (aₓ²+bₓ²+cₓ²
;; + aₓbₓ + aₓcₓ + bₓcₓ). Summed over a closed surface, the signed
;; pieces outside the solid cancel, just as for the volume.
(define (mesh-second-moments m)
  (define ps (mesh-positions m))
  (define is (mesh-indices m))
  (define-values (sxx syy szz)
    (for/fold ([sx 0.0] [sy 0.0] [sz 0.0]) ([t (in-range 0 (vector-length is) 3)])
      (define a (vector-ref ps (vector-ref is t)))
      (define b (vector-ref ps (vector-ref is (+ t 1))))
      (define c (vector-ref ps (vector-ref is (+ t 2))))
      (define det (vdot a (vcross b c)))
      (define (sq k)
        (define-values (u v w) (values (vector-ref a k) (vector-ref b k) (vector-ref c k)))
        (+ (* u u) (* v v) (* w w) (* u v) (* u w) (* v w)))
      (values (+ sx (* det (sq 0))) (+ sy (* det (sq 1))) (+ sz (* det (sq 2))))))
  (define-values (x2 y2 z2) (values (/ sxx 60) (/ syy 60) (/ szz 60)))
  (vector (+ y2 z2) (+ x2 z2) (+ x2 y2)))

;; → (values min-v3 max-v3)
(define (mesh-bounds m)
  (define ps (mesh-positions m))
  (for/fold ([lo (v3 +inf.0 +inf.0 +inf.0)] [hi (v3 -inf.0 -inf.0 -inf.0)]) ([p (in-vector ps)])
    (values (vector (min (vx lo) (vx p)) (min (vy lo) (vy p)) (min (vz lo) (vz p)))
            (vector (max (vx hi) (vx p)) (max (vy hi) (vy p)) (max (vz hi) (vz p))))))

;; ---------------------------------------------------------------------------
;; Primitives

(define (box-mesh sx sy sz)
  (define b (make-builder))
  (define hx (/ sx 2.0)) (define hy (/ sy 2.0)) (define hz (/ sz 2.0))
  (define (face n u v)
    ;; centre n·h, spanned by ±u and ±v
    (define c (vector (* (vx n) hx) (* (vy n) hy) (* (vz n) hz)))
    (define (corner su sv) (v+ c (v+ (v* u su) (v* v sv))))
    (builder-quad! b (corner -1 -1) (corner 1 -1) (corner 1 1) (corner -1 1) n n n n))
  (face (v3 1 0 0) (v3 0 hy 0) (v3 0 0 hz))
  (face (v3 -1 0 0) (v3 0 hy 0) (v3 0 0 hz))
  (face (v3 0 1 0) (v3 hx 0 0) (v3 0 0 hz))
  (face (v3 0 -1 0) (v3 hx 0 0) (v3 0 0 hz))
  (face (v3 0 0 1) (v3 hx 0 0) (v3 0 hy 0))
  (face (v3 0 0 -1) (v3 hx 0 0) (v3 0 hy 0))
  (builder->mesh b))

;; A solid cylinder along Z, centred on the origin.
(define (cylinder-mesh radius len #:segments [n 32])
  (define b (make-builder))
  (define h (/ len 2.0))
  (for ([k (in-range n)])
    (define a0 (/ (* 2 pi k) n))
    (define a1 (/ (* 2 pi (add1 k)) n))
    (define (rim a z) (v3 (* radius (cos a)) (* radius (sin a)) z))
    (define (out a) (v3 (cos a) (sin a) 0))
    (builder-quad! b (rim a0 (- h)) (rim a1 (- h)) (rim a1 h) (rim a0 h)
                   (out a0) (out a1) (out a1) (out a0))
    (for ([z (list h (- h))] [s (list 1 -1)])
      (define up (v3 0 0 s))
      (builder-tri! b (v3 0 0 z) (rim a0 z) (rim a1 z) up up up)))
  (builder->mesh b))

;; Where two adjoining segments meet at less than smooth-deg, their
;; shared corner gets one averaged normal (a smooth curve); at a sharper
;; corner each side keeps its own (a crisp edge).
(define (corner-normals edge-normals smooth-deg closed?)
  (define n (vector-length edge-normals))
  (define limit (cos (degrees->radians smooth-deg)))
  ;; → vector of (cons normal-at-start normal-at-end) per edge
  (for/vector ([i (in-range n)])
    (define here (vector-ref edge-normals i))
    (define (blend j)
      (cond [(or (< j 0) (>= j n)) (if closed? (blend (modulo j n)) here)]
            [else (define other (vector-ref edge-normals j))
                  (if (>= (vdot here other) limit) (vnorm (v+ here other)) here)]))
    (cons (blend (sub1 i)) (blend (add1 i)))))

;; Revolves a closed profile of (r . z) points around the Z axis.
(define (revolve-profile profile #:segments [n 48] #:smooth-deg [smooth-deg 35])
  (define pts (list->vector profile))
  (define count (vector-length pts))
  ;; Orientation of the profile in the (r, z) plane decides which side is out.
  (define area2 (for/sum ([i (in-range count)])
                  (define p (vector-ref pts i))
                  (define q (vector-ref pts (modulo (add1 i) count)))
                  (- (* (car p) (cdr q)) (* (car q) (cdr p)))))
  (define sign (if (>= area2 0) 1.0 -1.0))
  (define edge-normals ; 2D outward normals stored as (v3 nr nz 0)
    (for/vector ([i (in-range count)])
      (define p (vector-ref pts i))
      (define q (vector-ref pts (modulo (add1 i) count)))
      (vnorm (v3 (* sign (- (cdr q) (cdr p))) (* sign (- (car p) (car q))) 0))))
  (define corners (corner-normals edge-normals smooth-deg #t))
  (define b (make-builder))
  (for* ([k (in-range n)] [i (in-range count)])
    (define a0 (/ (* 2 pi k) n))
    (define a1 (/ (* 2 pi (add1 k)) n))
    (define p (vector-ref pts i))
    (define q (vector-ref pts (modulo (add1 i) count)))
    (define (at rz a) (v3 (* (car rz) (cos a)) (* (car rz) (sin a)) (cdr rz)))
    (define (nrm n2 a) (v3 (* (vx n2) (cos a)) (* (vx n2) (sin a)) (vy n2)))
    (define-values (np nq) (values (car (vector-ref corners i)) (cdr (vector-ref corners i))))
    (builder-quad! b (at p a0) (at q a0) (at q a1) (at p a1)
                   (nrm np a0) (nrm nq a0) (nrm nq a1) (nrm np a1)))
  (builder->mesh b))

;; Extrudes a star-shaped outline (every ray from the axis crosses it
;; once, as a gear's does) along Z by `thickness`, with a round bore of
;; `bore` radius through the middle. outline: (x . y) points going
;; counter-clockwise with non-decreasing polar angle.
;;
;; Because the outline is star-shaped, each face can be stitched to the
;; bore circle in angle order ("zipping" the two rings together) — no
;; general polygon triangulation needed. The bore ring has one point
;; midway (in angle) between each pair of neighbouring outline points.
;; Not at the outline's own angles: where a tooth flank runs straight
;; along a ray (below a gear's base circle) that would make collinear,
;; zero-area triangles and leave a crack. And not a coarser even ring:
;; with teeth narrower than its spacing, one bore point can end up
;; ahead of a whole tooth, and no valid triangle reaches back to it.
(define (extrude-star outline bore thickness #:smooth-deg [smooth-deg 35])
  (unless (> bore 0) (raise-argument-error 'extrude-star "positive bore" bore))
  (define pts (list->vector (dedupe outline)))
  (define n (vector-length pts))
  (define h (/ thickness 2.0))
  (define (xyz p z) (v3 (car p) (cdr p) z))
  ;; outline angles, unwrapped so they only increase
  (define theta0 (atan (cdr (vector-ref pts 0)) (car (vector-ref pts 0))))
  (define thetas
    (for/fold ([acc '()] #:result (list->vector (reverse acc))) ([p (in-vector pts)])
      (define a (atan (cdr p) (car p)))
      (define prev (if (null? acc) theta0 (car acc)))
      (cons (+ a (* 2 pi (ceiling (/ (- prev a 1e-9) (* 2 pi))))) acc)))
  (define (next-angle i) (if (= (add1 i) n) (+ theta0 (* 2 pi)) (vector-ref thetas (add1 i))))
  (define mids ; midway after each outline point, skipping zero-width gaps (radial flanks)
    (for/list ([i (in-range n)] #:when (> (next-angle i) (+ (vector-ref thetas i) 1e-12)))
      (/ (+ (vector-ref thetas i) (next-angle i)) 2)))
  ;; the one after the last outline point wraps round to just before the first
  (define bore-angles (list->vector (cons (- (last mids) (* 2 pi)) (drop-right mids 1))))
  (define nb (vector-length bore-angles))
  (define (bore-angle j) (if (= j nb) (+ (vector-ref bore-angles 0) (* 2 pi)) (vector-ref bore-angles j)))
  (define (bore-pt j) (let ([a (bore-angle j)]) (cons (* bore (cos a)) (* bore (sin a)))))
  (define edge-normals
    (for/vector ([i (in-range n)])
      (define p (vector-ref pts i))
      (define q (vector-ref pts (modulo (add1 i) n)))
      (vnorm (v3 (- (cdr q) (cdr p)) (- (car p) (car q)) 0))))
  (define corners (corner-normals edge-normals smooth-deg #t))
  (define b (make-builder))
  ;; faces: zip outline points 0..n (n = the first again, a turn later)
  ;; with bore points 0..nb (nb = the first again) in angle order
  (define (o-angle i) (if (= i n) (+ theta0 (* 2 pi)) (vector-ref thetas i)))
  (define (o-pt i) (vector-ref pts (modulo i n)))
  ;; A stitch triangle must go counter-clockwise (seen from +Z) like the
  ;; outline itself; one that doesn't is folded over its neighbours. Angle
  ;; order is right almost everywhere, but where a flank rises straight
  ;; out along a ray it would hang that edge on a bore point behind it,
  ;; so there the other advance is taken instead.
  (define (ccw? p q r)
    (> (- (* (- (car q) (car p)) (- (cdr r) (cdr p))) (* (- (cdr q) (cdr p)) (- (car r) (car p)))) 0))
  (let zip ([i 0] [j 0])
    (unless (and (= i n) (= j nb))
      (define by-outline (and (< i n) (list (o-pt i) (o-pt (add1 i)) (bore-pt j))))
      (define by-bore (and (< j nb) (list (o-pt i) (bore-pt (add1 j)) (bore-pt j))))
      (define ok-outline (and by-outline (apply ccw? by-outline)))
      (define ok-bore (and by-bore (apply ccw? by-bore)))
      (define prefer-outline (and by-outline (or (not by-bore) (<= (o-angle (add1 i)) (bore-angle (add1 j))))))
      (define outline? (cond [(and prefer-outline ok-outline) #t]
                             [(and (not prefer-outline) ok-bore) #f]
                             [ok-outline #t]
                             [ok-bore #f]
                             [else (error 'extrude-star "can't stitch the face at outline point ~a, bore point ~a" i j)]))
      (define-values (p q r) (apply values (if outline? by-outline by-bore)))
      (for ([z (list h (- h))] [nz (list (v3 0 0 1) (v3 0 0 -1))])
        (builder-tri! b (xyz p z) (xyz q z) (xyz r z) nz nz nz))
      (if outline? (zip (add1 i) j) (zip i (add1 j)))))
  (for ([i (in-range n)])
    ;; outer wall
    (define o0 (o-pt i)) (define o1 (o-pt (add1 i)))
    (define-values (n0 n1) (values (car (vector-ref corners i)) (cdr (vector-ref corners i))))
    (builder-quad! b (xyz o0 (- h)) (xyz o1 (- h)) (xyz o1 h) (xyz o0 h) n0 n1 n1 n0))
  (for ([j (in-range nb)])
    ;; bore wall, facing the axis
    (define b0 (bore-pt j)) (define b1 (bore-pt (add1 j)))
    (define (inward p) (vnorm (v3 (- (car p)) (- (cdr p)) 0)))
    (builder-quad! b (xyz b0 (- h)) (xyz b1 (- h)) (xyz b1 h) (xyz b0 h)
                   (inward b0) (inward b1) (inward b1) (inward b0)))
  (builder->mesh b))

;; Drops consecutive duplicate points (including last-vs-first).
(define (dedupe pts)
  (define (same? p q) (and (< (abs (- (car p) (car q))) 1e-12) (< (abs (- (cdr p) (cdr q))) 1e-12)))
  (define kept
    (for/fold ([acc '()] #:result (reverse acc)) ([p (in-list pts)])
      (if (and (pair? acc) (same? p (car acc))) acc (cons p acc))))
  (if (and (> (length kept) 1) (same? (car kept) (last kept))) (drop-right kept 1) kept))
