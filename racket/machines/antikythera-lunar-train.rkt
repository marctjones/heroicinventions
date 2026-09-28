#lang heroic
;; The lunar gear train of the Antikythera mechanism (Greek, c. 100 BC,
;; recovered from a shipwreck off Antikythera in 1901). Turning the
;; crank once a year turns b2 once; through three pairs of gears, e2
;; turns 64/38 × 48/24 × 127/32 = 254/19 times — the 254 sidereal months
;; in 19 years, the Moon's motion against the stars. Tooth counts from
;; the X-ray CT reconstruction (Freeth et al., Nature 444, 2006).
;;
;; The teeth are triangular, as the real ones are: involute teeth, which
;; give a perfectly steady ratio, weren't worked out until the 1700s.
;; About half a millimetre per tooth (module 0.5 mm), on bronze plate
;; ~2 mm thick. c1/c2 and d1/d2 are pairs fixed on one arbor each, so
;; the train steps from layer to layer — front to back here, so the big
;; 127-tooth d2 sits behind the gears it would otherwise hide.
;;
;; Every gear is placed at its exact centre distance and turned to the
;; phase where its teeth fall between its partner's (mate-angle). They
;; stand still for now: turning one gear to drive the next needs the
;; gear-coupling physics, not yet built.

(require racket/math racket/list)

(define m (mm 0.5))
(define plate (mm 2))
(define layer (mm 4)) ; spacing between the gear planes along the arbors
(define height (cm 8))

(define (gear z) (spur-gear #:teeth z #:module m #:width plate #:profile 'triangular))
(define (offset from dist deg)
  (list (+ (first from) (* dist (cos (degrees->radians deg))))
        (+ (second from) (* dist (sin (degrees->radians deg))))))

;; arbor positions: each is its gear pair's centre distance from the last
(define line-bc 0)   (define B (list 0 height))
(define line-cd 60)  (define C (offset B (center-distance 64 38 m) line-bc))
(define line-de 150) (define D (offset C (center-distance 48 24 m) line-cd))
(define E (offset D (center-distance 127 32 m) line-de))

;; phases: each driven gear turned to fit its driver's teeth
(define b2-angle 0)
(define c1-angle (radians->degrees (mate-angle 64 (degrees->radians b2-angle) 38 (degrees->radians line-bc))))
(define c2-angle 0)
(define d1-angle (radians->degrees (mate-angle 48 (degrees->radians c2-angle) 24 (degrees->radians line-cd))))
(define d2-angle 0)
(define e2-angle (radians->degrees (mate-angle 127 (degrees->radians d2-angle) 32 (degrees->radians line-de))))

(define-machine antikythera-lunar-train
  #:source "Antikythera mechanism, c. 100 BC (Freeth et al., Nature 2006)"
  (wheel b2 #:shape (gear 64)  #:at ((first B) (second B) (* 2 layer))   #:material bronze #:angle-deg b2-angle)
  (wheel c1 #:shape (gear 38)  #:at ((first C) (second C) (* 2 layer))   #:material bronze #:angle-deg c1-angle)
  (wheel c2 #:shape (gear 48)  #:at ((first C) (second C) layer)         #:material bronze #:angle-deg c2-angle)
  (wheel d1 #:shape (gear 24)  #:at ((first D) (second D) layer)         #:material bronze #:angle-deg d1-angle)
  (wheel d2 #:shape (gear 127) #:at ((first D) (second D) 0)             #:material bronze #:angle-deg d2-angle)
  (wheel e2 #:shape (gear 32)  #:at ((first E) (second E) 0)             #:material bronze #:angle-deg e2-angle))
