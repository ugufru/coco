\ rg-pixel.fs — RG6 artifact-color pixel primitives
\
\ Provides: rg-init, rg-pcls, rg-pset, rg-pget, rg-hline, rg-line
\ Requires: kernel primitives AND, OR, C@, C!, LSHIFT, RSHIFT, FILL,
\           *, +, -, DUP, DROP, SWAP, OVER, @, !, DO, LOOP, I
\           vdg.fs (set-sam-v, set-sam-f, set-pia)
\
\ RG6 mode is 256x192, 1 bit/pixel, 6144 bytes VRAM, 32 bytes/row.
\ On NTSC composite, adjacent pixel pairs produce artifact colors:
\
\   Bit pair   Artifact color
\   --------   --------------
\     00       Black
\     11       White
\     10       Blue
\     01       Red/orange
\
\ This library addresses artifact-color pixels at 128x192 resolution.
\ x ranges 0-127, y ranges 0-191.  Each artifact pixel is a 2-bit pair
\ in VRAM.  Color parameter: 0=black, 1=blue, 2=red, 3=white.
\
\ VRAM base: $0E00 (SAM F offset 7).  Leaves $0400-$0DFF free for
\ text VRAM and variables.
\
\ Byte layout (8 real pixels = 4 artifact pixels per byte):
\   bits 7-6 = artifact pixel 0 (leftmost)
\   bits 5-4 = artifact pixel 1
\   bits 3-2 = artifact pixel 2
\   bits 1-0 = artifact pixel 3 (rightmost)
\
\ Color encoding per artifact pixel (2 bits):
\   0 (black) = 00    1 (blue)  = 01
\   2 (red)   = 10    3 (white) = 11

VARIABLE rv                       \ VRAM base address

\ ── Lookup tables ─────────────────────────────────────────────────────────
\ Three 4-byte tables in high RAM, built once by rg-init.

$7CD0 CONSTANT COLTAB            \ color index → 2-bit pattern
$7CD4 CONSTANT SHFTAB            \ sub-pixel (x%4) → left-shift amount
$7CD8 CONSTANT MSKTAB            \ sub-pixel (x%4) → AND mask to clear

