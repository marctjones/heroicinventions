#lang racket/base
;; Maps: the ground a world stands on (issue #37). A map is its own file,
;; not a clause in a machine: one ground under every machine of a world,
;; loadable on any planet by any scenario. Racket describes and checks it;
;; racket/build.rkt writes it to game/maps/NAME.map (emit.rkt's
;; write-map-file); the C# Terrain reads it and ShallowWater2D runs water
;; over it; the game draws it and gives Jolt its heights.
;;
;;   (define-map flood-plain
;;     #:cell 1 #:size (60 40) #:origin (-10 -20)
;;     #:heights (slope #:gradient '(-0.02 0) #:base 1)  ; or (λ (x z) ...), or rows of numbers
;;     #:soil loam                                       ; or (λ (x z) 'clay)
;;     #:infiltration ((loam 1e-6) (sand 2e-5))          ; m/s each soil soaks away; 0 if not given
;;     #:cohesion ((clay 10000))                         ; Pa each intact soil holds together by (#44); 0 if not given
;;     #:grain ((sand 0.0005) (gravel 0.01 2650))        ; m its grains are across [and kg/m³ they weigh, 2650 if not
;;                                                       ; given]: flowing water can carry them off (#53); 0: it can't
;;     #:edges open #:roughness 0.03
;;     #:settle #t                                       ; let what can't stand collapse on the world's first tick (#54);
;;                                                       ; a number, passes a second, lets it play out on screen (#61)
;;     #:wind (corridor-wind #:through '(0 0) #:notch-deg 200 #:speed 6 #:width 80 ...)   ; the wind over the floor (#61)
;;     #:boulders ((regolith 0.3 0.6 granite))           ; when a face of this soil fails, 0.3 of what comes down comes
;;                                                       ; down as 0.6 m cubes of granite (#88): floor(0.3 V / 0.6³) of them
;;     (source spring #:at (2 0) #:flow 0.05))
;;
;; Heights are sampled at each cell's centre, (x0 + (i + 1/2) cell, z0 + (j + 1/2) cell).
;; Generators give a procedure (x z) -> height: slope, bowl, crater, and
;; ground+ to add them. Checked: soils are materials in materials.rktd; the
;; cell is at least 0.5 m and a map at most 40,000 cells (the budget #37 sets);
;; rows of heights fill the grid exactly; springs stand on the map; every
;; height is a finite number.
(require (for-syntax racket/base syntax/parse "materials.rkt")
         racket/list racket/math "materials.rkt")
(provide define-map source
         (struct-out ground-map) (struct-out map-source) (struct-out wind-field) corridor-wind
         take-registered-maps
         slope bowl crater ground+ max-map-cells min-map-cell)

(define max-map-cells 40000)
(define min-map-cell 0.5)

(struct map-source (id x z flow) #:transparent)
;; heights: a vector, x fastest (i + j nx); soil: a vector of material symbols, the same order
(struct ground-map (name origin cell nx nz heights soil infiltration edges roughness sources loc [cohesion #:auto #:mutable] [grain #:auto #:mutable] [settle #:auto #:mutable] [boulders #:auto #:mutable] [wind #:auto #:mutable]) #:transparent)

;; The wind over a map's floor (issue #61), a regular but variable wind funnelled by a notch in the rim:
;;   v(x, z, t) = speed x corridor(x, z) x daily(hour) x gusts(t)
;; corridor: base + (1 - base) exp(-(d / width)^2), d the distance across the line that runs from the notch
;; (at azimuth notch-deg, measured from +x toward +z) through the point #:through: strongest in a ribbon
;; below the notch, base of it elsewhere. daily: 1 + daily cos(2 pi (hour - peak-hour) / 24), the crater
;; walls draining cold air down at night (peak-hour 2) and drawing warm air up by day. gusts: 1 + gust
;; (sin(2 pi t / 37 s) + sin(2 pi t / 91 s + 1.3)) / 2, the same gusts every run.
;; Issue #193 gives it a heading: heading-deg, the azimuth the wind blows from (same convention, from +x toward +z), or #f for
;; none (windmills keep the heading they were built with), and veer, degrees an hour the heading turns toward +z.
(struct wind-field (through notch-deg speed width base daily peak-hour gust heading-deg veer) #:transparent)
(define (corridor-wind #:through [through '(0 0)] #:notch-deg notch #:speed speed #:width width
                       #:base [base 0.3] #:daily [daily 0.35] #:peak-hour [peak 2] #:gust [gust 0.25]
                       #:heading-deg [heading #f] #:veer [veer 0])
  (unless (or (not heading) (real? heading)) (raise-user-error 'corridor-wind "#:heading-deg is an azimuth in degrees (from +x toward +z) or #f, got ~e" heading))
  (unless (real? veer) (raise-user-error 'corridor-wind "#:veer is degrees an hour, got ~e" veer))
  (unless (and (list? through) (= (length through) 2) (andmap real? through))
    (raise-user-error 'corridor-wind "#:through is (x z), got ~e" through))
  (unless (and (real? speed) (> speed 0)) (raise-user-error 'corridor-wind "#:speed must be more than 0 m/s, got ~e" speed))
  (unless (and (real? width) (> width 0)) (raise-user-error 'corridor-wind "#:width must be more than 0 m, got ~e" width))
  (unless (and (real? base) (<= 0 base 1)) (raise-user-error 'corridor-wind "#:base is a share from 0 to 1, got ~e" base))
  (unless (and (real? daily) (<= 0 daily 1)) (raise-user-error 'corridor-wind "#:daily is a share from 0 to 1, got ~e" daily))
  (unless (and (real? gust) (<= 0 gust 1)) (raise-user-error 'corridor-wind "#:gust is a share from 0 to 1, got ~e" gust))
  (wind-field through notch speed width base daily peak gust heading veer))

(define registry '())
(define (register-map! m) (set! registry (cons m registry)))
(define (take-registered-maps) (begin0 (reverse registry) (set! registry '())))

(define-syntax (source stx)
  (raise-syntax-error #f "a source only means something inside define-map" stx))

;; ---------------------------------------------------------------------------
;; Generators

;; A plane: base + gx x + gz z.
(define (slope #:gradient [g '(0 0)] #:base [base 0])
  (λ (x z) (+ base (* (car g) x) (* (cadr g) z))))

;; A round hollow #:depth deep at #:centre, a paraboloid out to #:radius, level ground beyond.
(define (bowl #:centre [c '(0 0)] #:radius r #:depth d)
  (λ (x z)
    (define s (/ (+ (sqr (- x (car c))) (sqr (- z (cadr c)))) (sqr r)))
    (if (< s 1) (- (* d (- 1 s))) 0)))

;; An impact crater (The Lonely Rover's, #61): a floor #:depth below the
;; plain, walls rising steeply (as r^4) to a rim #:rim-height above it at
;; #:diameter / 2, the rim's ejecta falling away outside over #:rim-width
;; (a Gaussian). #:dunes (amplitude wavelength) ripples the floor; #:bays
;; (count depth) scallops the rim in and out by that fraction of the radius.
(define (crater #:centre [c '(0 0)] #:diameter dia #:depth d #:rim-height hr #:rim-width w
                #:dunes [dunes #f] #:bays [bays #f])
  (define r0 (/ dia 2))
  (λ (x z)
    (define dx (- x (car c)) ) (define dz (- z (cadr c)))
    (define r (sqrt (+ (sqr dx) (sqr dz))))
    (define theta (if (and (zero? dx) (zero? dz)) 0 (atan dz dx)))   ; exact 0/0 has no angle
    (define big-r (if bays (* r0 (+ 1 (* (cadr bays) (cos (* (car bays) theta))))) r0))
    (define s (/ r big-r))
    (cond
      [(< s 1)
       (define floor (+ (- d) (* (+ d hr) (expt s 4))))
       (define ripple (if dunes
                          (* (car dunes) (sin (/ (* 2 pi x) (cadr dunes))) (- 1 (expt s 4)))
                          0))
       (+ floor ripple)]
      [else (* hr (exp (- (sqr (/ (- r big-r) w)))))])))

;; Ground made of several shapes added together.
(define ((ground+ . fs) x z) (for/sum ([f fs]) (f x z)))

;; ---------------------------------------------------------------------------
;; define-map

(define (build-map name origin cell size heights soil infiltration edges roughness sources loc [cohesion '()] [grain '()] [boulders '()])
  (define (fail fmt . args) (raise-user-error name (apply format fmt args)))
  (unless (and (real? cell) (>= cell min-map-cell))
    (fail "#:cell must be at least ~a m (the grid is coarse on purpose: features smaller than a cell belong in a channel), got ~e" min-map-cell cell))
  (define-values (nx nz) (values (car size) (cadr size)))
  (unless (and (exact-positive-integer? nx) (exact-positive-integer? nz))
    (fail "#:size is (cells-across cells-deep), whole numbers, got ~e" size))
  (unless (<= (* nx nz) max-map-cells)
    (fail "~a x ~a = ~a cells is over the budget of ~a; use bigger cells" nx nz (* nx nz) max-map-cells))
  (define-values (x0 z0) (values (car origin) (cadr origin)))
  (define (cx i) (+ x0 (* (+ i 1/2) cell)))
  (define (cz j) (+ z0 (* (+ j 1/2) cell)))
  (define hv
    (cond
      [(procedure? heights)
       (for*/vector #:length (* nx nz) ([j nz] [i nx]) (heights (cx i) (cz j)))]
      [(and (list? heights) (andmap list? heights))
       (unless (and (= (length heights) nz) (andmap (λ (row) (= (length row) nx)) heights))
         (fail "#:heights has ~a rows of ~a; the map is ~a rows of ~a cells"
               (length heights) (remove-duplicates (map length heights)) nz nx))
       (list->vector (apply append heights))]
      [else (fail "#:heights is a procedure (x z) -> m, a generator (slope, bowl, crater), or rows of numbers")]))
  (for ([h hv] [k (in-naturals)])
    (unless (and (real? h) (rational? h))
      (fail "the height at cell (~a, ~a) is ~e, not a finite number" (modulo k nx) (quotient k nx) h)))
  (define sv
    (cond [(symbol? soil) (make-vector (* nx nz) soil)]
          [(procedure? soil) (for*/vector #:length (* nx nz) ([j nz] [i nx]) (soil (cx i) (cz j)))]
          [else (fail "#:soil is a material or a procedure (x z) -> material")]))
  (define known (material-ids))
  (for ([m (remove-duplicates (append (vector->list sv) (map car infiltration) (map car cohesion) (map car grain)))])
    (unless (memq m known) (fail "unknown soil ~a; known materials: ~a" m known)))
  (unless (memq edges '(open closed)) (fail "#:edges is open or closed, got ~e" edges))
  (for ([s sources])
    (unless (and (<= x0 (map-source-x s) (+ x0 (* nx cell))) (<= z0 (map-source-z s) (+ z0 (* nz cell))))
      (fail "source ~a at (~a ~a) is off the map" (map-source-id s) (map-source-x s) (map-source-z s))))
  (for ([c cohesion])
    (unless (and (real? (cadr c)) (>= (cadr c) 0)) (fail "#:cohesion of ~a must be 0 or more (Pa), got ~e" (car c) (cadr c))))
  (define m (ground-map name origin cell nx nz hv sv infiltration edges roughness sources loc))
  (for ([g grain])
    (unless (and (real? (cadr g)) (>= (cadr g) 0)) (fail "#:grain size of ~a must be 0 or more (m), got ~e" (car g) (cadr g)))
    (when (and (pair? (cddr g)) (not (and (real? (caddr g)) (> (caddr g) 1000))))
      (fail "#:grain density of ~a must be more than water's, 1000 kg/m³, got ~e" (car g) (caddr g))))
  ;; boulders (#88): (SOIL FRACTION SIZE MATERIAL), a soil of the map, a fraction of 0 to 1, a cube's side over 0, a material
  (for ([b boulders])
    (unless (memq (car b) (vector->list sv)) (fail "#:boulders names ~a, which is not a soil of this map" (car b)))
    (unless (and (real? (cadr b)) (<= 0 (cadr b) 1)) (fail "#:boulders fraction of ~a must be from 0 to 1, got ~e" (car b) (cadr b)))
    (unless (and (real? (caddr b)) (> (caddr b) 0)) (fail "#:boulders size of ~a must be more than 0 (m), got ~e" (car b) (caddr b)))
    (unless (memq (cadddr b) known) (fail "#:boulders of ~a: unknown material ~a; known materials: ~a" (car b) (cadddr b) known)))
  (set-ground-map-cohesion! m cohesion)
  (set-ground-map-grain! m grain)
  (set-ground-map-boulders! m boulders)
  m)

(begin-for-syntax
  (define known-soils (material-ids))
  (define-syntax-class source-clause
    #:literals (source)
    (pattern (source id:id #:at (x:expr z:expr) #:flow q:expr)
      #:with expr #'(map-source 'id x z q))))

(define-syntax (define-map stx)
  (syntax-parse stx
    [(_ name:id (~alt (~optional (~seq #:origin (ox:expr oz:expr)))
                      (~once (~seq #:cell cell:expr))
                      (~once (~seq #:size (sx:expr sz:expr)))
                      (~once (~seq #:heights heights:expr))
                      (~optional (~seq #:soil soil:expr))
                      (~optional (~seq #:infiltration ((im:id ir:expr) ...)))
                      (~optional (~seq #:cohesion ((cm:id cv:expr) ...)))
                      (~optional (~seq #:grain ((gm:id gv:expr ...+) ...)))
                      (~optional (~seq #:settle settle-v:expr))
                      (~optional (~seq #:wind wind-v:expr))
                      (~optional (~seq #:boulders ((bm:id bf:expr bs:expr bmat:id) ...)))
                      (~optional (~seq #:edges edges:id))
                      (~optional (~seq #:roughness rough:expr))) ...
        s:source-clause ...)
     #:fail-when (and (attribute soil) (identifier? #'soil) (not (memq (syntax-e #'soil) known-soils)) #'soil)
                 (format "unknown soil; known materials: ~a" known-soils)
     #:fail-when (for/first ([m (or (attribute im) '())] #:unless (memq (syntax-e m) known-soils)) m)
                 "unknown soil in #:infiltration"
     #:fail-when (for/first ([m (or (attribute cm) '())] #:unless (memq (syntax-e m) known-soils)) m)
                 "unknown soil in #:cohesion"
     #:fail-when (for/first ([m (append (or (attribute bm) '()) (or (attribute bmat) '()))] #:unless (memq (syntax-e m) known-soils)) m)
                 "unknown material in #:boulders"
     #:fail-when (let ([c (syntax-e #'cell)]) (and (real? c) (< c 0.5) #'cell))
                 "#:cell must be at least 0.5 m"
     #:fail-when (let ([a (syntax-e #'sx)] [b (syntax-e #'sz)])
                   (and (exact-positive-integer? a) (exact-positive-integer? b) (> (* a b) 40000) #'sx))
                 "a map is at most 40,000 cells: use bigger cells"
     #:fail-when (and (attribute edges) (not (memq (syntax-e #'edges) '(open closed))) #'edges)
                 "#:edges is open or closed"
     #`(begin
         (define name
           (build-map 'name (list (~? ox 0) (~? oz 0)) cell (list sx sz) heights
                      #,(if (and (attribute soil) (identifier? #'soil)) #''soil (or (attribute soil) #''sand))
                      (list (~? (~@ (list 'im ir) ...)))
                      '#,(if (attribute edges) #'edges #'open)
                      (~? rough 0.03)
                      (list s.expr ...)
                      '#,(let ([src (syntax-source stx)])
                           (vector (cond [(path? src) (path->string src)] [(string? src) src] [else "?"])
                                   (syntax-line stx) (syntax-column stx)))
                      (list (~? (~@ (list 'cm cv) ...)))
                      (list (~? (~@ (list 'gm gv ...) ...)))
                      (list (~? (~@ (list 'bm bf bs 'bmat) ...)))))
         (set-ground-map-settle! name (let ([v (~? settle-v #f)]) (cond [(not v) #f] [(and (real? v) (> v 0)) v] [else #t])))
         (set-ground-map-wind! name (~? wind-v #f))
         (register-map! name))]))
