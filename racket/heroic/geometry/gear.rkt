#lang racket/base
;; Spur gears from tooth count and module (the module m is the pitch
;; diameter per tooth, so tooth size: pitch radius = m·z/2).
;;
;; Two tooth profiles:
;;
;;  'involute   — the flank is the curve traced by the end of a string
;;                unwound from the base circle. Two involute gears turn
;;                at an exactly constant speed ratio at any centre
;;                distance. Worked out in the 1700s (Camus, Euler); the
;;                right choice for any machine that needs a smooth ratio.
;;  'triangular — straight-sided teeth, what the Antikythera mechanism's
;;                bronze gears actually have (c. 100 BC; Freeth et al.,
;;                Nature 2006). They mesh, but the ratio wobbles slightly
;;                within each tooth — an ancient gear train is a little
;;                jerky, and this reproduces that honestly.
;;
;; Every gear is centred on the origin with its axle along Z, a tooth
;; centred on the +X axis.
(require racket/math racket/list "mesh.rkt" "shape.rkt")
(provide spur-gear gear-outline
         pitch-radius base-radius tip-radius root-radius center-distance
         mate-angle inv)

(define (inv a) (- (tan a) a)) ; the involute function

(define (pitch-radius z m) (/ (* m z) 2))
(define (base-radius z m [alpha (degrees->radians 20)]) (* (pitch-radius z m) (cos alpha)))
(define (tip-radius z m [profile 'involute])
  (+ (pitch-radius z m) (if (eq? profile 'triangular) (* 0.45 (tri-height m)) m)))
(define (root-radius z m [profile 'involute])
  (- (pitch-radius z m) (if (eq? profile 'triangular) (* 0.55 (tri-height m)) (* 1.25 m))))
(define (center-distance z1 z2 m) (/ (* m (+ z1 z2)) 2))

;; Height of a straight-sided tooth whose flanks, if carried to a point,
;; make an equilateral triangle on one circular pitch (π·m).
(define (tri-height m) (* (sqrt 3) 1/2 pi m))

;; Where gear B must be turned so it meshes with gear A. A sits turned
;; `angle-a` (radians) about its axle; B's centre lies in direction
;; `line` (radians) from A's. Rolling without slip at the pitch point
;; means arc lengths along the two pitch circles match (and run in
;; opposite senses), and a tooth of one must face a gap of the other:
;;   θB = line + π + (π − zA·(θA − line)) / zB
(define (mate-angle za angle-a zb line)
  (+ line pi (/ (- pi (* za (- angle-a line))) zb)))

(define (polar r a) (cons (* r (cos a)) (* r (sin a))))

(define (samples lo hi n) ; n+1 evenly spaced values, both ends included
  (for/list ([i (in-range (add1 n))]) (+ lo (* (- hi lo) (/ i n)))))

;; The whole outline as (x . y) points, counter-clockwise.
(define (gear-outline z m #:profile [profile 'involute]
                      #:pressure-angle-deg [pa-deg 20] #:backlash [backlash 0]
                      #:flank-samples [nf 6])
  (case profile
    [(involute) (involute-outline z m (degrees->radians pa-deg) backlash nf)]
    [(triangular) (triangular-outline z m)] ; straight flanks: their two ends are exact
    [else (raise-argument-error 'gear-outline "'involute or 'triangular" profile)]))

(define (involute-outline z m alpha backlash nf)
  (define rp (pitch-radius z m))
  (define rb (base-radius z m alpha))
  (define ra (tip-radius z m))
  (define rf (root-radius z m))
  (when (<= rf 0) (error 'spur-gear "~a teeth is too few: the roots would pass the axle" z))
  (define pitch-angle (/ (* 2 pi) z))
  ;; Half the tooth's angular thickness at the pitch circle: half of half
  ;; a circular pitch, less half the backlash.
  (define psi-p (- (/ pi (* 2 z)) (/ backlash (* 2 rp))))
  (define offset (+ psi-p (inv alpha)))
  (define (alpha-at r) (acos (min 1.0 (/ rb r))))
  (define (flank-angle r) (- (inv (alpha-at r)) offset)) ; the tooth's clockwise flank
  (define r-start (max rb rf))
  (define tip-half (- (flank-angle ra)))
  (when (<= tip-half 0)
    (error 'spur-gear "~a-tooth involute teeth come to a point before the tip circle" z))
  (define root-gap (- pitch-angle (* 2 (- (flank-angle r-start)))))
  (when (<= root-gap 0)
    (error 'spur-gear "~a-tooth involute teeth overlap at the root" z))
  (define rs (samples r-start ra nf))
  (append*
   (for/list ([k (in-range z)])
     (define c (* k pitch-angle))
     (define (at r a) (polar r (+ c a)))
     (append
      (if (< rf rb) (list (at rf (flank-angle rb))) '())
      (for/list ([r rs]) (at r (flank-angle r)))                       ; up one flank
      (for/list ([a (cdr (drop-right (samples (- tip-half) tip-half 4) 1))]) (at ra a)) ; across the tip
      (for/list ([r (reverse rs)]) (at r (- (flank-angle r))))          ; down the other
      (if (< rf rb) (list (at rf (- (flank-angle rb)))) '())
      (let ([a0 (- (flank-angle r-start))])                            ; along the root
        (for/list ([a (cdr (drop-right (samples a0 (- pitch-angle a0) 4) 1))]) (at rf a)))))))

(define (triangular-outline z m)
  (define rp (pitch-radius z m))
  (define ra (tip-radius z m 'triangular))
  (define rf (root-radius z m 'triangular))
  (when (<= rf 0) (error 'spur-gear "~a teeth is too few: the roots would pass the axle" z))
  (define half-pitch (/ pi z))
  ;; The tooth's base takes 85% of a pitch at the root, leaving a small
  ;; flat between teeth; its tip is a narrow flat, not a knife edge.
  ;; Tooth thickness at the pitch circle then comes to ~43% of a pitch:
  ;; the rest is gap, so teeth of two such gears clear each other.
  (define base-half (* 0.85 half-pitch))
  (define tip-half (* 0.08 half-pitch))
  (define (line a b) (list a b)) ; a straight flank needs only its two ends
  (define (rotate p c) (cons (- (* (car p) (cos c)) (* (cdr p) (sin c)))
                             (+ (* (car p) (sin c)) (* (cdr p) (cos c)))))
  (append*
   (for/list ([k (in-range z)])
     (define c (* k 2 half-pitch))
     (map (λ (p) (rotate p c))
          (append (line (polar rf (- base-half)) (polar ra (- tip-half)))
                  (line (polar ra tip-half) (polar rf base-half))
                  (for/list ([a (cdr (drop-right (samples base-half (- (* 2 half-pitch) base-half) 3) 1))])
                    (polar rf a)))))))

;; → a shape of kind 'gear.
(define (spur-gear #:teeth z #:module m #:width width
                   #:profile [profile 'involute] #:bore [bore #f]
                   #:pressure-angle-deg [pa-deg 20] #:backlash [backlash #f])
  (unless (and (exact-integer? z) (>= z 6)) (raise-argument-error 'spur-gear "a tooth count of at least 6" z))
  ;; A little play by default (4% of a module), as any made gear has.
  (define bl (or backlash (* 0.04 m)))
  (define outline (gear-outline z m #:profile profile #:pressure-angle-deg pa-deg #:backlash bl))
  (define rf (root-radius z m profile))
  (define bore-r (or bore (max (* 0.15 rf) (* 0.5 m))))
  (unless (< bore-r (* 0.9 rf)) (error 'spur-gear "bore ~a leaves no metal inside the root circle ~a" bore-r rf))
  (make-shape 'gear (extrude-star outline bore-r width)
              `((teeth . ,z) (module . ,m) (profile . ,profile) (width . ,width) (bore . ,bore-r)
                (pitch-radius . ,(pitch-radius z m)) (tip-radius . ,(tip-radius z m profile))
                (root-radius . ,rf))))
