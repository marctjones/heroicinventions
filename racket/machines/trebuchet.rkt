#lang heroic
;; A counterweight trebuchet (medieval — not ancient, but the same lever
;; as the machines before it, taken as far as it goes). A heavy
;; counterweight hangs on a short chain from the arm's short end; the
;; long end carries a sling, and the stone lies on the ground behind the
;; machine at the sling's end.
;;
;; Let go, the counterweight falls and whips the arm round; the sling
;; lets the stone trail behind the arm's tip, then swing round faster
;; than the tip itself — the sling is a second, longer lever on the end
;; of the first. The sling's loop slips off its release pin once it has
;; swung close to in line with the arm (#:release-deg), and the stone
;; flies on its own momentum, released at about 0.85 s. Traced, it lands
;; about 16 m out: roughly an eighth of the machine's 856 J in the stone
;; (1.4 kg thrown 16 m needs ~110 J). Real trebuchets reach 30–60%,
;; mostly because their arms don't slam into a stop mid-throw. (Before the
;; rope fix for issue #45 it landed ~6 m out: the chain's stretch
;; correction was jerking the arm and wasting the throw.)
;;
;; Throws toward -X. Everything here is rope, hinge and falling weight —
;; nothing scripts the flight.
(require racket/math)

(define arm-length (m 1.8))
(define pivot-fraction 0.15)                         ; short arm = 15% of the beam
(define short-arm (* pivot-fraction arm-length))     ; 0.27 m
(define long-arm (- arm-length short-arm))           ; 1.53 m
(define pivot (list 0 (m 1.4)))
(define cocked-deg -50)                              ; long end down and back, ready to throw
(define c (degrees->radians cocked-deg))
(define (on-arm d) (list (+ (car pivot) (* d (cos c))) (+ (cadr pivot) (* d (sin c)))))

(define cw-size (m 0.3))                             ; granite: 73 kg
(define chain (cm 35))                                ; long enough that the weight clears the beam
(define short-end (on-arm (- short-arm)))
(define cw-at (list (car short-end) (- (cadr short-end) chain (/ cw-size 2))))

(define stone-size (cm 8))                           ; granite: 1.4 kg — about 1:50 to the counterweight
(define sling (m 1.2))
(define tip (on-arm long-arm))
(define stone-y (/ stone-size 2))
(define stone-at (list (+ (car tip) (sqrt (- (sqr sling) (sqr (- (cadr tip) stone-y))))) stone-y))

(define-machine trebuchet
  #:source "Classic mechanics demonstration (medieval; its lever is Archimedes')"
  (lever arm #:at ((car pivot) (cadr pivot) 0) #:length arm-length #:material oak
         #:pivot-fraction pivot-fraction #:start-angle-deg cocked-deg #:limit-deg 140 #:damping 0.2)
  (block counterweight #:at ((car cw-at) (cadr cw-at) 0) #:size cw-size #:material granite)
  (rope cw-chain #:from (arm (- short-arm) 0 0) #:to (counterweight 0 (/ cw-size 2) 0)
        #:length chain #:material iron #:diameter (cm 1.5))
  (block stone #:at ((car stone-at) (cadr stone-at) 0) #:size stone-size #:material granite)
  (rope sling-rope #:from (arm long-arm 0 0) #:to (stone 0 0 0) #:length sling
        #:release-deg 60 #:diameter (cm 1)))
