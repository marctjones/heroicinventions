#lang heroic
;; Branca's steam wheel with no fire: four mirrors on the ground throw
;; midsummer noon sunlight onto a blackened pot, and its steam turns the
;; paddle wheel. Salomon de Caus used the sun the same way in 1615, to warm
;; the air in a vessel and push water up a fountain.
;;
;; Four 1 m2 mirrors at 85%, at Alexandria's latitude at midsummer noon,
;; each deliver sunlight x area x reflectivity x the cosine of its angle:
;; together about 2.5 kW, more than the fire under the other wheel's pot.
;; Traced: the pot boils within five minutes from cold, settles at 113 C and
;; 58 kPa, and the jet (359 m/s, 1.1 g/s) turns the wheel at 524 rpm.
(define-machine solar-steam-wheel
  #:source "Branca (1629) and Salomon de Caus (1615)"
  #:latitude 31.2 #:day 172 #:time 12
  (post stand #:at (0 0 0) #:size ((cm 30) (m 1) (cm 30)) #:material limestone)
  (boiler pot #:at (0 1 0) #:radius (cm 12) #:height (cm 20)
          #:water (kg 1) #:fire 0 #:temperature 20 #:material bronze)
  (mirror m1 #:at ((m 2) 0.3 0) #:area 1 #:onto pot #:reflectivity 0.85 #:material bronze)
  (mirror m2 #:at ((m -2) 0.3 0) #:area 1 #:onto pot #:reflectivity 0.85 #:material bronze)
  (mirror m3 #:at (0 0.3 (m 2)) #:area 1 #:onto pot #:reflectivity 0.85 #:material bronze)
  (mirror m4 #:at (0 0.3 (m -2)) #:area 1 #:onto pot #:reflectivity 0.85 #:material bronze)
  (jetwheel wheel #:at ((cm 30) (m 1.4) 0) #:radius (cm 15) #:bore (mm 2.5)
            #:paddles 8 #:width (cm 3) #:mass (kg 0.5) #:load 0.003 #:material bronze)
  (connect pot.steam wheel.steam-in))
