#lang heroic
;; An airlock on Mars (issue #41): a habitat of 50 kPa air (21% O2, 79% N2,
;; 20 °C) and an 8 m³ chamber (2 x 2 x 2 m) between an inner door into it
;; and an outer door onto Mars (610 Pa of CO2). Its walls pass no heat, so
;; the gas stays at 20 °C throughout. One cycle out, worked out before
;; running (the rover works the doors and pump; see the test's timings):
;;
;; pump down  the air pump draws the chamber's air back into the habitat,
;;            sweeping 50 L/s: the chamber falls as 50 e^(-0.05 t / 8) kPa and
;;            its pressure switch stops it at 5 kPa after 8/0.05 x ln 10 =
;;            368.4 s. The habitat, 60 m³, rises 45 kPa x 8/60 = 6 kPa to
;;            56 kPa. As an ideal isothermal compressor pushing the air up
;;            from the chamber into the habitat it spends
;;            ∫ V ln(P_hab/P) dP = 288.7 kJ (267.9 kJ if the habitat were so
;;            big its pressure never rose).
;; bleed      a 5 cm² valve then lets the chamber down to Mars's pressure.
;;            Choked (610 Pa is well under the critical 0.528 of 5 kPa), it
;;            falls as e^(-t/τ), τ = 8/(0.6 x 5e-4 x 343.77 x 0.5788) =
;;            134.0 s: 2.371 kPa after 100 s.
;; lost       what goes out onto Mars in a cycle is the chamber's volume
;;            times the density of the gas left in it, above what Mars's own
;;            pressure holds in: 8 x (5000 - 610) x 0.028851 / (8.314 x
;;            293.15) = 0.4157 kg. Vented straight from 50 kPa it would be
;;            4.68 kg: pumping down saves 91% of the gas.
(define-machine airlock
  #:source "An airlock: pumping down, venting, and the gas each cycle costs"
  #:planet mars
  (enclosure habitat #:at ((m -5) 0 0) #:size ((m 5) (m 3) (m 4))
             #:pressure (kPa 50) #:air '((o2 0.21) (n2 0.79)) #:temperature 20 #:insulation 0)
  (enclosure chamber #:at ((m -1.5) 0 0) #:size ((m 2) (m 2) (m 2))
             #:pressure (kPa 50) #:air '((o2 0.21) (n2 0.79)) #:temperature 20 #:insulation 0)
  (door inner #:at ((m -2.5) 0 0) #:from habitat #:to chamber #:area (m2 1.6))
  (door outer #:at ((m -0.5) 0 0) #:from chamber #:to outside #:area (m2 1.6))
  (door bleed #:at ((m -0.5) (m 1.6) (m 0.6)) #:from chamber #:to outside #:area (cm2 5))
  (air-pump pump #:at ((m -2.5) (m 1.6) (m -0.6)) #:from chamber #:to habitat #:speed (L/s 50) #:until (kPa 5)))
