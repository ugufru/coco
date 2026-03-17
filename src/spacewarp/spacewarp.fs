\ spacewarp.fs — Bare Naked Space Warp for the TRS-80 Color Computer
\
\ A reimplementation of Joshua Lavinsky's Space Warp (1980) using
\ RG6 NTSC artifact coloring.  See SPEC.md and USERGUIDE.md.

\ ── Libraries ────────────────────────────────────────────────────────────

INCLUDE ../../forth/lib/vdg.fs
INCLUDE ../../forth/lib/screen.fs
INCLUDE ../../forth/lib/rg-pixel.fs
INCLUDE ../../forth/lib/datawrite.fs
INCLUDE ../../forth/lib/sprite.fs
INCLUDE ../../forth/lib/trig.fs
INCLUDE ../../forth/lib/rng.fs
INCLUDE ../../forth/lib/font-art.fs
INCLUDE ../../forth/lib/rg-text.fs
INCLUDE ../../forth/lib/keyboard.fs

\ ── Bulk pixel plotter (assembly) ───────────────────────────────────────
\ plot-dots ( addr count color -- )
\ Plots count pixels from (x,y) byte pairs at addr, all in one color.
\ Used for storm stars and event horizon spiral.

CODE plot-dots
        PSHS    X               ; save IP
        LDA     1,U             ; A = color
        ANDA    #$03
        STA     VAR_LINE_COL    ; stash color (reuse line scratch)
        LDD     2,U             ; D = count
        STB     VAR_SPR_ROW     ; stash count (reuse sprite scratch)
        LDX     4,U             ; X = addr
        LEAU    6,U             ; pop 3 args
        ; Loop over count (x,y) pairs
        LDA     VAR_SPR_ROW
        BEQ     @done
@loop   PSHS    A               ; save remaining count
        ; Load x, y from buffer
        LDA     ,X              ; A = x
        LDB     1,X             ; B = y
        PSHS    X               ; save buffer pointer
        ; Compute VRAM addr: Y = RGVRAM + y*32 + x/4
        PSHS    A               ; save x
        LDA     #32
        MUL                     ; D = y * 32
        ADDD    VAR_RGVRAM
        TFR     D,Y             ; Y = row base
        LDA     ,S              ; A = x
        LSRA
        LSRA                    ; A = x/4
        LEAY    A,Y             ; Y = VRAM byte
        ; Shift = 6 - (x%4)*2
        LDA     ,S+             ; A = x, pop
        ANDA    #$03
        ASLA
        NEGA
        ADDA    #6              ; A = shift count
        PSHS    A               ; save shift
        ; Shift color into position
        LDA     VAR_LINE_COL
        LDB     ,S
        BEQ     @ns
@sh     ASLA
        DECB
        BNE     @sh
@ns     PSHS    A               ; save shifted color
        ; Build clear mask
        LDA     #$03
        LDB     1,S             ; shift count
        BEQ     @nm
@sm     ASLA
        DECB
        BNE     @sm
@nm     COMA
        ANDA    ,Y              ; clear old pixel
        ORA     ,S              ; OR in new color
        STA     ,Y              ; write back
        LEAS    2,S             ; clean shift+color
        ; Advance
        PULS    X               ; restore buffer pointer
        LEAX    2,X             ; next (x,y) pair
        PULS    A               ; restore count
        DECA
        BNE     @loop
@done   PULS    X               ; restore IP
        ;NEXT
;CODE

\ ══════════════════════════════════════════════════════════════════════════
\  GALAXY DATA MODEL
\ ══════════════════════════════════════════════════════════════════════════
\
\ Galaxy: 8x8 = 64 quadrants, 1 byte each at GALAXY ($7600).
\ Packed format per byte:
\   bit 7    = magnetic storm (1=yes)
\   bit 6    = black hole (1=yes)
\   bits 5-3 = star count (0-5)
\   bit 2    = base present (1=yes)
\   bits 1-0 = jovian count (0-3)
\
\ Expanded quadrant state (when player is in a quadrant):
\   Object positions stored as (x, y) pairs in artifact pixels.
\   x: 2-125 (inside tactical border), y: 2-141 (inside border).

\ ── Galaxy array ─────────────────────────────────────────────────────────

\ Game data starts at $7600 (above font which ends at $75D8).
$7600 CONSTANT GALAXY          \ 64 bytes: 8x8 quadrant data

: gal-addr  ( col row -- addr )  8 * + GALAXY + ;
: gal@  ( col row -- byte )  gal-addr C@ ;
: gal!  ( byte col row -- )  gal-addr C! ;

\ ── Quadrant byte field access ───────────────────────────────────────────

: q-jovians  ( qbyte -- 0-3 )    3 AND ;
: q-base?    ( qbyte -- flag )   4 AND ;
: q-stars    ( qbyte -- 0-5 )    3 RSHIFT 7 AND ;
: q-bhole?   ( qbyte -- flag )   $40 AND ;
: q-storm?   ( qbyte -- flag )   $80 AND ;

\ ── Quadrant byte construction ───────────────────────────────────────────

: q-pack  ( storm bh stars base jovians -- byte )
  SWAP 2 LSHIFT OR            \ jovians | base<<2
  SWAP 3 LSHIFT OR            \ | stars<<3
  SWAP 6 LSHIFT OR            \ | bh<<6
  SWAP 7 LSHIFT OR ;          \ | storm<<7

\ ── Game variables ───────────────────────────────────────────────────────

VARIABLE glevel                \ difficulty level (1-10)
VARIABLE gjovians              \ total jovians in galaxy
VARIABLE gbases                \ total bases in galaxy
VARIABLE gtime                 \ stardate (increments each real-time minute)

\ Player state
VARIABLE pcol                  \ current quadrant column (0-7)
VARIABLE prow                  \ current quadrant row (0-7)
VARIABLE penergy               \ ship energy percentage (0-100)
VARIABLE pshields              \ deflector setting (0-100)
VARIABLE pmissiles             \ triton missiles remaining
VARIABLE pdmg-ion              \ ion engine damage (100=ok, 0=destroyed)
VARIABLE pdmg-warp             \ hyperdrive damage
VARIABLE pdmg-scan             \ scanner damage
VARIABLE pdmg-defl             \ deflector damage
VARIABLE pdmg-masr             \ maser damage

\ ── Expanded quadrant state ──────────────────────────────────────────────
\ When the player enters a quadrant, we expand it into object arrays.
\ Max 5 stars, 3 jovians, 1 base, 1 black hole.
\ Each object has an (x, y) position in artifact pixels within the
\ tactical view (2-125, 2-141).

\ Position arrays (2 bytes each: x then y)
$7640 CONSTANT STAR-POS        \ 5 stars x 2 bytes = 10 bytes
$764A CONSTANT JOV-POS         \ 3 jovians x 2 bytes = 6 bytes
$7650 CONSTANT BASE-POS        \ 1 base x 2 bytes = 2 bytes
$7652 CONSTANT BHOLE-POS       \ 1 black hole x 2 bytes = 2 bytes
$7654 CONSTANT SHIP-POS        \ player ship x 2 bytes = 2 bytes

\ Jovian damage (3 bytes, one per jovian: 100=full health, 0=dead)
$7656 CONSTANT JOV-DMG         \ 3 bytes

\ Quadrant object counts (from the packed byte, cached for speed)
VARIABLE qstars                \ star count in current quadrant
VARIABLE qjovians              \ jovian count in current quadrant
VARIABLE qbase                 \ base present? (0 or 1)
VARIABLE qbhole                \ black hole present? (0 or 1)

\ SOS alert state
VARIABLE sos-active
VARIABLE sos-col
VARIABLE sos-row

\ Docking state
VARIABLE docked                    \ 1 = currently docked at base

\ Death cause (for game-over message)
VARIABLE death-cause               \ 0=energy/star, 1=black hole

\ ── Sprite data ──────────────────────────────────────────────────────────
\ 7x5 pixel sprites in 2bpp artifact-color format.
\ Built at init time using datawrite helpers (tb).

$7700 CONSTANT SPR-SHIP           \ Endever: blue chevron (12 bytes)
$770C CONSTANT SPR-JOV            \ Jovian: red diamond (12 bytes)
$7718 CONSTANT SPR-BASE           \ UP base: blue cross (12 bytes)
$7724 CONSTANT SPR-MSL1           \ Missile frame 1: + shape (12 bytes)
$7730 CONSTANT SPR-MSL2           \ Missile frame 2: x shape (12 bytes)

\ ── Jovian AI data structures ────────────────────────────────────────────
$773C CONSTANT JOV-STATE        \ 3 bytes: 0=attack, 1=flee, 2=idle
$773F CONSTANT JOV-TICK         \ 3 bytes: per-Jovian frame counter
$7742 CONSTANT JOV-BG0          \ 20 bytes: bg save buffer Jovian 0
$7756 CONSTANT JOV-BG1          \ 20 bytes: bg save buffer Jovian 1
$776A CONSTANT JOV-BG2          \ 20 bytes: bg save buffer Jovian 2
$777E CONSTANT JOV-OLDX         \ 3 bytes: previous x per Jovian
$7781 CONSTANT JOV-OLDY         \ 3 bytes: previous y per Jovian

: jov-bg  ( i -- addr )
  DUP 0= IF DROP JOV-BG0 EXIT THEN
  1 = IF JOV-BG1 ELSE JOV-BG2 THEN ;

