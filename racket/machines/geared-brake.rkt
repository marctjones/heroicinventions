#lang heroic
;; A gear train turned by a flywheel and loaded by a brake (issue #113): the
;; check that a mesh carries torque both ways. Nothing cranks these trains;
;; each is let go turning at speed (the flywheel at 60 rpm, the brake shaft
;; at 600 the other way) and only the brake at the far end slows it.
;;
;; The issue's case is 60 rpm, so 600 at the brake: 62.8 rad/s. Jolt in Godot
;; once let a body spin no faster than 47.1 rad/s, which clamped the brake
;; shaft and broke the ratio, so this train ran at 36 rpm for a while (#113).
;; Since #187 the limit is 314.16 rad/s (100 pi), and the train runs the
;; issue's case.
;;
;; Each train: an iron flywheel keyed with a 100-tooth oak gear on one
;; arbor, meshing a 10-tooth iron pinion on another, keyed to an iron brake
;; drum, so the brake shaft turns 10 times as fast, the other way. The brake
;; is the drum's bearing: dry friction mu N r on a 1 cm pin, N the weight of
;; drum and pinion, mu set so it holds exactly 0.1 N m.
;;
;; The brake shaft turns 10 times as fast, so it reflects 10 x 0.1 = 1 N m
;; onto the flywheel's arbor (with a mesh of efficiency eta, 1/eta more:
;; 1.11 N m at 0.9). The flywheel's width is chosen so the whole train, seen
;; from its arbor, is I = 1 kg m^2: the flywheel and big gear, I_1, plus
;; 100 x the brake shaft's I_2. The speed falls in a straight line, 1 rad/s
;; every second, to a stop at
;;   t = I w0 / tau = 1 x 6.283 / 1 = 6.283 s             (eta = 1, "plain")
;;   t = (eta I_1 + n^2 I_2) w0 / (n tau) = 5.669 s       (eta = 0.9, "lossy")
;; with n = 10, I_1 = 0.9772 and I_2 = 0.000228: the mesh loses its share
;; only of the power through it; the brake shaft's own spin is spent at the
;; brake without passing through it.
;;
;; The drum leads the brake shaft's arbor (it carries the bearing and the
;; pinion rides on it): led by the light pinion, with the long drum hanging
;; off it, the arbor wobbled off its axle at 600 rpm and flew apart.
(require racket/math racket/list)

(define md (mm 4))                                     ; module: the 100-tooth gear's pitch radius is 20 cm, the pinion's 2 cm
(define big (spur-gear #:teeth 100 #:module md #:width (cm 1)))
(define pinion (spur-gear #:teeth 10 #:module md #:width (cm 3)))
(define brake-drum (drum #:radius (cm 1.5) #:length (cm 20)))
(define (rho mat) (case mat [(iron) 7700] [(oak) 720]))
(define (I-of s mat) (* (rho mat) (vector-ref (shape-inertia s) 2)))
(define (mass-of s mat) (* (rho mat) (shape-volume s)))

;; the brake shaft: an iron drum and pinion; the brake holds 0.1 N m on a 1 cm pin
(define I-brake (+ (I-of pinion 'iron) (I-of brake-drum 'iron)))
(define N-brake (* 9.81 (+ (mass-of pinion 'iron) (mass-of brake-drum 'iron))))
(define pin (cm 1))
(define brake-mu (/ 0.1 (* N-brake pin)))

;; the flywheel: a 25 cm iron disc whose width makes the train 1 kg m^2 seen from its arbor
(define unit-disc (disc-wheel #:radius (cm 25) #:width (cm 1)))
(define fly-width (* (cm 1) (/ (- 1.0 (I-of big 'oak) (* 100 I-brake)) (I-of unit-disc 'iron))))

(define apart (+ (* md 50) (* md 5)))                  ; the sum of the pitch radii
(define pinion-angle (radians->degrees (mate-angle 100 0 10 0)))

(define-machine geared-brake
  #:source "issue #113: a flywheel geared 10:1 up into a 0.1 N m brake"
  ;; plain: a perfect mesh
  (wheel plain-flywheel #:shape (disc-wheel #:radius (cm 25) #:width fly-width) #:at (0 (m 1) 0) #:material iron
         #:start-rpm 60 #:bearing-radius (cm 2))
  (wheel plain-gear #:shape big #:at (0 (m 1) (cm -4)) #:material oak #:start-rpm 60)
  (wheel plain-brake #:shape brake-drum #:at (apart (m 1) (cm -16)) #:material iron #:start-rpm -600
         #:bearing-radius pin #:bearing-mu brake-mu)
  (wheel plain-pinion #:shape pinion #:at (apart (m 1) (cm -4)) #:material iron #:angle-deg pinion-angle #:start-rpm -600)
  (arbor plain-flywheel plain-gear)
  (arbor plain-brake plain-pinion)
  (mesh plain-gear plain-pinion)

  ;; lossy: the same train through a mesh of efficiency 0.9
  (wheel lossy-flywheel #:shape (disc-wheel #:radius (cm 25) #:width fly-width) #:at ((m 1) (m 1) 0) #:material iron
         #:start-rpm 60 #:bearing-radius (cm 2))
  (wheel lossy-gear #:shape big #:at ((m 1) (m 1) (cm -4)) #:material oak #:start-rpm 60)
  (wheel lossy-brake #:shape brake-drum #:at ((+ (m 1) apart) (m 1) (cm -16)) #:material iron #:start-rpm -600
         #:bearing-radius pin #:bearing-mu brake-mu)
  (wheel lossy-pinion #:shape pinion #:at ((+ (m 1) apart) (m 1) (cm -4)) #:material iron #:angle-deg pinion-angle #:start-rpm -600)
  (arbor lossy-flywheel lossy-gear)
  (arbor lossy-brake lossy-pinion)
  (mesh lossy-gear lossy-pinion #:efficiency 0.9))
