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

;; The shot, worked (#187). Loosed, the skeins have 938 - 55 = 883 J to give
;; between the catches and the stops (the trace's potential energy). Each arm,
;; 0.056 kg.m2, is turned by 523.6 N.m: 9,350 rad/s2, 78 rad/s after one 8.3 ms
;; tick (traced 78.2), so the whole shot is four ticks and no per-tick number in
;; it is a prediction. The string drives the bolt to 44.5 m/s; at the peak the
;; bolt has 1/2 x 0.312 x 44.5^2 = 309 J and the arms 248 J, 557 J of the 883
;; (the rest goes into the strings' stretch and the arms' damping). The arms
;; hit their stops, the string comes taut across them and checks the bolt, and
;; it flies on at 33.2 m/s. Until #187 the engine capped every body's spin at
;; 47.1 rad/s; that clamp took the arms' speed back every tick, the shot
;; peaked at 137 J, and the bolt left at 17.6 m/s.
;;
;; The range, worked (#148, again for #187). It leaves level at 33.3 m/s, 0.686
;; m up (the channel top), from 0.9 m out. Level, so no v^2 sin 2 theta / g: it
;; falls 0.686 - 0.012 = 0.674 m in sqrt(2 x 0.674 / 9.81) = 0.371 s, 33.3 x
;; 0.371 = 12.4 m on: 13.3 m (traced 13.1 m: the 69 cm bolt noses down and its
;; tip lands first). It skips twice, at 31.1 and then 30.2 m/s (traced), and
;; from 22.4 m out slides on at mu g = 0.45 x 9.81 = 4.41 m/s^2 (oak on the
;; floor; traced 4.41): 30.2^2 / (2 x 4.41) = 103.3 m, so it rests 125.7 m out
;; (traced 126.7). A flat-ground range is the 13 m, not the 127.
;;
;; The windlass at the back of the stock that draws it again (issue #161): a
;; 6 cm drum across the channel's far end, a rope from it to each arm's tip.
;; Until the crew wind it, it is held (its pawl: the crank at 0 rpm, up to 400
;; N.m): left free, the arms' tips, at 42 m/s, take up the span ropes' slack
;; in the shot and spin the drum to 30 rad/s, and it pays out 5 m of rope.
;; Wound in at 15 rpm it hauls both arms back past their catches, which drop
;; in behind them (one-way catches, #:catch-side); paid out, the ropes lie
;; slack, 1.95 m against the 1.65 m the arms reach at their stops (with 1.75
;; m they came taut there and spun the drum). Hauled back, each arm takes
;; its skein from 20 to 102 degrees of twist: 150 (1.780^2 - 0.349^2) =
;; 457 J an arm, 914 J for the two, up to 1220 N in each rope (523.6 N.m on
;; a 0.43 m lever) at the catch, 146 N.m at the 6 cm drum, well within its
;; 400. The rope's length alone asks 10.7 s of winding, 1.0 m at 0.094 m/s;
;; traced, the catches drop in after 24 s, 2.26 m wound, because the rope
;; solver lets these two ropes on one light drum held by its crank stretch
;; 1.3 m at the 1,458 N they read there (hemp would stretch a few cm), so
;; wind 24 s and pay out at 30 rpm for 12 s. (With the spin capped at 47.1
;; rad/s the solver's stretch correction on the drum was clipped and the same
;; winding took 15.6 s.) Laid on the trough again ((string-right load 1) nocks
;; it on both strings), the bolt is shot as the first was, four times traced
;; alike: 33.2 m/s as it leaves, at rest 126.7 m out.
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
  ;; one full cycle (#161): the windlass held; shot at 2 s; set both catches and wind the arms back, pay the ropes out, nock a bolt, shoot
  (operator (at 0 (windlass drive-rpm 0)) (at 0 (windlass drive-torque 400))
            (at 5 (right-arm catch 1)) (at 5 (left-arm catch 1)) (at 5 (windlass drive-rpm 15))
            (at 29 (windlass drive-rpm -30)) (at 41 (windlass drive-rpm 0))
            (at 41.5 (string-right load 1)) (at 42.5 (right-arm catch 0)) (at 42.5 (left-arm catch 0))))
