#lang racket/base
;; A client for LiveLinkServer.cs. Connects to a running game over TCP and
;; drives it with the same S-expression vocabulary the game speaks: get,
;; set!, subscribe, run, pause. See game/scripts/LiveLinkServer.cs for the
;; other side of this protocol.
(require racket/tcp racket/port racket/string racket/format)
(provide live-connect live-disconnect live-connected?
         live-send live-get live-set! live-subscribe live-run live-pause live-sleep-until
         live-list-machines live-read-telemetry live-print-telemetry-for)

(struct link (in out) #:mutable)
(define current-link (make-parameter #f))

(define (live-connect [host "127.0.0.1"] [port 4747])
  (define-values (in out) (tcp-connect host port))
  (file-stream-buffer-mode out 'line)
  (define l (link in out))
  (current-link l)
  l)

(define (live-connected?) (and (current-link) #t))

(define (live-disconnect)
  (when (current-link)
    (close-input-port (link-in (current-link)))
    (close-output-port (link-out (current-link)))
    (current-link #f)))

(define (require-link who)
  (or (current-link) (error who "not connected; call (live-connect) first")))

;; Sends one command and returns the game's one-line reply, read back as data.
(define (live-send form)
  (define l (require-link 'live-send))
  (write form (link-out l))
  (newline (link-out l))
  (flush-output (link-out l))
  (read (link-in l)))

(define (live-run) (live-send '(run)))

;; (live-sleep-until "machine" 'wake-id) or (live-sleep-until "machine" '((cistern water above 50)) #:join 'and #:limit 600)
;; sleeps the running machine ahead as fast as it will go until the condition is met (issue #59); the reply is
;; (slept machine reason elapsed detail steps predicted note).
(define (live-sleep-until machine condition #:join [join 'and] #:limit [limit 3600])
  (live-send (if (symbol? condition)
                 (list 'sleep-until (string->symbol (format "~a" machine)) condition)
                 (list 'sleep-until (string->symbol (format "~a" machine)) condition join (exact->inexact limit)))))
(define (live-pause) (live-send '(pause)))
(define (live-list-machines) (live-send '(list-machines)))

;; (live-get 'aeolipile 'kettle 'temperature) → the field's current value
(define (live-get machine target field)
  (match-value (live-send (list 'get machine target field))))

(define (live-set! machine target field value)
  (live-send (list 'set! machine target field (exact->inexact value))))

(define (live-subscribe machine target field)
  (live-send (list 'subscribe machine target field)))

(define (match-value reply)
  (if (and (pair? reply) (eq? (car reply) 'value))
      (last reply)
      (error 'live-get "unexpected reply: ~a" reply)))

(define (last lst) (if (null? (cdr lst)) (car lst) (last (cdr lst))))

;; Blocks for one (telemetry time (machine target field value) ...) line
;; and returns it as a list of (machine target field value) entries.
(define (live-read-telemetry)
  (define l (require-link 'live-read-telemetry))
  (define form (read (link-in l)))
  (if (and (pair? form) (eq? (car form) 'telemetry))
      (cddr form)
      (error 'live-read-telemetry "unexpected message: ~a" form)))

;; Reads and prints telemetry lines for `seconds` of wall-clock time —
;; handy at the REPL: (live-print-telemetry-for 5)
(define (live-print-telemetry-for seconds)
  (define deadline (+ (current-inexact-milliseconds) (* seconds 1000)))
  (let loop ()
    (when (< (current-inexact-milliseconds) deadline)
      (define entries (live-read-telemetry))
      (displayln (string-join (map (λ (e) (~a e)) entries) "  "))
      (loop))))
