#lang heroic
;; The Kongming sky lantern (issues #111, #125): China, traditionally
;; attributed to Zhuge Liang (Kongming, 181-234 AD) of the Three Kingdoms,
;; who is said to have signalled with lit paper lanterns. The attribution
;; is tradition, not documented fact: I have not found a source from his own
;; time, and it is not settled what his lanterns were (hot-air craft, kites
;; or signal lamps); the name "Kongming lantern" is the later folk one. What
;; is certain is the principle: the sky lantern is the hot-air craft that
;; Chinese tradition places some 1,500 years before the Montgolfiers (1783).
;;
;; Sources (the history is secondary; check before relying on it):
;;   - Joseph Needham, Science and Civilisation in China, vol. 4 (Physics and
;;     Physical Technology), Cambridge, 1962-71: the Chinese sky lantern
;;     (k'ung-ming teng), as a forerunner of the hot-air balloon.
;;   - Zhuge Liang's life: Chen Shou, Records of the Three Kingdoms (3rd c.),
;;     which says nothing of lanterns; the lantern tradition is attached to
;;     his name by later writers.
;;   - The physics, Archimedes' principle for a gas: the lift is the weight of
;;     the air displaced less the weight of the air inside, (rho_out - rho_in)
;;     g V, rho = P / (R_s T), the ideal gas at the air's pressure (any text
;;     on balloon physics; the numbers are worked below).
;;
;; The lantern: a paper envelope of 1 m3, 50 g, open at the foot, with a
;; 20 g burner (a wax block, 10 g of it fuel). It stands on the ground in 15
;; C air (101,325 Pa: rho_out = 101325 / (287.05 x 288.15) = 1.2250 kg/m3).
;; The right-hand twin has no flame: the control.
;;
;; Lift-off. Weight m = 0.070 kg. It leaves the ground when
;; (rho_out - rho_in) V = m, i.e. rho_in = 1.2250 - 0.070 = 1.1550 kg/m3,
;; i.e. T_in = P / (R_s rho_in) = 305.61 K = 32.46 C: the temperature on the
;; issue's prediction, 32.5 C.
;;
;; Heating. The air inside stays at the air's pressure and only its
;; temperature rises, the swollen air leaving by the mouth: so what warms is
;; the air in there now plus the paper,
;;   C(T) dT/dt = Q - UA (T - T_out),  C(T) = rho_in(T) V c_p + m_paper c_paper,
;; with the burner Q = 800 W, the paper's UA = 15 W/K (about 3 m2 of skin at
;; 5 W/m2K), c_p = 1005 J/kgK and c_paper = 1400 J/kgK. C starts at
;; 1.225 x 1005 + 0.05 x 1400 = 1301 J/K and falls to 1226 J/K as the air
;; thins. Integrated (an independent RK4, 1 ms): the inside passes 32.46 C at
;; t = 33.40 s. With UA = 0 it would be the closed form
;; Q t = (P V c_p / R_s) ln(T/T0) + m c_paper (T - T0) = 27.62 s; the skin's
;; loss of 15 W/K x 17.5 K = 262 W, a third of the burner at that point,
;; costs the rest. It would settle at T_out + Q/UA = 68 C if it were not for
;; the flight: the fuel (10 g x 40 MJ/kg = 400 kJ) lasts 500 s.
;;
;; Flight. Once the lift passes 0.070 g it climbs. It is slowed by the air
;; (#33): 1/2 rho_out Cd A v^2 with Cd = 0.8 and the footprint A = V / h =
;; 0.83 m2 of its 1.2 m height, so it climbs at the speed where drag equals
;; lift less weight; as the heat goes up the speed does: 0.82 m/s at 40 C
;; inside, 1.23 m/s at 50 C. Integrating the body and the heating together
;; (python, 1 ms) it is 1 cm off the ground at 34.04 s, 0.5 m at 36.38 s,
;; 2.0 m at 39.99 s and 4.9 m at 45.0 s. Not modelled: the air a rising
;; envelope drags with it (its added mass), and the wind.
;;
;; The control carries no flame, so its inside stays at 15 C and its lift
;; stays 0 against a weight of 0.687 N: it never leaves the ground.
;;
;; On Mars (kongming-lantern-mars) 610 Pa of air at -63 C is 0.0152 kg/m3:
;; the lift can never reach rho_out g V = 0.0152 kg, below the 0.070 kg of
;; the lantern, however hot the inside: it stays on the ground.
(define-machine kongming-lantern
  #:source "Kongming (Zhuge Liang) sky lantern: a paper envelope lifted by its own hot air"
  #:ambient 15
  (envelope lantern #:at (0 0 0) #:volume 1 #:envelope-mass (g 50) #:burner-mass (g 20)
            #:burner-power (W 800) #:fuel (g 10) #:skin-conductance 15
            #:height (m 1.2) #:drag-coefficient 0.8 #:material hemp)
  (envelope control #:at ((m 3) 0 0) #:volume 1 #:envelope-mass (g 50) #:burner-mass (g 20)
            #:burner-power 0 #:fuel (g 10) #:skin-conductance 15
            #:height (m 1.2) #:drag-coefficient 0.8 #:material hemp))
