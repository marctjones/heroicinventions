#lang racket/base
;; Machines that can't do their job alone (issue #157): each has a demo operator in its blueprint, controls the
;; header names, and a cycle whose number is worked out in the header before it is run. Every test here plays the
;; blueprint's OWN operator, read back from its compiled .machine file, as an operator log (#:actions): the same
;; timed settings the game's demo plays. (Not the game's demo path itself: Main.Operator.cs speeds a machine that is
;; not in DefaultSpeeds up to 20x, and a rigid-body machine's physics is wrong there; see the report on #157.)
(require rackunit racket/list racket/math racket/file racket/string racket/runtime-path
         heroic/simhost heroic/godothost heroic/materials)

(define-runtime-path compiled-machines "../../../game/machines")
(define-runtime-path controls-source "../../../game/scripts/Controls.cs")

(define (machine-form name) (call-with-input-file (build-path compiled-machines (format "~a.machine" name)) read))

;; the blueprint's operator as an operator log: ((at t (part field value)) ...)
;; a blueprint's demo operator as an action log; #:until keeps the actions a short run reaches (a thrower's demo goes
;; on to re-span and reload, #161)
(define (demo-actions name #:until [until +inf.0])
  (define op (for/first ([f (cddr (machine-form name))] #:when (and (pair? f) (eq? (car f) 'operator))) f))
  (unless op (error 'demo-actions "~a has no operator" name))
  (for/list ([a (cdr op)] #:when (and (pair? a) (eq? (car a) 'at) (<= (cadr a) until))) a))

(define (value run key t)
  (define frame (for/fold ([best (car run)]) ([f (cdr run)]) (if (< (abs (- (car f) t)) (abs (- (car best) t))) f best)))
  (cadr (assq key (cdr frame))))

;; the first time at or after `from` that key satisfies pred
(define (first-time run key pred [from 0])
  (for/first ([f run] #:when (and (>= (car f) from) (let ([v (assq key (cdr f))]) (and v (pred (cadr v)))))) (car f)))

(define (between run key from to) (for/list ([f run] #:when (<= from (car f) to)) (cadr (assq key (cdr f)))))
(define (spread xs) (- (apply max xs) (apply min xs)))

(define (machine-inertia machine part)
  (define form (machine-form machine))
  (define p (for/first ([c (cddr form)] #:when (and (pair? c) (eq? (car c) 'part) (eq? (cadr c) part))) c))
  (define (field l k) (for/first ([x l] #:when (and (pair? x) (eq? (car x) k))) (cadr x)))
  (* (material-field (assq (field p 'material) (material-table)) 'density) (field (cdr (assq 'props (cdddr p))) 'inertia-z)))

;; ---------------------------------------------------------------------------------------------------------------
(define listed '(tank-leaks gristmill water-clock sand-timer herons-fountain greenhouse heron-temple-doors dam-break
                 hillside-pond roman-crane bar-crane ratchet-windlass walkers-wheel trebuchet torsion-catapult))

(test-case "Every machine in the issue has a demo operator whose every action has a click control in Controls.cs"
  (define controls (file->string controls-source))
  (for ([name listed])
    (define form (machine-form name))
    (define actions (demo-actions name))
    (check-true (pair? actions) (format "~a has an operator" name))
    (for ([a actions])
      (define-values (target field) (values (car (caddr a)) (cadr (caddr a))))
      ;; a part's kind, or "rope" for a rope (as Controls.KindOf names it)
      (define part (for/first ([c (cddr form)] #:when (and (pair? c) (memq (car c) '(part rope)) (eq? (cadr c) target))) c))
      (check-true (and part #t) (format "~a: ~a is a part or rope" name target))
      (when part
        (define kind (if (eq? (car part) 'rope) "rope" (symbol->string (caddr part))))
        ;; a Controls.cs row is Switch("kind kind", "field", ...) or new(["kind", ...], "field", ...)
        (define row
          (for/first ([line (string-split controls "\n")]
                      #:when (and (regexp-match? (regexp (format "\"~a\"" (regexp-quote (symbol->string field)))) line)
                                  (regexp-match? (regexp (format "(Switch\\(\"([a-z-]+ )*~a( [a-z-]+)*\"|new\\(\\[([^]]*\")~a\")" kind kind)) line)))
            line))
        (check-true (and row #t) (format "~a: a Controls.cs row for ~a.~a (a ~a)" name target field kind))))))

;; ---------------------------------------------------------------------------------------------------------------
;; tank-leaks.rkt: the thumb on the hole at 100 s

(test-case "Tank leaks: the thumb at 100 s leaves 0.1 + (sqrt 0.7 - 2.658e-3 x 100)^2 = 42.59 cm, 106.5 L, and the rest in the catch tank"
  (define run (simulate 'tank-leaks #:seconds 400 #:step 0.01 #:sample-dt 10 #:actions (demo-actions 'tank-leaks)))
  (define h (+ 0.1 (sqr (- (sqrt 0.7) (* 2.658e-3 100)))))
  (check-= h 0.4259 1e-4)
  (check-= (value run 'plug-hole.area 90) 5 0 "the hole is open before")
  (check-= (value run 'plug-hole.area 110) 0 0 "and plugged after")
  (check-= (value run 'plugged.level 100) (* 100 h) 0.05)
  (check-= (value run 'plugged.water 400) (* 1000 0.25 h) 0.2 "L in the barrel")
  (check-= (value run 'plugged.level 400) (value run 'plugged.level 110) 1e-6 "nothing leaks after")
  (check-= (value run 'catch.water 400) (- 200 (* 1000 0.25 h)) 0.2 "every litre out is in the catch tank")
  (check-= (value run 'catch.level 400) (/ (- 200 (* 1000 0.25 h)) 5) 0.05 "18.7 cm over 0.5 m2"))

;; ---------------------------------------------------------------------------------------------------------------
;; gristmill.rkt: fix the weak stone 5 s in

(test-case "Gristmill: the weak stone's grind-torque set to 100 at 5 s spins it up at 100 / I and it grinds 54 kg/kWh x 100 x 12.566 W"
  (when (godot-available?)
    (define run (godot-simulate 'gristmill #:seconds 60 #:sample-dt 1 #:actions (demo-actions 'gristmill)))
    (define I (machine-inertia 'gristmill 'weak))
    (define alpha (/ (- 200 100) I))
    (define w (* 120 (/ (* 2 pi) 60)))
    (check-= I 175 5 "kg m2: the stone's own")
    (check-= (value run 'weak.omega 4) 0 0.02 "not turning before the fix")
    (check-= (value run 'weak.grind-torque 20) 100 0 "the stones set lighter")
    (check-= (value run 'weak.omega 15) (* alpha 10) (* 0.02 alpha 10) (format "spinning up at ~a rad/s2" alpha))
    (check-= (first-time run 'weak.omega (λ (v) (> v (* 0.99 w))) 5) (+ 5 (/ w alpha)) 3 "up to speed 12.566 / alpha s after the fix")
    (check-= (value run 'weak.omega 60) w 0.05 "120 rpm")
    (check-= (value run 'weak.grinding-power 60) (* 100 w) 5 "1,257 W")
    (check-= (- (value run 'weak.flour 60) (value run 'weak.flour 50)) (* 10 100 w (/ 54 3.6e6)) 0.005 "kg of flour in 10 s")
    (check-= (value run 'sharp.omega 60) w 0.02 "the others are unharmed")))

;; ---------------------------------------------------------------------------------------------------------------
;; water-clock.rkt: the receiver tipped out at 410 s

(test-case "Water clock: the receiver fills in 404.5 s, is emptied at 410 s, and the clock goes again, full at 814.5 s"
  (define run (simulate 'water-clock #:seconds 830 #:step 0.05 #:sample-dt 1 #:actions (demo-actions 'water-clock)))
  (define rate 0.2967)   ; cm/s
  (check-= (/ 120 rate) 404.5 0.1)
  (check-= (value run 'receiver.level 100) (* rate 100) 0.1)
  (check-= (value run 'receiver.level 400) (* rate 400) 0.2 "nearly full")
  (check-= (value run 'receiver.level 409) 120 0.01 "full, and held there")
  (check-= (value run 'receiver.level 411) (* rate 1) 0.5 "tipped out at 410 s")
  (check-= (value run 'receiver.level 510) (* rate 100) 0.2 "29.67 cm 100 s after: the same clock again")
  (check-= (value run 'receiver.level 810) (* rate 400) 0.3)
  (check-= (first-time run 'receiver.level (λ (v) (>= v 119.99)) 411) 814.5 1.0 "full again 404.5 s after")
  (check-= (value run 'reservoir.level 800) 41.08 0.01 "the head never noticed"))

;; ---------------------------------------------------------------------------------------------------------------
;; sand-timer.rkt: turned over at 200 s

(test-case "Sand timer: empty at 193.0 s, turned over at 200 s, empty again at 393.0 s"
  (define run (simulate 'sand-timer #:seconds 420 #:step 0.05 #:sample-dt 1 #:actions (demo-actions 'sand-timer)))
  (check-= (first-time run 'sand.empty (λ (v) (> v 0.5))) 193.0 1.5 "first frame (1 s apart) that is empty")
  (check-= (value run 'sand.mass 199) 0 0 "nothing left to run")
  (check-= (value run 'sand.mass 201) 5 0.03 "turned: the 5 kg are above the orifice again")
  (check-= (value run 'sand.level 201) (- 31.25 0.1619) 0.02 "31.25 cm, less the second it has run")
  (check-= (first-time run 'sand.empty (λ (v) (> v 0.5)) 201) 393.0 1.5 "193.0 s after the turn")
  (check-= (value run 'sand.mass 296.5) 2.5 0.05 "half left at half time, as the first run")
  (check-= (value run 'water.level 250) (value run 'water.level 199) 1e-6 "the water tank beside it, empty, can't be turned"))

;; ---------------------------------------------------------------------------------------------------------------
;; herons-fountain.rkt: the basin poured again

;; The worked numbers of herons-fountain.rkt's header, from its geometry alone. Heights in m, volumes in L.
;; basin 0.09 m2 on a floor at 0.46; supply 0.0225 m2 on 0.17 (tip of the nozzle 0.09 over the basin's floor);
;; receiver 0.0225 m2 from 0; drain 4e-3 and nozzle 7e-4 m3/s per m of head; air 5.4 - 4 + 2.7 + 0.1 = 4.2 L at the start.
(define hf-tip (+ 0.46 0.09))
(define hf-gn/gd (/ 7e-4 4e-3))
(define (hf-fall vb vr) (- (+ 0.46 (/ vb 90)) (/ vr 22.5)))                 ; basin surface to receiver surface
(define (hf-supply-surface vs) (+ 0.17 (/ vs 22.5)))
;; the air is stiff, so the drain gives the receiver what the nozzle takes from the supply: Gd (F - P/rho g) = Gn J,
;; J = ss + P/rho g - tip, so J = (F + ss - tip) / (1 + Gn/Gd): the jet's rise over the nozzle, in cm
(define (hf-rise vb vr vs) (* 100 (/ (- (+ (hf-fall vb vr) (hf-supply-surface vs)) hf-tip) (+ 1 hf-gn/gd))))
;; the receiver full (2.7 L): the air is 1.5 + s L (s the litres the nozzle has given), P = 101.325 (4.2 / V - 1) kPa, and the jet is
;; gone when ss + P/rho g = tip. Bisection on s.
(define (hf-end-pressure s) (* 101.325 (- (/ 4.2 (+ 1.5 s)) 1)))
(define hf-end-s
  (let loop ([lo 0.0] [hi 4.0] [n 60])
    (define mid (/ (+ lo hi) 2))
    (define excess (- (+ (hf-supply-surface (- 4 mid)) (/ (hf-end-pressure mid) 9.81)) hf-tip))   ; > 0: still lifting past the tip
    (cond [(zero? n) mid] [(> excess 0) (loop mid hi (sub1 n))] [else (loop lo mid (sub1 n))])))
;; the time the receiver fills: r runs as the supply empties (r = s + d, d the squeeze), dr/dt = Gn J, J falling by
;; (r + s) / 22.5 over 1 + Gn/Gd: r(t) = r_inf (1 - exp(-t / tau)) with tau = 11.25 (1 + Gn/Gd) / (1000 Gn) and r_inf = 11.25 (K0 + d / 22.5)
(define hf-squeeze 0.17)   ; L: 4.2 L of air at about 4.3 kPa is compressed by 4.2 x 4.3 / 105.6
(define hf-tau (/ (* 11.25 (+ 1 hf-gn/gd)) (* 1000 7e-4)))
(define hf-r-inf (* 11.25 (+ (- (+ (hf-fall 6 0) (hf-supply-surface 4)) hf-tip) (/ hf-squeeze 22.5))))
(define hf-fill-time (* (- hf-tau) (log (- 1 (/ 2.7 hf-r-inf)))))

(test-case "Heron's fountain: its jet is the supply's height over the receiver's less the nozzle's rise over the basin, fills the receiver in 24 s, ends where Boyle says; emptied, refilled and poured again at 36 s it does it again"
  (define run (simulate 'herons-fountain #:seconds 100 #:step 0.05 #:sample-dt 0.5 #:actions (demo-actions 'herons-fountain)))
  (define first-peak (apply max (between run 'nozzle.jet-height 0 35)))
  (define second-peak (apply max (between run 'nozzle.jet-height 36 100)))
  ;; the jet's first rise: 27.6 cm if the air were rigid; a few mL have moved by the first half second
  (define rise0 (hf-rise 6 0 4))
  (check-= rise0 27.6 0.05 "worked: (0.527 + 0.348 - 0.55) / 1.175")
  (check-= first-peak rise0 2.0 (format "the first rise measured ~a cm against the worked ~a" first-peak rise0))
  (check-= second-peak first-peak (* 0.03 first-peak) "the second jet is the first again")
  ;; and it holds as the levels move, to 0.1 cm: the same formula on the levels the run is at
  (for ([t '(2 6 10 14 18)])
    (check-= (value run 'nozzle.jet-height t)
             (hf-rise (value run 'basin.water t) (value run 'receiver.water t) (value run 'supply.water t))
             0.1 (format "jet at ~a s" t)))
  ;; the receiver fills at about the worked time, then the drain stops
  (define filled (first-time run 'receiver.water (λ (v) (> v 2.699))))
  (check-= hf-fill-time 24.2 0.05 "worked time to fill the receiver")
  (check-= filled hf-fill-time 1.0 (format "receiver full at ~a s measured against the worked ~a" filled hf-fill-time))
  ;; the end: the receiver 2.7 L, the supply 4 - s, the basin 6 - 2.7 + s, the air 1.5 + s L at 101.325 (4.2 / V - 1) kPa
  (define s hf-end-s)
  (check-= s 2.5751 0.0001 "worked: litres the nozzle gives before the lift falls to the tip")
  (check-= (hf-end-pressure s) 3.1065 0.0005 "kPa (Boyle)")
  (for ([t '(35.5 99)])
    (check-= (value run 'supply.water t) (- 4 s) 0.002 (format "supply down to ~a L at ~a s" (- 4 s) t))
    (check-= (value run 'receiver.water t) 2.7 0.001)
    (check-= (value run 'basin.water t) (+ 3.3 s) 0.002)
    (check-= (value run 'supply.air-pressure t) (hf-end-pressure s) 0.002 "kPa"))
  (check-= (value run 'receiver.water 37) (value run 'receiver.water 1) 0.03 "poured again: the receiver fills from empty as it did")
  (check-true (< (value run 'nozzle.jet-height 35) 0.5) "the first jet had died by 36 s"))

;; ---------------------------------------------------------------------------------------------------------------
;; greenhouse.rkt: the trees coppiced onto the stove at 600 s

(test-case "Greenhouse: coppiced at 600 s, 3.181e-7 x 600 kg of wood burn in 2.86 s at 1 kW and the trees begin again"
  (define run (simulate 'greenhouse #:seconds 620 #:step 0.05 #:sample-dt 0.25
                        #:set '((scene clock-rate 0)) #:actions (demo-actions 'greenhouse)))
  (define net 3.181e-7)
  (define wood (* net 600))
  (check-= wood 1.909e-4 1e-7)
  (check-= (value run 'trees.wood 599.5) (* net 599.5) 1.5e-6 "kg grown")
  (check-= (value run 'trees.oxygen 599.5) (* (/ (* 6 0.031998) 0.16214) (value run 'trees.wood 599.5)) 1e-9 "1.1841 kg of O2 a kg")
  (check-= (value run 'stove.fuel 599.5) 0 1e-12 "nothing on the stove before")
  (check-= (value run 'stove.fuel 600.25) (- wood (* 0.25 (/ 1000 15e6))) 3e-6 "stacked on the stove")
  (check-= (first-time run 'stove.fuel (λ (v) (< v 1e-9)) 601) (+ 600 (/ (* wood 15e6) 1000)) 0.5 "burned out 2.86 s later")
  (check-true (< (value run 'trees.wood 606) (* net 7)) "the trees start again from nothing"))

;; ---------------------------------------------------------------------------------------------------------------
;; heron-temple-doors.rkt: the fire doused at 1000 s

(test-case "Temple doors: open from about 745 s, the fire doused at 1000 s, the doors shut about 1228 s (integrated by hand), within 2%"
  (define p0 101325.0) (define r-air 287.05)
  (define (kelvin c) (+ c 273.15))
  (define v0 (+ 0.075 0.020 0.0005))
  (define air-mass (/ (* p0 v0) (* r-air (kelvin 20))))
  (define tau (/ (+ 1900 (* air-mass 718)) 3))
  (define douse 1000.0)
  (define (temp t)
    (define (heating t) (+ 20 (* 30 (- 1 (exp (- (/ t tau)))))))
    (if (< t douse) (heating t) (+ 20 (* (- (heating douse) 20) (exp (- (/ (- t douse) tau)))))))
  (define (imbalance dv T open?)
    (define base (if open? (- 0.6 (* 0.05 (/ pi 2))) 0.6))
    (- (- (/ (* air-mass r-air (kelvin T)) (+ v0 dv)) p0)
       (* 1000 9.81 (- (+ base (/ dv 0.04)) (- 0.2 (/ dv 0.1))))))
  ;; a quasi-steady column through the siphon (2e-4 m3/s per m), the bucket lowered while the doors are open
  (define-values (opens shuts)
    (let loop ([t 0.0] [dv 0.0] [open? #f] [opened #f] [closed #f])
      (cond [(> t 2000) (values opened closed)]
            [else
             (define flow (* 2e-4 (/ (imbalance dv (temp t) open?) (* 1000 9.81))))
             (define dv2 (max 0 (+ dv (* flow 0.5))))
             (define now-open (if open? (>= dv2 0.002) (> dv2 0.002)))
             (loop (+ t 0.5) dv2 now-open (or opened (and now-open (not open?) t)) (or closed (and open? (not now-open) t)))])))
  (check-= opens 745 5 "the hand figure for opening")
  (check-= shuts 1228 5 "and for shutting")
  (check-= (temp 1000) 43.4 0.05)
  (define run (simulate 'heron-temple-doors #:seconds 1400 #:step 0.05 #:sample-dt 1 #:actions (demo-actions 'heron-temple-doors)))
  (define opened (first-time run 'doors.angle (λ (v) (> v 1))))
  (define shut (first-time run 'doors.angle (λ (v) (< v 1)) 1001))
  (check-= opened opens (* 0.02 opens) (format "opened at ~a s, by hand ~a" opened opens))
  (check-= shut shuts (* 0.02 shuts) (format "shut at ~a s, by hand ~a" shut shuts))
  (check-= (value run 'doors.angle 999) 90 0 "wide open when the fire is doused")
  (check-= (value run 'offering.power 1001) 0 0 "doused")
  (check-= (value run 'altar.air-temperature 1000) (temp 1000) 0.15 "43.3 to 43.4 C")
  (check-= (value run 'doors.angle 1300) 0 0)
  (check-true (> (- shut 1000) 150) (format "shut ~a s after the fire went out, not at once" (- shut 1000))))

;; ---------------------------------------------------------------------------------------------------------------
;; dam-break.rkt and hillside-pond.rkt: the sluice drawn by hand

(test-case "Dam break: the gate drawn by hand at 15 s (pond 53.75 cm) lets 167 L/s go and the front is at the low pond 13.8 to 32.5 s later"
  (define run (simulate 'dam-break #:seconds 60 #:step 0.01 #:sample-dt 0.5 #:actions (demo-actions 'dam-break)))
  (check-= (value run 'gate.opening 14.5) 0 0 "shut until the hand")
  (check-= (value run 'gate.opening 16) 1 0)
  (check-= (value run 'millpond.level 15) (+ 50 (/ (* 250 15) 1000 100 0.01)) 0.05 "53.75 cm")
  (check-= (value run 'gate.flow 16) (* 1000 1.705 0.5 (expt 0.3375 1.5)) 3 "L/s: the free weir over the lip")
  (define arrival (value run 'race.arrival 60))
  (check-true (<= (+ 15 13.8) arrival (+ 15 32.5)) (format "the front reached the low pond at ~a s" arrival))
  ;; the pond goes on rising at 250 - 168 = 82 L/s, so the 55 cm trigger fires later, at about 15 + 1.25 m3 / 0.078 m3/s = 31 s,
  ;; and finds the gate already open
  (check-= (value run 'full.fired-at 60) 31 2 "the trigger fires later, at 55 cm, with nothing left to do"))

(test-case "Hillside pond: the gate drawn by hand at 8 s, the cup at 7.69 cm, 2.5 s before the clock would trip it"
  (define run (simulate 'hillside-pond #:seconds 30 #:step 0.02 #:sample-dt 0.25 #:actions (demo-actions 'hillside-pond)))
  (define bare (simulate 'hillside-pond #:seconds 30 #:step 0.02 #:sample-dt 0.25))
  (check-= (value run 'clock.level 8) (* 100 (- 1 (exp -0.08))) 0.05 "cm")
  (check-= (value run 'gate.opening 7.5) 0 0)
  (check-= (value run 'gate.opening 8.5) 1 0)
  (check-= (value bare 'gate.opening 9) 0 0 "left alone, the gate is still shut at 9 s")
  (check-= (first-time bare 'gate.opening (λ (v) (> v 0.5))) (* 100 (log (/ 1 0.9))) 0.5 "the clock trips it at 100 ln(1/0.9) = 10.54 s")
  (check-= (value run 'pond.level 8) 100 0.05 "the pond has dropped only 0.1 mm by 8 s")
  (check-= (value run 'gate.flow 8.5) (* 1000 1.705 0.3 (expt 0.9 1.5)) 25 "L/s: the free weir, 437 at a metre"))

;; ---------------------------------------------------------------------------------------------------------------
;; the cranes, the walkers' wheel and the windlass (rigid bodies: under Godot)

(test-case "Roman crane: lifts for 20 s, hangs still for 10, comes down at 7.854 cm/s to the ground"
  (when (godot-available?)
    (define run (godot-simulate 'roman-crane #:seconds 55 #:sample-dt 0.25 #:actions (demo-actions 'roman-crane)))
    (define I (+ (machine-inertia 'roman-crane 'tympanus) (machine-inertia 'roman-crane 'drum)))
    (define m (* 2700 0.6 0.6 0.6)) (define r 0.25)
    (define pulley (/ (machine-inertia 'roman-crane 'pulley) (sqr 0.15)))
    (define I-eff (+ I (* (+ m pulley) r r)))
    (define torque-net (- 1545.1 (* m 9.81 r)))
    (define w-inf (min (/ torque-net (* 0.2 I)) (/ (* 3 2 pi) 60)))
    (define tc (/ I-eff (* 0.2 I)))
    (check-= I 1822.7 1 "kg m2")
    (check-= tc 5.1 0.05 "s")
    ;; the stone rests on the ground (#148), so the ground holds it while the drum winds in the rope's stretch: the
    ;; wheel spins up nearly unloaded (1545.1 / 1822.7 = 0.85 rad/s2) to w0 before the rope takes the 5.7 kN, and from
    ;; there follows the damped-wheel law: w(t) = w-inf - (w-inf - w0) e^(-t/tc). w0 is read at the first sample;
    ;; w0^2 / (2 x 0.85) x r is the stretch wound in (about 1.4 mm)
    (define w0 (value run 'tympanus.omega 0.25))
    (check-true (< 0.03 w0 0.15) (format "w0 ~a rad/s: the stretch is wound in unloaded" w0))
    (check-= (* r (/ (sqr w0) (* 2 (/ 1545.1 I)))) 0.0014 0.001 "m of rope stretch wound in before the stone lifts")
    (define (rise t) (* r (- (* w-inf t) (* (- w-inf w0) tc (- 1 (exp (- (/ t tc))))))))
    (define y0 (value run 'stone.y 0))
    (check-= (value run 'stone.y 18) (+ y0 (rise 18)) 0.04 "rising as the damped wheel predicts")
    (define top (value run 'stone.y 21))
    (check-= top (+ y0 (rise 20)) 0.06 (format "top ~a m, predicted ~a m" top (+ y0 (rise 20))))
    (check-true (> (- top y0) 1.0) "lifted more than a metre")
    (check-true (< (spread (between run 'stone.y 22 30)) 0.01) "held 10 s: it hangs still")
    (define v (/ (- (value run 'stone.y 42) (value run 'stone.y 36)) 6))
    (check-= v (- (* r 3 (/ (* 2 pi) 60))) 0.002 "lowered at 7.854 cm/s")
    (define ground (first-time run 'stone.y (λ (y) (< y (+ y0 0.01))) 31))
    (check-= ground (+ 30 (/ (- top y0) (* r 3 (/ (* 2 pi) 60)))) 1.0 (format "on the ground at ~a s" ground))
    (check-= (value run 'stone.y 55) y0 0.01 "back where it started")))

(test-case "Bar crane: seven walkers lift at 7.854 cm/s for 20 s, hold, and lower; the two stay at the ground"
  (when (godot-available?)
    (define run (godot-simulate 'bar-crane #:seconds 55 #:sample-dt 0.25 #:actions (demo-actions 'bar-crane)))
    (define v (* 0.25 3 (/ (* 2 pi) 60)))
    (define y0 (value run 'seven-stone.y 0))
    (check-= (/ (- (value run 'seven-stone.y 18) (value run 'seven-stone.y 6)) 12) v 0.002 "lifts at 7.854 cm/s")
    (define top (value run 'seven-stone.y 21))
    (check-= top (+ y0 (* v 20)) 0.04 (format "top ~a m, predicted ~a m" top (+ y0 (* v 20))))
    (check-true (< (spread (between run 'seven-stone.y 22 30)) 0.01) "held 10 s")
    (check-= (/ (- (value run 'seven-stone.y 42) (value run 'seven-stone.y 36)) 6) (- v) 0.002 "and lowered at the same")
    (check-= (first-time run 'seven-stone.y (λ (y) (< y (+ y0 0.01))) 31) (+ 30 (/ (- top y0) v)) 1.0 "to the ground")
    (check-true (< (apply max (between run 'two-stone.y 0 55)) (+ y0 0.01)) "two walkers cannot lift it over the bar")))

(test-case "Walkers' wheel: 3 rpm for 10 s is 180 degrees, held for 10, and back to 0 at 30 s"
  (when (godot-available?)
    (define run (godot-simulate 'walkers-wheel #:seconds 36 #:sample-dt 0.25 #:actions (demo-actions 'walkers-wheel)))
    (define deg/s (* 3 6))
    (check-= (value run 'tympanus.angle 0) 0 0)
    (check-= (value run 'tympanus.angle 5) (* deg/s 5) 5 "(a fifth of a second to get going)")
    (check-= (value run 'tympanus.angle 12) (* deg/s 10) 1.5 "180 degrees")
    (check-true (< (spread (between run 'tympanus.angle 13 20)) 0.5) "held")
    (check-= (value run 'tympanus.angle 25) (- (* deg/s 10) (* deg/s 5)) 5 "half way back")
    (check-= (value run 'tympanus.angle 34) 0 1.5 "back at 0")))

(test-case "Ratchet windlass: the crank winds 0.314 m in 5 s, let go it holds on its pawl, and the pawl lifted drops it at 7.88 m/s2"
  (when (godot-available?)
    (define run (godot-simulate 'ratchet-windlass #:seconds 16 #:sample-dt 1/120 #:actions (demo-actions 'ratchet-windlass)))
    (define I (* 720 6.797550167426925e-5))
    (define m (* 2700 (expt 0.195 3)))
    (define a (/ (* m 9.81) (+ m (/ I (sqr 0.1)))))
    (check-= a 7.883 0.002)
    (check-= (value run 'wind-load.y 5) (+ 1 (* 0.1 pi)) 0.025 "the rope wound in r x theta")
    (check-= (value run 'wind-drum.drive-torque 4) 40 0 "cranked")
    (check-= (value run 'wind-drum.drive-torque 6) 0 0 "the crank let go at 5 s")
    (define held (between run 'wind-load.y 6 9.9))
    (check-true (< (spread held) 0.015) (format "held by the pawl alone: moved ~a m in 4 s" (spread held)))
    (check-true (> (apply min held) 1.28) "and not lowered")
    (define y10 (value run 'wind-load.y 9.99))
    (define floor-y (/ 0.195 2))
    (define near-floor (+ floor-y 0.05))      ; the block bounces on landing, so time it passing 5 cm above its resting height
    (define landed (first-time run 'wind-load.y (λ (y) (< y near-floor)) 10))
    (check-= (- landed 10) (sqrt (/ (* 2 (- y10 near-floor)) a)) 0.06 (format "fell ~a m in ~a s" (- y10 near-floor) (- landed 10)))
    ;; the fourth windlass's pawl, lifted at 2 s by the operator (it used to lift itself)
    (check-= (value run 'lower-pawl.pawl 1.9) 1 0)
    (check-= (value run 'lower-pawl.pawl 2.1) 0 0)
    (check-= (- (first-time run 'lower-load.y (λ (y) (< y near-floor)) 2) 2) (sqrt (/ (* 2 (- 1.0 near-floor)) a)) 0.06 "about 0.47 s")
    ;; the first holds throughout
    (check-true (for/and ([y (between run 'hold-load.y 0 16)]) (< (abs (- y 1.0)) 0.012)) "the held load sags under a centimetre")))

;; ---------------------------------------------------------------------------------------------------------------
;; the throwing machines: the catch pulled by the demo operator

(test-case "Trebuchet: held on its catch carrying 95.8 N.m until the demo operator pulls it at 3 s, then it throws"
  (when (godot-available?)
    (define run (godot-simulate 'trebuchet #:seconds 7 #:sample-dt 1/120 #:actions (demo-actions 'trebuchet #:until 7)))
    (define cw (* 2700 (expt 0.3 3) 9.81 0.27 (cos (* pi 50/180))))
    (define beam (* 720 1.8 0.025 0.22 9.81 0.63 (cos (* pi 50/180))))
    (check-= (value run 'arm.catch-load 2.9) (- cw beam) (* 0.02 (- cw beam)) "N.m the catch carries")
    (check-= (first-time run 'arm.catch zero?) 3.0 0.05 "the catch opens at 3 s")
    (check-true (< (abs (- (value run 'stone.x 2.9) (value run 'stone.x 0))) 1e-3) "nothing moves while it holds")
    (check-true (< (apply min (between run 'stone.x 3 7)) -5) "and then the stone is thrown toward -X")))

(test-case "Onager: held on its slip-hook carrying 275 N.m until the demo operator looses it at 2 s"
  (when (godot-available?)
    (define run (godot-simulate 'torsion-catapult #:seconds 4 #:sample-dt 0.05 #:actions (demo-actions 'torsion-catapult #:until 4)))
    (define expected (- (* 150 (* pi 120/180)) (* 720 0.06 0.06 1.0 9.81 0.5) (* 2700 0.001 9.81 1.0)))
    (check-= expected 275.0 0.1)
    (check-= (value run 'arm.catch-load 1.9) expected (* 0.02 expected))
    (check-= (value run 'arm.angle 1.9) 0 0.1 "held where it was winched to")
    (check-= (first-time run 'arm.catch zero?) 2.0 0.1 "loosed at 2 s")
    (check-true (> (value run 'arm.angle 2.5) 30) "and swings up")))
