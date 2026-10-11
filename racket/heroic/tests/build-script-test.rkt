#lang racket/base
;; Building plan, phase 0 (docs/plans/building-plan.md): build mode's own scripted steps from a world's script (HEROIC_INPUT's
;; "build WORD ...", Main.Build.cs forwarding to BuildMode.ScriptStep), and the nested-box pick (PickBoxes.Choose) on a windmill's gears.
;; Headless Godot at 120 Hz in lonely-rover-opening, a new machine at (200, 130): a post, the 10 m sails, a 72-tooth wheel on the sails'
;; arbor and an 18-tooth pinion beside it (the first stage of the route's train).
;; Predicted:
;;  - "build bogus" before any build: "[build] no build open"; "build frob" in a build: "[build] unknown build word: frob";
;;  - "build picklist 1" with no list open says so; "build palette gear" places the cluster's smallest size first, involute-gear-m5-18
;;    (state line placing=involute-gear-m5-18); Esc stops placing;
;;  - "build select sails" -> selected=sails; "build link mesh" -> link=Mesh;
;;  - from behind the train (look 180 12 3 at the wheel's plane), "build click-part pinion-b" clicks where the camera draws the pinion: the
;;    ray enters the sails' box first (it reaches back past the gears to the windshaft's end), the log names sails first and says
;;    "pinion-b lies inside sails's box and is the smaller: it takes the click"; selected=pinion-b;
;;  - the same click again (same spot, same view) offers the pick list, sails first; "build picklist 1" chooses the sails: selected=sails.
;; Phase 1 (post and sails by mouse), a second run at (200, 130): "build palette post", "build preset post 10" (a real click on the card's
;; nearest preset button, 10.6 m), a click at the build's spot places it; "build palette windmill" and a click on the post's top.
;; Predicted: "[BuildMode] preset post: 10.6 m (size-x 0.6 size-y 10.6 size-z 0.6)"; the post's command carries #:size-y 10.6; the log
;; says the windmill's hub is on post_1's top (post top + nave radius 0.45), the post under the train 1.45 m behind the sails and 0.45 m
;; aside, so hub - post foot = (-0.45, 11.05, 1.45), within 0.1 m of the route's (0 11 0) - (0.45 0 -1.45) = (-0.45, 11, 1.45), and
;; "the sails' disc clears the post by 1.03 m" (1.45 - 0.3 half depth - 0.12 the stocks); "build list" says "post_1:post windmill_2:windmill".
;; A second windmill clicked on open ground 8 m off is placed as before (no hub-on-post line for it), and a third clicked on the post's top
;; with Alt held is placed freely (no hub-on-post line either).
;; The mouse path in a real window, with a shot, is tools/windmill-gui.sh. Skipped when Godot is not installed.
(require rackunit racket/system racket/port racket/string racket/list racket/runtime-path
         (only-in heroic/godothost godot-available? godot-binary))

(define-runtime-path game-dir "../../../game")

(define (run script)
  (define env (environment-variables-copy (current-environment-variables)))
  (for ([kv `(("HEROIC_WORLD" . "lonely-rover-opening") ("HEROIC_HINTS" . "0") ("HEROIC_INPUT" . ,script))])
    (environment-variables-set! env (string->bytes/utf-8 (car kv)) (string->bytes/utf-8 (cdr kv))))
  (string-split
   (parameterize ([current-directory game-dir] [current-environment-variables env])
     (with-output-to-string (λ () (system* godot-binary "--headless" "--fixed-fps" "120" "--path" "."))))
   "\n"))
(define (index-of-line rx ls [from 0]) (for/first ([l ls] [i (in-naturals)] #:when (and (>= i from) (regexp-match? rx l))) i))
(define (states ls) (for/list ([l ls] #:when (string-prefix? l "[BuildMode] state:")) l))
(define (field name l) (cadr (regexp-match (regexp (string-append " " name "=([^ ]+)")) l)))

(when (godot-available?)
  (define lines
    (run (string-join
          '("wait 360" "build bogus" "build new 200 130" "build picklist 1" "build palette gear" "build log" "key escape" "build log"
            "build (post tower #:at (0.45 0 -1.45) #:size-y 10.6 #:size-x 0.6 #:size-z 0.6)"
            "build (windmill sails #:at (0 11 0) #:wind-from-map #f)"
            "build (wheel wheel-a #:catalogue involute-gear-m5-72 #:at (0 11 -1.3))" "build (arbor sails wheel-a)"
            "build (wheel pinion-b #:catalogue involute-gear-m5-18 #:at (0.225 11 -1.3))"
            "build select sails" "build log" "build link mesh" "build log" "key escape"
            "look 180 12 3 200.1 -52.45 128.7" "wait 10" "build click-part pinion-b" "wait 10" "build log"
            "build click-part pinion-b" "wait 10" "build picklist 1" "build log" "build frob" "quit")
          "; ")))
  (define all (string-join lines "\n"))
  (define st (states lines))

  (test-case "words build mode doesn't know, and words with nothing to act on, say so"
    (check-true (and (index-of-line #rx"^\\[build\\] no build open" lines) #t) all)
    (check-true (and (index-of-line #rx"^\\[build\\] unknown build word: frob" lines) #t))
    (check-true (and (index-of-line #rx"^\\[BuildMode\\] no pick list is open" lines) #t)))

  (test-case "palette, select and link from the world's script"
    (check-equal? (length st) 6 all)
    (check-equal? (field "placing" (list-ref st 0)) "involute-gear-m5-18")
    (check-equal? (field "placing" (list-ref st 1)) "none" "Esc stops placing")
    (check-equal? (field "selected" (list-ref st 2)) "sails")
    (check-equal? (field "link" (list-ref st 3)) "Mesh"))

  (test-case "a click on the pinion picks the pinion, not the sails whose box holds it; a second click offers the list"
    (define crossed (index-of-line #rx"^\\[BuildMode\\] the click crossed [0-9]+ parts' boxes, nearest first: sails " lines))
    (check-true (and crossed #t) all)
    (check-true (and (index-of-line #rx"^\\[BuildMode\\] pinion-b lies inside sails's box and is the smaller: it takes the click" lines) #t))
    (check-equal? (field "selected" (list-ref st 4)) "pinion-b")
    (define listed (index-of-line #rx"^\\[BuildMode\\] pick list at .*: 1 sails, .*pinion-b" lines))
    (check-true (and listed #t))
    (check-true (and (index-of-line #rx"^\\[BuildMode\\] chosen from the list: 1 sails" lines) #t))
    (check-equal? (field "selected" (list-ref st 5)) "sails")))

(when (godot-available?)
  (define lines
    (run (string-join
          '("wait 360" "build new 200 130" "build palette post" "wait 5" "build preset post 10" "wait 5" "build click-at 0 0 0" "wait 5"
            "build palette windmill" "wait 5" "build click-at 0 10.6 0" "wait 5"
            "build palette windmill" "wait 5" "build click-at 8 0 0" "wait 5"
            "build palette windmill" "wait 5" "build click-at 0 10.6 0 alt" "wait 5" "build log" "build list" "quit")
          "; ")))
  (define all (string-join lines "\n"))
  (define (at-of id)   ; a part's #:at from the last state line, as numbers
    (define st (last (states lines)))
    (map string->number (string-split (cadr (regexp-match (regexp (string-append " parts=.*" id "@\\(([^)]*)\\)")) st)))))

  (test-case "the post's card: the tall preset by a click gives the route's 10.6 m tower"
    (check-true (and (index-of-line #rx"^\\[BuildMode\\] preset post: 10.6 m \\(size-x 0.6 size-y 10.6 size-z 0.6\\)" lines) #t) all)
    (check-true (and (index-of-line #rx"^\\[BuildMode\\] > \\(post post_1 .*#:size-x 0.6 #:size-y 10.6 #:size-z 0.6" lines) #t)))

  (test-case "the windmill's ghost on the post's top: hub over its middle, the nave resting on the top"
    (check-true (and (index-of-line #rx"^\\[BuildMode\\] the windmill's hub on post_1's top: post top .* \\+ nave radius 0.45 = .*the post under the train 1.45 m behind the sails and 0.45 m aside; the sails' disc clears the post by 1.03 m" lines) #t) all)
    (check-true (and (index-of-line #rx"^\\[build\\] built-1: design post_1:post windmill_2:windmill windmill_3:windmill windmill_4:windmill" lines) #t))
    (define post (at-of "post_1"))
    (define hub (at-of "windmill_2"))
    (define rel (map - hub post))
    (for ([got rel] [want '(-0.45 11 1.45)]) (check-within got want 0.1 (format "hub - post foot = ~a" rel)))
    (check-equal? (length (filter (λ (l) (regexp-match? #rx"hub on post_1's top" l)) lines)) 1 "the windmill off the post, and the one placed with Alt, aren't snapped")
    (check-true (> (abs (- (car (at-of "windmill_4")) (car hub))) 0.1) "with Alt the hub isn't put where the snap puts it")
    (check-true (> (abs (- (- (cadr (at-of "windmill_3")) (cadr post)) 11.05)) 0.1) "off the post the ghost rests as before")))
