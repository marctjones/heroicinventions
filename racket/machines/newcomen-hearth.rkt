#lang heroic
;; Newcomen's atmospheric engine, fired by a real hearth. The plain
;; newcomen-engine.rkt gives its boiler a preset 250 kW "fire" that never
;; runs out; here the heat has to come from something. A hearth burns a
;; load of coal — 24 MJ/kg — at 1 MW, and the boiler gets only a quarter of
;; that (an early Newcomen boiler was a plain bowl of water over the fire,
;; most of whose heat went up the chimney). So the fire delivers 250 kW to
;; the water, as before, but for only as long as the coal lasts:
;;
;;   2 kg of coal x 24 MJ/kg = 48 MJ released, x 0.25 = 12 MJ into the water,
;;   burning at 1 MW, so the fire is out after 2 / (1e6 / 24e6) = 48 s.
;;
;; From then on the engine lives on the heat stored in 2 tonnes of hot
;; water, and every stroke draws down that store: ~0.5 MJ of latent heat
;; a stroke, for ~23 kJ of water lifted. Nothing here feeds the fire; give
;; it more coal with (set firebox fuel) and it burns again.
;;
;; Everything else — the beam, piston, pump and mine — is newcomen-engine.
(require racket/math)

(define pivot (list 0 (m 6.2)))
(define half-beam (m 3))
(define swing-deg 17.5)                   ; ±17.5° moves each beam end ±0.9 m: a 1.8 m stroke
(define stroke (m 1.8))
(define end-x (* half-beam (cos (degrees->radians swing-deg)))) ; beam end, horizontally, at full swing
(define drop (* half-beam (sin (degrees->radians swing-deg))))
(define cylinder-x (- (/ (+ half-beam end-x) 2)))  ; under the beam end's mean position, so the chain hangs near-plumb
(define pump-x (- cylinder-x))
(define piston-bottom (m 2.2))            ; the cylinder stands on the 1.8 m boiler
(define pump-bottom (m 1.0))
(define disc (cm 5))                      ; half a piston's thickness: the chain hooks on its top

;; Starting at the bottom of the steam stroke: engine end down, pump end up.
(define (dist x1 y1 x2 y2) (sqrt (+ (sqr (- x1 x2)) (sqr (- y1 y2)))))
(define chain-1 (dist (- end-x) (- (cadr pivot) drop) cylinder-x (+ piston-bottom disc)))
(define chain-2 (dist end-x (+ (cadr pivot) drop) pump-x (+ pump-bottom stroke disc)))

(define-machine newcomen-hearth
  #:source "Thomas Newcomen, the Dudley Castle engine, 1712, fired by a coal hearth"
;; the boiler sits over its fire on a stone firebox; the cylinder stands on
  ;; top of it, fed by a short steam pipe through the steam valve
  (boiler boiler #:at (cylinder-x (m 0.6) 0) #:radius (m 1.1) #:height (m 1.2) #:material iron
          #:water (kg 2000) #:temperature 104)
  (hearth firebox #:at (cylinder-x 0 0) #:heats boiler #:power (kW 1000) #:fuel (kg 2)
          #:fuel-kind coal #:efficiency 0.25)
  (piston engine-piston #:at (cylinder-x piston-bottom 0) #:bore (cm 53) #:stroke stroke #:material iron)
  (atmospheric-cylinder cylinder #:piston engine-piston #:steam-from boiler)
  (lever beam #:at ((car pivot) (cadr pivot) 0) #:length (* 2 half-beam) #:material oak
         #:start-angle-deg swing-deg #:limit-deg 20 #:damping 0.2
         #:section (cm 30))                   ; the great oak beam: ~390 kg
  (rope engine-chain #:from (beam (- half-beam) 0 0) #:to (engine-piston 0 disc 0) #:length chain-1
        #:material iron #:diameter (cm 3))
  (piston pump-rod #:at (pump-x pump-bottom 0) #:bore (cm 18.5) #:stroke stroke #:start 1
          #:rod-mass (kg 350) #:material iron)
  (rope pump-chain #:from (beam half-beam 0 0) #:to (pump-rod 0 disc 0) #:length chain-2
        #:material iron #:diameter (cm 3))
  (tank mine #:at (pump-x (m -48) 0) #:area 2 #:height (m 3) #:water (L 5000))
  (tank cistern #:at ((+ pump-x (m 1.4)) 0 0) #:area 2 #:height (m 1))
  (lift drainage #:by pump-rod #:from mine #:to cistern))
