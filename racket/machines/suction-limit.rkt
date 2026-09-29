#lang heroic
;; The suction limit. Galileo (Two New Sciences, 1638) tells of a well pump
;; that would not raise water more than 18 braccia, about 10.5 m, however
;; it was worked; Berti ran a lead pipe up the front of his house in Rome
;; (c. 1641) and found the water stood about 10 m up it, whatever was done
;; above; Torricelli (1644) saw why. A lift pump does not pull water up: it
;; empties the pipe above it and the atmosphere, pressing on the well, pushes
;; the water up after its bucket. It can push only until the pressure under
;; the bucket falls to the water's vapour pressure, when the water boils
;; away from the bucket and the column breaks:
;;   h_max = (P_atm - P_v) / (rho g) = (101325 - 2330) / 9810 = 10.09 m
;; at 20 C (10.33 m if water had no vapour pressure).
;;
;; Three lift pumps, each a 15 cm bucket over a 50 cm stroke, cranked at 20
;; strokes a minute, each over its own well. By hand:
;;   short    its bucket 6 m over the water, the barrel's top 6.5 m: well
;;            within the limit. A S eta = 0.01767 x 0.5 x 0.8 = 7.07 L a
;;            stroke, 2.36 L/s. The rod carries rho g A (spout - surface)
;;            = 1127 N on the upstroke, and the water gets eta = 80% of
;;            the work: the rest goes in the water that slips back.
;;   tall     its bucket 11 m over the water: past the limit. The water
;;            stands 10.09 m up the pipe and no higher; the drive pulls
;;            with up to 10 kN, but the column can take only
;;            A (P_atm - P_v) = 1749 N before it breaks, and that pull is
;;            only a vacuum, handed back on the downstroke. Nothing comes
;;            out.
;;   drawing  its bucket 8 m over a narrow well (0.25 m2), which it draws
;;            down 28.3 mm a stroke. Once the barrel's top stands more than
;;            10.09 m over the water (after 56 strokes, 168 s) the column
;;            breaks part way up each stroke and each draws less; the gap
;;            to the limit shrinks by 1 / (1 + A eta / 0.25) a stroke, so
;;            the lift closes on 10.09 m and the pump stops lifting.
(define barrel-bore (cm 15))
(define barrel-stroke (cm 50))
(define strokes-a-minute 20)

(define-machine suction-limit
  #:source "Galileo, Two New Sciences (1638); Berti's tube (c. 1641); Torricelli (1644)"
  (tank short-well #:at (0 0 0) #:area 4 #:height 1 #:water 3.2 #:material limestone)          ; 80 cm deep
  (pump short #:at ((m 0.6) (m 6.8) 0) #:from short-well #:to short-cistern
        #:bore barrel-bore #:stroke barrel-stroke #:rpm strokes-a-minute)
  (tank short-cistern #:at ((m 1.6) (m 6.4) 0) #:area 2 #:height (cm 80) #:material oak)

  (tank tall-well #:at ((m 3.5) 0 0) #:area 1 #:height 1 #:water 0.8 #:material limestone)     ; 80 cm deep
  (pump tall #:at ((m 3.5) (m 11.8) 0) #:from tall-well #:to tall-cistern
        #:bore barrel-bore #:stroke barrel-stroke #:rpm strokes-a-minute #:force 10000)
  (tank tall-cistern #:at ((m 4.2) (m 11.4) 0) #:area 1 #:height (cm 80) #:material oak)

  (tank deep-well #:at ((m 6.5) 0 0) #:area 0.25 #:height 3 #:water 0.7 #:material limestone)  ; 2.8 m deep
  (pump drawing #:at ((m 6.5) (m 10.8) 0) #:from deep-well #:to deep-cistern
        #:bore barrel-bore #:stroke barrel-stroke #:rpm strokes-a-minute)
  (tank deep-cistern #:at ((m 7.2) (m 10.4) 0) #:area 1 #:height (cm 80) #:material oak))
