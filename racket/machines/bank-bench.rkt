#lang heroic
;; The battery bank's rules, on a bench (issue #64): what it takes, when, and what makes the call. Everything here is turned by the
;; sim itself (small windmills straight on a bench generator), so a run is headless and fast. The generators are bench motors with a
;; 30 rpm cut-in (3.1416 rad/s), 150 rpm rated (15.708 rad/s) at 40 N.m, eta 0.8; the real default is a 1,500 rpm cut-in, 2,500 rpm,
;; 12 N.m, which found-electrics tests with a geared train.
;;
;; Each windmill: 2 m sails of 30 kg (I = m R^2 / 3 = 40 kg.m2) in a 5 m/s wind of air at 20 C (1.2041 kg/m3), Cp 0.3 at lambda* = 2.5.
;;   tau* = 1/2 rho A v^2 R Cp / lambda* = 45.394 N.m, w* = v lambda* / R = 6.25 rad/s, free-running at lambda 5: 12.5 rad/s (119.4 rpm).
;;   The generator's torque k (w - w_cut), k = 40 / (15.708 - 3.1416) = 3.1831 N.m per rad/s, against the sails' tau* (2 - w / w*):
;;   w = (2 tau* + k w_cut) / (tau* / w* + k) = 100.79 / 10.4466 = 9.6484 rad/s (92.1 rpm), tau_g = 20.712 N.m,
;;   shaft power 199.83 W, charge 0.8 x 199.83 = 159.87 W (5.71 A at 28 V). The sails slow by 2.852 rad/s from their free 12.5 (23%).
;;   They settle in tau = I / (tau* / w* + k) = 40 / 10.447 = 3.8 s.
;; A bank of 5 Wh (18,000 J) fills in 18,000 / 159.87 = 112.6 s of charging.
;;
;; warm   20 C, held there: charges from the start, full at about 117 s
;; cold   -10 C, in a 20 C air through 5 W/K (the cells' 16 kJ/K: tau 3,200 s): T = 20 - 30 exp(-t / 3200) crosses 0 C at
;;        3,200 ln(30/20) = 1,297.5 s. No charge before it; charging resumes from it, full about 113 s later
;; hot    50 C, cooling the same way: 20 + 30 exp(-t / 3200) falls to 45 C at 3,200 ln(30/25) = 583.4 s. No charge before it; from it, full about 113 s later
;; The call (banks already full, windmills nowhere near them), the clock held and set by hand:
;; ok     full at 20 C: wins when the pass opens at 03:00, not at 02:59, and still at 03:09:59 but its window shuts at 03:10
;; late   charged only at 200 s, at 03:11 (the window shut): not won; at 03:06 on the next try (300 s) it is
;; hot-w  full but at 50 C: never, at any hour in the window
;; cold-w full but at -5 C: never
;; easy   full at 20 C with the easy setting: wins at once, at noon
(define-machine bank-bench
  #:source "issue #64: the battery bank takes charge from 0 to 45 C, and the call on the 03:00 pass wins"
  #:ambient 20
  #:time 12
  ;; three generators charging three banks
  (windmill w-warm #:at (0 (m 3) 0) #:radius (m 2) #:mass 30 #:wind 5 #:cp 0.3 #:tip-speed-ratio 2.5 #:material oak)
  (windmill w-cold #:at ((m 6) (m 3) 0) #:radius (m 2) #:mass 30 #:wind 5 #:cp 0.3 #:tip-speed-ratio 2.5 #:material oak)
  (windmill w-hot #:at ((m 12) (m 3) 0) #:radius (m 2) #:mass 30 #:wind 5 #:cp 0.3 #:tip-speed-ratio 2.5 #:material oak)
  (generator g-warm #:at (0 (m 3) (m -0.4)) #:on w-warm #:charges warm #:cut-in-rpm 30 #:rated-rpm 150 #:rated-torque 40)
  (generator g-cold #:at ((m 6) (m 3) (m -0.4)) #:on w-cold #:charges cold #:cut-in-rpm 30 #:rated-rpm 150 #:rated-torque 40)
  (generator g-hot #:at ((m 12) (m 3) (m -0.4)) #:on w-hot #:charges hot #:cut-in-rpm 30 #:rated-rpm 150 #:rated-torque 40 #:driven-by falling-weight)
  (heat-store warm-cells #:at ((m 1.5) 0 (m -1)) #:mass 16 #:contents cells #:temperature 20 #:area 0)
  (heat-store cold-cells #:at ((m 7.5) 0 (m -1)) #:mass 16 #:contents cells #:temperature -10 #:area 0 #:conductance 5)
  (heat-store hot-cells #:at ((m 13.5) 0 (m -1)) #:mass 16 #:contents cells #:temperature 50 #:area 0 #:conductance 5)
  (battery-bank warm #:at ((m 2.1) 0 (m -1)) #:in warm-cells #:capacity 5)
  (battery-bank cold #:at ((m 8.1) 0 (m -1)) #:in cold-cells #:capacity 5)
  (battery-bank hot #:at ((m 14.1) 0 (m -1)) #:in hot-cells #:capacity 5)
  ;; the call
  (heat-store ok-cells #:at ((m 1.5) 0 (m -3)) #:mass 16 #:contents cells #:temperature 20 #:area 0)
  (heat-store late-cells #:at ((m 4.5) 0 (m -3)) #:mass 16 #:contents cells #:temperature 20 #:area 0)
  (heat-store hot-w-cells #:at ((m 7.5) 0 (m -3)) #:mass 16 #:contents cells #:temperature 50 #:area 0)
  (heat-store cold-w-cells #:at ((m 10.5) 0 (m -3)) #:mass 16 #:contents cells #:temperature -5 #:area 0)
  (heat-store easy-cells #:at ((m 13.5) 0 (m -3)) #:mass 16 #:contents cells #:temperature 20 #:area 0)
  (battery-bank ok #:at ((m 2.1) 0 (m -3)) #:in ok-cells #:capacity 5 #:charge 5)
  (battery-bank late #:at ((m 5.1) 0 (m -3)) #:in late-cells #:capacity 5 #:charge 0)
  (battery-bank hot-w #:at ((m 8.1) 0 (m -3)) #:in hot-w-cells #:capacity 5 #:charge 5)
  (battery-bank cold-w #:at ((m 11.1) 0 (m -3)) #:in cold-w-cells #:capacity 5 #:charge 5)
  (battery-bank easy #:at ((m 14.1) 0 (m -3)) #:in easy-cells #:capacity 5 #:charge 5 #:call-any-time #t))
