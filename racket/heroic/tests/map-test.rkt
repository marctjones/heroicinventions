#lang racket/base
;; define-map (issue #37): the checks a map must pass, and what it writes.
(require rackunit racket/list racket/string "../map.rkt" "../emit.rkt")

(define-namespace-anchor anchor)
(define ns (namespace-anchor->namespace anchor))

;; Expands (and runs) a define-map form; its errors, syntax or not, come back as the exception.
(define (define-map-form form)
  (parameterize ([current-namespace ns])
    (eval `(let () ,form (void)))))

(define-syntax-rule (check-map-error rx form)
  (check-exn (λ (e) (regexp-match? rx (exn-message e))) (λ () (define-map-form 'form))))

(test-case "a map's soil must be a known material, checked when it is compiled"
  (check-exn exn:fail:syntax?
             (λ () (define-map-form '(define-map m #:cell 1 #:size (4 4) #:heights (λ (x z) 0) #:soil mud))))
  (check-exn exn:fail:syntax?
             (λ () (define-map-form '(define-map m #:cell 1 #:size (4 4) #:heights (λ (x z) 0) #:infiltration ((mud 1e-5)))))))

(test-case "a map keeps to the cell budget: cells of at least 0.5 m, at most 40,000 of them"
  (check-exn exn:fail:syntax? (λ () (define-map-form '(define-map m #:cell 0.25 #:size (4 4) #:heights (λ (x z) 0)))))
  (check-exn exn:fail:syntax? (λ () (define-map-form '(define-map m #:cell 1 #:size (201 200) #:heights (λ (x z) 0)))))
  (check-map-error #rx"over the budget" (define-map m #:cell 1 #:size ((* 201 1) 200) #:heights (λ (x z) 0))))

(test-case "rows of heights must fill the grid exactly; heights must be finite; springs must stand on the map"
  (check-map-error #rx"has 2 rows" (define-map m #:cell 1 #:size (3 3) #:heights '((0 0 0) (0 0 0))))
  (check-map-error #rx"not a finite number" (define-map m #:cell 1 #:size (2 2) #:heights (λ (x z) (/ 1.0 0.0))))
  (check-map-error #rx"off the map" (define-map m #:cell 1 #:size (2 2) #:heights (λ (x z) 0) (source s #:at (5 5) #:flow 0.1))))

(test-case "a map writes its heights at the cells' centres, and one soil index for a uniform map"
  (define-map small #:origin (-1 0) #:cell 2 #:size (2 2) #:heights (slope #:gradient '(0.5 0.1) #:base 1) #:soil sand
    #:infiltration ((sand 1e-5)) (source spring #:at (0 1) #:flow 0.01))
  (define s (map->sexp small))
  (check-equal? (assq 'size (cddr s)) '(size 2 2))
  ;; centres at x = 0, 2 and z = 1, 3: 1 + 0.5x + 0.1z
  (check-equal? (cdr (assq 'heights (cddr s))) '(1.1 2.1 1.3 2.3))
  (check-equal? (assq 'soil (cddr s)) '(soil 0))
  (check-equal? (assq 'soils (cddr s)) '(soils (sand 1e-5)))
  (check-equal? (assq 'source (cddr s)) '(source spring 0.0 1.0 0.01)))

(test-case "the crater generator: floor, rim and ejecta where they should be, finite everywhere, with dunes and bays"
  (define c (crater #:diameter 200 #:depth 20 #:rim-height 5 #:rim-width 30 #:dunes '(0.5 12) #:bays '(7 0.05)))
  (check-= ((crater #:diameter 200 #:depth 20 #:rim-height 5 #:rim-width 30) 0 0) -20 1e-9 "the floor at the centre")
  (check-= ((crater #:diameter 200 #:depth 20 #:rim-height 5 #:rim-width 30) 100 0) 5 1e-9 "the rim's crest at the radius")
  (check-true (< ((crater #:diameter 200 #:depth 20 #:rim-height 5 #:rim-width 30) 160 0) 0.2) "the ejecta dying away outside")
  (for* ([x (in-range -300 301 7)] [z (in-range -300 301 11)])
    (define h (c x z))
    (check-true (and (real? h) (rational? h)) (format "finite at (~a, ~a)" x z))))
