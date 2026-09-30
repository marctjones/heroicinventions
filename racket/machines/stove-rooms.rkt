#lang heroic
;; Oxygen-limited fire (issue #40): the same charcoal stove, 2 kW, three
;; times over. A fire breathes its zone's air: charcoal burns C + O2 -> CO2,
;; 32/12 = 2.667 kg of oxygen a kilogram, 2000 / 29e6 = 6.897e-5 kg/s of
;; charcoal, so 5.742e-3 mol of O2 a second. It goes out below 15% oxygen.
;; Worked out before running (20 °C, 101.3 kPa of Earth's air outside):
;;
;; sealed    a 30 m³ room (3 x 2.5 x 4 m) holding PV/RT = 1247.2 mol, 20.95%
;;           of it oxygen, 261.3 mol. The fire can have what is above 15%:
;;           261.3 - 0.15 x 1247.2 = 74.21 mol, in 74.21 / 5.742e-3 =
;;           12,924 s (3.6 h), having burned 0.891 kg of its 3 kg. Charcoal
;;           swaps each O2 for a CO2, so the room's moles never change, and
;;           when it goes out the air is 5.99% CO2. The stove heats the room,
;;           2 kW against walls losing 50 W/K: it settles at 20 + 2000/50 =
;;           60 °C within about C/UA = 26 kJ/K / 50 = 520 s, and its pressure
;;           rises with its temperature, 101.3 x 333.15/293.15 = 115.15 kPa
;;           (Heron's temple doors, in a room).
;; supplied  the same room with a bellows blowing in 5 L/s of outside air and
;;           a 20 cm² vent: 0.2079 mol/s in, of which 20.95% oxygen, against
;;           the fire's 5.742e-3 mol/s, so the oxygen settles at
;;           20.95% - 5.742e-3/0.2079 = 18.19%, above the limit: it burns
;;           until its fuel is gone.
;; outdoors  in the open air, which never runs short: 8 hours burn
;;           28,800 x 6.897e-5 = 1.986 kg, leaving 1.014 kg.
(define-machine stove-rooms
  #:source "A charcoal stove in a sealed room, a ventilated room and the open air"
  (enclosure sealed #:at ((m -5) 0 0) #:size ((m 3) (m 2.5) (m 4)) #:insulation 50 #:material oak)
  (hearth stove #:at ((m -5) 0 0) #:heats sealed #:power 2000 #:fuel 3 #:fuel-kind charcoal)
  (enclosure supplied #:at (0 0 0) #:size ((m 3) (m 2.5) (m 4)) #:insulation 50
             #:supply (L/s 5) #:leak (cm2 20) #:material oak)
  (hearth stove2 #:at (0 0 0) #:heats supplied #:power 2000 #:fuel 3 #:fuel-kind charcoal)
  (boiler pot #:at ((m 5) (m 0.3) 0) #:radius (m 0.15) #:height (m 0.3) #:water (kg 10))
  (hearth stove3 #:at ((m 5) 0 0) #:heats pot #:power 2000 #:fuel 3 #:fuel-kind charcoal))
