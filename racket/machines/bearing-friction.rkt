#lang heroic
;; Friction and wear in a pendulum's bearing. Three identical iron
;; pendulums, 1 m long, each hung on a 3 cm iron pin, all let go from 15°.
;; Rod and ball weigh 18.9 kg together, I = 17.36 kg m² about the pin, and
;; their centre of mass hangs d = 0.936 m below it: a 1.99 s swing.
;;
;; perfect: a frictionless pin. It keeps swinging to 15° for ever.
;;
;; greased: the pin turns in thick grease, which drags with c·ω, c = 0.6
;; N m s/rad. Like any lightly damped oscillator its swings die away inside
;; the envelope 15° × exp(−c t / 2I): halved every 40 s.
;;
;; dry: bare iron on iron, μ = 0.4. The pin carries the pendulum's weight,
;; N = m g = 186 N, so friction resists with μ N r = 1.11 N m at any speed.
;; Each half-swing loses the same angle, about 2 μ r / d = 0.73°, so the
;; swings shrink in a straight line, not a curve; after 20 of them gravity
;; can no longer pull it past the pin's grip and it stops, a fraction of a
;; degree off plumb. All 5.9 J of its swing become heat in the pin, and the
;; pin wears by Archard's law, V = K N s: K = 1e-4 mm³ per N m for dry
;; iron, s the 8 cm its surface slides in the eye, 0.0015 mm³ in all.
(define-machine bearing-friction
  #:source "Coulomb and viscous friction in a pendulum's bearing, and the wear it causes"
  (pendulum perfect #:at ((m -0.8) (m 1.3) 0) #:length (m 1) #:material iron #:start-angle-deg 15
            #:bearing-radius (cm 1.5))
  (pendulum greased #:at (0 (m 1.3) 0) #:length (m 1) #:material iron #:start-angle-deg 15
            #:bearing-radius (cm 1.5) #:bearing-drag 0.6)
  (pendulum dry #:at ((m 0.8) (m 1.3) 0) #:length (m 1) #:material iron #:start-angle-deg 15
            #:bearing-radius (cm 1.5) #:bearing-mu 0.4 #:bearing-wear 1e-4))