: init-tables  ( -- )
  \ Color table: index 0-3 → bit pair value
  \   0=black(00) 1=blue(01) 2=red(10) 3=white(11)
  \ Note: artifact phase depends on XRoar -tv-input setting.
  \ With cmp-br: 01=blue, 10=red/orange.
  0 COLTAB     C!
  1 COLTAB 1 + C!
  2 COLTAB 2 + C!
  3 COLTAB 3 + C!
  \ Shift table: sub-pixel position → left shift count
  \   x%4=0 → 6, x%4=1 → 4, x%4=2 → 2, x%4=3 → 0
  6 SHFTAB     C!
  4 SHFTAB 1 + C!
  2 SHFTAB 2 + C!
  0 SHFTAB 3 + C!
  \ Mask table: sub-pixel position → AND mask (clears that pixel's bits)
  \   x%4=0 → $3F, x%4=1 → $CF, x%4=2 → $F3, x%4=3 → $FC
  $3F MSKTAB     C!
  $CF MSKTAB 1 + C!
  $F3 MSKTAB 2 + C!
  $FC MSKTAB 3 + C! ;

\ ── rg-init ( -- ) ────────────────────────────────────────────────────────
\ Switch to RG6 mode, build lookup tables, clear screen.

: rg-init  ( -- )
  init-tables
  $5000 DUP rv !  $57 !        \ store VRAM base in rv AND kernel var
  6 set-sam-v                  \ V2:V1:V0 = 110 (RG6)
  rv @ 9 RSHIFT set-sam-f     \ F offset = VRAM / 512
  $F8 set-pia                  \ A*/G=1, GM2=1, GM1=1, GM0=1, CSS=1
  rv @ 6144 0 FILL ;           \ clear to black

\ ── rg-pcls ( -- ) ────────────────────────────────────────────────────────
\ Clear entire screen to black.

: rg-pcls  ( -- )  rv @ 6144 0 FILL ;

\ ── Internal variables for pixel ops ──────────────────────────────────────

VARIABLE pa                    \ VRAM byte address
VARIABLE ps                    \ sub-pixel index (0-3)

\ ── rg-addr ( x y -- ) ───────────────────────────────────────────────────
\ Calculate VRAM byte address and sub-pixel index for artifact pixel at
\ (x, y).  Stores results in pa and ps.  x: 0-127, y: 0-191.

: rg-addr  ( x y -- )
  32 * rv @ + SWAP             \ ( row-addr x )
  DUP 3 AND ps !              \ ps = x % 4
  2 RSHIFT + pa ! ;            \ pa = row-addr + x/4

\ ── rg-pset ( x y color -- ) ─────────────────────────────────────────────
\ Plot one 2bpp artifact pixel.  ~45 cycles vs ~500 in ITC Forth.

CODE rg-pset
        LDA     5,U             ; A = x (low byte of 3rd item)
        LDB     3,U             ; B = y (low byte of 2nd item)
        PSHS    A               ; save x on S
        LDA     #32
        MUL                     ; D = y * 32
        ADDD    VAR_RGVRAM      ; D += VRAM base
        TFR     D,Y             ; Y = row start
        LDA     ,S              ; A = x
        LSRA
        LSRA                    ; A = x / 4
        LEAY    A,Y             ; Y = VRAM byte address
        LDA     ,S+             ; A = x, pop S
        ANDA    #$03            ; A = x % 4
        ASLA                    ; A = (x%4)*2
        NEGA
        ADDA    #6              ; A = 6 - (x%4)*2
        PSHS    A               ; save shift count
        LDA     1,U             ; A = color (0-3)
        ANDA    #$03
        LDB     ,S              ; B = shift count
        BEQ     @ns
@sh     ASLA
        DECB
        BNE     @sh
@ns     PSHS    A               ; save shifted color
        LDA     #$03
        LDB     1,S             ; B = shift count
        BEQ     @nm
@sm     ASLA
        DECB
        BNE     @sm
@nm     COMA                    ; A = clear mask
        ANDA    ,Y              ; clear old pixel
        ORA     ,S              ; OR in new color
        STA     ,Y              ; write back
        LEAS    2,S             ; clean S
        LEAU    6,U             ; pop 3 data stack items
        ;NEXT
;CODE

\ ── rg-pget ( x y -- color ) ─────────────────────────────────────────────
\ Read the raw 2-bit value at artifact pixel (x, y).
\ Returns: 0=black(00), 1=red(01), 2=blue(10), 3=white(11).
\ Note: raw bit values, not the color indices used by rg-pset.
\ To compare with rg-pset colors, use the inverse mapping:
\   raw 0→color 0, raw 1→color 2, raw 2→color 1, raw 3→color 3.

: rg-pget  ( x y -- raw )
  rg-addr                      \ sets pa, ps
  pa @ C@                      \ ( byte )
  ps @ SHFTAB + C@ RSHIFT     \ shift pixel to bits 1-0
  3 AND ;                      \ mask to 2 bits

\ ── rg-hline ( x1 x2 y color -- ) ────────────────────────────────────────
\ Horizontal line from x1 to x2 (inclusive) at row y.  x1 <= x2.
\ Simple per-pixel loop.  TODO: optimize with full-byte fills for aligned
\ interior spans.

VARIABLE hl-c  VARIABLE hl-y

: rg-hline  ( x1 x2 y color -- )
  hl-c ! hl-y !               \ save color and y
  1 + SWAP                     \ ( x2+1 x1 )
  DO
    I hl-y @ hl-c @ rg-pset
  LOOP ;

\ ── Helpers ──────────────────────────────────────────────────────────────

: abs  ( n -- |n| )  DUP 0 < IF NEGATE THEN ;

\ ── rg-line ( x1 y1 x2 y2 color -- ) ───────────────────────────────────
\ Bresenham line drawing with inlined pixel write.  All 8 octants.
\ ~150 cycles/pixel vs ~1500 in ITC Forth.

CODE rg-line
        PSHS    X               ; save IP
        ; Load args: U+0,1=color U+2,3=y2 U+4,5=x2 U+6,7=y1 U+8,9=x1
        LDA     1,U
        STA     VAR_LINE_COL
        LDA     5,U
        STA     VAR_LINE_X2
        LDA     3,U
        STA     VAR_LINE_Y2
        LDA     9,U
        STA     VAR_LINE_CX
        LDA     7,U
        STA     VAR_LINE_CY
        LEAU    10,U            ; pop 5 args
        ; dx = |x2-x1|, sx = sign(x2-x1)
        CLRA
        LDB     VAR_LINE_CX
        PSHS    D
        CLRA
        LDB     VAR_LINE_X2
        SUBD    ,S++
        BPL     @sx_p
        COMA
        COMB
        ADDD    #1
        STB     VAR_LINE_DX
        LDA     #$FF
        STA     VAR_LINE_SX
        BRA     @sx_d
@sx_p   STB     VAR_LINE_DX
        LDA     #$01
        STA     VAR_LINE_SX
@sx_d
        ; dy = |y2-y1|, sy = sign(y2-y1)
        CLRA
        LDB     VAR_LINE_CY
        PSHS    D
        CLRA
        LDB     VAR_LINE_Y2
        SUBD    ,S++
        BPL     @sy_p
        COMA
        COMB
        ADDD    #1
        STB     VAR_LINE_DY
        LDA     #$FF
        STA     VAR_LINE_SY
        BRA     @sy_d
@sy_p   STB     VAR_LINE_DY
        LDA     #$01
        STA     VAR_LINE_SY
@sy_d
        ; err = dx - dy (signed 16-bit)
        CLRA
        LDB     VAR_LINE_DX
        STD     VAR_LINE_ERR
        CLRA
        LDB     VAR_LINE_DY
        PSHS    D
        LDD     VAR_LINE_ERR
        SUBD    ,S++
        STD     VAR_LINE_ERR
@loop
        ; Plot pixel at (CX, CY)
        LDA     VAR_LINE_CY
        LDB     #32
        MUL
        ADDD    VAR_RGVRAM
        TFR     D,Y
        LDA     VAR_LINE_CX
        LSRA
        LSRA
        LEAY    A,Y
        LDA     VAR_LINE_CX
        ANDA    #$03
        ASLA
        NEGA
        ADDA    #6
        PSHS    A
        LDA     VAR_LINE_COL
        ANDA    #$03
        LDB     ,S
        BEQ     @ns
@sh     ASLA
        DECB
        BNE     @sh
@ns     PSHS    A
        LDA     #$03
        LDB     1,S
        BEQ     @nm
@sm     ASLA
        DECB
        BNE     @sm
@nm     COMA
        ANDA    ,Y
        ORA     ,S
        STA     ,Y
        LEAS    2,S
        ; Done check
        LDA     VAR_LINE_CX
        CMPA    VAR_LINE_X2
        BNE     @step
        LDA     VAR_LINE_CY
        CMPA    VAR_LINE_Y2
        BEQ     @done
@step
        ; e2 = 2 * err
        LDD     VAR_LINE_ERR
        ASLB
        ROLA
        STD     VAR_LINE_E2
        ; if e2 > -dy: err -= dy, cx += sx
        CLRA
        LDB     VAR_LINE_DY
        COMA
        COMB
        ADDD    #1
        CMPD    VAR_LINE_E2
        BGE     @nosx
        CLRA
        LDB     VAR_LINE_DY
        PSHS    D
        LDD     VAR_LINE_ERR
        SUBD    ,S++
        STD     VAR_LINE_ERR
        LDA     VAR_LINE_CX
        ADDA    VAR_LINE_SX
        STA     VAR_LINE_CX
@nosx
        ; if e2 < dx: err += dx, cy += sy
        CLRA
        LDB     VAR_LINE_DX
        CMPD    VAR_LINE_E2
        BLE     @nosy
        CLRA
        LDB     VAR_LINE_DX
        ADDD    VAR_LINE_ERR
        STD     VAR_LINE_ERR
        LDA     VAR_LINE_CY
        ADDA    VAR_LINE_SY
        STA     VAR_LINE_CY
@nosy
        LBRA    @loop
@done
        PULS    X
        ;NEXT
;CODE