: init-sprites  ( -- )
  \ Endever — blue (1) filled chevron
  \   ...1...
  \   ..1.1..
  \   .11.11.
  \   1111111
  \   1111111
  SPR-SHIP tp !
  7 tb 5 tb
  $01 tb $00 tb
  $04 tb $40 tb
  $14 tb $50 tb
  $55 tb $54 tb
  $55 tb $54 tb

  \ Jovian — red (2) diamond
  \   ...2...
  \   ..2.2..
  \   .2.2.2.
  \   ..2.2..
  \   ...2...
  SPR-JOV tp !
  7 tb 5 tb
  $02 tb $00 tb
  $08 tb $80 tb
  $22 tb $20 tb
  $08 tb $80 tb
  $02 tb $00 tb

  \ Base — blue (1) cross/ring
  \   ..111..
  \   .1...1.
  \   1..1..1
  \   .1...1.
  \   ..111..
  SPR-BASE tp !
  7 tb 5 tb
  $05 tb $40 tb
  $10 tb $10 tb
  $41 tb $04 tb
  $10 tb $10 tb
  $05 tb $40 tb

  \ Missile frame 1 — red (2) plus +
  \   ..2..
  \   ..2..
  \   22222
  \   ..2..
  \   ..2..
  SPR-MSL1 tp !
  5 tb 5 tb
  $08 tb $00 tb
  $08 tb $00 tb
  $AA tb $80 tb
  $08 tb $00 tb
  $08 tb $00 tb

  \ Missile frame 2 — red (2) cross x
  \   2...2
  \   .2.2.
  \   ..2..
  \   .2.2.
  \   2...2
  SPR-MSL2 tp !
  5 tb 5 tb
  $80 tb $80 tb
  $22 tb $00 tb
  $08 tb $00 tb
  $22 tb $00 tb
  $80 tb $80 tb ;

\ ── Random position within tactical view ─────────────────────────────────
\ Returns x in 4-123, y in 4-139 (away from borders).

VARIABLE rp-tmp

: rnd-x  ( -- x )  128 rnd 4 + DUP 123 > IF DROP 123 THEN ;
: rnd-y  ( -- y )  128 rnd 4 + DUP 139 > IF DROP 139 THEN ;

\ ── Galaxy generation ────────────────────────────────────────────────────
\ Single-pass generation: iterate all 64 quadrants, roll dice for each.
\ No retry loops — guaranteed to terminate.

VARIABLE gi                    \ loop index
VARIABLE gq-tmp                \ temp for building quadrant byte

: clear-galaxy  ( -- )
  64 0 DO  0 GALAXY I + C!  LOOP ;

: gen-galaxy  ( level -- )
  glevel !
  clear-galaxy
  0 gjovians !  0 gbases !

  64 0 DO
    0 gq-tmp !

    \ Stars: 1-4 per quadrant
    4 rnd 1 + 3 LSHIFT gq-tmp @ OR gq-tmp !

    \ Base: ~12% chance (1 in 8)
    8 rnd 0= IF
      4 gq-tmp @ OR gq-tmp !
      gbases @ 1 + gbases !
    THEN

    \ Jovians: probability scales with level
    \ level 1: 1/8 chance → ~8 quadrants with jovians
    \ level 5: 5/8 chance → ~40 quadrants
    \ level 10: always → 64 quadrants
    8 rnd glevel @ < IF
      \ 1-3 jovians, more at higher levels
      2 rnd 1 +               \ 1-2 base
      glevel @ 5 > IF 2 rnd + THEN  \ +0-1 at high levels
      DUP 3 > IF DROP 3 THEN  \ cap at 3
      DUP gjovians @ + gjovians !
      gq-tmp @ OR gq-tmp !
    THEN

    \ Black hole: ~12% chance
    8 rnd 0= IF  $40 gq-tmp @ OR gq-tmp !  THEN

    \ Storm: ~6% chance
    16 rnd 0= IF  $80 gq-tmp @ OR gq-tmp !  THEN

    gq-tmp @ GALAXY I + C!
  LOOP

  \ Ensure at least 1 base
  gbases @ 0= IF
    GALAXY C@ 4 OR GALAXY C!
    1 gbases !
  THEN ;

\ ── expand-quadrant ( col row -- ) ───────────────────────────────────────
\ Expand packed quadrant byte into position arrays for rendering.
\ Assigns random positions to all objects.

: expand-quadrant  ( col row -- )
  OVER OVER gal@               \ ( col row qbyte )

  \ Extract counts
  DUP q-jovians qjovians !
  DUP q-base? IF 1 ELSE 0 THEN qbase !
  DUP q-bhole? IF 1 ELSE 0 THEN qbhole !
  q-stars qstars !

  \ Save player quadrant
  SWAP pcol ! prow !

  \ Generate star positions
  qstars @ ?DUP IF 0 DO
    rnd-x STAR-POS I 2 * + C!
    rnd-y STAR-POS I 2 * + 1 + C!
  LOOP THEN

  \ Generate jovian positions
  qjovians @ ?DUP IF 0 DO
    rnd-x JOV-POS I 2 * + C!
    rnd-y JOV-POS I 2 * + 1 + C!
    100 JOV-DMG I + C!         \ full health
  LOOP THEN

  \ Generate base position
  qbase @ IF
    rnd-x BASE-POS C!
    rnd-y BASE-POS 1 + C!
  THEN

  \ Generate black hole position
  qbhole @ IF
    rnd-x BHOLE-POS C!
    rnd-y BHOLE-POS 1 + C!
  THEN

  \ Place ship at center
  64 SHIP-POS C!
  72 SHIP-POS 1 + C! ;

\ ── init-player ( -- ) ───────────────────────────────────────────────────

: init-player  ( -- )
  100 penergy !
  0 pshields !
  10 pmissiles !
  100 pdmg-ion !
  100 pdmg-warp !
  100 pdmg-scan !
  100 pdmg-defl !
  100 pdmg-masr !
  0 gtime !
  0 move-count ! ;

\ ══════════════════════════════════════════════════════════════════════════
\  STATUS PANEL
\ ══════════════════════════════════════════════════════════════════════════

\ rg-char is a kernel primitive.  Configure for artifact font:
\ 8-byte glyphs at $7000, 8 rows to copy, 32 bpr, 10-pixel row height.
: init-text  ( -- )
  init-font
  rv @ cv !  32 cb !
  rv @ $57 !                    \ kernel VRAM base
  $7400 $75 !                   \ font base
  $20 $77 C!                    \ min char (space)
  8 $78 C!                      \ bytes per glyph
  8 $79 C!                      \ rows to copy
  32 $7A C!                     \ bytes per VRAM row
  10 $7B C!                     \ row height for cy
  $F8 set-pia ;                 \ CSS=1: buff/white for NTSC artifacts

VARIABLE tcx
VARIABLE tcy
: at-xy  ( cx cy -- )  tcy ! tcx ! ;
: rg-emit  ( char -- )  tcx @ tcy @ rg-char  tcx @ 1 + tcx ! ;

: rg-u.  ( u -- )  10 /MOD ?DUP IF rg-u. THEN  CHAR 0 + rg-emit ;

\ Count decimal digits of u (minimum 1)
: #digits  ( u -- n )
  DUP 100 < IF
    10 < IF 1 ELSE 2 THEN
  ELSE
    DROP 3
  THEN ;

\ Print u right-justified so last digit ends at end-col - 1
: rg-u.r  ( u end-col -- )  OVER #digits - tcx !  rg-u. ;

: clear-panel  ( -- )
  rv @ 4608 + 1536 0 FILL ;

\ Panel text rows: cy=15 (pixel 150), 16 (160), 17 (170), 18 (180)

: draw-cond  ( end-col -- )
  docked @ IF
    6 - tcx !
    CHAR D rg-emit CHAR O rg-emit CHAR C rg-emit
    CHAR K rg-emit CHAR E rg-emit CHAR D rg-emit
  ELSE
    qjovians @ IF
      3 - tcx !
      CHAR R rg-emit CHAR E rg-emit CHAR D rg-emit
    ELSE
      5 - tcx !
      CHAR G rg-emit CHAR R rg-emit CHAR E rg-emit CHAR E rg-emit CHAR N rg-emit
    THEN
  THEN ;

: draw-panel  ( -- )
  clear-panel

  \ Left labels col 0, left values right-align to col 14
  \ Right labels col 17, right values right-align to col 32

  \ Row 15: STARDATE      n  MISSILES      nn
  0 15 at-xy
  CHAR S rg-emit CHAR T rg-emit CHAR A rg-emit CHAR R rg-emit
  CHAR D rg-emit CHAR A rg-emit CHAR T rg-emit CHAR E rg-emit
  gtime @ 14 rg-u.r

  17 15 at-xy
  CHAR M rg-emit CHAR I rg-emit CHAR S rg-emit CHAR S rg-emit
  CHAR I rg-emit CHAR L rg-emit CHAR E rg-emit CHAR S rg-emit
  pmissiles @ 32 rg-u.r

  \ Row 16: QUADRANT    n n  ENERGY       nnn
  0 16 at-xy
  CHAR Q rg-emit CHAR U rg-emit CHAR A rg-emit CHAR D rg-emit
  CHAR R rg-emit CHAR A rg-emit CHAR N rg-emit CHAR T rg-emit
  11 16 at-xy  pcol @ rg-u.  CHAR , rg-emit  prow @ rg-u.

  17 16 at-xy
  CHAR E rg-emit CHAR N rg-emit CHAR E rg-emit CHAR R rg-emit
  CHAR G rg-emit CHAR Y rg-emit
  penergy @ 32 rg-u.r

  \ Row 17: COND/SOS left, SHIELDS right
  sos-active @ IF
    0 17 at-xy
    CHAR S rg-emit CHAR O rg-emit CHAR S rg-emit
    CHAR - rg-emit CHAR B rg-emit CHAR A rg-emit
    CHAR S rg-emit CHAR E rg-emit
    11 17 at-xy
    sos-col @ rg-u.  CHAR , rg-emit  sos-row @ rg-u.
  ELSE
    0 17 at-xy
    CHAR C rg-emit CHAR O rg-emit CHAR N rg-emit CHAR D rg-emit
    14 draw-cond
  THEN

  17 17 at-xy
  CHAR S rg-emit CHAR H rg-emit CHAR I rg-emit CHAR E rg-emit
  CHAR L rg-emit CHAR D rg-emit CHAR S rg-emit
  pshields @ 32 rg-u.r

  \ Row 18: COMMAND prompt
  17 18 at-xy
  CHAR C rg-emit CHAR O rg-emit CHAR M rg-emit CHAR M rg-emit
  CHAR A rg-emit CHAR N rg-emit CHAR D rg-emit ;

