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
(define (run-editor script #:seconds [seconds 40] #:errors [errors #f])
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
      (define drain (thread (λ () (let ([e (port->string stderr)]) (when errors (set-box! errors e))))))
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
                 "drag-to light 0.72 0.42 0; wait 5; wait-test; wait 5; " ; step 5, then the lesson's own run (step 6)
                 "log"))

(test-case "Lesson 1 plays through: every step completes and the lesson's own run finds the beam balanced"
  (when (godot-available?)
    (define out (run-editor playthrough))
    (define done (lines-with out "[Lesson] step "))
    (for ([n (in-range 1 7)])
      (check-true (ormap (λ (l) (regexp-match? (pregexp (format "\\[Lesson\\] step ~a done" n)) l)) done)
                  (format "step ~a completes" n)))
    ;; steps 1-3 and 5 set a close placement exactly on its target
    (check-true (ormap (λ (l) (string-contains? l "(move light (0.75 0.4625 0))")) out) "the oak block is set on 0.75 m")
    ;; step 4's Test: the granite end went down to the stop, as worked out
    (define tip (findf (λ (l) (string-contains? l "test seen at step 4")) out))
    (check-true (and tip #t) "the first Test is seen at step 4")
    (check-true (> (number-after tip #px"beam most ([0-9.]+)") 15) (format "the beam tipped to its stop: ~a" tip))
    (check-true (string-contains? tip "heavy=2.7kg") "the sim's granite block weighs 2.7 kg")
    (check-true (string-contains? tip "light=0.72kg") "the sim's oak block weighs 0.72 kg")
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
    (check-true (ormap (λ (l) (regexp-match? #px"state: .*beam@\\(0 0.4 0\\).*heavy@\\(-0.2 0.463 0\\)" l)) again)
                "with the beam and the granite block back where they were")))

;; #180: a placement that doesn't count says why, in the lesson panel's words. At step 2 (a granite
;; block on the green block, 0.2 m left of the pivot) three wrong things in turn, each taken away
;; before the next; the panel's note is printed as "[Lesson] step 2 nudge shown: ..." when it changes.
;; Worked by hand: a block at -0.5 is 0.3 m from the green one at -0.2 (within 0.15 m), so 0.3 m right.
(define wrong-placements
  (string-append "wait 30; lesson see-saw; wait 5; "
                 "palette lever; click-at 0 0 0; wait 5; "                              ; step 1 done: the beam
                 "palette ball; click-at -0.2 0.42 0; wait 5; "                           ; the wrong part, by the palette
                 "log; cmd (remove ball_1); wait 3; "
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

;; the number after "key=" in the facts line of the Test, or #f; `part` is the "rise light" etc.
(define (fact-of out part key)
  (define facts (findf (λ (l) (string-contains? l "[BuildMode] facts: ")) out))
  (and facts
       (let ([m (regexp-match (pregexp (string-append (regexp-quote part) "=(-?[0-9.]+)(?: peak=-?[0-9.]+ low=-?[0-9.]+ across=([0-9.]+) top=([0-9.]+))?")) facts)])
         (and m (case key
                  [(value) (string->number (cadr m))]
                  [(across) (and (caddr m) (string->number (caddr m)))]
                  [(top) (and (cadddr m) (string->number (cadddr m)))])))))

(define (facts-line out) (findf (λ (l) (string-contains? l "[BuildMode] facts: ")) out))
;; the facts line of the Test after the first (a lesson's experiment, #197), or #f
(define (second-facts-line out)
  (define all (filter (λ (l) (string-contains? l "[BuildMode] facts: ")) out))
  (and (>= (length all) 2) (cadr all)))

;; ---- Lesson 2: the pulley
;; Worked by hand (game/lessons/02-pulley.lesson): a granite block is 2.7 kg, an oak one 0.72 kg, so over a
;; fixed pulley a = g (2.7 - 0.72) / (2.7 + 0.72) = 5.68 m/s² and the rope carries 11.15 N. The stone is hung
;; 0.55 m up, so it falls 0.55 m and the wood is hauled up 0.55 m (the rope does not stretch): the wood ends
;; 0.55 m up (0.5472 measured, 0.82 m at its highest on the stone's bounce), the pulley turns 0.55 / (2 pi 0.1) = 0.875
;; of a turn, and the stone lands at sqrt(2 a d) = sqrt(2 5.68 0.55) = 2.50 m/s.
;; The swap (#197), worked by hand: the wood can pull on the rope with only its weight, 0.72 x 9.81 = 7.06 N, and the stone needs
;; 2.7 x 9.81 = 26.49 N to be lifted, so nothing moves (the rope carries 7.06 N; 7.41 N traced while it takes up its slack).
(define pulley-playthrough
  (string-append "wait 30; lesson pulley; wait 5; "
                 "palette post; click-at 0 0 0; wait 5; "                  ; step 1: the post (the lesson slims it)
                 "palette pulley; click-at 0 1.0 0; wait 5; "              ; step 2: the 10 cm pulley the card offers, in oak
                 "palette block; click-at 0.1 0 0; wait 5; "               ; step 3: oak, on the ground to the right
                 "palette block; click-at -0.1 0 0; wait 5; "              ; step 4: granite, to the left
                 "palette rope; click-part heavy; click-part light; wait 5; "   ; step 5: stone, then wood; the lesson threads it
                 "log; test; wait-test; wait 5; "                          ; step 6: Test, and the lesson reads its facts
                 "test; wait-test; wait 5; "                               ; step 7 (#197): the lesson has swapped the blocks; Test again
                 "lesson-leave; lesson-resume pulley; wait 5; log; quit"))

(test-case "Lesson 2 plays through: the rope is threaded over the pulley and the wood rises as far as the stone fell"
  (when (godot-available?)
    (define out (run-editor pulley-playthrough #:seconds 60))
    (check-equal? (steps-done out 6) '(1 2 3 4 5 6) "every step completes")
    (check-true (ormap (λ (l) (string-contains? l "(set post #:size-x 0.06)")) out) "the post is made slim")
    (check-true (ormap (λ (l) (string-contains? l "(move heavy (-0.1 0.6 0))")) out) "the stone is hung 0.55 m up")
    (check-true (ormap (λ (l) (string-contains? l "(rope rope-1 #:from (heavy 0 0.05 0) #:to (light 0 0.05 0) #:length 1.7561 #:over ((-0.1 1.1 0)")) out)
                "the rope runs over the pulley, 1.7561 m of it")
    ;; the Test: the wood rose 0.55 m (within 0.08), the stone fell 0.55 m, the pulley turned 0.875 of a turn, the stone landed at 2.50 m/s
    (check-= (fact-of out "rise light" 'value) 0.55 0.08 (format "the wood rose 0.55 m: ~a" (facts-line out)))
    (check-= (fact-of out "rise heavy" 'value) -0.55 0.01 "the stone fell 0.55 m")
    (check-= (fact-of out "turns pulley" 'value) (/ 0.55 (* 2 pi 0.1)) 0.06 "the pulley turned 0.875 of a turn")
    (check-= (fact-of out "rise heavy" 'top) (sqrt (* 2 (/ (* 9.81 (- 2.7 0.72)) (+ 2.7 0.72)) 0.55)) 0.15 "the stone landed at 2.50 m/s")
    (check-true (ormap (λ (l) (string-contains? l "step 6 done: lifted")) out) "the lesson's check passes")
    ;; the experiment: the blocks swapped by the lesson, the rope tied again with its ends exchanged
    (check-true (ormap (λ (l) (string-contains? l "[Lesson] set for the next step: (remove rope-1) ; (move heavy (0.1 0.05 0)) ; (move light (-0.1 0.6 0))")) out)
                "the lesson swaps the blocks after the first Test")
    (check-true (ormap (λ (l) (string-contains? l "(rope rope-1 #:from (light 0 0.05 0) #:to (heavy 0 0.05 0) #:length 1.7561 #:over ((-0.1 1.1 0)")) out)
                "the same rope, tied again from the wood to the stone")
    (define swapped (second-facts-line out))
    (check-true (and swapped #t) "the swapped rig is tested")
    (check-false (regexp-match? #px"rise (light|heavy)=" swapped) (format "nothing moved: ~a" swapped))
    (check-= (let ([m (regexp-match #px"rope rope-1 max=([0-9.]+)" swapped)]) (string->number (cadr m))) (* 0.72 9.81) 0.5 "the rope carries the wood's weight, 7.06 N")
    (check-equal? (steps-done out 7) '(1 2 3 4 5 6 7) "the swap step completes too")
    (check-true (ormap (λ (l) (and (string-contains? l "[Lesson] finished pulley") (string-contains? l "The stone stayed on the ground") (string-contains? l "5.68 metres a second"))) out)
                "the lesson says it is done, with the experiment's result")
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
                 "cmd (remove pulley_1); wait 3; "
                 "palette pulley-10cm; click-at 0.02 1.0 0; wait 5; "     ; (a click on the same pixel as before sends no mouse move, so the ghost wouldn't follow)
                 "palette block; click-at 0.1 0 0; wait 5; "
                 "palette block; click-at -0.1 0 0; wait 5; "
                 "palette rope; click-part pulley; click-part heavy; wait 5; "     ; rope: pulley to stone
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
;; The block (#197), worked by hand: oak on limestone has the friction number 0.45, so a block holds while tan(angle) < 0.45, up to
;; atan(0.45) = 24.2 degrees; the ramp is 15 degrees, tan 15 = 0.268: it holds. (Traced with the angle changed: still at 22 degrees,
;; sliding at 25: 0.40 m down, 0.53 m/s.)
(define ramp-playthrough
  (string-append "wait 30; lesson ramp; wait 5; "
                 "palette ramp; click-at 0 0 0; wait 5; "                  ; step 1: the ramp
                 "palette ball; click-at 0 0.3 -0.85; wait 5; "            ; step 2: the ball, near the top (set exactly on the slope)
                 "log; test; wait-test; wait 5; "                          ; step 3: Test
                 "palette block; click-at 0.15 0.3 -0.85; wait 5; "         ; step 4 (#197): an oak block beside the ball, laid flat on the slope by the lesson
                 "log; test; wait-test; wait 5; quit"))                    ; step 5: Test again

(test-case "Lesson 3 plays through: the ball rolls at sqrt(10/7 g h), not the sliding block's sqrt(2 g h)"
  (when (godot-available?)
    (define out (run-editor ramp-playthrough #:seconds 90))
    (check-equal? (steps-done out 3) '(1 2 3) "every step completes")
    (check-true (ormap (λ (l) (string-contains? l "(move ball (0 0.3063 -0.8497))")) out) "the ball is set 0.9 m up the slope")
    (define v (sqrt (* (/ 10 7) 9.81 (- 0.3063 0.05))))
    (check-= v 1.895 0.001 "the worked speed")
    (check-= (fact-of out "rise ball" 'top) v 0.15 (format "the ball's fastest speed: ~a" (facts-line out)))
    (check-true (< (fact-of out "rise ball" 'top) (sqrt (* 2 9.81 (- 0.3063 0.05)))) "slower than a sliding block")
    (check-true (< (/ (fact-of out "rise ball" 'top) 0.05) 47.1) "under the engine's spin cap, 47.1 rad/s")
    (check-true (> (fact-of out "rise ball" 'across) 1) "it rolled on")
    (check-= (fact-of out "rise ball" 'value) (- (- 0.3063 0.05)) 0.005 "it came down 0.2563 m")
    (check-true (ormap (λ (l) (string-contains? l "step 3 done: rolled")) out) "the lesson's check passes")
    (check-= (* (/ 180 pi) (atan 0.45)) 24.2 0.05 "the worked angle at which oak lets go on limestone")
    (check-true (< (tan (* 15 (/ pi 180))) 0.45) "a 15 degree slope is gentler than that")
    (check-true (ormap (λ (l) (string-contains? l "(set block #:tilt-deg 15)")) out) "the lesson lays the block flat on the slope")
    (check-true (ormap (λ (l) (string-contains? l "(move block (0.15 0.3063 -0.8497))")) out) "beside the ball, 0.15 m to the side")
    (check-equal? (steps-done out 5) '(1 2 3 4 5) "every step completes, the block's too")
    (check-false (regexp-match? #px"rise block=" (second-facts-line out)) (format "the block held still: ~a" (second-facts-line out)))
    (check-true (ormap (λ (l) (and (string-contains? l "[Lesson] finished ramp") (string-contains? l "The block held still while the ball rolled away"))) out) "the lesson says it is done")))

;; ---- Lesson 4: water from tank to tank
;; Worked by hand (game/lessons/04-water.lesson), from the pipe model's flow q = k (h_upper - h_lower) with k = 0.001 m³/s per
;; metre: a 10 litre starter tank (0.05 m², 0.2 m deep) on a 1 m post over an empty one on the ground, a(t) = -0.4 + 0.6 e^(-0.04 t),
;; the upper tank dry at ln(1.5)/0.04 = 10.14 s, all 10 litres moved, the Test over a second later (11.14 s).
;; Uphill (#197), worked by hand: the lower tank on a 2 m post has its floor at 2.0 m, above the 1.2 m the upper tank's water reaches, and
;; it holds nothing to send down, so q = 0: no water moves, and the Test ends at its 4 s minimum (4.01 s traced).
(define water-playthrough
  (string-append "wait 30; lesson water; wait 5; "
                 "palette post; click-at 0 0 0; wait 5; "                  ; step 1: the post
                 "palette tank; click-at 0 1.0 0; wait 5; "                ; step 2: the upper tank, on it (10 litres)
                 "palette tank; click-at 0.8 0 0; wait 5; "                ; step 3: the lower, poured out by the lesson
                 "click-port upper.outlet; click-port lower.inlet; wait 5; "   ; step 4: the pipe, by clicking a dot on each
                 "log; test; wait-test; wait 5; "                         ; step 5: Test
                 "test; wait-test; wait 5; log; quit"))                   ; step 6 (#197): the lesson has stood the lower tank on a tall post; Test again

(test-case "Lesson 4 plays through: all ten litres run down into the lower tank, the Test ending when the upper is dry"
  (when (godot-available?)
    (define out (run-editor water-playthrough #:seconds 90))
    (check-equal? (steps-done out 5) '(1 2 3 4 5) "every step completes")
    (check-true (ormap (λ (l) (string-contains? l "(set lower #:water 0)")) out) "the lower tank is poured out")
    (define t-dry (/ (log 1.5) 0.04))
    (check-= t-dry 10.14 0.005 "the worked time to run dry")
    (define facts (facts-line out))
    (check-= (fact-of out "water upper" 'value) -10 0.05 (format "the upper tank lost its 10 litres: ~a" facts))
    (check-= (fact-of out "water lower" 'value) 10 0.05 "the lower tank gained them")
    (define t (string->number (cadr (regexp-match #px"t=([0-9.]+) by=settled" facts))))
    (check-true (< (+ t-dry 0.9) t (+ t-dry 1.4)) (format "the test ended a second after the water stopped: ~a s" t))
    (check-true (ormap (λ (l) (string-contains? l "step 5 done: level")) out) "the lesson's check passes")
    ;; the experiment
    (check-true (ormap (λ (l) (string-contains? l "[Lesson] set for the next step: (move lower (0.8 2 0)) ; (post tower #:at (0.8 0 0) #:size-y 2")) out)
                "the lesson stands the lower tank on a tall post after the first Test")
    (define uphill (second-facts-line out))
    (check-true (and uphill #t) "the raised rig is tested")
    (check-false (string-contains? uphill "water ") (format "no water moved: ~a" uphill))
    (check-true (< (string->number (cadr (regexp-match #px"t=([0-9.]+) by=settled" uphill))) 5) "the Test ends at its 4 s minimum")
    (check-equal? (steps-done out 6) '(1 2 3 4 5 6) "the uphill step completes")
    (check-true (ormap (λ (l) (and (string-contains? l "[Lesson] finished water") (string-contains? l "No water moved: the upper tank kept its 10 litres"))) out) "the lesson says it is done")))

;; ---------------------------------------------------------------------------------------------
;; "Build five machines" (docs/plans/tutorial-plan.md), after the four basics in the Lessons menu.

;; the end speed of a wheel in the Test's facts line ("turns wheel_4=29.8188 peak=29.8188 rpm=60.0"), or #f
(define (rpm-of out wheel)
  (define facts (facts-line out))
  (and facts (let ([m (regexp-match (pregexp (string-append "turns " (regexp-quote wheel) "=-?[0-9.]+ peak=[0-9.]+ rpm=(-?[0-9.]+)")) facts)])
               (and m (string->number (cadr m))))))

;; ---- Lesson 5: crank and gears
;; Worked by hand (game/lessons/05-gears.lesson): module-5 gears, pitch radii 0.18 / 0.045 / 0.09 m, so "Add a meshing gear" puts
;; the 18 0.225 m from the 72 and the 36 0.135 m from the 18. The crank turns the 72 at 30 rpm: the 18 at 30 x 72/18 = 120 rpm the
;; other way, the 36 at 120 x 18/36 = 60 rpm the crank's way, 72:36 = 2:1. In the Test's 30 s, 15, 60 and 30 turns less the crank's
;; 0.36 s start (5 N.m on the bronze 72's 0.575 kg.m2): 14.91, 59.64, 29.82 (the lesson's header cites this trace).
;; Played by the mouse words alone: the palette's Post and Gear (the card's 72 picked by the lesson), clicks on the targets, the
;; inspector's "Add a meshing gear" (a real click on the lit button) for the 18 and the 36, and Test.
(define gears-playthrough
  (string-append "wait 30; lesson-menu; lesson gears; wait 5; "
                 "palette-click post; wait 5; click-at 0 0 0; wait 5; "            ; step 1: the 1 m post (the card's first size)
                 "palette-click gear; wait 5; click-at 0 1.0 0; wait 5; log; "     ; step 2: the 72 the card offers, on the post's top; the lesson fits the crank
                 "mesh-with 18; wait 10; "                                         ; step 3: the crank is selected by the lesson; its lit button, an 18
                 "select second; wait 5; mesh-with 36; wait 10; log; "            ; step 4: the 18's button, a 36 on its far side
                 "test; wait-test; wait 5; quit"))                                  ; step 5: Test, 30 s

(test-case "Lesson 5 plays through by the mouse: a crank turns three gears and the last runs at 60 rpm, 2:1"
  (when (godot-available?)
    (define out (run-editor gears-playthrough #:seconds 120))
    (check-true (ormap (λ (l) (string-contains? l "[Lesson] menu: Lesson 1: the see-saw | Lesson 2: the pulley | Lesson 3: the ramp and the ball | Lesson 4: water from tank to tank | == Build five machines | Lesson 5: crank and gears")) out)
                "the menu: the four basics, the heading, then lesson 5")
    (check-equal? (steps-done out 5) '(1 2 3 4 5) "every step completes")
    (check-true (ormap (λ (l) (string-contains? l "(wheel crank #:at (0 1.18 0) #:catalogue involute-gear-m5-72")) out) "the card offered the 72")
    (check-true (ormap (λ (l) (string-contains? l "step 2 done: set on its target (move crank (0 1.19 0)) ; (set crank #:drive-rpm 30) ; (set crank #:drive-torque 5)")) out)
                "the 72 is set on 1.19 m and made a crank")
    (check-true (ormap (λ (l) (regexp-match? #px"state: .*selected=crank .*crank@\\(0 1.19 0\\)" l)) out) "the lesson selected the crank for step 3")
    (check-true (ormap (λ (l) (string-contains? l "[Lesson] step 3 lit button mesh_with:crank")) out) "the crank's Add a meshing gear is lit")
    (check-true (ormap (λ (l) (string-contains? l "[Lesson] step 4 lit button mesh_with:second")) out) "then the 18's")
    (check-true (ormap (λ (l) (string-contains? l "mesh: crank, second: centre distance 0.225 m (pitch radii 0.18 + 0.045), they stand 0.225 m apart: on it")) out) "the 18 on its circle")
    (check-true (ormap (λ (l) (string-contains? l "mesh: second, third: centre distance 0.135 m (pitch radii 0.045 + 0.09), they stand 0.135 m apart: on it")) out) "the 36 on its circle")
    (check-true (ormap (λ (l) (string-contains? l "[BuildMode] train: crank -> second, ratio 4:1 (1 mesh)")) out) "the live ratio after the 18: 4:1")
    (check-true (ormap (λ (l) (string-contains? l "[BuildMode] train: crank -> third, ratio 2:1 (2 meshes)")) out) "and after the 36: 2:1")
    ;; the Test: the 36 at 60 rpm (within 2), the 18 at 120 the other way, the crank at its 30; and the turns
    (check-= (rpm-of out "third") 60 2 (format "the 36 at 60 rpm: ~a" (facts-line out)))
    (check-= (rpm-of out "second") -120 2 "the 18 at 120 rpm, the other way")
    (check-= (rpm-of out "crank") 30 0.5 "the crank at 30 rpm")
    (check-= (fact-of out "turns third" 'value) 29.82 0.05 "the 36 turned 29.82 times (30 less the crank's start)")
    (check-= (fact-of out "turns second" 'value) -59.64 0.1 "the 18 59.64 times, the other way")
    (check-false (ormap (λ (l) (string-contains? l "spinning at Jolt's limit")) out) "nothing near the engine's spin limit")
    (check-true (ormap (λ (l) (string-contains? l "step 5 done: turned")) out) "the lesson's check passes")
    (check-true (ormap (λ (l) (and (string-contains? l "[Lesson] finished gears") (string-contains? l "The small gear turned at 120 rpm and the last gear at 60 rpm"))) out)
                "the lesson says it is done, with the traced speeds")))

;; the number after "rise PART=" in a facts line, or #f
(define (rise-in facts part)
  (and facts (let ([m (regexp-match (pregexp (string-append "rise " (regexp-quote part) "=(-?[0-9.]+)")) facts)])
               (and m (string->number (cadr m))))))

;; ---- Lesson 6: the windlass
;; Worked by hand (game/lessons/06-windlass.lesson): a 10 cm granite block, 2.7 kg, hung from a 15 cm drum turned at 3 rpm (0.31416
;; rad/s) by a hand on a 1.2 m wheel on the drum's axle: the rope winds in 0.15 x 0.31416 = 0.04712 m/s, 1.4137 m in the Test's 30 s,
;; less a millimetre for the 0.04 s start (40 N.m against the block's 3.97): 1.413 m. The hand at the wheel's rim needs 3.97 / 0.6 =
;; 6.6 N, a quarter of the block's 26.5 N. The rope: 3.22 - (0.35 + 0.05) = 2.82 m, the block hung 0.3 m up under the drum's +x side.
;; Played by the mouse words: the palette's Post (the lesson has pressed its card's 3 m), the drum clicked on the post's top, the
;; drum's lit "Put a wheel on this shaft" (the lesson picked Wheel in its Which list; the card's 120 cm), Block, the Rope tool's two
;; clicks, and Test.
(define windlass-playthrough
  (string-append "wait 30; lesson windlass; wait 5; "
                 "palette-click post; wait 5; click-at 0 0 0; wait 5; "              ; step 1: the 3 m post, slimmed by the lesson
                 "palette-click drum; wait 5; click-at 0 3.0 0; wait 5; "            ; step 2: the drum on its top, set on 3.22
                 "gear-on cart-wheel-120cm; wait 10; log; "                          ; step 3: the drum's button, the wheel at its default spot
                 "palette-click block; wait 5; click-at 0.15 0 0; wait 5; "          ; step 4: the block under the drum's edge
                 "palette-click rope; wait 5; click-part drum; click-part load; wait 5; log; "   ; step 5: the lesson winds it on the drum
                 "test; wait-test; wait 5; quit"))                                   ; step 6: Test, 30 s

(test-case "Lesson 6 plays through by the mouse: the hand's wheel turns the drum and winds the block up 1.41 m"
  (when (godot-available?)
    (define out (run-editor windlass-playthrough #:seconds 120))
    (check-equal? (steps-done out 6) '(1 2 3 4 5 6) "every step completes")
    (check-true (ormap (λ (l) (string-contains? l "(post post #:at (0 0 0) #:material oak #:size-x 0.3 #:size-y 3 #:size-z 0.3)")) out) "the card's 3 m, pressed by the lesson")
    (check-true (ormap (λ (l) (string-contains? l "step 1 done: set on its target (set post #:size-x 0.15) ; (set post #:size-z 0.15)")) out) "made slim")
    (check-true (ormap (λ (l) (string-contains? l "(wheel drum #:at")) out) "the drum is named by its role as it is placed")
    (check-true (ormap (λ (l) (string-contains? l "[Lesson] step 3 lit button gear_on:drum")) out) "the drum's Put a wheel on this shaft is lit")
    (check-true (ormap (λ (l) (string-contains? l "[BuildMode] wheel on drum's shaft: placing cart-wheel-120cm, the ghost 0.386 m behind drum")) out)
                "the wheel starts clear of the 0.6 m drum: 0.3 + 0.036 + 0.05")
    (check-true (ormap (λ (l) (string-contains? l "step 3 done: set on its target (move handle (0 3.22 -0.4)) ; (set drum #:drive-rpm 3) ; (set drum #:drive-torque 40)")) out)
                "the hand's drive on the drum, the axle's first wheel")
    (check-false (ormap (λ (l) (string-contains? l "step 3 nudge shown")) out) "no nudge between the wheel and its axle: the lesson looks once both are in")
    (check-true (ormap (λ (l) (string-contains? l "step 5 done: set on its target (remove rope-1) ; (move load (0.15 0.35 0)) ; (rope rope-1 #:from (drum 0 0 0) #:to (load 0 0.05 0) #:length 2.82 #:wind-on drum")) out)
                "the rope wound on the drum, the block hung 0.3 m up under its winding side")
    (define facts (facts-line out))
    (check-= (rise-in facts "load") 1.413 0.01 (format "the block rose 1.413 m: ~a" facts))
    (check-= (rpm-of out "drum") 3 0.1 "the drum at the hand's 3 rpm")
    (check-true (string-contains? facts "broke=no") "the rope held")
    (check-false (ormap (λ (l) (string-contains? l "spinning at Jolt's limit")) out) "nothing near the engine's spin limit")
    (check-true (ormap (λ (l) (string-contains? l "step 6 done: lifted")) out) "the lesson's check passes")
    (check-true (ormap (λ (l) (and (string-contains? l "[Lesson] finished windlass") (string-contains? l "The block rose 1.41 m"))) out)
                "the lesson says it is done, with the traced rise")))

;; ---- Lesson 7: the treadwheel crane
;; Worked by hand (game/lessons/07-crane.lesson): a 0.5 m granite stone, 337.5 kg, 3,311 N, on a 30 cm drum: 993 N.m. Two 70 kg walkers
;; 30 degrees up a 4 m treadwheel's side turn it with 2 x 70 x 9.81 x 2 x sin 30 = 1,373 N.m, to 1 rpm in 0.35 s: the stone rises at
;; 0.3 x 0.10472 = 0.0314 m/s, 0.9425 m in 30 s less 5.5 mm for the start and 2 mm of stretch, 0.935 m. One walker (687 N.m) loses:
;; the stone, hung 0.3 m up, runs the wheel back and lands in 2.8 s, rise -0.3. The rope: 4.1193 off the drum to the pulley, 0.6123
;; round it, 2.41 down: 7.1416 m.
;; Played by the mouse words, then the done text's experiment: the treadwheel's drive-torque field typed 687, and Test again.
(define crane-playthrough
  (string-append "wait 30; lesson crane; wait 5; "
                 "palette-click treadwheel; wait 5; click-at 0 0 0; wait 5; "                 ; step 1: the wheel, set 2.4 m up, two walkers in it
                 "gear-on drum-30cm; wait 10; "                                               ; step 2: the drum on its axle, 1.25 m back
                 "palette-click post; wait 5; click-at -4 0 -1.25; wait 5; "                  ; step 3: the jib post
                 "palette-click pulley; wait 5; click-at -4.12 3.0 -1.25; wait 5; "           ; step 4: the pulley at its top's edge
                 "palette-click block; wait 5; click-at -4.45 0 -1.25; wait 5; "              ; step 5: the stone, made 0.5 m by the lesson
                 "palette-click rope; wait 5; click-part drum; click-part stone; wait 5; "    ; step 6: wound on the drum, over the pulley
                 "test; wait-test; wait 5; "                                                  ; step 7: Test
                 "select treadwheel; wait 5; field drive-torque 687; wait 5; test; wait-test; wait 5; quit"))   ; one walker

(test-case "Lesson 7 plays through by the mouse: two walkers lift the 337.5 kg stone 0.94 m; one walker loses it to the ground"
  (when (godot-available?)
    (define out (run-editor crane-playthrough #:seconds 170))
    (check-equal? (steps-done out 7) '(1 2 3 4 5 6 7) "every step completes")
    (check-true (ormap (λ (l) (string-contains? l "step 1 done: set on its target (move treadwheel (0 2.4 0)) ; (set treadwheel #:drive-rpm 1) ; (set treadwheel #:drive-torque 1373)")) out)
                "the wheel 2.4 m up, two walkers in it")
    (check-true (ormap (λ (l) (string-contains? l "[BuildMode] drum on treadwheel's shaft: placing drum-30cm, the ghost 1.25 m behind treadwheel")) out)
                "the drum starts clear of the 1.2 m wheel: 0.6 + 0.6 + 0.05")
    (check-true (ormap (λ (l) (string-contains? l "(post jib #:at (-4 0 -1.25) #:material oak #:size-x 0.3 #:size-y 3 #:size-z 0.3)")) out) "the jib, the card's 3 m")
    (check-true (ormap (λ (l) (string-contains? l "step 5 done: set on its target (set stone #:size 0.5) ; (move stone (-4.45 0.25 -1.25))")) out) "the stone made 0.5 m")
    (check-true (ormap (λ (l) (string-contains? l "(rope rope-1 #:from (drum 0 0 0) #:to (stone 0 0.25 0) #:length 7.1416 #:wind-on drum #:over ((-4.05 3.21 -1.25) (-4.1086 3.3514 -1.25) (-4.25 3.41 -1.25) (-4.3914 3.3514 -1.25) (-4.45 3.21 -1.25)) #:turns pulley")) out)
                "the rope wound on the drum and over the pulley's top, 7.1416 m")
    (check-true (ormap (λ (l) (string-contains? l "masses treadwheel=424.16kg drum=265.785kg pulley=96.593kg stone=337.5kg")) out) "the Test's masses, as worked")
    (define facts (filter (λ (l) (string-contains? l "[BuildMode] facts: ")) out))
    (check-equal? (length facts) 2 "two Tests")
    (check-= (rise-in (car facts) "stone") 0.935 0.01 (format "two walkers lift the stone 0.935 m: ~a" (car facts)))
    (check-true (string-contains? (car facts) "broke=no") "the rope held")
    (check-true (ormap (λ (l) (string-contains? l "step 7 done: lifted")) out) "the lesson's check passes")
    (check-true (ormap (λ (l) (string-contains? l "> (set treadwheel #:drive-torque 687)")) out) "one walker, typed into the field")
    (check-= (rise-in (cadr facts) "stone") -0.3 0.01 (format "one walker: the stone runs the wheel back and lands, 0.3 m down: ~a" (cadr facts)))
    (check-true (regexp-match? #px"t=[0-9.]+ by=settled" (cadr facts)) "and everything stops there")
    (check-false (ormap (λ (l) (string-contains? l "spinning at Jolt's limit")) out) "nothing near the engine's spin limit")
    (define finished (filter (λ (l) (string-contains? l "[Lesson] finished crane")) out))
    (check-true (and (pair? finished) (andmap (λ (l) (string-contains? l "The stone rose 0.94 m")) finished))
                "the done text keeps the two walkers' rise after the one-walker Test")))

;; the numbers of "gen ID ..." in the facts line, as an assoc of key -> number ("since" #f when never), or #f
(define (gen-of out id)
  (define facts (facts-line out))
  (define m (and facts (regexp-match (pregexp (string-append "gen " (regexp-quote id) " rpm=([0-9.]+) watts=([0-9.]+) delivered=([0-9.]+) most=([0-9.]+) wh=([0-9.]+) since=([0-9.]+|never)")) facts)))
  (and m (map cons '(rpm watts delivered most wh since) (map string->number (cdr m)))))

;; ---- Lesson 8: the mill
;; Worked by hand (game/lessons/08-mill.lesson): the workshop's 6 m/s wind on the palette's 10 m sails, tau = 8,171 (2 - w / 1.5) N.m;
;; the millstones hold 11,440, so the sails settle at 0.9 rad/s; 50,000 + 2,667 kg.m2 reflected through the gears, a time constant 9.67 s;
;; at 30 s the sails at 0.8596 rad/s (8.21 rpm), the last pinion at 256 x that, 2,101.3 rpm; the sails' shaft turned 2.9745 times.
;; Played by the mouse words: the palette's Post (its card's 10.6 m pressed by the lesson), Windmill on the tower's top, the lit "Add a
;; stage" four times (each stage named by the lesson's roles as it is built), and Test.
(define mill-playthrough
  (string-append "wait 30; lesson mill; wait 5; "
                 "palette-click post; wait 5; click-at 0.45 0 -1.45; wait 5; "       ; step 1: the tower
                 "palette-click windmill; wait 5; click-at 0.45 10.6 -1.45; wait 5; " ; step 2: the sails on its top, the millstones set
                 "add-stage; wait 10; add-stage; wait 10; add-stage; wait 10; add-stage; wait 10; log; "   ; steps 3-6
                 "test; wait-test; wait 5; quit"))                                    ; step 7: Test, 30 s

(test-case "Lesson 8 plays through by the mouse: four stages, 256:1, the last pinion at 2,100 rpm under the millstones"
  (when (godot-available?)
    (define out (run-editor mill-playthrough #:seconds 150))
    (check-equal? (steps-done out 7) '(1 2 3 4 5 6 7) "every step completes")
    (check-true (ormap (λ (l) (string-contains? l "(post post #:at (0.45 0 -1.45) #:material oak #:size-x 0.6 #:size-y 10.6 #:size-z 0.6)")) out) "the card's 10.6 m")
    (check-true (ormap (λ (l) (string-contains? l "(windmill sails #:at (0 11.05 -0) #:material oak)")) out) "the sails on the tower's top, in oak")
    (check-true (ormap (λ (l) (string-contains? l "step 2 done: set on its target (set sails #:load 11440)")) out) "the millstones")
    (for ([w '("wheel-a" "wheel-b" "wheel-c" "wheel-d")] [p '("pinion-b" "pinion-c" "pinion-d" "pinion-e")] [n '(3 4 5 6)])
      (check-true (ormap (λ (l) (and (string-contains? l (format "(wheel ~a " w)) (string-contains? l (format "(wheel ~a " p)))) out)
                  (format "stage ~a: ~a and ~a, named as built" (- n 2) w p))
      (check-true (ormap (λ (l) (string-contains? l (format "[Lesson] step ~a lit button add_stage:" n))) out) (format "step ~a's Add a stage lit" n)))
    (check-true (ormap (λ (l) (string-contains? l "[BuildMode] train: sails -> pinion-e, ratio 256:1 (4 meshes)")) out) "the live ratio, in the lesson's words")
    (check-= (rpm-of out "pinion-e") 2101.3 2 (format "the last pinion at 2,101.3 rpm: ~a" (facts-line out)))
    (check-= (fact-of out "turns wheel-a" 'value) 2.9745 0.01 "the sails' shaft turned 2.9745 times")
    (check-= (/ (fact-of out "turns pinion-e" 'value) (fact-of out "turns wheel-a" 'value)) 256 0.5 "256 turns of the pinion a turn of the sails")
    (check-false (ormap (λ (l) (string-contains? l "spinning at Jolt's limit")) out) "nothing near the engine's spin limit")
    (check-true (ormap (λ (l) (string-contains? l "step 7 done: turned, ratio")) out) "both of the Test's checks pass")
    (check-true (ormap (λ (l) (and (string-contains? l "[Lesson] finished mill") (string-contains? l "The last pinion ended at 2101 rpm, and over the Test it turned 256 times"))) out)
                "the lesson says it is done, with the traced speed and ratio")))

;; ---- Lesson 9: mill to generator
;; Worked by hand (game/lessons/09-generator.lesson): no millstones, three stages (64:1); the generator cuts in at 1,500 rpm, the sails
;; then at 2.454 rad/s, reached at 15.70 s (time constant 9.21 s); past it it holds the sails back, and at 30 s its rotor is at 1,750.2
;; rpm putting 440.2 W into the bank (its cells in the heat store at 20 C, inside 0-45 C).
;; Played by the mouse words: Post, Windmill, three Add a stage, the last pinion's "Put a generator on this shaft" (the lesson picked
;; Generator in the Which list), Heat store and Battery bank on the ground (the step's own view), and Test.
(define generator-playthrough
  (string-append "wait 30; lesson generator; wait 5; "
                 "palette-click post; wait 5; click-at 0.45 0 -1.45; wait 5; "
                 "palette-click windmill; wait 5; click-at 0.45 10.6 -1.45; wait 5; "
                 "add-stage; wait 10; add-stage; wait 10; add-stage; wait 10; "         ; steps 3-5
                 "gear-on generator; wait 10; "                                       ; step 6: the generator on pinion-d's shaft
                 "palette-click heat-store; wait 5; click-at 2.5 0 -3; wait 5; "      ; step 7: the cells' store
                 "palette-click battery-bank; wait 5; click-at 3.5 0 -3; wait 5; log; "   ; step 8: the bank, in it, charged by the generator
                 "test; wait-test; wait 5; quit"))                                    ; step 9: Test

(test-case "Lesson 9 plays through by the mouse: the generator on the last pinion charges the bank at 440 W at 1,750 rpm"
  (when (godot-available?)
    (define out (run-editor generator-playthrough #:seconds 170))
    (check-equal? (steps-done out 9) '(1 2 3 4 5 6 7 8 9) "every step completes")
    (check-true (ormap (λ (l) (string-contains? l "[Lesson] step 6 lit button gear_on:pinion-d")) out) "the pinion's Put a generator on this shaft is lit")
    (check-true (ormap (λ (l) (string-contains? l "[BuildMode] generator on pinion-d's shaft: placing generator, the ghost 0.24 m behind pinion-d")) out)
                "the can starts clear of the pinion: 0.02 + 0.17 + 0.05")
    (check-true (ormap (λ (l) (string-contains? l "(begin (generator generator #:at (0.675 11.05 -1.74) #:material iron) (set generator #:on pinion-d))")) out)
                "one edit: the generator turned by the pinion")
    (check-true (ormap (λ (l) (string-contains? l "step 8 done: set on its target (set bank #:in cells) ; (set generator #:charges bank)")) out) "the bank in the store, charged by the generator")
    (check-true (ormap (λ (l) (string-contains? l "[BuildMode] train: sails -> pinion-d, ratio 64:1 (3 meshes)")) out) "64:1")
    (define gen (gen-of out "generator"))
    (check-true (and gen #t) (format "the Test saw the generator: ~a" (facts-line out)))
    (check-= (cdr (assq 'rpm gen)) 1750.2 2 "its rotor at 1,750.2 rpm")
    (check-= (cdr (assq 'delivered gen)) 440.2 3 "440 W into the bank")
    (check-= (cdr (assq 'since gen)) 15.70 0.1 "charging from 15.70 s, when the rotor passed 1,500 rpm")
    (check-= (abs (rpm-of out "pinion-d")) 1750.2 2 "the last pinion at the generator's speed")
    (check-true (ormap (λ (l) (string-contains? l "The generator charged the bank at 440 W at the end, its rotor at 1,750 rpm (from 15.7 s).")) out) "the Test says so in a sentence")
    (check-false (ormap (λ (l) (string-contains? l "spinning at Jolt's limit")) out) "nothing near the engine's spin limit")
    (check-true (ormap (λ (l) (string-contains? l "step 9 done: charging, turned")) out) "both of the Test's checks pass")
    (check-true (ormap (λ (l) (and (string-contains? l "[Lesson] finished generator") (string-contains? l "The generator ended at 1750 rpm, charging the battery at 440 W."))) out)
                "the lesson says it is done, with the traced numbers")))

;; #270: build mode's frozen preview of a windmill-driven gear train once threw a NullReferenceException in
;; DrawGearTrains (a drawing path, so only the engine's error output shows it, not the lesson's lines).
(test-case "Meshing stages onto a windmill logs no NullReferenceException (#270)"
  (when (godot-available?)
    (define errors (box ""))
    (define out
      (run-editor (string-append "wait 30; cmd (post post #:at (0.45 0 -1.45) #:material oak #:size-x 0.6 #:size-y 10.6 #:size-z 0.6); "
                                 "cmd (windmill sails #:at (0 11.05 0) #:material oak); select sails; wait 3; add-stage; wait 5; add-stage; wait 5; log; quit")
                  #:errors errors))
    (check-true (ormap (λ (l) (string-contains? l "meshed")) out) "the stages were meshed")
    (check-false (string-contains? (string-append (string-join out "\n") (unbox errors)) "NullReferenceException")
                 "no NullReferenceException in the output")))
