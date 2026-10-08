#lang heroic
;; A counterweight trebuchet (medieval — not ancient, but the same lever
;; as the machines before it, taken as far as it goes). A heavy
;; counterweight hangs on a short chain from the arm's short end; the
;; long end carries a sling, and the stone lies on the ground behind the
;; machine at the sling's end.
;;
;; Let go, the counterweight falls and whips the arm round; the sling
;; lets the stone trail behind the arm's tip, then swing round faster
;; than the tip itself — the sling is a second, longer lever on the end
;; of the first. The sling's loop slips off its release pin once it has
;; swung close to in line with the arm (#:release-deg), and the stone
;; flies on its own momentum, released at about 0.95 s after the catch lets go. Traced, let go at once, it rests
;; 13.1 m out: roughly a tenth of the machine's 856 J in the stone
;; (1.4 kg thrown 13 m needs ~90 J). Real trebuchets reach 30–60%,
;; mostly because their arms don't slam into a stop mid-throw. (Before the
;; rope fix for issue #45 it landed ~6 m out: the chain's stretch
;; correction was jerking the arm and wasting the throw. Until #80 the chain
;; stretched as much as 0.75 m, let the counterweight fall to the ground,
;; and yanked it back up with energy that came from nowhere; the 16 m it
;; landed then was partly that. The chain now holds the counterweight's
;; centre at least 0.63 m up, the short arm's lowest reach less the chain.)
;;
;; Throws toward -X. Everything here is rope, hinge and falling weight —
;; nothing scripts the flight.
;;
;; It starts held on its catch (issue #155), as a real one waits for the
;; trigger to be pulled: (arm catch 0) lets it go (click the arm: "Release the
;; catch"), and the blueprint's demo operator pulls it 3 s in. Held, the catch carries the counterweight's
;; moment about the axle, 72.9 kg (granite, 0.3 m a side) x 9.81 x 0.27 m x
;; cos 50 = 124.1 N.m, less the beam's own, which leans the other way: 7.13 kg
;; of oak (1.8 x 0.025 x 0.22 m) with its middle 0.63 m out on the long side,
;; 7.13 x 9.81 x 0.63 x cos 50 = 28.3 N.m. So 95.8 N.m (arm catch-load). Let
;; go after the counterweight has settled on its chain, it throws as it did
;; let go at once: the stone first touches down the same distance out
;; (traced: 9.53 m from where it lay, both ways). Where it comes to rest is
;; another matter: a cube's first bounce goes as it lands, and held 3 s it
;; bounces nearly straight up and stops 9.1 m from the axle, where let go at
;; once it skids on to the 13.1 m above.
(require racket/math)

(define arm-length (m 1.8))
(define pivot-fraction 0.15)                         ; short arm = 15% of the beam
(define short-arm (* pivot-fraction arm-length))     ; 0.27 m
(define long-arm (- arm-length short-arm))           ; 1.53 m
(define pivot (list 0 (m 1.4)))
(define cocked-deg -50)                              ; long end down and back, ready to throw
(define c (degrees->radians cocked-deg))
(define (on-arm d) (list (+ (car pivot) (* d (cos c))) (+ (cadr pivot) (* d (sin c)))))

(define cw-size (m 0.3))                             ; granite: 73 kg
(define chain (cm 35))                                ; long enough that the weight clears the beam
(define short-end (on-arm (- short-arm)))
(define cw-at (list (car short-end) (- (cadr short-end) chain (/ cw-size 2))))

(define stone-size (cm 8))                           ; granite: 1.4 kg — about 1:50 to the counterweight
(define sling (m 1.2))
(define tip (on-arm long-arm))
(define stone-y (/ stone-size 2))
(define stone-at (list (+ (car tip) (sqrt (- (sqr sling) (sqr (- (cadr tip) stone-y))))) stone-y))

;; The windlass that spans it again (issue #161): a 10 cm drum on the ground
;; under the long arm, its rope tied 1 m out along the arm. Wound in at 15
;; rpm it hauls the long end down and back past the catch, a one-way catch
;; (#:catch-side 1) that lets the arm by going back and drops in behind it;
;; paid out again (at -30 rpm for half as long), the 2.6 m rope lies slack,
;; longer than the arm can pull it in a throw (2.32 m at its stop).
;;
;; Spanning, worked through: thrown, the arm comes to rest hanging with the
;; short end straight down (+90 degrees; the counterweight's 19.7 kg.m beats
;; the beam's 4.5 on the other side). Back on the catch at -50 the short end
;; is 0.27 (sin 90 + sin 50) = 0.4768 m higher, and so the counterweight:
;; 72.9 x 9.81 x 0.4768 = 341.0 J. The beam's middle comes down 0.63 (1 +
;; sin 50) = 1.1126 m, giving back 7.13 x 9.81 x 1.1126 = 77.8 J; so the
;; windlass does 263.2 J. Its rope comes in at 2 pi x 0.1 x 15/60 = 0.157 m/s;
;; from 2.6 m to the 0.40 m it has with the arm at -52 is 2.20 m: 14 s, the
;; last 1.86 m of it (from the 2.29 m the rope reaches with the arm hanging)
;; hauling, 11.9 s. That is 263 J in 11.9 s, 22 W: a man at a windlass gives
;; ~75 W for a while, so at his pace, not the drum's, 263/75 = 3.5 s. Its
;; most, 100 N.m, is six times the ~16 N.m the rope asks (about 160 N at 0.1 m).
;;
;; Then the stone is laid in the sling's pouch again ((sling-rope load 1),
;; or dragged there and let go) and the catch let go: it throws as the
;; first did, first touching down 9.53 m from where it lay (the 13.1 m
;; above is where it ends up, from the axle), within 2% every time -- once
;; the counterweight has been steadied. Nothing in the chain damps it, and
;; left swinging the 5-7 cm it keeps from the throw before, the next throw
;; lands 5-7% further (traced 10.22 and 9.99 m); a hand holding it still for
;; a moment puts every throw back at 9.526 m. The demo operator below does
;; one cycle, without that hand.
(define span-at 1.0)                                 ; m out along the long arm
(define drum-r (cm 10))
(define drum-at (list (* span-at (cos c)) (m 0.2)))  ; straight under the tie when cocked
(define span-length (m 2.6))

(define-machine trebuchet
  #:source "Classic mechanics demonstration (medieval; its lever is Archimedes')"
  (lever arm #:at ((car pivot) (cadr pivot) 0) #:length arm-length #:material oak
         #:pivot-fraction pivot-fraction #:start-angle-deg cocked-deg #:limit-deg 140 #:damping 0.2
         #:catch-deg cocked-deg #:catch-side 1)
  (block counterweight #:at ((car cw-at) (cadr cw-at) 0) #:size cw-size #:material granite)
  (rope cw-chain #:from (arm (- short-arm) 0 0) #:to (counterweight 0 (/ cw-size 2) 0)
        #:length chain #:material iron #:diameter (cm 1.5))
  (block stone #:at ((car stone-at) (cadr stone-at) 0) #:size stone-size #:material granite)
  (rope sling-rope #:from (arm long-arm 0 0) #:to (stone 0 0 0) #:length sling
        #:release-deg 60 #:diameter (cm 1))
  (wheel windlass #:shape (drum #:radius drum-r #:length (cm 30)) #:at ((car drum-at) (cadr drum-at) 0) #:material oak
         #:drive-rpm 15 #:drive-torque 0)
  (rope span #:wind-on windlass #:to (arm span-at 0 0) #:length span-length #:diameter (cm 1.5))
  ;; one full cycle (#161): loosed at 3 s by its catch; set the catch and wind it back down, pay the rope out,
  ;; lay the stone in the sling and loose it again
  (operator (at 3 (arm catch 0))
            (at 12 (arm catch 1)) (at 12 (windlass drive-torque 100))
            (at 26 (windlass drive-rpm -30)) (at 33 (windlass drive-rpm 0))
            (at 33.5 (sling-rope load 1)) (at 36 (arm catch 0))))
