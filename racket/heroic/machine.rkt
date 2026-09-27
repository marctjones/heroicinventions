#lang racket/base
;; define-machine: the heart of #lang heroic.
;;
;; A machine is a list of part clauses (tank, boiler, rotor, block,
;; pendulum, lever, ramp) and link clauses (pipe, connect, sealed-air).
;; pendulum/lever/ramp need no solver of their own — they're pure Jolt
;; rigid-body physics, built in MachineView. The macro checks the whole
;; machine while the file compiles: part names, materials, port names,
;; port kinds, and that every rotor has steam. Errors point at the exact
;; clause that is wrong. Parameter values are ordinary Racket expressions,
;; evaluated when the module runs.

(require (for-syntax racket/base racket/list racket/string syntax/parse "materials.rkt"))

(provide define-machine
         tank boiler rotor block pendulum lever ramp pipe connect sealed-air port
         (struct-out machine) (struct-out part) (struct-out port-spec)
         (struct-out pipe-spec) (struct-out connect-spec) (struct-out air-spec)
         take-registered-machines)

;; ---------------------------------------------------------------------------
;; Runtime representation

(struct machine (name source parts pipes connects airs) #:transparent)
;; kind: 'tank | 'boiler | 'rotor | 'block
;; at: (list x y z); props: (listof (cons symbol value)); loc: #(file line column)
(struct part (id kind material at props ports loc) #:transparent)
(struct port-spec (name kind height) #:transparent)          ; kind: 'water | 'steam
(struct pipe-spec (id from to conductance jet? loc) #:transparent) ; from/to: (list part port)
(struct connect-spec (from to loc) #:transparent)
(struct air-spec (tanks tube-volume loc) #:transparent)

(define (make-machine name source items)
  (machine name source
           (filter part? items)
           (filter pipe-spec? items)
           (filter connect-spec? items)
           (filter air-spec? items)))

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

(define-clause-keywords tank boiler rotor block pendulum lever ramp pipe connect sealed-air port)

;; ---------------------------------------------------------------------------
;; Compile-time checking

(begin-for-syntax
  ;; What the checker needs to know about each part.
  (struct pinfo (id kind mat ports)) ; ports: (listof (cons symbol kind))
  (struct linfo (type id from to))   ; type: 'pipe | 'connect
  (struct ainfo (tanks))             ; tanks: (listof identifier)

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
    #:description "a part (tank, boiler, rotor, block, pendulum, lever, ramp) or link (pipe, connect, sealed-air)"
    #:literals (tank boiler rotor block pendulum lever ramp pipe connect sealed-air)
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
                           (~optional (~seq #:material mat:id))) ...)
      #:attr info (pinfo #'id 'boiler (attribute mat) (list (cons 'steam 'steam)))
      #:with expr #`(part 'id 'boiler '(~? mat bronze) (list at.x at.y at.z)
                          (list (cons 'radius radius-v) (cons 'height height-v)
                                (cons 'water water-v) (cons 'fire (~? fire-v 0)))
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

    ;; #:hang-from attaches the block to another part with a free hinge
    ;; (no angular limit) instead of just resting it by friction — a real
    ;; trebuchet counterweight hangs this way, so it stays vertical under
    ;; gravity as the arm rotates instead of sliding off like cargo on a
    ;; tilting ramp.
    (pattern (block id:id
                    (~alt (~once (~seq #:at at:vec3))
                          (~once (~seq #:size size-v:expr))
                          (~once (~seq #:material mat:id))
                          (~optional (~seq #:hang-from hang:id))) ...)
      #:attr info (pinfo #'id 'block (attribute mat) '())
      #:with expr #`(part 'id 'block 'mat (list at.x at.y at.z)
                          (list (cons 'size size-v) (cons 'hang-from '(~? hang #f)))
                          '()
                          #,(loc-of this-syntax)))

    ;; A compound pendulum: a rod hanging from a fixed pivot at #:at, with a
    ;; bob at its far end. Jolt computes its real moment of inertia from the
    ;; rod+bob shapes, so this swings with genuine (not idealized point-mass)
    ;; pendulum dynamics — released from #:start-angle-deg off vertical.
    (pattern (pendulum id:id
                       (~alt (~once (~seq #:at at:vec3))
                             (~once (~seq #:length length-v:expr))
                             (~once (~seq #:material mat:id))
                             (~optional (~seq #:start-angle-deg angle-v:expr))) ...)
      #:attr info (pinfo #'id 'pendulum (attribute mat) '())
      #:with expr #`(part 'id 'pendulum 'mat (list at.x at.y at.z)
                          (list (cons 'length length-v) (cons 'start-angle-deg (~? angle-v 30)))
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
    (pattern (lever id:id
                    (~alt (~once (~seq #:at at:vec3))
                          (~once (~seq #:length length-v:expr))
                          (~once (~seq #:material mat:id))
                          (~optional (~seq #:start-angle-deg angle-v:expr))
                          (~optional (~seq #:pivot-fraction pivot-v:expr))
                          (~optional (~seq #:limit-deg limit-v:expr))
                          (~optional (~seq #:damping damping-v:expr))) ...)
      #:attr info (pinfo #'id 'lever (attribute mat) '())
      #:with expr #`(part 'id 'lever 'mat (list at.x at.y at.z)
                          (list (cons 'length length-v) (cons 'start-angle-deg (~? angle-v 0))
                                (cons 'pivot-fraction (~? pivot-v 1/2))
                                (cons 'limit-deg (~? limit-v 18))
                                (cons 'damping (~? damping-v 8.0)))
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

    (pattern (sealed-air (t:id ...+) (~optional (~seq #:tube tube:expr)))
      #:attr info (ainfo (syntax->list #'(t ...)))
      #:with expr #`(air-spec '(t ...) (~? tube 0) #,(loc-of this-syntax))))

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
             (cond [(and (eq? (pinfo-kind pa) 'boiler) (eq? (pinfo-kind pb) 'rotor)) (values pa pb)]
                   [(and (eq? (pinfo-kind pb) 'boiler) (eq? (pinfo-kind pa) 'rotor)) (values pb pa)]
                   [else (fail "a steam connection must join a boiler to a rotor" (linfo-to l))]))
           (define b-sym (syntax-e (pinfo-id b)))
           (hash-set! steam-feeds (syntax-e (pinfo-id r)) b-sym)
           (hash-update! boiler-loads b-sym add1 0)
           (when (> (hash-ref boiler-loads b-sym) 1)
             (fail (format "boiler ~a already feeds a rotor; one rotor per boiler for now" b-sym)
                   (linfo-to l))))]))

    (for ([(sym p) parts] #:when (eq? (pinfo-kind p) 'rotor))
      (unless (hash-ref steam-feeds sym #f)
        (fail (format "rotor ~a has no steam supply; add (connect <boiler>.steam ~a.steam-in)" sym sym)
              (pinfo-id p))))

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
    [(_ name:id (~optional (~seq #:source src:expr)) c:clause ...)
     (check-machine! stx (attribute c.info))
     #'(begin
         (define name (make-machine 'name (~? src #f) (list c.expr ...)))
         (register-machine! name))]))
