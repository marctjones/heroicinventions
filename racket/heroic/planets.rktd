;; Planet table: the source of truth for every planet a scene can stand on
;; (issue #38). A planet is numbers fed into the game's unchanged formulas,
;; and nothing else. Read by racket/heroic/planets.rkt (define-machine's
;; #:planet) and, embedded as it is, by the C# sim (Planet.cs), whose
;; editor's (planet mars) command needs the presets too.
;;
;; Fields (SI; temperatures °C):
;;   gravity            m/s²
;;   pressure           Pa, absolute, at the surface
;;   temperature        °C, a scene's air when it gives no #:ambient
;;   air                volume (mole) fractions of O2, N2, CO2, H2O, Ar; sum 1
;;   molar-mass         kg/mol of the air, or #f to take the mixture's mean.
;;                      Earth gives it: 8.314 / 287.05, the dry-air gas
;;                      constant the game has always used, so every Earth
;;                      scene is unchanged to the last digit.
;;   solar-constant     W/m² above the air, at the planet's mean distance
;;   sky-transmittance  clear-sky beam: DNI = S · T^(AM^k), AM the air mass
;;   air-mass-exponent  k in that (Meinel & Meinel, 1976, on Earth: 0.7, 0.678)
;;   sol                s in a solar day
;;   year               sols in a year
;;   obliquity          degrees of axial tilt (sets the sun's declination)
;;   sky-color, ground-color   r g b, 0..1: how the view tints the scene
;;
;; Mars: NASA Mars fact sheet (nssdc.gsfc.nasa.gov/planetary/factsheet/
;; marsfact.html): 586.2 W/m², a solar day of 24.6597 h = 88,775 s, 686.98
;; Earth days = 668.6 sols, obliquity 25.19°, CO2 95.1%, N2 2.59%, Ar 1.94%,
;; O2 0.16%, CO 0.06%, H2O 210 ppm, mean molecular weight 43.49 g/mol. The
;; CO is folded into the CO2 (the sim tracks five gases) and O2 is 0.17%,
;; the figure the design uses; the mixture's mean is then 43.49 g/mol, as
;; the fact sheet has it. Gravity 3.71 m/s² (equatorial; the sheet's mean
;; is 3.73), 610 Pa and -63 °C are the design's numbers (the sheet: 636 Pa
;; mean, variable 400-870 with the season; -59 °C average). The clear-sky
;; beam on Mars is dust's: Beer-Lambert, e^(-tau·AM), k = 1, with tau = 0.3
;; for a clear season (Lemmon et al. 2015, Icarus: tau 0.2 to 2 at the MER
;; sites, highest in global storms), so T = e^-0.3 = 0.741.
(earth (name "Earth") (gravity 9.81) (pressure 101325) (temperature 20)
       (air (o2 0.2095) (n2 0.7808) (co2 0.0004) (h2o 0) (ar 0.0093))
       (molar-mass 0.028963595192475)
       (solar-constant 1361) (sky-transmittance 0.7) (air-mass-exponent 0.678)
       (sol 86400) (year 365) (obliquity 23.45)
       (sky-color 0.55 0.7 0.9) (ground-color 0.55 0.53 0.5))
(mars  (name "Mars") (gravity 3.71) (pressure 610) (temperature -63)
       (air (o2 0.0017) (n2 0.0259) (co2 0.9527) (h2o 0.0003) (ar 0.0194))
       (molar-mass #f)
       (solar-constant 586.2) (sky-transmittance 0.741) (air-mass-exponent 1)
       (sol 88775) (year 669) (obliquity 25.19)
       (sky-color 0.78 0.6 0.45) (ground-color 0.6 0.36 0.22))
