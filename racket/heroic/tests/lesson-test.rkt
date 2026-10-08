#lang racket/base
;; Lesson 1, the see-saw (#166), played through in the real game, headless:
;; the palette picks, clicks and the drag go through Godot's own input
;; pipeline (game/scripts/ScriptedInput.cs), the two Tests run the design in
;; Jolt, and the lesson prints each step as it completes ("[Lesson] ...").
;;
;; Worked by hand (game/lessons/01-see-saw.lesson): a 10 cm granite cube is
;; 2.7 kg, an oak one 0.72 kg. At 0.2 m each side the granite turns 0.54
;; against 0.144, so the beam tips to its 18° stop; the oak balances it at
;; 0.54 / 0.72 = 0.75 m, and there the beam's traced tilt must stay within 3°.
;; Skipped where Godot isn't installed (set HEROIC_GODOT).
(require rackunit racket/port racket/string racket/list racket/file racket/math heroic/godothost)

(define-values (game-dir) (simplify-path (build-path (collection-file-path "godothost.rkt" "heroic") 'up 'up 'up "game")))

;; Runs build mode with an input script and a fresh lesson folder; returns the lines it printed.
(define (run-editor script #:seconds [seconds 40])
  (define lessons (make-temporary-file "heroic-lessons-~a" 'directory))
  (define env (environment-variables-copy (current-environment-variables)))
  (for ([kv `(("HEROIC_EDITOR" . "1") ("HEROIC_EDITOR_INPUT" . ,script)
              ("HEROIC_EDITOR_QUIT_AFTER_SECONDS" . ,(number->string seconds))
              ("HEROIC_LESSONS_DIR" . ,(path->string lessons)))])
    (environment-variables-set! env (string->bytes/utf-8 (car kv)) (string->bytes/utf-8 (cdr kv))))
  (define out
    (parameterize ([current-environment-variables env] [current-directory game-dir])
      (define-values (p stdout stdin stderr)
        (subprocess #f #f #f godot-binary "--headless" "--resolution" "1280x800" "."))
      (close-output-port stdin)
      (define drain (thread (λ () (port->string stderr))))
      (begin0 (port->string stdout) (subprocess-wait p) (thread-wait drain) (close-input-port stdout) (close-input-port stderr))))
  (delete-directory/files lessons #:must-exist? #f)
  (string-split out "\n"))

(define (lines-with ls s) (filter (λ (l) (string-contains? l s)) ls))
(define (number-after l rx) (cond [(regexp-match rx l) => (λ (m) (string->number (cadr m)))] [else #f]))

;; Lesson 1, the way a newcomer plays it: pick the lit entry, click on the green target,
;; press Test, drag the light block out, and let the lesson run its own check.
(define playthrough
  (string-append "wait 30; lesson see-saw; wait 5; "
                 "palette lever; click-at 0.1 0 0.05; wait 5; "            ; step 1: the beam, a little off its target
                 "palette block; click-at -0.2 0.42 0; wait 5; "           ; step 2: granite (the block's own material)
                 "palette block; click-at 0.2 0.42 0; wait 5; "            ; step 3: oak (the lesson picks it on the card)
                 "test; wait 5; wait-test; "                               ; step 4: the granite end goes down
                 "drag-to block_3 0.72 0.42 0; wait 5; wait-test; wait 5; " ; step 5, then the lesson's own run (step 6)
                 "log"))

(test-case "Lesson 1 plays through: every step completes and the lesson's own run finds the beam balanced"
  (when (godot-available?)
    (define out (run-editor playthrough))
    (define done (lines-with out "[Lesson] step "))
    (for ([n (in-range 1 7)])
      (check-true (ormap (λ (l) (regexp-match? (pregexp (format "\\[Lesson\\] step ~a done" n)) l)) done)
                  (format "step ~a completes" n)))
    ;; steps 1-3 and 5 set a close placement exactly on its target
    (check-true (ormap (λ (l) (string-contains? l "(move block_3 (0.75 0.4625 0))")) out) "the oak block is set on 0.75 m")
    ;; step 4's Test: the granite end went down to the stop, as worked out
    (define tip (findf (λ (l) (string-contains? l "test seen at step 4")) out))
    (check-true (and tip #t) "the first Test is seen at step 4")
    (check-true (> (number-after tip #px"lever_1 most ([0-9.]+)") 15) (format "the beam tipped to its stop: ~a" tip))
    (check-true (string-contains? tip "block_2=2.7kg") "the sim's granite block weighs 2.7 kg")
    (check-true (string-contains? tip "block_3=0.72kg") "the sim's oak block weighs 0.72 kg")
    (check-true (ormap (λ (l) (string-contains? l "The beam tipped 18° down on the left, on the granite block's side.")) out)
                "Test says what happened in a sentence")
    ;; step 6: the lesson ran the machine and the traced tilt stayed within 3°
    (define check (findf (λ (l) (string-contains? l "step 6 done: balanced")) out))
    (check-true (and check #t) "the final check passes")
    (check-true (<= (number-after check #px"most tilt ([0-9.]+)") 3) (format "within 3°: ~a" check))
    (check-true (ormap (λ (l) (string-contains? l "[Lesson] finished see-saw")) out) "the lesson says it is done")))

(test-case "A lesson left part-way is taken up again at the same step, with its design"
  (when (godot-available?)
    (define lessons (make-temporary-file "heroic-lessons-~a" 'directory))
    (define (run script)
      (define env (environment-variables-copy (current-environment-variables)))
      (for ([kv `(("HEROIC_EDITOR" . "1") ("HEROIC_EDITOR_INPUT" . ,script) ("HEROIC_EDITOR_QUIT_AFTER_SECONDS" . "12")
                  ("HEROIC_LESSONS_DIR" . ,(path->string lessons)))])
        (environment-variables-set! env (string->bytes/utf-8 (car kv)) (string->bytes/utf-8 (cdr kv))))
      (parameterize ([current-environment-variables env] [current-directory game-dir])
        (define-values (p stdout stdin stderr) (subprocess #f #f #f godot-binary "--headless" "--resolution" "1280x800" "."))
        (close-output-port stdin)
        (define drain (thread (λ () (port->string stderr))))
        (begin0 (string-split (port->string stdout) "\n") (subprocess-wait p) (thread-wait drain) (close-input-port stdout) (close-input-port stderr))))
    (run "wait 30; lesson see-saw; wait 5; palette lever; click-at 0 0 0; wait 5; palette block; click-at -0.2 0.42 0; wait 5; lesson-leave")
    (define again (run "wait 30; lesson-resume see-saw; wait 5; log"))
    (delete-directory/files lessons #:must-exist? #f)
    (check-true (ormap (λ (l) (string-contains? l "[Lesson] started see-saw at step 3")) again) "it carries on at step 3")
    (check-true (ormap (λ (l) (regexp-match? #px"state: .*lever_1@\\(0 0.4 0\\).*block_2@\\(-0.2 0.463 0\\)" l)) again)
                "with the beam and the granite block back where they were")))

;; #180: a placement that doesn't count says why, in the lesson panel's words. At step 2 (a granite
;; block on the green block, 0.2 m left of the pivot) three wrong things in turn, each taken away
;; before the next; the panel's note is printed as "[Lesson] step 2 nudge shown: ..." when it changes.
;; Worked by hand: a block at -0.5 is 0.3 m from the green one at -0.2 (within 0.15 m), so 0.3 m right.
(define wrong-placements
  (string-append "wait 30; lesson see-saw; wait 5; "
                 "palette lever; click-at 0 0 0; wait 5; "                              ; step 1 done: the beam
                 "palette ball; click-at -0.2 0.42 0; wait 5; "                           ; the wrong part, by the palette
                 "log; cmd (remove ball_2); wait 3; "
                 "cmd (block w #:at (-0.2 0.47 0) #:material oak); wait 5; "             ; the wrong material
                 "cmd (remove w); wait 3; "
                 "palette block; click-at -0.5 0.42 0; wait 5; "                          ; off the target, by the palette
                 "log"))

(test-case "Lesson 1 says why a placement didn't count: wrong part, wrong material, off the target"
  (when (godot-available?)
    (define out (run-editor wrong-placements #:seconds 45))
    (define nudges (map (λ (l) (cadr (regexp-match #px"nudge shown: (.*)$" l))) (lines-with out "step 2 nudge shown: ")))
    (check-equal? (length nudges) 3 (format "three nudges: ~a" nudges))
    (check-equal? (car nudges)
                  "That is a ball, but this step needs a block. Take the ball away and pick Block in the parts list."
                  "the wrong part")
    (check-equal? (cadr nudges)
                  "That block is oak, a wood, but this step asks for a stone one, such as granite. Take it away and pick Block again: the lesson picks the material on its card."
                  "the wrong material")
    (check-equal? (caddr nudges)
                  "Not on the green block yet: that block is 0.3 m from it, and it has to be within 0.15 m. Move it 0.3 m to the right."
                  "off the target")
    (check-false (ormap (λ (l) (string-contains? l "[Lesson] step 2 done")) out) "none of them counted")))

;; ---------------------------------------------------------------------------------------------
;; Lessons 2 to 4 (#183, #184, #185), played the same way: the palette picks, clicks and the
;; rope's two clicks go through Godot's input, each Test runs the design in the real sim, and the
;; lesson's own check reads the Test's facts ("[BuildMode] facts: ...") against the worked number.

(define (steps-done out n)   ; the numbers of the steps 1..n that completed
  (for/list ([k (in-range 1 (add1 n))]
             #:when (ormap (λ (l) (regexp-match? (pregexp (format "\\[Lesson\\] step ~a done" k)) l)) out))
    k))

;; the number after "key=" in the facts line of the Test, or #f; `part` is the "rise block_3" etc.
(define (fact-of out part key)
  (define facts (findf (λ (l) (string-contains? l "[BuildMode] facts: ")) out))
  (and facts
       (let ([m (regexp-match (pregexp (string-append (regexp-quote part) "=(-?[0-9.]+)(?: peak=-?[0-9.]+ low=-?[0-9.]+ across=([0-9.]+) top=([0-9.]+))?")) facts)])
         (and m (case key
                  [(value) (string->number (cadr m))]
                  [(across) (and (caddr m) (string->number (caddr m)))]
                  [(top) (and (cadddr m) (string->number (cadddr m)))])))))

(define (facts-line out) (findf (λ (l) (string-contains? l "[BuildMode] facts: ")) out))

;; ---- Lesson 2: the pulley
;; Worked by hand (game/lessons/02-pulley.lesson): a granite block is 2.7 kg, an oak one 0.72 kg, so over a
;; fixed pulley a = g (2.7 - 0.72) / (2.7 + 0.72) = 5.68 m/s² and the rope carries 11.15 N. The stone is hung
;; 0.55 m up, so it falls 0.55 m and the wood is hauled up 0.55 m (the rope does not stretch): the wood ends
;; 0.55 m up (0.5472 measured, 0.82 m at its highest on the stone's bounce), the pulley turns 0.55 / (2 pi 0.1) = 0.875
;; of a turn, and the stone lands at sqrt(2 a d) = sqrt(2 5.68 0.55) = 2.50 m/s.
(define pulley-playthrough
  (string-append "wait 30; lesson pulley; wait 5; "
                 "palette post; click-at 0 0 0; wait 5; "                  ; step 1: the post (the lesson slims it)
                 "palette pulley; click-at 0 1.0 0; wait 5; "              ; step 2: the 10 cm pulley the card offers, in oak
                 "palette block; click-at 0.1 0 0; wait 5; "               ; step 3: oak, on the ground to the right
                 "palette block; click-at -0.1 0 0; wait 5; "              ; step 4: granite, to the left
                 "palette rope; click-part block_4; click-part block_3; wait 5; "   ; step 5: stone, then wood; the lesson threads it
                 "log; test; wait-test; wait 5; "                          ; step 6: Test, and the lesson reads its facts
                 "lesson-leave; lesson-resume pulley; wait 5; log; quit"))

(test-case "Lesson 2 plays through: the rope is threaded over the pulley and the wood rises as far as the stone fell"
  (when (godot-available?)
    (define out (run-editor pulley-playthrough #:seconds 60))
    (check-equal? (steps-done out 6) '(1 2 3 4 5 6) "every step completes")
    (check-true (ormap (λ (l) (string-contains? l "(set post_1 #:size-x 0.06)")) out) "the post is made slim")
    (check-true (ormap (λ (l) (string-contains? l "(move block_4 (-0.1 0.6 0))")) out) "the stone is hung 0.55 m up")
    (check-true (ormap (λ (l) (string-contains? l "(rope rope-1 #:from (block_4 0 0.05 0) #:to (block_3 0 0.05 0) #:length 1.7561 #:over ((-0.1 1.1 0)")) out)
                "the rope runs over the pulley, 1.7561 m of it")
    ;; the Test: the wood rose 0.55 m (within 0.08), the stone fell 0.55 m, the pulley turned 0.875 of a turn, the stone landed at 2.50 m/s
    (check-= (fact-of out "rise block_3" 'value) 0.55 0.08 (format "the wood rose 0.55 m: ~a" (facts-line out)))
    (check-= (fact-of out "rise block_4" 'value) -0.55 0.01 "the stone fell 0.55 m")
    (check-= (fact-of out "turns pulley_10cm_2" 'value) (/ 0.55 (* 2 pi 0.1)) 0.06 "the pulley turned 0.875 of a turn")
    (check-= (fact-of out "rise block_4" 'top) (sqrt (* 2 (/ (* 9.81 (- 2.7 0.72)) (+ 2.7 0.72)) 0.55)) 0.15 "the stone landed at 2.50 m/s")
    (check-true (ormap (λ (l) (string-contains? l "step 6 done: lifted")) out) "the lesson's check passes")
    (check-true (ormap (λ (l) (and (string-contains? l "[Lesson] finished pulley") (string-contains? l "5.68 m/s²") (string-contains? l "The wood rose 0.55 m"))) out)
                "the lesson says it is done, with the worked acceleration")
    ;; a finished lesson is remembered like lesson 1's: taken up again, it starts from its first step, with its design
    (check-true (ormap (λ (l) (string-contains? l "[Lesson] started pulley at step 1")) out) "a finished lesson is remembered")))

;; The sim's own acceleration, from a trace of this very rig (HEROIC_TRACE of the saved design, 0.05 s samples):
;; the stone's speed rises 5.679 m/s each second between 0.15 s and 0.40 s, and the rope carries 11.152 N.
(test-case "Lesson 2's worked numbers are the sim's own"
  (check-= (/ (* 9.81 (- 2.7 0.72)) (+ 2.7 0.72)) 5.68 0.005)
  (check-= (/ (* 2 2.7 0.72 9.81) (+ 2.7 0.72)) 11.152 0.001))

;; Wrong things at the lesson's own steps: a 5 cm pulley, and a rope tied to the pulley instead of the two blocks.
(define pulley-wrong
  (string-append "wait 30; lesson pulley; wait 5; "
                 "palette post; click-at 0 0 0; wait 5; "
                 "palette pulley-5cm; click-at 0 1.0 0; wait 5; "          ; the wrong size (the card's choice is changed by hand)
                 "cmd (remove pulley_5cm_2); wait 3; "
                 "palette pulley-10cm; click-at 0.02 1.0 0; wait 5; "     ; (a click on the same pixel as before sends no mouse move, so the ghost wouldn't follow)
                 "palette block; click-at 0.1 0 0; wait 5; "
                 "palette block; click-at -0.1 0 0; wait 5; "
                 "palette rope; click-part pulley_10cm_3; click-part block_5; wait 5; "     ; rope: pulley to stone
                 "quit"))

(test-case "Lesson 2 says why a pulley or a rope didn't count"
  (when (godot-available?)
    (define out (run-editor pulley-wrong #:seconds 60))
    (define size (lines-with out "step 2 nudge shown: "))
    (check-equal? (length size) 1 (format "one nudge for the pulley: ~a" size))
    (check-true (string-contains? (car size) "That pulley is 5 cm in radius, but this lesson uses the 10 cm one. Take it away, pick the right size in the box under the parts list and place it again.") (car size))
    (define join (lines-with out "step 5 nudge shown: "))
    (check-equal? (length join) 1 (format "one nudge for the rope: ~a" join))
    (check-true (string-contains? (car join) "That rope joins the pulley to the granite block, but this step needs it between the granite block and the oak block. Undo it (Ctrl+Z) and join those two.") (car join))
    (check-false (ormap (λ (l) (string-contains? l "[Lesson] step 5 done")) out) "that rope did not count")))

;; ---- Lesson 3: the ramp and the ball
;; Worked by hand (game/lessons/03-ramp.lesson): the ball is set 0.9 m up the 15° ramp, its centre at y = 0.3063 m; it comes
;; to rest on flat ground with its centre 0.05 m up, so it gives up h = 0.2563 m and at the most reaches
;; v = sqrt(10/7 g h) = 1.895 m/s (a sliding block: sqrt(2 g h) = 2.243), turning at 37.9 rad/s, under the 47.1 rad/s cap.
;; Nothing slows a ball rolling on flat ground, so it goes on for the 30 s of the test: at least a metre.
(define ramp-playthrough
  (string-append "wait 30; lesson ramp; wait 5; "
                 "palette ramp; click-at 0 0 0; wait 5; "                  ; step 1: the ramp
                 "palette ball; click-at 0 0.3 -0.85; wait 5; "            ; step 2: the ball, near the top (set exactly on the slope)
                 "log; test; wait-test; wait 5; quit"))                    ; step 3: Test

(test-case "Lesson 3 plays through: the ball rolls at sqrt(10/7 g h), not the sliding block's sqrt(2 g h)"
  (when (godot-available?)
    (define out (run-editor ramp-playthrough #:seconds 90))
    (check-equal? (steps-done out 3) '(1 2 3) "every step completes")
    (check-true (ormap (λ (l) (string-contains? l "(move ball_2 (0 0.3063 -0.8497))")) out) "the ball is set 0.9 m up the slope")
    (define v (sqrt (* (/ 10 7) 9.81 (- 0.3063 0.05))))
    (check-= v 1.895 0.001 "the worked speed")
    (check-= (fact-of out "rise ball_2" 'top) v 0.15 (format "the ball's fastest speed: ~a" (facts-line out)))
    (check-true (< (fact-of out "rise ball_2" 'top) (sqrt (* 2 9.81 (- 0.3063 0.05)))) "slower than a sliding block")
    (check-true (< (/ (fact-of out "rise ball_2" 'top) 0.05) 47.1) "under the engine's spin cap, 47.1 rad/s")
    (check-true (> (fact-of out "rise ball_2" 'across) 1) "it rolled on")
    (check-= (fact-of out "rise ball_2" 'value) (- (- 0.3063 0.05)) 0.005 "it came down 0.2563 m")
    (check-true (ormap (λ (l) (string-contains? l "step 3 done: rolled")) out) "the lesson's check passes")
    (check-true (ormap (λ (l) (and (string-contains? l "[Lesson] finished ramp") (string-contains? l "worked out beforehand"))) out) "the lesson says it is done")))

;; ---- Lesson 4: water from tank to tank
;; Worked by hand (game/lessons/04-water.lesson), from the pipe model's flow q = k (h_upper - h_lower) with k = 0.001 m³/s per
;; metre: a 10 litre starter tank (0.05 m², 0.2 m deep) on a 1 m post over an empty one on the ground, a(t) = -0.4 + 0.6 e^(-0.04 t),
;; the upper tank dry at ln(1.5)/0.04 = 10.14 s, all 10 litres moved, the Test over a second later (11.14 s).
(define water-playthrough
  (string-append "wait 30; lesson water; wait 5; "
                 "palette post; click-at 0 0 0; wait 5; "                  ; step 1: the post
                 "palette tank; click-at 0 1.0 0; wait 5; "                ; step 2: the upper tank, on it (10 litres)
                 "palette tank; click-at 0.8 0 0; wait 5; "                ; step 3: the lower, poured out by the lesson
                 "click-port tank_2.outlet; click-port tank_3.inlet; wait 5; "   ; step 4: the pipe, by clicking a dot on each
                 "log; test; wait-test; wait 5; quit"))                   ; step 5: Test

(test-case "Lesson 4 plays through: all ten litres run down into the lower tank, the Test ending when the upper is dry"
  (when (godot-available?)
    (define out (run-editor water-playthrough #:seconds 90))
    (check-equal? (steps-done out 5) '(1 2 3 4 5) "every step completes")
    (check-true (ormap (λ (l) (string-contains? l "(set tank_3 #:water 0)")) out) "the lower tank is poured out")
    (define t-dry (/ (log 1.5) 0.04))
    (check-= t-dry 10.14 0.005 "the worked time to run dry")
    (define facts (facts-line out))
    (check-= (fact-of out "water tank_2" 'value) -10 0.05 (format "the upper tank lost its 10 litres: ~a" facts))
    (check-= (fact-of out "water tank_3" 'value) 10 0.05 "the lower tank gained them")
    (define t (string->number (cadr (regexp-match #px"t=([0-9.]+) by=settled" facts))))
    (check-true (< (+ t-dry 0.9) t (+ t-dry 1.4)) (format "the test ended a second after the water stopped: ~a s" t))
    (check-true (ormap (λ (l) (string-contains? l "step 5 done: level")) out) "the lesson's check passes")
    (check-true (ormap (λ (l) (and (string-contains? l "[Lesson] finished water") (string-contains? l "10 litres more water, 20 cm deep"))) out) "the lesson says it is done")))
