#lang racket/base
;; Time and weather on a planet (issue #69): define-machine's #:weather.
;;
;; (weather [#:daily #t] [#:passes '(3 15)] [#:pass-minutes 10] [#:storms (list (storm ...) ...)])
;;   #:daily   the air follows the planet's daily curve (planets.rktd's
;;             daily-temperature: coldest before dawn, warmest in the
;;             afternoon), unless the machine fixes its #:ambient
;;   #:passes  local solar hours at which the relay orbiter passes over,
;;             each pass #:pass-minutes long (Mars Reconnaissance Orbiter:
;;             about 03:00 and 15:00)
;;   #:storms  dust storms, as a schedule the player can see coming
;;
;; (storm #:sol n [#:hour h] #:tau τ [#:sols d] [#:settle k])
;;   from hour h of the run's nth sol (the first sol is 1), for d sols, the
;;   air's dust optical depth is τ (Opportunity's last storm: 10.8), so the
;;   sun's beam falls as e^(-τ·AM); dust settles on mirrors, taking the
;;   fraction k a sol of what they still reflect (default 0.5).
(provide weather weather? weather-without-daily weather-daily weather-passes weather-pass-minutes weather-storms
         storm storm? storm-sol storm-hour storm-tau storm-sols storm-settle)

(struct weather-spec (daily passes pass-minutes storms) #:transparent)
(struct storm-spec (sol hour tau sols settle) #:transparent)
(define weather? weather-spec?)
(define weather-daily weather-spec-daily)
(define weather-passes weather-spec-passes)
(define weather-pass-minutes weather-spec-pass-minutes)
(define weather-storms weather-spec-storms)
(define storm? storm-spec?)
(define storm-sol storm-spec-sol)
(define storm-hour storm-spec-hour)
(define storm-tau storm-spec-tau)
(define storm-sols storm-spec-sols)
(define storm-settle storm-spec-settle)

(define (bad who what v) (error who "~a, got ~e" what v))

(define (weather #:daily [daily #t] #:passes [passes '(3 15)] #:pass-minutes [minutes 10] #:storms [storms '()])
  (unless (boolean? daily) (bad 'weather "#:daily must be #t or #f" daily))
  (unless (and (list? passes) (andmap (λ (h) (and (real? h) (<= 0 h) (< h 24))) passes))
    (bad 'weather "#:passes must be a list of local solar hours in [0, 24)" passes))
  (unless (and (real? minutes) (> minutes 0)) (bad 'weather "#:pass-minutes must be above 0" minutes))
  (unless (and (list? storms) (andmap storm? storms)) (bad 'weather "#:storms must be a list of (storm ...)" storms))
  (weather-spec daily passes minutes storms))

(define (storm #:sol sol #:hour [hour 0] #:tau tau #:sols [sols 1] #:settle [settle 0.5])
  (unless (exact-positive-integer? sol) (bad 'storm "#:sol must be a sol of the run, 1 or more" sol))
  (unless (and (real? hour) (<= 0 hour) (< hour 24)) (bad 'storm "#:hour must be in [0, 24)" hour))
  (unless (and (real? tau) (>= tau 0)) (bad 'storm "#:tau must be an optical depth, 0 or more" tau))
  (unless (and (real? sols) (> sols 0)) (bad 'storm "#:sols must be above 0" sols))
  (unless (and (real? settle) (>= settle 0)) (bad 'storm "#:settle must be 0 or more a sol" settle))
  (storm-spec sol hour tau sols settle))

;; The same weather with the air held still: a machine that fixes its #:ambient keeps it.
(define (weather-without-daily w) (struct-copy weather-spec w [daily #f]))
