#lang racket/base
;; Writes a mesh as a binary glTF 2.0 file (.glb): one node, one mesh,
;; POSITION + NORMAL attributes and 32-bit indices. No materials — the
;; game colours every part from the material table, so a mesh file only
;; ever carries shape. `extras` (glTF's free-form field) records the
;; numbers the shape was generated from, so a .glb opened in Blender
;; still says what it is.
(require racket/list json "mesh.rkt")
(provide write-glb mesh->glb-bytes)

(define (f32 x) (real->floating-point-bytes x 4 #f))
(define (u32 n) (integer->integer-bytes n 4 #f #f))
;; The value a float32 actually stores, so accessor min/max match the
;; buffer exactly, as the spec requires.
(define (as-f32 x) (floating-point-bytes->real (f32 x) #f))

(define (pad bs byte)
  (define extra (modulo (- 4 (modulo (bytes-length bs) 4)) 4))
  (bytes-append bs (make-bytes extra byte)))

(define (mesh->glb-bytes m #:name [name "shape"] #:extras [extras (hasheq)])
  (define ps (mesh-positions m))
  (define ns (mesh-normals m))
  (define is (mesh-indices m))
  (define n (vector-length ps))
  (define (vec-bytes vs)
    (apply bytes-append (for*/list ([v (in-vector vs)] [k (in-range 3)]) (f32 (vector-ref v k)))))
  (define pos-bytes (vec-bytes ps))
  (define nrm-bytes (vec-bytes ns))
  (define idx-bytes (apply bytes-append (for/list ([i (in-vector is)]) (u32 i))))
  (define (stored k) (for/list ([p (in-vector ps)]) (as-f32 (vector-ref p k))))
  (define lo (for/list ([k 3]) (apply min (stored k))))
  (define hi (for/list ([k 3]) (apply max (stored k))))
  (define bin (bytes-append pos-bytes nrm-bytes idx-bytes)) ; each part is a multiple of 4 bytes
  (define json
    (hasheq 'asset (hasheq 'version "2.0" 'generator "#lang heroic geometry")
            'scene 0
            'scenes (list (hasheq 'nodes '(0)))
            'nodes (list (hasheq 'mesh 0 'name name 'extras extras))
            'meshes (list (hasheq 'name name
                                  'primitives (list (hasheq 'attributes (hasheq 'POSITION 0 'NORMAL 1)
                                                            'indices 2
                                                            'mode 4))))
            'buffers (list (hasheq 'byteLength (bytes-length bin)))
            'bufferViews (list (hasheq 'buffer 0 'byteOffset 0 'byteLength (bytes-length pos-bytes) 'target 34962)
                               (hasheq 'buffer 0 'byteOffset (bytes-length pos-bytes)
                                       'byteLength (bytes-length nrm-bytes) 'target 34962)
                               (hasheq 'buffer 0 'byteOffset (+ (bytes-length pos-bytes) (bytes-length nrm-bytes))
                                       'byteLength (bytes-length idx-bytes) 'target 34963))
            'accessors (list (hasheq 'bufferView 0 'componentType 5126 'count n 'type "VEC3" 'min lo 'max hi)
                             (hasheq 'bufferView 1 'componentType 5126 'count n 'type "VEC3")
                             (hasheq 'bufferView 2 'componentType 5125 'count (vector-length is) 'type "SCALAR"))))
  (define json-chunk (pad (jsexpr->bytes json) (char->integer #\space)))
  (define bin-chunk (pad bin 0))
  (define total (+ 12 8 (bytes-length json-chunk) 8 (bytes-length bin-chunk)))
  (bytes-append #"glTF" (u32 2) (u32 total)
                (u32 (bytes-length json-chunk)) #"JSON" json-chunk
                (u32 (bytes-length bin-chunk)) #"BIN\0" bin-chunk))

(define (write-glb m path #:name [name "shape"] #:extras [extras (hasheq)])
  (call-with-output-file path #:exists 'truncate/replace
    (λ (out) (write-bytes (mesh->glb-bytes m #:name name #:extras extras) out))))
