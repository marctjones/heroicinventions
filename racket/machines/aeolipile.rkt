#lang heroic
;; Hero of Alexandria's aeolipile: a bronze sphere on an axle over a
;; boiler, spun by the reaction of two bent steam nozzles.

(define sphere-radius (cm 6))

(define-machine aeolipile
  #:source "Hero of Alexandria, Pneumatica"
  (boiler kettle #:at (0 (cm 8) 0) #:radius (cm 12) #:height (cm 16)
          #:water (kg 0.3) #:fire (W 3000) #:material bronze)
  (rotor ball #:at (0 (cm 45) 0) #:radius sphere-radius #:wall (mm 1)
         #:material bronze #:nozzles 2 #:bore (mm 2)
         ;; Nozzle tips sit 2 cm beyond the sphere's surface.
         #:arm (+ sphere-radius (cm 2)))
  (connect kettle.steam ball.steam-in))
