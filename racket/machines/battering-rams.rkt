#lang heroic
;; Three iron rams, each a 2 m pendulum with a 132 kg ball, let go from 30
;; degrees into a post 1.2 m tall that stands where the ball bottoms out,
;; struck 0.8 m above its foot: a slim oak post (8 cm square), a stout one
;; (14 cm) and a limestone column (30 cm across).
;;
;; The ram arrives at about 2.3 m/s (2.30 undamped; before #33 took Godot's
;; 0.1/s damping out it was 2.2), with an effective mass at the ball of
;; I / L^2 = 536.2 / 4 = 134.0 kg. The post bends like a cantilever spring,
;; k = 3 E I / h^3, stops the blow with F = v sqrt(k m), and the moment
;; F h at its foot bends it to a stress F h c / I:
;;   slim oak (E 11 GPa, 90 MPa along the grain): 50.9 MPa per m/s of
;;     blow -- 112 MPa at 2.2 m/s: it snaps (it would take anything over
;;     1.77 m/s). The break takes F_b^2 / 2k = 209 J of the ram's ~325 J;
;;     the ram goes on at about 1.3 m/s.
;;   stout oak: 29.1 MPa per m/s -- 64 MPa: it holds (it'd need 3.09 m/s).
;;   limestone (E 40 GPa, only 5 MPa in tension): 33.7 MPa per m/s -- 74
;;     MPa. Stone is strong only in compression: a blow at 0.15 m/s would
;;     break this column. The break takes just 1.5 J.
(define L (m 2))
(define bob-r (* 0.08 L))
(define pivot-y (+ (m 0.8) L))
(define (post-x px w) (+ px bob-r (mm 5) (/ w 2)))

(define-machine battering-rams
  #:source "the battering ram; Galileo's cantilever (Two New Sciences, 1638)"
  (pendulum slim-ram #:at (-3 pivot-y 0) #:length L #:material iron #:start-angle-deg -30)
  (post slim-oak #:at ((post-x -3 (cm 8)) 0 0) #:size ((cm 8) (m 1.2) (cm 8)) #:material oak #:breakable #t)
  (pendulum stout-ram #:at (0 pivot-y 0) #:length L #:material iron #:start-angle-deg -30)
  (post stout-oak #:at ((post-x 0 (cm 14)) 0 0) #:size ((cm 14) (m 1.2) (cm 14)) #:material oak #:breakable #t)
  (pendulum column-ram #:at (3 pivot-y 0) #:length L #:material iron #:start-angle-deg -30)
  (post column #:at ((post-x 3 (cm 30)) 0 0) #:size ((cm 30) (m 1.2) (cm 30)) #:material limestone #:round #t #:breakable #t))
