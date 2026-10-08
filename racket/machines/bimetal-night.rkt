#lang heroic
;; The bimetal thermostat on the heat bin's lid (issue #97): night-heat's tight vault with the ideal switch replaced by a strip of
;; brass bonded to steel, 100 mm x 10 mm x 1 mm, clamped at one end against the battery bank. Three vaults, each a 0.5 m cavity in a
;; 0.5 m regolith wall at the ground's -55 C, a 16 kg bank (16 kJ/K), 40 kg of basalt at 200 C in a bin whose lid leaks 0.1 W/K,
;; from 17:00 (the sun has set at 18:00); 03:00 is 10 local hours (3,699 s each) on: 36,990 s. The vault, rock and wall numbers are
;; night-heat's (see its header).
;;
;; ideal   the stand-in of #71: a switch that senses the bank itself, opens the lid at 5 C and shuts it at 40 C, with hysteresis
;; strip   a bimetal strip senses the bank and works the lid in proportion
;; warm    the strip again in a vault whose wall is already warm (+10 C) with the bank at +30 C, to show the strip shutting the lid
;;
;; The strip (all worked before running; the C# sim prints the same)
;;   layers  brass (alpha 19.9e-6 /K, E 110 GPa, rho 8,530, c 380) bonded to steel (alpha 11.7e-6, E 200 GPa, rho 7,850, c 490), 0.5 mm
;;           each: m = t1/t2 = 1, n = E1/E2 = 200/110 = 1.818 (layer 1 the one that expands less: the steel)
;;   Timoshenko's curvature   kappa = 6 d_alpha dT (1+m)^2 / (t [3 (1+m)^2 + (1+mn)(m^2 + 1/(mn))])
;;           = 6 x 8.2e-6 dT x 4 / (0.001 [12 + 2.818 x 1.550]) = 1.968e-4 / 0.016368 = 0.012023 /m per K
;;           (equal layers of equal modulus would give 3 d_alpha dT / (2 t) = 0.01230 /m per K; the unequal moduli lower it by 2.3%)
;;   tip     delta = (1 - cos kappa L) / kappa, which is kappa L^2 / 2 for a small bend: 0.01202 x 0.1^2 / 2 = 60.12 um per K,
;;           so 1.20 mm at 20 K above its flat temperature (and 3 d_alpha dT L^2 / (4 t) = 52.5 um/K for equal layers and moduli)
;;   lid     shut at 40 C, wide open after 2.1 mm of tip movement: 2.1 mm / 60.12 um/K = 34.94 K (the arc is a hair under linear), so the lid is wide open at 5.06 C
;;           and open by (40 - T)/34.94 between (0.5 open at 22.5 C)
;;   tau     C = (rho c) mean x V = (0.5 x 8530 x 380 + 0.5 x 7850 x 490) J/(m3 K) x 1e-6 m3 = 3.544 J/K; surface 2 (LW + Lt + Wt) = 2.22e-3 m2;
;;           against a clamped film h = 10 W/(m2 K): tau = C / (h A) = 3.544 / 0.0222 = 159.6 s. The strip trails a bank warming at r K/s by r tau.
;;
;; Worked before running: night 1, the bank at 03:00 (strip vault)
;;   The bank starts at -55 C and has only risen to +4.3 C at 03:00 in the ideal vault (night-heat: model -1.3, trace +4.3), always under
;;   the strip's wide-open temperature of 5.06 C. The strip, trailing a bank that warms at r by r tau (13.9 K/h at 1 h: 0.62 K), is therefore
;;   wide open (opening 1.0) the whole night, the lid does what the ideal switch's does, and the bank is the same 4.3 C at 03:00.
;;   Traced: ideal bank 4.28, strip bank 4.28 (the lid is open in both for the whole night), strip 4.27 (0.01 K behind: the bank is warming at
;;   0.3 K/h by then), strip lag 0.62 K at 1 h against r tau = 13.9/3600 x 159.6 = 0.62 K.
;;   The strip bends toward the brass when colder than flat at 20 C: at the start (-55 C) dT = -75 K gives a tip 4.51 mm back from flat
;;   (kappa L = 0.0902); at 03:00 (4.27 C) dT = -15.7 K gives 60.12 um/K x -15.7 = -0.95 mm (traced -0.95); the lid shut at 40 C (dT 20 K) is +1.20 mm.
;;   So the tip would travel 2.1 mm from 5.07 C to 40 C, and tonight it travels only from -4.51 to -0.95 mm, always on the lid-open side of it.
;;   The bank must be at 0 C or more at 03:00 to charge (it charges only at 0-45 C: Battery University BU-410): 4.28 C, yes.
;;
;; Worked before running: the warm vault, in which the strip must shut the lid. The wall and the ground are at +10 C (a vault warmed through a
;; week of fires), the bank at +30 C, the rock 200 C: the lid starts 29% open ((40 - 30) / 34.94) and the strip shuts it as the bank warms.
;; Shut, the rock can give the cavity only L (T_rock - T) = 0.1 x (150 - 38) = 11 W through the lid, and open it gives the cavity
;; 0.8798 x 0.3451 x sigma (T_rock^4 - T_cavity^4) = 391 W at a rock of 150 C and a cavity of 38 C (423.15 K, 311.15 K). The wall draws, at
;; hour 5 (18,000 s), A I dT / sqrt(pi t) = 1.5 x 216.3 x 28.6 / sqrt(pi x 18,000) = 39 W for a surface 28.6 K over the wall's 10 C. For the
;; lid to give that, f = (39 - 11) / (391 - 11) = 0.074 open, so the bank settles where the strip is 7.4% open: 40 - 0.074 x 34.94 = 37.4 C.
;; Traced: 38.0 C at 5 h (lid 6% open, rock 151.7 C, exchange 33.5 W), 38.2 C at the peak, 15 h: a proportional strip, so the bank sits 1.8 K
;; under the lid's shut temperature (a droop) and never nears the 45 C a lithium bank may charge at, however long it runs.

(define-machine bimetal-night
  #:source "Bimetal strip: the default thermostat for a heat bin"
  #:planet mars
  #:latitude -2 #:day 100 #:time 17
  #:weather (weather #:passes '(3 15) #:pass-minutes 10)
  ;; ideal: the stand-in switch, 5 / 40 C
  (enclosure ideal #:at ((m -2) 0 0) #:size ((m 0.5) (m 0.5) (m 0.5)) #:pressure 610 #:temperature -55
             #:wall regolith #:wall-thickness 0.5 #:ground -55)
  (heat-store ideal-bank #:at ((m -2.12) 0 0) #:mass 16 #:contents cells #:temperature -55)
  (heat-store ideal-rock #:at ((m -1.88) 0 0) #:mass 40 #:contents basalt #:temperature 200)
  (heat-bin ideal-bin #:at ((m -1.88) 0 0) #:holds ideal-rock #:leak 0.1 #:sense ideal-bank)
  ;; strip: a brass-steel strip on the bank works the lid
  (enclosure strip #:at (0 0 0) #:size ((m 0.5) (m 0.5) (m 0.5)) #:pressure 610 #:temperature -55
             #:wall regolith #:wall-thickness 0.5 #:ground -55)
  (heat-store strip-bank #:at ((m -0.12) 0 0) #:mass 16 #:contents cells #:temperature -55)
  (heat-store strip-rock #:at ((m 0.12) 0 0) #:mass 40 #:contents basalt #:temperature 200)
  (heat-bin strip-bin #:at ((m 0.12) 0 0) #:holds strip-rock #:leak 0.1)
  (bimetal strip-strip #:at ((m -0.19) (m 0.46) 0) #:senses strip-bank #:drives strip-bin #:layers (brass steel))
  ;; warm: the bank starts at +30 C
  (enclosure warm #:at ((m 2) 0 0) #:size ((m 0.5) (m 0.5) (m 0.5)) #:pressure 610 #:temperature 10
             #:wall regolith #:wall-thickness 0.5 #:ground 10)
  (heat-store warm-bank #:at ((m 1.88) 0 0) #:mass 16 #:contents cells #:temperature 30)
  (heat-store warm-rock #:at ((m 2.12) 0 0) #:mass 40 #:contents basalt #:temperature 200)
  (heat-bin warm-bin #:at ((m 2.12) 0 0) #:holds warm-rock #:leak 0.1)
  (bimetal warm-strip #:at ((m 1.81) (m 0.46) 0) #:senses warm-bank #:drives warm-bin #:layers (brass steel)))
