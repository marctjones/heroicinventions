#lang racket/base
;; Wheels on an axle: pulley sheaves, windlass drums, treadwheels and
;; norias. All centred on the origin, axle along Z.
(require racket/math racket/list "mesh.rkt" "shape.rkt")
(provide pulley drum treadwheel noria)

(define (ring inner outer z0 z1 #:segments [n 64]) ; an annulus of rectangular section
  (revolve-profile (list (cons inner z0) (cons outer z0) (cons outer z1) (cons inner z1)) #:segments n))

;; A box of the given size, its centre moved to `at` then turned `angle`
;; about the axle.
(define (placed-box sx sy sz at angle)
  (mesh-transform (mesh-translate (box-mesh sx sy sz) at) (rot-z angle)))

(define (composite kind pieces props)
  (make-shape kind (apply mesh-append pieces) props))

;; A grooved sheave, the wheel of a block-and-tackle.
(define (pulley #:radius r #:width w #:groove-depth [depth #f] #:bore [bore #f])
  (define d (or depth (* 0.18 r)))
  (define b (or bore (* 0.15 r)))
  (define h (/ w 2))
  (make-shape 'pulley
              (revolve-profile (list (cons b (- h)) (cons r (- h)) (cons r (* -0.3 w))
                                     (cons (- r d) 0) (cons r (* 0.3 w)) (cons r h) (cons b h)))
              `((radius . ,r) (width . ,w) (groove-depth . ,d) (bore . ,b))))

;; A windlass barrel: rope winds on the barrel, flanges keep it on.
(define (drum #:radius r #:length len #:flange-radius [fr #f] #:flange-thickness [ft #f] #:bore [bore #f])
  (define f (or fr (* 1.4 r)))
  (define t (or ft (* 0.08 len)))
  (define b (or bore (* 0.25 r)))
  (define h (/ len 2))
  (make-shape 'drum
              (revolve-profile (list (cons b (- h)) (cons f (- h)) (cons f (+ (- h) t)) (cons r (+ (- h) t))
                                     (cons r (- h t)) (cons f (- h t)) (cons f h) (cons b h)))
              `((radius . ,r) (length . ,len) (flange-radius . ,f) (bore . ,b))))

;; Hub, two rims and spokes — shared by treadwheels and norias.
(define (spoked-frame r w spokes rim-depth rim-thick hub-r)
  (define h (/ w 2))
  (define inner (- r rim-depth))
  (define spoke-len (- inner hub-r))
  (append
   (list (ring (* 0.4 hub-r) hub-r (- h) h))
   (for/list ([z (list (- h (/ rim-thick 2)) (- (/ rim-thick 2) h))])
     (ring inner r (- z (/ rim-thick 2)) (+ z (/ rim-thick 2))))
   (for*/list ([z (list (- h (/ rim-thick 2)) (- (/ rim-thick 2) h))]
               [i (in-range spokes)])
     ;; each spoke runs a little into the hub and the rim so no gap shows
     (placed-box (+ spoke-len (* 0.5 rim-depth)) rim-thick rim-thick
                 (v3 (+ hub-r (/ spoke-len 2)) 0 z) (/ (* 2 pi i) spokes)))))

;; A Roman treadwheel (magnus tympanus), the engine of the great
;; building cranes: men walk on treads inside the rim to turn it.
(define (treadwheel #:radius r #:width w #:spokes [spokes 8] #:treads [treads 32])
  (define rim-depth (* 0.06 r))
  (define rim-thick (* 0.05 w))
  (define hub-r (* 0.08 r))
  (define tread-r (- r rim-depth (* 0.01 r)))
  (define pieces
    (append (spoked-frame r w spokes rim-depth rim-thick hub-r)
            (for/list ([i (in-range treads)])
              (placed-box (* 0.03 r) (* 0.05 r) w (v3 tread-r 0 0) (/ (* 2 pi (+ i 0.5)) treads)))))
  (composite 'treadwheel pieces `((radius . ,r) (width . ,w) (spokes . ,spokes) (treads . ,treads))))

;; A noria: a wheel with buckets built into its rim and paddles outside
;; it. River current on the paddles turns it; each bucket fills at the
;; bottom and empties into a trough near the top. Buckets open toward
;; +θ (counter-clockwise, looking down +Z), so the wheel lifts water
;; when it turns that way.
(define (noria #:radius r #:width w #:spokes [spokes 12] #:buckets [buckets 24]
               #:bucket-depth [depth #f] #:paddle-depth [paddle #f])
  (define bd (or depth (* 0.12 r)))
  (define pd (or paddle (* 0.10 r)))
  (define rim-depth (* 0.04 r))
  (define rim-thick (* 0.05 w))
  (define hub-r (* 0.06 r))
  (define plate (min (* 0.012 r) 0.03)) ; bucket boards stay a few cm thick however big the wheel
  (define bucket-len (* 0.8 (/ (* 2 pi r) buckets))) ; tangential length along the rim
  (define inner-w (- w (* 2 rim-thick)))
  (define (bucket angle)
    ;; built with the bucket's mouth facing +Y at angle 0 (+θ there),
    ;; its outer wall on the rim and its back wall trailing
    (define rc (- r (/ bd 2)))
    (for/list ([b (list (list plate bucket-len inner-w (v3 (- r (/ plate 2)) 0 0))          ; outer wall
                        (list plate bucket-len inner-w (v3 (- r bd (/ plate -2)) 0 0))     ; inner wall
                        (list bd plate inner-w (v3 rc (- (/ bucket-len 2)) 0))            ; back wall
                        (list bd bucket-len plate (v3 rc 0 (- (/ inner-w 2) (/ plate 2))))   ; sides
                        (list bd bucket-len plate (v3 rc 0 (- (/ plate 2) (/ inner-w 2)))))])
      (placed-box (first b) (second b) (third b) (fourth b) angle)))
  (define pieces
    (append (spoked-frame r w spokes rim-depth rim-thick hub-r)
            (append* (for/list ([i (in-range buckets)]) (bucket (/ (* 2 pi i) buckets))))
            (for/list ([i (in-range buckets)])
              (placed-box (+ pd (* 0.5 rim-depth)) plate w (v3 (+ r (/ pd 2) (* -0.25 rim-depth)) 0 0)
                          (/ (* 2 pi (+ i 0.5)) buckets)))))
  (composite 'noria pieces
             `((radius . ,r) (width . ,w) (spokes . ,spokes) (buckets . ,buckets)
               (bucket-depth . ,bd) (bucket-length . ,bucket-len) (paddle-depth . ,pd))))
