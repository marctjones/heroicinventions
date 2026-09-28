#lang racket/base
;; Generated geometry: every mesh is a closed solid facing outward, the
;; gear formulas are the textbook ones, gears placed by mate-angle
;; really mesh, the historical proportions are Vitruvius's, and the .glb
;; files are well-formed glTF.
(require rackunit racket/math racket/list json
         heroic/geometry heroic/geometry/mesh heroic/units heroic/machine heroic/emit)

;; ---------------------------------------------------------------------------
;; Closed, outward-facing solids

;; After welding corners at the same position, a closed surface uses
;; every edge equally often in each direction; a single flipped triangle
;; or a hole breaks that balance. (Composite shapes are unions of closed
;; pieces, which balance too.)
(define (balanced-edges? m)
  (define ps (mesh-positions m))
  (define is (mesh-indices m))
  (define ids (make-hash))
  (define (id i)
    (define p (vector-ref ps i))
    (hash-ref! ids (for/list ([k 3]) (inexact->exact (round (* (vector-ref p k) 1e9))))
               (λ () (hash-count ids))))
  (define edges (make-hash))
  (for ([t (in-range 0 (vector-length is) 3)])
    (define a (id (vector-ref is t)))
    (define b (id (vector-ref is (+ t 1))))
    (define c (id (vector-ref is (+ t 2))))
    (for ([e (list (cons a b) (cons b c) (cons c a))])
      (hash-update! edges e add1 0)))
  (for/and ([(e n) edges])
    (= n (hash-ref edges (cons (cdr e) (car e)) 0))))

(test-case "every catalogue shape is a closed solid with outward faces"
  (for ([e catalogue])
    (define s (catalogue-entry-shape e))
    (check-true (balanced-edges? (shape-mesh s)) (format "~a has holes or flipped faces" (catalogue-entry-id e)))
    (check-true (> (mesh-volume (shape-mesh s)) 0) (format "~a is inside out" (catalogue-entry-id e)))))