\ Quick-update just the energy value (avoids full panel redraw)
: update-energy  ( -- )
  16 tcy !
  29 tcx !  $20 rg-emit  $20 rg-emit  $20 rg-emit
  penergy @ 32 rg-u.r ;

\ Quick-update just the missiles value
: update-missiles  ( -- )
  15 tcy !
  30 tcx !  $20 rg-emit  $20 rg-emit
  pmissiles @ 32 rg-u.r ;

\ ══════════════════════════════════════════════════════════════════════════
\  TACTICAL VIEW DRAWING
\ ══════════════════════════════════════════════════════════════════════════

: draw-border  ( -- )
  0   0   127 0   3 rg-line
  127 0   127 143 3 rg-line
  127 143 0   143 3 rg-line
  0   143 0   0   3 rg-line ;

: draw-stars  ( -- )
  qstars @ ?DUP IF 0 DO
    STAR-POS I 2 * + C@
    STAR-POS I 2 * + 1 + C@
    3 rnd 1 + rg-pset
  LOOP THEN ;

VARIABLE dj-i
: draw-jovians  ( -- )
  qjovians @ ?DUP IF 0 DO
    I dj-i !
    SPR-JOV
    JOV-POS dj-i @ 2 * + C@ 3 -
    JOV-POS dj-i @ 2 * + 1 + C@ 2 -
    spr-draw
  LOOP THEN ;

: draw-base  ( -- )
  qbase @ IF
    SPR-BASE
    BASE-POS C@ 3 - BASE-POS 1 + C@ 2 -
    spr-draw
  THEN ;

VARIABLE old-sx                   \ previous ship x
VARIABLE old-sy                   \ previous ship y

: save-ship-pos  ( -- )
  SHIP-POS C@ old-sx !  SHIP-POS 1 + C@ old-sy ! ;

: draw-ship  ( -- )
  SPR-SHIP
  SHIP-POS C@ 3 - SHIP-POS 1 + C@ 2 -
  spr-draw ;

: erase-ship  ( -- )
  SPR-SHIP
  old-sx @ 3 - old-sy @ 2 -
  spr-erase-box ;

\ ── Background save/restore for flicker-free ship movement ────────────
\ Save 4 bytes × 5 rows of VRAM under the sprite bounding box.
\ Restore to erase without a black flash.
$76D0 CONSTANT SHIP-BG              \ 20-byte save buffer

CODE bg-save   \ ( buf x y -- )  save 4×5 VRAM bytes to buf
        PSHS    X
        LDA     1,U             ; A = y
        LDB     #32
        MUL                     ; D = y * 32
        ADDD    VAR_RGVRAM      ; D = row base
        TFR     D,Y             ; Y = row base
        LDA     3,U             ; A = x
        LSRA
        LSRA                    ; A = x / 4
        LEAY    A,Y             ; Y = first byte to save
        LDX     4,U             ; X = buffer address
        LEAU    6,U             ; pop 3 args
        LDB     #5
@row    LDA     ,Y
        STA     ,X+
        LDA     1,Y
        STA     ,X+
        LDA     2,Y
        STA     ,X+
        LDA     3,Y
        STA     ,X+
        LEAY    32,Y            ; next VRAM row
        DECB
        BNE     @row
        PULS    X
        ;NEXT
;CODE

CODE bg-restore  \ ( buf x y -- )  restore 4×5 VRAM bytes from buf
        PSHS    X
        LDA     1,U             ; A = y
        LDB     #32
        MUL
        ADDD    VAR_RGVRAM
        TFR     D,Y
        LDA     3,U
        LSRA
        LSRA
        LEAY    A,Y
        LDX     4,U
        LEAU    6,U
        LDB     #5
@row    LDA     ,X+
        STA     ,Y
        LDA     ,X+
        STA     1,Y
        LDA     ,X+
        STA     2,Y
        LDA     ,X+
        STA     3,Y
        LEAY    32,Y
        DECB
        BNE     @row
        PULS    X
        ;NEXT
;CODE

: save-ship-bg  ( -- )
  SHIP-BG SHIP-POS C@ 3 - SHIP-POS 1 + C@ 2 - bg-save ;

: restore-ship-bg  ( -- )
  SHIP-BG old-sx @ 3 - old-sy @ 2 - bg-restore ;

\ Missile background buffer
$76E4 CONSTANT MSL-BG                \ 20-byte save buffer

: save-msl-bg  ( -- )
  MSL-BG msl-scrx 2 - msl-scry 2 - bg-save ;

: restore-msl-bg  ( -- )
  MSL-BG msl-px @ 2 - msl-py @ 2 - bg-restore ;

\ ── Flicker-free Jovian background save/restore ─────────────────────────
\ Same pattern as ship: save VRAM under sprite, restore before move.

VARIABLE jbg-i

: save-jov-bgs  ( -- )
  qjovians @ ?DUP IF 0 DO
    I jbg-i !
    JOV-DMG jbg-i @ + C@ IF
      jbg-i @ jov-bg
      JOV-POS jbg-i @ 2 * + C@ 3 -
      JOV-POS jbg-i @ 2 * + 1 + C@ 2 -
      bg-save
    THEN
  LOOP THEN ;

: restore-jov-bgs  ( -- )
  qjovians @ ?DUP IF 0 DO
    I jbg-i !
    JOV-DMG jbg-i @ + C@ IF
      jbg-i @ jov-bg
      JOV-OLDX jbg-i @ + C@ 3 -
      JOV-OLDY jbg-i @ + C@ 2 -
      bg-restore
    THEN
  LOOP THEN ;

: save-jov-oldpos  ( -- )
  qjovians @ ?DUP IF 0 DO
    I jbg-i !
    JOV-POS jbg-i @ 2 * + C@ JOV-OLDX jbg-i @ + C!
    JOV-POS jbg-i @ 2 * + 1 + C@ JOV-OLDY jbg-i @ + C!
  LOOP THEN ;

: draw-jovians-live  ( -- )
  qjovians @ ?DUP IF 0 DO
    I jbg-i !
    JOV-DMG jbg-i @ + C@ IF
      SPR-JOV
      JOV-POS jbg-i @ 2 * + C@ 3 -
      JOV-POS jbg-i @ 2 * + 1 + C@ 2 -
      spr-draw
    THEN
  LOOP THEN ;

: init-jovian-ai  ( -- )
  qjovians @ ?DUP IF 0 DO
    I jbg-i !
    0 JOV-STATE jbg-i @ + C!
    0 JOV-TICK jbg-i @ + C!
    JOV-POS jbg-i @ 2 * + C@ JOV-OLDX jbg-i @ + C!
    JOV-POS jbg-i @ 2 * + 1 + C@ JOV-OLDY jbg-i @ + C!
  LOOP THEN ;

VARIABLE jov-moved               \ flag: did any Jovian move this frame?

\ ── Jovian AI movement ──────────────────────────────────────────────────
\ Chase player at 1px per N frames (N scales with difficulty).
\ Obstacle avoidance: stars (6px), black holes (15px), bases (5px).
\ Reuses sg-sx/sg-sy (star gravity scratch) and hc-jx/hc-jy (hit scratch).

VARIABLE jtk-nx                   \ proposed new x
VARIABLE jtk-ny                   \ proposed new y

\ Movement speed: frames between moves (lower = faster)
: jov-speed  ( -- n )
  10 glevel @ - DUP 2 < IF DROP 2 THEN ;

\ Sign: -1, 0, or 1  (reuses existing abs)
: sign  ( n -- s )  DUP 0= IF ELSE 0 < IF -1 ELSE 1 THEN THEN ;

\ Manhattan distance from (hc-jx, hc-jy) to (x, y) byte pair at addr
: jov-dist  ( addr -- d )
  DUP C@ hc-jx @ - abs
  SWAP 1 + C@ hc-jy @ - abs + ;

\ Check if (hc-jx, hc-jy) is blocked by any obstacle
VARIABLE jblk
: jov-blocked?  ( -- flag )
  0 jblk !
  qstars @ ?DUP IF 0 DO
    jblk @ 0= IF
      STAR-POS I 2 * + jov-dist 6 < IF 1 jblk ! THEN
    THEN
  LOOP THEN
  jblk @ 0= IF
    qbhole @ IF BHOLE-POS jov-dist 15 < IF 1 jblk ! THEN THEN
  THEN
  jblk @ 0= IF
    qbase @ IF BASE-POS jov-dist 5 < IF 1 jblk ! THEN THEN
  THEN
  jblk @ ;

