#lang heroic
;; A steam reaction engine on a stone firebox, fired by a wood hearth. It is
;; Hero's aeolipile, but with the fire made honest: instead of a boiler
;; given a preset number of watts for ever, a hearth burns a load of wood
;; and goes out when it is gone.
;;
;;   60 g of wood x 15 MJ/kg = 0.9 MJ released, burning at 4 kW, so it
;;   lasts 225 s; the open fire puts half of it (x0.5) into the kettle:
;;   2 kW while it burns, 0.45 MJ in all.
;;
;; The kettle heats from cold, boils, and the steam leaves through two bent
;; nozzles on the bronze sphere. What speed it settles at isn't set
;; anywhere: the water boils only as fast as the fire's 2 kW (less what the
;; kettle loses to the air) can make steam, the boiler's pressure rises
;; until that much steam can just get out of the nozzles, and the sphere
;; speeds up until air drag on it balances the nozzles' thrust. When the
;; wood is gone the fire dies, the steam falls off and the sphere slows.
;;
;; The kettle and the fire stand on a limestone plinth; the sphere is held
;; over the kettle by its two hollow steam posts.
(define sphere-radius (cm 6))
(define plinth-height (cm 50))

(define-machine hearth-engine
  #:source "Hero of Alexandria's aeolipile, fired by a wood hearth"
  (post plinth #:at (0 0 0) #:size ((cm 70) plinth-height (cm 70)) #:material limestone)
  (boiler kettle #:at (0 (+ plinth-height (cm 8)) 0) #:radius (cm 12) #:height (cm 16)
          #:water (kg 0.3) #:material bronze)
  (hearth fire #:at (0 plinth-height 0) #:heats kettle #:power (W 4000) #:fuel (g 60)
          #:fuel-kind wood #:efficiency 0.5)
  (rotor ball #:at (0 (+ plinth-height (cm 45)) 0) #:radius sphere-radius #:wall (mm 1)
         #:material bronze #:nozzles 2 #:bore (mm 2) #:arm (+ sphere-radius (cm 2)))
  (connect kettle.steam ball.steam-in))
