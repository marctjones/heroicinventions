#lang heroic
;; Tongs (issue #55). Three pairs of tongs hang in the air on the world, each
;; with an iron crate within reach at rest in its jaws. Tongs of bronze on iron
;; grip as well as the poorer surface allows, mu = min(0.30, 0.40) = 0.30, and
;; squeeze from two sides, so they carry 2 mu N, and a crate of mass m needs
;; N >= m g / (2 mu). Working it through:
;;   light   an 8.7 cm iron cube is 5.07 kg, a load of 49.7 N; tongs of 100 N
;;           carry 2 (0.30)(100) = 60 N: it holds (83% of the limit). It needs
;;           only 5.07 (9.81) / 0.6 = 82.9 N.
;;   heavy   a 9.9 cm iron cube is 7.47 kg, 73.3 N; the same 100 N tongs carry
;;           60: it slips. It needs 7.47 (9.81) / 0.6 = 122.1 N.
;;   firm    the heavy crate in tongs of 140 N (limit 84 N): it holds (87%).
;;   let go  the light crate is let go after it has been held one second (a
;;           trigger on the tongs' own clock opens them), 1.2 m up and falling
;;           onto a 30 cm stone step, so its middle falls 1.2 - 0.3 - 0.0435 =
;;           0.8565 m: it lands after t = sqrt(2 h / g) = 0.418 s at
;;           sqrt(2 g h) = 4.10 m/s. The heavy crate, dropped at once, falls
;;           the same height in the same time.
;; On Mars (g = 3.71) the same tongs need 2.64 times less force per kilogram,
;; or hold 2.64 times the mass.
(define-machine crate-tongs
  #:source "Tongs that hold what their grip allows"
  (post step-light #:at (0 0 0) #:size ((cm 40) (cm 30) (cm 40)) #:material limestone)
  (post step-heavy #:at ((m 1) 0 0) #:size ((cm 40) (cm 30) (cm 40)) #:material limestone)
  (post step-firm #:at ((m 2) 0 0) #:size ((cm 40) (cm 30) (cm 40)) #:material limestone)
  (block light-crate #:at (0 (m 1.2) 0) #:size (cm 8.7) #:material iron)
  (block heavy-crate #:at ((m 1) (m 1.2) 0) #:size (cm 9.9) #:material iron)
  (block firm-crate #:at ((m 2) (m 1.2) 0) #:size (cm 9.9) #:material iron)
  (grip light-tongs #:at (0 (m 1.2) 0) #:kind tongs #:force 100 #:reach (cm 15) #:closed 1 #:material bronze)
  (grip heavy-tongs #:at ((m 1) (m 1.2) 0) #:kind tongs #:force 100 #:reach (cm 15) #:closed 1 #:material bronze)
  (grip firm-tongs #:at ((m 2) (m 1.2) 0) #:kind tongs #:force 140 #:reach (cm 15) #:closed 1 #:material bronze)
  (trigger let-go #:when (light-tongs held-for above 1) #:do ((light-tongs closed 0))))
