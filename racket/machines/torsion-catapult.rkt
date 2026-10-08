#lang heroic
;; The onager: a late-Roman one-armed stone-thrower (Ammianus Marcellinus,
;; Res Gestae XXIII.4, 4th century AD), named for the wild ass's kick.
;; A single arm stands in a horizontal skein of twisted sinew rope. The
;; crew winch the arm down and back, twisting the skein further; loosed,
;; the skein flings the arm up until it slams into a padded crossbeam near
;; upright, and the stone, in a sling on the arm's tip, whips round and
;; flies on.
;;
;; The skein is a torsion spring: it pulls the arm back toward where it
;; would rest untwisted, with torque −k·(θ − rest). It's wound so that
;; rest lies past the crossbeam — the skein still pulls when the arm is
;; against it. The sling lets go once it has whipped round close to in
;; line with the arm (#:release-deg), like a trebuchet's. Ammianus gives no
;; skein stiffness; 150 N·m per radian is an estimate. Throws toward −X.
;;
;; It starts winched down and held on its slip-hook, a catch (issue #155):
;; (arm catch 0) looses it, and until demo operators exist it looses itself
;; 2 s in. Held level, the catch carries the skein's 150 x (120 - 0) degrees
;; = 150 x 2.094 = 314.2 N.m, less the weights hanging on the arm: its own
;; 2.59 kg of oak (6 cm square, 1 m) at 0.5 m, 12.7 N.m, and the 2.70 kg
;; stone at the tip, 26.5 N.m. So 275.0 N.m (arm catch-load).
(require racket/math)

(define pivot-y (m 0.6))
(define arm-length (m 1.0))
(define stop-deg 85)                     ; the padded crossbeam, just short of upright
(define rest-deg 120)                    ; untwisted: past the crossbeam
(define sling (m 0.45))
(define stone-size (cm 10))              ; granite: 2.7 kg

(define-machine torsion-catapult
  #:source "Ammianus Marcellinus, Res Gestae XXIII.4 (the onager)"
  ;; the arm starts winched down level, pointing back (+X)
  (lever arm #:at (0 pivot-y 0) #:length arm-length #:material oak #:pivot-fraction 0
         #:section (cm 6) #:start-angle-deg 0 #:limit-lower-deg -5 #:limit-upper-deg stop-deg
         #:spring-stiffness 150 #:spring-rest-deg rest-deg #:damping 0.1
         #:catch-deg 0 #:release-after 2)
  ;; the stone hangs in the sling below the arm's tip, just clear of the ground
  (block stone #:at (arm-length (- pivot-y sling) 0) #:size stone-size #:material granite)
  (rope sling-rope #:from (arm arm-length 0 0) #:to (stone 0 0 0) #:length sling
        #:release-deg 30 #:diameter (cm 1)))