\ Try to move one Jovian toward the player (index in jbg-i)
: move-one-jovian  ( -- )
  \ Get current position into sg-sx, sg-sy (reuse star gravity scratch)
  JOV-POS jbg-i @ 2 * + C@ sg-sx !
  JOV-POS jbg-i @ 2 * + 1 + C@ sg-sy !
  \ Compute proposed position: cur + sign(player - cur), clamped
  SHIP-POS C@ sg-sx @ - sign sg-sx @ +
  DUP 4 < IF DROP sg-sx @ THEN
  DUP 123 > IF DROP sg-sx @ THEN
  jtk-nx !
  SHIP-POS 1 + C@ sg-sy @ - sign sg-sy @ +
  DUP 4 < IF DROP sg-sy @ THEN
  DUP 139 > IF DROP sg-sy @ THEN
  jtk-ny !
  \ Check if blocked (both axes)
  jtk-nx @ hc-jx !  jtk-ny @ hc-jy !
  jov-blocked? IF
    \ Try x only
    jtk-nx @ hc-jx !  sg-sy @ hc-jy !
    jov-blocked? IF
      \ Try y only
      sg-sx @ hc-jx !  jtk-ny @ hc-jy !
      jov-blocked? IF
        \ both blocked, stay put
        sg-sx @ jtk-nx !  sg-sy @ jtk-ny !
      ELSE
        sg-sx @ jtk-nx !             \ keep old x
      THEN
    ELSE
      sg-sy @ jtk-ny !               \ keep old y
    THEN
  THEN
  \ Apply move if position changed
  jtk-nx @ sg-sx @ <>
  jtk-ny @ sg-sy @ <> OR IF
    jtk-nx @ JOV-POS jbg-i @ 2 * + C!
    jtk-ny @ JOV-POS jbg-i @ 2 * + 1 + C!
    1 jov-moved !
  THEN ;

\ Tick all living Jovians
: tick-jovians  ( -- )
  qjovians @ ?DUP IF 0 DO
    I jbg-i !
    JOV-DMG jbg-i @ + C@ IF
      JOV-TICK jbg-i @ + C@ 1 + DUP jov-speed < IF
        JOV-TICK jbg-i @ + C!
      ELSE
        DROP 0 JOV-TICK jbg-i @ + C!
        move-one-jovian
      THEN
    THEN
  LOOP THEN ;

\ ── Jovian gravity (black holes + stars pull/kill Jovians) ────────────
\ Applies every frame after tick-jovians.  Uses sg-sx/sg-sy as scratch.
\ Reuses grav-tick from player gravity for frame timing.

\ Pull one Jovian (index in jbg-i) toward (sg-sx, sg-sy) by 1px
: jov-pull  ( -- )
  JOV-POS jbg-i @ 2 * + C@ sg-sx @ < IF
    JOV-POS jbg-i @ 2 * + C@ 1 + JOV-POS jbg-i @ 2 * + C!
    1 jov-moved !
  THEN
  JOV-POS jbg-i @ 2 * + C@ sg-sx @ > IF
    JOV-POS jbg-i @ 2 * + C@ 1 - JOV-POS jbg-i @ 2 * + C!
    1 jov-moved !
  THEN
  JOV-POS jbg-i @ 2 * + 1 + C@ sg-sy @ < IF
    JOV-POS jbg-i @ 2 * + 1 + C@ 1 + JOV-POS jbg-i @ 2 * + 1 + C!
    1 jov-moved !
  THEN
  JOV-POS jbg-i @ 2 * + 1 + C@ sg-sy @ > IF
    JOV-POS jbg-i @ 2 * + 1 + C@ 1 - JOV-POS jbg-i @ 2 * + 1 + C!
    1 jov-moved !
  THEN ;

\ Kill Jovian (index in jbg-i) — erase sprite, zero health, explode
\ Uses spr-erase-box (not bg-restore) because gravity may have moved
\ the Jovian since the last bg-save, making the saved bg stale.
: jov-kill  ( -- )
  JOV-DMG jbg-i @ + C@ IF
    JOV-POS jbg-i @ 2 * + C@
    JOV-POS jbg-i @ 2 * + 1 + C@
    SPR-JOV
    JOV-POS jbg-i @ 2 * + C@ 3 -
    JOV-POS jbg-i @ 2 * + 1 + C@ 2 -
    spr-erase-box
    0 JOV-DMG jbg-i @ + C!
    explode-jovian
    1 jov-moved !
  THEN ;

: jov-gravity  ( -- )
  qjovians @ ?DUP 0= IF EXIT THEN
  0 DO
    I jbg-i !
    JOV-DMG jbg-i @ + C@ IF
      \ Black hole gravity
      qbhole @ IF
        BHOLE-POS C@ sg-sx !  BHOLE-POS 1 + C@ sg-sy !
        JOV-POS jbg-i @ 2 * + C@ sg-sx @ - abs
        JOV-POS jbg-i @ 2 * + 1 + C@ sg-sy @ - abs +
        DUP 3 < IF                   \ contact: kill
          DROP jov-kill
        ELSE DUP 20 > IF             \ outside well
          DROP
        ELSE 10 > IF                  \ 10-20: pull every 2 frames
          grav-tick @ 1 AND 0= IF jov-pull THEN
        ELSE                          \ <10: pull every frame
          jov-pull
        THEN THEN THEN
      THEN
      \ Star gravity (only if still alive)
      JOV-DMG jbg-i @ + C@ IF
        qstars @ ?DUP IF 0 DO
          STAR-POS I 2 * + C@ sg-sx !
          STAR-POS I 2 * + 1 + C@ sg-sy !
          JOV-POS jbg-i @ 2 * + C@ sg-sx @ - abs
          JOV-POS jbg-i @ 2 * + 1 + C@ sg-sy @ - abs +
          DUP 3 < IF                 \ contact: kill
            DROP jov-kill
          ELSE 8 > IF                \ outside range
          ELSE                        \ 3-8: pull every 4 frames
            grav-tick @ 3 AND 0= IF jov-pull THEN
          THEN THEN
        LOOP THEN
      THEN
    THEN
  LOOP ;

\ ── Explosion effects ────────────────────────────────────────────────
\ Scatter random colored dots in a radius, hold, erase.  Uses a buffer
\ at $7D00 for up to 40 (x,y) pairs = 80 bytes.  Clamped to screen.
\ Sizes: Jovian=4px/12dots, Endever=8px/20dots, Base=12px/30dots,
\ Self-destruct=20px/40dots.

$7D00 CONSTANT EXPLBUF               \ explosion dot buffer (x,y pairs)
VARIABLE expl-cx                      \ explosion center x
VARIABLE expl-cy                      \ explosion center y
VARIABLE expl-count                   \ number of dots

VARIABLE expl-rad                     \ explosion radius

\ Generate random dot within radius, clamped to tactical view
: expl-dot  ( i -- )   \ fills EXPLBUF entry i
  2 * EXPLBUF +                      \ ( buf-addr )
  expl-rad @ 2 * 1 + rnd expl-rad @ -
  expl-cx @ + DUP 1 < IF DROP 1 THEN DUP 126 > IF DROP 126 THEN
  OVER C!                            \ store x
  expl-rad @ 2 * 1 + rnd expl-rad @ -
  expl-cy @ + DUP 1 < IF DROP 1 THEN DUP 142 > IF DROP 142 THEN
  SWAP 1 + C! ;                      \ store y

\ Fill explosion buffer with random dots
: gen-explosion  ( cx cy radius count -- )
  expl-count !  expl-rad !
  expl-cy !  expl-cx !
  expl-count @ 0 DO  I expl-dot  LOOP ;

\ Animate: draw white, hold, draw red, hold, erase
\ hold-frames controls how long each phase lasts
: show-explosion  ( hold-frames -- )
  EXPLBUF expl-count @ 3 plot-dots    \ white burst
  DUP 0 DO VSYNC LOOP
  EXPLBUF expl-count @ 2 plot-dots    \ red fade
  0 DO VSYNC LOOP
  EXPLBUF expl-count @ 0 plot-dots    \ erase to black
  draw-stars ;                        \ restore any stars damaged by erase

\ Convenience words for each explosion size
: explode-jovian  ( x y -- )   6 20 gen-explosion   3 show-explosion ;
: explode-ship    ( x y -- )   12 30 gen-explosion   5 show-explosion ;
: explode-base    ( x y -- )   16 35 gen-explosion   8 show-explosion ;
: explode-destruct ( x y -- )  24 40 gen-explosion  12 show-explosion ;

\ ── Jovian beam fire ──────────────────────────────────────────────────
\ One red beam at a time, from a random living Jovian toward the player.
\ Fire cooldown scales with difficulty: 90 frames (level 1) to 18 (level 9).
\ Uses dedicated variables (separate from player maser beam).

VARIABLE jbeam-x1                 \ Jovian beam start
VARIABLE jbeam-y1
VARIABLE jbeam-x2                 \ Jovian beam endpoint (clamped)
VARIABLE jbeam-y2
VARIABLE jbeam-timer              \ frames remaining (0=none)
VARIABLE jbeam-drawn              \ 1 = beam pixels currently on screen
VARIABLE jbeam-cool               \ frames until next fire allowed
8 CONSTANT JBEAM-FRAMES           \ how long Jovian beam stays on screen
5 CONSTANT JBEAM-DMG              \ energy damage to player per hit
20 CONSTANT JBEAM-SYS-DMG         \ system damage per hit (out of 100)

\ Fire cooldown: 150 - (level * 14), min 24 frames
\ Level 1: ~136 frames (2.3s), Level 5: ~80 frames (1.3s), Level 9: ~24 frames (0.4s)
: jbeam-cooldown  ( -- n )
  150 glevel @ 14 * - DUP 24 < IF DROP 24 THEN ;

: clamp-jbeam  ( -- )
  jbeam-x2 @ 1 < IF 1 jbeam-x2 ! THEN
  jbeam-x2 @ 126 > IF 126 jbeam-x2 ! THEN
  jbeam-y2 @ 1 < IF 1 jbeam-y2 ! THEN
  jbeam-y2 @ 142 > IF 142 jbeam-y2 ! THEN ;

