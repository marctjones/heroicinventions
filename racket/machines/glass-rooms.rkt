#lang heroic
;; Glass walls on Mars (issue #57): four rooms, each 3 x 2.5 x 4 m, holding
;; 13.5 kPa of 21/79 air at 20 °C, walls losing 20 W/K, at Meridiani at
;; noon (the sun held still: 432.7 W/m² of beam, 80.71° up, so a roof takes
;; 432.7 x sin 80.71° = 427.0 W/m²). Worked out before running:
;;
;; membrane  no glass: the habitat's membranes are opaque. No sunlight gets
;;           in, and it cools to the outside's -63 °C.
;; glazed    a roof of 36 panes of silica glass, 30 cm square (3.24 m²) and
;;           7.5 mm thick. They pass 0.9 x 427.0 x 3.24 = 1,245 W, and the
;;           room settles at -63 + 1245/20 = -0.74 °C, within C/UA =
;;           166 mol x 20.84 J/(mol K) / 20 = 173 s. A 30 cm pane 7.5 mm
;;           thick cracks at q = 7 MPa x (0.0075/0.3)² / 0.29 = 15.09 kPa
;;           across it; the most it bears is 13.5 - 0.61 = 12.89 kPa: it holds.
;; dark      the same roof in basalt glass, 0.05 of the sun through: 69 W,
;;           settling at -63 + 69.2/20 = -59.5 °C. Glass from Mars's common
;;           sand is nearly a membrane.
;; thin      the same roof, 5 mm thick: it cracks at 7e6 x (0.005/0.3)² / 0.29
;;           = 6.71 kPa, and 12.89 kPa is across it from the start. It cracks
;;           at once, and the room's air is gone through 3.24 m² of holes.
;;           (Thickness needed at 13.5 kPa: a √(0.29 q/σ) = 7.1 mm for a
;;           30 cm pane, 2.4 cm for a 1 m pane.)
(define-machine glass-rooms
  #:source "Glass walls: light in, and the pressure a pane can bear (Roark)"
  #:planet mars
  #:latitude -2 #:day 100 #:time 12
  (enclosure membrane #:at ((m -9) 0 0) #:size ((m 3) (m 2.5) (m 4))
             #:pressure 13500 #:air '((o2 0.21) (n2 0.79)) #:temperature 20 #:insulation 20)
  (enclosure glazed #:at ((m -3) 0 0) #:size ((m 3) (m 2.5) (m 4))
             #:pressure 13500 #:air '((o2 0.21) (n2 0.79)) #:temperature 20 #:insulation 20)
  (pane glazed-roof #:at ((m -3) (m 2.5) 0) #:on glazed #:side (cm 30) #:thickness (mm 7.5) #:count 36 #:glass silica)
  (enclosure dark #:at ((m 3) 0 0) #:size ((m 3) (m 2.5) (m 4))
             #:pressure 13500 #:air '((o2 0.21) (n2 0.79)) #:temperature 20 #:insulation 20)
  (pane dark-roof #:at ((m 3) (m 2.5) 0) #:on dark #:side (cm 30) #:thickness (mm 7.5) #:count 36 #:glass basalt)
  (enclosure thin #:at ((m 9) 0 0) #:size ((m 3) (m 2.5) (m 4))
             #:pressure 13500 #:air '((o2 0.21) (n2 0.79)) #:temperature 20 #:insulation 20)
  (pane thin-roof #:at ((m 9) (m 2.5) 0) #:on thin #:side (cm 30) #:thickness (mm 5) #:count 36 #:glass silica))
