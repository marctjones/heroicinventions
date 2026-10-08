#lang heroic
;; The "Baghdad battery": clay jars from Khujut Rabu near Ctesiphon
;; (Parthian or Sasanian), each holding a copper tube 26 mm across and
;; 9 cm tall round an iron rod, sealed with bitumen. Wilhelm König took
;; them for galvanic cells in 1938; most archaeologists doubt it (no wires,
;; a seal that shuts the electrolyte in, no plated work of the period).
;; Replicas do make electricity, and that is what is built here.
;;
;; Numbers (sourced; no chemistry or circuit is simulated):
;;   one jar     Eggebrecht's replica in 5% vinegar: 0.5 V at 0.15 mA,
;;               75 µW. Its 45 mL of vinegar holds 2.25 g of acetic acid,
;;               0.0375 mol, so it passes 3,615 C (1.8 kJ) over 279 days.
;;   ten jars    MythBusters (2005) put ten replicas in series and read
;;               4.33 V: 0.433 V a jar. Same current, so 649.5 µW.
;;   drops       a jar with 0.1 mL left: 8 C, spent in 14.9 hours. Its
;;               rod rusts as it goes (Sleep until, or 20x, to watch).
;;   lamp        each row is wired to a small lamp (#176) so the current can be seen.
;;               Its filament radiates what it is fed, so T goes as P^(1/4): the lamp is
;;               rated at the ten-jar stack's 649.5 uW and 1,500 C, T = 1773.15 K (P /
;;               649.5 uW)^(1/4): one jar 75 uW -> 760.5 C (dull red), ten jars 1,500 C
;;               (white-yellow), the drops jar the same as one jar until it is spent
;;               at 53,558 s and then cold (20 C). Scaled for the eye: 75 uW would not
;;               really warm a filament. The trace has it as <jar>.filament.
;; The Lonely Rover's 5 kWh bank (18 MJ) would take the single jar about
;; 7,600 years, and the vinegar of about 10,000 jars: a trap that teaches.

(define-machine baghdad-battery
  #:source "Khujut Rabu jars (Parthian/Sasanian); replicas by Eggebrecht and MythBusters"
  (galvanic-jar one-jar #:at ((m -0.35) 0 0))
  (galvanic-jar ten-jars #:at ((m 0.45) 0 0) #:cells 10 #:volts 0.433)
  (galvanic-jar drops #:at ((m -0.35) 0 (m 0.3)) #:electrolyte (L 0.0001))
  ;; Sleep until the drops are spent: 14.9 hours, then its rod is rust
  (wake drops-spent #:when ((drops spent above 99.99)) #:limit 86400))