: draw-jbeam  ( -- )
  jbeam-x1 @ jbeam-y1 @ jbeam-x2 @ jbeam-y2 @ 2 rg-line ;

: erase-jbeam  ( -- )
  jbeam-x1 @ jbeam-y1 @ jbeam-x2 @ jbeam-y2 @ 0 rg-line ;

\ Check if player ship is near the Jovian beam line (same cross-product method)
: jbeam-hit?  ( -- flag )
  jbeam-x2 @ jbeam-x1 @ -
  SHIP-POS 1 + C@ jbeam-y1 @ - *
  jbeam-y2 @ jbeam-y1 @ -
  SHIP-POS C@ jbeam-x1 @ - *
  -
  abs HIT-THRESH < ;

\ Pick a random living Jovian index, or -1 if none alive
VARIABLE pj-result
: pick-jovian  ( -- i|-1 )
  -1 pj-result !
  qjovians @ ?DUP 0= IF -1 EXIT THEN
  rnd                             \ random starting index
  qjovians @ 0 DO
    DUP JOV-DMG + C@ IF           \ alive?
      pj-result @ 0 < IF DUP pj-result ! THEN  \ take first alive
    THEN
    1 + DUP qjovians @ < 0= IF DROP 0 THEN  \ wrap
  LOOP DROP
  pj-result @ ;

\ Fire a red beam from Jovian i toward the player
: fire-jbeam  ( i -- )
  \ Get Jovian position as beam origin
  DUP 2 * JOV-POS + C@ jbeam-x1 !
  2 * JOV-POS + 1 + C@ jbeam-y1 !
  \ Compute angle from Jovian to player (approximate using dx/dy signs + magnitude)
  \ Simple approach: extend line from Jovian through player by 140px
  SHIP-POS C@ jbeam-x1 @ - 4 *     \ dx * 4 (amplify for line extension)
  jbeam-x1 @ + jbeam-x2 !
  SHIP-POS 1 + C@ jbeam-y1 @ - 4 *  \ dy * 4
  jbeam-y1 @ + jbeam-y2 !
  clamp-jbeam
  \ Start timer (render phase draws the beam)
  JBEAM-FRAMES jbeam-timer !
  \ Check if player was hit
  jbeam-hit?              \ math-only, no drawing needed
  IF
    \ Energy damage
    penergy @ JBEAM-DMG - DUP 0 < IF DROP 0 THEN penergy !
    \ System damage: pick random system (0-4), reduce its level
    5 rnd DUP 0= IF DROP pdmg-ion
    ELSE DUP 1 = IF DROP pdmg-warp
    ELSE DUP 2 = IF DROP pdmg-scan
    ELSE DUP 3 = IF DROP pdmg-defl
    ELSE DROP pdmg-masr
    THEN THEN THEN THEN
    DUP @ JBEAM-SYS-DMG - DUP 0 < IF DROP 0 THEN SWAP !
  THEN
  \ Reset cooldown
  jbeam-cooldown jbeam-cool ! ;

\ Tick: count down beam display, count down cooldown, maybe fire
: tick-jbeam  ( -- )
  \ Decay existing beam (render phase handles draw/erase)
  jbeam-timer @ IF
    jbeam-timer @ 1 - jbeam-timer !
  THEN
  \ Cooldown toward next shot
  jbeam-cool @ ?DUP IF
    1 - jbeam-cool !
  ELSE
    \ Cooldown expired — fire if any Jovians alive and not docked
    docked @ 0= IF
      pick-jovian DUP 0 < IF DROP ELSE fire-jbeam THEN
    THEN
  THEN ;

\ ── Magnetic storm: fake stars + event horizon ─────────────────────────
\ In storm quadrants, scatter noise dots across the tactical view.
\ Black hole gravity well (30px Manhattan) is kept clear, and its
\ boundary is outlined so the player can detect it through the static.

: in-grav-well?  ( x y -- flag )
  qbhole @ 0= IF DROP DROP 0 EXIT THEN
  BHOLE-POS 1 + C@ - abs
  SWAP BHOLE-POS C@ - abs + 30 < ;

\ Storm star positions saved at quadrant entry for redraw.
\ Max 25 fake stars (5 real × 5 fake). 3 bytes each: x, y, color.
$7660 CONSTANT FSTAR-POS          \ 25 × 3 = 75 bytes
VARIABLE fstar-count

VARIABLE fs-tmp

: gen-storm-stars  ( -- )
  0 fstar-count !
  pcol @ prow @ gal@ q-storm? 0= IF EXIT THEN
  qstars @ 3 * ?DUP IF 0 DO
    rnd-x rnd-y                  \ ( x y )
    OVER OVER in-grav-well? IF
      DROP DROP
    ELSE
      fstar-count @ 3 * FSTAR-POS + fs-tmp !
      3 rnd 1 + fs-tmp @ 2 + C!  \ color
      fs-tmp @ 1 + C!             \ y
      fs-tmp @ C!                  \ x
      fstar-count @ 1 + fstar-count !
    THEN
  LOOP THEN ;

: draw-storm-stars  ( -- )
  fstar-count @ ?DUP IF 0 DO
    FSTAR-POS I 3 * + C@         \ x
    FSTAR-POS I 3 * + 1 + C@    \ y
    FSTAR-POS I 3 * + 2 + C@    \ color
    rg-pset
  LOOP THEN ;

\ Spiral dot positions precomputed at quadrant entry.
\ 4 arms × 8 dots = 32 dots max. 2 bytes each (x, y).
$7690 CONSTANT SPIRAL-POS         \ 32 × 2 = 64 bytes
VARIABLE spiral-count

VARIABLE sp-r
VARIABLE sp-r2

: gen-spiral-arm  ( start-angle -- )
  20 sp-r2 !                     \ radius*2 = 20 → 10px start
  8 0 DO
    sp-r2 @ 1 RSHIFT sp-r !
    DUP sp-r @ angle-dx BHOLE-POS C@ +
    OVER sp-r @ angle-dy BHOLE-POS 1 + C@ +
    \ Bounds check — skip out-of-range dots ( angle x y )
    DUP 2 < OVER 141 > OR IF
      DROP DROP
    ELSE
      SWAP DUP 2 < OVER 125 > OR IF
        DROP DROP
      ELSE
        SWAP                       \ ( angle x y )
        spiral-count @ 2 * SPIRAL-POS + fs-tmp !
        fs-tmp @ 1 + C!           \ store y
        fs-tmp @ C!               \ store x
        spiral-count @ 1 + spiral-count !
      THEN
    THEN
    20 +
    DUP 360 > IF 360 - THEN
    sp-r2 @ 2 - sp-r2 !
  LOOP DROP ;

: gen-event-horizon  ( -- )
  0 spiral-count !
  qbhole @ 0= IF EXIT THEN
  pcol @ prow @ gal@ q-storm? 0= IF EXIT THEN
  0 gen-spiral-arm
  90 gen-spiral-arm
  180 gen-spiral-arm
  270 gen-spiral-arm ;

: draw-event-horizon  ( -- )
  spiral-count @ IF
    SPIRAL-POS spiral-count @ 1 plot-dots
  THEN ;

: draw-quadrant  ( -- )
  gen-storm-stars gen-event-horizon
  draw-border draw-stars draw-storm-stars draw-event-horizon
  draw-base
  init-jovian-ai
  save-jov-bgs save-jov-oldpos
  draw-jovians-live
  save-ship-bg draw-ship ;

\ ══════════════════════════════════════════════════════════════════════════
\  SHIP MOVEMENT (arrow keys via direct matrix scan)
\ ══════════════════════════════════════════════════════════════════════════

\ Scan arrow keys (all on column 3) and move ship.
\ Bounds: x 4-123, y 4-139 (inside tactical border).

VARIABLE moved                    \ flag: did ship move this frame?
VARIABLE move-count               \ counts moves; energy charged every 4th
VARIABLE prev-energy              \ last displayed energy (for dirty check)
VARIABLE prev-missiles            \ last displayed missile count
VARIABLE prev-docked              \ last displayed dock state
\ Ship speed: 3px at 100% ion engines, 1px at 1-33%, 2px at 34-66%
: ship-dx  ( -- n )
  pdmg-ion @ DUP 67 > IF DROP 3 ELSE 34 > IF 2 ELSE 1 THEN THEN ;
: ship-dy  ( -- n )  ship-dx ;
10 CONSTANT MASER-COST            \ energy per maser fire

: use-energy  ( cost -- )
  penergy @ SWAP - DUP 0 < IF DROP 0 THEN penergy ! ;

\ Check if ship overlaps base (within 5px both axes)
: ship-on-base?  ( -- flag )
  qbase @ 0= IF 0 EXIT THEN
  SHIP-POS C@ BASE-POS C@ - abs 5 <
  SHIP-POS 1 + C@ BASE-POS 1 + C@ - abs 5 < AND ;

VARIABLE was-near-base

