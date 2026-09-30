#lang heroic
;; A clay cliff 4 m tall (issue #54), too tall for its clay to hold: a cut
;; face of clay with 10 kPa of cohesion stands to 4c/γ · tan(45° + φ/2) =
;; 3.19 m. The map settles on the world's first tick, and the face's crest
;; comes down.
;;
;; Worked out beforehand, per metre along the cliff: the half-metre column at
;; the crest fails (the face left behind, 4 m less the debris at its foot,
;; is then under 3.19 m and stands), so V = 0.5 x 4 = 2 m² of loose clay
;; slides down and lies at its angle of repose, tan φ = 0.35, in a wedge
;; against the new face at x = 4.5: h0² / (2 tan φ) = V, h0 = sqrt(2 x 2 x
;; 0.35) = 1.18 m at the face, running out sqrt(2V / tan φ) = 3.38 m, to
;; x = 7.88. A crate standing at x = 5.5 is buried under 1.18 - 0.35 x 1 =
;; 0.83 m of it, its 0.5 m top under 0.33 m of cover.
(define-map cliff
  #:origin (0 -3) #:cell 0.5 #:size (30 12)
  #:heights (λ (x z) (if (< x 5) 4 0))
  #:soil clay
  #:cohesion ((clay 10000))
  #:edges closed
  #:settle #t)
