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
;; (arm catch 0) looses it (click the arm: "Release the catch"), and the
;; blueprint's demo operator looses it 2 s in. Held level, the catch carries the skein's 150 x (120 - 0) degrees
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

;; The windlass that winches it down again (issue #161): an 8 cm drum on the
;; ground beside the arm's tip, its rope tied 0.9 m out along the arm. Wound
;; in at 15 rpm it hauls the arm down from the crossbeam, twisting the skein,
;; past the slip-hook, which drops in behind it; paid out, the rope lies slack,
;; longer than the arm can pull it in a throw (1.9 m against 1.62 m with the
;; arm on the crossbeam). The slip-hook is one-way (#:catch-side 1).
;;
;; What the skein holds at the catch, worked through: twisted 120 degrees
;; past rest, it stores 1/2 k theta^2 = 1/2 x 150 x 2.0944^2 = 329.0 J, and
;; gives the arm 150 x (2.0944 x 1.4835 - 1.4835^2 / 2) = 301.0 J of it
;; between the catch and the crossbeam (85 degrees). Winching the arm back
;; down from the crossbeam takes that 301.0 J less the 2.59 kg arm's fall,
;; 2.59 x 9.81 x 0.5 sin 85 = 12.7 J: 288.4 J. (Traced: 301.8 J reaches the
;; arm, by the drum's turning less the rope's stretch, 4.6% over: the rope
;; solver doesn't follow the rope winding round the drum as its angle of
;; departure swings 60 degrees, about r x 1 rad x 300 N = 24 J.) Each time
;; the arm comes back to the hook carrying 150 x 2.094 less the arm's 12.7 =
;; 301.5 N.m (traced 301.4), and laid in the sling again the stone flies as
;; the first did: 12.42 m/s, first touching down 15.24 m from where it hung,
;; four times in a row to the millimetre.
(define drum-r (cm 8))
(define span-at (m 0.9))
(define drum-at (list span-at (m 0.13) (m 0.3)))   ; beside the stone, clear of it
(define span-length (m 1.9))

(define-machine torsion-catapult
  #:source "Ammianus Marcellinus, Res Gestae XXIII.4 (the onager)"
  ;; the arm starts winched down level, pointing back (+X)
  (lever arm #:at (0 pivot-y 0) #:length arm-length #:material oak #:pivot-fraction 0
         #:section (cm 6) #:start-angle-deg 0 #:limit-lower-deg -5 #:limit-upper-deg stop-deg
         #:spring-stiffness 150 #:spring-rest-deg rest-deg #:damping 0.1
         #:catch-deg 0 #:catch-side 1)
  ;; the stone hangs in the sling below the arm's tip, just clear of the ground
  (block stone #:at (arm-length (- pivot-y sling) 0) #:size stone-size #:material granite)
  (rope sling-rope #:from (arm arm-length 0 0) #:to (stone 0 0 0) #:length sling
        #:release-deg 30 #:diameter (cm 1))
  (wheel windlass #:shape (drum #:radius drum-r #:length (cm 20)) #:at ((car drum-at) (cadr drum-at) (caddr drum-at))
         #:material oak #:drive-rpm 15 #:drive-torque 0)
  (rope span #:wind-on windlass #:to (arm span-at 0 0) #:length span-length #:diameter (cm 1.5))
  ;; one full cycle (#161): loosed at 2 s; winch it down past the hook (11.8 s), pay the rope out as long at twice
  ;; the speed, lay the stone in the sling and loose it again
  (operator (at 2 (arm catch 0))
            (at 5 (arm catch 1)) (at 5 (windlass drive-torque 100))
            (at 16.8 (windlass drive-rpm -30)) (at 22.7 (windlass drive-rpm 0))
            (at 23.5 (sling-rope load 1)) (at 25 (arm catch 0))))
