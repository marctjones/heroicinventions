#lang racket/base
;; The catalogue of standard parts the in-game editor will offer. Each
;; entry is (id description thunk) — thunks so that requiring this module
;; doesn't generate every mesh.
;;
;; Sizes follow the historical machines they come from, not round modern
;; numbers: Antikythera-scale gears use its real tooth counts, screws and
;; catapults are sized by Vitruvius's rules, treadwheels and norias by the
;; surviving examples.
(require racket/list "gear.rkt" "screw.rkt" "wheels.rkt" "catapult.rkt")
(provide catalogue catalogue-entry-id catalogue-entry-description catalogue-entry-shape)

(define (catalogue-entry-id e) (first e))
(define (catalogue-entry-description e) (second e))
(define (catalogue-entry-shape e) ((third e)))

(define (mm x) (/ x 1000))

;; The Antikythera mechanism's gears are about half a millimetre per
;; tooth (module ≈ 0.5 mm) and ~2 mm thick plate. These tooth counts
;; are its lunar train and main wheel (Freeth et al. 2006).
(define antikythera-teeth '(24 32 38 48 64 127 223))

;; Involute gears in a few module sizes for mill- and crane-scale work.
(define involute-teeth '(18 24 36 48 72))
(define involute-modules (list (mm 5) (mm 10) (mm 20)))

(define catalogue
  (append
   (for/list ([z antikythera-teeth])
     (list (string->symbol (format "antikythera-gear-~a" z))
           (format "Antikythera-style bronze gear, ~a triangular teeth, module 0.5 mm" z)
           (λ () (spur-gear #:teeth z #:module (mm 0.5) #:width (mm 2) #:profile 'triangular))))
   (for*/list ([m involute-modules] [z involute-teeth])
     (list (string->symbol (format "involute-gear-m~a-~a" (* 1000 m) z))
           (format "Involute spur gear, ~a teeth, module ~a mm" z (* 1000 m))
           (λ () (spur-gear #:teeth z #:module m #:width (* 8 m)))))
   (for/list ([len '(2 4 6)])
     (list (string->symbol (format "vitruvian-screw-~am" len))
           (format "Archimedes' screw by Vitruvius's rules, ~a m long" len)
           (λ () (vitruvian-screw #:length len))))
   (for/list ([r '(0.05 0.1 0.2)])
     (list (string->symbol (format "pulley-~acm" (inexact->exact (round (* 100 r)))))
           (format "Pulley sheave, ~a cm radius" (* 100 r))
           (λ () (pulley #:radius r #:width (* 0.5 r)))))
   (for/list ([r '(0.15 0.3)])
     (list (string->symbol (format "drum-~acm" (inexact->exact (round (* 100 r)))))
           (format "Windlass drum, ~a cm radius" (* 100 r))
           (λ () (drum #:radius r #:length (* 4 r)))))
   ;; Roman crane treadwheels ran about 4–5 m across (the Capua relief,
   ;; the Bonn crane reconstruction)
   (for/list ([r '(2.0 2.5)])
     (list (string->symbol (format "treadwheel-~am" (* 2 r)))
           (format "Roman crane treadwheel, ~a m across" (* 2 r))
           (λ () (treadwheel #:radius r #:width 1.2))))
   ;; The Hama norias on the Orontes range from ~10 m to ~20 m across
   (for/list ([r '(3.0 5.0 10.0)])
     (list (string->symbol (format "noria-~am" (* 2 r)))
           (format "Noria (water-lifting wheel), ~a m across" (* 2 r))
           (λ () (noria #:radius r #:width (* 0.12 r)))))
   ;; Bolt lengths in Greek spans: the 3-span scorpion was the common
   ;; field piece.
   (for/list ([spans '(3 4 6)])
     (list (string->symbol (format "catapulta-frame-~a-span" spans))
           (format "Catapulta frame for a ~a-span (~a cm) bolt, by Vitruvius's table"
                   spans (inexact->exact (round (* 100 spans greek-span))))
           (λ () (catapulta-frame #:bolt-length (* spans greek-span)))))))
