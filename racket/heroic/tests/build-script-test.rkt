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
;; Phase 2 ("Put a gear on this shaft"), a third run at (200, 130): the route's post and sails typed, "build select sails", "build gear-on 72"
;; (picks the 72 on the gear's card, presses the inspector's button by a real click, places at the default spot).
;; Predicted: "[BuildMode] arbor: sails, wheel_1", wheel_1 - sails = (0, 0, -1.3) (the route's wheel-a: the windshaft runs back along -z
;; from a mill facing +z); the new wheel is selected, so a second "build gear-on 72" puts wheel_2 0.1 m further back, (0, 0, -1.4), "arbor:
;; wheel_1, wheel_2"; "build list" has both. Then by the mouse, viewed across the shaft: "gear-on 72 hover" on wheel_2 and a click at
;; (0 11 -2): the ghost held on the axle where the ray passes nearest, wheel_3 - sails = (0, 0, -2), joined to the axle's lead, "arbor:
;; wheel_1, wheel_3" (ShaftSlots.PartnerOf); a click with Alt held puts wheel_4 where the free ghost is (on the sails' box, metres off the
;; line), still "arbor: wheel_1, wheel_4", and the log says "placed freely (Alt)". The document's arbors: sails+wheel_1, wheel_1+wheel_2,
;; wheel_1+wheel_3, wheel_1+wheel_4.
;; Phase 3 ("Add a meshing gear" and the live ratio), a fourth run at (200, 130): the route's post and sails typed; "build select sails",
;; "build gear-on 72", "build mesh-with 18" (presses the selected wheel's "Add a meshing gear" by a real click, places at the default: the
;; pitch circle at +x), then three times "build gear-on 72" (on the new pinion's axle, 0.1 m behind it) and "build mesh-with 18".
;; Predicted, relative to the sails: wheel_1 (0 0 -1.3), wheel_2 (0.225 0 -1.3), wheel_3 (0.225 0 -1.4), wheel_4 (0.45 0 -1.4), wheel_5
;; (0.45 0 -1.5), wheel_6 (0.675 0 -1.5), wheel_7 (0.675 0 -1.6), wheel_8 (0.9 0 -1.6): the route's wheel-a, pinion-b ... pinion-e
;; (solve.steps:93-115) within 1 mm; 72s and 18s alternately (the catalogue ids in the echoed commands); arbors sails+wheel_1,
;; wheel_2+wheel_3, wheel_4+wheel_5, wheel_6+wheel_7 and meshes wheel_1+wheel_2, wheel_3+wheel_4, wheel_5+wheel_6, wheel_7+wheel_8, as the
;; route's (sails+wheel-a, pinion-b+wheel-b, ...; wheel-a+pinion-b, ...), all at their centre distance (no "~" in the log's meshes field);
;; the log's train lines 4:1, 16:1, 64:1, then "sails -> wheel_8, ratio 256:1 (4 meshes)"; "build list" has 10 parts. Then by the mouse,
;; from behind the train looking down the axles: "mesh-with 18 hover" on wheel_1 and a click at (0 11.225 -1.3): the ghost on the pitch
;; circle where the ray meets the wheel's plane, 90 degrees (up), wheel_9 - sails = (0 0.225 -1.3), "on the pitch circle at 90°"; with Alt
;; a click at (-0.4 11 -1.3) puts wheel_10 there (in the wheel's plane, off the circle), still meshed, "drawn orange", meshes field
;; wheel_1+wheel_10~0.4/0.225. The Gear mesh tool on a typed 18 at (-0.2 10.4 -1.3) and wheel_1: meshed, 0.632 m apart, drawn orange.
;; The train's last gear stays wheel_8 at 256:1 (the most meshes from the sails).
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

(when (godot-available?)
  (define lines
    (run (string-join
          '("wait 360" "build new 200 130"
            "build (post tower #:at (0.45 0 -1.45) #:size-y 10.6 #:size-x 0.6 #:size-z 0.6)"
            "build (windmill sails #:at (0 11 0) #:wind-from-map #f)"
            "build select sails" "wait 5" "build gear-on 72" "wait 5" "build log"
            "build gear-on 72" "wait 5" "build log" "build list"
            "look 90 10 6 200 -48.2 128.5" "wait 10" "build gear-on 72 hover" "wait 5" "build hover-at 0 11 -2" "wait 5" "build click-at 0 11 -2" "wait 5"
            "build gear-on 72 hover" "wait 5" "build click-at 0.5 11 -3 alt" "wait 5" "build log" "quit")
          "; ")))
  (define all (string-join lines "\n"))
  (define st (states lines))
  (define (at-of id [n (sub1 (length st))])   ; a part's #:at from state line n, as numbers
    (map string->number (string-split (cadr (regexp-match (regexp (string-append "[ ,=]" id "@\\(([^)]*)\\)")) (list-ref st n))))))
  (define (arbor-line a b) (index-of-line (regexp (string-append "^\\[BuildMode\\] arbor: " a ", " b "$")) lines))
  (define (rel id) (map - (at-of id) (at-of "sails")))
  (define (on-shaft id want)
    (define r (rel id))
    (check-within (car r) 0 0.001 (format "~a - sails = ~a" id r))
    (check-within (cadr r) 0 0.001 (format "~a - sails = ~a" id r))
    (check-within (caddr r) want 0.01 (format "~a - sails = ~a" id r)))

  (test-case "gear-on 72 on the selected sails: a 72 on the windshaft 1.3 m behind the hub, joined on the sails' arbor"
    (check-true (and (arbor-line "sails" "wheel_1") #t) all)
    (check-equal? (field "selected" (list-ref st 0)) "wheel_1")
    (check-regexp-match #rx"arbors=sails\\+wheel_1$" (list-ref st 0))
    (check-true (and (index-of-line #rx"^\\[BuildMode\\] > \\(wheel wheel_1 #:at .* #:catalogue involute-gear-m5-72 " lines) #t))
    (on-shaft "wheel_1" -1.3))

  (test-case "a second gear-on 72, on the new wheel: 0.1 m further back on the same axle"
    (check-true (and (arbor-line "wheel_1" "wheel_2") #t) all)
    (on-shaft "wheel_2" -1.4)
    (check-true (and (index-of-line #rx"^\\[build\\] built-1: design tower:post sails:windmill wheel_1:wheel wheel_2:wheel" lines) #t)))

  (test-case "by the mouse: the ghost held on the axle where the ray passes nearest; Alt frees it, still joined"
    (check-true (and (arbor-line "wheel_1" "wheel_3") #t) all)
    (on-shaft "wheel_3" -2)
    (check-true (and (arbor-line "wheel_1" "wheel_4") #t))
    (check-true (and (index-of-line #rx"^\\[BuildMode\\] gear on wheel_3's shaft: wheel_4 .*placed freely \\(Alt\\): [0-9.]+ m off the axle line" lines) #t))
    (define r4 (rel "wheel_4"))
    (check-true (> (sqrt (+ (* (car r4) (car r4)) (* (cadr r4) (cadr r4)))) 1) (format "the Alt-placed wheel is off the axle: ~a" r4))
    (check-regexp-match #rx"arbors=sails\\+wheel_1,wheel_1\\+wheel_2,wheel_1\\+wheel_3,wheel_1\\+wheel_4$" (last st))))

(when (godot-available?)
  (define lines
    (run (string-join
          '("wait 360" "build new 200 130"
            "build (post tower #:at (0.45 0 -1.45) #:size-y 10.6 #:size-x 0.6 #:size-z 0.6)"
            "build (windmill sails #:at (0 11 0) #:wind-from-map #f)"
            "build select sails" "wait 5" "build gear-on 72" "wait 5" "build mesh-with 18" "wait 5" "build log"
            "build gear-on 72" "wait 5" "build mesh-with 18" "wait 5"
            "build gear-on 72" "wait 5" "build mesh-with 18" "wait 5"
            "build gear-on 72" "wait 5" "build mesh-with 18" "wait 5" "build log" "build list"
            "look 180 8 3 200 -48.2 128.7" "wait 10" "build select wheel_1" "wait 5"
            "build mesh-with 18 hover" "wait 5" "build hover-at 0 11.225 -1.3" "wait 5" "build click-at 0 11.225 -1.3" "wait 5"
            "build select wheel_1" "wait 5" "build mesh-with 18 hover" "wait 5" "build click-at -0.4 11 -1.3 alt" "wait 5"
            "build (wheel far #:catalogue involute-gear-m5-18 #:at (-0.2 10.4 -1.3))" "wait 5"
            "build link mesh" "wait 5" "build click-part far" "wait 10" "build click-part wheel_1" "wait 10" "build log" "quit")
          "; ")))
  (define all (string-join lines "\n"))
  (define st (states lines))
  (define (at-of id [n (sub1 (length st))])
    (map string->number (string-split (cadr (regexp-match (regexp (string-append "[ ,=]" id "@\\(([^)]*)\\)")) (list-ref st n))))))
  (define (rel id [n (sub1 (length st))]) (map - (at-of id n) (at-of "sails" n)))
  (define (near id want [n (sub1 (length st))])
    (for ([r (rel id n)] [w want]) (check-within r w 0.001 (format "~a - sails = ~a, want ~a" id (rel id n) want))))

  (test-case "gear-on 72 then mesh-with 18 on the sails: the route's first stage, the pinion on the pitch circle at +x, 4:1"
    (check-true (and (index-of-line #rx"^\\[BuildMode\\] mesh with wheel_1: wheel_2 \\(involute-gear-m5-18\\) .*on the pitch circle at 0°" lines) #t) all)
    (check-true (and (index-of-line #rx"^\\[BuildMode\\] mesh: wheel_1, wheel_2: centre distance 0.225 m .* they stand 0.225 m apart: on it$" lines) #t))
    (check-true (and (index-of-line #rx"^\\[BuildMode\\] train: sails -> wheel_2, ratio 4:1 \\(1 mesh\\)$" lines) #t))
    (check-equal? (field "selected" (list-ref st 0)) "wheel_2")
    (near "wheel_1" '(0 0 -1.3) 0)
    (near "wheel_2" '(0.225 0 -1.3) 0))

  (test-case "four stages by gear-on 72 and mesh-with 18: the route's train, 256:1"
    (for ([id '("wheel_1" "wheel_2" "wheel_3" "wheel_4" "wheel_5" "wheel_6" "wheel_7" "wheel_8")]
          [want '((0 0 -1.3) (0.225 0 -1.3) (0.225 0 -1.4) (0.45 0 -1.4) (0.45 0 -1.5) (0.675 0 -1.5) (0.675 0 -1.6) (0.9 0 -1.6))])
      (near id want 1))
    (for ([id '("wheel_1" "wheel_2" "wheel_3" "wheel_4" "wheel_5" "wheel_6" "wheel_7" "wheel_8")] [teeth '(72 18 72 18 72 18 72 18)])
      (check-true (and (index-of-line (regexp (format "^\\[BuildMode\\] > \\(wheel ~a #:at .* #:catalogue involute-gear-m5-~a " id teeth)) lines) #t) id))
    (check-regexp-match #rx" meshes=wheel_1\\+wheel_2,wheel_3\\+wheel_4,wheel_5\\+wheel_6,wheel_7\\+wheel_8 arbors=sails\\+wheel_1,wheel_2\\+wheel_3,wheel_4\\+wheel_5,wheel_6\\+wheel_7$" (list-ref st 1))
    (for ([r '("16:1" "64:1")]) (check-true (and (index-of-line (regexp (string-append "^\\[BuildMode\\] train: sails -> wheel_[0-9]+, ratio " r)) lines) #t) r))
    (check-true (and (index-of-line #rx"^\\[BuildMode\\] train: sails -> wheel_8, ratio 256:1 \\(4 meshes\\)$" lines) #t))
    (check-true (and (index-of-line #rx"^\\[build\\] built-1: design tower:post sails:windmill wheel_1:wheel wheel_2:wheel wheel_3:wheel wheel_4:wheel wheel_5:wheel wheel_6:wheel wheel_7:wheel wheel_8:wheel;" lines) #t)))

  (test-case "by the mouse: the ghost on the pitch circle where the ray meets the wheel's plane; Alt frees it in the plane, orange, still meshed"
    (check-true (and (index-of-line #rx"^\\[BuildMode\\] mesh with wheel_1: wheel_9 .*on the pitch circle at 90°" lines) #t) all)
    (near "wheel_9" '(0 0.225 -1.3))
    (check-true (and (index-of-line #rx"^\\[BuildMode\\] mesh with wheel_1: wheel_10 .*placed freely \\(Alt\\)" lines) #t))
    (check-true (and (index-of-line #rx"^\\[BuildMode\\] mesh: wheel_1, wheel_10: .* drawn orange \\(0.4 m apart, not 0.225 m \\(too far by 0.175 m\\)\\)" lines) #t))
    ;; (the machine view as it stands, MachineView.CheckMesh at 2 %, refuses it on stderr, which run doesn't read: "wheel_1 and wheel_10
    ;; are 400.0 mm apart; to mesh they must be 225.0 mm")
    (near "wheel_10" '(-0.4 0 -1.3)))

  (test-case "the Gear mesh tool on two gears off their centre distance: meshed, drawn orange; the train still ends at wheel_8, 256:1"
    (check-true (and (index-of-line #rx"^\\[BuildMode\\] mesh: far, wheel_1: .* drawn orange" lines) #t) all)
    (check-regexp-match #rx" meshes=wheel_1\\+wheel_2,wheel_3\\+wheel_4,wheel_5\\+wheel_6,wheel_7\\+wheel_8,wheel_1\\+wheel_9,wheel_1\\+wheel_10~0.4/0.225,far\\+wheel_1~0.632/0.225 " (last st))
    (check-false (index-of-line #rx"^\\[BuildMode\\] train: sails -> wheel_(9|10)|train: sails -> far" lines))))
