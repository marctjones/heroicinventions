#lang heroic
;; Three 10 cm balls let go together 350 m up -- a cedar ball, an iron ball and one that moves
;; through no air at all -- to watch the air catch them.
;;
;; With drag F = 1/2 rho Cd A v^2 against gravity, a ball's speed is v = vt tanh(g t / vt) and its
;; fall (vt^2 / g) ln cosh(g t / vt), vt = sqrt(2 m g / (rho Cd A)) the speed at which drag balances
;; weight. Here rho = 1.2041 kg/m3 (20 C), Cd = 0.47 (a sphere), A = pi r^2 = 7.854e-3 m2:
;;   cedar (380 kg/m3): m 0.199 kg,  vt 29.64 m/s -- at 6 s it is at 96% of it, 28.5 m/s
;;   iron (7700 kg/m3): m 4.032 kg, vt 133.4 m/s -- at 6 s it has 55.3 m/s (133.4 tanh 0.441), of the 58.9 the bare
;;                      fall gives (47.0 at 5 s)
;;   the ball without a coefficient falls g t: 58.9 m/s at 6 s, whatever it is made of.
;; They land, the bare ball first, at 8.46 s and 82.8 m/s (sqrt(2 g 349.95)), the iron ball at 8.73 s and 75.4 m/s,
;; and the cedar ball at 13.9 s and 29.6 m/s, its terminal speed, which it has kept for the last 200 m.
;;
;; Laid out to be seen falling (#192): a 10 cm ball is a dot from far enough away to take in 350 m, so the three fall
;; 100 m apart, each in a lane named on the ground in front of it, beside a 350 m pole marked every 100 m, and the
;; camera stands back to take in the whole fall; the balls' labels race down it. The balls fall exactly as they did
;; 2 m apart (the fall is the same to the last bit; after landing they bounce off the ground a few mm differently).
(define top (m 350))
(define-machine falling-stones
  #:source "quadratic drag and terminal velocity: Newton, Principia (1687), Book II"
  (ball cedar-ball  #:at ((m -100) top 0) #:radius (cm 5) #:drag-coefficient 0.47 #:material cedar)
  (ball iron-ball   #:at ((m 0)    top 0) #:radius (cm 5) #:drag-coefficient 0.47 #:material iron)
  (ball vacuum-ball #:at ((m 100)  top 0) #:radius (cm 5) #:material iron)
  ;; what each lane is, on the ground 40 m in front of it (clear of the balls; they stand below the landings on screen,
  ;; which leaves room for the readouts there)
  (post cedar-in-air #:at ((m -100) 0 (m 40)) #:size ((m 1) (m 1) (m 1)) #:material limestone)
  (post iron-in-air  #:at ((m 0)    0 (m 40)) #:size ((m 1) (m 1) (m 1)) #:material limestone)
  (post iron-no-air  #:at ((m 100)  0 (m 40)) #:size ((m 1) (m 1) (m 1)) #:material limestone)
  ;; a pole as high as the balls start, to read the height by, marked every 100 m
  (post 350-m #:at ((m -150) 0 0) #:size ((m 1) top (m 1)) #:material limestone)
  (post 100-m #:at ((m -150) (m 100) 0) #:size ((m 8) (m 1) (m 1)) #:material limestone)
  (post 200-m #:at ((m -150) (m 200) 0) #:size ((m 8) (m 1) (m 1)) #:material limestone)
  (post 300-m #:at ((m -150) (m 300) 0) #:size ((m 8) (m 1) (m 1)) #:material limestone))
