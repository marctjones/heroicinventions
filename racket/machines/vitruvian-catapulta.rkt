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
;;
;; A catch on each arm (issue #155) stands for the one claw on the slider
;; that holds the string: (right-arm catch 1) and (left-arm catch 1) hold it
;; drawn, 0 looses it. Held, each catch carries its spring's 300 x (60 + 40)
;; degrees = 300 x 1.745 = 523.6 N.m: the arms turn about the vertical, so
;; their weight puts no turn on them. Both open 2 s in (#:release-after 2), as
;; the trebuchet's and onager's do; until then the 0.31 kg bolt (720 x 0.025^2
;; x 0.693) lies in the channel, its 3.06 N carried by the frame (issue #189).
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

;; The windlass at the back of the stock that draws it again (issue #161): a
;; 6 cm drum across the channel's far end, a rope from it to each arm's tip.
;; Wound in at 15 rpm it hauls both arms back past their catches, which drop
;; in behind them (one-way catches, #:catch-side); paid out, the ropes lie
;; slack, 1.95 m against the 1.65 m the arms reach at their stops (with 1.75
;; m they came taut there and spun the drum). Hauled back, each arm takes
;; its skein from 20 to 102 degrees of twist: 150 (1.780^2 - 0.349^2) =
;; 457 J an arm, 914 J for the two, up to 1220 N in each rope (523.6 N.m on
;; a 0.43 m lever) at the catch, 146 N.m at the 6 cm drum, well within its
;; 400. (Traced the ropes read only ~300 N and stretch 0.35 m: the arms are
;; light, 0.056 kg.m2, under a stiff skein, and the rope solver leaves most
;; of the hold to its stretch correction, so wind 15.6 s rather than the
;; 10.7 s the rope's length alone asks.) Laid on the trough again
;; ((string-right load 1) nocks it on both strings), the bolt is shot as the
;; first was, four times traced alike: 19.8 m/s, at rest 35.9 m out.
;;
;; The range, worked (#148). The bolt is driven along the channel by the
;; string to 22-24 m/s (traced 22.1 at the sampling used here, 23.8 at the
;; finest) and, when the arms hit their stops and the string goes slack,
;; flies on at 17.6 m/s, level, 0.686 m up (the channel top). The 19.8 m/s
;; above is a spike read off sampled frames, not the speed it flies at.
;; Level, so no v^2 sin 2 theta / g: it falls 0.686 - 0.012 = 0.674 m in
;; sqrt(2 x 0.674 / 9.81) = 0.371 s, 17.6 x 0.371 = 6.5 m on, from the 0.96 m
;; where it leaves the trough: 7.5 m (traced 7.0 m, the 69 cm bolt noses down
;; and its tip lands first). It runs on along the floor, at 15.4 m/s after
;; the first bounce, and slides on at mu g = 0.45 x 9.81 = 4.41 m/s^2
;; (oak on the floor): 15.4^2 / (2 x 4.41) = 26.9 m, so it rests 7.6 + 26.9
;; = 34.5 m out, against the traced 35.9 m (the slide begins as the bolt
;; tumbles and drags, 4% more). A flat-ground range is the 7 m, not the 36.
(define drum-r (cm 6))
(define drum-z (- (g 'channel-back) (cm 18)))
(define span-length (m 1.95))

(define-machine vitruvian-catapulta
  #:source "Vitruvius, De Architectura X.10"
  (fixture frame   #:shape (catapulta-frame #:bolt-length bolt-length #:arm-stop-deg stop-deg) #:at (0 0 0) #:material oak)
  (fixture springs #:shape (catapulta-springs #:bolt-length bolt-length) #:at (0 0 0) #:material hemp)
  ;; right arm: along +x from its spring, turning about the vertical;
  ;; positive angles sweep it back (−z)
  (lever right-arm #:at (sx sy 0) #:length L #:material oak #:axis y #:pivot-fraction 0
         #:start-angle-deg drawn-deg #:limit-lower-deg (- stop-deg) #:limit-upper-deg (+ drawn-deg 5)
         #:spring-stiffness k #:spring-rest-deg (- rest-deg) #:damping 0.1 #:section (g (quote arm-section))
         #:catch-deg drawn-deg #:catch-side -1 #:release-after 2)
  ;; left arm: mirrored, along −x
  (lever left-arm #:at ((- sx) sy 0) #:length L #:material oak #:axis y #:pivot-fraction 1
         #:start-angle-deg (- drawn-deg) #:limit-lower-deg (- (+ drawn-deg 5)) #:limit-upper-deg stop-deg
         #:spring-stiffness k #:spring-rest-deg rest-deg #:damping 0.1 #:section (g (quote arm-section))
         #:catch-deg (- drawn-deg) #:catch-side 1 #:release-after 2)
  (block bolt #:at (0 nock-y bolt-z) #:size bolt-section #:material oak
         #:dimensions (bolt-section bolt-section bolt-length))
  (rope string-right #:from (right-arm L 0 0) #:to (bolt 0 0 (/ bolt-length -2)) #:length string-len #:diameter (cm 1) #:nocked #t)
  (rope string-left #:from (left-arm (- L) 0 0) #:to (bolt 0 0 (/ bolt-length -2)) #:length string-len #:diameter (cm 1) #:nocked #t)
  (wheel windlass #:shape (drum #:radius drum-r #:length (cm 24)) #:at (0 sy drum-z) #:axis x #:material oak
         #:drive-rpm 15 #:drive-torque 0)
  (rope span-right #:wind-on windlass #:to (right-arm L 0 0) #:length span-length #:diameter (cm 1.5))
  (rope span-left #:wind-on windlass #:to (left-arm (- L) 0 0) #:length span-length #:diameter (cm 1.5))
  ;; one full cycle (#161): shot at 2 s; set both catches and wind the arms back, pay the ropes out, nock a bolt, shoot
  (operator (at 5 (right-arm catch 1)) (at 5 (left-arm catch 1)) (at 5 (windlass drive-torque 400))
            (at 20.6 (windlass drive-rpm -30)) (at 28.4 (windlass drive-rpm 0))
            (at 29 (string-right load 1)) (at 30 (right-arm catch 0)) (at 30 (left-arm catch 0))))
