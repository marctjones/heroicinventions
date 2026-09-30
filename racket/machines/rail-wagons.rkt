#lang heroic
;; Why rails: two iron-wheeled wagons on the same gentle 1 degree grade,
;; 12 m long. One stands on a limestone roadway, the other on a pair of
;; iron rails, its flanged wheels straddling them.
;;
;; A wheel on a road loses C_rr = 0.04 of its load to rolling (a stage
;; coach on a dirt road); iron on iron rail only 0.002 (a railway wheel;
;; both from Wikipedia's table). The grade pulls tan 1 = 0.0175 of the
;; weight along it: more than the rails take, less than the road does.
;; So the wagon on the road stays where it is, and the one on the rails
;; rolls away, at
;;   a = g (sin t - C_rr cos t) M / (M + sum I / r^2)
;;     = 9.81 (0.017452 - 0.002 x 0.99985) x 95.08 / (95.08 + 44.94)
;;     = 0.1029 m/s2
;; with an oak bed of 17.3 kg on four flanged iron wheels of 19.4 kg, each
;; turning on a 10 cm tread with I/r^2 = 11.24 kg (much of its iron in the
;; flanges, far out): a third of the pull goes into turning them. After
;; 8 s it runs 0.82 m/s, 3.3 m down the line. Give the road wagon a
;; steeper road -- over 2.3 degrees, tan t > 0.04 -- and it will go too.
(require racket/math racket/list)

(define grade-deg 1)
(define t (degrees->radians grade-deg))
(define line (m 12))
(define half-slab (cm 2.5))                 ; a ramp's slab is 5 cm thick
(define r (cm 10))                          ; tread radius
(define gauge (m 0.6))                      ; rail centre to rail centre
(define up (m 10))

(define (on-slope x along lift base-z)
  (define n (+ half-slab lift (mm 1)))
  (list x (+ (* along (sin t)) (* n (cos t))) (+ base-z (- (* along (cos t))) (* n (sin t)))))
(define (P p) p)
(define rail-x (m 2))                       ; the railway's centre line
(define road-x (m -1))
(define base-z (m 6))

(define flanged (drum #:radius r #:length (cm 8) #:flange-radius (cm 12)))
(define plain (disc-wheel #:radius r #:width (cm 6)))

(define (wheel-point cx dx end) (on-slope (+ cx dx) (+ up (* end (cm 35))) r base-z))
(define rw (for*/list ([dx (list (- (/ gauge 2)) (/ gauge 2))] [end '(-1 1)]) (wheel-point rail-x dx end)))
(define dw (for*/list ([dx (list (- (/ gauge 2)) (/ gauge 2))] [end '(-1 1)]) (wheel-point road-x dx end)))
(define rail-bed (on-slope rail-x up r base-z))
(define road-bed (on-slope road-x up r base-z))
(define (x p) (first p)) (define (y p) (second p)) (define (z p) (third p))

(define-machine rail-wagons
  #:source "the wagonway (Newcastle, 17th century); rolling resistance"
  (ramp road #:at (road-x 0 base-z) #:length line #:width (m 1.2) #:angle-deg grade-deg #:material limestone)
  (ramp left-rail #:at ((- rail-x (/ gauge 2)) 0 base-z) #:length line #:width (cm 4) #:angle-deg grade-deg #:material iron)
  (ramp right-rail #:at ((+ rail-x (/ gauge 2)) 0 base-z) #:length line #:width (cm 4) #:angle-deg grade-deg #:material iron)
  (block rail-wagon #:at ((x rail-bed) (y rail-bed) (z rail-bed)) #:size (cm 6)
         #:dimensions ((m 0.5) (cm 6) (m 0.8)) #:material oak #:tilt-deg grade-deg)
  (wheel rail-1 #:shape flanged #:at ((x (list-ref rw 0)) (y (list-ref rw 0)) (z (list-ref rw 0))) #:axis x #:material iron #:on rail-wagon)
  (wheel rail-2 #:shape flanged #:at ((x (list-ref rw 1)) (y (list-ref rw 1)) (z (list-ref rw 1))) #:axis x #:material iron #:on rail-wagon)
  (wheel rail-3 #:shape flanged #:at ((x (list-ref rw 2)) (y (list-ref rw 2)) (z (list-ref rw 2))) #:axis x #:material iron #:on rail-wagon)
  (wheel rail-4 #:shape flanged #:at ((x (list-ref rw 3)) (y (list-ref rw 3)) (z (list-ref rw 3))) #:axis x #:material iron #:on rail-wagon)
  (block road-wagon #:at ((x road-bed) (y road-bed) (z road-bed)) #:size (cm 6)
         #:dimensions ((m 0.5) (cm 6) (m 0.8)) #:material oak #:tilt-deg grade-deg)
  (wheel road-1 #:shape plain #:at ((x (list-ref dw 0)) (y (list-ref dw 0)) (z (list-ref dw 0))) #:axis x #:material iron #:on road-wagon)
  (wheel road-2 #:shape plain #:at ((x (list-ref dw 1)) (y (list-ref dw 1)) (z (list-ref dw 1))) #:axis x #:material iron #:on road-wagon)
  (wheel road-3 #:shape plain #:at ((x (list-ref dw 2)) (y (list-ref dw 2)) (z (list-ref dw 2))) #:axis x #:material iron #:on road-wagon)
  (wheel road-4 #:shape plain #:at ((x (list-ref dw 3)) (y (list-ref dw 3)) (z (list-ref dw 3))) #:axis x #:material iron #:on road-wagon))
