#lang heroic
;; Two belt drives, side by side, alike but for how tight the belt is. On
;; each, a 10 cm pulley on the left, turned by a motor giving a steady
;; 1.0 N.m, drives a 20 cm pulley on the right 60 cm away through an open belt
;; of hemp (mu 0.5). Working it through:
;;   wrap      on the smaller pulley theta = pi - 2 asin((r2 - r1)/C) =
;;             pi - 2 asin(0.1/0.6) = 2.807 rad, 160.8 degrees.
;;   limit     the belt carries at most T1 - T2 with T1/T2 = e^(mu theta) = 4.07
;;             and T1 + T2 = 2 T0: F = 2 T0 tanh(mu theta / 2) = 1.2109 T0.
;;               loose,  T0 =  5 N:   F =  6.05 N, 0.605 N.m at the small pulley
;;               tight,  T0 = 10 N:   F = 12.11 N, 1.211 N.m at the small pulley
;;             The motor gives 1.0 N.m, so the loose belt cannot carry it and
;;             the tight one can: twice the tension, twice the torque.
;;   tight     it grips: the big pulley turns at r1/r2 = half the small one's
;;             speed all the while, and the pair speed up together at
;;             1.0 / (I1 + I2/4) = 1.0 / (0.004605 + 0.14736/4) = 24.1 rad/s2
;;             (oak, 720 kg/m3), a little less for the bearings' damping.
;;             The belt works one physics tick (8 ms) behind the drums, so the
;;             ratio reads a little slow while they are slow: 8% low at 1 s, 3% at 3 s.
;;   loose     it slips, and carries exactly its limit, 6.06 N: that gives the
;;             big pulley 6.06 N x 0.2 m = 1.211 N.m, so it gains 8.22 rad/s2
;;             against the damping's 0.2/s (omega = 41.1 (1 - e^(-0.2 t)): 2.0
;;             rad/s at 0.25 s), while the small pulley, freed of all but
;;             0.606 N.m, races at (1.0 - 0.606) / 0.004605 = 85.6 rad/s2, 21.4
;;             rad/s at 0.25 s. The physics engine caps any body at 47.1
;;             rad/s (15 pi) and the small pulley reaches that at 0.55 s, so
;;             compare before then: a ratio of 0.09, not the 0.5 of a belt that holds.
(define-machine belt-drive
  #:source "A belt drive: tighter carries more"
  ;; the loose pair, in front
  (wheel loose-driver #:shape (pulley #:radius (cm 10) #:width (cm 5)) #:at (0 1 0) #:material oak
         #:drive-rpm 2000 #:drive-torque 1.0)
  (wheel loose-driven #:shape (pulley #:radius (cm 20) #:width (cm 10)) #:at ((cm 60) 1 0) #:material oak)
  (belt loose-belt loose-driver loose-driven #:tension 5)
  ;; the tight pair, behind
  (wheel tight-driver #:shape (pulley #:radius (cm 10) #:width (cm 5)) #:at (0 1 (cm -50)) #:material oak
         #:drive-rpm 2000 #:drive-torque 1.0)
  (wheel tight-driven #:shape (pulley #:radius (cm 20) #:width (cm 10)) #:at ((cm 60) 1 (cm -50)) #:material oak)
  (belt tight-belt tight-driver tight-driven #:tension 10))
