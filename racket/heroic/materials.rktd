;; Material table: the source of truth for every material in the game.
;; racket/build.rkt exports it to src/HeroicInventions.Sim/Materials/materials.json.
;; Approximate handbook values. Units: density kg/m³, strengths MPa, modulus GPa.
;; Wood strengths are along the grain, with a separate across-grain tension.
;; Restitution: the fraction of closing speed a collision gives back (0 =
;; dead stop, 1 = perfectly elastic), for a part of this material striking
;; another. Hardened steel is here for the Newton's cradle, a modern toy
;; that only works because steel balls lose so little in each collision.

(oak       (name "Oak")          (category wood)  (density 720)  (youngs-modulus 11.0)  (tension 90)  (across-grain 5)   (compression 50)  (friction 0.45) (restitution 0.50))
(pine      (name "Pine")         (category wood)  (density 500)  (youngs-modulus 9.0)   (tension 70)  (across-grain 3)   (compression 40)  (friction 0.40) (restitution 0.50))
(cedar     (name "Cedar")        (category wood)  (density 380)  (youngs-modulus 7.5)   (tension 45)  (across-grain 2)   (compression 35)  (friction 0.40) (restitution 0.45))
(olive     (name "Olive")        (category wood)  (density 950)  (youngs-modulus 13.0)  (tension 100) (across-grain 6)   (compression 60)  (friction 0.40) (restitution 0.50))
(limestone (name "Limestone")    (category stone) (density 2500) (youngs-modulus 40.0)  (tension 5)   (across-grain 5)   (compression 60)  (friction 0.60) (restitution 0.50))
(marble    (name "Marble")       (category stone) (density 2700) (youngs-modulus 55.0)  (tension 8)   (across-grain 8)   (compression 100) (friction 0.55) (restitution 0.60))
(granite   (name "Granite")      (category stone) (density 2700) (youngs-modulus 60.0)  (tension 10)  (across-grain 10)  (compression 180) (friction 0.60) (restitution 0.60))
(bronze    (name "Bronze")       (category metal) (density 8800) (youngs-modulus 110.0) (tension 350) (across-grain 350) (compression 350) (friction 0.30) (restitution 0.60))
(iron      (name "Wrought iron") (category metal) (density 7700) (youngs-modulus 190.0) (tension 330) (across-grain 330) (compression 330) (friction 0.40) (restitution 0.55))
(hemp      (name "Hemp rope")    (category fiber) (density 1100) (youngs-modulus 2.0)   (tension 60)  (across-grain 0)   (compression 0)   (friction 0.50) (restitution 0.10))
(steel     (name "Hardened steel") (category metal) (density 7850) (youngs-modulus 200.0) (tension 1000) (across-grain 1000) (compression 1000) (friction 0.35) (restitution 0.95))
