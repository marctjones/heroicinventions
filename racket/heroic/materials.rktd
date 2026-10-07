;; Material table: the source of truth for every material in the game.
;; racket/build.rkt exports it to src/HeroicInventions.Sim/Materials/materials.json.
;; Approximate handbook values. Units: density kg/m³, strengths MPa, modulus GPa.
;; Wood strengths are along the grain, with a separate across-grain tension.
;; Restitution: the fraction of closing speed a collision gives back (0 =
;; dead stop, 1 = perfectly elastic), for a part of this material striking
;; another. Hardened steel is here for the Newton's cradle, a modern toy
;; that only works because steel balls lose so little in each collision.

(oak       (name "Oak")          (category wood)  (density 720)  (youngs-modulus 11.0)  (tension 90)  (across-grain 5)   (compression 50)  (friction 0.45) (restitution 0.50) (color "#8C6138"))
(pine      (name "Pine")         (category wood)  (density 500)  (youngs-modulus 9.0)   (tension 70)  (across-grain 3)   (compression 40)  (friction 0.40) (restitution 0.50) (color "#D1A86E"))
(cedar     (name "Cedar")        (category wood)  (density 380)  (youngs-modulus 7.5)   (tension 45)  (across-grain 2)   (compression 35)  (friction 0.40) (restitution 0.45) (color "#C2855C"))
(olive     (name "Olive")        (category wood)  (density 950)  (youngs-modulus 13.0)  (tension 100) (across-grain 6)   (compression 60)  (friction 0.40) (restitution 0.50) (color "#998557"))
(limestone (name "Limestone")    (category stone) (density 2500) (youngs-modulus 40.0)  (tension 5)   (across-grain 5)   (compression 60)  (friction 0.60) (restitution 0.50) (color "#D3C8AE"))
(marble    (name "Marble")       (category stone) (density 2700) (youngs-modulus 55.0)  (tension 8)   (across-grain 8)   (compression 100) (friction 0.55) (restitution 0.60) (color "#E4E0D6") (finish veined))
(granite   (name "Granite")      (category stone) (density 2700) (youngs-modulus 60.0)  (tension 10)  (across-grain 10)  (compression 180) (friction 0.60) (restitution 0.60) (color "#7D7672") (finish crystalline))
(bronze    (name "Bronze")       (category metal) (density 8800) (youngs-modulus 110.0) (tension 350) (across-grain 350) (compression 350) (friction 0.30) (restitution 0.60) (color "#CC8F4A"))
(iron      (name "Wrought iron") (category metal) (density 7700) (youngs-modulus 190.0) (tension 330) (across-grain 330) (compression 330) (friction 0.40) (restitution 0.55) (color "#4A4A52") (finish wrought))
(hemp      (name "Hemp rope")    (category fiber) (density 1100) (youngs-modulus 2.0)   (tension 60)  (across-grain 0)   (compression 0)   (friction 0.50) (restitution 0.10) (color "#C7B285"))
(steel     (name "Hardened steel") (category metal) (density 7850) (youngs-modulus 200.0) (tension 1000) (across-grain 1000) (compression 1000) (friction 0.35) (restitution 0.95) (color "#B8BDC4") (finish polished))

;; Soils: the ground of a map (issue #37). Loose and weak, so their
;; strengths are nominal; friction is the tangent of the angle of repose
;; (dry sand ~32°, Mars regolith ~35°). How fast each soaks up water is the
;; map's to say (#:infiltration), since it depends on how packed and how wet.
(sand      (name "Sand")         (category soil)  (density 1600) (youngs-modulus 0.05)  (tension 0)   (across-grain 0)   (compression 0.1) (friction 0.62) (restitution 0.10) (color "#DBC794"))
(loam      (name "Loam")         (category soil)  (density 1400) (youngs-modulus 0.02)  (tension 0)   (across-grain 0)   (compression 0.1) (friction 0.55) (restitution 0.10) (color "#6B5738"))
(clay      (name "Clay")         (category soil)  (density 1800) (youngs-modulus 0.03)  (tension 0.05) (across-grain 0.05) (compression 0.2) (friction 0.35) (restitution 0.05) (color "#9E6E4F"))
(regolith  (name "Mars regolith") (category soil) (density 1500) (youngs-modulus 0.05)  (tension 0)   (across-grain 0)   (compression 0.1) (friction 0.70) (restitution 0.10) (color "#8A5A3C"))
;; The Lonely Rover's crater (issue #61): the layers of its ground. Which of them stands as a cliff is the
;; map's to say, by cohesion (#:cohesion), so the two regoliths that differ only in how well their ice holds
;; are the same numbers here. Basalt sand is dark, the sand of the dunes on the floor (glass for the dark kind);
;; silica sand is the pale layer in one bay of the wall (clear glass); bedrock is the wall itself, which the
;; rover can neither climb nor cut; ice-cemented regolith is soil held by ice on the cold wall; sublimed regolith
;; is the same soil after the ice has gone.
(basalt-sand (name "Basalt sand") (category soil) (density 1600) (youngs-modulus 0.05)  (tension 0)   (across-grain 0)   (compression 0.1) (friction 0.62) (restitution 0.10) (color "#3D3838"))
(silica-sand (name "Silica sand") (category soil) (density 1600) (youngs-modulus 0.05)  (tension 0)   (across-grain 0)   (compression 0.1) (friction 0.62) (restitution 0.10) (color "#EBE0C7"))
(bedrock   (name "Bedrock")      (category stone) (density 2700) (youngs-modulus 60.0)  (tension 10)  (across-grain 10)  (compression 180) (friction 0.60) (restitution 0.60) (color "#755C4D"))
(ice-cemented-regolith (name "Ice-cemented regolith") (category soil) (density 1500) (youngs-modulus 0.05) (tension 0) (across-grain 0) (compression 0.1) (friction 0.70) (restitution 0.10) (color "#998582"))
(sublimed-regolith (name "Sublimed regolith") (category soil) (density 1500) (youngs-modulus 0.05) (tension 0) (across-grain 0) (compression 0.1) (friction 0.70) (restitution 0.10) (color "#BD7347"))
;; Glass (issue #57): soda-lime or fused silica, brittle. Tensile strength of a sound pane ~40 MPa (handbook); a pane is designed to ~7 MPa (see Pane.Strength).
(glass     (name "Glass")        (category stone) (density 2500) (youngs-modulus 70.0)  (tension 40)  (across-grain 40)  (compression 1000) (friction 0.40) (restitution 0.60) (color "#D1EBF2") (finish clear))
