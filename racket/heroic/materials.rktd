;; Material table: the source of truth for every material in the game.
;; racket/build.rkt exports it to src/HeroicInventions.Sim/Materials/materials.json.
;; Approximate handbook values. Units: density kg/m³, strengths MPa, modulus GPa.
;; Wood strengths are along the grain, with a separate across-grain tension.
;; Restitution: the fraction of closing speed a collision gives back (0 =
;; dead stop, 1 = perfectly elastic), for a part of this material striking
;; another. Hardened steel is here for the Newton's cradle, a modern toy
;; that only works because steel balls lose so little in each collision.

(oak       (name "Oak")          (category wood)  (density 720)  (youngs-modulus 11.0)  (tension 90)  (across-grain 5)   (compression 50)  (friction 0.45) (restitution 0.50) (color "#8C6138") (specific-heat 2000) (conductivity 0.17))
(pine      (name "Pine")         (category wood)  (density 500)  (youngs-modulus 9.0)   (tension 70)  (across-grain 3)   (compression 40)  (friction 0.40) (restitution 0.50) (color "#D1A86E") (specific-heat 1700) (conductivity 0.12))
(cedar     (name "Cedar")        (category wood)  (density 380)  (youngs-modulus 7.5)   (tension 45)  (across-grain 2)   (compression 35)  (friction 0.40) (restitution 0.45) (color "#C2855C") (specific-heat 1700) (conductivity 0.11))
(olive     (name "Olive")        (category wood)  (density 950)  (youngs-modulus 13.0)  (tension 100) (across-grain 6)   (compression 60)  (friction 0.40) (restitution 0.50) (color "#998557") (specific-heat 1700) (conductivity 0.17))
(limestone (name "Limestone")    (category stone) (density 2500) (youngs-modulus 40.0)  (tension 5)   (across-grain 5)   (compression 60)  (friction 0.60) (restitution 0.50) (color "#D3C8AE") (specific-heat 910) (conductivity 1.3))
(marble    (name "Marble")       (category stone) (density 2700) (youngs-modulus 55.0)  (tension 8)   (across-grain 8)   (compression 100) (friction 0.55) (restitution 0.60) (color "#E4E0D6") (finish veined) (specific-heat 880) (conductivity 2.8))
(granite   (name "Granite")      (category stone) (density 2700) (youngs-modulus 60.0)  (tension 10)  (across-grain 10)  (compression 180) (friction 0.60) (restitution 0.60) (color "#7D7672") (finish crystalline) (specific-heat 790) (conductivity 2.9))
(bronze    (name "Bronze")       (category metal) (density 8800) (youngs-modulus 110.0) (tension 350) (across-grain 350) (compression 350) (friction 0.30) (restitution 0.60) (color "#CC8F4A") (melting 950) (specific-heat 380) (conductivity 26))
(iron      (name "Wrought iron") (category metal) (density 7700) (youngs-modulus 190.0) (tension 330) (across-grain 330) (compression 330) (friction 0.40) (restitution 0.55) (color "#4A4A52") (finish wrought) (melting 1500) (specific-heat 450) (conductivity 59))
(hemp      (name "Hemp rope")    (category fiber) (density 1100) (youngs-modulus 2.0)   (tension 60)  (across-grain 0)   (compression 0)   (friction 0.50) (restitution 0.10) (color "#C7B285") (specific-heat 1400) (conductivity 0.06))
(steel     (name "Hardened steel") (category metal) (density 7850) (youngs-modulus 200.0) (tension 1000) (across-grain 1000) (compression 1000) (friction 0.35) (restitution 0.95) (color "#B8BDC4") (finish polished) (melting 1450) (specific-heat 490) (conductivity 50))

