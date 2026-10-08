#lang heroic
;; Four windlasses, alike but for their ratchets (issue #49). On each a 10 cm
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
;;   lower   the fourth is the first again, but its pawl is lifted 2 s in
;;           (issue #155; the field (lower-pawl pawl), 0 lifts it). Then nothing
;;           holds the drum and the block lowers itself, turning the drum as it
;;           goes: m g = (m + I/r^2) a. The drum (shape drum: 10.25 L of oak with
;;           its flanges, I = 720 x 6.798e-5 = 0.04894 kg.m2 about its axle) is
;;           I/r^2 = 4.894 kg at the rope, so a = 20.02 x 9.81 / (20.02 + 4.89)
;;           = 7.88 m/s2, not g: it reaches the floor 0.9 m down in
;;           sqrt(2 (0.9) / 7.88) = 0.48 s, as the free one does from the start.
;;   held, then lowered (issue #157) the third's crank is let go at 5 s (right-click
;;           the wheel: "Let go of the drive", drive-torque 0; the pawl alone holds) and its
;;           block stays where the winding left it, 1 + 0.314 m (less the first
;;           moments of the crank's start); at 10 s the pawl is lifted (click
;;           the ratchet) and the block falls as the fourth does, at 7.88 m/s2, the
;;           1.21 m to the floor in sqrt(2 x 1.21 / 7.883) = 0.555 s.
;;   controls  click a wheel to stop its crank (drive-rpm), Shift+click to reverse
;;           it; click a ratchet to lift its pawl or drop it again.
;; The four stand in a row 1 m apart along x, seen from the front, ratchets and all (#192). One behind the other along
;; their axles they shared one axle line, so they were drawn as one axle under one label of four stacked names. Moved
;; along x, the held and free windlasses run the same to 6e-5 m/s; the two pawls that are let go (wind, lower) catch
;; a tick earlier or later than before on rounding, so the held sag differs by up to 5 mm and the loads reach the floor
;; a tick apart, but every number above is unchanged.
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
  (wheel free-drum #:shape (drum #:radius drum-r #:length (cm 30)) #:at ((m 1) axle-height 0) #:material oak)
  (block free-load #:at ((+ (m 1) drum-r) hang 0) #:size block-size #:material granite)
  (rope free-rope #:wind-on free-drum #:to (free-load 0 (/ block-size 2) 0) #:length rope-length)
  ;; cranked, with a pawl
  (wheel wind-drum #:shape (drum #:radius drum-r #:length (cm 30)) #:at ((m 2) axle-height 0) #:material oak
         #:drive-rpm 6 #:drive-torque 40)
  (ratchet wind-pawl #:at ((m 2) axle-height 0) #:on wind-drum #:teeth 12 #:radius (cm 15))
  (block wind-load #:at ((+ (m 2) drum-r) hang 0) #:size block-size #:material granite)
  (rope wind-rope #:wind-on wind-drum #:to (wind-load 0 (/ block-size 2) 0) #:length rope-length)
  ;; held by a pawl that is lifted 2 s in
  (wheel lower-drum #:shape (drum #:radius drum-r #:length (cm 30)) #:at ((m 3) axle-height 0) #:material oak)
  (ratchet lower-pawl #:at ((m 3) axle-height 0) #:on lower-drum #:teeth 12 #:radius (cm 15))
  (block lower-load #:at ((+ (m 3) drum-r) hang 0) #:size block-size #:material granite)
  (rope lower-rope #:wind-on lower-drum #:to (lower-load 0 (/ block-size 2) 0) #:length rope-length)
  ;; the pawl of the fourth lifted 2 s in; the crank of the third let go at 5 s, held by its pawl until the pawl is lifted at 10 s (issue #157)
  (operator (at 2 (lower-pawl pawl 0))
            (at 5 (wind-drum drive-torque 0)) (at 10 (wind-pawl pawl 0))))
