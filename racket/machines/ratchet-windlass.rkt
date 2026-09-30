#lang heroic
;; Three windlasses, alike but for their ratchets (issue #49). On each a 10 cm
;; drum on an oak axle winds a rope holding a 20 kg granite block (a 19.5 cm
;; cube, 20.0 kg). Working it through:
;;   hold    the first has a ratchet of 12 teeth on a 15 cm circle and no crank:
;;           the block pulls the drum back with m g r = 20.0 (9.81)(0.10) =
;;           19.6 N.m, and the pawl, at the teeth's circle, must push back with
;;           m g r / R = 19.6 / 0.15 = 131 N. The block does not fall: the wheel
;;           may turn back only to the valley the pawl is in, and it starts in one:
;;           the block sags a few millimetres (the pawl acts a tick behind the load),
;;           never as much as a tooth (30 degrees, 5 cm of rope).
;;   free    the second has no ratchet: the block falls, at m g / (m + I/r^2),
;;           just under g, the 90 cm to the floor in about half a second.
;;   wind    the third has a ratchet and a crank turning it at 6 rpm with up to
;;           40 N.m (over the 19.6 the block asks): the wheel goes round 0.628
;;           rad/s, winding the rope in at r w = 6.28 cm/s, so the block rises
;;           0.314 m in 5 s; the pawl drops over a tooth every 30 degrees (360/12),
;;           5 s = 180 degrees = 6 teeth, 12 at 10 s; and clear of the pawl
;;           it carries nothing.
(define drum-r (cm 10))
(define block-size (cm 19.5))
(define hang (m 1.0))                        ; the block's middle, above the floor
(define axle-height (m 2.0))
(define (windlass z)
  (list (list 0 axle-height z)                                                   ; the drum
        (list drum-r hang z)))                                                   ; the block
(define rope-length (- axle-height hang (/ block-size 2)))

(define-machine ratchet-windlass
  #:source "A ratchet holds a windlass's load; without one it runs away"
  ;; held by a pawl
  (wheel hold-drum #:shape (drum #:radius drum-r #:length (cm 30)) #:at (0 axle-height 0) #:material oak)
  (ratchet hold-pawl #:at (0 axle-height 0) #:on hold-drum #:teeth 12 #:radius (cm 15))
  (block hold-load #:at (drum-r hang 0) #:size block-size #:material granite)
  (rope hold-rope #:wind-on hold-drum #:to (hold-load 0 (/ block-size 2) 0) #:length rope-length)
  ;; no pawl
  (wheel free-drum #:shape (drum #:radius drum-r #:length (cm 30)) #:at (0 axle-height (m -1.5)) #:material oak)
  (block free-load #:at (drum-r hang (m -1.5)) #:size block-size #:material granite)
  (rope free-rope #:wind-on free-drum #:to (free-load 0 (/ block-size 2) 0) #:length rope-length)
  ;; cranked, with a pawl
  (wheel wind-drum #:shape (drum #:radius drum-r #:length (cm 30)) #:at (0 axle-height (m -3)) #:material oak
         #:drive-rpm 6 #:drive-torque 40)
  (ratchet wind-pawl #:at (0 axle-height (m -3)) #:on wind-drum #:teeth 12 #:radius (cm 15))
  (block wind-load #:at (drum-r hang (m -3)) #:size block-size #:material granite)
  (rope wind-rope #:wind-on wind-drum #:to (wind-load 0 (/ block-size 2) 0) #:length rope-length))