;; Soft metals for pressure shells (#139), handbook values. Lead: pure Pb, tensile ~12 MPa, melts 327.5 C, dead in a
;; collision. Tin: cast pure Sn, ~14 MPa, melts 231.9 C. Copper: annealed, ~220 MPa, melts 1085 C. Bronze (above) is
;; a 88/12 Cu-Sn alloy whose solidus and liquidus straddle 950 C.
(copper    (name "Copper")       (category metal) (density 8960) (youngs-modulus 117.0) (tension 220) (across-grain 220) (compression 220) (friction 0.30) (restitution 0.55) (color "#B8693D") (melting 1085) (specific-heat 385) (conductivity 401))
(lead      (name "Lead")         (category metal) (density 11340) (youngs-modulus 16.0) (tension 12)  (across-grain 12)  (compression 12)  (friction 0.40) (restitution 0.20) (color "#5F6670") (melting 327.5) (specific-heat 129) (conductivity 35))
(tin       (name "Tin")          (category metal) (density 7265) (youngs-modulus 50.0)  (tension 14)  (across-grain 14)  (compression 14)  (friction 0.35) (restitution 0.30) (color "#C9CED3") (melting 231.9) (specific-heat 227) (conductivity 67))

;; Soils: the ground of a map (issue #37). Loose and weak, so their
;; strengths are nominal; friction is the tangent of the angle of repose
;; (dry sand ~32°, Mars regolith ~35°). How fast each soaks up water is the
;; map's to say (#:infiltration), since it depends on how packed and how wet.
(sand      (name "Sand")         (category soil)  (density 1600) (youngs-modulus 0.05)  (tension 0)   (across-grain 0)   (compression 0.1) (friction 0.62) (restitution 0.10) (color "#DBC794") (specific-heat 830) (conductivity 0.25))
(loam      (name "Loam")         (category soil)  (density 1400) (youngs-modulus 0.02)  (tension 0)   (across-grain 0)   (compression 0.1) (friction 0.55) (restitution 0.10) (color "#6B5738") (specific-heat 1000) (conductivity 0.35))
(clay      (name "Clay")         (category soil)  (density 1800) (youngs-modulus 0.03)  (tension 0.05) (across-grain 0.05) (compression 0.2) (friction 0.35) (restitution 0.05) (color "#9E6E4F") (specific-heat 920) (conductivity 0.9))
(regolith  (name "Mars regolith") (category soil) (density 1500) (youngs-modulus 0.05)  (tension 0)   (across-grain 0)   (compression 0.1) (friction 0.70) (restitution 0.10) (color "#8A5A3C") (specific-heat 800) (conductivity 0.039))
;; The Lonely Rover's crater (issue #61): the layers of its ground. Which of them stands as a cliff is the
;; map's to say, by cohesion (#:cohesion), so the two regoliths that differ only in how well their ice holds
;; are the same numbers here. Basalt sand is dark, the sand of the dunes on the floor (glass for the dark kind);
;; silica sand is the pale layer in one bay of the wall (clear glass); bedrock is the wall itself, which the
;; rover can neither climb nor cut; ice-cemented regolith is soil held by ice on the cold wall; sublimed regolith
;; is the same soil after the ice has gone.
(basalt-sand (name "Basalt sand") (category soil) (density 1600) (youngs-modulus 0.05)  (tension 0)   (across-grain 0)   (compression 0.1) (friction 0.62) (restitution 0.10) (color "#3D3838") (specific-heat 840) (conductivity 0.2))
(silica-sand (name "Silica sand") (category soil) (density 1600) (youngs-modulus 0.05)  (tension 0)   (across-grain 0)   (compression 0.1) (friction 0.62) (restitution 0.10) (color "#EBE0C7") (specific-heat 830) (conductivity 0.25))
(bedrock   (name "Bedrock")      (category stone) (density 2700) (youngs-modulus 60.0)  (tension 10)  (across-grain 10)  (compression 180) (friction 0.60) (restitution 0.60) (color "#755C4D") (specific-heat 840) (conductivity 1.7))
(ice-cemented-regolith (name "Ice-cemented regolith") (category soil) (density 1500) (youngs-modulus 0.05) (tension 0) (across-grain 0) (compression 0.1) (friction 0.70) (restitution 0.10) (color "#998582") (specific-heat 1000) (conductivity 1.5))
(sublimed-regolith (name "Sublimed regolith") (category soil) (density 1500) (youngs-modulus 0.05) (tension 0) (across-grain 0) (compression 0.1) (friction 0.70) (restitution 0.10) (color "#BD7347") (specific-heat 800) (conductivity 0.039))
;; Glass (issue #57): soda-lime or fused silica, brittle. Tensile strength of a sound pane ~40 MPa (handbook); a pane is designed to ~7 MPa (see Pane.Strength).
(glass     (name "Glass")        (category stone) (density 2500) (youngs-modulus 70.0)  (tension 40)  (across-grain 40)  (compression 1000) (friction 0.40) (restitution 0.60) (color "#D1EBF2") (finish clear) (specific-heat 840) (conductivity 1.0))

;; Thermal properties (issue #71): specific heat in J/(kg K) and conductivity in W/(m K), handbook values at room
;; temperature unless said (Engineering ToolBox tables and the CRC Handbook of Chemistry and Physics; wood across the
;; grain; dry sand and soils are loose and vary by a factor of two with packing). Mars regolith: k = 0.039 W/(m K)
;; as measured by InSight's HP3 probe (Grott et al. 2021, J. Geophys. Res. Planets 126: 0.039 W/(m K), because the gas in
;; its pores is too thin to carry heat); c = 800 J/(kg K) is a round value for dry basaltic soil (the design check in
;; docs/lonely-rover.html uses it) and density 1500 its bulk density. Basalt: c = 840 (Waples & Waples 2004, 0.84-0.96 kJ/(kg K)
;; at 25 C), k = 1.7 (1.3-2.9 by texture), density 2900: the rock the rover digs and heats in the open.
(basalt    (name "Basalt")       (category stone) (density 2900) (youngs-modulus 70.0)  (tension 10)  (across-grain 10)  (compression 250) (friction 0.60) (restitution 0.55) (color "#4F4B49") (specific-heat 840) (conductivity 1.7))
