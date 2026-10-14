#lang racket/base
;; Building phase 5's acceptance (#254, docs/plans/building-phase5-plan.md): the vault round the found bank built by the mouse alone, then the
;; night. tools/vault-mouse.sh in a hidden real window (it needs a window server: opt in with HEROIC_GUI_VAULT=1; a few minutes), its log
;; read for the build and the trace for the night. The predictions are in the script's header, worked before its first run:
;;  - the log says, in order: the 0.5 m preset, the room's command (#:wall regolith #:insulation 0), depths 0.50 1.00 1.50 and the bank's join
;;    after 1.50 and not before, the rock stood on the room's floor, the bin's (begin ... (set heat_bin_3 #:holds heat_store_2)), the strip's
;;    (begin ... senses enclosure_1 ... drives heat_bin_3), the strip's (remove bimetal_4), the night's numbers typed into the fields, the rock's
;;    the Wall list taken to none and back to regolith (the soil starts afresh at the ground's -55 C: see the script's header), the rock's
;;    200 C last;
;;  - the night is hand-vault-test's network (no strip on the bin): at 03:00 the found bank's cells +4.279 +- 0.05 C (worked: night-heat.rkt,
;;    vault-zone-test; the typed vault traced +4.279), the room's insulation 0, the rock cooled to about 28.7 C.
;; Prints "VAULT BY MOUSE: ...".
(require rackunit racket/system racket/port racket/string racket/list racket/file racket/runtime-path
         (only-in heroic/godothost godot-available?))

(define-runtime-path script "../../../tools/vault-mouse.sh")

(define (last-field path key)
  (define l (last (file->lines path)))
  (string->number (cadr (regexp-match (pregexp (string-append "\\(" (regexp-quote key) " (-?[0-9.eE+-]+)\\)")) l))))

(when (and (godot-available?) (getenv "HEROIC_GUI_VAULT"))
  (define out (path->string (make-temporary-directory "vault-mouse~a")))
  (define log (with-output-to-string (λ () (system* script out))))
  (define lines (string-split log "\n"))
  (define (at rx) (for/first ([l lines] [i (in-naturals)] #:when (regexp-match? rx l)) i))
  (define (in-order . rxs)
    (define is (for/list ([rx rxs]) (at rx)))
    (and (andmap values is) (apply < is)))

  (test-case "the vault is built by the mouse: the log's lines in order"
    (check-true (and (in-order #rx"^\\[build\\] new machine built-1 at \\(254\\.[45][0-9] -4[23]\\.[0-9]+ 137\\.[89][0-9]\\)"
                               #rx"preset enclosure: 0.5 m"
                               #rx"> \\(enclosure enclosure_1 #:at \\([-0-9. ]+\\) #:material bronze #:size-x 0.5 #:size-y 0.5 #:size-z 0.5 #:wall regolith #:insulation 0\\)"
                               #rx"depth: enclosure_1 depth 0.50 m"
                               #rx"depth: enclosure_1 depth 1.00 m"
                               #rx"depth: enclosure_1 depth 1.50 m"
                               #rx"\\[zones\\] battery-bank.cells joined built-1.enclosure_1"
                               #rx"in enclosure_1: the ghost on its floor, 1\\.[45]"
                               #rx"> \\(heat-store heat_store_2 #:at"
                               #rx"placed in enclosure_1: heat_store_2 on its floor"
                               #rx"> \\(begin \\(heat-bin heat_bin_3 #:at \\([-0-9. ]+\\) [^)]*\\) \\(set heat_bin_3 #:holds heat_store_2\\)\\)"
                               #rx"in enclosure_1: heat_bin_3 holds heat_store_2"
                               #rx"> \\(begin \\(bimetal bimetal_4 #:at \\([-0-9. ]+\\) [^)]*\\) \\(set bimetal_4 #:senses enclosure_1\\) \\(set bimetal_4 #:drives heat_bin_3\\)\\)"
                               #rx"> \\(remove bimetal_4\\)"
                               #rx"> \\(set enclosure_1 #:temperature -55\\)"
                               #rx"> \\(set enclosure_1 #:ground -55\\)"
                               #rx"> \\(set enclosure_1 #:pressure 610\\)"
                               #rx"> \\(set enclosure_1 #:wall #f\\)"
                               #rx"> \\(set enclosure_1 #:wall regolith\\)"
                               #rx"> \\(set heat_store_2 #:mass 40\\)"
                               #rx"> \\(set heat_bin_3 #:open 1\\)"
                               #rx"> \\(set heat_store_2 #:temperature 200\\)")
                     #t)
                log)
    (check-false (at #rx"build \\(") "no typed build line")
    ;; no join before the room is 1.50 m down
    (check-true (< (at #rx"depth: enclosure_1 depth 1.00 m") (at #rx"\\[zones\\] battery-bank.cells joined built-1.enclosure_1"))))

  (test-case "the night: the bank is +4.28 C at 03:00, as the typed vault's"
    (define t (last-field (string-append out "/n.battery-bank") "cells.temperature"))
    (define rock (last-field (string-append out "/n.built-1") "heat_store_2.temperature"))
    (define ua (last-field (string-append out "/n.built-1") "enclosure_1.insulation"))
    (define room (last-field (string-append out "/n.built-1") "enclosure_1.temperature"))
    (printf "VAULT BY MOUSE: at 03:00 the found bank's cells are ~a C (worked +4.28; the typed vault +4.279), the rock ~a C, the room ~a C, insulation ~a W/K; shots ~a\n"
            t rock room ua out)
    (check-= ua 0 1e-9)
    (check-= t 4.279 0.05 "C: vault-zone-test's night, the same network")
    (check-true (< 0 t 45) "the bank may charge")))
