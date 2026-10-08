#lang racket/base
;; Thermal mass (issue #71): heat stores, a lidded bin and a regolith wall that heat soaks into, checked against numbers
;; worked out before the run (each machine's header shows the working), through the headless C# simulation.
;;   racket/machines/night-heat.rkt       the vault: a bank, 11 kg and 40 kg of rock, a 0.1 and a 0.5 W/K lid
;;   racket/machines/hot-water-night.rkt  a tank and its room cooling together, and water that freezes
(require rackunit racket/list racket/math heroic/simhost)

(define (at frame key) (cadr (assq key (cdr frame))))
(define (frame-near run t) (argmin (λ (f) (abs (- (car f) t))) run))
(define (value-at run key t) (at (frame-near run t) key))

;; ---------------------------------------------------------------------------
;; The vault.  Local hours are 3,699 s (a Mars sol of 88,775 s in 24); the machine starts at 17:00, so 03:00 is 36,990 s in.

(define hour 3699.0)
(define sol 88775.0)

(test-case "Vault: the rock cools at the radiation's initial rate, then as the lumped model says (wall impedance A I / sqrt(pi t))"
  (define run (simulate 'night-heat #:seconds (* 10 hour) #:step 5 #:sample-dt 5))
  ;; rock: 40 kg basalt, c = 840: 33.6 kJ/K, a 0.3451 m² cube-of-its-volume surface (6 (40/2900)^(2/3)) in a 1.5 m² cavity: the two-surface
  ;; exchange is eps_eff = 1/(1/0.9 + (A1/A2)(1/0.9 - 1)) = 0.8798, so at the start (rock 200 C, wall -55 C)
  ;;   Q0 = 0.8798 x 0.3451 x 5.670374e-8 x (473.15^4 - 218.15^4) = 823.8 W   ->   dT/dt = 0.02452 K/s, tau = 255 K / 823.8 W x 33.6 kJ/K = 2.89 h
  (check-= (- 200 (value-at run 'tight-rock.temperature 5.0)) (* 5 0.02452) 0.01 "0.1226 K in the first 5 s")
  ;; the lumped model (rock and bank joined to a quasi-steady cavity, which loses heat to a fresh wall with conductance
  ;; A I / sqrt(pi t), I = 216.3 J/(m² K sqrt(s))), integrated by hand at 1 s: after 1 h and 5 h
  (check-= (value-at run 'tight-rock.temperature hour) 144.2 4 "rock after 1 h: model 144.2")
  (check-= (value-at run 'tight-bank.temperature hour) -39.9 4 "bank after 1 h: model -39.9 (the bank lags: tau = 16 kJ/K / 0.8 W/K = 5.5 h)")
  (check-= (value-at run 'tight-rock.temperature (* 5 hour)) 61.6 3 "rock after 5 h: model 61.6")
  (check-= (value-at run 'tight-bank.temperature (* 5 hour)) -5.4 3 "bank after 5 h: model -5.4")
  (check-= (value-at run 'wide-rock.temperature hour) 111.6 4 "the 11 kg rock in the 1 m cavity: model 111.6")
  (check-= (value-at run 'wide-bank.temperature (* 5 hour)) -51.6 3 "bank in the 1 m cavity: model -51.6"))

(test-case "Vault: a fresh wall soaks heat up as sqrt(t), and the room's heat goes where energy says"
  (define run (simulate 'night-heat #:seconds (* 10 hour) #:step 5 #:sample-dt hour))
  (define last-frame (last run))
  (define (v k) (at last-frame k))
  ;; steady loss through the 0.5 m wall, k A / L = 0.039 x 1.5 / 0.5 = 0.117 W/K: the 'weeks' it would take to be reached is L²/alpha = 89 days
  (check-= (v 'tight.wall-steady) 0.117 1e-9)
  ;; the wall has taken far more than 0.117 W/K x 70 K x 10 h = 0.3 MJ: it is at about sqrt(pi t / alpha_t) times it
  (check-true (> (v 'tight.wall-heat) 4.0) (format "tight wall soaked ~a MJ in the first night" (v 'tight.wall-heat)))
  (check-true (< 5 (v 'tight.wall-depth) 12) (format "and the warmth has gone only ~a cm (5% depth 2.33 sqrt(alpha t) = 8 cm for a surface held at a step; sqrt(alpha t) = 3.5 cm in 10 h)" (v 'tight.wall-depth)))
  ;; energy: what the rock gave = what the wall took + what the bank gained + what went on into the ground
  (define bank-gain (* 16 1000 (- (v 'tight-bank.temperature) -55)))
  (check-= (* 1e6 (v 'tight-rock.given)) (+ (* 1e6 (v 'tight.wall-heat)) bank-gain) (* 0.01 (* 1e6 (v 'tight-rock.given)))
           "energy is conserved within 1% (the cavity's 1 J/K of air is the rest)")
  (check-= (* 1e6 (v 'tight-rock.given)) (* 40 840 (- 200 (v 'tight-rock.temperature))) 1 "and the rock gave m c dT"))

(test-case "Vault: 11 kg of rock fails in a 1 m cavity; about 40 kg works from night 1, even from a bank frozen at -55 C"
  (define run (simulate 'night-heat #:seconds (* 10 hour) #:step 5 #:sample-dt hour))
  (define last-frame (last run))
  ;; energy: 40 kg x 840 x (200 - 32) = 5.6 MJ available; the 1.5 m² wall takes 2 I A dT sqrt(t/pi) = 4.7 MJ at a mean 17 K over -55 + ...,
  ;; the bank 16 kg x 1000 x 59 K = 0.95 MJ to reach +4: just enough. The 1 m cavity has 6 m²: the same rock's 11 kg (1.4 MJ above 50 C) is gone in the wall.
  (check-true (<= 0 (at last-frame 'tight-bank.temperature) 45) (format "bank at 03:00 in the tight vault: ~a" (at last-frame 'tight-bank.temperature)))
  (check-= (at last-frame 'tight-bank.temperature) 4.3 1.0 "trace 4.3 C; the lumped model says -1.3 (it over-counts the late wall uptake)")
  (check-true (< (at last-frame 'wide-bank.temperature) -40) (format "bank at 03:00 with 11 kg in a 1 m cavity: ~a" (at last-frame 'wide-bank.temperature)))
  (check-= (at last-frame 'wide-bank.temperature) -50.1 1.0 "model -51.5")
  (check-= (at last-frame 'wide.wall-heat) 1.94 0.1 "a 6 m² wall takes MJ, a 1.5 m² one 4.7: per m² the same sqrt(t) law")
  ;; the thermostat opened both lids at once (bank under 5 C) and neither has closed (bank never reached 40)
  (check-= (at last-frame 'tight-bin.open) 1 0)
  (check-= (at last-frame 'tight-bin.openings) 1 0))

(test-case "Vault: a lid leaking 0.1 W/K holds the bank at the thermostat's 40 C; one leaking 0.5 W/K lets it overheat as the wall warms"
  ;; the rover pushes 40 kg of fresh 200 C rock in each dusk: the rock is reset at each sol's start
  (define sols 8)
  (define reloads (for*/list ([n (in-range 1 sols)] [rock '(tight-rock leaky-rock)]) (list rock 'temperature 200.0 (* n sol))))
  (define run (simulate 'night-heat #:seconds (* sols sol) #:step 10 #:sample-dt 600 #:set reloads))
  (define (peak-of key from-sol)
    (apply max (for/list ([f run] #:when (>= (car f) (* from-sol sol))) (at f key))))
  ;; the first night is the same either way: the lid is open all night (bank under 5 C until 03:00)
  (check-= (value-at run 'tight-bank.temperature (* 10 hour)) (value-at run 'leaky-bank.temperature (* 10 hour)) 0.05 "night 1: same")
  ;; a shut lid is a leak L to the cavity, so the rock cools towards the cavity's temperature T_e with time constant tau = m c / L:
  ;;   0.1 W/K: 40 x 840 / 0.1 = 336,000 s (3.9 days);  0.5 W/K: 67,200 s (18.7 h)
  ;; by sol 5 the bank is over 5 C all day, so the lid stays shut from one reload to the next, and the rock should end the sol at
  ;;   T_e + (200 - T_e) exp(-88,775 / tau)   (T_e the day's mean cavity temperature, read from the trace: 14.1 and 48.6 C)
  ;; = 156.8 C and 89.0 C
  (define day5 (for/list ([f run] #:when (and (>= (car f) (* 4 sol)) (< (car f) (* 5 sol)))) f))
  (for ([v '(tight leaky)] [L '(0.1 0.5)] [end '(156.8 89.0)])
    (define rock (string->symbol (format "~a-rock.temperature" v)))
    (define te (/ (apply + (map (λ (f) (at f (string->symbol (format "~a.temperature" v)))) day5)) (length day5)))
    (define tau (/ (* 40 840) L))
    (check-= (at (last day5) rock) (+ te (* (- 200 te) (exp (- (/ sol tau))))) 1.0 (format "~a W/K lid: the rock ends sol 5 at ~a C" L end))
    (check-true (for/and ([f day5]) (>= (at f (string->symbol (format "~a-bank.temperature" v))) 5)) "with the lid shut all day"))
  (check-true (<= (peak-of 'tight-bank.temperature 3) 41) (format "0.1 W/K lid: bank peaks at ~a C from sol 4" (peak-of 'tight-bank.temperature 3)))
  (check-true (>= (peak-of 'leaky-bank.temperature 3) 55) (format "0.5 W/K lid: bank reaches ~a C by sol 8" (peak-of 'leaky-bank.temperature 3)))
  (check-true (> (peak-of 'leaky-bank.temperature 3) 45) "above the 45 C a lithium bank may charge at"))

;; ---------------------------------------------------------------------------
;; A tank and its room: C1 T1' = -U (T1 - T0) + G (Ts - T1), Cs Ts' = -G (Ts - T1); the 2x2 linear system's closed form.

(define U 2.0) (define G 6.0) (define C1 3000.0) (define T0 -80.0)
(define (two-node m [Ts0 60.0] [T10 0.0])
  (define Cs (* m 4186.0))
  (define a (- (/ (+ U G) C1))) (define b (/ G C1)) (define c (/ G Cs)) (define d (- (/ G Cs)))
  (define tr (+ a d)) (define det (- (* a d) (* b c))) (define disc (sqrt (- (/ (* tr tr) 4) det)))
  (define l1 (+ (/ tr 2) disc)) (define l2 (- (/ tr 2) disc))
  (define v1 (list b (- l1 a))) (define v2 (list b (- l2 a)))
  (define x0 (list (- T10 T0) (- Ts0 T0)))
  (define D (- (* (car v1) (cadr v2)) (* (cadr v1) (car v2))))
  (define k1 (/ (- (* (car x0) (cadr v2)) (* (cadr x0) (car v2))) D))
  (define k2 (/ (- (* (cadr x0) (car v1)) (* (car x0) (cadr v1))) D))
  (values (λ (t) (+ T0 (* k1 (car v1) (exp (* l1 t))) (* k2 (car v2) (exp (* l2 t)))))
          (λ (t) (+ T0 (* k1 (cadr v1) (exp (* l1 t))) (* k2 (cadr v2) (exp (* l2 t)))))
          l2))

(test-case "Hot water: a tank and its room cool together as the two-node balance says (time constants 8.1 h, 25.9 h and 51.5 h)"
  (define run (simulate 'hot-water-night #:seconds 43200 #:step 1 #:sample-dt 3600))
  (for ([m '(33 66)] [room '(thirty-three sixty-six)])
    (define-values (room-t tank-t slow) (two-node m))
    ;; the slow mode: Cs (U+G) / (U G) in the limit of a small room, = m c / (hA in series) = 8.1 h at 10 kg ...
    (for ([h '(1 3 6 9 12)])
      (check-= (value-at run (string->symbol (format "~a.temperature" room)) (* h 3600)) (room-t (* h 3600.0)) 0.15 (format "~a kg room at ~a h" m h))
      (check-= (value-at run (string->symbol (format "~a-tank.temperature" room)) (* h 3600)) (tank-t (* h 3600.0)) 0.15 (format "~a kg tank at ~a h" m h))))
  ;; the arithmetic of the design doc, 33 kg for 7 MJ, holds the room 80 K up only if 160 W is given for all 12 h: a cooling tank gives less, so
  ;; 33 kg leaves the room at -13.8 C at dawn, and about 56 kg is wanted to keep it at 0 C
  (check-= (value-at run 'thirty-three.temperature 43200) -13.83 0.1)
  (check-true (> (value-at run 'sixty-six.temperature 43200) 0) "66 kg carries the room through the night")
  (check-true (< (value-at run 'thirty-three.temperature 43200) 0) "33 kg does not"))

(test-case "Hot water: a small tank that cools to 0 C holds there while it freezes, the room settling at U T0 / (U + G) = -20 C"
  (define run (simulate 'hot-water-night #:seconds 43200 #:step 1 #:sample-dt 60))
  ;; 10 kg reaches 0 C at t0 where the two-node closed form crosses 0: 15,967 s (4.44 h)
  (define-values (room-t tank-t slow) (two-node 10))
  (define t0 (for/first ([t (in-range 0 43200 1.0)] #:when (<= (tank-t t) 0)) t))
  (check-= t0 15967 2)
  (define froze (for/first ([f run] #:when (> (at f 'ten-tank.frozen) 0)) (car f)))
  (check-= froze t0 120 "ice appears in the tank within a sample of that")
  (check-= (value-at run 'ten-tank.temperature 30000) 0 1e-6 "and the water holds at 0 C")
  (check-= (value-at run 'ten.temperature 30000) -20 0.01 "while its room sits at -20 C: 120 W out of the walls")
  ;; 120 W for the remaining 27,233 s, less the room's own settling (6 W/K x 375 s x 4.8 K), over 334 kJ/kg x 10 kg:
  (define share (/ (* G (- (* 20 (- 43200 t0)) (* (+ (room-t t0) 20) (/ C1 (+ U G))))) (* 10 334000)))
  (check-= share 0.9779 0.0005)
  (check-= (/ (value-at run 'ten-tank.frozen 43200) 100) share 0.003 "frozen share at dawn"))

(test-case "Material table: specific heat and conductivity, with InSight's 0.039 for regolith"
  ;; (the numbers are in racket/heroic/materials.rktd with their sources; the C# suite asserts them through the library)
  (define run (simulate 'night-heat #:seconds 1 #:step 1 #:sample-dt 1))
  (check-= (value-at run 'tight-rock.capacity 1) 33.6 1e-9 "40 kg x 840 J/(kg K) = 33.6 kJ/K")
  (check-= (value-at run 'tight-bank.capacity 1) 16.0 1e-9 "16 kg of cells x 1,000")
  (check-= (value-at run 'wide-rock.capacity 1) 9.24 1e-9 "11 kg x 840"))

(test-case "A store heated by power P rises at P / (m c): 40 kg of basalt under a 4 m² heliostat"
  ;; the sun held at 17:00 (15.1 degrees up, 188.26 W/m² of beam): the mirror delivers I A rho cos(theta/2) = 188.26 x 4 x 0.85 x 0.7248 = 463.9 W,
  ;; m c = 40 x 840 = 33,600 J/K, so 463.9 / 33,600 = 0.013807 K/s: 0.8284 K in 60 s, 1.6568 K in 120 s (the rock starts at the air's
  ;; temperature, so the air gives it next to nothing)
  (define run (simulate 'night-heat #:seconds 600 #:step 1 #:sample-dt 60 #:set '((scene clock-rate 0))))
  (define (v k t) (value-at run k t))
  (define p (* (v 'scene.irradiance 60) 4 0.85 (v 'sun-heliostat.cosine 60)))
  (check-= (v 'sun-heliostat.power 60) p 1e-6 "the mirror's power is beam x area x reflectivity x cosine")
  (check-= p 463.9 0.05)
  (check-= (- (v 'sunrock.temperature 60) -24) (/ (* p 60) 33600) 0.005 "60 s")
  (check-= (- (v 'sunrock.temperature 120) -24) (/ (* p 120) 33600) 0.01 "120 s")
  (check-= (v 'sunrock.gained 600) (/ (* p 600) 1e6) 1e-6 "and the energy that landed is what the mirror gave"))

;; ---------------------------------------------------------------------------
;; The DSL checks what it is given.

(define (expand-machine . clauses)
  (parameterize ([current-namespace (make-base-namespace)])
    (expand `(module test-machine heroic (define-machine test ,@clauses)))
    (void)))
(define-syntax-rule (check-compile-error rx clause ...)
  (check-exn (λ (e) (and (exn:fail:syntax? e) (regexp-match? rx (exn-message e))))
             (λ () (expand-machine 'clause ...))))

(test-case "DSL: heat stores, bins and walls are checked when the machine is built"
  (check-not-exn (λ () (expand-machine '(heat-store rock #:at (0 0 0) #:mass 40 #:contents basalt #:temperature 200 #:area 0.35 #:emissivity 0.9 #:conductance 0)
                                       '(heat-store bank #:at (1 0 0) #:mass 16 #:contents cells)
                                       '(heat-bin bin #:at (0 0 0) #:holds rock #:leak 0.1 #:sense bank #:open-below 5 #:close-above 40)
                                       '(enclosure vault #:at (0 0 0) #:size (0.5 0.5 0.5) #:wall regolith #:wall-thickness 0.5 #:ground -55 #:emissivity 0.9))))
  (check-compile-error #rx"#:contents is water or a material of the table"
    (heat-store rock #:at (0 0 0) #:mass 40 #:contents unobtainium))
  (check-compile-error #rx"rock is not a heat-store; a heat-bin holds one"
    (block rock #:at (0 0 0) #:size 0.1 #:material oak)
    (heat-bin bin #:at (0 0 0) #:holds rock))
  (check-compile-error #rx"bank is not a heat-store; a heat-bin senses one"
    (heat-store rock #:at (0 0 0) #:mass 10)
    (heat-bin bin #:at (0 0 0) #:holds rock #:sense bank))
  (check-compile-error #rx"#:wall is a material of the table"
    (enclosure vault #:at (0 0 0) #:size (1 1 1) #:wall unobtainium))
  ;; a mirror or a fire can heat a store
  (check-not-exn (λ () (expand-machine '(heat-store rock #:at (0 0 0) #:mass 10)
                                       '(mirror m #:at (2 1 0) #:area 1 #:onto rock)
                                       '(hearth fire #:at (1 0 0) #:power 1000 #:fuel 0.1 #:heats rock)))))

;; numbers are checked when the machine is built, not when it expands
(test-case "DSL: a store's mass, a bin's thermostat and a wall's thickness are checked at build time"
  (define (build . clauses)
    (parameterize ([current-namespace (make-base-namespace)])
      (eval `(module stores heroic (define-machine stores ,@clauses)))
      (dynamic-require ''stores #f)))
  (check-exn #rx"#:mass must be above 0 kg" (λ () (build '(heat-store rock #:at (0 0 0) #:mass 0))))
  (check-exn #rx"#:open-below must be under #:close-above"
             (λ () (build '(heat-store rock #:at (0 0 0) #:mass 10) '(heat-bin bin #:at (0 0 0) #:holds rock #:open-below 40 #:close-above 5))))
  (check-exn #rx"#:wall-thickness must be above 0 m" (λ () (build '(enclosure vault #:at (0 0 0) #:size (1 1 1) #:wall regolith #:wall-thickness 0))))
  (check-exn #rx"#:emissivity must be in" (λ () (build '(heat-store rock #:at (0 0 0) #:mass 10 #:emissivity 1.5))))
  (check-not-exn (λ () (build '(heat-store rock #:at (0 0 0) #:mass 10 #:contents water #:area 0 #:conductance 6)))))
