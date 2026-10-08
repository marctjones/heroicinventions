#lang heroic
;; The Roman crane (roman-crane.rkt) with more stones on the ground beside the first, for a hand to change loads (#160):
;; lift the first one, let it down (the walkers turn the other way: HEROIC_SET "tympanus drive-rpm -3"), unhook it
;; ("hoist hook 0"), drag the hook over another stone and let go (it hooks the stone), and lift that one.
;;   stone-b  583 kg granite, 0.6 m cube, 0.8 m to one side of the first: the walkers lift it exactly as the first,
;;            at 3 rpm x 25 cm drum = 7.85 cm/s, the rope carrying 583 x 9.81 = 5719 N, 1430 N.m at the drum.
;;   stone-c  900 kg granite (a 0.6934 m cube), 0.8 m to the other side: 900 x 9.81 x 0.25 = 2207 N.m at the drum,
;;            more than the two walkers' 1545 N.m, so it stays on the ground and the wheel stalls with the rope
;;            holding what the walkers can give, 1545 / 0.25 = 6180 N.
;; roman-crane-reload has the two walkers of the original (2 x 772.5 = 1545 N.m). roman-crane-reload-gang has three (2318 N.m),
;; enough for any stone here: the wheel's bearing friction (0.2 per second of its turning) eats 1545 - 1430 = 115 N.m at the
;; two walkers' lifting speed, so two walkers raise even the first stone at about 4 cm/s, and three reach the 3 rpm limit, 7.85 cm/s.
;; (The hook is a 2 kg iron one; once it is hooked to a stone it is gone, and the stone alone hangs on the rope.)
(require racket/math)

(define wheel-r (m 2.25))
(define drum-r (cm 25))
(define axle (list (m -1.6) (m 2.6) 0))
(define rope-z (m 1.05))                 ; the drum, and the plane the rope runs in
(define walkers 2)
(define walker-torque (* 70 9.81 wheel-r (sin (degrees->radians 30))))

;; The jib's feet stand clear of the treadwheel (whose rim reaches x = 0.65).
(define jib-base (list (m 1.0) 0 rope-z))
(define jib-reach (m 2.2))
(define jib-height (m 6.8))
(define pulley-r (cm 15))
(define pulley-at (list (+ (car jib-base) jib-reach) (- jib-height pulley-r) rope-z))
(define stone-size (m 0.6))              ; granite: 583 kg
(define big-size (m 0.6934))             ; granite: 900 kg
(define stone-at (list (+ (car pulley-at) pulley-r) (+ (/ stone-size 2) (mm 5)) rope-z))

;; The rope runs from the drum over the pulley's top and down its far side
;; to the stone; its length is that path, so it starts just taut.
(define over-top (list (car pulley-at) (+ (cadr pulley-at) pulley-r) rope-z))
(define over-side (list (+ (car pulley-at) pulley-r) (cadr pulley-at) rope-z))
(define stone-top (list (car stone-at) (+ (cadr stone-at) (/ stone-size 2)) rope-z))
(define (dist p q) (sqrt (for/sum ([a p] [b q]) (sqr (- a b)))))
(define drum-centre (list (car axle) (cadr axle) rope-z))
(define rope-length (+ (sqrt (- (sqr (dist drum-centre over-top)) (sqr drum-r)))
                       (dist over-top over-side) (dist over-side stone-top)))

(define-syntax-rule (reload-crane name men)
  (define-machine name
  #:source "Vitruvius, De Architectura X.2; Haterii relief, c. AD 100"
  (wheel tympanus #:shape (treadwheel #:radius wheel-r #:width (m 1.2))
         #:at ((car axle) (cadr axle) 0) #:material oak
         #:drive-rpm 3 #:drive-torque (* men walker-torque))
  (wheel drum #:shape (drum #:radius drum-r #:length (cm 70))
         #:at ((car axle) (cadr axle) rope-z) #:material oak)
  (arbor tympanus drum)
  (fixture jib #:shape (crane-jib #:height jib-height #:reach jib-reach #:spread (m 1.6))
           #:at ((car jib-base) 0 rope-z) #:material oak)
  (wheel pulley #:shape (pulley #:radius pulley-r #:width (cm 8))
         #:at ((car pulley-at) (cadr pulley-at) rope-z) #:material bronze)
  (block stone #:at ((car stone-at) (cadr stone-at) rope-z) #:size stone-size #:material granite)
  (block stone-b #:at ((car stone-at) (cadr stone-at) (+ rope-z (m 0.8))) #:size stone-size #:material granite)
  (block stone-c #:at ((car stone-at) (+ (/ big-size 2) (mm 5)) (- rope-z (m 0.8))) #:size big-size #:material granite)
  (rope hoist #:wind-on drum #:to (stone 0 (/ stone-size 2) 0) #:length rope-length
        #:over (((car over-top) (cadr over-top) rope-z) ((car over-side) (cadr over-side) rope-z))
        #:diameter (cm 4) #:turns pulley)))

(reload-crane roman-crane-reload 2)
(reload-crane roman-crane-reload-gang 3)
