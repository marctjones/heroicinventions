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
;; The Lonely Rover's 5 kWh bank (18 MJ) would take the single jar about
;; 7,600 years, and the vinegar of about 10,000 jars: a trap that teaches.

(define-machine baghdad-battery
  #:source "Khujut Rabu jars (Parthian/Sasanian); replicas by Eggebrecht and MythBusters"
  (galvanic-jar one-jar #:at ((m -0.35) 0 0))
  (galvanic-jar ten-jars #:at ((m 0.45) 0 0) #:cells 10 #:volts 0.433)
  (galvanic-jar drops #:at ((m -0.35) 0 (m 0.3)) #:electrolyte (L 0.0001))
  ;; Sleep until the drops are spent: 14.9 hours, then its rod is rust
  (wake drops-spent #:when ((drops spent above 99.99)) #:limit 86400))