(test-case "a gear's volume is its plate area times its width"
  ;; tooth area ≈ pitch circle area (teeth above it fill gaps below it)
  (define s (spur-gear #:teeth 40 #:module (mm 5) #:width (cm 3) #:bore (cm 1)))
  (define rp (pitch-radius 40 (mm 5)))
  (define expected (* pi (- (* rp rp) (* 0.01 0.01)) 0.03))
  (check-= (shape-volume s) expected (* 0.05 expected)))

(test-case "moments of inertia match the textbook solids"
  ;; box a×b×c: Izz = (a²+b²)/12 · volume
  (define box (box-mesh 0.3 0.2 0.1))
  (define v (* 0.3 0.2 0.1))
  (define I (mesh-second-moments box))
  (check-= (vector-ref I 2) (* v (/ (+ 0.09 0.04) 12)) 1e-12)
  (check-= (vector-ref I 0) (* v (/ (+ 0.04 0.01) 12)) 1e-12)
  ;; solid cylinder about its axis: ½·r²·volume (a 128-gon is ~0.1% short of a circle)
  (define cyl (cylinder-mesh 0.5 0.2 #:segments 128))
  (check-= (vector-ref (mesh-second-moments cyl) 2) (* 0.5 0.25 (* pi 0.25 0.2)) 2e-4))

(test-case "a treadwheel's mass sits out at its rim, a gear's is spread through it"
  ;; I / (volume · r²): ½ for a uniform disc, near 1 for a thin ring
  (define (shape-ratio s r) (/ (vector-ref (shape-inertia s) 2) (* (shape-volume s) r r)))
  (check-true (> (shape-ratio (treadwheel #:radius 2 #:width 1) 2) 0.6))
  (check-= (shape-ratio (spur-gear #:teeth 60 #:module 0.01 #:width 0.02) 0.3) 0.5 0.05))

;; ---------------------------------------------------------------------------
;; Gear formulas

(test-case "involute gear radii are the textbook ones"
  (define z 30) (define m (mm 4))
  (check-= (pitch-radius z m) 0.06 1e-12)
  (check-= (base-radius z m) (* 0.06 (cos (degrees->radians 20))) 1e-12)
  (check-= (tip-radius z m) 0.064 1e-12)
  (check-= (root-radius z m) 0.055 1e-12)
  (check-= (center-distance 20 37 (mm 10)) 0.285 1e-12))

(define (polar-angle p) (atan (cdr p) (car p)))
(define (radius p) (sqrt (+ (sqr (car p)) (sqr (cdr p)))))

;; Where the outline crosses radius r, in order along the outline.
(define (crossings outline r)
  (define pts (list->vector outline))
  (define n (vector-length pts))
  (for*/list ([i (in-range n)]
              [p (in-value (vector-ref pts i))]
              [q (in-value (vector-ref pts (modulo (add1 i) n)))]
              #:when (not (eq? (< (radius p) r) (< (radius q) r))))
    (define t (/ (- r (radius p)) (- (radius q) (radius p))))
    (polar-angle (cons (+ (car p) (* t (- (car q) (car p)))) (+ (cdr p) (* t (- (cdr q) (cdr p))))))))

(test-case "an involute tooth is half a circular pitch thick at the pitch circle, less backlash"
  (define z 24) (define m (mm 10)) (define backlash (mm 0.4))
  (define outline (gear-outline z m #:backlash backlash #:flank-samples 40))
  (define rp (pitch-radius z m))
  ;; the first tooth sits on the +X axis: its two flank crossings bracket 0
  (define angles (crossings outline rp))
  (define thickness (* rp (- (second angles) (first angles))))
  (check-= thickness (- (* pi m 1/2) backlash) 1e-6))

(test-case "the Antikythera lunar train gives the Moon's 254 sidereal months in 19 years"
  (check-equal? (* 64/38 48/24 127/32) 254/19))

;; ---------------------------------------------------------------------------
;; Meshing: gear B at the centre distance to the right of gear A, turned
;; by mate-angle, shares no area with A — and stays that way as they
;; roll, which is what an involute pair promises and what a wrongly
;; phased pair can't do.

(define (place outline center angle)
  (for/list ([p outline])
    (define-values (c s) (values (cos angle) (sin angle)))
    (cons (+ (car center) (- (* c (car p)) (* s (cdr p))))
          (+ (cdr center) (+ (* s (car p)) (* c (cdr p)))))))

(define (inside? p poly)
  (define pts (list->vector poly))
  (define n (vector-length pts))
  (for/fold ([in #f]) ([i (in-range n)])
    (define a (vector-ref pts i))
    (define b (vector-ref pts (modulo (add1 i) n)))
    (if (and (not (eq? (> (cdr a) (cdr p)) (> (cdr b) (cdr p))))
             (< (car p) (+ (car a) (/ (* (- (car b) (car a)) (- (cdr p) (cdr a))) (- (cdr b) (cdr a))))))
        (not in)
        in)))

(define (segment-distance p a b)
  (define-values (dx dy) (values (- (car b) (car a)) (- (cdr b) (cdr a))))
  (define len2 (+ (* dx dx) (* dy dy)))
  (define t (if (zero? len2) 0 (max 0 (min 1 (/ (+ (* (- (car p) (car a)) dx) (* (- (cdr p) (cdr a)) dy)) len2)))))
  (sqrt (+ (sqr (- (car p) (+ (car a) (* t dx)))) (sqr (- (cdr p) (+ (cdr a) (* t dy)))))))

;; → (values points-of-A-inside-B smallest-gap-between-them)
(define (contact za zb m profile angle-a #:extra-b [extra 0])
  (define a (center-distance za zb m))
  (define oa (place (gear-outline za m #:profile profile #:backlash (* 0.04 m) #:flank-samples 60)
                    (cons 0 0) angle-a))
  (define ob (place (gear-outline zb m #:profile profile #:backlash (* 0.04 m) #:flank-samples 60)
                    (cons a 0) (+ extra (mate-angle za angle-a zb 0))))
  (define rb-tip (tip-radius zb m profile))
  (define near-a (filter (λ (p) (< (sqrt (+ (sqr (- (car p) a)) (sqr (cdr p)))) (* 1.01 rb-tip))) oa))
  (define near-b (filter (λ (p) (< (radius p) (* 1.05 (tip-radius za m profile)))) ob))
  (define segs (for/list ([p near-b] [q (append (cdr near-b) (list (car near-b)))]
                          #:when (< (sqrt (+ (sqr (- (car p) (car q))) (sqr (- (cdr p) (cdr q))))) (* 2 m)))
                 (cons p q)))
  (values (for/sum ([p near-a]) (if (inside? p ob) 1 0))
          (for*/fold ([best +inf.0]) ([p near-a] [s segs])
            (min best (segment-distance p (car s) (cdr s))))))

(test-case "involute gears placed by mate-angle mesh and keep meshing as they roll"
  (define m (mm 10))
  (define step (/ (* 2 pi) 20 12)) ; a twelfth of a tooth
  ;; zero overlap at every position through two whole teeth, with a
  ;; clearance that stays the same — the constant-ratio property itself.
  ;; Each gear's teeth are thinned by the backlash, so a tooth centred in
  ;; its partner's gap has one backlash of play each side, which is
  ;; backlash·cos α measured square to the flanks.
  (define gaps
    (for/list ([k (in-range 25)])
      (define-values (overlap gap) (contact 20 37 m 'involute (* k step)))
      (check-equal? overlap 0 (format "teeth overlap at step ~a" k))
      gap))
  (define expected (* 0.04 m (cos (degrees->radians 20))))
  (for ([g gaps]) (check-= g expected (* 0.35 expected))))

(test-case "the same gears turned a quarter tooth out of phase collide"
  (define-values (overlap gap) (contact 20 37 (mm 10) 'involute 0 #:extra-b (/ pi 2 37)))
  (check-true (> overlap 0)))

(test-case "Antikythera-style triangular gears placed by mate-angle clear each other"
  (define-values (overlap gap) (contact 64 38 (mm 0.5) 'triangular 0))
  (check-equal? overlap 0))

(test-case "gears too small to cut are refused"
  (check-exn #rx"at least 6" (λ () (spur-gear #:teeth 4 #:module 0.01 #:width 0.01))))

;; ---------------------------------------------------------------------------
;; Vitruvius

(test-case "the Vitruvian screw follows De Architectura X.6"
  (define s (vitruvian-screw #:length 4))
  (check-= (* 2 (shape-prop s 'radius)) 0.5 1e-12)          ; whole thickness = length/8
  (check-= (* 2 (shape-prop s 'core-radius)) 0.25 1e-12)    ; core = length/16 (digits = feet)
  (check-= (shape-prop s 'pitch) (* pi 0.25) 1e-12)         ; one turn per core circumference
  (check-equal? (shape-prop s 'starts) 8)
  (check-= (sin (degrees->radians vitruvian-screw-incline-deg)) 3/5 1e-12)) ; 3-4-5 slope

(test-case "the catapulta scales from a hole one ninth of the bolt"
  (define bolt (* 3 greek-span))
  (define hole (catapulta-hole bolt))
  (check-= hole (/ bolt 9) 1e-12)
  (define d (catapulta-dimensions bolt))
  (check-= (hash-ref d 'arm-length) (* 7 hole) 1e-12)
  (check-= (hash-ref d 'channel-length) (* 19 hole) 1e-12)
  (check-= (hash-ref d 'column-height) (* 8 hole) 1e-12)
  ;; every row says where its number comes from
  (for ([row catapulta-proportions])
    (check-not-false (memq (third row) '(vitruvius emended reconstructed)))))

(test-case "Philo's stone-thrower rule: a one-talent (60 minae) shot needs a ~20-digit hole"
  (check-= (ballista-hole-digits 60) 20.0 0.05))

;; ---------------------------------------------------------------------------
;; glTF

(test-case "a .glb is a valid glTF 2.0 binary whose positions round-trip"
  (define s (pulley #:radius 0.1 #:width 0.05))
  (define bs (mesh->glb-bytes (shape-mesh s) #:name "pulley" #:extras (hasheq 'radius 0.1)))
  (define (u32 at) (integer-bytes->integer bs #f #f at (+ at 4)))
  (check-equal? (subbytes bs 0 4) #"glTF")
  (check-equal? (u32 4) 2)
  (check-equal? (u32 8) (bytes-length bs))
  (define json-len (u32 12))
  (check-equal? (subbytes bs 16 20) #"JSON")
  (define js (bytes->jsexpr (subbytes bs 20 (+ 20 json-len))))
  (define bin-at (+ 20 json-len 8))
  (check-equal? (subbytes bs (+ 20 json-len 4) bin-at) #"BIN\0")
  (define accessors (hash-ref js 'accessors))
  (define n (hash-ref (first accessors) 'count))
  (check-equal? n (mesh-vertex-count (shape-mesh s)))
  (check-equal? (hash-ref (third accessors) 'count) (* 3 (mesh-triangle-count (shape-mesh s))))
  (check-equal? (hash-ref (hash-ref (first (hash-ref js 'nodes)) 'extras) 'radius) 0.1)
  ;; first vertex, read back out of the binary buffer
  (define (f32 at) (floating-point-bytes->real bs #f at (+ at 4)))
  (define p0 (vector-ref (mesh-positions (shape-mesh s)) 0))
  (for ([k 3]) (check-= (f32 (+ bin-at (* 4 k))) (vector-ref p0 k) 1e-6))
  ;; every index is in range
  (define idx-at (+ bin-at (* 24 n)))
  (for ([i (in-range (hash-ref (third accessors) 'count))])
    (check-true (< (u32 (+ idx-at (* 4 i))) n))))

;; ---------------------------------------------------------------------------
;; In machines

;; The machine registry and the shape struct must be the same instances
;; this test module uses, or machines registered inside the evaluated
;; module would be invisible here and its shapes unrecognisable.
(define-namespace-anchor anchor)
(define (expand-machine . clauses)
  (define ns (make-base-namespace))
  (for ([mod '(heroic/machine heroic/geometry/shape)])
    (namespace-attach-module (namespace-anchor->namespace anchor) mod ns))
  (parameterize ([current-namespace ns])
    (eval `(module test-machine heroic (define-machine test ,@clauses)))
    (eval '(require 'test-machine)))
  (take-registered-machines))

(test-case "a wheel carries its generated shape into the .machine file"
  (define m (car (expand-machine
                  '(wheel g #:shape (spur-gear #:teeth 30 #:module (mm 5) #:width (cm 2))
                          #:at (0 1 0) #:material bronze #:axis x #:drive-rpm 6))))
  (define props (cdr (assq 'props (cdddr (car (cddr (machine->sexp m)))))))
  (check-equal? (assq 'shape props) '(shape gear))
  (check-true (string? (cadr (assq 'mesh props))))
  (check-equal? (assq 'teeth props) '(teeth 30.0))
  (check-equal? (assq 'axis props) '(axis x))
  (check-equal? (assq 'drive-rpm props) '(drive-rpm 6.0)))

(test-case "a wheel won't take a screw's shape"
  (check-exn #rx"use the screw clause"
             (λ () (expand-machine '(wheel s #:shape (vitruvian-screw #:length 2) #:at (0 0 0) #:material pine)))))

(test-case "a wheel's axis must be x, y or z"
  (check-exn exn:fail:syntax?
             (λ () (expand-machine '(wheel g #:shape (pulley #:radius 0.1 #:width 0.05)
                                           #:at (0 0 0) #:material oak #:axis w)))))
