#lang heroic
;; Heron's fountain, drawn the way Hero drew it: one stacked, cut-away vessel. An
;; open basin on top, the sealed supply (water) chamber in the middle, the
;; sealed receiver (air) at the bottom. Water poured into the basin runs down
;; a pipe to the bottom of the receiver, squeezing the air it shares with the
;; supply; that air pushes the supply's water up the nozzle, out of the basin
;; and higher than where it started, with no pump.
;;
;; The rule the scene draws: water that falls a height h (the basin's surface
;; to the receiver's) lifts the jet about h above the supply's surface. The
;; drain pipe ends on the receiver's floor, so the sim's head across it is
;; exactly that fall less the air's pressure head P/(rho g); the nozzle sees
;; the supply's surface plus P/(rho g). Heights (m above the ground):
;;   receiver 0 to 0.12 (2.7 L: 0.0225 m2), neck 0.05, supply 0.17 to 0.41
;;   (5.4 L), neck 0.05, basin 0.46 to 0.56 (9 L: 0.09 m2), nozzle tip 0.55.
;; Starting: basin 6 L (6.7 cm), supply 4 L (17.8 cm), receiver empty, so the
;; fall is 0.46 + 0.0667 = 0.527 m and the supply's surface stands at 0.348 m.
;;
;; The jet's first rise. The air is stiff, so within a second the water the
;; drain gives the receiver is the water the nozzle takes from the supply:
;; Gd (F - P/rho g) = Gn J, with J = ss + P/rho g - tip the rise over the tip:
;;   J = (F + ss - tip) / (1 + Gn / Gd) = (0.527 + 0.348 - 0.55) / (1 + 0.7/4)
;;     = 0.3245 / 1.175 = 27.6 cm if the air were rigid. Measured: 26.2 cm at
;; 0.5 s (a few mL are already in the receiver), then the same formula on the
;; levels the run is at holds to 0.05 cm: 24.28 against 24.24 at 2 s, 15.93
;; against 15.90 at 10 s, 10.45 against 10.43 at 18 s. The jet falls as the
;; receiver's surface climbs and the supply's drops, each lowering F + ss.
;;
;; Poured again. The receiver (2.7 L) fills: r runs as the supply empties,
;; dr/dt = Gn J, so r = r_inf (1 - e^(-t/tau)), tau = 11.25 (1.175) / 0.7 =
;; 18.9 s and r_inf = 11.25 (0.325 + 0.17/22.5) = 3.735 L (0.17 L is the
;; squeeze, 4.2 L of air at 4.3 kPa): full at 24.2 s worked, 24.0 s measured.
;; The drain then stops, and the pressure, no longer held up by the falling
;; water, sags as the supply keeps giving water to the basin; the jet dies when
;; ss + P/rho g = tip (the "lifts" bracket reaches the nozzle's tip and no
;; further). With the receiver full the air is the supply's 5.4 - (4 - s) = 1.4 + s
;; L over its water plus the 0.1 L tube: V = 1.5 + s L, from 4.2 L at the start.
;; Boyle: P = 101.325 (4.2 / V - 1), and 0.17 + (4 - s)/22.5 + P/9.81 = 0.55
;; gives s = 2.5751 L down the nozzle: the supply left with 1.4249 L, the basin
;; 6 - 2.7 + 2.5751 = 5.8751 L, the air 4.0751 L at 3.1065 kPa (measured 1.4249,
;; 5.8751, 3.1065). The jet is under 0.5 cm by 27 s.
;; To work it again somebody tips out the receiver, pours the supply back up to
;; 4 L and fills the basin to 6 L. The demo operator does all three at 36 s
;; (Shift+click the receiver: "Empty the tank"; set supply.water to 4 and
;; basin.water to 6 in the right-click list, since "Pour 10 L" would put 5.4 L
;; in the supply, its brim, and it would then jet harder). Everything is where it
;; was at 0 s, so the second jet is the first again.
;;
;; Sized deliberately small: a fountain scaled for a real courtyard would take
;; many real minutes to visibly drain even sped up, since a large sealed air
;; volume barely compresses for the same litre of water moved. This one holds
;; only what it needs to make the whole draw-down-and-slow story finish in well
;; under a minute, even before the speed control.

(define-machine herons-fountain
  #:source "Hero of Alexandria, Pneumatica"
  (tank basin #:at (0 0.46 0) #:area 0.09 #:height (cm 10) #:water (L 6)
        (port drain #:height 0)
        (port jet #:height (cm 9)))
  (tank supply #:at (0 0.17 0) #:area 0.0225 #:height (cm 24) #:water (L 4)
        (port outlet #:height 0))
  (tank receiver #:at (0 0 0) #:area 0.0225 #:height (cm 12)
        (port inlet #:height 0))
  (pipe drain basin.drain receiver.inlet #:conductance 4e-3)
  (pipe nozzle supply.outlet basin.jet #:conductance 7e-4 #:jet #t)
  ;; The air tube joins the tops of the two sealed vessels. Small on
  ;; purpose: less spare air volume means the same litre of draining
  ;; water raises pressure more, and faster.
  (sealed-air (supply receiver) #:tube (L 0.1))
  ;; emptied, refilled and poured again once the jet has died (issue #157); taking any control stops it
  (operator (at 36 (receiver water 0)) (at 36 (supply water 4)) (at 36 (basin water 6))))
