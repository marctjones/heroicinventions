#lang racket/base
;; Builds everything the Godot game needs from the Racket sources:
;;   racket/machines/*.rkt       → game/machines/<name>.machine
;;   racket/heroic/materials.rktd → src/HeroicInventions.Sim/Materials/materials.json
;;
;; Run from anywhere:  racket racket/build.rkt
(require racket/runtime-path racket/path racket/file json
         heroic/machine heroic/emit heroic/materials)

(define-runtime-path repo "..")
(define machines-dir (build-path repo "racket" "machines"))
(define out-dir (build-path repo "game" "machines"))
(define materials-json (build-path repo "src" "HeroicInventions.Sim" "Materials" "materials.json"))

(define (rel p) (path->string (find-relative-path (simple-form-path repo) (simple-form-path p))))

(define (export-materials!)
  (define materials
    (for/list ([entry (material-table)])
      (define (f k) (material-field entry k))
      (hasheq 'id (symbol->string (car entry))
              'name (f 'name)
              'category (symbol->string (f 'category))
              'density (f 'density)
              'youngsModulus (f 'youngs-modulus)
              'tensileStrength (f 'tension)
              'tensileStrengthAcrossGrain (f 'across-grain)
              'compressiveStrength (f 'compression)
              'friction (f 'friction))))
  (call-with-output-file materials-json #:exists 'truncate/replace
    (λ (out)
      (write-json (hasheq '_note "Generated from racket/heroic/materials.rktd by racket/build.rkt. Edit that file, not this one."
                          'materials materials)
                  out)
      (newline out)))
  (printf "~a (~a materials)\n" (rel materials-json) (length materials)))

(define (build-machines!)
  (make-directory* out-dir)
  (define sources
    (sort (for/list ([f (directory-list machines-dir #:build? #t)]
                     #:when (equal? (path-get-extension f) #".rkt"))
            f)
          path<?))
  (define written
    (for*/list ([src sources]
                [m (begin (dynamic-require (simple-form-path src) #f)
                          (take-registered-machines))])
      (define dest (build-path out-dir (format "~a.machine" (machine-name m))))
      (write-machine-file m dest #:root repo #:from (rel src))
      (printf "~a → ~a\n" (rel src) (rel dest))
      dest))
  ;; Remove .machine files whose machine no longer exists.
  (for ([f (directory-list out-dir #:build? #t)]
        #:when (and (equal? (path-get-extension f) #".machine")
                    (not (member (simple-form-path f) (map simple-form-path written)))))
    (delete-file f)
    (printf "removed stale ~a\n" (rel f))))

(module+ main
  (export-materials!)
  (build-machines!))
