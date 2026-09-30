#lang racket/base
;; define-machine: the heart of #lang heroic.
;;
;; (define-machine name [#:source "..."] [#:ambient C] [#:planet p]
;;                 [#:latitude deg] [#:day n] [#:time hours] clause ...)
;; #:planet sets the numbers the scene's physics runs on (issue #38):
;; gravity, surface pressure, the air's gas mix (and so its density),
;; sunlight and the length of a day. A preset by name -- earth (the
;; default) or mars, from planets.rktd -- or (planet mars #:gravity 9.81
;; ...) for a preset with some numbers changed (#:gravity #:pressure
;; #:temperature #:air #:solar-constant #:sol). Formulas never change, only
;; their numbers: a pendulum swings slower under Mars's 3.71 m/s², a lift
;; pump holds no water under its 610 Pa, a windmill makes 1/79 the power.
;; #:latitude/#:day/#:time put the scene under the sun: degrees north, day
;; of the year, solar hours (12 is noon; the clock then runs with the
;; simulation). Defaults, if any is given: Alexandria, 31.2 N, on
;; midsummer's day (172) at noon.
;; #:ambient is the air round the machine, °C (default 20, or the
;; #:planet's temperature, -63 on Mars): boilers and
;; sealed air cool towards it, boilers and pumped water start at it, a
;; hearth's quench water and a windmill's or bellows' air are at it, and
;; open tanks freeze below 0.
;;
;; A machine is a list of part clauses (tank, boiler, rotor, block,
;; pendulum, lever, ramp, and the generated-geometry parts wheel, screw,
;; fixture) and link clauses (pipe, connect, sealed-air).
;; pendulum/lever/ramp need no solver of their own — they're pure Jolt
;; rigid-body physics, built in MachineView — except a pendulum hung on a
;; bearing, which the sim swings so its friction and wear can be checked. The macro checks the whole
;; machine while the file compiles: part names, materials, port names,
;; port kinds, and that every rotor has steam. Errors point at the exact
;; clause that is wrong. Parameter values are ordinary Racket expressions,
;; evaluated when the module runs.

(require (for-syntax racket/base racket/list racket/string syntax/parse "materials.rkt" (only-in "planets.rkt" planet-ids))
         "geometry/shape.rkt" "planets.rkt")

(provide define-machine
         tank boiler rotor jetwheel smokejack block pendulum lever ramp wheel screw fixture post hearth bellows sluice waterwheel windmill capstan mirror counterpoise float-valve leak safety-valve pump enclosure grip
         pipe connect sealed-air port rope world arbor mesh lift piston atmospheric-cylinder
         inflow channel off trigger follow belt joint
         (struct-out machine) (struct-out part) (struct-out port-spec)
         (struct-out pipe-spec) (struct-out connect-spec) (struct-out air-spec)
         (struct-out rope-spec) (struct-out arbor-spec) (struct-out mesh-spec) (struct-out lift-spec) (struct-out cylinder-spec)
         (struct-out inflow-spec) (struct-out channel-spec) (struct-out trigger-spec) (struct-out follow-spec) (struct-out belt-spec) (struct-out joint-spec)
         take-registered-machines
         planet make-planet planet? planet-field planet-name earth-planet?)

;; ---------------------------------------------------------------------------
;; Runtime representation

(struct machine (name source ambient sun planet parts pipes connects airs ropes arbors meshes lifts cylinders inflows channels triggers follows belts joints) #:transparent)
;; kind: 'tank | 'boiler | 'rotor | 'block
;; at: (list x y z); props: (listof (cons symbol value)); loc: #(file line column)
(struct part (id kind material at props ports loc) #:transparent)
(struct port-spec (name kind height) #:transparent)          ; kind: 'water | 'steam
(struct pipe-spec (id from to conductance jet? loc) #:transparent) ; from/to: (list part port)
(struct connect-spec (from to loc) #:transparent)
;; heat-loss: W/K through the vessels' walls; heat-capacity: J/K of the vessels themselves
(struct air-spec (tanks tube-volume loc heat-loss heat-capacity) #:transparent)
;; from/to: (list part-or-world x y z), the point in that part's own frame
;; (for world, in world coordinates); over: fixed points the rope runs
;; over (pulleys); wind-on: a wheel the from end winds onto, or #f;
;; release-deg: see the rope clause; diameter in m; bar: the material of the
;; fixed bars at the over points, or #f for turning pulleys (no friction);
;; mu: a friction coefficient that overrides the materials', or #f.
(struct rope-spec (id from to length over wind-on release-deg material diameter nocked turns bar mu loc) #:transparent)
;; parts: wheels fixed on one axle, first one first — they turn as one.
(struct arbor-spec (parts loc) #:transparent)
;; a, b: two gears whose teeth engage.
(struct mesh-spec (a b loc) #:transparent)
;; piston: the piston part it drives; boiler: where its steam comes from.
(struct cylinder-spec (id piston boiler injection-temperature loc) #:transparent)
;; by: the screw or noria that lifts; from, to: tanks; current: a river's
;; speed (m/s) pushing a noria's paddles, or #f.
(struct lift-spec (id by from to current current-from loc) #:transparent)
;; An open belt between two drums a and b (wheel part ids), pretensioned to `tension` N,
;; gripping as far as the friction of `material` allows.
(struct belt-spec (id a b tension material loc) #:transparent)
;; A field that follows a mechanism: lever (a part id) or rope (a rope id), the other #f;
;; the input (degrees turned, or newtons of tension) runs from `from` to `to`, and the field
;; target.field from `low` to `high`, linearly and held at the ends.
(struct follow-spec (id lever rope from to target field low high loc) #:transparent)
;; A joint between two moving parts (or a part and the world). kind: 'pin
;; (a hinge about axis), 'ball (turns every way about the point; limit-deg,
;; if given, caps how far it swings), 'universal (a Cardan cross between two
;; shafts: the parts' own axles set its arms), '6dof (free: the list of
;; motions it leaves free, among x y z rx ry rz; the rest are locked).
;; a, b: part ids or 'world; at: (list x y z) in the world; axis: (list x y z) or #f.
(struct joint-spec (id kind a b at axis free limit-deg loc) #:transparent)
;; A sensor that acts when something arrives. Watches a body (body: a part id, with
;; at and size: the box, (list x y z) each, that fires it when the part's centre enters it)
;; or a field (when: (list target field mode value), mode 'above or 'below). actions: the
;; (list target field value) settings it applies, once, when it fires.
(struct trigger-spec (id at size body when actions loc) #:transparent)
;; into: a tank; flow in m³/s.
(struct inflow-spec (id into flow loc) #:transparent)
;; from: (list tank port); to: (list tank port) or 'off; end: (list x y z) or #f;
;; via: list of (x z) waypoints the channel bends through, in order; length: m or #f
;; (worked out from the tanks' positions and the waypoints).
;; onto: a hearth or boiler the water falls onto at #:end, or #f
(struct channel-spec (id from to end via width length loc onto dynamic cells) #:transparent)

;; A sluice with no #:at stands at the lip where its channel leaves its
;; tank: on the tank's wall, facing the way the channel runs.
(define (place-sluices parts channels)
  (define (find-part id) (for/first ([p parts] #:when (eq? (part-id p) id)) p))
  (for/list ([p parts])
    (cond
      [(and (eq? (part-kind p) 'sluice) (not (part-at p)))
       (define on (cdr (assq 'on (part-props p))))
       (define c (for/first ([c channels] #:when (eq? (channel-spec-id c) on)) c))
       (define tank (and c (find-part (car (channel-spec-from c)))))
       (define port (and tank (for/first ([pt (part-ports tank)]
                                          #:when (eq? (port-spec-name pt) (cadr (channel-spec-from c))))
                                pt)))
       (cond
         [(not port) (struct-copy part p [at (list 0 0 0)])]
         [else
          (define-values (tx ty tz) (apply values (part-at tank)))
          (define far
            (cond [(pair? (channel-spec-via c)) (car (channel-spec-via c))]
                  [(pair? (channel-spec-to c))
                   (define to (find-part (car (channel-spec-to c))))
                   (if to (list (car (part-at to)) (caddr (part-at to))) (list (+ tx 1) tz))]
                  [else (list (car (channel-spec-end c)) (caddr (channel-spec-end c)))]))
          (define dx (- (car far) tx))
          (define dz (- (cadr far) tz))
          (define len (max 1e-9 (sqrt (+ (* dx dx) (* dz dz)))))
          (define half (/ (sqrt (cdr (assq 'area (part-props tank)))) 2))
          (struct-copy part p [at (list (+ tx (* half (/ dx len))) (+ ty (port-spec-height port))
                                        (+ tz (* half (/ dz len))))])])]
      [else p])))

;; A float valve with no #:at floats in the middle of the tank its feed
;; fills, at the level where it shuts.
(define (place-float-valves parts items)
  (for/list ([p parts])
    (cond
      [(eq? (part-kind p) 'float-valve)
       (define (prop k) (cdr (assq k (part-props p))))
       (unless (and (real? (prop 'travel)) (> (prop 'travel) 0))
         (define loc (part-loc p))
         (error 'define-machine "~a:~a:~a: float-valve ~a: #:travel must be a positive length, got ~e"
                (vector-ref loc 0) (vector-ref loc 1) (vector-ref loc 2) (part-id p) (prop 'travel)))
       (define on (prop 'on))
       (define fed
         (for/or ([i items])
           (cond [(and (inflow-spec? i) (eq? (inflow-spec-id i) on)) (inflow-spec-into i)]
                 [(and (pipe-spec? i) (eq? (pipe-spec-id i) on)) (car (pipe-spec-to i))]
                 [(and (channel-spec? i) (eq? (channel-spec-id i) on) (pair? (channel-spec-to i)))
                  (car (channel-spec-to i))]
                 [else #f])))
       (define tank (for/first ([t parts] #:when (eq? (part-id t) fed)) t))
       (cond
         [(part-at p) p]
         [(not tank) (struct-copy part p [at (list 0 0 0)])]
         [else
          (define-values (tx ty tz) (apply values (part-at tank)))
          (struct-copy part p [at (list tx (+ ty (prop 'shut)) tz)])])]
      [else p])))

;; A leak with no #:at is drilled through the wall of its tank, on the
;; +x side at its height.
(define (place-leaks parts)
  (for/list ([p parts])
    (cond
      [(eq? (part-kind p) 'leak)
       (define (prop k) (cdr (assq k (part-props p))))
       (define loc (part-loc p))
       (define (bad what)
         (error 'define-machine "~a:~a:~a: leak ~a: ~a"
                (vector-ref loc 0) (vector-ref loc 1) (vector-ref loc 2) (part-id p) what))
       (define tank (for/first ([t parts] #:when (eq? (part-id t) (prop 'on))) t))
       (define (tank-prop k) (cdr (assq k (part-props tank))))
       (unless (and (real? (prop 'area)) (>= (prop 'area) 0)) (bad (format "#:area must be a length squared, 0 or more, got ~e" (prop 'area))))
       (unless (and (real? (prop 'coefficient)) (> (prop 'coefficient) 0) (<= (prop 'coefficient) 1))
         (bad (format "#:coefficient must be in (0, 1], got ~e" (prop 'coefficient))))
       (unless (and (real? (prop 'evaporation)) (>= (prop 'evaporation) 0)) (bad (format "#:evaporation must be a flow, 0 or more, got ~e" (prop 'evaporation))))
       (unless (and (real? (prop 'bore)) (>= (prop 'bore) 0)) (bad (format "#:bore must be a length, 0 or more, got ~e" (prop 'bore))))
       (unless (and (real? (prop 'lift)) (>= (prop 'lift) 0)) (bad (format "#:lift must be a length, 0 or more, got ~e" (prop 'lift))))
       (unless (> (+ (prop 'area) (prop 'evaporation) (prop 'bore)) 0) (bad "needs an #:area, a #:bore or an #:evaporation, or it leaks nothing"))
       (unless (and (real? (prop 'height)) (<= 0 (prop 'height) (tank-prop 'height)))
         (bad (format "#:height ~e is not in ~a's wall, 0 to ~e" (prop 'height) (part-id tank) (tank-prop 'height))))
       (cond
         [(part-at p) p]
         [else
          (define-values (tx ty tz) (apply values (part-at tank)))
          (struct-copy part p [at (list (+ tx (/ (sqrt (tank-prop 'area)) 2)) (+ ty (prop 'height)) tz)])])]
      [else p])))

;; A safety valve with no #:at sits in its boiler's lid, halfway out from
;; the middle on the +x side. Its lift must be below the boiler's rating,
;; or the boiler bursts before the valve ever opens.
(define (place-safety-valves parts)
  (for/list ([p parts])
    (cond
      [(eq? (part-kind p) 'safety-valve)
       (define (prop k) (cdr (assq k (part-props p))))
       (define loc (part-loc p))
       (define (bad what)
         (error 'define-machine "~a:~a:~a: safety-valve ~a: ~a"
                (vector-ref loc 0) (vector-ref loc 1) (vector-ref loc 2) (part-id p) what))
       (define b (for/first ([t parts] #:when (eq? (part-id t) (prop 'on))) t))
       (define (boiler-prop k) (cond [(assq k (part-props b)) => cdr] [else 0]))
       (unless (and (real? (prop 'lift)) (> (prop 'lift) 0)) (bad (format "#:lift must be a gauge pressure above 0, got ~e" (prop 'lift))))
       (unless (and (real? (prop 'bore)) (> (prop 'bore) 0)) (bad (format "#:bore must be a length above 0, got ~e" (prop 'bore))))
       (unless (and (real? (prop 'coefficient)) (> (prop 'coefficient) 0) (<= (prop 'coefficient) 1))
         (bad (format "#:coefficient must be in (0, 1], got ~e" (prop 'coefficient))))
       (unless (and (real? (prop 'accumulation)) (> (prop 'accumulation) 0))
         (bad (format "#:accumulation must be above 0, got ~e" (prop 'accumulation))))
       (define rating (boiler-prop 'burst))
       (when (and (> rating 0) (>= (prop 'lift) rating))
         (bad (format "lifts at ~e Pa, but ~a bursts at ~e Pa" (prop 'lift) (part-id b) rating)))
       (cond
         [(part-at p) p]
         [else
          (define-values (bx by bz) (apply values (part-at b)))
          (struct-copy part p [at (list (+ bx (/ (boiler-prop 'radius) 2)) (+ by (boiler-prop 'height)) bz)])])]
      [(and (eq? (part-kind p) 'boiler) (assq 'burst (part-props p)))
       (define rating (cdr (assq 'burst (part-props p))))
       (unless (and (real? rating) (>= rating 0))
         (error 'define-machine "boiler ~a: #:burst must be a gauge pressure, 0 or more, got ~e" (part-id p) rating))
       p]
      [else p])))

;; A pump's numbers are checked when the machine is built. Nothing stops
;; a barrel standing higher over its water than the atmosphere can lift:
;; that pump simply delivers nothing.
(define (check-pumps parts)
  (for ([p parts] #:when (eq? (part-kind p) 'pump))
    (define (prop k) (cdr (assq k (part-props p))))
    (define loc (part-loc p))
    (define (bad what)
      (error 'define-machine "~a:~a:~a: pump ~a: ~a"
             (vector-ref loc 0) (vector-ref loc 1) (vector-ref loc 2) (part-id p) what))
    (unless (and (real? (prop 'bore)) (> (prop 'bore) 0)) (bad (format "#:bore must be a length above 0, got ~e" (prop 'bore))))
    (unless (and (real? (prop 'stroke)) (> (prop 'stroke) 0)) (bad (format "#:stroke must be a length above 0, got ~e" (prop 'stroke))))
    (unless (and (real? (prop 'rpm)) (>= (prop 'rpm) 0)) (bad (format "#:rpm must be 0 or more, got ~e" (prop 'rpm))))
    (unless (and (real? (prop 'efficiency)) (> (prop 'efficiency) 0) (<= (prop 'efficiency) 1))
      (bad (format "#:efficiency must be in (0, 1], got ~e" (prop 'efficiency))))
    (unless (or (not (prop 'force)) (and (real? (prop 'force)) (> (prop 'force) 0)))
      (bad (format "#:force must be a force above 0, got ~e" (prop 'force))))
    (unless (or (not (prop 'temperature)) (and (real? (prop 'temperature)) (<= 0 (prop 'temperature)) (< (prop 'temperature) 100)))
      (bad (format "#:temperature must be in [0, 100) C, got ~e" (prop 'temperature)))))
  parts)

;; A windmill's numbers are checked when the machine is built; its #:cp
;; above all, which no rotor can take past the Betz limit.
;; A capstan's numbers are checked when the machine is built.
(define (check-capstans parts)
  (for ([p parts] #:when (eq? (part-kind p) 'capstan))
    (define (prop k) (cdr (assq k (part-props p))))
    (define loc (part-loc p))
    (define (bad what)
      (error 'define-machine "~a:~a:~a: capstan ~a: ~a"
             (vector-ref loc 0) (vector-ref loc 1) (vector-ref loc 2) (part-id p) what))
    (unless (and (real? (prop 'turns)) (> (prop 'turns) 0)) (bad (format "#:turns must be above 0, got ~e" (prop 'turns))))
    (unless (and (real? (prop 'load)) (> (prop 'load) 0)) (bad (format "#:load must be a mass above 0, got ~e" (prop 'load))))
    (unless (and (real? (prop 'hold)) (>= (prop 'hold) 0)) (bad (format "#:hold must be a pull, 0 or more, got ~e" (prop 'hold))))
    (unless (or (not (prop 'mu)) (and (real? (prop 'mu)) (> (prop 'mu) 0)))
      (bad (format "#:mu must be a friction coefficient above 0, got ~e" (prop 'mu))))
    (unless (and (real? (prop 'drop)) (> (prop 'drop) 0) (< (prop 'drop) (cadr (part-at p))))
      (bad (format "#:drop must be above 0 and leave the load off the ground (the post is ~e m up), got ~e" (cadr (part-at p)) (prop 'drop)))))
  parts)

;; An enclosure's numbers are checked when the machine is built. Its #:air
;; becomes one prop per gas (o2 n2 co2 h2o ar), #f when it gives none: then
;; it holds its surroundings' air.
(define enclosure-gases '(o2 n2 co2 h2o ar))
(define (enclosure-air-props who air loc)
  (define (bad what) (error 'define-machine "~a:~a:~a: enclosure ~a: ~a" (vector-ref loc 0) (vector-ref loc 1) (vector-ref loc 2) who what))
  (cond
    [(not air) (for/list ([g enclosure-gases]) (cons g #f))]
    [else
     (for ([g air])
       (unless (and (pair? g) (memq (car g) enclosure-gases) (pair? (cdr g)) (real? (cadr g)) (>= (cadr g) 0))
         (bad (format "#:air lists (gas fraction) for gases among ~a, got ~e" enclosure-gases g))))
     (define total (for/sum ([g air]) (cadr g)))
     (unless (< (abs (- total 1)) 0.001) (bad (format "#:air's fractions must add up to 1, got ~a" total)))
     (for/list ([g enclosure-gases]) (cons g (cond [(assq g air) => cadr] [else 0])))]))

(define (check-enclosures parts)
  (for ([p parts] #:when (eq? (part-kind p) 'enclosure))
    (define (prop k) (cdr (assq k (part-props p))))
    (define loc (part-loc p))
    (define (bad what)
      (error 'define-machine "~a:~a:~a: enclosure ~a: ~a"
             (vector-ref loc 0) (vector-ref loc 1) (vector-ref loc 2) (part-id p) what))
    (for ([k '(size-x size-y size-z)])
      (unless (and (real? (prop k)) (> (prop k) 0)) (bad (format "#:size must be (w h d), each above 0 m, got ~e" (prop k)))))
    (unless (or (not (prop 'pressure)) (and (real? (prop 'pressure)) (>= (prop 'pressure) 0)))
      (bad (format "#:pressure must be an absolute pressure, 0 Pa or more, got ~e" (prop 'pressure))))
    (unless (or (not (prop 'temperature)) (and (real? (prop 'temperature)) (> (prop 'temperature) -273.15)))
      (bad (format "#:temperature must be above absolute zero, got ~e" (prop 'temperature))))
    (for ([k '(insulation leak heater heat-capacity supply)])
      (unless (and (real? (prop k)) (>= (prop k) 0)) (bad (format "#:~a must be 0 or more, got ~e" k (prop k)))))
    (unless (and (real? (prop 'coefficient)) (> (prop 'coefficient) 0) (<= (prop 'coefficient) 1))
      (bad (format "#:coefficient must be in (0, 1], got ~e" (prop 'coefficient)))))
  parts)

;; A mirror's numbers are checked when the machine is built.
(define (check-mirrors parts)
  (for ([p parts] #:when (eq? (part-kind p) 'mirror))
    (define (prop k) (cdr (assq k (part-props p))))
    (define loc (part-loc p))
    (define (bad what)
      (error 'define-machine "~a:~a:~a: mirror ~a: ~a"
             (vector-ref loc 0) (vector-ref loc 1) (vector-ref loc 2) (part-id p) what))
    (unless (and (real? (prop 'area)) (> (prop 'area) 0)) (bad (format "#:area must be above 0, got ~e" (prop 'area))))
    (unless (and (real? (prop 'reflectivity)) (> (prop 'reflectivity) 0) (<= (prop 'reflectivity) 1))
      (bad (format "#:reflectivity must be in (0, 1], got ~e" (prop 'reflectivity)))))
  parts)

(define (check-windmills parts)
  (for ([p parts] #:when (eq? (part-kind p) 'windmill))
    (define (prop k) (cdr (assq k (part-props p))))
    (define loc (part-loc p))
    (define (bad what)
      (error 'define-machine "~a:~a:~a: windmill ~a: ~a"
             (vector-ref loc 0) (vector-ref loc 1) (vector-ref loc 2) (part-id p) what))
    (unless (and (real? (prop 'radius)) (> (prop 'radius) 0)) (bad (format "#:radius must be a length above 0, got ~e" (prop 'radius))))
    (unless (and (real? (prop 'mass)) (> (prop 'mass) 0)) (bad (format "#:mass must be above 0, got ~e" (prop 'mass))))
    (unless (and (real? (prop 'wind)) (>= (prop 'wind) 0)) (bad (format "#:wind must be a speed, 0 or more, got ~e" (prop 'wind))))
    (unless (and (real? (prop 'load)) (>= (prop 'load) 0)) (bad (format "#:load must be a torque, 0 or more, got ~e" (prop 'load))))
    (unless (and (real? (prop 'cp)) (> (prop 'cp) 0) (<= (prop 'cp) 16/27))
      (bad (format "#:cp must be above 0 and at most the Betz limit, 16/27 = 0.593 (no rotor takes more of the wind), got ~e" (prop 'cp))))
    (unless (and (real? (prop 'tip-speed-ratio)) (> (prop 'tip-speed-ratio) 0))
      (bad (format "#:tip-speed-ratio must be above 0, got ~e" (prop 'tip-speed-ratio)))))
  parts)

(define (make-machine name source ambient sun planet-v items)
  (unless (planet? planet-v)
    (error 'define-machine "machine ~a: #:planet must be a planet (earth, mars, or (planet mars #:gravity 9.81 ...)), got ~e" name planet-v))
  (unless (and (real? ambient) (> ambient -273.15))
    (error 'define-machine "machine ~a: #:ambient must be a temperature in °C above absolute zero, got ~e" name ambient))
  (when sun
    (define-values (lat day time) (apply values sun))
    (unless (and (real? lat) (<= -90 lat 90))
      (error 'define-machine "machine ~a: #:latitude must be in [-90, 90] degrees, got ~e" name lat))
    (define year (inexact->exact (round (planet-field planet-v 'year))))
    (unless (and (exact-integer? day) (<= 1 day year))
      (error 'define-machine "machine ~a: #:day must be a day of the year, 1 to ~a, got ~e" name year day))
    (unless (and (real? time) (<= 0 time) (< time 24))
      (error 'define-machine "machine ~a: #:time must be solar hours in [0, 24), got ~e" name time)))
  (machine name source ambient sun planet-v
           (check-enclosures (check-mirrors (check-capstans (check-windmills (check-pumps (place-safety-valves (place-leaks (place-float-valves (place-sluices (filter part? items) (filter channel-spec? items)) items))))))))
           (filter pipe-spec? items)
           (filter connect-spec? items)
           (filter air-spec? items)
           (filter rope-spec? items)
           (filter arbor-spec? items)
           (filter mesh-spec? items)
           (filter lift-spec? items)
           (filter cylinder-spec? items)
           (filter inflow-spec? items)
           (filter channel-spec? items)
           (filter trigger-spec? items)
           (filter follow-spec? items)
           (filter belt-spec? items)
           (filter joint-spec? items)))

;; Machines register themselves when their module runs, so the build
;; script can collect every machine in a file without knowing their names.
(define registry '())
(define (register-machine! m) (set! registry (cons m registry)))
(define (take-registered-machines)
  (begin0 (reverse registry) (set! registry '())))

;; ---------------------------------------------------------------------------
;; Clause keywords. They mean something only inside define-machine.

(define-syntax-rule (define-clause-keywords id ...)
  (begin
    (define-syntax (id stx)
      (raise-syntax-error #f "only allowed inside define-machine" stx))
    ...))

(define-clause-keywords tank boiler rotor jetwheel smokejack block pendulum lever ramp wheel screw fixture post hearth bellows sluice waterwheel windmill capstan mirror counterpoise float-valve leak safety-valve pump enclosure grip
  pipe connect sealed-air port rope world arbor mesh lift piston atmospheric-cylinder
  inflow channel off trigger follow belt joint)

;; A part built from a generated shape (see heroic/geometry). The shape is
;; an ordinary runtime value, so whether it suits the clause is checked
;; here, when the machine's module runs, and reported at the clause.
(define (shaped-part id kind mat at s props loc)
  (define (fail fmt . args)
    (error 'define-machine "~a:~a:~a: ~a" (vector-ref loc 0) (vector-ref loc 1) (vector-ref loc 2)
           (apply format fmt args)))
  (unless (shape? s)
    (fail "~a ~a: #:shape must be a generated shape (spur-gear, pulley, vitruvian-screw, ...), got ~e"
          kind id s))
  (case kind
    [(wheel) (unless (memq (shape-kind s) wheel-kinds)
               (fail "wheel ~a: a ~a shape is not a wheel (wheels are ~a)~a" id (shape-kind s) wheel-kinds
                     (if (eq? (shape-kind s) 'screw) "; use the screw clause" "")))]
    [(screw) (unless (eq? (shape-kind s) 'screw)
               (fail "screw ~a: needs a screw shape (archimedes-screw, vitruvian-screw), got a ~a"
                     id (shape-kind s)))]
    [else (void)])
  (part id kind mat at (cons (cons 'shape s) props) '() loc))

;; ---------------------------------------------------------------------------
;; Compile-time checking

(begin-for-syntax
  ;; What the checker needs to know about each part.
  (struct pinfo (id kind mat ports)) ; ports: (listof (cons symbol kind))
  (struct linfo (type id from to))   ; type: 'pipe | 'connect
  (struct ainfo (tanks))             ; tanks: (listof identifier)
  (struct rinfo (id ends drum))      ; ends: identifiers naming parts (or world); drum: identifier or #f
  (struct arinfo (parts))            ; identifiers of wheels on one axle
  (struct minfo (a b))               ; two gears in mesh
  (struct linfo2 (id by from to))    ; a water lift
  (struct cinfo (id piston boiler))  ; an atmospheric cylinder
  (struct iinfo (id into))           ; an inflow
  (struct hinfo (id heats))          ; a hearth: the boiler it heats
  (struct bvinfo (id on mat))
  (struct mrinfo (id onto))          ; a mirror: the boiler or sealed vessel it throws the sun onto        ; a bellows: the hearth it forces draught into
  (struct sinfo (id on))             ; a sluice gate: the channel it stands across
  (struct fvinfo (id feed mat))      ; a float valve: the inflow, pipe or channel it throttles
  (struct lkinfo (id on into mat))   ; a leak: the tank it is in, the tank under it (or #f)
  (struct svinfo (id on mat))        ; a safety valve: the boiler whose lid it sits in
  (struct puinfo (id from to mat))   ; a lift pump: the tank it draws from, the tank it fills
  (struct cpinfo (id vessel))        ; a counterpoise: the tank that hangs from it
  (struct winfo (id race tail))      ; a water wheel: the channel it stands in, the tank it spills to (or #f)
  (struct grinfo (id on mat))        ; a grip: the body it hangs on (or #f: the world)
  (struct beinfo (id a b))           ; a belt: the two drums it runs on
  (struct fwinfo (id lever rope))    ; a follow: the lever it follows, or the rope
  (struct jinfo (id kind a b))       ; a joint: its kind and the two parts (or world)
  (struct trinfo (id body))          ; a trigger: the part it watches, or #f
  (struct chinfo (id from to onto))  ; a channel: from a ref, to a ref or #f (off the scene), onto a hearth/boiler id or #f

  (define known-materials (material-ids))

  (define (loc-of stx)
    (define src (syntax-source stx))
    #`(quote #,(vector (cond [(path? src) (path->string src)]
                             [(string? src) src]
                             [else "?"])
                       (or (syntax-line stx) 0)
                       (or (syntax-column stx) 0))))

  (define-syntax-class vec3
    #:description "a position (x y z)"
    (pattern (x:expr y:expr z:expr)))

  (define-syntax-class xz
    #:description "a waypoint (x z)"
    (pattern (x:expr z:expr)))

  (define-syntax-class axis-name
    #:description "an axle direction: x, y or z"
    (pattern a:id #:when (memq (syntax-e #'a) '(x y z))))

  (define-syntax-class rope-end
    #:description "a rope end (part x y z) — a point in that part's own frame, or (world x y z)"
    (pattern (part:id x:expr y:expr z:expr)))

  (define-syntax-class port-clause
    #:literals (port)
    #:description "(port name #:height h)"
    (pattern (port name:id #:height h:expr)))

  (define-syntax-class ref
    #:description "a part.port reference such as kettle.steam"
    (pattern r:id
      #:do [(define pieces (string-split (symbol->string (syntax-e #'r)) "." #:trim? #f))]
      #:fail-unless (= (length pieces) 2) "expected part.port, such as kettle.steam"
      #:with part-id (datum->syntax #'r (string->symbol (first pieces)) #'r)
      #:with port-id (datum->syntax #'r (string->symbol (second pieces)) #'r)))

  (define-syntax-class clause
    #:description "a part (tank, boiler, rotor, jetwheel, smokejack, block, pendulum, lever, ramp, wheel, screw, fixture, post, hearth, bellows, sluice, waterwheel, windmill, capstan, mirror, counterpoise, float-valve, leak, safety-valve, pump, enclosure, grip) or link (pipe, connect, sealed-air)"
    #:literals (enclosure grip tank boiler rotor jetwheel smokejack block pendulum lever ramp wheel screw fixture post hearth bellows sluice waterwheel windmill capstan mirror counterpoise float-valve leak safety-valve pump piston pipe connect sealed-air rope arbor mesh lift atmospheric-cylinder inflow channel off trigger follow belt joint)
    #:attributes (expr info)

    (pattern (tank id:id
                   (~alt (~once (~seq #:at at:vec3))
                         (~once (~seq #:area area-v:expr))
                         (~once (~seq #:height height-v:expr))
                         (~optional (~seq #:water water-v:expr))
                         (~optional (~seq #:material mat:id))) ...
                   p:port-clause ...)
      #:attr info (pinfo #'id 'tank (attribute mat)
                         (for/list ([n (syntax->list #'(p.name ...))]) (cons (syntax-e n) 'water)))
      #:with expr #`(part 'id 'tank '(~? mat bronze) (list at.x at.y at.z)
                          (list (cons 'area area-v) (cons 'height height-v) (cons 'water (~? water-v 0)))
                          (list (port-spec 'p.name 'water p.h) ...)
                          #,(loc-of this-syntax)))

    (pattern (boiler id:id
                     (~alt (~once (~seq #:at at:vec3))
                           (~once (~seq #:radius radius-v:expr))
                           (~once (~seq #:height height-v:expr))
                           (~once (~seq #:water water-v:expr))
                           (~optional (~seq #:fire fire-v:expr))
                           (~optional (~seq #:temperature temp-v:expr))
                           (~optional (~seq #:burst burst-v:expr))
                           (~optional (~seq #:material mat:id))) ...)
      #:attr info (pinfo #'id 'boiler (attribute mat) (list (cons 'steam 'steam)))
      #:with expr #`(part 'id 'boiler '(~? mat bronze) (list at.x at.y at.z)
                          (list* (cons 'radius radius-v) (cons 'height height-v)
                                 (cons 'water water-v) (cons 'fire (~? fire-v 0))
                                 (cons 'temperature (~? temp-v #f))
                                 (~? (list (cons 'burst burst-v)) '()))
                          (list (port-spec 'steam 'steam height-v))
                          #,(loc-of this-syntax)))

    (pattern (rotor id:id
                    (~alt (~once (~seq #:at at:vec3))
                          (~once (~seq #:radius radius-v:expr))
                          (~once (~seq #:material mat:id))
                          (~once (~seq #:bore bore-v:expr))
                          (~once (~seq #:arm arm-v:expr))
                          (~optional (~seq #:wall wall-v:expr))
                          (~optional (~seq #:nozzles nozzles-v:expr))) ...)
      #:attr info (pinfo #'id 'rotor (attribute mat) (list (cons 'steam-in 'steam)))
      #:with expr #`(part 'id 'rotor 'mat (list at.x at.y at.z)
                          (list (cons 'radius radius-v) (cons 'wall (~? wall-v 1/1000))
                                (cons 'bore bore-v) (cons 'arm arm-v) (cons 'nozzles (~? nozzles-v 2)))
                          (list (port-spec 'steam-in 'steam 0))
                          #,(loc-of this-syntax)))

    ;; A paddle wheel turned by a jet of steam, after Giovanni Branca's 1629
    ;; design: a spout of #:bore on the boiler blows steam at flat paddles
    ;; on a wheel of #:radius. The push on the paddles is the jet's mass flow
    ;; times how much faster the steam moves than the paddles, so the wheel
    ;; runs up until that balances its #:load (a torque, N·m) and its
    ;; bearing. #:mass sits at the rim (I = m r²).
    (pattern (jetwheel id:id
                    (~alt (~once (~seq #:at at:vec3))
                          (~once (~seq #:radius radius-v:expr))
                          (~once (~seq #:material mat:id))
                          (~once (~seq #:bore bore-v:expr))
                          (~optional (~seq #:paddles paddles-v:expr))
                          (~optional (~seq #:width width-v:expr))
                          (~optional (~seq #:mass mass-v:expr))
                          (~optional (~seq #:load load-v:expr))) ...)
      #:attr info (pinfo #'id 'jetwheel (attribute mat) (list (cons 'steam-in 'steam)))
      #:with expr #`(part 'id 'jetwheel 'mat (list at.x at.y at.z)
                          (list (cons 'radius radius-v) (cons 'bore bore-v)
                                (cons 'paddles (~? paddles-v 8)) (cons 'width (~? width-v 3/100))
                                (cons 'mass (~? mass-v 1/2)) (cons 'load (~? load-v 0)))
                          (list (port-spec 'steam-in 'steam 0))
                          #,(loc-of this-syntax)))

    ;; A smoke jack: a wheel of angled vanes in a kitchen chimney, turned by
    ;; the hot air rising from the hearth #:over it (the share of the fire's
    ;; heat its pot doesn't take). The draught is the chimney's stack effect,
    ;; #:chimney-height tall and #:chimney-area across; the wheel turns a
    ;; spit against #:load (N.m).
    (pattern (smokejack id:id
                    (~alt (~once (~seq #:at at:vec3))
                          (~once (~seq #:over over-v:id))
                          (~once (~seq #:radius radius-v:expr))
                          (~once (~seq #:material mat:id))
                          (~optional (~seq #:vanes vanes-v:expr))
                          (~optional (~seq #:width width-v:expr))
                          (~optional (~seq #:mass mass-v:expr))
                          (~optional (~seq #:load load-v:expr))
                          (~optional (~seq #:chimney-height ch-v:expr))
                          (~optional (~seq #:chimney-area ca-v:expr))) ...)
      #:attr info (pinfo #'id 'smokejack (attribute mat) '())
      #:with expr #`(part 'id 'smokejack 'mat (list at.x at.y at.z)
                          (list (cons 'over 'over-v) (cons 'radius radius-v)
                                (cons 'vanes (~? vanes-v 6)) (cons 'width (~? width-v 6/100))
                                (cons 'mass (~? mass-v 3/10)) (cons 'load (~? load-v 0))
                                (cons 'chimney-height (~? ch-v 2)) (cons 'chimney-area (~? ca-v 5/100)))
                          '()
                          #,(loc-of this-syntax)))

    ;; A cube of #:size of the given material, its mass and friction from
    ;; the material table. #:tilt-deg turns it about the X axis, the way a
    ;; ramp tilts — so it can start resting flat on a slope instead of being
    ;; dropped onto it. #:dimensions (x y z) makes it a box of that size
    ;; instead (a catapult's bolt). #:fast #f turns off continuous collision
    ;; detection for it: by default a block is swept along its path each step,
    ;; so a bolt at 60 m/s (half a metre a tick) cannot tunnel through a thin
    ;; plank; a heap of slow blocks can save the cost of that.
    (pattern (block id:id
                    (~alt (~once (~seq #:at at:vec3))
                          (~once (~seq #:size size-v:expr))
                          (~once (~seq #:material mat:id))
                          (~optional (~seq #:tilt-deg tilt-v:expr))
                          (~optional (~seq #:fast fast-v:expr))
                          (~optional (~seq #:dimensions dims:vec3))) ...)
      #:attr info (pinfo #'id 'block (attribute mat) '())
      #:with expr #`(part 'id 'block 'mat (list at.x at.y at.z)
                          (list (cons 'size size-v)
                                (cons 'tilt-deg (~? tilt-v 0))
                                (cons 'fast (~? fast-v #t))
                                (~@ . (~? ((cons 'dim-x dims.x) (cons 'dim-y dims.y) (cons 'dim-z dims.z)) ())))
                          '()
                          #,(loc-of this-syntax)))

    ;; A compound pendulum: a rod hanging from a fixed pivot at #:at, with a
    ;; bob at its far end. Jolt computes its real moment of inertia from the
    ;; rod+bob shapes, so this swings with genuine (not idealized point-mass)
    ;; pendulum dynamics — released from #:start-angle-deg off vertical.
    ;;
    ;; Hung on a bearing, #:bearing-radius m (the pin's radius), it swings in
    ;; the sim instead, so its run-down can be checked: the bearing carries
    ;; its weight N = m·g and resists with Coulomb friction μ·N·r
    ;; (#:bearing-mu μ, dry metal) and viscous drag c·ω (#:bearing-drag c,
    ;; N·m·s/rad, grease). Friction's work is heat; the pin wears by
    ;; Archard's law V = K·N·s, #:bearing-wear K in mm³/(N·m).
    (pattern (pendulum id:id
                       (~alt (~once (~seq #:at at:vec3))
                             (~once (~seq #:length length-v:expr))
                             (~once (~seq #:material mat:id))
                             (~optional (~seq #:start-angle-deg angle-v:expr))
                             (~optional (~seq #:bearing-radius journal-v:expr))
                             (~optional (~seq (~and mu-kw #:bearing-mu) mu-v:expr))
                             (~optional (~seq (~and drag-kw #:bearing-drag) drag-v:expr))
                             (~optional (~seq (~and wear-kw #:bearing-wear) wear-v:expr))) ...)
      #:fail-when (and (not (attribute journal-v)) (or (attribute mu-kw) (attribute drag-kw) (attribute wear-kw)))
                  "a pendulum's bearing needs a #:bearing-radius (its pin's radius, m)"
      #:attr info (pinfo #'id 'pendulum (attribute mat) '())
      #:with expr #`(part 'id 'pendulum 'mat (list at.x at.y at.z)
                          (list (cons 'length length-v) (cons 'start-angle-deg (~? angle-v 0))
                                (~@ . (~? ((cons 'bearing-radius journal-v)
                                           (cons 'bearing-mu (~? mu-v 0))
                                           (cons 'bearing-drag (~? drag-v 0))
                                           (cons 'bearing-wear (~? wear-v 0)))
                                          ())))
                          '()
                          #,(loc-of this-syntax)))

    ;; A lever/see-saw: a beam hinged at its centre (#:at). Rest weights on
    ;; it with separate `block` parts — Jolt's own contact physics settles
    ;; the torque balance, no separate lever equation needed.
    ;; #:pivot-fraction places the hinge along the beam: 0.5 (default) is
    ;; a centred see-saw; nearer 0 or 1 is a lopsided beam — a short heavy
    ;; end and a long light end, i.e. a trebuchet arm. #:limit-deg caps
    ;; rotation each way (a real see-saw has stops); pass a large value
    ;; (or omit near 90) to let a trebuchet swing through its full arc.
    ;; #:axis turns the hinge to x, y or z (default z, facing the camera);
    ;; #:limit-lower-deg / #:limit-upper-deg give uneven stops. A torsion
    ;; spring — twisted sinew, like a catapult's — is #:spring-stiffness
    ;; (N·m per radian) pulling the lever toward #:spring-rest-deg.
    ;; #:section makes the beam square, that many metres a side, instead of
    ;; the default plank (2.5 cm × 22 cm) — a catapult's arm is a stout rod.
    (pattern (lever id:id
                    (~alt (~once (~seq #:at at:vec3))
                          (~once (~seq #:length length-v:expr))
                          (~once (~seq #:material mat:id))
                          (~optional (~seq #:start-angle-deg angle-v:expr))
                          (~optional (~seq #:pivot-fraction pivot-v:expr))
                          (~optional (~seq #:limit-deg limit-v:expr))
                          (~optional (~seq #:damping damping-v:expr))
                          (~optional (~seq #:axis ax:axis-name))
                          (~optional (~seq #:limit-lower-deg lo-v:expr))
                          (~optional (~seq #:limit-upper-deg hi-v:expr))
                          (~optional (~seq #:spring-stiffness k-v:expr))
                          (~optional (~seq #:spring-rest-deg rest-v:expr))
                          (~optional (~seq #:section section-v:expr))) ...)
      #:attr info (pinfo #'id 'lever (attribute mat) '())
      #:with expr #`(part 'id 'lever 'mat (list at.x at.y at.z)
                          (list (cons 'length length-v) (cons 'start-angle-deg (~? angle-v 0))
                                (cons 'pivot-fraction (~? pivot-v 1/2))
                                (cons 'limit-deg (~? limit-v 18))
                                (cons 'damping (~? damping-v 8.0))
                                (cons 'axis '(~? ax z))
                                (cons 'limit-lower-deg (~? lo-v #f))
                                (cons 'limit-upper-deg (~? hi-v #f))
                                (cons 'spring-stiffness (~? k-v 0))
                                (cons 'spring-rest-deg (~? rest-v 0))
                                (cons 'section (~? section-v #f)))
                          '()
                          #,(loc-of this-syntax)))

    ;; A static, immovable ramp. Drop `block` parts on it to compare
    ;; per-material friction — this needs no new mechanics at all, since
    ;; the existing material friction coefficients already drive the result.
    (pattern (ramp id:id
                   (~alt (~once (~seq #:at at:vec3))
                         (~once (~seq #:length length-v:expr))
                         (~once (~seq #:width width-v:expr))
                         (~once (~seq #:angle-deg angle-v:expr))
                         (~once (~seq #:material mat:id))) ...)
      #:attr info (pinfo #'id 'ramp (attribute mat) '())
      #:with expr #`(part 'id 'ramp 'mat (list at.x at.y at.z)
                          (list (cons 'length length-v) (cons 'width width-v) (cons 'angle-deg angle-v))
                          '()
                          #,(loc-of this-syntax)))

    ;; Parts with generated geometry. #:shape is any expression producing
    ;; a shape — (spur-gear #:teeth 64 ...), (noria ...), a catalogue
    ;; entry — so shapes can be named, shared and computed like any value.
    ;;
    ;; A wheel turns on an axle through #:at along #:axis (z, the default,
    ;; faces the camera), or raised #:tilt-deg from it (an x or z axle
    ;; tilts up toward y; a y axle leans toward x). #:angle-deg sets where it starts turned to — how
    ;; meshing gears are phased (see mate-angle). #:drive-rpm turns it at
    ;; that steady speed, as a man at a crank or a treadmill would; without
    ;; it the wheel turns only if something pushes it. #:drive-torque caps
    ;; what that drive can deliver (N·m) — men walking in a treadwheel can
    ;; only push so hard — so a load too heavy for it wins and runs the
    ;; wheel backwards. Without it, the drive holds its speed whatever it
    ;; takes.
    (pattern (wheel id:id
                    (~alt (~once (~seq #:shape shape-v:expr))
                          (~once (~seq #:at at:vec3))
                          (~once (~seq #:material mat:id))
                          (~optional (~seq #:axis ax:axis-name))
                          (~optional (~seq #:angle-deg angle-v:expr))
                          (~optional (~seq #:tilt-deg tilt-v:expr))
                          (~optional (~seq #:drive-rpm rpm-v:expr))
                          (~optional (~seq #:drive-torque torque-v:expr))) ...)
      #:attr info (pinfo #'id 'wheel (attribute mat) '())
      #:with expr #`(shaped-part 'id 'wheel 'mat (list at.x at.y at.z) shape-v
                                 (list (cons 'axis '(~? ax z)) (cons 'angle-deg (~? angle-v 0))
                                       (~@ . (~? ((cons 'tilt-deg tilt-v)) ()))
                                       (cons 'drive-rpm (~? rpm-v 0)) (cons 'drive-torque (~? torque-v #f)))
                                 #,(loc-of this-syntax)))

    ;; An Archimedes' screw, its axle running along X and raised
    ;; #:tilt-deg from level (Vitruvius sets it at vitruvian-screw-incline-deg).
    (pattern (screw id:id
                    (~alt (~once (~seq #:shape shape-v:expr))
                          (~once (~seq #:at at:vec3))
                          (~once (~seq #:material mat:id))
                          (~optional (~seq #:tilt-deg tilt-v:expr))
                          (~optional (~seq #:drive-rpm rpm-v:expr))
                          (~optional (~seq #:drive-torque torque-v:expr))) ...)
      #:attr info (pinfo #'id 'screw (attribute mat) '())
      #:with expr #`(shaped-part 'id 'screw 'mat (list at.x at.y at.z) shape-v
                                 (list (cons 'tilt-deg (~? tilt-v 0)) (cons 'drive-rpm (~? rpm-v 0))
                                       (cons 'drive-torque (~? torque-v #f)))
                                 #,(loc-of this-syntax)))

    ;; Generated geometry that doesn't move: a catapult's frame, a stand.
    ;; #:turn-deg turns it about the vertical.
    (pattern (fixture id:id
                      (~alt (~once (~seq #:shape shape-v:expr))
                            (~once (~seq #:at at:vec3))
                            (~once (~seq #:material mat:id))
                            (~optional (~seq #:turn-deg turn-v:expr))) ...)
      #:attr info (pinfo #'id 'fixture (attribute mat) '())
      #:with expr #`(shaped-part 'id 'fixture 'mat (list at.x at.y at.z) shape-v
                                 (list (cons 'turn-deg (~? turn-v 0)))
                                 #,(loc-of this-syntax)))

    ;; A post, pier or wall: a fixed block of #:material standing on the
    ;; ground, #:at its base centre, #:size (width height depth). Other
    ;; parts can rest on it or be held up by it; it never moves. #:round #t
    ;; makes it a column (its width the diameter).
    (pattern (post id:id
                   (~alt (~once (~seq #:at at:vec3))
                         (~once (~seq #:size size:vec3))
                         (~once (~seq #:material mat:id))
                         (~optional (~seq #:round round-v:expr))) ...)
      #:attr info (pinfo #'id 'post (attribute mat) '())
      #:with expr #`(part 'id 'post 'mat (list at.x at.y at.z)
                          (list (cons 'size-x size.x) (cons 'size-y size.y) (cons 'size-z size.z)
                                (cons 'round (and (~? round-v #f) #t)))
                          '()
                          #,(loc-of this-syntax)))

    ;; A hearth: a fire of #:fuel kg of wood, charcoal or coal burning at
    ;; #:power watts of heat, warming the boiler named by #:heats. Heat
    ;; that reaches the water is power × #:efficiency (open fires waste
    ;; most of theirs; default 0.5). It burns fuel at power ÷ energy density and
    ;; goes out when the fuel is gone — feed it with (set hearth fuel).
    (pattern (hearth id:id
                     (~alt (~once (~seq #:at at:vec3))
                           (~once (~seq #:heats boiler-id:id))
                           (~once (~seq #:power power-v:expr))
                           (~once (~seq #:fuel fuel-v:expr))
                           (~optional (~seq #:fuel-kind kind:id))
                           (~optional (~seq #:efficiency eff-v:expr))) ...)
      #:fail-unless (memq (syntax-e (or (attribute kind) #'wood)) '(wood charcoal coal))
                    "#:fuel-kind is wood, charcoal or coal"
      #:attr info (hinfo #'id #'boiler-id)
      #:with expr #`(part 'id 'hearth 'limestone (list at.x at.y at.z)
                          (list (cons 'heats 'boiler-id) (cons 'power power-v) (cons 'fuel fuel-v)
                                (cons 'fuel-kind '(~? kind wood)) (cons 'efficiency (~? eff-v 1/2)))
                          '()
                          #,(loc-of this-syntax)))

    ;; A bellows or fan working #:airflow (m3/s, e.g. (L/s 40)) into the
    ;; hearth #:on names. A hearth's #:power already implies a natural
    ;; draught — the air its steady burn rate draws, power / energy-density
    ;; x its fuel's stoichiometric air-fuel ratio. The bellows adds its own
    ;; air on top of that; a fire is worked exactly as much harder as the
    ;; total air is more than the natural draught alone, so #:airflow raises
    ;; the burn RATE (and, while it lasts, the heat given off), never the
    ;; total energy a load of fuel can ever release — still fuel x energy
    ;; density, however fast it is burned. Settable at run time
    ;; ((set id airflow v)) — work it harder, ease off, or still it.
;; A heliostat: a flat mirror of #:area m2 turned through the day to keep
    ;; throwing the sun onto #:onto, a boiler or a tank in a sealed-air.
    ;; It faces halfway between the sun and its target, so it catches
    ;; DNI x area x #:reflectivity (default 0.85; polished bronze ~0.6) x
    ;; cos(theta/2), theta the angle between them seen from the mirror.
    ;; Settable: (set id area a) -- cover it with 0.
    (pattern (mirror id:id
                     (~alt (~once (~seq #:at at:vec3))
                           (~once (~seq #:area area-v:expr))
                           (~once (~seq #:onto target:id))
                           (~optional (~seq #:reflectivity refl-v:expr))
                           (~optional (~seq #:material mat:id))) ...)
      #:attr info (mrinfo #'id #'target)
      #:with expr #`(part 'id 'mirror '(~? mat bronze) (list at.x at.y at.z)
                          (list (cons 'onto 'target) (cons 'area area-v) (cons 'reflectivity (~? refl-v 0.85)))
                          '()
                          #,(loc-of this-syntax)))

    ;; An enclosure (issue #39): a box #:size (w h d) across, #:at the middle
    ;; of its floor, holding its own air -- #:pressure (absolute Pa), #:air
    ;; '((o2 0.21) (n2 0.79)) and #:temperature (°C), each by default its
    ;; surroundings' -- which every part standing inside it reads instead of
    ;; the planet's. Its walls lose #:insulation W/K (default 2, a well
    ;; insulated membrane) and hold #:heat-capacity J/K (default 0); a
    ;; #:heater (W) warms it, and so can a hearth or mirror aimed at it. A
    ;; hole of #:leak m² (default 0) lets gas out, or in, as a compressible
    ;; orifice with discharge coefficient #:coefficient (default 0.6). A fan
    ;; or bellows can blow in #:supply m³/s of the surroundings' air (default
    ;; 0): a room's air supply, which a leak then lets out again.
    ;; Enclosures nest: one inside another leaks and loses heat into it.
    (pattern (enclosure id:id
                        (~alt (~once (~seq #:at at:vec3))
                              (~once (~seq #:size size:vec3))
                              (~optional (~seq #:pressure pressure-v:expr))
                              (~optional (~seq #:air air-v:expr))
                              (~optional (~seq #:temperature temp-v:expr))
                              (~optional (~seq #:insulation ua-v:expr))
                              (~optional (~seq #:heat-capacity cap-v:expr))
                              (~optional (~seq #:heater heater-v:expr))
                              (~optional (~seq #:leak leak-v:expr))
                              (~optional (~seq #:supply supply-v:expr))
                              (~optional (~seq #:coefficient cd-v:expr))
                              (~optional (~seq #:material mat:id))) ...)
      #:attr info (pinfo #'id 'enclosure (attribute mat) '())
      #:with expr #`(part 'id 'enclosure '(~? mat hemp) (list at.x at.y at.z)
                          (list* (cons 'size-x size.x) (cons 'size-y size.y) (cons 'size-z size.z)
                                 (cons 'pressure (~? pressure-v #f)) (cons 'temperature (~? temp-v #f))
                                 (cons 'insulation (~? ua-v 2)) (cons 'heat-capacity (~? cap-v 0))
                                 (cons 'heater (~? heater-v 0)) (cons 'leak (~? leak-v 0)) (cons 'supply (~? supply-v 0))
                                 (cons 'coefficient (~? cd-v 0.6))
                                 (enclosure-air-props 'id (~? air-v #f) #,(loc-of this-syntax)))
                          '()
                          #,(loc-of this-syntax)))

    ;; Tongs or a hook at #:at, hung on the body #:on names (a lever, a wheel,
    ;; a block...) or on the world (the default). While closed it takes hold
    ;; of the nearest loose block within #:reach (default 15 cm) and carries it;
    ;; opened, or overloaded, it lets go. #:kind tongs squeeze with #:force N a
    ;; jaw and carry 2 mu F, mu the lower of the jaws' and the load's friction,
    ;; so a mass m needs F >= m g / (2 mu); #:kind hook carries #:strength N.
    ;; It starts open; #:closed 1 starts it shut. Set (grip closed 1) or 0 at
    ;; run time, or let a trigger or a follow do it.
    (pattern (grip id:id
                   (~alt (~once (~seq #:at at:vec3))
                         (~optional (~seq #:on on-id:id))
                         (~optional (~seq #:kind kind:id))
                         (~optional (~seq #:reach reach-v:expr))
                         (~optional (~seq #:force force-v:expr))
                         (~optional (~seq #:strength strength-v:expr))
                         (~optional (~seq #:closed closed-v:expr))
                         (~optional (~seq #:material mat:id))) ...)
      #:fail-when (and (attribute kind) (not (memq (syntax-e #'kind) '(tongs hook))) #'kind) "#:kind is tongs or hook"
      #:attr info (grinfo #'id (and (attribute on-id) #'on-id) (attribute mat))
      #:with expr #`(part 'id 'grip '(~? mat bronze) (list at.x at.y at.z)
                          (list (cons 'on '(~? on-id world)) (cons 'kind '(~? kind tongs)) (cons 'reach (~? reach-v 0.15))
                                (cons 'force (~? force-v 0)) (cons 'strength (~? strength-v 0)) (cons 'closed (~? closed-v 0)))
                          '()
                          #,(loc-of this-syntax)))

    (pattern (bellows id:id
                      (~alt (~once (~seq #:at at:vec3))
                            (~once (~seq #:on hearth-id:id))
                            (~once (~seq #:airflow airflow-v:expr))
                            (~optional (~seq #:material mat:id))) ...)
      #:attr info (bvinfo #'id #'hearth-id (attribute mat))
      #:with expr #`(part 'id 'bellows '(~? mat oak) (list at.x at.y at.z)
                          (list (cons 'on 'hearth-id) (cons 'airflow airflow-v))
                          '()
                          #,(loc-of this-syntax)))

    ;; A water wheel on a horizontal axle turning a millstone that resists
    ;; with #:load N·m (settable: (set id load v)). #:mass kg sits mostly in
    ;; the rim, so I = mass × radius². Two ways to drive it, either or both:
    ;;   overshot   #:buckets n of #:bucket-volume m³ each; a channel run
    ;;              #:to off pours #:onto it at the top, and the buckets
    ;;              carry the water down until they have turned #:spill-deg
    ;;              (default 120: buckets start tipping near 90° and are empty by 150°), then tip it into #:tail
    ;;              (a tank) or away. Power ρ·g·Q·r·(1 − cos θ).
    ;;   undershot  #:race channel: it stands in that channel, whose current
    ;;              pushes on paddles #:paddle-depth deep; at best 8/27 of
    ;;              the stream's kinetic energy through them.
    (pattern (waterwheel id:id
                         (~alt (~once (~seq #:at at:vec3))
                               (~once (~seq #:radius radius-v:expr))
                               (~once (~seq #:width width-v:expr))
                               (~once (~seq #:mass mass-v:expr))
                               (~optional (~seq #:load load-v:expr))
                               (~optional (~seq #:buckets buckets-v:expr))
                               (~optional (~seq #:bucket-volume bucket-v:expr))
                               (~optional (~seq #:spill-deg spill-v:expr))
                               (~optional (~seq #:tail tail-tank:id))
                               (~optional (~seq #:race race-ch:id))
                               (~optional (~seq #:paddle-depth paddle-v:expr))
                               (~optional (~seq #:material mat:id))) ...)
      #:fail-unless (or (attribute buckets-v) (attribute race-ch)) "a water wheel needs #:buckets (overshot) or a #:race (undershot) to be driven"
      #:fail-when (and (attribute buckets-v) (not (attribute bucket-v)) #'id) "#:buckets needs a #:bucket-volume"
      #:fail-when (and (attribute race-ch) (not (attribute paddle-v)) #'id) "an undershot wheel (#:race) needs a #:paddle-depth"
      #:attr info (winfo #'id (attribute race-ch) (attribute tail-tank))
      #:with expr #`(part 'id 'waterwheel '(~? mat oak) (list at.x at.y at.z)
                          (list (cons 'radius radius-v) (cons 'width width-v) (cons 'mass mass-v)
                                (cons 'load (~? load-v 0))
                                (cons 'buckets (~? buckets-v 0)) (cons 'bucket-volume (~? bucket-v 0))
                                (cons 'spill-deg (~? spill-v 120))
                                (cons 'tail (~? 'tail-tank #f)) (cons 'race (~? 'race-ch #f))
                                (cons 'paddle-depth (~? paddle-v 0)))
                          '()
                          #,(loc-of this-syntax)))

;; A rope wrapped #:turns times round a fixed post #:radius (m, default
    ;; 0.15) across — a bollard, a snubbing post — a #:load kg hanging #:drop
    ;; m (default 1) below the post from one end, someone pulling on the
    ;; other with #:hold N (settable: (set id hold n)). Friction round the
    ;; post multiplies what a pull can hold by e^(μθ), θ = 2π × turns (the
    ;; capstan equation): the load stays for any pull between m·g·e^(−μθ)
    ;; and m·g·e^(μθ), runs out below that, comes in above it. μ is #:mu,
    ;; or by default the rope's (#:rope, hemp) and post's frictions
    ;; combined as √(μ₁μ₂). The post's radius doesn't enter into it.
    (pattern (capstan id:id
                      (~alt (~once (~seq #:at at:vec3))
                            (~once (~seq #:turns turns-v:expr))
                            (~once (~seq #:load load-v:expr))
                            (~optional (~seq #:hold hold-v:expr))
                            (~optional (~seq #:mu mu-v:expr))
                            (~optional (~seq #:drop drop-v:expr))
                            (~optional (~seq #:radius radius-v:expr))
                            (~optional (~seq #:rope rope-mat:id))
                            (~optional (~seq #:material mat:id))) ...)
      #:fail-unless (memq (syntax-e (or (attribute rope-mat) #'hemp)) known-materials)
                    (format "unknown rope material ~a" (syntax-e (or (attribute rope-mat) #'hemp)))
      #:attr info (pinfo #'id 'capstan (attribute mat) '())
      #:with expr #`(part 'id 'capstan '(~? mat oak) (list at.x at.y at.z)
                          (list (cons 'turns turns-v) (cons 'load load-v) (cons 'hold (~? hold-v 0))
                                (cons 'mu (~? mu-v #f)) (cons 'drop (~? drop-v 1)) (cons 'radius (~? radius-v 0.15))
                                (cons 'rope '(~? rope-mat hemp)))
                          '()
                          #,(loc-of this-syntax)))

    ;; A windmill: sails #:radius (m, hub to tip) across, #:mass kg of them
    ;; (slender arms from the hub, I = mass × radius² / 3), facing a #:wind
    ;; (m/s) and turning a millstone that resists with #:load N·m. Both are
    ;; settable: (set id wind v), (set id load v). The wind carries ½ρAv³
    ;; through the swept disc; the sails take the fraction Cp of it, most —
    ;; #:cp (default 0.3) — when their tips run #:tip-speed-ratio (default
    ;; 2.5) times the wind speed, less either side. #:cp can be no more than
    ;; the Betz limit, 16/27: no rotor takes more of the wind than that.
    (pattern (windmill id:id
                       (~alt (~once (~seq #:at at:vec3))
                             (~once (~seq #:radius radius-v:expr))
                             (~once (~seq #:mass mass-v:expr))
                             (~once (~seq #:wind wind-v:expr))
                             (~optional (~seq #:load load-v:expr))
                             (~optional (~seq #:cp cp-v:expr))
                             (~optional (~seq #:tip-speed-ratio tsr-v:expr))
                             (~optional (~seq #:material mat:id))) ...)
      #:attr info (pinfo #'id 'windmill (attribute mat) '())
      #:with expr #`(part 'id 'windmill '(~? mat oak) (list at.x at.y at.z)
                          (list (cons 'radius radius-v) (cons 'mass mass-v) (cons 'wind wind-v)
                                (cons 'load (~? load-v 0)) (cons 'cp (~? cp-v 0.3))
                                (cons 'tip-speed-ratio (~? tsr-v 2.5)))
                          '()
                          #,(loc-of this-syntax)))

    ;; A #:vessel (a tank) hanging on a rope wound round a spindle #:radius
    ;; across, against a #:counterweight kg wound the other way: Heron's
    ;; temple doors. Heavier than the counterweight with its water, the
    ;; vessel sinks and turns the spindle, up to #:turn-deg (the doors wide
    ;; open); lighter, the counterweight turns it back shut. The spindle
    ;; stands at #:at; #:leaf (w h) sizes the door leaf it swings.
    (pattern (counterpoise id:id
                           (~alt (~once (~seq #:at at:vec3))
                                 (~once (~seq #:vessel vessel-tank:id))
                                 (~once (~seq #:vessel-mass vmass-v:expr))
                                 (~once (~seq #:counterweight cw-v:expr))
                                 (~once (~seq #:radius radius-v:expr))
                                 (~once (~seq #:turn-deg turn-v:expr))
                                 (~optional (~seq #:friction fr-v:expr))
                                 (~optional (~seq #:leaf-inertia li-v:expr))
                                 (~optional (~seq #:leaf (leaf-w:expr leaf-h:expr)))
                                 (~optional (~seq #:material mat:id))) ...)
      #:attr info (cpinfo #'id #'vessel-tank)
      #:with expr #`(part 'id 'counterpoise '(~? mat oak) (list at.x at.y at.z)
                          (list (cons 'vessel 'vessel-tank) (cons 'vessel-mass vmass-v) (cons 'counterweight cw-v)
                                (cons 'radius radius-v) (cons 'turn-deg turn-v)
                                (cons 'friction (~? fr-v 0)) (cons 'leaf-inertia (~? li-v 0))
                                (cons 'leaf-width (~? leaf-w 1)) (cons 'leaf-height (~? leaf-h 2)))
                          '()
                          #,(loc-of this-syntax)))

    ;; A sluice gate across the head of channel #:on: a plate #:height tall
    ;; (m) sliding in grooves at the channel's lip, raised by #:opening (0
    ;; shut .. 1 drawn right up; default 1). Water runs out under it as an
    ;; orifice, Q = 0.6·w·a·√(2gh), a the slot the raised plate leaves, h the
    ;; water above the slot's middle; water over the plate's top spills as a
    ;; weir, so a shut gate is a dam. #:width defaults to the channel's.
    ;; #:at defaults to the lip where the channel leaves its tank. Set
    ;; (gate opening v) at run time to work it.
    (pattern (sluice id:id
                     (~alt (~once (~seq #:on on-ch:id))
                           (~once (~seq #:height height-v:expr))
                           (~optional (~seq #:opening open-v:expr))
                           (~optional (~seq #:width width-v:expr))
                           (~optional (~seq #:at at:vec3))
                           (~optional (~seq #:material mat:id))) ...)
      #:attr info (sinfo #'id #'on-ch)
      #:with expr #`(part 'id 'sluice '(~? mat oak) (~? (list at.x at.y at.z) #f)
                          (list (cons 'on 'on-ch) (cons 'height height-v) (cons 'opening (~? open-v 1))
                                (cons 'width (~? width-v #f)))
                          '()
                          #,(loc-of this-syntax)))

    ;; A float valve, as Ctesibius and Philo built one to hold a water
    ;; clock's head steady: a float riding in the tank that feed #:on fills
    ;; (an inflow, a pipe, or a channel running into a tank) lifts a conical
    ;; plug into the feed's mouth. The plug seats once the water stands
    ;; #:shut (m) above the tank's floor and is clear once the level has
    ;; fallen #:travel (m) below that, the gap opening in proportion in
    ;; between: the feed passes (shut - level) / travel of its flow, so the
    ;; level settles inside that band however hard the tank is drawn, as
    ;; long as the open feed can outrun the draw. #:at defaults to the
    ;; middle of the tank at the shut level. Set (valve shut cm) at run time
    ;; to reset the float.
    (pattern (float-valve id:id
                          (~alt (~once (~seq #:on feed:id))
                                (~once (~seq #:shut shut-v:expr))
                                (~once (~seq #:travel travel-v:expr))
                                (~optional (~seq #:at at:vec3))
                                (~optional (~seq #:material mat:id))) ...)
      #:attr info (fvinfo #'id #'feed (attribute mat))
      #:with expr #`(part 'id 'float-valve '(~? mat bronze) (~? (list at.x at.y at.z) #f)
                          (list (cons 'on 'feed) (cons 'shut shut-v) (cons 'travel travel-v))
                          '()
                          #,(loc-of this-syntax)))

    ;; A hole in the wall of tank #:on, #:height (m) above its floor,
    ;; #:area (m2) across: water leaves as an orifice jet, Q = Cd A
    ;; sqrt(2 g h), h the water above the hole, until the level falls to the
    ;; hole. #:coefficient is Cd (default 0.6). The jet falls to the ground
    ;; unless a tank #:into stands under it. #:evaporation (m3/s, e.g.
    ;; (L/s 0.0001)) is a seep off the surface, taken while any water is
    ;; left; a leak may have either or both. #:at defaults to the tank's +x
    ;; wall at that height. Set (hole area cm2) at run time to plug it (0).
    ;; A hole given a #:bore (m across) instead has a plug in it, lifted
    ;; #:lift (m, default 0: shut) off its seat: the water passes the curtain
    ;; π·bore·lift, up to the bore's own area once the plug is a quarter of the
    ;; bore clear. Set (hole lift mm) at run time, or let a #:follow work it.
    (pattern (leak id:id
                   (~alt (~once (~seq #:on tank-id:id))
                         (~once (~seq #:height height-v:expr))
                         (~optional (~seq #:area area-v:expr))
                         (~optional (~seq #:coefficient cd-v:expr))
                         (~optional (~seq #:into catch:id))
                         (~optional (~seq #:evaporation evap-v:expr))
                         (~optional (~seq #:bore bore-v:expr))
                         (~optional (~seq #:lift lift-v:expr))
                         (~optional (~seq #:at at:vec3))
                         (~optional (~seq #:material mat:id))) ...)
      #:attr info (lkinfo #'id #'tank-id (attribute catch) (attribute mat))
      #:with expr #`(part 'id 'leak '(~? mat oak) (~? (list at.x at.y at.z) #f)
                          (list (cons 'on 'tank-id) (cons 'height height-v) (cons 'area (~? area-v 0))
                                (cons 'coefficient (~? cd-v 0.6)) (cons 'into '(~? catch #f))
                                (cons 'evaporation (~? evap-v 0))
                                (cons 'bore (~? bore-v 0)) (cons 'lift (~? lift-v 0)))
                          '()
                          #,(loc-of this-syntax)))

    ;; Papin's safety valve (1679), in the lid of boiler #:on: a disc on a
    ;; seat of #:bore (m) across, held down by a weighted lever until the
    ;; steam reaches #:lift (gauge Pa, e.g. (kPa 100)). It opens in
    ;; proportion as the pressure climbs on to #:accumulation × lift over
    ;; (default 0.1, a tenth), and vents steam through its bore as a
    ;; compressible nozzle with discharge coefficient #:coefficient (default
    ;; 0.8), choked above about 85 kPa. A boiler given #:burst (gauge Pa)
    ;; bursts at that pressure; the valve must lift below it. Set (valve
    ;; lift kPa) at run time to tie it down. #:at defaults to the lid.
    (pattern (safety-valve id:id
                           (~alt (~once (~seq #:on boiler-id:id))
                                 (~once (~seq #:lift lift-v:expr))
                                 (~once (~seq #:bore bore-v:expr))
                                 (~optional (~seq #:coefficient cd-v:expr))
                                 (~optional (~seq #:accumulation acc-v:expr))
                                 (~optional (~seq #:at at:vec3))
                                 (~optional (~seq #:material mat:id))) ...)
      #:attr info (svinfo #'id #'boiler-id (attribute mat))
      #:with expr #`(part 'id 'safety-valve '(~? mat bronze) (~? (list at.x at.y at.z) #f)
                          (list (cons 'on 'boiler-id) (cons 'lift lift-v) (cons 'bore bore-v)
                                (cons 'coefficient (~? cd-v 0.8)) (cons 'accumulation (~? acc-v 0.1)))
                          '()
                          #,(loc-of this-syntax)))

    ;; A lift (suction) pump: a bucket with a flap valve in it, worked up
    ;; and down a barrel of #:bore (m) over #:stroke (m) by a crank at #:rpm
    ;; (strokes a minute, default 0: set (pump rpm n) to work it), drawing
    ;; water up a suction pipe from tank #:from and pouring it from a spout
    ;; at the top of the stroke into tank #:to. #:at is the barrel's foot,
    ;; the bucket's lowest point. Each stroke lifts bore area x stroke x
    ;; #:efficiency (default 0.8; the rest slips back past the bucket).
    ;; The atmosphere pushes the water up the pipe, and only so far: to
    ;; (P_atm - P_v) / (rho g) over the source's surface, 10.09 m for water
    ;; at #:temperature 20 C (default: the machine's #:ambient, or just
    ;; above freezing in a frost); higher the column breaks, and a
    ;; barrel whose foot stands that far above the water lifts nothing.
    ;; #:force (N) is the most the drive can pull the rod with (default: as
    ;; much as it takes); asked for more, the pump stalls.
    (pattern (pump id:id
                   (~alt (~once (~seq #:at at:vec3))
                         (~once (~seq #:from from-id:id))
                         (~once (~seq #:to to-id:id))
                         (~once (~seq #:bore bore-v:expr))
                         (~once (~seq #:stroke stroke-v:expr))
                         (~optional (~seq #:rpm rpm-v:expr))
                         (~optional (~seq #:efficiency eff-v:expr))
                         (~optional (~seq #:force force-v:expr))
                         (~optional (~seq #:temperature temp-v:expr))
                         (~optional (~seq #:material mat:id))) ...)
      #:attr info (puinfo #'id #'from-id #'to-id (attribute mat))
      #:with expr #`(part 'id 'pump '(~? mat oak) (list at.x at.y at.z)
                          (list (cons 'from 'from-id) (cons 'to 'to-id)
                                (cons 'bore bore-v) (cons 'stroke stroke-v) (cons 'rpm (~? rpm-v 0))
                                (cons 'efficiency (~? eff-v 0.8)) (cons 'force (~? force-v #f))
                                (cons 'temperature (~? temp-v #f)))
                          '()
                          #,(loc-of this-syntax)))

    ;; A rope (or chain) between two parts. It only pulls, never pushes: it
    ;; goes slack when its ends come closer than its length. Each end is a
    ;; point on a part, in that part's own frame — (arm 0.9 0 0) is 0.9 m
    ;; along the arm from its pivot — or a fixed point, (world x y z).
    ;;   #:over    fixed points it runs over, in order: the tops of pulleys
    ;;   #:wind-on a wheel (a drum) whose turning winds the rope on,
    ;;             shortening it by radius × angle; replaces #:from
    ;;   #:release-deg  lets go of the #:to end once the rope has swung to
    ;;             within this many degrees of pointing straight out along
    ;;             the #:from part (from its pivot through the rope's
    ;;             end) — how a trebuchet's sling slips off its release pin
    ;;   #:diameter, #:material  set its breaking strength: tensile
    ;;             strength × cross-section
    ;;   #:nocked #t  the #:to end sits in a notch rather than being tied —
    ;;             a bowstring on a bolt: the rope can drive it forward, and
    ;;             lets go the moment it would pull it back
    ;;   #:turns   the pulley wheel whose rim the rope runs over, at the
    ;;             first #:over point: turned so its rim moves with the rope
    ;;   #:bar     the #:over points are fixed bars of this material, not
    ;;             turning pulleys: the rope drags over them, and the tight
    ;;             side can carry up to e^(mu theta) times the slack side
    ;;             (the capstan equation), theta the angle it turns through
    ;;             over all of them. mu is the rope's and the bar's
    ;;             friction combined, sqrt(mu1 mu2), or #:mu to set it
    ;;             (a greased bar, say)
    (pattern (rope id:id
                   (~alt (~optional (~seq #:from from:rope-end))
                         (~optional (~seq #:wind-on drum:id))
                         (~once (~seq #:to to:rope-end))
                         (~once (~seq #:length len-v:expr))
                         (~optional (~seq #:over (over:vec3 ...)))
                         (~optional (~seq #:release-deg rel-v:expr))
                         (~optional (~seq #:diameter dia-v:expr))
                         (~optional (~seq #:material mat:id))
                         (~optional (~seq #:nocked nocked-v:expr))
                         (~optional (~seq #:turns sheave:id))
                         (~optional (~seq #:bar bar-mat:id))
                         (~optional (~seq #:mu mu-v:expr))) ...)
      #:fail-unless (or (attribute from) (attribute drum)) "a rope needs a #:from end or a #:wind-on drum"
      #:fail-when (and (attribute bar-mat) (attribute sheave) #'sheave) "a rope runs over turning pulleys (#:turns) or fixed bars (#:bar), not both"
      #:fail-when (and (attribute mu-v) (not (attribute bar-mat)) #'mu-v) "#:mu is the friction over fixed bars; give #:bar too"
      #:fail-when (and (attribute bar-mat) (not (memq (syntax-e (attribute bar-mat)) known-materials)) (attribute bar-mat))
                  (format "unknown bar material ~a" (and (attribute bar-mat) (syntax-e (attribute bar-mat))))
      #:fail-when (and (attribute from) (attribute drum) #'drum) "give a rope #:from or #:wind-on, not both (#:wind-on is its from end)"
      #:attr info (rinfo #'id (filter values (list (and (attribute from) #'from.part) #'to.part)) (attribute drum))
      #:with from-expr (if (attribute drum)
                           #'(list 'drum 0 0 0)
                           #'(list 'from.part from.x from.y from.z))
      #:with expr #`(rope-spec 'id from-expr (list 'to.part to.x to.y to.z) len-v
                               (~? (list (list over.x over.y over.z) ...) '())
                               '(~? drum #f) (~? rel-v #f) '(~? mat hemp) (~? dia-v 0.02)
                               (and (~? nocked-v #f) #t)
                               '(~? sheave #f)
                               '(~? bar-mat #f) (~? mu-v #f)
                               #,(loc-of this-syntax)))

    ;; Wheels fixed on one axle (an arbor): a treadwheel and the drum its
    ;; rope winds on, two gears keyed to one shaft. They turn as one piece,
    ;; so a load on one is felt by all. The first holds the axle's bearing
    ;; and any drive; the rest ride on it.
    (pattern (arbor w:id ...+)
      #:attr info (arinfo (syntax->list #'(w ...)))
      #:with expr #`(arbor-spec '(w ...) #,(loc-of this-syntax)))

    ;; Two gears whose teeth engage: turning one turns the other the
    ;; opposite way, at the inverse ratio of their tooth counts. The game
    ;; checks they're cut to one module and set at the right distance.
    ;; Water lifted by a turning machine — an Archimedes' screw or a noria —
    ;; from one tank to another. How much each turn carries comes from the
    ;; machine's own shape; how fast it turns, from whatever turns it.
    ;; Lifting water takes torque, so the machine feels the load.
    ;; #:current gives a noria a river to stand in: water flowing at that
    ;; speed (m/s) past its paddles, which is what turns it.
    (pattern (lift id:id
                   (~alt (~once (~seq #:by by:id))
                         (~once (~seq #:from from:id))
                         (~once (~seq #:to to:id))
                         (~optional (~seq #:current cur-v:expr))
                         (~optional (~seq #:current-from race:id))) ...)
      #:attr info (linfo2 #'id #'by #'from #'to)
      #:with expr #`(lift-spec 'id 'by 'from 'to (~? cur-v #f) '(~? race #f) #,(loc-of this-syntax)))

    ;; Water arriving from outside the scene — a spring, a river from
    ;; upstream — into a tank at a steady #:flow (m³/s; (L/s 500) reads
    ;; better). The world beyond the machine isn't modelled; this is what it
    ;; provides.
    (pattern (inflow id:id
                     (~alt (~once (~seq #:into into:id))
                           (~once (~seq #:flow flow-v:expr))) ...)
      #:attr info (iinfo #'id #'into)
      #:with expr #`(inflow-spec 'id 'into flow-v #,(loc-of this-syntax)))

    ;; An open channel — a millrace, a conduit, a tailrace — from a tank's
    ;; port, over whose lip the water spills, down to another tank's port or
    ;; #:to off, out of the scene at #:end (x y z). How much flows is the
    ;; weir over the lip; how deep and fast it runs, the slope and width.
    ;; #:via ((x z) ...) bends it through waypoints, so a stream can follow
    ;; any course; #:length defaults to the path between the tanks' walls.
    ;; A channel run #:to off can pour #:onto a hearth (drowning it) or a
    ;; boiler (feeding it cold water) standing at its #:end.
    (pattern (channel id:id
                      (~alt (~once (~seq #:from from:ref))
                            (~once (~seq #:to (~or* (~and off-kw off) to:ref)))
                            (~optional (~seq #:end end:vec3))
                            (~optional (~seq #:via (via:xz ...)))
                            (~once (~seq #:width width-v:expr))
                            (~optional (~seq #:length len-v:expr))
                            (~optional (~seq #:onto onto:id))
                            (~optional (~seq #:dynamic dyn:expr))
                            (~optional (~seq #:cells cells-v:expr))) ...)
      #:fail-when (and (attribute off-kw) (not (attribute end)) #'id) "a channel running off the scene needs an #:end (x y z)"
      #:fail-when (and (attribute cells-v)
                       (let ([n (syntax-e #'cells-v)]) (and (number? n) (not (and (exact-integer? n) (<= 2 n 2000)))))
                       #'cells-v)
                  "a dynamic channel's #:cells is a whole number from 2 to 2000"
      #:fail-when (and (attribute onto) (not (attribute off-kw)) #'onto) "only a channel running #:to off can pour #:onto a hearth, boiler or water wheel"
      #:attr info (chinfo #'id #'from (and (attribute to) #'to) (attribute onto))
      #:with expr #`(channel-spec 'id (list 'from.part-id 'from.port-id)
                                  (~? (list 'to.part-id 'to.port-id) 'off)
                                  (~? (list end.x end.y end.z) #f)
                                  (~? (list (list via.x via.z) ...) '())
                                  width-v (~? len-v #f) #,(loc-of this-syntax) (~? 'onto #f)
                                  ;; a dynamic reach (issue #36): #:dynamic #t, or giving #:cells, makes one
                                  (and (or (~? dyn #f) (~? cells-v #f)) #t)
                                  (let ([n (~? cells-v #f)])
                                    (when (and n (not (and (exact-integer? n) (<= 2 n 2000))))
                                      (raise-user-error 'channel "~a: #:cells must be a whole number from 2 to 2000, got ~e" 'id n))
                                    n)))

    ;; A sensor that acts when something arrives, once. #:body part with a box
    ;; (#:at its centre, #:size its extents) fires when that part's centre
    ;; enters it (a falling weight tripping a catch); #:when (target field above|below value)
    ;; fires when a field crosses a value (a float rising to a level). #:do
    ;; lists what it sets, ((target field value) ...), the same targets and
    ;; fields the console's (set) and a machine's own fields use.
    (pattern (trigger id:id
                      (~alt (~optional (~seq #:at at:vec3))
                            (~optional (~seq #:size size:vec3))
                            (~optional (~seq #:body body:id))
                            (~optional (~seq #:when (wt:id wf:id mode:id wv:expr)))
                            (~once (~seq #:do ((dt:id df:id dv:expr) ...)))) ...)
      #:fail-when (and (attribute body) (attribute wt) #'id) "a trigger watches either a body (#:body) or a field (#:when), not both"
      #:fail-unless (or (attribute body) (attribute wt)) "a trigger needs something to watch: #:body part (with #:at and #:size), or #:when (target field above|below value)"
      #:fail-when (and (attribute body) (not (and (attribute at) (attribute size))) #'id) "a trigger watching a body needs its box: #:at (x y z) and #:size (w h d)"
      #:fail-when (and (attribute wt) (not (memq (syntax-e #'mode) '(above below))) #'mode) "the mode is above or below"
      #:fail-when (and (null? (syntax->list #'(dt ...))) #'id) "a trigger does nothing: give it #:do ((target field value) ...)"
      #:attr info (trinfo #'id (and (attribute body) #'body))
      #:with expr #`(trigger-spec 'id (~? (list at.x at.y at.z) #f) (~? (list size.x size.y size.z) #f)
                                  (~? 'body #f) (~? (list 'wt 'wf 'mode wv) #f)
                                  (list (list 'dt 'df dv) ...) #,(loc-of this-syntax)))

    ;; A field that follows a mechanism, continuously (a plug lifted part-way
    ;; lets part of the flow through; #:trigger acts once, this tracks).
    ;; #:lever names a lever, whose angle from its start in degrees is the
    ;; input; #:rope names a rope, whose tension in N is. As the input runs
    ;; #:from -> #:to, the field (target field) named by #:set runs #:low -> #:high
    ;; (default 0 -> 1), held at the ends.
    (pattern (follow id:id
                     (~alt (~optional (~seq #:lever lever-id:id))
                           (~optional (~seq #:rope rope-id:id))
                           (~once (~seq #:from from-v:expr))
                           (~once (~seq #:to to-v:expr))
                           (~once (~seq #:set (st:id sf:id)))
                           (~optional (~seq #:low low-v:expr))
                           (~optional (~seq #:high high-v:expr))) ...)
      #:fail-when (and (attribute lever-id) (attribute rope-id) #'id) "a follow follows either a lever (#:lever) or a rope (#:rope), not both"
      #:fail-unless (or (attribute lever-id) (attribute rope-id)) "a follow needs a mechanism: #:lever part or #:rope rope"
      #:attr info (fwinfo #'id (and (attribute lever-id) #'lever-id) (and (attribute rope-id) #'rope-id))
      #:with expr #`(follow-spec 'id '(~? lever-id #f) '(~? rope-id #f) from-v to-v 'st 'sf (~? low-v 0) (~? high-v 1)
                                 #,(loc-of this-syntax)))

    ;; An open belt between two drums, a and b (wheels, pulleys or drums on
    ;; parallel axles), pretensioned to #:tension N. While it grips, their
    ;; rims run at the same speed, so b turns at r_a/r_b of a; it carries at
    ;; most 2 T0 tanh(mu theta / 2) before it slips, theta the wrap on the
    ;; smaller drum and mu the friction of #:material (default hemp). Tighten
    ;; it and it carries more: set (belt tension N) at run time.
    (pattern (belt id:id a:id b:id
                   (~alt (~once (~seq #:tension tension-v:expr))
                         (~optional (~seq #:material mat:id))) ...)
      #:attr info (beinfo #'id #'a #'b)
      #:with expr #`(belt-spec 'id 'a 'b tension-v '(~? mat hemp) #,(loc-of this-syntax)))

    ;; A joint between two moving parts, or a part and the world, at a point
    ;; #:at (x y z) in the world. #:kind
    ;;   pin        a hinge: they turn about #:axis (x y z) through the point
    ;;              and nothing else -- a crank pin, a door on its post
    ;;   ball       they turn every way about the point; #:limit-deg caps how
    ;;              far one swings from where it started (a cone)
    ;;   universal  a Cardan (Hooke's) joint between two shafts, each on its
    ;;              own axle through the point: a cross whose arms are hinged
    ;;              one to each shaft's yoke. At an angle, the driven shaft
    ;;              speeds up and slows down twice a turn.
    ;;   6dof       everything locked but the motions named in #:free, among
    ;;              x y z (sliding along the world's axes) and rx ry rz (turning)
    (pattern (joint id:id
                    (~alt (~once (~seq #:kind kind:id))
                          (~once (~seq #:a a:id))
                          (~once (~seq #:b b:id))
                          (~once (~seq #:at at:vec3))
                          (~optional (~seq #:axis axis:vec3))
                          (~optional (~seq #:free (free:id ...)))
                          (~optional (~seq #:limit-deg limit-v:expr))) ...)
      #:fail-unless (memq (syntax-e #'kind) '(pin ball universal 6dof)) "a joint's #:kind is pin, ball, universal or 6dof"
      #:fail-when (and (eq? (syntax-e #'kind) 'pin) (not (attribute axis)) #'id) "a pin joint needs the #:axis (x y z) it turns about"
      #:fail-when (and (attribute free) (not (eq? (syntax-e #'kind) '6dof)) #'id) "only a 6dof joint takes #:free"
      #:fail-when (for/first ([f (or (attribute free) '())] #:unless (memq (syntax-e f) '(x y z rx ry rz))) f)
                  "a 6dof joint frees x y z (sliding) or rx ry rz (turning)"
      #:fail-when (and (attribute limit-v) (not (eq? (syntax-e #'kind) 'ball)) #'id) "only a ball joint takes #:limit-deg"
      #:fail-when (and (eq? (syntax-e #'a) (syntax-e #'b)) #'b) "a joint joins two different parts"
      #:fail-when (and (eq? (syntax-e #'kind) 'universal) (or (eq? (syntax-e #'a) 'world) (eq? (syntax-e #'b) 'world)) #'id)
                  "a universal joint joins two shafts, not a shaft and the world"
      #:attr info (jinfo #'id (syntax-e #'kind) #'a #'b)
      #:with expr #`(joint-spec 'id 'kind 'a 'b (list at.x at.y at.z) (~? (list axis.x axis.y axis.z) #f)
                                '(~? (free ...) ()) (~? limit-v #f) #,(loc-of this-syntax)))

    (pattern (mesh a:id b:id)
      #:attr info (minfo #'a #'b)
      #:with expr #`(mesh-spec 'a 'b #,(loc-of this-syntax)))

    ;; A piston sliding up and down in a cylinder of #:bore, over #:stroke.
    ;; #:at is the bottom of its travel; #:start is where along it it
    ;; starts (0 bottom, 1 top). A pump's piston hangs on a long, heavy rod
    ;; down the shaft: #:rod-mass adds that weight.
    (pattern (piston id:id
                     (~alt (~once (~seq #:at at:vec3))
                           (~once (~seq #:bore bore-v:expr))
                           (~once (~seq #:stroke stroke-v:expr))
                           (~once (~seq #:material mat:id))
                           (~optional (~seq #:start start-v:expr))
                           (~optional (~seq #:rod-mass rod-v:expr))) ...)
      #:attr info (pinfo #'id 'piston (attribute mat) '())
      #:with expr #`(part 'id 'piston 'mat (list at.x at.y at.z)
                          (list (cons 'bore bore-v) (cons 'stroke stroke-v)
                                (cons 'start (~? start-v 0)) (cons 'rod-mass (~? rod-v 0)))
                          '()
                          #,(loc-of this-syntax)))

    ;; Newcomen's atmospheric engine cylinder (1712). Steam from the boiler
    ;; fills it below the piston at about atmospheric pressure; at the top
    ;; of the stroke a jet of cold water condenses the steam, leaving a
    ;; partial vacuum, and the atmosphere pushes the piston down — that is
    ;; the working stroke. Tappets on a rod hanging from the beam switch the
    ;; valves at each end of the stroke. #:injection-temperature is how warm
    ;; the injection water gets (°C), which sets how good the vacuum is; by
    ;; default 40 K over the #:ambient it is drawn at (60 °C on a 20 °C day).
    (pattern (atmospheric-cylinder id:id
                                   (~alt (~once (~seq #:piston p:id))
                                         (~once (~seq #:steam-from b:id))
                                         (~optional (~seq #:injection-temperature inj-v:expr))) ...)
      #:attr info (cinfo #'id #'p #'b)
      #:with expr #`(cylinder-spec 'id 'p 'b (~? inj-v #f) #,(loc-of this-syntax)))

    (pattern (pipe id:id from:ref to:ref
                   (~alt (~once (~seq #:conductance c:expr))
                         (~optional (~seq #:jet jet:expr))) ...)
      #:attr info (linfo 'pipe #'id #'from #'to)
      #:with expr #`(pipe-spec 'id (list 'from.part-id 'from.port-id) (list 'to.part-id 'to.port-id)
                               c (and (~? jet #f) #t)
                               #,(loc-of this-syntax)))

    (pattern (connect a:ref b:ref)
      #:attr info (linfo 'connect #f #'a #'b)
      #:with expr #`(connect-spec (list 'a.part-id 'a.port-id) (list 'b.part-id 'b.port-id)
                                  #,(loc-of this-syntax)))

    ;; #:heat-loss (W/K through the walls) and #:heat-capacity (J/K of the
    ;; vessels themselves) matter once a hearth #:heats one of its tanks:
    ;; the air's pressure follows its temperature, P = m R T / V.
    (pattern (sealed-air (t:id ...+) (~alt (~optional (~seq #:tube tube:expr))
                                           (~optional (~seq #:heat-loss loss-v:expr))
                                           (~optional (~seq #:heat-capacity cap-v:expr))) ...)
      #:attr info (ainfo (syntax->list #'(t ...)))
      #:with expr #`(air-spec '(t ...) (~? tube 0) #,(loc-of this-syntax) (~? loss-v 0) (~? cap-v 0))))

  ;; Checks the whole machine. Each error names the offending clause so
  ;; DrRacket and `racket` both point at the exact source location.
  (define (check-machine! whole infos)
    (define (fail msg at) (raise-syntax-error 'define-machine msg whole at))
    (define parts (make-hasheq))

    (for ([i infos] #:when (pinfo? i))
      (define sym (syntax-e (pinfo-id i)))
      (when (hash-ref parts sym #f)
        (fail (format "there is already a part named ~a" sym) (pinfo-id i)))
      (hash-set! parts sym i)
      (define mat (pinfo-mat i))
      (when (and mat (not (memq (syntax-e mat) known-materials)))
        (fail (format "unknown material ~a; known materials are: ~a"
                      (syntax-e mat) (string-join (map symbol->string known-materials) ", "))
              mat)))

    ;; → the port's kind, or a syntax error explaining what is wrong
    (define (resolve ref)
      (define pieces (string-split (symbol->string (syntax-e ref)) "."))
      (define part-sym (string->symbol (first pieces)))
      (define port-sym (string->symbol (second pieces)))
      (define p (hash-ref parts part-sym #f))
      (unless p
        (fail (format "no part named ~a" part-sym) ref))
      (define port (assq port-sym (pinfo-ports p)))
      (unless port
        (fail (format "~a is a ~a with no port named ~a; its ports are: ~a"
                      part-sym (pinfo-kind p) port-sym
                      (if (null? (pinfo-ports p))
                          "(none)"
                          (string-join (map (λ (pt) (symbol->string (car pt))) (pinfo-ports p)) ", ")))
              ref))
      (values p (cdr port)))

    (define steam-feeds (make-hasheq)) ; rotor symbol → boiler symbol
    (define boiler-loads (make-hasheq)) ; boiler symbol → rotor count

    (for ([l infos] #:when (linfo? l))
      (define-values (pa ka) (resolve (linfo-from l)))
      (define-values (pb kb) (resolve (linfo-to l)))
      (case (linfo-type l)
        [(pipe)
         (define id (syntax-e (linfo-id l)))
         (when (hash-ref parts id #f)
           (fail (format "pipe ~a has the same name as a part" id) (linfo-id l)))
         (for ([k (list ka kb)] [r (list (linfo-from l) (linfo-to l))])
           (unless (eq? k 'water)
             (fail (format "a pipe carries water, but ~a is a ~a port" (syntax-e r) k) r)))]
        [(connect)
         (unless (eq? ka kb)
           (fail (format "cannot join ~a (~a port) to ~a (~a port)"
                         (syntax-e (linfo-from l)) ka (syntax-e (linfo-to l)) kb)
                 (linfo-to l)))
         (when (eq? ka 'steam)
           (define-values (b r)
             (cond [(and (eq? (pinfo-kind pa) 'boiler) (memq (pinfo-kind pb) '(rotor jetwheel))) (values pa pb)]
                   [(and (eq? (pinfo-kind pb) 'boiler) (memq (pinfo-kind pa) '(rotor jetwheel))) (values pb pa)]
                   [else (fail "a steam connection must join a boiler to a rotor or a jetwheel" (linfo-to l))]))
           (define b-sym (syntax-e (pinfo-id b)))
           (hash-set! steam-feeds (syntax-e (pinfo-id r)) b-sym)
           (hash-update! boiler-loads b-sym add1 0)
           (when (> (hash-ref boiler-loads b-sym) 1)
             (fail (format "boiler ~a already feeds a rotor or jetwheel; one per boiler for now" b-sym)
                   (linfo-to l))))]))

    (for ([(sym p) parts] #:when (memq (pinfo-kind p) '(rotor jetwheel)))
      (unless (hash-ref steam-feeds sym #f)
        (fail (format "~a ~a has no steam supply; add (connect <boiler>.steam ~a.steam-in)" (pinfo-kind p) sym sym)
              (pinfo-id p))))

    (for ([r infos] #:when (rinfo? r))
      (define rid (syntax-e (rinfo-id r)))
      (when (hash-ref parts rid #f)
        (fail (format "rope ~a has the same name as a part" rid) (rinfo-id r)))
      (for ([end (rinfo-ends r)] #:unless (eq? (syntax-e end) 'world))
        (unless (hash-ref parts (syntax-e end) #f)
          (fail (format "no part named ~a (rope ends are parts, or world for a fixed point)" (syntax-e end)) end)))
      (define drum (rinfo-drum r))
      (when drum
        (define p (hash-ref parts (syntax-e drum) #f))
        (unless (and p (eq? (pinfo-kind p) 'wheel))
          (fail (format "#:wind-on needs a wheel to wind onto; ~a is ~a" (syntax-e drum)
                        (if p (format "a ~a" (pinfo-kind p)) "not a part"))
                drum))))

    (define on-arbor (make-hasheq))
    (for ([a infos] #:when (arinfo? a))
      (when (< (length (arinfo-parts a)) 2)
        (fail "an arbor joins two or more wheels" (car (arinfo-parts a))))
      (for ([w (arinfo-parts a)])
        (define p (hash-ref parts (syntax-e w) #f))
        (unless (and p (eq? (pinfo-kind p) 'wheel))
          (fail (format "~a is not a wheel; an arbor fixes wheels together on one axle" (syntax-e w)) w))
        (when (hash-ref on-arbor (syntax-e w) #f)
          (fail (format "wheel ~a is already on another arbor" (syntax-e w)) w))
        (hash-set! on-arbor (syntax-e w) #t)))

    (for ([mi infos] #:when (minfo? mi))
      (for ([g (list (minfo-a mi) (minfo-b mi))])
        (define p (hash-ref parts (syntax-e g) #f))
        (unless (and p (eq? (pinfo-kind p) 'wheel))
          (fail (format "~a is not a wheel; mesh joins two gears" (syntax-e g)) g))))

    (for ([l infos] #:when (linfo2? l))
      (define by (hash-ref parts (syntax-e (linfo2-by l)) #f))
      (unless (and by (memq (pinfo-kind by) '(screw wheel piston)))
        (fail (format "~a can't lift water: a lift is done #:by a screw, a noria wheel or a pump's piston" (syntax-e (linfo2-by l)))
              (linfo2-by l)))
      (for ([t (list (linfo2-from l) (linfo2-to l))])
        (define p (hash-ref parts (syntax-e t) #f))
        (unless (and p (eq? (pinfo-kind p) 'tank))
          (fail (format "~a is not a tank; a lift carries water between tanks" (syntax-e t)) t))))

    (for ([c infos] #:when (cinfo? c))
      (define p (hash-ref parts (syntax-e (cinfo-piston c)) #f))
      (unless (and p (eq? (pinfo-kind p) 'piston))
        (fail (format "~a is not a piston" (syntax-e (cinfo-piston c))) (cinfo-piston c)))
      (define b (hash-ref parts (syntax-e (cinfo-boiler c)) #f))
      (unless (and b (eq? (pinfo-kind b) 'boiler))
        (fail (format "~a is not a boiler; the cylinder needs one for steam" (syntax-e (cinfo-boiler c))) (cinfo-boiler c))))

    (for ([t infos] #:when (and (trinfo? t) (trinfo-body t)))
      (unless (hash-ref parts (syntax-e (trinfo-body t)) #f)
        (fail (format "~a is not a part; a trigger watches a part's centre" (syntax-e (trinfo-body t))) (trinfo-body t))))

    (for ([g infos] #:when (and (grinfo? g) (grinfo-on g)))
      (define p (hash-ref parts (syntax-e (grinfo-on g)) #f))
      (unless (and p (memq (pinfo-kind p) '(block lever wheel screw fixture post piston pendulum ramp)))
        (fail (format "~a is not a body a grip can hang from (a block, lever, wheel, post ...); leave #:on out to hang it on the world" (syntax-e (grinfo-on g))) (grinfo-on g))))

    (for ([bt infos] #:when (beinfo? bt))
      (for ([d (list (beinfo-a bt) (beinfo-b bt))])
        (define p (hash-ref parts (syntax-e d) #f))
        (unless (and p (eq? (pinfo-kind p) 'wheel))
          (fail (format "~a is not a wheel, pulley or drum; a belt runs on two of them" (syntax-e d)) d))))

    (for* ([j infos] #:when (jinfo? j) [end (list (jinfo-a j) (jinfo-b j))] #:unless (eq? (syntax-e end) 'world))
      (define p (hash-ref parts (syntax-e end) #f))
      (unless p (fail (format "~a is not a part" (syntax-e end)) end))
      (unless (memq (pinfo-kind p) '(block pendulum lever wheel screw piston))
        (fail (format "~a is a ~a, which doesn't move; a joint joins blocks, pendulums, levers, wheels, screws or pistons (or world)"
                      (syntax-e end) (pinfo-kind p)) end))
      (when (and (eq? (jinfo-kind j) 'universal) (not (memq (pinfo-kind p) '(wheel screw))))
        (fail (format "~a is not a shaft; a universal joint joins two wheels or screws, each on its own axle" (syntax-e end)) end)))

    (for ([f infos] #:when (and (fwinfo? f) (fwinfo-lever f)))
      (define p (hash-ref parts (syntax-e (fwinfo-lever f)) #f))
      (unless (and p (eq? (pinfo-kind p) 'lever))
        (fail (format "~a is not a lever" (syntax-e (fwinfo-lever f))) (fwinfo-lever f))))

    (for ([i infos] #:when (iinfo? i))
      (define p (hash-ref parts (syntax-e (iinfo-into i)) #f))
      (unless (and p (eq? (pinfo-kind p) 'tank))
        (fail (format "~a is not a tank; an inflow runs into a tank" (syntax-e (iinfo-into i))) (iinfo-into i))))
    (for ([c infos] #:when (chinfo? c))
      (for ([r (filter values (list (chinfo-from c) (chinfo-to c)))])
        (define-values (p kind) (resolve r))
        (unless (eq? (pinfo-kind p) 'tank)
          (fail (format "a channel runs between tanks' ports; ~a is a ~a" (syntax-e r) (pinfo-kind p)) r)))
      (when (chinfo-onto c)
        (define onto (syntax-e (chinfo-onto c)))
        (define p (hash-ref parts onto #f))
        (unless (or (and p (eq? (pinfo-kind p) 'boiler))
                    (for/or ([h infos]) (and (hinfo? h) (eq? (syntax-e (hinfo-id h)) onto)))
                    (for/or ([w infos]) (and (winfo? w) (eq? (syntax-e (winfo-id w)) onto))))
          (fail (format "~a is not a hearth, boiler or water wheel; a channel pours #:onto one of those" (syntax-e (chinfo-onto c))) (chinfo-onto c)))))
    (for ([w infos] #:when (winfo? w))
      (when (winfo-race w)
        (define r (syntax-e (winfo-race w)))
        (unless (for/or ([c infos]) (and (chinfo? c) (eq? (syntax-e (chinfo-id c)) r)))
          (fail (format "~a is not a channel; an undershot wheel stands in a channel" r) (winfo-race w))))
      (when (winfo-tail w)
        (define p (hash-ref parts (syntax-e (winfo-tail w)) #f))
        (unless (and p (eq? (pinfo-kind p) 'tank))
          (fail (format "~a is not a tank; a wheel's buckets tip into a tank" (syntax-e (winfo-tail w))) (winfo-tail w)))))

    (define gated (make-hasheq))
    (for ([g infos] #:when (sinfo? g))
      (define on (syntax-e (sinfo-on g)))
      (unless (for/or ([c infos]) (and (chinfo? c) (eq? (syntax-e (chinfo-id c)) on)))
        (fail (format "~a is not a channel; a sluice stands across a channel" on) (sinfo-on g)))
      (when (hash-ref gated on #f)
        (fail (format "channel ~a already has a gate" on) (sinfo-on g)))
      (hash-set! gated on #t))

    (define valved (make-hasheq))
    (for ([v infos] #:when (fvinfo? v))
      (define feed (syntax-e (fvinfo-feed v)))
      (define (named? pred id-of) (for/or ([i infos]) (and (pred i) (id-of i) (eq? (syntax-e (id-of i)) feed) i)))
      (define ch (named? chinfo? chinfo-id))
      (unless (or (named? iinfo? iinfo-id)
                  (named? (λ (l) (and (linfo? l) (eq? (linfo-type l) 'pipe))) linfo-id)
                  ch)
        (fail (format "~a is not an inflow, pipe or channel; a float valve throttles the feed into a tank" feed) (fvinfo-feed v)))
      (when (and ch (not (chinfo-to ch)))
        (fail (format "channel ~a runs off the scene; a float valve rides in the tank its feed fills" feed) (fvinfo-feed v)))
      (when (hash-ref valved feed #f)
        (fail (format "~a already has a float valve" feed) (fvinfo-feed v)))
      (hash-set! valved feed #t)
      (define mat (fvinfo-mat v))
      (when (and mat (not (memq (syntax-e mat) known-materials)))
        (fail (format "unknown material ~a" (syntax-e mat)) mat)))

    (for ([l infos] #:when (lkinfo? l))
      (define (tank-named? id) (let ([p (hash-ref parts (syntax-e id) #f)]) (and p (eq? (pinfo-kind p) 'tank))))
      (unless (tank-named? (lkinfo-on l))
        (fail (format "~a is not a tank; a leak is a hole in a tank's wall" (syntax-e (lkinfo-on l))) (lkinfo-on l)))
      (define into (lkinfo-into l))
      (when into
        (unless (tank-named? into)
          (fail (format "~a is not a tank; a leak can only run #:into a tank" (syntax-e into)) into))
        (when (eq? (syntax-e into) (syntax-e (lkinfo-on l)))
          (fail "a leak cannot run into its own tank" into)))
      (define mat (lkinfo-mat l))
      (when (and mat (not (memq (syntax-e mat) known-materials)))
        (fail (format "unknown material ~a" (syntax-e mat)) mat)))

    (for ([v infos] #:when (svinfo? v))
      (define b (hash-ref parts (syntax-e (svinfo-on v)) #f))
      (unless (and b (eq? (pinfo-kind b) 'boiler))
        (fail (format "~a is not a boiler; a safety valve sits in a boiler's lid" (syntax-e (svinfo-on v))) (svinfo-on v)))
      (define mat (svinfo-mat v))
      (when (and mat (not (memq (syntax-e mat) known-materials)))
        (fail (format "unknown material ~a" (syntax-e mat)) mat)))

    (define bellowed (make-hasheq))
    (for ([v infos] #:when (bvinfo? v))
      (define on (syntax-e (bvinfo-on v)))
      (unless (for/or ([h infos]) (and (hinfo? h) (eq? (syntax-e (hinfo-id h)) on)))
        (fail (format "~a is not a hearth; a bellows forces draught into a hearth" on) (bvinfo-on v)))
      (when (hash-ref bellowed on #f)
        (fail (format "~a already has a bellows" on) (bvinfo-on v)))
      (hash-set! bellowed on #t)
      (define mat (bvinfo-mat v))
      (when (and mat (not (memq (syntax-e mat) known-materials)))
        (fail (format "unknown material ~a" (syntax-e mat)) mat)))

    (for ([u infos] #:when (puinfo? u))
      (for ([t (list (puinfo-from u) (puinfo-to u))])
        (define p (hash-ref parts (syntax-e t) #f))
        (unless (and p (eq? (pinfo-kind p) 'tank))
          (fail (format "~a is not a tank; a pump draws from a tank and pours into another" (syntax-e t)) t)))
      (when (eq? (syntax-e (puinfo-from u)) (syntax-e (puinfo-to u)))
        (fail "a pump cannot pour into the tank it draws from" (puinfo-to u)))
      (define mat (puinfo-mat u))
      (when (and mat (not (memq (syntax-e mat) known-materials)))
        (fail (format "unknown material ~a" (syntax-e mat)) mat)))

    (for ([h infos] #:when (hinfo? h))
      (define heats (syntax-e (hinfo-heats h)))
      (define b (hash-ref parts heats #f))
      (unless (or (and b (memq (pinfo-kind b) '(boiler enclosure)))
                  (for/or ([a infos]) (and (ainfo? a) (memq heats (map syntax-e (ainfo-tanks a))))))
        (fail (format "~a is not a boiler, a tank in a sealed-air or an enclosure; a hearth heats one of those" heats) (hinfo-heats h))))
    (for ([m infos] #:when (mrinfo? m))
      (define onto (syntax-e (mrinfo-onto m)))
      (define b (hash-ref parts onto #f))
      (unless (or (and b (memq (pinfo-kind b) '(boiler enclosure)))
                  (for/or ([a infos]) (and (ainfo? a) (memq onto (map syntax-e (ainfo-tanks a))))))
        (fail (format "~a is not a boiler, a tank in a sealed-air or an enclosure; a mirror heats one of those" onto) (mrinfo-onto m))))
    (for ([c infos] #:when (cpinfo? c))
      (define v (hash-ref parts (syntax-e (cpinfo-vessel c)) #f))
      (unless (and v (eq? (pinfo-kind v) 'tank))
        (fail (format "~a is not a tank; a counterpoise hangs a tank" (syntax-e (cpinfo-vessel c))) (cpinfo-vessel c))))

    (define sealed (make-hasheq))
    (for ([a infos] #:when (ainfo? a))
      (for ([t (ainfo-tanks a)])
        (define p (hash-ref parts (syntax-e t) #f))
        (unless (and p (eq? (pinfo-kind p) 'tank))
          (fail (format "~a is not a tank in this machine" (syntax-e t)) t))
        (when (hash-ref sealed (syntax-e t) #f)
          (fail (format "tank ~a is already in another sealed-air group" (syntax-e t)) t))
        (hash-set! sealed (syntax-e t) #t)))))

;; ---------------------------------------------------------------------------

(define-syntax (define-machine stx)
  (syntax-parse stx
    [(_ name:id (~alt (~optional (~seq #:source src:expr)) (~optional (~seq #:ambient amb:expr))
                      (~optional (~seq #:planet pl:expr))
                      (~optional (~seq #:latitude lat:expr)) (~optional (~seq #:day day:expr)) (~optional (~seq #:time time:expr))) ...
        c:clause ...)
     ;; a bare name is a preset, checked here; anything else is an
     ;; expression making a planet, such as (planet mars #:gravity 9.81)
     #:with planet-expr (cond
                          [(not (attribute pl)) #'(planet-preset 'earth)]
                          [(identifier? #'pl)
                           (unless (memq (syntax-e #'pl) (planet-ids))
                             (raise-syntax-error 'define-machine
                                                 (format "unknown planet ~a; the planets are: ~a" (syntax-e #'pl)
                                                         (string-join (map symbol->string (planet-ids)) ", "))
                                                 stx #'pl))
                           #'(planet-preset 'pl)]
                          [else #'pl])
     #:with sun (if (or (attribute lat) (attribute day) (attribute time))
                    #'(list (~? lat 31.2) (~? day 172) (~? time 12))
                    #'#f)
     (check-machine! stx (attribute c.info))
     #'(begin
         (define the-planet planet-expr)
         ;; a scene on a planet stands in its air unless it says otherwise
         (define name (make-machine 'name (~? src #f) (~? amb (if (planet? the-planet) (planet-field the-planet 'temperature) 20))
                                    sun the-planet (list c.expr ...)))
         (register-machine! name))]))
