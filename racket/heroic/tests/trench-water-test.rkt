#lang racket/base
;; Issue #200: water on the rover's worked ground, through the game's own dig, save and load (headless Godot, Jolt, 120 Hz).
;; The spring-channel world (racket/maps/spring-field.rkt): a 6 L/s spring on level ground at (-6.6, 0). The rover digs a
;; channel from it along x, eighteen bucketfuls 0.6 m apart from alternate sides (3.6 m³; the spoil goes beside it), and at
;; 300 s the world is saved. Predictions worked out first:
;;  - the spring has poured 0.006 × 300 = 1.8 m³ (× the clock the save is written at, the first tick past 300 s);
;;  - every drop is on the ledger: poured − soaked in − run off = what stands on the patch's fine grid + on the map's 5 m
;;    cells, to 1e-9 of what was poured (the coupling moves water across the seam and never makes or loses any);
;;  - the channel holds 3.6 m³, twice what has come, so the water stands in it: 95% or more of the water on the patch is over
;;    ground cut more than 0.1 m down. (Before #200 the solver saw only the 5 m cells and the channel held none.)
;;  - loaded again, the save's patch carries its water: the next save, 10 s on, has the 1.86 m³ poured less what soaked in
;;    since, and the ledger still closes.
;; Skipped when Godot is not installed.
(require rackunit racket/system racket/port racket/string racket/list racket/file racket/math racket/runtime-path
         (only-in heroic/godothost godot-available? godot-binary))

(define-runtime-path game-dir "../../../game")

(define (run-game script env)
  (define e (environment-variables-copy (current-environment-variables)))
  (environment-variables-set! e #"HEROIC_WORLD" #"spring-channel")
  (environment-variables-set! e #"HEROIC_INPUT" (string->bytes/utf-8 script))
  (for ([kv env]) (environment-variables-set! e (string->bytes/utf-8 (car kv)) (string->bytes/utf-8 (cdr kv))))
  (parameterize ([current-directory game-dir] [current-environment-variables e])
    (with-output-to-string (λ () (system* godot-binary "--headless" "--fixed-fps" "120" "--path" ".")))))

;; the channel: from x = -6.6 to 3.6 along z = 0, the rover 2 m off it, north and south in turn (each side tips its spoil away
;; from the last heap)
(define dig-script
  (string-join
   (append
    '("wait 60")
    (for*/list ([i 18]
                [step (list (if (even? i)
                                (format "rover place ~a 2 0" (/ (round (* 10 (+ -6.6 (* 0.6 i)))) 10.0))
                                (format "rover place ~a -2 180" (/ (round (* 10 (+ -6.6 (* 0.6 i)))) 10.0)))
                            "wait 60" "key b" "wait 60" "rover until Stowed" "wait 30" "rover")])
      step)
    '("rover place -1 5 0" "wait 30000" "quit"))
   "; "))

(define (field form key) (for/first ([f (in-list form)] #:when (and (pair? f) (eq? (car f) key))) f))
(define (state-value ground path)   ; (ground (state (path value) ...))
  (cadr (field (cdr (field (cdr ground) 'state)) path)))

;; what a save says of the water: poured, soaked in, run off, drained, clipped; standing on the 5 m cells; on the patch, all
;; told and over ground cut more than 0.1 m down
(define (water-of path)
  (define save (with-input-from-file path (λ () (read))))
  (define ground (field (cddr save) 'ground))
  (define (v k) (state-value ground k))
  (define coarse (* 25 (apply + (v 'ground/Water/_h))))
  (define patch (car (filter pair? (cddr (field (cddr save) 'worked)))))   ; (patch (box ...) ...)
  (define box (map inexact->exact (cdr (field (cdr patch) 'box))))
  (define heights (list->vector (cdr (field (cdr patch) 'heights))))
  (define k 20) (define nx (add1 (* k (- (caddr box) (car box))))) (define fx (* k (- (caddr box) (car box) 1)))
  (define cell-area (* 0.25 0.25))
  (define-values (on-patch in-channel)
    (for/fold ([all 0.0] [cut 0.0]) ([e (in-list (cdr (or (field (cdr patch) 'water) '(water))))])
      (define c (inexact->exact (car e))) (define d (cadr e))
      (define-values (a b) (values (remainder c fx) (quotient c fx)))
      (define-values (i j) (values (+ a (quotient k 2)) (+ b (quotient k 2))))
      (define bed (/ (+ (vector-ref heights (+ i (* j nx))) (vector-ref heights (+ i 1 (* j nx)))
                        (vector-ref heights (+ i (* (add1 j) nx))) (vector-ref heights (+ i 1 (* (add1 j) nx)))) 4))
      (values (+ all (* d cell-area)) (if (< bed -0.1) (+ cut (* d cell-area)) cut))))
  (define clock (cadr (field (cdddr (field (cddr save) 'machine)) 'clock)))
  (hash 'clock clock 'poured (v 'ground/Water/Poured) 'infiltrated (v 'ground/Water/Infiltrated) 'leaked (v 'ground/Water/Leaked)
        'drained (v 'ground/Water/Drained) 'clipped (v 'ground/Water/Clipped)
        'coarse coarse 'patch on-patch 'channel in-channel))

(define (ledger w)
  (- (hash-ref w 'poured)
     (+ (hash-ref w 'infiltrated) (hash-ref w 'leaked) (hash-ref w 'drained) (hash-ref w 'clipped) (hash-ref w 'coarse) (hash-ref w 'patch))))

(when (godot-available?)
  (define dir (make-temporary-file "trench-water~a" 'directory))
  (define (p f) (path->string (build-path dir f)))
  (run-game dig-script `(("HEROIC_SAVE" . ,(p "dug.save")) ("HEROIC_SAVE_AT" . "300")))
  (define dug (water-of (p "dug.save")))
  (run-game "wait 1800; quit" `(("HEROIC_LOAD" . ,(p "dug.save")) ("HEROIC_SAVE" . ,(p "on.save")) ("HEROIC_SAVE_AT" . "310")))
  (define on (water-of (p "on.save")))

  (test-case "the spring's water, every drop of it, is on the ledger"
    ;; the save is written on the first tick at or past 300 s: the spring's 6 L/s times the clock it was written at
    (check-= (hash-ref dug 'clock) 300 (/ 1.0 120))
    (check-= (hash-ref dug 'poured) (* 0.006 (hash-ref dug 'clock)) 1e-9)
    (check-true (< (abs (ledger dug)) (* 1e-9 (hash-ref dug 'poured))) (format "ledger off by ~a m3" (ledger dug))))

  (test-case "the dug channel holds the water, at its own 0.25 m cells"
    (printf "trench-water: ~a m3 on the patch, ~a in the channel (~a%), ~a on the 5 m cells, ~a soaked in\n"
            (hash-ref dug 'patch) (hash-ref dug 'channel) (* 100 (/ (hash-ref dug 'channel) (hash-ref dug 'patch)))
            (hash-ref dug 'coarse) (hash-ref dug 'infiltrated))
    (check-true (> (hash-ref dug 'channel) (* 0.95 (hash-ref dug 'patch))))
    (check-true (> (hash-ref dug 'patch) 1.7)))

  (test-case "the patch's water survives a save and load"
    (check-= (hash-ref on 'poured) (* 0.006 (hash-ref on 'clock)) 1e-9)
    (check-= (hash-ref on 'clock) 310 (/ 1.0 120))
    (check-true (< (abs (ledger on)) (* 1e-9 (hash-ref on 'poured))) (format "ledger off by ~a m3 after the load" (ledger on)))
    (check-true (> (hash-ref on 'channel) (hash-ref dug 'channel)) "the channel goes on filling from where it was")))
