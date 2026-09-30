#lang heroic
;; Two inflated modules on Mars (issue #39), each holding its own air: 50 kPa
;; of 21% oxygen and 79% nitrogen, at 20 °C, against the planet's 610 Pa of
;; CO2 at -63 °C outside. Every part inside reads the module's air, not
;; Mars's. Worked out before running:
;;
;; sealed     3 x 4 x 5 m (60 m³). Its walls lose 20 W/K, and a 1.8 kW heater
;;            warms it, so it settles at T_out + Q/UA = -63 + 1800/20 = 27 °C.
;;            It holds n = PV/RT = 50000 x 60 / (8.314 x 293.15) = 1230.9 mol
;;            of gas, whose heat capacity n c_v = 1230.9 x 20.84 = 25.65 kJ/K
;;            sets the time constant C/UA = 1283 s: T = 27 - 7 e^(-t/1283),
;;            26.58 °C after an hour. Sealed, its pressure follows its
;;            temperature: 50 x 300.15/293.15 = 51.19 kPa once settled.
;;   pump     a lift pump inside it, its barrel 2.5 m over a well. On Mars in
;;            the open it could not hold water at all (earth-machines-on-mars);
;;            in here the air holds a column (P - P_v)/(ρ g) =
;;            (50000 - 2339)/(1000 x 3.71) = 12.85 m -- farther than on
;;            Earth, since Mars pulls the water down less -- and it lifts
;;            A S η = 7.07 L a stroke into the trough.
;;   locker   a 1 m³ box standing inside it, holding pure nitrogen at
;;            100 kPa, with a pinhole (0.1 cm²): its gas leaks into the
;;            module round it, not onto Mars, and the two together never
;;            lose a gram.
;; punctured  3 x 2.5 x 4 m (30 m³), no heater and walls that pass no heat,
;;            with a 1 cm² puncture. Its air rushes out at the speed of sound
;;            (choked: Mars's 610 Pa is far under the critical 0.528 of the
;;            pressure inside), and the air left behind stays at 20 °C (a
;;            slow leak, isothermal), so the pressure falls exponentially:
;;            τ = V / (Cd A √(γ R T) (2/(γ+1))^((γ+1)/(2(γ-1)))), with
;;            γ = 1.399 for the mix, R = 8.314/0.028851 = 288.2 J/(kg K):
;;            τ = 30 / (0.6 x 1e-4 x 343.77 x 0.5788) = 2513 s. After half
;;            an hour 50 e^(-1800/2513) = 24.43 kPa; after an hour 11.94 kPa.
;;            It stays choked until the air inside is down to
;;            610/0.5285 = 1.15 kPa, 2.6 hours in. Its oxygen stays 21%:
;;            what leaks out is its own mixture.
(define-machine two-modules
  #:source "Inflatable habitat modules: Dalton's law, Newton's cooling and a choked orifice"
  #:planet mars
  (enclosure sealed #:at ((m -4) 0 0) #:size ((m 3) (m 4) (m 5))
             #:pressure (kPa 50) #:air '((o2 0.21) (n2 0.79)) #:temperature 20
             #:insulation 20 #:heater 1800)
  (tank well #:at ((m -4.8) 0 (m -1)) #:area (m2 0.5) #:height (m 1) #:water (L 400) #:material limestone)
  (post pier #:at ((m -3.3) 0 (m -1)) #:size ((m 0.5) (m 2.5) (m 0.5)) #:material limestone)
  (tank trough #:at ((m -3.3) (m 2.5) (m -1)) #:area (m2 0.25) #:height (m 0.5) #:material oak)
  (pump pump #:at ((m -4.3) (m 3.3) (m -1)) #:from well #:to trough #:bore (cm 15) #:stroke (cm 50) #:rpm 20)
  (enclosure locker #:at ((m -4) 0 (m 1.5)) #:size ((m 1) (m 1) (m 1))
             #:pressure (kPa 100) #:air '((n2 1)) #:temperature 20 #:leak (cm2 0.1))
  (enclosure punctured #:at ((m 4) 0 0) #:size ((m 3) (m 2.5) (m 4))
             #:pressure (kPa 50) #:air '((o2 0.21) (n2 0.79)) #:temperature 20
             #:insulation 0 #:leak (cm2 1)))
