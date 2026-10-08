#lang racket/base
;; Builds everything the Godot game needs from the Racket sources:
;;   racket/machines/*.rkt       → game/machines/<name>.machine
;;   racket/heroic/materials.rktd → src/HeroicInventions.Sim/Materials/materials.json
;;   every generated shape a machine uses, and the standard-parts
;;   catalogue                  → game/meshes/<stem>.glb + catalogue.rktd
;;
;; Run from anywhere:  racket racket/build.rkt
(require racket/runtime-path racket/path racket/file racket/list json
         heroic/machine heroic/map heroic/emit heroic/materials heroic/geometry)

(define-runtime-path repo "..")
(define machines-dir (build-path repo "racket" "machines"))
(define out-dir (build-path repo "game" "machines"))
(define maps-dir (build-path repo "racket" "maps"))            ; define-map sources (issue #37)
(define maps-out-dir (build-path repo "game" "maps"))
(define meshes-dir (build-path repo "game" "meshes"))
;; Meshes only the catalogue uses are rebuilt by every build and not
;; committed; meshes a machine uses are, like the .machine files, so the
;; game runs from a fresh checkout without Racket.
(define catalogue-dir (build-path meshes-dir "catalogue"))
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
              'friction (f 'friction)
              'restitution (f 'restitution)
              ;; the look (docs/art-direction.md): an sRGB hex colour, and a finish the game turns
              ;; into a procedural surface; a material without a finish takes its category's
              ;; melting point in °C, for the metals a pressure shell derates towards (#139); null for the rest
              'meltingPoint (material-field/default entry 'melting (json-null))
              ;; thermal properties (#71), J/(kg K) and W/(m K); null where a material has none
              'specificHeat (material-field/default entry 'specific-heat (json-null))
              'conductivity (material-field/default entry 'conductivity (json-null))
              'color (material-field/default entry 'color (json-null))
              'finish (let ([v (material-field/default entry 'finish #f)]) (if v (symbol->string v) (json-null))))))
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
  (define-values (written machines)
    (for*/lists (written machines) ([src sources]
                [m (begin (dynamic-require (simple-form-path src) #f)
                          (take-registered-machines))])
      (define dest (build-path out-dir (format "~a.machine" (machine-name m))))
      (write-machine-file m dest #:root repo #:from (rel src))
      (printf "~a → ~a\n" (rel src) (rel dest))
      (values dest m)))
  ;; Remove .machine files whose machine no longer exists.
  (for ([f (directory-list out-dir #:build? #t)]
        #:when (and (equal? (path-get-extension f) #".machine")
                    (not (member (simple-form-path f) (map simple-form-path written)))))
    (delete-file f)
    (printf "removed stale ~a\n" (rel f)))
  machines)

;; Writes one .glb per distinct shape — every shape a machine's parts use,
;; plus the whole catalogue — and catalogue.rktd, the list the in-game
;; editor offers parts from. Identical shapes share a file (the stem is a
;; hash of the numbers that made them).
(define (build-meshes! machines)
  (make-directory* catalogue-dir)
  ;; Godot would otherwise import each .glb as an editor resource; the
  ;; game loads them itself at runtime (GltfDocument), so keep the
  ;; editor's importer out of this folder.
  (call-with-output-file (build-path meshes-dir ".gdignore") #:exists 'truncate/replace void)
  (define (write-shape! s dir)
    (define stem (shape-file-stem s))
    (write-glb (shape-mesh s) (build-path dir (format "~a.glb" stem))
               #:name stem
               #:extras (for/hasheq ([kv (shape-props s)])
                          (values (car kv) (let ([v (cdr kv)]) (if (symbol? v) (symbol->string v) (exact->inexact v)))))))
  (define machine-shapes
    (remove-duplicates
     (for*/list ([m machines] [p (machine-parts m)] [kv (part-props p)] #:when (shape? (cdr kv)))
       (cdr kv))
     #:key shape-file-stem))
  (define entries
    (for/list ([e catalogue])
      (list (catalogue-entry-id e) (catalogue-entry-description e) (catalogue-entry-shape e))))
  (for ([s machine-shapes]) (write-shape! s meshes-dir))
  (for ([e entries]) (write-shape! (third e) catalogue-dir))
  ;; The editor's palette needs each catalogue entry's moment of inertia
  ;; too (not just volume): a wheel/screw/fixture part in a .machine file
  ;; carries inertia-x/y/z (see emit.rkt's prop->items), and MachineView
  ;; sets a shaped part's RigidBody3D.Inertia straight from those props
  ;; (MachineView.cs, BuildRigidBody) — without them a catalogue part the
  ;; editor places can't be given a physical body.
  (call-with-output-file (build-path catalogue-dir "catalogue.rktd") #:exists 'truncate/replace
    (λ (out)
      (fprintf out ";; Generated by racket/build.rkt from racket/heroic/geometry/catalogue.rkt. Do not edit.\n")
      (fprintf out ";; (entry id description kind mesh-stem volume (inertia-x inertia-y inertia-z) ((prop value) ...))\n(")
      (for ([e entries])
        (define s (third e))
        (fprintf out "\n (entry ~s ~s ~s ~s ~s ~s ~s)" (first e) (second e) (shape-kind s) (shape-file-stem s)
                 (exact->inexact (shape-volume s))
                 (for/list ([i (shape-inertia s)]) (exact->inexact i))
                 (for/list ([kv (shape-props s)])
                   (list (car kv) (if (symbol? (cdr kv)) (cdr kv) (exact->inexact (cdr kv)))))))
      (fprintf out ")\n")))
  (define (prune! dir keep-shapes)
    (define keep (for/list ([s keep-shapes]) (format "~a.glb" (shape-file-stem s))))
    (for ([f (directory-list dir)]
          #:when (and (equal? (path-get-extension f) #".glb") (not (member (path->string f) keep))))
      (delete-file (build-path dir f))
      (printf "removed stale ~a\n" (rel (build-path dir f)))))
  (prune! meshes-dir machine-shapes)
  (prune! catalogue-dir (map third entries))
  (printf "~a (~a meshes used by machines; catalogue: ~a entries in ~a)\n"
          (rel meshes-dir) (length machine-shapes) (length entries) (rel catalogue-dir)))

;; racket/maps/*.rkt → game/maps/<name>.map (issue #37), as machines are built.
(define (build-maps!)
  (when (directory-exists? maps-dir)
    (make-directory* maps-out-dir)
    (define written
      (for*/list ([src (sort (for/list ([f (directory-list maps-dir #:build? #t)]
                                        #:when (equal? (path-get-extension f) #".rkt")) f)
                             path<?)]
                  [m (begin (dynamic-require (simple-form-path src) #f) (take-registered-maps))])
        (define dest (build-path maps-out-dir (format "~a.map" (ground-map-name m))))
        (write-map-file m dest #:root repo #:from (rel src))
        (printf "~a → ~a (~a x ~a cells of ~a m)\n" (rel src) (rel dest) (ground-map-nx m) (ground-map-nz m) (ground-map-cell m))
        (simple-form-path dest)))
    (for ([f (directory-list maps-out-dir #:build? #t)]
          #:when (and (equal? (path-get-extension f) #".map") (not (member (simple-form-path f) written))))
      (delete-file f)
      (printf "removed stale ~a\n" (rel f)))))

(module+ main
  (export-materials!)
  (build-meshes! (build-machines!))
  (build-maps!))