: move-ship  ( -- )
  0 moved !
  penergy @ 0= IF EXIT THEN
  ship-on-base? was-near-base !    \ already near? allow escape
  \ Arrow keys: all on row 3 ($08), different columns
  KB-C3 KBD-SCAN $08 AND IF       \ UP: col 3, row 3
    SHIP-POS 1 + C@ SHIP-DY 4 + > IF
      SHIP-POS 1 + C@ SHIP-DY - SHIP-POS 1 + C!
      ship-on-base? was-near-base @ 0= AND IF
        SHIP-POS 1 + C@ SHIP-DY + SHIP-POS 1 + C!
      ELSE 1 moved ! THEN
    THEN
  THEN
  KB-C4 KBD-SCAN $08 AND IF       \ DN: col 4, row 3
    SHIP-POS 1 + C@ 139 SHIP-DY - < IF
      SHIP-POS 1 + C@ SHIP-DY + SHIP-POS 1 + C!
      ship-on-base? was-near-base @ 0= AND IF
        SHIP-POS 1 + C@ SHIP-DY - SHIP-POS 1 + C!
      ELSE 1 moved ! THEN
    THEN
  THEN
  KB-C5 KBD-SCAN $08 AND IF       \ LT: col 5, row 3
    SHIP-POS C@ SHIP-DX 4 + > IF
      SHIP-POS C@ SHIP-DX - SHIP-POS C!
      ship-on-base? was-near-base @ 0= AND IF
        SHIP-POS C@ SHIP-DX + SHIP-POS C!
      ELSE 1 moved ! THEN
    THEN
  THEN
  KB-C6 KBD-SCAN $08 AND IF       \ RT: col 6, row 3
    SHIP-POS C@ 123 SHIP-DX - < IF
      SHIP-POS C@ SHIP-DX + SHIP-POS C!
      ship-on-base? was-near-base @ 0= AND IF
        SHIP-POS C@ SHIP-DX - SHIP-POS C!
      ELSE 1 moved ! THEN
    THEN
  THEN
  moved @ IF
    move-count @ 1 + DUP 32 = IF
      DROP 0  1 use-energy
    THEN move-count !
  THEN ;

\ ── Collision detection (called after move-ship AND gravity-well) ──────

: check-collisions  ( -- )
  moved @ 0= IF EXIT THEN
  \ Star collision: within 3px = destroyed
  qstars @ ?DUP IF 0 DO
    SHIP-POS C@ STAR-POS I 2 * + C@ - abs 3 <
    SHIP-POS 1 + C@ STAR-POS I 2 * + 1 + C@ - abs 3 < AND IF
      0 penergy !
    THEN
  LOOP THEN
  \ Black hole collision: within 3px = vanish instantly
  qbhole @ IF
    SHIP-POS C@ BHOLE-POS C@ - abs 3 <
    SHIP-POS 1 + C@ BHOLE-POS 1 + C@ - abs 3 < AND IF
      1 death-cause !
      0 penergy !
    THEN
  THEN ;

\ ══════════════════════════════════════════════════════════════════════════
\  BLACK HOLE GRAVITY WELL
\ ══════════════════════════════════════════════════════════════════════════
\ Within 30px Manhattan distance, pull ship toward black hole center.
\ Tiered pull rate: edge=every 2 frames, mid=every frame, close=2px/frame.
\ Ship thrust is 3px/frame, so edge is escapable, core is not.

VARIABLE grav-pull
VARIABLE grav-tick

: gravity-well  ( -- )
  qbhole @ 0= IF EXIT THEN
  grav-tick @ 1 + grav-tick !
  SHIP-POS C@ BHOLE-POS C@ - abs
  SHIP-POS 1 + C@ BHOLE-POS 1 + C@ - abs +
  DUP 30 > IF DROP EXIT THEN
  DUP 20 > IF                  \ 20-30: gentle drift, every 4 frames
    DROP grav-tick @ 3 AND IF EXIT THEN 1
  ELSE DUP 10 > IF             \ 10-20: moderate, every 2 frames
    DROP grav-tick @ 1 AND IF EXIT THEN 1
  ELSE 6 > IF                  \ 6-10: every frame
    1
  ELSE                          \ <6: inescapable, 2px/frame
    2
  THEN THEN THEN
  grav-pull !
  1 moved !
  \ Pull X toward black hole
  SHIP-POS C@ BHOLE-POS C@ < IF
    SHIP-POS C@ grav-pull @ + SHIP-POS C!
  THEN
  SHIP-POS C@ BHOLE-POS C@ > IF
    SHIP-POS C@ grav-pull @ - SHIP-POS C!
  THEN
  \ Pull Y toward black hole
  SHIP-POS 1 + C@ BHOLE-POS 1 + C@ < IF
    SHIP-POS 1 + C@ grav-pull @ + SHIP-POS 1 + C!
  THEN
  SHIP-POS 1 + C@ BHOLE-POS 1 + C@ > IF
    SHIP-POS 1 + C@ grav-pull @ - SHIP-POS 1 + C!
  THEN ;

\ ── Star gravity (weaker, smaller) ──────────────────────────────────────
\ 10px radius. Close (<5): pull every 2 frames. Far (5-10): every 4.

VARIABLE sg-i                      \ star gravity loop index
VARIABLE sg-sx                     \ star x cache
VARIABLE sg-sy                     \ star y cache

: star-pull  ( -- )
  1 moved !
  SHIP-POS C@ sg-sx @ < IF  SHIP-POS C@ 1 + SHIP-POS C!  THEN
  SHIP-POS C@ sg-sx @ > IF  SHIP-POS C@ 1 - SHIP-POS C!  THEN
  SHIP-POS 1 + C@ sg-sy @ < IF  SHIP-POS 1 + C@ 1 + SHIP-POS 1 + C!  THEN
  SHIP-POS 1 + C@ sg-sy @ > IF  SHIP-POS 1 + C@ 1 - SHIP-POS 1 + C!  THEN ;

: star-gravity  ( -- )
  qstars @ ?DUP IF 0 DO
    I sg-i !
    STAR-POS sg-i @ 2 * + C@ sg-sx !
    STAR-POS sg-i @ 2 * + 1 + C@ sg-sy !
    SHIP-POS C@ sg-sx @ - abs
    SHIP-POS 1 + C@ sg-sy @ - abs +
    DUP 10 > IF
      DROP
    ELSE
      5 < IF
        grav-tick @ 1 AND 0= IF star-pull THEN
      ELSE
        grav-tick @ 3 AND 0= IF star-pull THEN
      THEN
    THEN
  LOOP THEN ;

\ ══════════════════════════════════════════════════════════════════════════
\  DOCKING
\ ══════════════════════════════════════════════════════════════════════════
\ Dock when ship overlaps a base (within 4px on each axis).
\ Restores energy, missiles, and all ship systems.

: do-dock  ( -- )
  1 docked !
  10 pmissiles ! ;

: do-undock  ( -- )  0 docked ! ;

\ Gradual energy recharge while docked.
\ Inverse log curve: fast when empty, slows every 20%.
VARIABLE dock-tick

: tick-dock  ( -- )
  docked @ 0= IF EXIT THEN
  penergy @ 100 = IF EXIT THEN
  dock-tick @ 1 + dock-tick !
  penergy @ 20 < IF                    \ 0-19%: +1 every frame
    penergy @ 1 + penergy !
  ELSE penergy @ 40 < IF               \ 20-39%: +1 every 2 frames
    dock-tick @ 1 AND 0= IF penergy @ 1 + penergy ! THEN
  ELSE penergy @ 60 < IF               \ 40-59%: +1 every 4 frames
    dock-tick @ 3 AND 0= IF penergy @ 1 + penergy ! THEN
  ELSE penergy @ 80 < IF               \ 60-79%: +1 every 8 frames
    dock-tick @ 7 AND 0= IF penergy @ 1 + penergy ! THEN
  ELSE                                  \ 80-99%: +1 every 16 frames
    dock-tick @ 15 AND 0= IF penergy @ 1 + penergy ! THEN
  THEN THEN THEN THEN
  penergy @ 100 > IF 100 penergy ! THEN
  \ Repair systems: +2 per frame each (0→100 in ~50 frames = 0.8s)
  pdmg-ion  @ 100 < IF pdmg-ion  @ 2 + 100 < IF pdmg-ion  @ 2 + ELSE 100 THEN pdmg-ion  ! THEN
  pdmg-warp @ 100 < IF pdmg-warp @ 2 + 100 < IF pdmg-warp @ 2 + ELSE 100 THEN pdmg-warp ! THEN
  pdmg-scan @ 100 < IF pdmg-scan @ 2 + 100 < IF pdmg-scan @ 2 + ELSE 100 THEN pdmg-scan ! THEN
  pdmg-defl @ 100 < IF pdmg-defl @ 2 + 100 < IF pdmg-defl @ 2 + ELSE 100 THEN pdmg-defl ! THEN
  pdmg-masr @ 100 < IF pdmg-masr @ 2 + 100 < IF pdmg-masr @ 2 + ELSE 100 THEN pdmg-masr ! THEN ;

: check-dock  ( -- )
  qbase @ 0= IF EXIT THEN
  SHIP-POS C@ BASE-POS C@ - abs 8 <
  SHIP-POS 1 + C@ BASE-POS 1 + C@ - abs 8 < AND IF
    docked @ 0= IF do-dock THEN
  ELSE
    docked @ IF do-undock THEN
  THEN ;

\ Redraw condition area (left half of row 17)
: update-cond  ( -- )
  0 17 at-xy  14 0 DO $20 rg-emit LOOP
  sos-active @ IF
    0 17 at-xy
    CHAR S rg-emit CHAR O rg-emit CHAR S rg-emit
    CHAR - rg-emit CHAR B rg-emit CHAR A rg-emit
    CHAR S rg-emit CHAR E rg-emit
    11 17 at-xy
    sos-col @ rg-u.  CHAR , rg-emit  sos-row @ rg-u.
  ELSE
    0 17 at-xy
    CHAR C rg-emit CHAR O rg-emit CHAR N rg-emit CHAR D rg-emit
    14 draw-cond
  THEN ;

\ ══════════════════════════════════════════════════════════════════════════
\  COMMAND INPUT SYSTEM
\ ══════════════════════════════════════════════════════════════════════════
\ State machine: 0=idle (waiting for command key 1-7),
\                1=collecting digits for parameter.
\ Arrow keys continue working during input via move-ship.

