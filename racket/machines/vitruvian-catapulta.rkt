#lang heroic
;; A catapulta — the two-armed torsion bolt-shooter — proportioned
;; entirely from Vitruvius's table (De Architectura X.10), and loosed.
;; Every dimension scales from one number: the spring hole is a ninth of
;; the bolt. This is the common 3-span field piece: a 69 cm bolt, a 7.7 cm
;; hole, 54 cm arms, a 1.46 m channel (heroic/geometry/catapult.rkt has
;; each rule, and says where the Latin is corrupt and emended).
;;
;; Each arm is set through a spring of twisted sinew standing upright in
;; the frame, and turns about it. Winding the springs up twists them past
;; the arms' rest position — the spring still pulls when the arm is
;; against its forward stop — and drawing the bowstring back twists them
;; further. Loosed, the springs throw the arms forward; the bowstring,
;; running from both arm tips to the bolt's notched end, drives the bolt
;; along the channel; the arms hit their stops, the string goes slack,
;; and the bolt flies on.
;;
;; A spring pulls back in proportion to how far it's twisted: torque −k·(θ
;; − rest). Vitruvius tunes a spring by ear — plucked, both should ring at
;; the same note — and gives no stiffness; 300 N·m per radian here is an
;; estimate, and what the bolt does with it is what the physics gives.
(require racket/math)

(define bolt-length (* 3 greek-span))
(define G (catapulta-geometry bolt-length))
(define (g k) (hash-ref G k))
(define sx (g 'spring-x))
(define sy (g 'spring-y))
(define L (g 'arm-length))
(define drawn-deg 60)                     ; arms drawn back
(define stop-deg 20)                      ; forward stop, against the frame
(define rest-deg 40)                      ; untwisted: past the stop, so the spring still pulls there
(define k 300)                            ; N·m/rad, each spring

(define bolt-section (cm 2.5))
(define nock-y (+ (g 'channel-top) (/ bolt-section 2)))
(define (tip deg) (list (+ sx (* L (cos (degrees->radians deg)))) (* L (sin (degrees->radians deg)))))
;; The string is just straight across when the arms reach their stops...
(define dy (- sy nock-y))
(define string-len (let ([t (tip stop-deg)]) (sqrt (+ (sqr (car t)) (sqr dy)))))
;; ...so with the arms drawn back, the bolt's notched end sits here, behind them:
(define nock-z (let ([t (tip drawn-deg)])
                 (- (- (cadr t)) (sqrt (- (sqr string-len) (sqr (car t)) (sqr dy))))))
(define bolt-z (+ nock-z (/ bolt-length 2)))

(define-machine vitruvian-catapulta
  #:source "Vitruvius, De Architectura X.10"
  (fixture frame   #:shape (catapulta-frame #:bolt-length bolt-length)   #:at (0 0 0) #:material oak)
  (fixture springs #:shape (catapulta-springs #:bolt-length bolt-length) #:at (0 0 0) #:material hemp)
  ;; right arm: along +x from its spring, turning about the vertical;
  ;; positive angles sweep it back (−z)
  (lever right-arm #:at (sx sy 0) #:length L #:material oak #:axis y #:pivot-fraction 0
         #:start-angle-deg drawn-deg #:limit-lower-deg (- stop-deg) #:limit-upper-deg (+ drawn-deg 5)
         #:spring-stiffness k #:spring-rest-deg (- rest-deg) #:damping 0.1 #:section (g (quote arm-section)))
  ;; left arm: mirrored, along −x
  (lever left-arm #:at ((- sx) sy 0) #:length L #:material oak #:axis y #:pivot-fraction 1
         #:start-angle-deg (- drawn-deg) #:limit-lower-deg (- (+ drawn-deg 5)) #:limit-upper-deg stop-deg
         #:spring-stiffness k #:spring-rest-deg rest-deg #:damping 0.1 #:section (g (quote arm-section)))
  (block bolt #:at (0 nock-y bolt-z) #:size bolt-section #:material oak
         #:dimensions (bolt-section bolt-section bolt-length))
  (rope string-right #:from (right-arm L 0 0) #:to (bolt 0 0 (/ bolt-length -2)) #:length string-len #:diameter (cm 1) #:nocked #t)
  (rope string-left #:from (left-arm (- L) 0 0) #:to (bolt 0 0 (/ bolt-length -2)) #:length string-len #:diameter (cm 1) #:nocked #t))
