#lang heroic
;; A winter night at -10 C. A copper of 5 kg of water, taken off the fire
;; at 90 C, cools towards the air by Newton's law: it loses 2 W for every
;; kelvin it is warmer, and holds 5 x 4186 J/K, so it falls as
;;   T = -10 + 100 exp(-t / 10465 s):  60.9 C after an hour, 40.3 C after two
;; (in a 20 C room it would be 69.6 C and 55.2 C).
;;
;; Beside it a cistern, 1 m2 and half full, freezes over. Water under ice
;; stays at 0 C; each new layer's latent heat has to leave by conduction up
;; through the ice already there, so the ice thickens as the square root of
;; time (Stefan, 1891): h = sqrt(2 k (0 - T) t / (rho_ice L_f))
;;   = sqrt(2 x 2.22 x 10 x 3600 / (917 x 334000)) = 22.8 mm after an hour,
;; twice that after four. A shallow basin with a seep evaporates nothing
;; while it is iced over.
(define-machine winter-night
  #:source "Newton's law of cooling (1701); Stefan's problem (1891)"
  #:ambient -10
  (post hob #:at (0 0 0) #:size ((m 0.5) (m 0.4) (m 0.5)) #:material limestone)
  (boiler copper #:at (0 (m 0.4) 0) #:radius (m 0.15) #:height (m 0.3) #:water (kg 5) #:temperature 90 #:fire 0 #:material bronze)
  (tank cistern #:at ((m 1.5) 0 0) #:area (m2 1) #:height (m 1) #:water (L 500) #:material oak)
  (tank basin #:at ((m -1.5) 0 0) #:area (m2 1) #:height (m 0.2) #:water (L 100) #:material limestone)
  (leak seep #:on basin #:height 0 #:evaporation (L/s 0.001)))
