#lang heroic
;; High-pressure steam on Mars (issue #65). Two small Trevithick engines,
;; the same in every part, each a double-acting 5 cm cylinder fed from a
;; boiler at 150 C and working an iron flywheel through a crank and rod.
;; One stands in the open, exhausting into Mars's 610 Pa; the other inside
;; a pressurised hut at 101,325 Pa, like an engine on Earth.
;;
;; The boiler's steam pushes the piston directly, so each stroke does
;;   W = (P_boiler - P_air) x A x S
;; P_boiler = 472.6 kPa (the sim's Antoine equation at 150 C; steam tables
;; give 476), A =
;; pi 0.05^2 / 4 = 1.963e-3 m2, and the crank's stroke S: the cylinder
;; stands 5 cm off the crank's line (so it can start: from the top its rod
;; pulls at 30 degrees), which makes the stroke
;;   S = sqrt((l + r)^2 - e^2) - sqrt((l - r)^2 - e^2) = 0.20105 m
;; with the rod l = 0.5 m, the crank r = 0.1 m, the offset e = 0.05 m. So:
;;   in the open:  (472.6 - 0.6) kPa x A x S = 186.3 J a stroke
;;   in the hut:   (472.6 - 101.3) kPa x A x S = 146.6 J: the open one
;;                 does 1.271 times as much
;; Newcomen's engine, pushed by the air, fails outright on Mars (-1.5 kN
;; on its piston, the vapour beating the air); this one does better there.
;; Each 390 kg flywheel is loaded with 30 N m of work to do (a mill it
;; turns), less than either engine's average 2W / 2 pi, so both run up,
;; slowly. (Jolt's joints give a little: the traced stroke is 1-2 mm long.)
(require racket/math)
(define r (cm 10)) (define l (cm 50)) (define e (cm -5))   ; on the left: from the top, the first stroke is a full one down
(define crank-y (m 0.6))
(define pin-y (+ crank-y (sqrt (- (* r r) (* e e)))))
(define wrist-y (+ pin-y l))
(define bottom (m 0.95))
(define travel (cm 30))
(define z (cm 9))                               ; rod and piston in front of the flywheel

(define-machine steam-engines-mars
  #:source "Richard Trevithick's high-pressure engine (1802), on Mars"
  #:planet mars
  (enclosure hut #:at ((m 3) 0 0) #:size ((m 2.5) (m 2.4) (m 2.5)) #:pressure 101325 #:temperature 20)
  ;; in the open
  (boiler open-boiler #:at ((m -3) 0 (m -1)) #:radius (cm 30) #:height (cm 60) #:water 200 #:fire 5000 #:temperature 150 #:material iron)
  (wheel open-flywheel #:shape (disc-wheel #:radius (cm 40) #:width (cm 10)) #:at ((m -3) crank-y 0) #:material iron #:grind-torque 30)
  (block open-rod #:at ((+ (m -3) e) (+ pin-y (/ l 2)) z) #:size (cm 3) #:dimensions ((cm 3) l (cm 3)) #:material iron)
  (piston open-piston #:at ((+ (m -3) e) bottom z) #:bore (cm 5) #:stroke travel #:start (/ (- wrist-y bottom) travel) #:rod-mass 5 #:material iron)
  (steam-cylinder open-cylinder #:piston open-piston #:steam-from open-boiler #:crank open-flywheel)
  (joint open-crank-pin #:kind ball #:a open-flywheel #:b open-rod #:at ((+ (m -3) e) pin-y z))
  (joint open-wrist-pin #:kind pin #:a open-rod #:b open-piston #:at ((+ (m -3) e) wrist-y z) #:axis (0 0 1))
  ;; in the hut
  (boiler hut-boiler #:at ((m 3) 0 (m -1)) #:radius (cm 30) #:height (cm 60) #:water 200 #:fire 5000 #:temperature 150 #:material iron)
  (wheel hut-flywheel #:shape (disc-wheel #:radius (cm 40) #:width (cm 10)) #:at ((m 3) crank-y 0) #:material iron #:grind-torque 30)
  (block hut-rod #:at ((+ (m 3) e) (+ pin-y (/ l 2)) z) #:size (cm 3) #:dimensions ((cm 3) l (cm 3)) #:material iron)
  (piston hut-piston #:at ((+ (m 3) e) bottom z) #:bore (cm 5) #:stroke travel #:start (/ (- wrist-y bottom) travel) #:rod-mass 5 #:material iron)
  (steam-cylinder hut-cylinder #:piston hut-piston #:steam-from hut-boiler #:crank hut-flywheel)
  (joint hut-crank-pin #:kind ball #:a hut-flywheel #:b hut-rod #:at ((+ (m 3) e) pin-y z))
  (joint hut-wrist-pin #:kind pin #:a hut-rod #:b hut-piston #:at ((+ (m 3) e) wrist-y z) #:axis (0 0 1)))
