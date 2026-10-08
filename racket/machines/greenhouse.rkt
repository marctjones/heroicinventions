#lang heroic
;; A greenhouse on Mars (issue #42): water, trees, oxygen for fire, and an
;; electrolyser. The rover doesn't breathe; an enclosure's air matters for
;; plants and fire. The numbers below hold the sun at noon (the test does;
;; in play the sols turn and the trees grow only by day). Worked out before
;; running:
;;
;; greenhouse  30 m³ under a roof of 36 silica panes (3.24 m², 30 cm square and
;;             10 mm thick, good to 26.8 kPa across them), 13.5 kPa of
;;             21% O2, 49% N2, 30% CO2 (pumped in from Mars's air) at 20 °C:
;;             166.2 mol, 1.117 kg of O2. The noon sun through the glass,
;;             0.9 x 427.0 W/m² x 3.24 = 1,245 W, and a 3 kW heater against
;;             walls losing 50 W/K hold it at -63 + 4245/50 = 21.9 °C.
;; trees       10 m² of coppiced willow keeping 0.5% of that light as wood at
;;             18 MJ/kg: 0.005 x 1245.2 / 18e6 = 3.459e-7 kg/s, less
;;             respiration of 0.01 g/(m² h), 2.78e-8 kg/s: 3.181e-7 kg/s net.
;;             Over 30 sols (2,663,250 s) 0.847 kg of wood, and, as cellulose
;;             (6 CO2 + 5 H2O -> C6H10O5 + 6 O2, 1.1841 kg of O2 a kg),
;;             1.003 kg of O2: the greenhouse's oxygen nearly doubles.
;; stove       then the harvest is stacked on the stove and burned at 1 kW:
;;             0.847 kg x 15 MJ/kg = 12.7 MJ in 12,708 s. Burning takes back
;;             exactly the O2 growing gave and returns the CO2, so both come
;;             back to where they started, but for what the trees have grown
;;             since the harvest: O2 up, CO2 down, by exactly 1.1841 and 1.6284
;;             times the wood then standing.
;; coppiced   the harvest is cut by hand (click the trees: trees.harvest, kg
;;             onto the stove; the stove is lit by clicking it too), and the
;;             demo operator does it 600 s in, with the sun held where it is
;;             at noon: 3.181e-7 x 600 = 1.909e-4 kg of wood, which the stove
;;             then burns at 1 kW: 1.909e-4 x 15 MJ/kg = 2.863 kJ in 2.86 s,
;;             taking back the 1.1841 x 1.909e-4 = 2.26e-4 kg of O2 growing it
;;             gave. The trees begin again from nothing. (The wood is so little
;;             because 600 s is so short; 30 sols of it is the 0.847 kg above.)
;; melter      an ice drill and melter, 1 kW, cutting ice at -63 °C:
;;             2100 x 63 + 334,000 = 466.3 kJ/kg, 7.72 kg an hour; its 100 L
;;             tank, in the warm greenhouse (outside, the meltwater would
;;             freeze again), is full after 46,631 s.
;; cell        an electrolyser at 500 W and 70% in a sealed 8 m³ cell:
;;             350 W / 17.875 MJ/kg = 70.5 g of O2 an hour. Burning a kg of
;;             wood with oxygen made so costs 1.1841 x 17.875/0.7 = 30.3 MJ of
;;             electricity for 15-18 MJ of heat: allowed, and a poor trade.
(define-machine greenhouse
  #:source "A greenhouse on Mars: trees, oxygen, wood, meltwater and an electrolyser"
  #:planet mars
  #:latitude -2 #:day 100 #:time 12
  (enclosure house #:at (0 0 0) #:size ((m 3) (m 2.5) (m 4))
             #:pressure 13500 #:air '((o2 0.21) (n2 0.49) (co2 0.3)) #:temperature 20
             #:insulation 50 #:heater 3000 #:material glass)
  (pane glazing #:at (0 (m 2.5) 0) #:on house #:side (cm 30) #:thickness (mm 10) #:count 36 #:glass silica)
  (tank cistern #:at ((m 1) 0 (m 1.4)) #:area (m2 0.5) #:height (m 0.5) #:water (L 100) #:material limestone)
  (hearth stove #:at ((m 1) 0 (m -1.4)) #:heats house #:power 1000 #:fuel 0 #:fuel-kind wood)
  (plants trees #:at ((m -0.5) 0 0) #:area 10 #:water cistern #:store stove)
  (tank meltwater #:at ((m -1) 0 (m 1.3)) #:area (m2 0.5) #:height (m 0.2) #:material limestone)
  (melter drill #:at ((m -1) 0 (m 0.6)) #:into meltwater #:power 1000 #:ice-temperature -63)
  (enclosure cell #:at ((m -4) 0 0) #:size ((m 2) (m 2) (m 2))
             #:pressure 13500 #:air '((o2 0.21) (n2 0.79)) #:temperature 20 #:insulation 0 #:material iron)
  (tank feed #:at ((m -4) 0 (m -0.5)) #:area (m2 0.1) #:height (m 0.3) #:water (L 20) #:material iron)
  (electrolyser splitter #:at ((m -4) 0 (m 0.5)) #:water feed #:power 500 #:efficiency 0.7)
  ;; the trees coppiced onto the stove (issue #157), every kilogram standing (it cuts only what has grown); taking any control stops it
  (operator (at 600 (trees harvest 1000))))
