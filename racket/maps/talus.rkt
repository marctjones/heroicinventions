#lang heroic
;; A cemented regolith cliff with rock in it (issue #88): when its face comes
;; down, some of what falls comes down as granite boulders, which slide down
;; the debris and stop.
;;
;; Worked out beforehand (Earth gravity, the world's crate; 8 m along the cliff):
;; - The face: regolith, c = 4 kPa, tan φ = 0.70, 1500 kg/m³, stands to
;;   4c/γ · tan(45° + φ/2) = 4 x 4000 / (1500 x 9.81) x 1.9206 = 2.088 m; the cliff is
;;   3 m, so the crest column (x 4.5 to 5) fails.
;; - What comes down: the column's 3 m of height spread at repose (a drop of
;;   0.70 x 0.5 = 0.35 m a cell) over it and the cells below: h + (h - 0.35) +
;;   (h - 0.70) + (h - 1.05) = 3, h = 1.275 m left on the column, so it loses
;;   1.725 m, 1.725 x 0.5 x 8 = 6.90 m³.
;; - The rock: 8 % of it in 0.5 m cubes, floor(0.08 x 6.90 / 0.125) = floor(4.42)
;;   = 4 boulders, 0.5 m³ (it stays 4 for anything from 6.25 to 7.80 m³).
;;   They come out of the debris, 0.5 / (16 cells x 0.25 m²) = 0.125 m of a
;;   cell's height along each row, and what is left settles again: 4h - 2.1 =
;;   3 - 0.125, h = 1.244 m, so the collapse is V = (3 - 1.244) x 0.5 x 8 =
;;   7.03 m³ (allow 3 %: the settling stops each step a little short of repose).
;;   The ground ends up 0.5 m³ less than it started; the debris holds V - 0.5.
;; - The face left behind, 3 m less the ~1.2 m of debris on the failed column,
;;   ~1.8 m, is under 2.088 m: it stands.
;; - The boulders are laid on the debris where it is thickest, at its 35° slope
;;   (tan 0.70). Granite on the ground, √(0.6 x 0.6) = 0.6, holds on slopes up to
;;   atan 0.6 = 31.0°: on 35° they slide, at g (sin 35° - 0.6 cos 35°) = 0.80 m/s²,
;;   and stop where the ground is no steeper than 31.0°, the predicted angle.
;;   Friction takes μ m g of energy for each metre they go across (whatever the
;;   slope), so their drop over their run is at least μ: the line from where each
;;   starts to where it stops is at least atan 0.6 = 31° below level.
(define-map talus
  #:origin (0 -4) #:cell 0.5 #:size (30 16)
  #:heights (λ (x z) (if (< x 5) 3 0))
  #:soil regolith
  #:cohesion ((regolith 4000))
  #:boulders ((regolith 0.08 0.5 granite))
  #:edges closed
  #:settle #t)
