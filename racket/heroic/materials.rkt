#lang racket/base
;; Reads materials.rktd. Used at compile time by define-machine (to reject
;; unknown materials) and at build time to export JSON for the C# runtime.
(require racket/runtime-path)
(provide material-table material-ids material-field)

(define-runtime-path materials-file "materials.rktd")

;; → (listof (cons id-symbol (listof (list field value))))
(define (material-table)
  (call-with-input-file materials-file
    (λ (in) (for/list ([entry (in-port read in)]) entry))))

(define (material-ids) (map car (material-table)))

(define (material-field entry field)
  (cond [(assq field (cdr entry)) => cadr]
        [else (error 'material-field "material ~a has no field ~a" (car entry) field)]))
