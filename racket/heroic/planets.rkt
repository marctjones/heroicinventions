#lang racket/base
;; Reads planets.rktd (issue #38). A planet is a scene-level setting: the
;; numbers (gravity, surface pressure, gas mix, sunlight, sol length) the
;; game's formulas run on. define-machine's #:planet takes a preset's name
;; (#:planet mars) or a planet value made with (planet mars #:gravity 9.81 ...),
;; a preset with some of its numbers changed.
(require racket/runtime-path (for-syntax racket/base))
(provide planet-ids planet-preset planet-field planet-fields planet-name planet?
         make-planet planet planet-gas-names earth-planet?)

(define-runtime-path planets-file "planets.rktd")

;; name: symbol; fields: (listof (list key value ...)) in the file's order
(struct planet-value (name fields) #:transparent)
(define planet? planet-value?)
(define planet-name planet-value-name)
(define planet-fields planet-value-fields)

(define planet-gas-names '(o2 n2 co2 h2o ar))

(define table
  (call-with-input-file planets-file
    (λ (in) (for/list ([entry (in-port read in)]) entry))))

(define (planet-ids) (map car table))

(define (planet-preset id)
  (define entry (assq id table))
  (unless entry
    (error 'planet "no planet named ~a; the planets are: ~a" id (planet-ids)))
  (planet-value id (cdr entry)))

;; The single value of a field; the air as an assoc list ((o2 x) ...);
;; a colour as a list of three.
(define (planet-field p key)
  (define f (assq key (planet-fields p)))
  (unless f (error 'planet-field "planet ~a has no field ~a" (planet-name p) key))
  (case key
    [(air sky-color ground-color daily-temperature) (cdr f)]
    [else (cadr f)]))

;; Is this the default planet, number for number? Then a machine on it
;; writes no planet clause at all.
(define (earth-planet? p) (equal? (planet-fields p) (planet-fields (planet-preset 'earth))))

(define (check who ok? what v)
  (unless ok? (error who "~a, got ~e" what v)))

;; A preset with some numbers changed. #:air replaces the whole mixture:
;; '((o2 0.21) (n2 0.79)); gases left out are 0, and the fractions must add
;; up to 1.
(define (make-planet base
                     #:gravity [gravity #f] #:pressure [pressure #f] #:temperature [temperature #f]
                     #:air [air #f] #:solar-constant [solar #f] #:sol [sol #f]
                     #:sky-transmittance [sky #f] #:air-mass-exponent [ame #f] #:year [year #f] #:obliquity [obliquity #f])
  (define p (if (planet? base) base (planet-preset base)))
  (when gravity (check 'planet (and (real? gravity) (> gravity 0)) "#:gravity must be above 0 m/s²" gravity))
  (when pressure (check 'planet (and (real? pressure) (>= pressure 0)) "#:pressure must be an absolute pressure, 0 Pa or more" pressure))
  (when temperature (check 'planet (and (real? temperature) (> temperature -273.15)) "#:temperature must be above absolute zero" temperature))
  (when solar (check 'planet (and (real? solar) (>= solar 0)) "#:solar-constant must be 0 W/m² or more" solar))
  (when sol (check 'planet (and (real? sol) (> sol 0)) "#:sol must be a day's length in seconds, above 0" sol))
  (when sky (check 'planet (and (real? sky) (<= 0 sky 1)) "#:sky-transmittance must be in [0, 1]" sky))
  (when ame (check 'planet (and (real? ame) (> ame 0)) "#:air-mass-exponent must be above 0" ame))
  (when year (check 'planet (and (real? year) (>= year 1)) "#:year must be 1 day or more" year))
  (when obliquity (check 'planet (and (real? obliquity) (<= 0 obliquity 90)) "#:obliquity must be in [0, 90] degrees" obliquity))
  (define mix
    (and air
         (let ()
           (for ([g air])
             (check 'planet (and (pair? g) (memq (car g) planet-gas-names) (pair? (cdr g)) (real? (cadr g)) (>= (cadr g) 0))
                    (format "#:air lists (gas fraction) for gases among ~a" planet-gas-names) g))
           (define total (for/sum ([g air]) (cadr g)))
           (check 'planet (< (abs (- total 1)) 0.001) "#:air's fractions must add up to 1" total)
           (for/list ([name planet-gas-names])
             (list name (cond [(assq name air) => cadr] [else 0]))))))
  (define (swap key v)
    (λ (f) (if (and v (eq? (car f) key)) (if (eq? key 'air) (cons 'air v) (list key v)) f)))
  (planet-value (planet-name p)
                (for/list ([f (planet-fields p)])
                  (cond
                    ;; a new mixture has its own mean molar mass
                    [(and mix (eq? (car f) 'molar-mass)) (list 'molar-mass #f)]
                    [else
                     (for/fold ([f f]) ([s (list (swap 'gravity gravity) (swap 'pressure pressure) (swap 'temperature temperature)
                                                 (swap 'air mix) (swap 'solar-constant solar) (swap 'sol sol)
                                                 (swap 'sky-transmittance sky) (swap 'air-mass-exponent ame)
                                                 (swap 'year year) (swap 'obliquity obliquity))])
                       (s f))]))))

;; (planet mars #:gravity 9.81): a preset by name, with numbers changed.
(define-syntax (planet stx)
  (syntax-case stx ()
    [(_ base arg ...) (identifier? #'base) #'(make-planet 'base arg ...)]))
