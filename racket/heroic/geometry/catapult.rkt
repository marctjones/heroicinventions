#lang racket/base
;; Vitruvius's proportions for the catapulta, the two-armed torsion
;; bolt-shooter (De Architectura X.10). Every dimension is a multiple of
;; one number, the diameter of the holes that carry the twisted-sinew
;; springs, and that hole is a ninth of the bolt's length. Greek
;; engineers (Philo, Hero) had worked this out by trial and recorded it
;; as a formula — the first design rule in engineering to scale a whole
;; machine from one measurement.
;;
;; The Latin text is corrupt in places. Each entry below says where its
;; number comes from:
;;   'vitruvius   — as the text reads
;;   'emended     — the manuscript's number is impossible; the value is
;;                  the emendation in Granger's Loeb edition
;;   'reconstructed — Vitruvius gives no number; chosen to fit the parts
;;                  he does size (noted in the entry)
(require racket/math racket/list "mesh.rkt" "shape.rkt")
(provide catapulta-proportions catapulta-dimensions catapulta-hole catapulta-geometry
         catapulta-frame catapulta-springs catapulta-arm
         ballista-hole-digits roman-digit roman-foot greek-span)

(define roman-foot 0.296)             ; m
(define roman-digit (/ roman-foot 16)) ; m
(define greek-span 0.231)             ; m — a spithamē, ¾ of a Greek foot

(define (catapulta-hole bolt-length) (/ bolt-length 9))

;; (name multiple-of-hole source note)
(define catapulta-proportions
  '((board-thickness       1     vitruvius "upper and lower boards (parallels) of the capital")
    (board-width           7/4   vitruvius "")
    (side-post-height      4     vitruvius "side posts (parastatae), between the boards")
    (side-post-thickness   5/12  emended   "text reads 'five'")
    (middle-post-width     5/4   vitruvius "middle post (mesostates)")
    (middle-post-thickness 1     vitruvius "")
    (spring-gap            3/2   reconstructed "room between posts for a spring one hole across")
    (bolt-window           3/5   reconstructed "height of the opening through the middle post for the bolt and string")
    (channel-length        19    vitruvius "the channel (canalis) the bolt slides in")
    (channel-section       3/4   vitruvius "width and depth of the channel's base")
    (arm-length            7     vitruvius "each arm (bracchium)")
    (arm-section           1/2   reconstructed "text's 'three twelfths at the root, half at the tip' is inverted; half used throughout")
    (column-height         8     vitruvius "support column (columella) with its base")
    (column-section        3/4   vitruvius "")
    (base-length           12    vitruvius "the base (subjectio) the column stands on")
    (brace-length          9     vitruvius "the braces (capreoli)")))

;; → hash of name → metres, for a bolt of the given length.
(define (catapulta-dimensions bolt-length)
  (define d (catapulta-hole bolt-length))
  (for/hasheq ([row catapulta-proportions])
    (values (first row) (* d (second row)))))

;; The stone-thrower's rule (X.11, after Philo): hole diameter in
;; digits = 1.1 × ∛(100 × shot weight in minae). Vitruvius tabulates it
;; in Roman pounds; this is the formula his table comes from.
(define (ballista-hole-digits minae) (* 1.1 (expt (* 100 minae) 1/3)))

(define (placed-box sx sy sz x y z) (mesh-translate (box-mesh sx sy sz) (v3 x y z)))

