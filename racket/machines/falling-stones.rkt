#lang heroic
;; Three 10 cm balls let go together 350 m up -- a cedar ball, an iron ball and one that moves
;; through no air at all -- to watch the air catch them.
;;
;; With drag F = 1/2 rho Cd A v^2 against gravity, a ball's speed is v = vt tanh(g t / vt) and its
;; fall (vt^2 / g) ln cosh(g t / vt), vt = sqrt(2 m g / (rho Cd A)) the speed at which drag balances
;; weight. Here rho = 1.2041 kg/m3 (20 C), Cd = 0.47 (a sphere), A = pi r^2 = 7.854e-3 m2:
;;   cedar (380 kg/m3): m 0.199 kg,  vt 29.64 m/s -- at 6 s it is at 96% of it, 28.5 m/s
;;   iron (7700 kg/m3): m 4.032 kg, vt 133.4 m/s -- at 6 s it has 47 m/s, of the 58.9 the bare fall gives
;;   the ball without a coefficient falls g t: 58.9 m/s at 6 s, whatever it is made of.
(define top (m 350))
(define-machine falling-stones
  #:source "quadratic drag and terminal velocity: Newton, Principia (1687), Book II"
  (ball cedar-ball  #:at ((m -1) top 0) #:radius (cm 5) #:drag-coefficient 0.47 #:material cedar)
  (ball iron-ball   #:at ((m 0)  top 0) #:radius (cm 5) #:drag-coefficient 0.47 #:material iron)
  (ball vacuum-ball #:at ((m 1)  top 0) #:radius (cm 5) #:material iron))
