#lang heroic
;; The Lonely Rover's world (issue #61): one crater about 800 m across and 70 m
;; deep, after Victoria crater, which Opportunity explored from 2006 to 2008.
;; The game is played on its floor and lower slopes; the rim bounds the world.
;;
;; The shape is racket/heroic/map.rkt's crater: a floor 70 m below the plain, walls
;; rising as r^4 (steepest, 0.75 or 37 degrees, at the rim) to a crest 5 m above it,
;; the floor rippled by dunes 1.2 m high and 40 m apart, and the rim scalloped into
;; seven bays by 5% of the radius. The ground is 170 x 170 cells of 5 m, 28,900 of
;; the 40,000 a map may have.
;;
;; The ground in layers, each its own soil, by where it lies:
;;   basalt-sand            the dunes on the floor and the apron of talus up to 0.7 of the radius: dark sand
;;                          for dark glass, no cohesion, standing to its repose, tan 32 = 0.62
;;   bedrock                the wall: 50 MPa of cohesion, which stands for tens of kilometres, so the rover
;;                          can't climb it, the backhoe can't cut it, and it never comes down
;;   ice-cemented-regolith  the cold, pole-facing wall (north, -z): soil held by ice, 40 kPa
;;   silica-sand            the pale layer exposed in one bay (azimuth 150 to 175, west): clear glass, 5 kPa
;;   regolith               the plain outside the rim
;;   sublimed-regolith      one section of the rim the storm has weakened (below)
;;
;; The slide. A block of rim, 12 degrees of azimuth across at its full height and tapering to
;; nothing 8 degrees further out each side, from 0.80 of the radius out, stands 24 m proud of the
;; wall, a cliff over the apron below it. Mars regolith (1500 kg/m3, tan phi = 0.70) holds a
;; cut face up to its critical height, 4c/(rho g) tan(45 + phi/2), 1.921 x 4c/(rho g):
;;     c = 40 kPa (ice-cemented): 55.2 m on Mars, 20.9 m on Earth  - it would stand
;;     c = 16 kPa (sublimed):     22.1 m on Mars,  8.4 m on Earth  - the storm's work: the ice
;;                                cementing it has gone, and 24 m (and the slope's own 1.5 m) is past
;;                                what is left
;; so the block collapses on screen as the world loads (it plays out at 20 passes of relaxation
;; a second, about four seconds), and what it brings down buries whatever stands at its foot.
;; Nothing is placed by hand: the rubble lies where the ground's physics puts it, the same each run.
;; The block runs out into the rim at its sides and back, so only its face, the part that
;; stands past what the regolith holds, comes down.
;;
;; The rock. The rim is rocky: 2% of what its failed faces lose comes down as 2 m cubes of
;; granite (21.6 t each, #88), floor(0.02 V / 8 m3) of them, laid on the rubble where it lies
;; thickest, from which they roll down the debris and stop on ground under their angle of
;; repose.
;;
;; The wind. A notch is cut in the rim at azimuth 200 (west, a little north), and a
;; corridor of wind runs from it across the floor: 6 m/s on the line from the notch through the
;; centre, falling to 0.3 of that away from it (Gaussian, 80 m wide), 1 + 0.35 cos(2 pi (hour - 2) / 24)
;; over the day (cold air draining down the walls at night, warm air rising by day) and gusting by 25%.
(require racket/math)

(define radius 400.0)
(define bays '(7 0.05))                         ; seven scallops, 5% of the radius deep
(define (rim-radius theta) (* radius (+ 1 (* (cadr bays) (cos (* (car bays) theta))))))
(define (degrees theta) (* theta (/ 180 pi)))

;; where a point lies: its share of the rim's radius there, and its azimuth in degrees from +x toward +z
(define (share x z) (/ (sqrt (+ (* x x) (* z z))) (rim-radius (atan z x))))
(define (azimuth x z) (degrees (atan z x)))
(define (within? a centre half)                  ; angles in degrees, wrapped
  (< (abs (- (modulo* (+ (- a centre) 180) 360) 180)) half))
(define (modulo* a n) (- a (* n (floor (/ a n)))))

(define weak-azimuth 30.0)                       ; the weakened rim section, east and a little south
(define weak-full 6.0)                           ; the block stands its whole height this many degrees either side of its middle,
(define weak-half 14.0)                          ; and tapers away to nothing at this many: no step on its sides to fail
(define weak-from 0.80)
(define weak-height 24.0)
(define (weak-rise x z)                          ; how far proud of the wall the block stands there
  (define across (abs (- (modulo* (+ (- (azimuth x z) weak-azimuth) 180) 360) 180)))
  (define sideways
    (cond [(<= across weak-full) 1.0]
          [else (expt (cos (* (/ pi 2) (/ (- across weak-full) (- weak-half weak-full)))) 2)]))
  (define outward                                ; it runs back into the plain over the rim, no step at its back either
    (cond [(<= (share x z) 1.0) 1.0]
          [else (expt (cos (* (/ pi 2) (/ (- (share x z) 1.0) 0.12))) 2)]))
  (cond [(or (< (share x z) weak-from) (> (share x z) 1.12) (>= across weak-half)) 0.0]
        [else (* weak-height sideways outward)]))
(define (weak? x z) (> (weak-rise x z) 1.0))

(define notch-azimuth 200.0)
(define (notch-cut x z)                          ; the rim lowered by up to 18 m where the corridor comes in
  (define s (share x z))
  (define across (abs (- (modulo* (+ (- (azimuth x z) notch-azimuth) 180) 360) 180)))
  (if (and (> s 0.88) (< across 6.0))
      (* 18.0 (expt (cos (* (/ pi 2) (/ across 6.0))) 2) (min 1.0 (/ (- s 0.88) 0.07)))
      0.0))

(define floor-shape (crater #:diameter 800 #:depth 70 #:rim-height 5 #:rim-width 60 #:dunes '(1.2 40) #:bays bays))

(define (height x z)
  (+ (floor-shape x z) (weak-rise x z) (- (notch-cut x z))))

(define (ground-soil x z)
  (define s (share x z))
  (define a (azimuth x z))
  (cond [(weak? x z) 'sublimed-regolith]
        [(> s 1.12) 'regolith]
        [(< s 0.70) 'basalt-sand]
        [(and (within? a 162.5 12.5) (< 0.58 s 0.95)) 'silica-sand]
        [(and (within? a -90 45) (> s 0.70)) 'ice-cemented-regolith]
        [else 'bedrock]))

(define-map victoria
  #:origin (-425 -425) #:cell 5 #:size (170 170)
  #:heights height
  #:soil (λ (x z) (ground-soil x z))
  #:cohesion ((bedrock 5e7) (ice-cemented-regolith 40000) (sublimed-regolith 16000) (silica-sand 5000))
  #:boulders ((sublimed-regolith 0.02 2.0 granite))
  #:edges closed
  #:settle 20
  #:wind (corridor-wind #:through '(0 0) #:notch-deg notch-azimuth #:speed 6 #:width 80 #:base 0.3
                        #:daily 0.35 #:peak-hour 2 #:gust 0.25))