VARIABLE cmd-state                \ 0=idle, 1=collecting
VARIABLE cmd-num                  \ active command (1-7)
VARIABLE cmd-val                  \ accumulated parameter value
VARIABLE cmd-digits               \ number of digits entered

: clear-cmd-area  ( -- )
  17 18 at-xy  15 0 DO  $20 rg-emit  LOOP ;

: draw-cmd-prompt  ( -- )
  clear-cmd-area
  17 18 at-xy
  CHAR C rg-emit CHAR O rg-emit CHAR M rg-emit CHAR M rg-emit
  CHAR A rg-emit CHAR N rg-emit CHAR D rg-emit ;

\ ── Maser fire (command 5) ──────────────────────────────────────────────
\ Draw a blue beam from ship at the given angle across the tactical view.
\ Beam persists for BEAM-FRAMES then auto-erases.

VARIABLE beam-x1                  \ beam start (ship pos at fire time)
VARIABLE beam-y1
VARIABLE beam-x2                  \ beam endpoint (clamped)
VARIABLE beam-y2
VARIABLE beam-timer               \ frames remaining until erase (0=none)
VARIABLE beam-drawn               \ 1 = beam pixels currently on screen
12 CONSTANT BEAM-FRAMES

: clamp-beam  ( -- )
  beam-x2 @ 1 < IF 1 beam-x2 ! THEN
  beam-x2 @ 126 > IF 126 beam-x2 ! THEN
  beam-y2 @ 1 < IF 1 beam-y2 ! THEN
  beam-y2 @ 142 > IF 142 beam-y2 ! THEN ;

: erase-beam  ( -- )
  beam-x1 @ beam-y1 @
  beam-x2 @ beam-y2 @
  0 rg-line ;                     \ redraw in black

\ ── Maser hit detection ────────────────────────────────────────────────
\ Cross-product distance: if |dx*(jy-y1) - dy*(jx-x1)| < threshold,
\ the Jovian is near the beam path.

VARIABLE hc-i                     \ hit check loop index
VARIABLE hc-jx
VARIABLE hc-jy
560 CONSTANT HIT-THRESH           \ ~4 pixel hit radius for 140px beam
\ Maser damage scales with system health: 30 at 100%, 3 at 10%
: maser-dmg  ( -- n )  pdmg-masr @ 30 * 100 /MOD SWAP DROP ;

: check-hit  ( -- )
  JOV-DMG hc-i @ + C@ IF
    JOV-POS hc-i @ 2 * + C@ hc-jx !
    JOV-POS hc-i @ 2 * + 1 + C@ hc-jy !
    \ Cross product: dx*(jy-y1) - dy*(jx-x1)
    beam-x2 @ beam-x1 @ -
    hc-jy @ beam-y1 @ - *
    beam-y2 @ beam-y1 @ -
    hc-jx @ beam-x1 @ - *
    -
    abs HIT-THRESH < IF
      \ Hit! Reduce health
      JOV-DMG hc-i @ + C@
      maser-dmg - DUP 0 < IF DROP 0 THEN
      JOV-DMG hc-i @ + C!
      \ If dead, erase sprite, explode, trigger sprite refresh
      JOV-DMG hc-i @ + C@ 0= IF
        SPR-JOV hc-jx @ 3 - hc-jy @ 2 - spr-erase-box
        hc-jx @ hc-jy @ explode-jovian
        1 jov-moved !
      THEN
    THEN
  THEN ;

: check-hits  ( -- )
  qjovians @ ?DUP IF 0 DO
    I hc-i !
    check-hit
  LOOP THEN ;

\ ── Fire maser ─────────────────────────────────────────────────────────

: fire-maser  ( angle -- )
  penergy @ 0= IF DROP EXIT THEN
  MASER-COST use-energy
  \ Erase any existing beam first
  beam-timer @ IF erase-beam THEN
  \ Save ship position as beam origin
  SHIP-POS C@ beam-x1 !  SHIP-POS 1 + C@ beam-y1 !
  \ Calculate and clamp endpoint
  DUP 140 angle-dx beam-x1 @ + beam-x2 !
  140 angle-dy beam-y1 @ + beam-y2 !
  clamp-beam
  \ Start timer (render phase draws the beam)
  BEAM-FRAMES beam-timer !
  check-hits ;

: draw-beam  ( -- )
  beam-x1 @ beam-y1 @ beam-x2 @ beam-y2 @ 1 rg-line ;

: tick-beam  ( -- )
  beam-timer @ IF
    beam-timer @ 1 - beam-timer !
  THEN ;

\ ── Triton missiles (command 6) ─────────────────────────────────────────
\ Animated projectile: alternates + and x sprites as it flies.
\ One-hit kill on Jovian contact.  Limited supply (pmissiles).

VARIABLE msl-x                   \ current missile x (signed, *128 fixed-point)
VARIABLE msl-y                   \ current missile y (signed, *128 fixed-point)
VARIABLE msl-dx                  \ x step per frame (*128)
VARIABLE msl-dy                  \ y step per frame (*128)
VARIABLE msl-px                  \ previous screen x (for erase)
VARIABLE msl-py                  \ previous screen y (for erase)
VARIABLE msl-frame               \ animation frame counter
VARIABLE msl-active              \ nonzero = missile in flight
100 CONSTANT MSL-SPEED           \ pixels per frame
5 CONSTANT MSL-COST              \ energy per missile

: msl-spr  ( -- addr )
  msl-frame @ 1 AND IF SPR-MSL2 ELSE SPR-MSL1 THEN ;

: msl-scrx  ( -- x )  msl-x @ 7 RSHIFT ;
: msl-scry  ( -- y )  msl-y @ 7 RSHIFT ;

: msl-erase  ( -- )  restore-msl-bg ;

: msl-draw  ( -- )
  save-msl-bg
  msl-spr msl-scrx 2 - msl-scry 2 - spr-draw ;

: msl-oob?  ( -- flag )
  msl-scrx 3 < IF 1 EXIT THEN
  msl-scrx 124 > IF 1 EXIT THEN
  msl-scry 3 < IF 1 EXIT THEN
  msl-scry 140 > IF 1 EXIT THEN
  0 ;

\ Check if missile hit a Jovian (within 4px)
VARIABLE msl-hi                  \ hit check index
VARIABLE msl-got                 \ hit flag
: msl-hit?  ( -- flag )
  0 msl-got !
  qjovians @ ?DUP IF 0 DO
    msl-got @ 0= IF
      I msl-hi !
      JOV-DMG msl-hi @ + C@ IF
        JOV-POS msl-hi @ 2 * + C@ msl-scrx - abs 4 <
        JOV-POS msl-hi @ 2 * + 1 + C@ msl-scry - abs 4 < AND IF
          \ Kill Jovian — erase sprite, explode, trigger sprite refresh
          0 JOV-DMG msl-hi @ + C!
          SPR-JOV
          JOV-POS msl-hi @ 2 * + C@ 3 -
          JOV-POS msl-hi @ 2 * + 1 + C@ 2 -
          spr-erase-box
          JOV-POS msl-hi @ 2 * + C@
          JOV-POS msl-hi @ 2 * + 1 + C@
          explode-jovian
          1 jov-moved !
          1 msl-got !
        THEN
      THEN
    THEN
  LOOP THEN
  msl-got @ ;

VARIABLE msl-dirty               \ 1 = needs erase+draw this frame

: tick-missile  ( -- )
  msl-active @ 0= IF EXIT THEN
  \ Advance position
  msl-dx @ msl-x @ + msl-x !
  msl-dy @ msl-y @ + msl-y !
  msl-frame @ 1 + msl-frame !
  \ Check bounds
  msl-oob? IF
    msl-erase  0 msl-active !
    draw-border
    EXIT
  THEN
  \ Check Jovian hit
  msl-hit? IF
    msl-erase  0 msl-active !
    draw-ship
    EXIT
  THEN
  1 msl-dirty ! ;

: fire-missile  ( angle -- )
  pmissiles @ 0= IF DROP EXIT THEN
  msl-active @ IF DROP EXIT THEN
  MSL-COST use-energy
  pmissiles @ 1 - pmissiles !
  \ Start outside ship sprite (offset 8px along firing angle)
  DUP 8 angle-dx SHIP-POS C@ + 7 LSHIFT msl-x !
  DUP 8 angle-dy SHIP-POS 1 + C@ + 7 LSHIFT msl-y !
  \ Compute dx/dy from angle
  DUP MSL-SPEED angle-dx msl-dx !
  MSL-SPEED angle-dy msl-dy !
  0 msl-frame !
  1 msl-active !  1 msl-dirty !
  msl-scrx msl-px !  msl-scry msl-py !
  save-msl-bg ;

\ ── Command dispatch ───────────────────────────────────────────────────

\ ── Hyperdrive (command 2) ─────────────────────────────────────────────
\ Enter 2-digit destination: tens=col, ones=row.  e.g. 35 = col 3, row 5.
\ Energy cost = 5 * Manhattan distance.  Damaged warp drive may mis-jump.

: warp-cost  ( col row -- cost )
  prow @ - abs SWAP pcol @ - abs + 5 * ;