(define (layout bolt-length)
  (define D (catapulta-dimensions bolt-length))
  (define (d k) (hash-ref D k))
  (define hole (catapulta-hole bolt-length))
  (define post-t (d 'side-post-thickness))
  (define gap (d 'spring-gap))
  (define mid-w (d 'middle-post-width))
  (define capital-len (+ (* 2 post-t) (* 2 gap) mid-w)) ; across, along X
  (define y0 (d 'column-height))                         ; underside of the channel
  ;; The stock passes through the capital: the springs' middle, where the
  ;; arms come out, is level with the bolt lying on the channel, so the
  ;; string pulls straight along it.
  (define spring-mid (+ y0 (d 'channel-section) (* 0.16 hole)))
  (define cap-y0 (- spring-mid (d 'board-thickness) (/ (d 'side-post-height) 2)))
  (values D d hole capital-len y0 cap-y0
          (for/list ([side '(-1 1)]) (* side (+ (/ mid-w 2) (/ gap 2)))))) ; spring x positions

;; Where the working parts go, for a machine built on this frame: each
;; spring's axis (x, and the height of its middle), the top of the channel
;; the bolt slides on, and the channel's front and back ends (z).
(define (catapulta-geometry bolt-length)
  (define-values (D d hole capital-len y0 cap-y0 spring-xs) (layout bolt-length))
  (define len (d 'channel-length))
  (define mid-z (* -0.3 len))
  (hasheq 'hole hole
          'spring-x (second spring-xs)
          'spring-y (+ cap-y0 (d 'board-thickness) (/ (d 'side-post-height) 2))
          'channel-top (+ y0 (d 'channel-section))
          'channel-front (+ mid-z (/ len 2))
          'channel-back (- mid-z (/ len 2))
          'arm-length (d 'arm-length)
          'arm-section (d 'arm-section)))

;; The static wooden frame: capital (two boards, two side posts, a middle
;; post), the channel running fore and aft through it, and the column,
;; base and braces it stands on. Origin on the ground under the column,
;; +Y up, the bolt flying toward +Z.
(define (catapulta-frame #:bolt-length bolt-length #:arm-stop-deg [stop-deg 20])
  (define-values (D d hole capital-len y0 cap-y0 spring-xs) (layout bolt-length))
  ;; The side posts stand just in front of the arms' sweep: each arm, at its
  ;; forward stop, rests against the back of its post (where the padding
  ;; went) and swings away behind it when drawn — never through it.
  (define post-inner-x (- (/ capital-len 2) (d 'side-post-thickness)))
  (define post-z (+ (* (- post-inner-x (second spring-xs)) (tan (degrees->radians stop-deg)))
                    (/ (d 'arm-section) 2)
                    (/ (d 'board-width) 2)))
  (define board-t (d 'board-thickness))
  (define board-w (d 'board-width))
  (define post-h (d 'side-post-height))
  (define mid-y (+ cap-y0 board-t (/ post-h 2)))
  (define cs (d 'channel-section))
  (define pieces
    (append
     ;; capital
     (for/list ([y (list (+ cap-y0 (/ board-t 2)) (+ cap-y0 board-t post-h (/ board-t 2)))])
       (placed-box capital-len board-t board-w 0 y 0))
     (for/list ([side '(-1 1)])
       (placed-box (d 'side-post-thickness) post-h board-w
                   (* side (- (/ capital-len 2) (/ (d 'side-post-thickness) 2))) mid-y post-z))
     ;; the middle post, with an opening where the bolt and string pass through
     (let* ([post-bottom (+ cap-y0 board-t)]
            [post-top (+ post-bottom post-h)]
            [gap-bottom y0]
            [gap-top (+ y0 cs (d 'bolt-window))])
       (list (placed-box (d 'middle-post-width) (- gap-bottom post-bottom) (d 'middle-post-thickness)
                         0 (/ (+ post-bottom gap-bottom) 2) 0)
             (placed-box (d 'middle-post-width) (- post-top gap-top) (d 'middle-post-thickness)
                         0 (/ (+ gap-top post-top) 2) 0)))
     (list
           ;; channel: most of it behind the capital, where the bolt is drawn back
           (placed-box cs cs (d 'channel-length) 0 (+ y0 (/ cs 2)) (* -0.3 (d 'channel-length)))
           ;; column and a cross-shaped base
           (placed-box (d 'column-section) y0 (d 'column-section) 0 (/ y0 2) 0)
           (placed-box (d 'base-length) (* 0.5 cs) cs 0 (* 0.25 cs) 0)
           (placed-box cs (* 0.5 cs) (d 'base-length) 0 (* 0.25 cs) 0))
     ;; two braces, fore and aft, each running from near the end of the
     ;; base up to the column: a 9-hole brace whose foot is 5.4 holes out
     ;; meets the 8-hole column 7.2 holes up (a 3-4-5 triangle again)
     (for/list ([side '(-1 1)])
       (define len (d 'brace-length))
       (define reach (* 0.45 (d 'base-length)))
       (define top (sqrt (- (* len len) (* reach reach))))
       ;; the box's long axis is its Z; rot-x(a) sends Z to (0, −sin a, cos a),
       ;; which must point from the foot (z = side·reach) up to the column
       (mesh-transform (box-mesh (* 0.5 cs) (* 0.5 cs) len)
                       (rot-x (atan (- top) (* -1 side reach)))
                       (v3 0 (/ top 2) (* side (/ reach 2)))))))
  (make-shape 'catapult-frame (apply mesh-append pieces)
              `((bolt-length . ,bolt-length) (hole . ,hole) (capital-length . ,capital-len)
                (channel-height . ,y0) (arm-stop-deg . ,stop-deg))))

;; The two twisted-sinew springs, standing upright through the capital —
;; the machine's whole store of energy. Same origin as the frame.
(define (catapulta-springs #:bolt-length bolt-length)
  (define-values (D d hole capital-len y0 cap-y0 spring-xs) (layout bolt-length))
  (define h (+ (d 'side-post-height) (* 2 (d 'board-thickness)) (* 0.6 hole)))
  (define cy (+ cap-y0 (d 'board-thickness) (/ (d 'side-post-height) 2)))
  (define pieces
    (for/list ([x spring-xs])
      (mesh-transform (cylinder-mesh (* 0.45 hole) h #:segments 24) (rot-x (/ pi 2)) (v3 x cy 0))))
  (make-shape 'catapult-springs (apply mesh-append pieces)
              `((bolt-length . ,bolt-length) (hole . ,hole))))

;; One arm, as it sits at rest: rooted in its spring, swept back and
;; outward. side is -1 (left) or 1 (right). Same origin as the frame.
(define (catapulta-arm #:bolt-length bolt-length #:side side)
  (define-values (D d hole capital-len y0 cap-y0 spring-xs) (layout bolt-length))
  (define len (d 'arm-length))
  (define sec (d 'arm-section))
  (define x0 (if (< side 0) (first spring-xs) (second spring-xs)))
  (define cy (+ cap-y0 (d 'board-thickness) (/ (d 'side-post-height) 2)))
  (define sweep (degrees->radians 20)) ; back from straight out sideways, at rest
  (define dir (v3 (* side (cos sweep)) 0 (- (sin sweep))))
  (define m (mesh-transform (box-mesh len sec sec)
                            (rot-y (* side sweep))
                            (v+ (v3 x0 cy 0) (v* dir (/ len 2)))))
  (make-shape 'catapult-arm m `((bolt-length . ,bolt-length) (side . ,side) (length . ,len))))
