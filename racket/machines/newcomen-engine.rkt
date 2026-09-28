#lang heroic
;; Thomas Newcomen's atmospheric engine — the first practical piston
;; engine, built to pump water out of mines. This follows his first known
;; one, at Dudley Castle (1712): a 21-inch (53 cm) cylinder, a stroke of
;; about six feet, pumping "10 gallons per stroke from 156 feet" (≈45 L
;; from 47.5 m).
;;
;; The cylinder stands on the boiler, open at the top. Its piston hangs on
;; a chain from one end of a great rocking beam; the mine pump's heavy rod
;; hangs from the other. The cycle:
;;   1. Steam at about atmospheric pressure fills the cylinder; the pump
;;      rod's weight, on the far end of the beam, draws the piston up.
;;   2. At the top a tappet opens a jet of cold water into the cylinder:
;;      the steam condenses, leaving a partial vacuum (~20 kPa).
;;   3. The atmosphere — 101 kPa on 0.22 m² of piston, ~18 kN — pushes the
;;      piston down, rocking the beam and lifting the pump rod, which
;;      raises a pump barrel's worth of water up the shaft.
;;   4. At the bottom another tappet shuts the jet and admits steam again.
;; It is the air that does the work; the steam only makes room for it.
;;
;; The pump's 18.5 cm bore over a 1.8 m stroke lifts 48 L a stroke from
;; 48 m — the Dudley engine's 10 gallons from 156 feet. The mine is a tank
;; 48 m below the floor, out of sight.
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

(define-machine newcomen-engine
  #:source "Thomas Newcomen, the Dudley Castle engine, 1712"
  (boiler boiler #:at (cylinder-x 0 0) #:radius (m 1.1) #:height (m 1.8) #:material iron
          #:water (kg 2000) #:fire (kW 250) #:temperature 104)
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