: do-warp  ( -- )
  cmd-val @ 10 /MOD                  \ ( row col )
  \ Validate coordinates
  OVER 8 < OVER 8 < AND 0= IF DROP DROP EXIT THEN
  \ Check if already there
  OVER pcol @ = OVER prow @ = AND IF DROP DROP EXIT THEN
  \ Calculate and pay energy cost
  OVER OVER warp-cost
  DUP penergy @ > IF DROP DROP DROP EXIT THEN   \ not enough energy
  use-energy
  \ Damaged warp: random chance of mis-jump (land in adjacent quadrant)
  pdmg-warp @ 50 < IF
    8 rnd 4 < IF                     \ 50% chance at <50% health
      SWAP 1 + 7 AND SWAP            \ shift col by 1
    THEN
  THEN
  \ Clear beams and missile
  beam-drawn @ IF erase-beam 0 beam-drawn ! THEN
  jbeam-drawn @ IF erase-jbeam 0 jbeam-drawn ! THEN
  0 beam-timer !  0 jbeam-timer !
  msl-active @ IF msl-erase 0 msl-active ! THEN
  \ Expand new quadrant and redraw
  rg-pcls
  SWAP expand-quadrant
  draw-quadrant
  draw-panel
  0 docked !  0 prev-docked !
  0 jov-moved !
  jbeam-cooldown jbeam-cool !
  0 msl-dirty ! ;

: exec-command  ( -- )
  cmd-num @ 2 = IF do-warp THEN
  cmd-num @ 5 = IF cmd-val @ fire-maser THEN
  cmd-num @ 6 = IF cmd-val @ fire-missile THEN
  0 cmd-state !
  draw-cmd-prompt ;

: cmd-start  ( cmd -- )
  cmd-num !
  \ Commands 1, 3: immediate (no parameter)
  cmd-num @ 1 = IF exec-command EXIT THEN
  cmd-num @ 3 = IF exec-command EXIT THEN
  \ Others: clear area once, show "N? ", start digit collection
  1 cmd-state !
  0 cmd-val !  0 cmd-digits !
  clear-cmd-area
  17 18 at-xy
  cmd-num @ CHAR 0 + rg-emit
  CHAR ? rg-emit ;

: cmd-add-digit  ( digit -- )
  cmd-digits @ 3 < IF
    DUP cmd-val @ 10 * + cmd-val !
    cmd-digits @ 1 + cmd-digits !
    \ Position cursor explicitly: col 18 + digit count, row 18
    cmd-digits @ 18 + 18 at-xy
    CHAR 0 + rg-emit
  ELSE
    DROP
  THEN ;

\ Handle key during digit collection
: process-cmd-input  ( key -- )
  DUP $30 < IF
    $0D = IF exec-command THEN
  ELSE
    DUP $3A < IF
      $30 - cmd-add-digit
    ELSE
      DROP
    THEN
  THEN ;

\ Handle key when idle
: process-idle  ( key -- )
  DUP $31 < IF DROP
  ELSE
    DUP $38 < IF
      $30 - cmd-start
    ELSE
      DROP
    THEN
  THEN ;

\ Poll keyboard and dispatch (debounce: only fire on key change)
VARIABLE prev-key                 \ last key seen by KEY?

: process-key  ( -- )
  KEY?
  DUP prev-key @ = IF
    DROP                          \ same or still zero — ignore
  ELSE
    DUP prev-key !
    ?DUP IF
      cmd-state @ IF process-cmd-input ELSE process-idle THEN
    THEN
  THEN ;

\ ══════════════════════════════════════════════════════════════════════════
\  GAME LOOP
\ ══════════════════════════════════════════════════════════════════════════

\ Find first quadrant with a base and expand it
: find-base-quadrant  ( -- )
  64 0 DO
    GALAXY I + C@ q-base? IF
      I 8 /MOD                 \ ( col row )
      expand-quadrant
    THEN
  LOOP ;

\ ── Title screen + level select ────────────────────────────────────────

: title-screen  ( -- )
  rg-pcls
  \ SPACE WARP centered on row 5
  11 5 at-xy
  CHAR S rg-emit CHAR P rg-emit CHAR A rg-emit CHAR C rg-emit CHAR E rg-emit
  $20 rg-emit
  CHAR W rg-emit CHAR A rg-emit CHAR R rg-emit CHAR P rg-emit
  \ LEVEL 1-9 on row 9
  11 9 at-xy
  CHAR L rg-emit CHAR E rg-emit CHAR V rg-emit CHAR E rg-emit CHAR L rg-emit
  $20 rg-emit
  CHAR 1 rg-emit CHAR - rg-emit CHAR 9 rg-emit
  \ Read level key (1-9)
  BEGIN KEY DUP CHAR 1 < OVER CHAR 9 > OR IF DROP 0 ELSE 1 THEN UNTIL
  CHAR 0 - glevel ! ;

\ ── Mission briefing ──────────────────────────────────────────────────

: briefing  ( -- )
  rg-pcls
  \ FROM UP COMMAND
  2 2 at-xy
  CHAR F rg-emit CHAR R rg-emit CHAR O rg-emit CHAR M rg-emit
  $20 rg-emit
  CHAR U rg-emit CHAR P rg-emit $20 rg-emit
  CHAR C rg-emit CHAR O rg-emit CHAR M rg-emit CHAR M rg-emit
  CHAR A rg-emit CHAR N rg-emit CHAR D rg-emit
  \ DESTROY
  2 5 at-xy
  CHAR D rg-emit CHAR E rg-emit CHAR S rg-emit CHAR T rg-emit
  CHAR R rg-emit CHAR O rg-emit CHAR Y rg-emit $20 rg-emit
  gjovians @ rg-u.
  \ JOVIAN SHIPS
  2 7 at-xy
  CHAR J rg-emit CHAR O rg-emit CHAR V rg-emit CHAR I rg-emit
  CHAR A rg-emit CHAR N rg-emit $20 rg-emit
  CHAR S rg-emit CHAR H rg-emit CHAR I rg-emit CHAR P rg-emit CHAR S rg-emit
  \ PROTECT
  2 9 at-xy
  CHAR P rg-emit CHAR R rg-emit CHAR O rg-emit CHAR T rg-emit
  CHAR E rg-emit CHAR C rg-emit CHAR T rg-emit $20 rg-emit
  gbases @ rg-u.
  $20 rg-emit
  CHAR B rg-emit CHAR A rg-emit CHAR S rg-emit CHAR E rg-emit CHAR S rg-emit
  \ GOOD LUCK
  14 13 at-xy
  CHAR G rg-emit CHAR O rg-emit CHAR O rg-emit CHAR D rg-emit
  $20 rg-emit
  CHAR L rg-emit CHAR U rg-emit CHAR C rg-emit CHAR K rg-emit
  KEY DROP ;

: main  ( -- )
  rg-init
  init-text
  init-sin
  12345 seed !
  0 sos-active !

  \ Title + level select
  title-screen

  \ Generate galaxy with selected level
  glevel @ gen-galaxy

  \ Mission briefing
  briefing
  rg-pcls

  \ Initialize game state
  init-sprites
  init-player
  find-base-quadrant

  \ Draw initial tactical view and status panel
  draw-quadrant
  draw-panel
  0 cmd-state !  0 prev-key !
  0 beam-timer !  0 beam-drawn !
  0 jbeam-timer !  0 jbeam-drawn !  jbeam-cooldown jbeam-cool !
  0 msl-active !  0 msl-dirty !
  0 docked !  0 prev-docked !  0 death-cause !
  0 jov-moved !
  100 prev-energy !
  10 prev-missiles !

  \ Game loop
  BEGIN
    save-ship-pos
    move-ship
    gravity-well
    star-gravity
    check-collisions
    check-dock
    tick-dock
    tick-beam
    tick-missile
    tick-jovians
    jov-gravity
    tick-jbeam
    process-key
    VSYNC
    \ Erase any drawn beams so bg-save stays clean
    beam-drawn @ IF erase-beam 0 beam-drawn ! THEN
    jbeam-drawn @ IF erase-jbeam 0 jbeam-drawn ! THEN
    moved @ jov-moved @ OR IF
      restore-ship-bg
      restore-jov-bgs
      jov-moved @ IF save-jov-oldpos THEN
      save-jov-bgs
      draw-jovians-live
      save-ship-bg
      draw-ship
      0 jov-moved !
    THEN
    \ Missile render (between beam erase and beam redraw)
    msl-dirty @ IF
      msl-erase
      msl-scrx msl-px !  msl-scry msl-py !
      msl-draw
      0 msl-dirty !
    THEN
    \ Redraw active beams on top of everything (last layer)
    beam-timer @ IF draw-beam 1 beam-drawn ! THEN
    jbeam-timer @ IF draw-jbeam 1 jbeam-drawn ! THEN
    penergy @ prev-energy @ <> IF
      penergy @ prev-energy !
      update-energy
    THEN
    pmissiles @ prev-missiles @ <> IF
      pmissiles @ prev-missiles !
      update-missiles
    THEN
    docked @ prev-docked @ <> IF
      docked @ prev-docked !
      update-cond
    THEN
    penergy @ 0= IF
      SHIP-POS C@ SHIP-POS 1 + C@
      death-cause @ IF
        \ Black hole — ship vanishes, no explosion
        restore-ship-bg
        DROP DROP
        0 17 at-xy
        CHAR B rg-emit CHAR L rg-emit CHAR A rg-emit CHAR C rg-emit
        CHAR K rg-emit  $20 rg-emit
        CHAR H rg-emit CHAR O rg-emit CHAR L rg-emit CHAR E rg-emit
      ELSE
        \ Energy depleted or star collision — ship explodes
        restore-ship-bg
        explode-ship
        0 17 at-xy
        CHAR D rg-emit CHAR E rg-emit CHAR S rg-emit CHAR T rg-emit
        CHAR R rg-emit CHAR O rg-emit CHAR Y rg-emit CHAR E rg-emit
        CHAR D rg-emit
      THEN
      \ AGAIN? prompt — any key restarts
      0 18 at-xy
      CHAR A rg-emit CHAR G rg-emit CHAR A rg-emit CHAR I rg-emit
      CHAR N rg-emit CHAR ? rg-emit
      KEY DROP
      main EXIT
    THEN
  AGAIN ;

main
