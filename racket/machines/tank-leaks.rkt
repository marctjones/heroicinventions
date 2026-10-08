#lang heroic
;; Leaks in tanks, Torricelli's law. Four identical oak barrels on stone
;; piers, each 0.25 m2 and 1 m tall. Three are 80 cm full and have a 5 cm2
;; hole (2.5 cm across) in the wall: a water jet, Q = 0.6 a sqrt(2 g h),
;; h the water standing above the hole. Nothing here sets a flow or a
;; level. By hand:
;;   draw-down  in u = sqrt(h - hole) the level falls at a constant rate,
;;              du/dt = -0.6 a sqrt(2g) / (2 A) = -2.658e-3 m^1/2 per
;;              second, so h(t) = hole + (sqrt(H - hole)
;;              - 2.658e-3 t)^2, and it stops when the water reaches the hole.
;;   low        hole 10 cm up: 70 cm of head to start with, 1.112 L/s. It
;;              runs dry to the hole at (A / 0.6 a) sqrt(2 (H - hole) / g)
;;              = 314.8 s, and holds 10 cm for good.
;;   high       the same hole 40 cm up: 40 cm of head, 0.840 L/s, gone in
;;              238.0 s, and holds 40 cm. Lower holes leak faster and
;;              drain further.
;;   plugged    the low hole again, drawn into a catch tank (0.5 m2) under
;;              its jet, and stopped with a thumb at 100 s. Every litre out
;;              of the barrel is a litre in the catch tank, and afterwards
;;              the level stands where it was.
;;   thumb      what a person does with the plugged barrel, and the demo operator
;;              does by itself: put a thumb on plug-hole at 100 s (click it:
;;              its area goes to 0; click again and it leaks as before). The
;;              level stands where it was at that moment, 0.1 + (sqrt 0.7 -
;;              2.658e-3 x 100)^2 = 42.59 cm, 106.5 L in the barrel, and the
;;              other 93.5 L (18.7 cm over the catch tank's 0.5 m2) are in
;;              the catch tank, for good.
;;   seep       no hole at all but a seep off the surface, 0.05 L/s: 50 cm
;;              falling a steady 0.2 mm/s whatever the level.
(define-machine tank-leaks
  #:source "Torricelli's law: leaks in tanks at three heights, plugged, and a seep"
  (post low-pier #:at (0 0 0) #:size ((cm 50) (cm 30) (cm 50)) #:material limestone)
  (tank low #:at (0 (cm 30) 0) #:area 0.25 #:height (m 1) #:water (L 200) #:material oak)
  (leak low-hole #:on low #:height (cm 10) #:area (cm2 5))

  (post high-pier #:at ((m 2) 0 0) #:size ((cm 50) (cm 30) (cm 50)) #:material limestone)
  (tank high #:at ((m 2) (cm 30) 0) #:area 0.25 #:height (m 1) #:water (L 200) #:material oak)
  (leak high-hole #:on high #:height (cm 40) #:area (cm2 5))

  (post plug-pier #:at ((m 4) 0 0) #:size ((cm 50) (cm 30) (cm 50)) #:material limestone)
  (tank plugged #:at ((m 4) (cm 30) 0) #:area 0.25 #:height (m 1) #:water (L 200) #:material oak)
  (leak plug-hole #:on plugged #:height (cm 10) #:area (cm2 5) #:into catch)
  (tank catch #:at ((m 4.8) 0 0) #:area 0.5 #:height (cm 30) #:water 0 #:material limestone)

  (post seep-pier #:at ((m 7) 0 0) #:size ((cm 50) (cm 30) (cm 50)) #:material limestone)
  (tank seep #:at ((m 7) (cm 30) 0) #:area 0.25 #:height (m 1) #:water (L 125) #:material oak)
  (leak seep-hole #:on seep #:height 0 #:evaporation (L/s 0.05))
  ;; the thumb on the plugged barrel's hole at 100 s (issue #157); taking any control stops it
  (operator (at 100 (plug-hole area 0))))
