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
INCLUDE ../../forth/lib/beam.fs

: min  ( a b -- smaller )  2DUP > IF SWAP THEN DROP ;

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
\ Galaxy: 8x8 = 64 quadrants, 1 byte each at GALAXY ($7640).
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

\ Game data starts at $7640 (above app code).
$7640 CONSTANT GALAXY          \ 64 bytes: 8x8 quadrant data

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
$7680 CONSTANT STAR-POS        \ 5 stars x 2 bytes = 10 bytes
$768A CONSTANT JOV-POS         \ 3 jovians x 2 bytes = 6 bytes
$7690 CONSTANT BASE-POS        \ 1 base x 2 bytes = 2 bytes
$7692 CONSTANT BHOLE-POS       \ 1 black hole x 2 bytes = 2 bytes
$7694 CONSTANT SHIP-POS        \ player ship x 2 bytes = 2 bytes

\ Jovian damage (3 bytes, one per jovian: 100=full health, 0=dead)
$7696 CONSTANT JOV-DMG         \ 3 bytes

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

$7740 CONSTANT SPR-SHIP           \ Endever: blue chevron (12 bytes)
$774C CONSTANT SPR-JOV            \ Jovian: red diamond (12 bytes)
$7758 CONSTANT SPR-BASE           \ UP base: blue cross (12 bytes)
$7764 CONSTANT SPR-MSL1           \ Missile frame 1: + shape (12 bytes)
$7770 CONSTANT SPR-MSL2           \ Missile frame 2: x shape (12 bytes)

\ ── Jovian AI data structures ────────────────────────────────────────────
\ Per-Jovian sprite + bg buffers (packed before GALAXY at $7640)
$75A4 CONSTANT JOV-BG0          \ 28 bytes: bg save buffer Jovian 0 (4x7)
$75C0 CONSTANT JOV-BG1          \ 28 bytes: bg save buffer Jovian 1
$75DC CONSTANT JOV-BG2          \ 28 bytes: bg save buffer Jovian 2
$75F8 CONSTANT JOV-SPR0         \ 23 bytes: generated sprite Jovian 0
$760F CONSTANT JOV-SPR1         \ 23 bytes: generated sprite Jovian 1
$7626 CONSTANT JOV-SPR2         \ 23 bytes: generated sprite Jovian 2
$763D CONSTANT JOV-EMCOL        \ 3 bytes: cached emotion color band

$777C CONSTANT JOV-STATE        \ 3 bytes: 0=attack, 1=flee, 2=idle
$777F CONSTANT JOV-TICK         \ 3 bytes: per-Jovian frame counter
$77BE CONSTANT JOV-OLDX         \ 3 bytes: previous x per Jovian
$77C1 CONSTANT JOV-OLDY         \ 3 bytes: previous y per Jovian

\ ── Genome data (AI diversity system) ──────────────────────────────────
\ 4 bytes per Jovian: behavior(2) + appearance(1) + emotion|origin(1)
$77C5 CONSTANT JOV-GENOME       \ 12 bytes: 3 Jovians x 4 bytes
\ Intent output from jov-think: dx, dy, flags per Jovian
$77D1 CONSTANT JOV-INTENT       \ 9 bytes: 3 Jovians x 3 bytes
\ Sprite generation workspace
$77DA CONSTANT JOV-SPRWORK      \ 12 bytes: scratch for sprite gen
\ Quadrant mood grid (8x8 sectors, emotion persistence)
$7E00 CONSTANT MOOD-GRID        \ 64 bytes: mood per sector

: jov-spr  ( i -- addr )  23 * JOV-SPR0 + ;
: jov-bg  ( i -- addr )  28 * JOV-BG0 + ;

\ Dynamic centering offsets from sprite header
: jov-draw-dx  ( i -- dx )  jov-spr C@ 1 RSHIFT ;
: jov-draw-dy  ( i -- dy )  jov-spr 1 + C@ 1 RSHIFT ;

: init-sprites  ( -- )  sprite-data SPR-SHIP 60 CMOVE ;

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
      1 gbases +!
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
  2DUP gal@                    \ ( col row qbyte )

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
  64 SHIP-POS C!  72 SHIP-POS 1 + C! ;

VARIABLE ss-safe

\ Relocate ship if too close to stars or black hole
: safe-spawn  ( -- )
  16 0 DO
    1 ss-safe !
    qstars @ ?DUP IF 0 DO
      SHIP-POS C@ STAR-POS I 2 * + C@ - abs
      SHIP-POS 1 + C@ STAR-POS I 2 * + 1 + C@ - abs +
      35 < IF 0 ss-safe ! THEN
    LOOP THEN
    qbhole @ IF
      SHIP-POS C@ BHOLE-POS C@ - abs
      SHIP-POS 1 + C@ BHOLE-POS 1 + C@ - abs +
      35 < IF 0 ss-safe ! THEN
    THEN
    ss-safe @ 0= IF
      rnd-x SHIP-POS C!  rnd-y SHIP-POS 1 + C!
    THEN
  LOOP ;

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
  0 gtime !  0 stardate-timer !
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
  $9000 $75 !                   \ font base (all-RAM region)
  $20 $77 C!                    \ min char (space)
  8 $78 C!                      \ bytes per glyph
  8 $79 C!                      \ rows to copy
  32 $7A C!                     \ bytes per VRAM row
  10 $7B C!                     \ row height for cy
  $F8 set-pia ;                 \ CSS=1: buff/white for NTSC artifacts

VARIABLE tcx
VARIABLE tcy
: at-xy  ( cx cy -- )  tcy ! tcx ! ;
: rg-emit  ( char -- )  tcx @ tcy @ rg-char  1 tcx +! ;

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
    S" DOCKED" rg-type
  ELSE
    qjovians @ IF
      3 - tcx !
      S" RED" rg-type
    ELSE
      5 - tcx !
      S" GREEN" rg-type
    THEN
  THEN ;

: draw-panel  ( -- )
  clear-panel

  \ Left labels col 0, left values right-align to col 14
  \ Right labels col 17, right values right-align to col 32

  \ Row 15: STARDATE      n  MISSILES      nn
  0 15 at-xy
  S" STARDATE" rg-type
  gtime @ 14 rg-u.r

  17 15 at-xy
  S" MISSILES" rg-type
  pmissiles @ 32 rg-u.r

  \ Row 16: QUADRANT    n n  ENERGY       nnn
  0 16 at-xy
  S" QUADRANT" rg-type
  11 16 at-xy  pcol @ rg-u.  CHAR , rg-emit  prow @ rg-u.

  17 16 at-xy
  S" ENERGY" rg-type
  penergy @ 32 rg-u.r

  \ Row 17: COND/SOS left, SHIELDS right
  sos-active @ IF
    0 17 at-xy
    S" SOS-BASE" rg-type
    11 17 at-xy
    sos-col @ rg-u.  CHAR , rg-emit  sos-row @ rg-u.
  ELSE
    0 17 at-xy
    S" COND" rg-type
    14 draw-cond
  THEN

  17 17 at-xy
  S" SHIELDS" rg-type
  pshields @ 32 rg-u.r

  \ Row 18: COMMAND prompt
  17 18 at-xy
  S" COMMAND" rg-type ;

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
    I jov-spr-xy spr-draw
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
$7710 CONSTANT SHIP-BG              \ 20-byte save buffer

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
$7724 CONSTANT MSL-BG                \ 20-byte save buffer

: save-msl-bg  ( -- )
  MSL-BG msl-scrx 2 - msl-scry 2 - bg-save ;

: restore-msl-bg  ( -- )
  MSL-BG msl-px @ 2 - msl-py @ 2 - bg-restore ;

\ ── Flicker-free Jovian background save/restore ─────────────────────────
\ 4x7 bg save/restore for variable-height Jovian sprites.

CODE bg-save-7   \ ( buf x y -- )  save 4x7 VRAM bytes to buf
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
        LDB     #7
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

CODE bg-restore-7  \ ( buf x y -- )  restore 4x7 VRAM bytes from buf
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
        LDB     #7
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

VARIABLE jbg-i

\ CODE helpers: compute sprite/bg addr + centered (x,y) for Jovian i.
\ Inlines jov-spr/jov-bg/jov-draw-dx/jov-draw-dy into register ops.

CODE jov-spr-xy   \ ( i -- spr x y )
        PSHS    X               ; save IP
        LDA     1,U             ; A = i
        LEAU    -4,U            ; grow stack by 2 cells
        PSHS    A               ; save i
        LDB     #23
        MUL
        ADDD    #$75F8          ; JOV-SPR0
        STD     4,U             ; spr addr
        TFR     D,Y             ; Y = spr header
        LDA     ,S+             ; i
        ASLA                    ; A = i*2
        PSHS    A               ; save i*2
        LDX     #$768A          ; JOV-POS
        ; x = JOV-POS[i*2] - width/2
        LDB     ,Y              ; width
        LSRB
        NEGB
        ADDB    A,X             ; B = pos_x - width/2
        CLRA
        STD     2,U             ; x
        ; y = JOV-POS[i*2+1] - height/2
        LDA     ,S+             ; i*2
        INCA                    ; i*2+1
        LDB     1,Y             ; height
        LSRB
        NEGB
        ADDB    A,X             ; B = pos_y - height/2
        CLRA
        STD     ,U              ; y
        PULS    X
        ;NEXT
;CODE

CODE jov-bg-xy   \ ( i -- bg x y )
        PSHS    X               ; save IP
        LDA     1,U             ; A = i
        LEAU    -4,U            ; grow stack by 2 cells
        PSHS    A               ; save i
        ; bg addr = i * 28 + JOV-BG0
        LDB     #28
        MUL
        ADDD    #$75A4          ; JOV-BG0
        STD     4,U             ; bg addr
        ; sprite header for centering: i * 23 + JOV-SPR0
        LDA     ,S+             ; i
        PSHS    A               ; save i again
        LDB     #23
        MUL
        ADDD    #$75F8          ; JOV-SPR0
        TFR     D,Y             ; Y = spr header
        LDA     ,S+             ; i
        ASLA                    ; i*2
        PSHS    A               ; save i*2
        LDX     #$768A          ; JOV-POS
        LDB     ,Y              ; width
        LSRB
        NEGB
        ADDB    A,X             ; pos_x - width/2
        CLRA
        STD     2,U
        LDA     ,S+             ; i*2
        INCA
        LDB     1,Y             ; height
        LSRB
        NEGB
        ADDB    A,X             ; pos_y - height/2
        CLRA
        STD     ,U
        PULS    X
        ;NEXT
;CODE

CODE jov-bg-old-xy   \ ( i -- bg oldx oldy )
        PSHS    X               ; save IP
        LDA     1,U             ; A = i
        LEAU    -4,U
        PSHS    A               ; save i
        LDB     #28
        MUL
        ADDD    #$75A4          ; JOV-BG0
        STD     4,U             ; bg addr
        LDA     ,S+             ; i
        PSHS    A
        LDB     #23
        MUL
        ADDD    #$75F8          ; JOV-SPR0
        TFR     D,Y             ; Y = spr header (for width/height)
        LDA     ,S+             ; i
        PSHS    A               ; save i
        ; oldx = JOV-OLDX[i] - width/2
        LDX     #$77BE          ; JOV-OLDX
        LDB     ,Y              ; width
        LSRB
        NEGB
        ADDB    A,X             ; OLDX[i] - width/2
        CLRA
        STD     2,U
        ; oldy = JOV-OLDY[i] - height/2
        LDA     ,S+             ; i
        LDX     #$77C1          ; JOV-OLDY
        LDB     1,Y             ; height
        LSRB
        NEGB
        ADDB    A,X             ; OLDY[i] - height/2
        CLRA
        STD     ,U
        PULS    X
        ;NEXT
;CODE

CODE save-jov-oldpos-n   \ ( n -- )  copy JOV-POS to JOV-OLDX/Y for n Jovians
        PSHS    X               ; save IP
        LDB     1,U             ; B = count
        LEAU    2,U             ; pop
        TSTB
        BEQ     @done
        LDX     #$768A          ; JOV-POS
        LDY     #$77BE          ; JOV-OLDX
@loop   LDA     ,X+             ; pos_x
        STA     ,Y              ; OLDX[i]
        LDA     ,X+             ; pos_y
        STA     3,Y             ; OLDY[i] (JOV-OLDY = JOV-OLDX + 3)
        LEAY    1,Y
        DECB
        BNE     @loop
@done   PULS    X
        ;NEXT
;CODE

: save-jov-bgs  ( -- )
  qjovians @ ?DUP IF 0 DO
    JOV-DMG I + C@ IF I jov-bg-xy bg-save-7 THEN
  LOOP THEN ;

: restore-jov-bgs  ( -- )
  qjovians @ ?DUP IF 0 DO
    JOV-DMG I + C@ IF I jov-bg-old-xy bg-restore-7 THEN
  LOOP THEN ;

: save-jov-oldpos  ( -- )  qjovians @ save-jov-oldpos-n ;

: draw-jovians-live  ( -- )
  qjovians @ ?DUP IF 0 DO
    JOV-DMG I + C@ IF I jov-spr-xy spr-draw THEN
  LOOP THEN ;

: init-jovian-ai  ( -- )
  qjovians @ ?DUP IF 0 DO
    I jbg-i !
    0 JOV-STATE jbg-i @ + C!
    0 JOV-TICK jbg-i @ + C!
    JOV-POS jbg-i @ 2 * + C@ JOV-OLDX jbg-i @ + C!
    JOV-POS jbg-i @ 2 * + 1 + C@ JOV-OLDY jbg-i @ + C!
  LOOP THEN ;

\ ── Genome generation ──────────────────────────────────────────────────
\ Generate 4 genome bytes per Jovian at spawn time.
\ Difficulty (glevel 1-10) biases aggression, pilot skill, and speed
\ upward.  Always with variance so no two Jovians are identical.
\
\ Byte layout (see AI_DIVERSITY_STRATEGY.md):
\   0-1: behavior genome (16 bits, set at spawn, never changes)
\   2:   appearance seed  (random, cosmetic only)
\   3:   emotion (hi nibble) | origin (lo nibble)
\
\ Difficulty bias: trait = clamp( random + glevel_shift, 0, max )
\   aggression (0-7): base random 0-7, + (glevel-1)/2
\   pilot_skill (0-7): base random 0-7, + (glevel-1)/2
\   speed_mod (0-3): base random 0-3, + (glevel-1)/3
\   initiative, path, hand, regularity: pure random (no bias)
\   emotion: seeded from aggression (aggressive=high, peaceful=low)

: clamp7  ( n -- 0..7 )  DUP 7 > IF DROP 7 THEN ;
: clamp3  ( n -- 0..3 )  DUP 3 > IF DROP 3 THEN ;

: gen-genome  ( i -- )
  4 *  JOV-GENOME +            \ addr = base + i*4

  \ -- Byte 0: aggression(3) | initiative(2) | pilot_skill_hi(3) --
  8 rnd  glevel @ 1 - 1 RSHIFT  +  clamp7   \ aggression (biased)
  DUP >R                       \ R: aggression (for emotion seed)
  3 LSHIFT                     \ shift to bits 7-5
  4 rnd  2 LSHIFT  OR          \ initiative (bits 4-3)
  8 rnd  glevel @ 1 - 1 RSHIFT  +  clamp7  \ pilot_skill (biased)
  OR                           \ combine into byte 0
  OVER C!                      \ store byte 0

  \ -- Byte 1: path(2) | speed(2) | hand(2) | regularity(2) --
  4 rnd  6 LSHIFT              \ path style (bits 7-6)
  4 rnd  glevel @ 1 - 3 /MOD SWAP DROP  +  clamp3  \ speed_mod (biased)
  4 LSHIFT  OR                 \ speed (bits 5-4)
  4 rnd  2 LSHIFT  OR          \ handedness (bits 3-2)
  4 rnd  OR                    \ regularity (bits 1-0)
  OVER 1 + C!                  \ store byte 1

  \ -- Byte 2: appearance seed (pure random) --
  256 rnd  OVER 2 + C!

  \ -- Byte 3: emotion (hi nibble) | origin (lo nibble) --
  R>                            \ recover aggression (0-7)
  2 *  8 +                     \ map 0-7 → 8-22, center at neutral
  DUP 15 > IF DROP 15 THEN    \ clamp to 0-15
  4 LSHIFT                     \ emotion in hi nibble
  pcol @ prow @ + $0F AND     \ origin = sector hash (0-15)
  OR
  SWAP 3 + C! ;                \ store byte 3

: gen-genomes  ( -- )
  qjovians @ ?DUP IF 0 DO
    I gen-genome
  LOOP THEN ;

\ ── Emotion system ─────────────────────────────────────────────────────
\ Emotion = byte 3 high nibble of genome (0-15).
\ 0-3=fear/panic, 4-7=uneasy, 8-11=neutral/alert, 12-15=angry/enraged.
\ Decays toward genome baseline every 120 frames (~2s).
\ Stimuli shift emotion immediately; clamped to 0-15.

: jov-emotion@  ( i -- e )
  4 * JOV-GENOME + 3 + C@ 4 RSHIFT ;

CODE jov-emotion!   \ ( e i -- )
        LDA     1,U             ; A = i
        LDB     #4
        MUL
        ADDD    #$77C8          ; JOV-GENOME + 3
        TFR     D,Y             ; Y = byte 3 addr
        LDA     3,U             ; A = e (low byte)
        LEAU    4,U             ; pop 2 args
        BPL     @pos
        CLRA                    ; clamp negative to 0
        BRA     @cldn
@pos    CMPA    #15
        BLS     @cldn
        LDA     #15             ; clamp to 15
@cldn   ASLA
        ASLA
        ASLA
        ASLA                    ; A = emotion << 4
        PSHS    A               ; save shifted emotion
        LDB     ,Y              ; B = current byte 3
        ANDB    #$0F            ; preserve low nibble
        ORB     ,S+             ; combine
        STB     ,Y              ; write back
        ;NEXT
;CODE

\ Genome baseline: aggression (byte 0 bits 7-5) mapped to emotion center
: jov-emotion-base  ( i -- e )
  4 * JOV-GENOME + C@ 5 RSHIFT    \ aggression 0-7
  2 * 8 + DUP 15 > IF DROP 15 THEN ;

\ Drift 1 step toward baseline
: jov-emotion-decay  ( i -- )
  DUP jov-emotion@ OVER jov-emotion-base  \ ( i cur base )
  2DUP = IF 2DROP DROP EXIT THEN          \ at baseline, done
  < IF 1 ELSE -1 THEN                     \ +1 if cur<base, -1 if cur>base
  OVER jov-emotion@ + SWAP jov-emotion! ;

\ Apply stimulus (signed delta) to one Jovian
: jov-emotion-stim  ( delta i -- )
  DUP jov-emotion@ ROT + SWAP jov-emotion! ;

\ Apply stimulus to all living Jovians
: jov-emotion-all  ( delta -- )
  qjovians @ ?DUP IF 0 DO
    JOV-DMG I + C@ IF
      DUP I jov-emotion-stim
    THEN
  LOOP THEN DROP ;

\ Decay all living Jovians (called every 120 frames)
: jov-emotion-decay-all  ( -- )
  qjovians @ ?DUP IF 0 DO
    JOV-DMG I + C@ IF
      I jov-emotion-decay
    THEN
  LOOP THEN ;

120 CONSTANT EMOTION-DECAY-RATE   \ frames between decay ticks
VARIABLE emotion-timer            \ frame counter for decay

\ ── Procedural Jovian sprite generation ────────────────────────────────
\ Each Jovian gets a unique sprite from its genome appearance seed.
\ Shape from seed, color from emotion band, density from origin.

\ Emotion → band index: 0=fear, 1=neutral, 2=rage
: jov-color-band  ( emo -- band )
  DUP 5 < IF DROP 0 ELSE
  10 > IF 2 ELSE 1 THEN THEN ;

\ Generate sprite for Jovian i from its genome (all-in-one CODE word).
\ Reads appearance seed, computes dimensions + color, generates pixels
\ with per-row PRNG bit pattern + mirror.  Uses JOV-SPRWORK as scratch.
CODE gen-jov-sprite   \ ( i -- )
        PSHS    X               ; save IP
        LDA     1,U             ; A = i (low byte)
        LEAU    2,U             ; pop arg
        ; --- Sprite buffer addr = i * 23 + JOV-SPR0 ---
        PSHS    A               ; save i
        LDB     #23
        MUL
        ADDD    #$75F8          ; JOV-SPR0
        STD     $77DA           ; SPRWORK+0 = sprite addr
        ; --- Genome addr = i * 4 + JOV-GENOME ---
        LDA     ,S+             ; restore i
        LDB     #4
        MUL
        ADDD    #$77C5          ; JOV-GENOME
        TFR     D,Y             ; Y = genome ptr
        ; --- Seed (byte 2) → PRNG state, width, height ---
        LDA     2,Y             ; appearance seed
        STA     $77DF           ; PRNG state
        ; Width from bits 7-6: 00=5, 01=7, 10=7, 11=9
        TFR     A,B             ; save seed in B
        LSRA
        LSRA
        LSRA
        LSRA
        LSRA
        LSRA                    ; A = seed >> 6
        CMPA    #0
        BNE     @nw5
        LDA     #5
        BRA     @wdn
@nw5    CMPA    #3
        BNE     @w7
        LDA     #9
        BRA     @wdn
@w7     LDA     #7
@wdn    STA     $77E0           ; width
        LDX     $77DA
        STA     ,X              ; sprite header byte 0
        ; Height from bits 5-4: 00/01=5, 10/11=7
        TFR     B,A             ; restore seed
        LSRA
        LSRA
        LSRA
        LSRA
        ANDA    #$03
        CMPA    #2
        BHS     @h7
        LDA     #5
        BRA     @hdn
@h7     LDA     #7
@hdn    STA     $77E1           ; height
        STA     1,X             ; sprite header byte 1
        ; Half-width = (width + 1) / 2
        LDA     $77E0
        INCA
        LSRA
        STA     $77DD           ; half_width
        ; --- Emotion (byte 3 high nibble) → 2bpp color ---
        LDA     3,Y             ; emotion|origin
        LSRA
        LSRA
        LSRA
        LSRA                    ; A = emotion 0-15
        CMPA    #5
        BLO     @cblue
        CMPA    #11
        BLO     @cwht
        LDA     #2              ; red (rage)
        BRA     @cldn
@cblue  LDA     #1              ; blue (fear)
        BRA     @cldn
@cwht   LDA     #3              ; white (neutral)
@cldn   STA     $77DC           ; 2bpp color
        ; === Clear sprite data ===
        LDX     $77DA
        LEAX    2,X
        LDA     $77E0
        ADDA    #3
        LSRA
        LSRA                    ; A = bpr
        LDB     $77E1
        MUL
        TSTB
        BEQ     @clrdn
@clrlp  CLR     ,X+
        DECB
        BNE     @clrlp
@clrdn
        ; === Per-row pixel generation ===
        CLR     $77E3           ; row = 0
@rowlp  LDB     $77DF           ; PRNG state
        LDA     #5
        MUL
        ADDB    #3
        STB     $77DF
        LDA     $77E3
        STA     $77E5           ; row for @setpx
        CLR     $77E2           ; col = 0
@collp  LDA     $77DF           ; state
        LDB     $77E2
        BEQ     @nsh
@shlp   LSRA
        DECB
        BNE     @shlp
@nsh    BITA    #$01
        BEQ     @nopx
        LDA     $77E2
        STA     $77E4           ; col for @setpx
        BSR     @setpx
        LDA     $77DD           ; half_width
        DECA
        CMPA    $77E2           ; center?
        BEQ     @nopx
        LDA     $77E0           ; width
        DECA
        SUBA    $77E2           ; mirror col
        STA     $77E4
        BSR     @setpx
@nopx   INC     $77E2
        LDA     $77E2
        CMPA    $77DD
        BCS     @collp
        INC     $77E3
        LDA     $77E3
        CMPA    $77E1
        BCS     @rowlp
        ; === Center column guarantee ===
        LDA     $77DD
        DECA
        STA     $77E4
        LDA     $77E1
        LSRA
        STA     $77E5
        BSR     @setpx
        ;
        PULS    X
        ;NEXT
        ;
@setpx  LDA     $77E0
        ADDA    #3
        LSRA
        LSRA
        LDB     $77E5
        MUL
        PSHS    D
        LDB     $77E4
        LSRB
        LSRB
        CLRA
        ADDD    ,S++
        LDX     $77DA
        LEAX    2,X
        LEAX    D,X
        LDA     $77E4
        ANDA    #$03
        NEGA
        ADDA    #3
        ASLA
        LDB     $77DC
        TSTA
        BEQ     @sns
@ssh    ASLB
        DECA
        BNE     @ssh
@sns    ORB     ,X
        STB     ,X
        RTS
;CODE

\ Generate sprites for all Jovians in current quadrant
: gen-jov-sprites  ( -- )
  qjovians @ ?DUP IF 0 DO
    I gen-jov-sprite
    I jov-emotion@ jov-color-band
    I JOV-EMCOL + C!               \ cache initial band
  LOOP THEN ;

\ Check if any Jovian's emotion crossed a color band → regenerate sprite
: jov-check-regen  ( -- )
  qjovians @ ?DUP IF 0 DO
    JOV-DMG I + C@ IF
      I jov-emotion@ jov-color-band
      I JOV-EMCOL + C@ <> IF
        I gen-jov-sprite
        I jov-emotion@ jov-color-band I JOV-EMCOL + C!
      THEN
    THEN
  LOOP THEN ;

\ ── Quadrant mood persistence ──────────────────────────────────────────
\ MOOD-GRID (64 bytes at $7E00): one byte per sector, 0-15 scale.
\ Saved on quadrant exit (aggregate Jovian emotions), loaded on entry
\ (seeds starting emotion).  Decays toward neutral (8) each stardate.
\ Unvisited quadrants drift aggressive.

\ Mood grid address for quadrant (col, row)
: mood-addr  ( col row -- addr )  8 * + MOOD-GRID + ;

\ Save current quadrant mood: average emotion of living Jovians
: mood-save  ( -- )
  0 0                              \ ( sum count )
  qjovians @ ?DUP IF 0 DO
    JOV-DMG I + C@ IF
      SWAP I jov-emotion@ + SWAP 1 +
    THEN
  LOOP THEN
  DUP 0= IF 2DROP EXIT THEN       \ no living Jovians, keep old mood
  /MOD SWAP DROP                   \ average
  pcol @ prow @ mood-addr C! ;

\ Load mood into spawned Jovians: bias starting emotion from mood byte
: mood-load  ( -- )
  pcol @ prow @ mood-addr C@       \ mood 0-15
  DUP 8 = IF DROP EXIT THEN       \ neutral, no bias needed
  8 -                              \ delta from neutral (-8 to +7)
  qjovians @ ?DUP IF 0 DO
    DUP I jov-emotion-stim
  LOOP THEN DROP ;

3600 CONSTANT STARDATE-FRAMES     \ ~60 seconds per stardate
VARIABLE stardate-timer            \ frame counter

\ Decay all mood bytes 1 step toward neutral (8)
\ Unvisited (still at init value 8) drift to 9 (slightly aggressive)
: mood-decay-all  ( -- )
  64 0 DO
    MOOD-GRID I + C@
    DUP 8 = IF 1 +                \ neutral drifts aggressive
    ELSE DUP 8 > IF 1 -           \ above neutral: decay down
    ELSE 1 +                       \ below neutral: decay up
    THEN THEN
    MOOD-GRID I + C!
  LOOP ;

\ Stardate tick: increment gtime, decay mood grid
: tick-stardate  ( -- )
  stardate-timer @ 1 + DUP STARDATE-FRAMES < IF
    stardate-timer !
  ELSE
    DROP 0 stardate-timer !
    1 gtime +!
    mood-decay-all
  THEN ;

\ ── Detection & awareness ──────────────────────────────────────────────
\ Jovians start idle (JOV-STATE=0) on quadrant entry.  Every 30 frames,
\ idle Jovians roll detection: (pilot_skill + emotion) * 4 >= distance.
\ On detection → JOV-STATE=1 (attack) + distance-scaled alarm stimulus.
\ Firing maser/missile instantly reveals player to all Jovians.

30 CONSTANT DETECT-RATE           \ frames between detection rolls

\ Manhattan distance from Jovian i to player
: jov-player-dist  ( i -- d )
  2 * JOV-POS + DUP C@ SHIP-POS C@ - abs
  SWAP 1 + C@ SHIP-POS 1 + C@ - abs + ;

\ Detection range from genome: (pilot_skill + emotion) * 4
: jov-detect-range  ( i -- r )
  DUP 4 * JOV-GENOME + C@ 7 AND  \ pilot_skill (0-7)
  SWAP jov-emotion@               \ emotion (0-15)
  + 4 * ;                         \ range in pixels

\ Detection roll: returns 1 if player detected
: jov-detect?  ( i -- flag )
  DUP jov-detect-range
  SWAP jov-player-dist
  > ;                             \ detect if range > distance

\ Reveal player to all living Jovians + distance-scaled alarm
: jov-reveal-all  ( -- )
  qjovians @ ?DUP IF 0 DO
    JOV-DMG I + C@ IF
      1 JOV-STATE I + C!
    THEN
  LOOP THEN ;

\ On detection: set aware + alarm stimulus scaled by distance
\ Close (<15px): ±4, medium (15-40): ±2, far (>40): ±1
\ Sign: aggressive (aggr>=4) → positive, peaceful → negative
: jov-on-detect  ( i -- )
  DUP 1 SWAP JOV-STATE + C!      \ set JOV-STATE = attack
  DUP jov-player-dist             \ ( i dist )
  DUP 15 < IF DROP 4
  ELSE 40 < IF 2
  ELSE 1
  THEN THEN                       \ ( i magnitude )
  SWAP DUP 4 * JOV-GENOME + C@ 5 RSHIFT  \ ( mag i aggr )
  4 < IF SWAP NEGATE SWAP         \ peaceful: fear (negative)
  THEN jov-emotion-stim ;         \ apply stimulus

\ Roll detection for all idle Jovians
: jov-detect-tick  ( -- )
  qjovians @ ?DUP IF 0 DO
    JOV-DMG I + C@ IF
      JOV-STATE I + C@ 0= IF     \ only check idle Jovians
        I jov-detect? IF
          I jov-on-detect
          1 I jov-emotion-stim    \ +1 alertness
        THEN
      THEN
    THEN
  LOOP THEN ;

VARIABLE jov-moved               \ flag: did any Jovian move this frame?

\ ── Jovian AI (genome-driven) ──────────────────────────────────────────
\ jov-think CODE word computes intent (proposed position + flags) from
\ genome data, current positions, and target selection.
\ apply-intent applies obstacle avoidance and writes final position.
\
\ Intent buffer (JOV-INTENT + i*3): nx, ny, flags
\   flags bit 0 = targets base
\   flags bit 1 = base-stop (within 30px, hold position)

VARIABLE hc-jx                    \ scratch: candidate x for obstacle check
VARIABLE hc-jy                    \ scratch: candidate y for obstacle check
VARIABLE jtk-nx                   \ proposed new x
VARIABLE jtk-ny                   \ proposed new y

\ Per-Jovian tick threshold from genome (lower = faster)
\ speed_modifier (byte 1, bits 5-4) + pilot_skill (byte 0, bits 2-0)
\ threshold = 10 - (speed + skill), clamped [2, 8]
: jov-threshold  ( i -- n )
  4 * JOV-GENOME +               \ genome addr
  DUP 1 + C@ 4 RSHIFT 3 AND     \ speed_modifier (0-3)
  SWAP C@ 7 AND                  \ pilot_skill (0-7)
  + 10 SWAP -                    \ 10 - (speed + skill)
  DUP 2 < IF DROP 2 THEN
  DUP 8 > IF DROP 8 THEN ;

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

VARIABLE base-attack              \ frame counter for base destruction

\ ── jov-think: genome-driven intent computation (6809 CODE) ────────────
\ ( i qbase -- )
\ Reads: JOV-POS, JOV-DMG, SHIP-POS, BASE-POS, JOV-GENOME (future)
\ Writes: JOV-INTENT + i*3 = { proposed_x, proposed_y, flags }
\
\ Target selection (identical to prior Forth logic):
\   Jovian 0 → base (if exists), DMG < 50 → base, else → ship
\ Direction: 1px step toward target, bounds-clamped [4,123] x [4,139]
\ Base-stop: within 30px manhattan of base → hold position
\ Genome bytes are loaded but not yet used (Phase 2+)

CODE jov-think  ( i qbase -- )
        PSHS    X               ; save IP
        LDA     1,U             ; A = qbase (low byte)
        LDB     3,U             ; B = i (low byte)
        LEAU    4,U             ; pop 2 args
        PSHS    D               ; [S+0]=qbase, [S+1]=i

        ; Compute intent addr: Y = $77D1 + i*3
        LDA     #3
        MUL                     ; D = i*3
        ADDD    #$77D1
        TFR     D,Y             ; Y = intent output

        ; Compute pos addr: X = $768A + i*2
        LDB     1,S             ; B = i
        ASLB
        LDX     #$768A
        ABX                     ; X = &JOV-POS[i]

        ; Save current position
        LDA     ,X              ; cx
        LDB     1,X             ; cy
        PSHS    D               ; [S+0]=cx [S+1]=cy [S+2]=qbase [S+3]=i

        ; --- Target selection ---
        LDX     #$7694          ; default: SHIP-POS
        LDA     2,S             ; qbase
        BEQ     @calc           ; no base, target ship
        LDA     3,S             ; i
        BEQ     @tbase          ; i==0, target base
        LDX     #$7696          ; JOV-DMG
        LDA     A,X             ; DMG[i]
        CMPA    #50
        BHS     @calc           ; healthy, target ship

@tbase  LDX     #$7690          ; BASE-POS
        BRA     @calc2
@calc   LDX     #$7694          ; SHIP-POS
@calc2

        ; --- Flags: bit 0 = targets_base ---
        CLR     2,Y             ; flags = 0
        CMPX    #$7690
        BNE     @nx
        LDA     #1
        STA     2,Y             ; flags |= targets_base

        ; --- Base-stop check: manhattan < 30, hold ---
        LDA     ,X              ; tx
        SUBA    ,S              ; tx - cx
        BPL     @absx
        NEGA
@absx   TFR     A,B             ; B = |tx-cx|
        LDA     1,X             ; ty
        SUBA    1,S             ; ty - cy
        BPL     @absy
        NEGA
@absy   PSHS    B               ; save |tx-cx|
        ADDA    ,S+             ; A = manhattan dist
        CMPA    #30
        BHS     @nx
        ; Within 30px, stay put
        LDA     ,S              ; cx
        STA     ,Y              ; intent.nx = cx (no move)
        LDA     1,S             ; cy
        STA     1,Y             ; intent.ny = cy
        LDA     2,Y
        ORA     #$02            ; flags |= base_stop
        STA     2,Y
        BRA     @done

        ; --- Proposed new x: cx + sign(tx - cx), clamped ---
@nx     LDA     ,X              ; tx
        CMPA    ,S              ; vs cx
        BEQ     @kx             ; same, keep cx
        BHI     @incx
        ; tx < cx, decrement
        LDA     ,S              ; cx
        DECA
        CMPA    #4
        BHS     @sx
        LDA     ,S              ; clamp: keep cx
        BRA     @sx
@incx   LDA     ,S              ; cx
        INCA
        CMPA    #123
        BLS     @sx
        LDA     ,S              ; clamp: keep cx
@sx     STA     ,Y              ; intent.nx
        BRA     @ny
@kx     LDA     ,S
        STA     ,Y              ; intent.nx = cx

        ; --- Proposed new y: cy + sign(ty - cy), clamped ---
@ny     LDA     1,X             ; ty
        CMPA    1,S             ; vs cy
        BEQ     @ky
        BHI     @incy
        LDA     1,S             ; cy
        DECA
        CMPA    #4
        BHS     @sy
        LDA     1,S
        BRA     @sy
@incy   LDA     1,S             ; cy
        INCA
        CMPA    #139
        BLS     @sy
        LDA     1,S
@sy     STA     1,Y             ; intent.ny
        BRA     @done
@ky     LDA     1,S
        STA     1,Y             ; intent.ny = cy

@done   LEAS    4,S             ; pop cx, cy, qbase, i
        PULS    X               ; restore IP
        ;NEXT
;CODE

\ ── apply-intent: obstacle avoidance + position update ─────────────────
\ Reads proposed position from JOV-INTENT, applies 3-tier obstacle
\ fallback (both axes → x-only → y-only → stay), writes JOV-POS.

: apply-intent  ( -- )
  JOV-INTENT jbg-i @ 3 * +       \ intent addr
  DUP 2 + C@ 2 AND IF DROP EXIT THEN  \ base-stop → no move
  DUP C@ jtk-nx !                \ proposed x
  1 + C@ jtk-ny !                \ proposed y
  JOV-POS jbg-i @ 2 * + >R       \ R: pos addr
  \ Try both axes
  jtk-nx @ hc-jx !  jtk-ny @ hc-jy !
  jov-blocked? IF
    \ Try x only (keep current y)
    jtk-nx @ hc-jx !  R@ 1 + C@ hc-jy !
    jov-blocked? IF
      \ Try y only (keep current x)
      R@ C@ hc-jx !  jtk-ny @ hc-jy !
      jov-blocked? IF
        R> DROP EXIT             \ all blocked → stay put
      THEN
      R@ C@ jtk-nx !            \ keep old x
    ELSE
      R@ 1 + C@ jtk-ny !       \ keep old y
    THEN
  THEN
  \ Apply if changed
  jtk-nx @ R@ C@ <>
  jtk-ny @ R@ 1 + C@ <> OR IF
    jtk-nx @ R@ C!
    jtk-ny @ R@ 1 + C!
    1 jov-moved !
  THEN R> DROP ;

VARIABLE detect-timer              \ frame counter for detection rolls

\ Tick all living Jovians (per-Jovian threshold from genome)
: tick-jovians  ( -- )
  qjovians @ ?DUP IF 0 DO
    I jbg-i !
    JOV-DMG jbg-i @ + C@ IF
      JOV-STATE jbg-i @ + C@ IF   \ aware: think + move
        JOV-TICK jbg-i @ + C@ 1 + DUP I jov-threshold < IF
          JOV-TICK jbg-i @ + C!
        ELSE
          DROP 0 JOV-TICK jbg-i @ + C!
          jbg-i @ qbase @ jov-think
          apply-intent
        THEN
      THEN
    THEN
  LOOP THEN
  \ Detection: every 30 frames, roll for idle Jovians
  detect-timer @ 1 + DUP DETECT-RATE < IF
    detect-timer !
  ELSE
    DROP 0 detect-timer !
    jov-detect-tick
  THEN
  \ Emotion decay: every 120 frames, drift toward baseline
  emotion-timer @ 1 + DUP EMOTION-DECAY-RATE < IF
    emotion-timer !
  ELSE
    DROP 0 emotion-timer !
    jov-emotion-decay-all
  THEN ;

\ ── Jovian gravity (black holes + stars pull/kill Jovians) ────────────
\ Applies every frame after tick-jovians.  Uses sg-sx/sg-sy as scratch.
\ Reuses grav-tick from player gravity for frame timing.

\ Pull one Jovian (index in jbg-i) toward (sg-sx, sg-sy) by 1px
: jov-pull  ( -- )
  JOV-POS jbg-i @ 2 * + sg-sx @ sg-sy @ jov-moved xy-pull ;

\ After any kill + explosion, do a full sprite refresh.
\ Kills corrupt bg-save buffers (beam pixels, explosion debris), so we must:
\ 1. Erase ship at old AND current pos (restore-ship-bg already wrote stale
\    pixels at old-sx/old-sy earlier this frame — must clear both)
\ 2. Redraw stars (fix black spots from erase/explosion)
\ 3. Re-save all bg buffers from clean VRAM
\ 4. Redraw all living sprites
: clear-tactical  ( -- )
  rv @ 4608 0 FILL ;              \ clear rows 0-143 (144 * 32 bytes)

: refresh-after-kill  ( -- )
  clear-tactical
  draw-border draw-stars draw-storm-stars draw-event-horizon
  draw-base
  save-jov-oldpos
  save-jov-bgs
  draw-jovians-live
  save-ship-bg
  draw-ship ;

\ Kill Jovian (index in jbg-i) — erase sprite, zero health, explode
VARIABLE check-win                \ flag: a kill happened, check win/lose

: jov-kill  ( -- )
  JOV-DMG jbg-i @ + C@ IF
    jbg-i @ jov-spr
    JOV-POS jbg-i @ 2 * + C@ jbg-i @ jov-draw-dx -
    JOV-POS jbg-i @ 2 * + 1 + C@ jbg-i @ jov-draw-dy -
    spr-erase-box
    0 JOV-DMG jbg-i @ + C!
    1 check-win !
    JOV-POS jbg-i @ 2 * + C@
    JOV-POS jbg-i @ 2 * + 1 + C@
    explode-jovian
    proximity-damage
    3 jov-emotion-all              \ fellow killed: rage/panic
    refresh-after-kill
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
\ Animated expanding ring explosion.  Each frame generates dots along
\ a ring at increasing radius, cycling white→red→fade.  Uses a buffer
\ at $7D00 for up to 32 (x,y) pairs = 64 bytes.  Clamped to screen.
\ Game loop pauses during the explosion (synchronous).

$7D00 CONSTANT EXPLBUF               \ explosion dot buffer (x,y pairs)
VARIABLE expl-cx                      \ explosion center x
VARIABLE expl-cy                      \ explosion center y
VARIABLE expl-rad                     \ start radius
VARIABLE expl-nframes                 \ total animation frames
VARIABLE expl-currad                  \ current frame radius
VARIABLE expl-radstep                 \ radius increment per frame
VARIABLE expl-dots                    \ dots per frame
VARIABLE expl-dmgrad                  \ proximity damage radius
VARIABLE expl-dmgamt                  \ proximity damage amount

\ Scratch vars for ring-dot
VARIABLE rd-rad
VARIABLE rd-ang
VARIABLE expl-total                   \ total accumulated dots so far

\ Generate one ring dot: random angle, jittered radius from center
\ Buffer index offset by expl-total so dots accumulate across frames
: ring-dot  ( i radius -- )
  8 rnd 4 - +  DUP 1 < IF DROP 1 THEN  rd-rad !
  64 rnd 6 *  rd-ang !
  expl-total @ + 2 * EXPLBUF +        \ buf-addr (offset by accumulated)
  rd-ang @ rd-rad @ angle-dx expl-cx @ +
  DUP 1 < IF DROP 1 THEN  DUP 126 > IF DROP 126 THEN
  OVER C!                             \ store x
  rd-ang @ rd-rad @ angle-dy expl-cy @ +
  DUP 1 < IF DROP 1 THEN  DUP 142 > IF DROP 142 THEN
  SWAP 1 + C! ;                       \ store y

\ Fill explosion buffer with ring dots at given radius
: gen-ring  ( radius -- )
  expl-dots @ 0 DO  I OVER ring-dot  LOOP  DROP ;

\ Color schedule: 30% white, 30% red, 25% blue, 15% erase-only
: expl-color  ( frame -- color )
  10 *  DUP expl-nframes @ 3 * < IF DROP 3 EXIT THEN
         DUP expl-nframes @ 6 * < IF DROP 2 EXIT THEN
             expl-nframes @ 8 * < IF       1 EXIT THEN
  0 ;

\ Animated expanding ring explosion
\ Dots ACCUMULATE across frames — no per-frame erase.  Each frame adds
\ a new ring at a larger radius.  White rings stay white, red rings stay
\ red, building an expanding cloud.  Final erase wipes everything.
VARIABLE expl-clr                     \ current frame color

: animate-explosion  ( -- )
  0 expl-total !
  expl-nframes @ 0 DO
    VSYNC
    \ Compute current radius: startR + step*i
    expl-rad @ expl-radstep @ I * +  expl-currad !
    \ Get color for this frame
    I expl-color expl-clr !
    expl-clr @ IF
      \ Generate ring dots at buffer offset and draw them
      expl-currad @ gen-ring
      EXPLBUF expl-total @ 2 * + expl-dots @ expl-clr @ plot-dots
      expl-dots @ expl-total +!
    THEN
  LOOP
  \ Hold the full explosion briefly before erasing
  VSYNC VSYNC
  \ Erase ALL accumulated dots + star restore
  EXPLBUF expl-total @ 0 plot-dots
  draw-stars ;

\ Setup explosion parameters and run animation
: setup-explosion  ( cx cy startR endR dots nframes dmgR dmgAmt -- )
  expl-dmgamt !  expl-dmgrad !
  \ Stack: cx cy startR endR dots nframes
  expl-nframes !  expl-dots !
  \ Stack: cx cy startR endR
  \ Compute radius step: (endR - startR) / nframes
  OVER -  expl-nframes @ /MOD SWAP DROP  expl-radstep !
  expl-rad !
  expl-cy !  expl-cx ! ;

\ Convenience words for each explosion type
: explode-jovian   ( x y -- )   2 12 20  6 12 30 setup-explosion animate-explosion ;
: explode-ship     ( x y -- )   3 22 28 10  0  0 setup-explosion animate-explosion ;
: explode-base     ( x y -- )   4 26 32 12 20 50 setup-explosion animate-explosion ;
: explode-destruct ( x y -- )   8 68 48 20 60 200 setup-explosion animate-explosion ;

\ ── Proximity damage ────────────────────────────────────────────────
\ After explosion, check Manhattan distance from center to all living
\ Jovians and ship.  Proximity-killed Jovians chain-explode (no further
\ chain from those secondary explosions).
VARIABLE pd-kills                     \ bitmask of Jovians killed (max 3)

\ prox-dmg: assembly Jovian proximity scan + damage application.
\ Scans JOV-POS entries, applies damage to JOV-DMG health bytes,
\ returns bitmask of killed Jovians.  ~10x faster than ITC equivalent.
CODE prox-dmg  ( cx cy radius damage count -- killmask )
        PSHS    X
        LDA     1,U             ; count
        BEQ     @zero
        LDX     #$768A          ; JOV-POS (fixed address)
        LDY     #$7696          ; JOV-DMG (fixed address)
        ; Build stack frame
        LDD     #0
        PSHS    D               ; +0,1: killmask
        LDD     #1
        PSHS    D               ; +2,3: bit
        LDA     1,U             ; count
        PSHS    A               ; +4: count
        LDA     9,U             ; cx
        LDB     7,U             ; cy
        PSHS    D               ; +5: cx, +6: cy
        LDA     5,U             ; radius
        LDB     3,U             ; damage
        PSHS    D               ; +7: radius, +8: damage
        LEAU    10,U            ; pop 5 args
        ; Frame: +0=radius +1=damage +2=cx +3=cy +4=count
        ;        +5,6=bit +7,8=killmask +9,10=saved IP
@loop   LDA     ,Y              ; health
        BEQ     @next           ; dead
        LDB     ,X              ; entry x
        SUBB    2,S             ; - cx
        BPL     @ax
        NEGB
@ax     PSHS    B               ; save |dx| — all offsets +1
        LDB     1,X             ; entry y
        SUBB    4,S             ; - cy (+3 shifted to +4)
        BPL     @ay
        NEGB
@ay     ADDB    ,S+             ; |dx|+|dy| — offsets back
        BCS     @next           ; overflow
        CMPB    ,S              ; radius (+0)
        BHS     @next           ; out of range
        ; Apply damage
        SUBA    1,S             ; health - damage (+1)
        BHI     @alive
        CLRA
        STA     ,Y              ; health = 0
        ; Kill: killmask |= bit
        LDD     7,S             ; killmask (+7,8)
        ORA     5,S             ; | bit_hi (+5)
        ORB     6,S             ; | bit_lo (+6)
        STD     7,S
        BRA     @next
@alive  STA     ,Y              ; store reduced health
@next   LEAX    2,X             ; pos += 2
        LEAY    1,Y             ; dmg += 1
        LSL     6,S             ; bit_lo <<= 1 (+6)
        ROL     5,S             ; bit_hi (+5)
        DEC     4,S             ; count-- (+4)
        BNE     @loop
        ; Return killmask
        LDD     7,S             ; killmask (+7,8)
        LEAS    9,S             ; pop frame
        STD     ,--U
        PULS    X
        ;NEXT
@zero   LEAU    10,U            ; pop 5 args
        LDD     #0
        STD     ,--U
        PULS    X
        ;NEXT
;CODE

: proximity-damage  ( -- )
  expl-dmgrad @ 0= IF EXIT THEN
  expl-cx @ expl-cy @ expl-dmgrad @ expl-dmgamt @
  qjovians @ prox-dmg                    \ ( -- killmask )
  \ Check ship
  SHIP-POS C@ expl-cx @ - abs
  SHIP-POS 1 + C@ expl-cy @ - abs +
  expl-dmgrad @ < IF
    penergy @ expl-dmgamt @ 1 RSHIFT - DUP 0 < IF DROP 0 THEN penergy !
  THEN
  \ Chain-explode any proximity-killed Jovians (no further chain)
  ?DUP IF
    pd-kills !
    qjovians @ ?DUP IF 0 DO
      1 I LSHIFT pd-kills @ AND IF
        I jov-spr
        JOV-POS I 2 * + C@ I jov-draw-dx -
        JOV-POS I 2 * + 1 + C@ I jov-draw-dy -
        spr-erase-box
        JOV-POS I 2 * + C@
        JOV-POS I 2 * + 1 + C@
        explode-jovian
      THEN
    LOOP THEN
    refresh-after-kill
  THEN ;

\ ── Jovian beam fire ──────────────────────────────────────────────────
\ One red beam at a time, from a random living Jovian toward the player.
\ Fire cooldown scales with difficulty: 90 frames (level 1) to 18 (level 9).
\ Uses pixel-save path buffer (JBEAM-PATH) with animated bolt.

VARIABLE jbeam-cool               \ frames until next fire allowed
VARIABLE jbeam-hit-ship            \ 1 = bolt will hit player ship
5 CONSTANT JBEAM-DMG              \ energy damage to player per hit
20 CONSTANT JBEAM-SYS-DMG         \ system damage per hit (out of 100)

\ Fire cooldown: 150 - (level * 14), min 24 frames
\ Level 1: ~136 frames (2.3s), Level 5: ~80 frames (1.3s), Level 9: ~24 frames (0.4s)
: jbeam-cooldown  ( -- n )
  150 glevel @ 14 * - DUP 24 < IF DROP 24 THEN ;

: clamp-jbeam  ( -- )
  jbeam-x1 @ 2 < IF 2 jbeam-x1 ! THEN
  jbeam-x1 @ 125 > IF 125 jbeam-x1 ! THEN
  jbeam-y1 @ 2 < IF 2 jbeam-y1 ! THEN
  jbeam-y1 @ 141 > IF 141 jbeam-y1 ! THEN
  jbeam-x2 @ 1 < IF 1 jbeam-x2 ! THEN
  jbeam-x2 @ 126 > IF 126 jbeam-x2 ! THEN
  jbeam-y2 @ 1 < IF 1 jbeam-y2 ! THEN
  jbeam-y2 @ 142 > IF 142 jbeam-y2 ! THEN ;

\ Check if player ship bbox overlaps any pixel in the Jovian beam path
\ Ship sprite is 7x5 centered at SHIP-POS: x±3, y±2
VARIABLE jbhit-flag

: jbeam-ship-hit?  ( -- flag )
  0 jbhit-flag !
  jbeam-total @ ?DUP 0= IF 0 EXIT THEN
  0 DO
    jbhit-flag @ 0= IF
      JBEAM-PATH I 3 * + C@
      SHIP-POS C@ - abs 4 < IF
        JBEAM-PATH I 3 * + 1 + C@
        SHIP-POS 1 + C@ - abs 3 < IF
          1 jbhit-flag !
        THEN
      THEN
    THEN
  LOOP jbhit-flag @ ;

\ Pick a random living + aware Jovian index, or -1 if none
VARIABLE pj-result
: pick-jovian  ( -- i|-1 )
  -1 pj-result !
  qjovians @ ?DUP 0= IF -1 EXIT THEN
  rnd                             \ random starting index
  qjovians @ 0 DO
    DUP JOV-DMG + C@ IF           \ alive?
      DUP JOV-STATE + C@ IF       \ aware?
        pj-result @ 0 < IF DUP pj-result ! THEN
      THEN
    THEN
    1 + DUP qjovians @ < 0= IF DROP 0 THEN  \ wrap
  LOOP DROP
  pj-result @ ;

\ Fire a red beam from Jovian i toward the player
: fire-jbeam  ( i -- )
  \ Cancel any active Jovian beam first
  cancel-jbeam
  \ Get Jovian position
  2 * JOV-POS + DUP C@ jbeam-x1 !
  1 + C@ jbeam-y1 !
  \ Direction from Jovian to player (dx, dy scaled ×4)
  SHIP-POS C@ jbeam-x1 @ - 4 *
  SHIP-POS 1 + C@ jbeam-y1 @ - 4 *
  \ Endpoint: Jovian pos + direction
  OVER jbeam-x1 @ + jbeam-x2 !
  DUP  jbeam-y1 @ + jbeam-y2 !
  \ Offset origin 5px along direction (use sign of dx/dy)
  SWAP DUP 0= IF DROP ELSE 0 < IF -5 ELSE 5 THEN jbeam-x1 @ + jbeam-x1 ! THEN
  DUP 0= IF DROP ELSE 0 < IF -5 ELSE 5 THEN jbeam-y1 @ + jbeam-y1 ! THEN
  clamp-jbeam
  \ Trace path into buffer
  jbeam-x1 @ jbeam-y1 @ jbeam-x2 @ jbeam-y2 @ JBEAM-PATH beam-trace
  jbeam-total !
  \ Truncate at first non-black pixel (star, sprite, border)
  JBEAM-PATH jbeam-total @ beam-find-obstacle jbeam-total !
  \ Check if beam passes through player ship
  jbeam-ship-hit? jbeam-hit-ship !
  \ Start bolt animation
  0 jbeam-head !  0 jbeam-tail !
  \ Reset cooldown, scaled by emotion of firing Jovian
  \ Rage (15) = 60% cooldown, neutral (8) = 100%, fear (0) = 140%
  \ Formula: cooldown * (140 - emotion*~5) / 100
  jbeam-cooldown
  pj-result @ DUP 0 < 0= IF
    jov-emotion@ 5 * 140 SWAP -    \ scale factor: 140 at fear, 65 at rage
    * 100 /MOD SWAP DROP            \ apply percentage
  ELSE DROP THEN
  jbeam-cool ! ;

\ Jovian beam tick: erase tail
: tick-jbeam-erase  ( -- )
  jbeam-total @ 0= IF EXIT THEN
  jbeam-head @ jbeam-total @ < IF
    \ Head still advancing — erase to keep bolt at BOLT-LEN
    jbeam-head @ BOLT-LEN - jbeam-tail @ > IF
      JBEAM-PATH jbeam-tail @
      jbeam-head @ BOLT-LEN - jbeam-tail @ - BOLT-SPEED min
      0 beam-draw-slice
      jbeam-tail @ jbeam-head @ BOLT-LEN - jbeam-tail @ - BOLT-SPEED min +
      jbeam-tail !
    THEN
  ELSE
    \ Head reached end — drain remaining visible bolt
    jbeam-tail @ jbeam-head @ < IF
      JBEAM-PATH jbeam-tail @
      jbeam-head @ jbeam-tail @ - BOLT-SPEED min
      0 beam-draw-slice
      jbeam-tail @ jbeam-head @ jbeam-tail @ - BOLT-SPEED min +
      jbeam-tail !
    THEN
  THEN ;

\ Jovian beam tick: draw head
: tick-jbeam-draw  ( -- )
  jbeam-total @ 0= IF EXIT THEN
  jbeam-head @ jbeam-total @ < IF
    JBEAM-PATH jbeam-head @
    jbeam-total @ jbeam-head @ - BOLT-SPEED min
    2 beam-draw-slice                \ color 2 = red
    jbeam-head @ jbeam-total @ jbeam-head @ - BOLT-SPEED min +
    jbeam-head !
  THEN
  \ If tail caught up to head, beam is done
  jbeam-tail @ jbeam-head @ < 0= IF
    jbeam-head @ jbeam-total @ < 0= IF
      0 jbeam-total !
    THEN
  THEN ;

\ Apply Jovian beam damage when bolt reaches end
\ Shields reduce incoming damage: absorbed = shields * 2/3.
\ damage_taken = base * (300 - shields*2) / 300, minimum 1.
: apply-jbeam-hit  ( -- )
  jbeam-hit-ship @ 0= IF EXIT THEN
  jbeam-head @ jbeam-total @ < IF EXIT THEN  \ not there yet
  \ Energy damage scaled by shields
  JBEAM-DMG 300 pshields @ 2 * - * 300 /MOD SWAP DROP
  DUP 1 < IF DROP 1 THEN
  penergy @ SWAP - DUP 0 < IF DROP 0 THEN penergy !
  \ System damage scaled by shields
  JBEAM-SYS-DMG 300 pshields @ 2 * - * 300 /MOD SWAP DROP
  DUP 1 < IF DROP 1 THEN
  \ Pick random system (0-4), reduce its level
  5 rnd DUP 0= IF DROP pdmg-ion
  ELSE DUP 1 = IF DROP pdmg-warp
  ELSE DUP 2 = IF DROP pdmg-scan
  ELSE DUP 3 = IF DROP pdmg-defl
  ELSE DROP pdmg-masr
  THEN THEN THEN THEN
  SWAP NEGATE OVER @ + DUP 0 < IF DROP 0 THEN SWAP !
  \ Confidence boost to the shooter
  pj-result @ DUP 0 < 0= IF 1 SWAP jov-emotion-stim ELSE DROP THEN
  0 jbeam-hit-ship ! ;

\ Tick: cooldown toward next shot, maybe fire
: tick-jbeam  ( -- )
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
  qbhole @ 0= IF 2DROP 0 EXIT THEN
  BHOLE-POS 1 + C@ - abs
  SWAP BHOLE-POS C@ - abs + 30 < ;

\ Storm star positions saved at quadrant entry for redraw.
\ Max 25 fake stars (5 real × 5 fake). 3 bytes each: x, y, color.
$76A0 CONSTANT FSTAR-POS          \ 25 × 3 = 75 bytes
VARIABLE fstar-count

VARIABLE fs-tmp

: gen-storm-stars  ( -- )
  0 fstar-count !
  pcol @ prow @ gal@ q-storm? 0= IF EXIT THEN
  qstars @ 3 * ?DUP IF 0 DO
    rnd-x rnd-y                  \ ( x y )
    2DUP in-grav-well? IF
      2DROP
    ELSE
      fstar-count @ 3 * FSTAR-POS + fs-tmp !
      3 rnd 1 + fs-tmp @ 2 + C!  \ color
      fs-tmp @ 1 + C!             \ y
      fs-tmp @ C!                  \ x
      1 fstar-count +!
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
$76D0 CONSTANT SPIRAL-POS         \ 32 × 2 = 64 bytes
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
      2DROP
    ELSE
      SWAP DUP 2 < OVER 125 > OR IF
        2DROP
      ELSE
        SWAP                       \ ( angle x y )
        spiral-count @ 2 * SPIRAL-POS + fs-tmp !
        fs-tmp @ 1 + C!           \ store y
        fs-tmp @ C!               \ store x
        1 spiral-count +!
      THEN
    THEN
    20 +
    DUP 360 > IF 360 - THEN
    -2 sp-r2 +!
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
  gen-genomes init-jovian-ai
  mood-load                        \ seed emotions from quadrant mood
  0 emotion-timer !  0 detect-timer !
  2 jov-emotion-all                \ alarm: player enters quadrant
  gen-jov-sprites                  \ generate unique sprites from genomes
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

\ Check if any (x,y) pair in array is within 3px of (sx,sy) on both axes.
\ Returns nonzero flag if collision found.
CODE collision-scan  ( sx sy array count -- flag )
        PSHS    X
        LDA     1,U             ; A = count (low byte)
        BEQ     @none
        LDX     2,U             ; X = array base
        LDB     7,U             ; B = sx (low byte of 16-bit)
        PSHS    B               ; +0,S = sx
        LDB     5,U             ; B = sy (low byte)
        PSHS    B               ; +0,S = sy, +1,S = sx
@loop   ; check |array[i].x - sx| < 3
        LDB     ,X              ; B = entry x
        SUBB    1,S             ; B = entry_x - sx
        BPL     @ax
        NEGB
@ax     CMPB    #3
        BHS     @next           ; |dx| >= 3, skip
        ; check |array[i].y - sy| < 3
        LDB     1,X             ; B = entry y
        SUBB    ,S              ; B = entry_y - sy
        BPL     @ay
        NEGB
@ay     CMPB    #3
        BHS     @next           ; |dy| >= 3, skip
        ; hit!
        LEAS    2,S             ; pop sx,sy
        LEAU    8,U             ; pop 4 args
        LDD     #1
        STD     ,--U            ; push flag=1
        PULS    X
        ;NEXT
@next   LEAX    2,X             ; next entry
        DECA
        BNE     @loop
        LEAS    2,S             ; pop sx,sy
@none   LEAU    8,U             ; pop 4 args
        LDD     #0
        STD     ,--U            ; push flag=0
        PULS    X
        ;NEXT
;CODE

: check-collisions  ( -- )
  moved @ 0= IF EXIT THEN
  \ Star collision: within 3px = destroyed
  qstars @ ?DUP IF
    >R SHIP-POS C@ SHIP-POS 1 + C@ STAR-POS R>
    collision-scan IF
      0 penergy !
    THEN
  THEN
  \ Black hole collision: within 3px = vanish instantly
  qbhole @ IF
    SHIP-POS C@ SHIP-POS 1 + C@ BHOLE-POS 1
    collision-scan IF
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
  1 grav-tick +!
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
  >R 1 moved !
  SHIP-POS BHOLE-POS C@ BHOLE-POS 1 + C@ R> moved xyn-pull ;

\ ── Star gravity (weaker, smaller) ──────────────────────────────────────
\ 10px radius. Close (<5): pull every 2 frames. Far (5-10): every 4.

VARIABLE sg-i                      \ star gravity loop index
VARIABLE sg-sx                     \ star x cache
VARIABLE sg-sy                     \ star y cache

\ Pull byte pair at addr toward (tx,ty) by step px per axis.
\ Sets the 16-bit cell at flag-addr to 1 if any axis moved.
CODE xyn-pull  ( addr tx ty step flag-addr -- )
        PSHS    X
        LDX     8,U             ; X = addr of (x,y) byte pair
        LDY     ,U              ; Y = flag-addr
        LDB     3,U             ; B = step (low byte)
        ; -- pull X axis --
        LDA     ,X              ; A = current x
        CMPA    7,U             ; compare with tx (low byte)
        BEQ     @xdone
        BHS     @xdec           ; current > target: decrement
        PSHS    B               ; save step
        ADDA    ,S+             ; A += step
        BRA     @xset
@xdec   PSHS    A               ; save current
        TFR     B,A             ; A = step
        NEGA                    ; A = -step
        ADDA    ,S+             ; A = current - step
@xset   STA     ,X
        LDD     #1
        STD     ,Y              ; flag = 1
@xdone  ; -- pull Y axis --
        LDB     3,U             ; B = step (reload)
        LDA     1,X             ; A = current y
        CMPA    5,U             ; compare with ty (low byte)
        BEQ     @ydone
        BHS     @ydec
        PSHS    B
        ADDA    ,S+
        BRA     @yset
@ydec   PSHS    A
        TFR     B,A
        NEGA
        ADDA    ,S+
@yset   STA     1,X
        LDD     #1
        STD     ,Y              ; flag = 1
@ydone  LEAU    10,U            ; pop 5 args
        PULS    X
        ;NEXT
;CODE

\ Convenience: pull by 1 pixel (common case)
: xy-pull  ( addr tx ty flag-addr -- )
  >R 1 R> xyn-pull ;

: star-pull  ( -- )
  1 moved !
  SHIP-POS sg-sx @ sg-sy @ moved xy-pull ;

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
  10 pmissiles !
  1 jov-emotion-all ;              \ boldness: player docking

: do-undock  ( -- )  0 docked ! ;

\ Gradual energy recharge while docked.
\ Inverse log curve: fast when empty, slows every 20%.
VARIABLE dock-tick

\ Add 2 to the 16-bit cell at addr, capping at 100. No-op if already >= 100.
: repair-sys  ( addr -- )
  DUP @ 100 < IF DUP @ 2 + 100 < IF DUP @ 2 + ELSE 100 THEN SWAP ! ELSE DROP THEN ;

: tick-dock  ( -- )
  docked @ 0= IF EXIT THEN
  penergy @ 100 = IF EXIT THEN
  1 dock-tick +!
  penergy @ 20 < IF                    \ 0-19%: +1 every frame
    1 penergy +!
  ELSE penergy @ 40 < IF               \ 20-39%: +1 every 2 frames
    dock-tick @ 1 AND 0= IF 1 penergy +! THEN
  ELSE penergy @ 60 < IF               \ 40-59%: +1 every 4 frames
    dock-tick @ 3 AND 0= IF 1 penergy +! THEN
  ELSE penergy @ 80 < IF               \ 60-79%: +1 every 8 frames
    dock-tick @ 7 AND 0= IF 1 penergy +! THEN
  ELSE                                  \ 80-99%: +1 every 16 frames
    dock-tick @ 15 AND 0= IF 1 penergy +! THEN
  THEN THEN THEN THEN
  penergy @ 100 > IF 100 penergy ! THEN
  \ Repair systems: +2 per frame each (0→100 in ~50 frames = 0.8s)
  pdmg-ion  repair-sys
  pdmg-warp repair-sys
  pdmg-scan repair-sys
  pdmg-defl repair-sys
  pdmg-masr repair-sys ;

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
    S" SOS-BASE" rg-type
    11 17 at-xy
    sos-col @ rg-u.  CHAR , rg-emit  sos-row @ rg-u.
  ELSE
    0 17 at-xy
    S" COND" rg-type
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
  S" COMMAND" rg-type ;

\ ══════════════════════════════════════════════════════════════════════════
\  BEAM SYSTEM — Pixel-save/restore for artifact-free rendering
\ ══════════════════════════════════════════════════════════════════════════
\
\ Beams save what's underneath pixel-by-pixel and erase by restoring.
\ Animation: a fixed-length "bolt" travels from ship to target.
\ Layer 2: always drawn last, always erased first.

\ ── Path buffers (in free RAM $7800+) ──────────────────────────────────
\ 3 bytes per pixel × 200 max pixels = 600 bytes each

$7800 CONSTANT BEAM-PATH           \ player maser path buffer (600 bytes)
$7A58 CONSTANT JBEAM-PATH          \ Jovian beam path buffer (600 bytes)

\ ── Beam state variables ───────────────────────────────────────────────
\ Per-beam: total pixel count, head index, tail index, hit info

VARIABLE beam-total                \ total pixels in maser path (0=inactive)
VARIABLE beam-head                 \ next pixel to draw (head of bolt)
VARIABLE beam-tail                 \ next pixel to erase (tail of bolt)
VARIABLE beam-hit-idx              \ Jovian index hit (-1=none)
VARIABLE beam-x1                   \ beam start x (for endpoint calc)
VARIABLE beam-y1                   \ beam start y
VARIABLE beam-x2                   \ beam endpoint x (clamped)
VARIABLE beam-y2                   \ beam endpoint y (clamped)

VARIABLE jbeam-total               \ total pixels in Jovian beam path
VARIABLE jbeam-head
VARIABLE jbeam-tail
VARIABLE jbeam-x1
VARIABLE jbeam-y1
VARIABLE jbeam-x2
VARIABLE jbeam-y2

20 CONSTANT BOLT-LEN               \ visible bolt length in pixels
20 CONSTANT BOLT-SPEED             \ pixels advanced per frame

\ ── Maser damage ──────────────────────────────────────────────────────
\ Scales with maser system health: 30 at 100%, 3 at 10%.
\ Shields reduce output: loss = shields * 2/3 (50% shields = 1/3 loss,
\ 100% shields = 2/3 loss).  Formula: base * (300 - shields*2) / 300.
: maser-dmg  ( -- n )
  pdmg-masr @ 30 * 100 /MOD SWAP DROP
  300 pshields @ 2 * - * 300 /MOD SWAP DROP
  DUP 1 < IF DROP 1 THEN ;

\ ── Bbox hit detection (during beam-trace) ────────────────────────────
\ After tracing, walk the path buffer and check each pixel against
\ live Jovian bounding boxes (7×5 sprite: x±3, y±2).

VARIABLE hc-i                      \ hit-check loop variable

VARIABLE bchk-buf                  \ path buffer base for hit check
VARIABLE bchk-px                   \ pixel index being checked
VARIABLE bchk-hitpx                \ pixel index where hit was found

: beam-check-one-jov  ( jov-idx -- )
  hc-i !
  JOV-DMG hc-i @ + C@ 0= IF EXIT THEN  \ dead, skip
  beam-hit-idx @ 0 < 0= IF EXIT THEN    \ already found a hit
  \ Check bbox: |px - jx| <= 3 AND |py - jy| <= 2
  bchk-buf @ bchk-px @ 3 * + C@
  JOV-POS hc-i @ 2 * + C@ - abs 4 < IF
    bchk-buf @ bchk-px @ 3 * + 1 + C@
    JOV-POS hc-i @ 2 * + 1 + C@ - abs 3 < IF
      hc-i @ beam-hit-idx !
      bchk-px @ bchk-hitpx !      \ record pixel index of hit
    THEN
  THEN ;

: beam-check-path-hits  ( buf count -- hit-idx | -1 )
  -1 beam-hit-idx !
  -1 bchk-hitpx !
  ?DUP 0= IF DROP -1 EXIT THEN
  SWAP bchk-buf !                  \ save buf base
  0 DO
    beam-hit-idx @ 0 < IF         \ only check until first hit
      I bchk-px !
      qjovians @ ?DUP IF 0 DO
        I beam-check-one-jov
      LOOP THEN
    THEN
  LOOP
  beam-hit-idx @ ;

\ ── Clamp beam coordinates to tactical view ──────────────────────────

\ Scan path buffer for first non-black pixel (obstacle)
\ Returns index of first obstacle, or count if path is clear
VARIABLE bfo-hit
VARIABLE bfo-found

: beam-find-obstacle  ( buf count -- index )
  DUP bfo-hit !  0 bfo-found !
  0 DO
    bfo-found @ 0= IF
      DUP I 3 * + 2 + C@ IF
        I bfo-hit !  1 bfo-found !
      THEN
    THEN
  LOOP DROP  bfo-hit @ ;

: clamp-beam  ( -- )
  beam-x1 @ 2 < IF 2 beam-x1 ! THEN
  beam-x1 @ 125 > IF 125 beam-x1 ! THEN
  beam-y1 @ 2 < IF 2 beam-y1 ! THEN
  beam-y1 @ 141 > IF 141 beam-y1 ! THEN
  beam-x2 @ 1 < IF 1 beam-x2 ! THEN
  beam-x2 @ 126 > IF 126 beam-x2 ! THEN
  beam-y2 @ 1 < IF 1 beam-y2 ! THEN
  beam-y2 @ 142 > IF 142 beam-y2 ! THEN ;

\ ── Cancel a beam (erase any visible pixels) ──────────────────────────

: cancel-beam  ( -- )
  beam-total @ 0= IF EXIT THEN
  \ Restore any currently visible bolt pixels
  beam-tail @ beam-head @ < IF
    BEAM-PATH beam-tail @ beam-head @ beam-tail @ - 0 beam-draw-slice
  THEN
  0 beam-total ! ;

: cancel-jbeam  ( -- )
  jbeam-total @ 0= IF EXIT THEN
  jbeam-tail @ jbeam-head @ < IF
    JBEAM-PATH jbeam-tail @ jbeam-head @ jbeam-tail @ - 0 beam-draw-slice
  THEN
  0 jbeam-total ! ;

\ ── Fire maser ─────────────────────────────────────────────────────────
\ Trace path, detect hits, start bolt animation.

: fire-maser  ( angle -- )
  penergy @ 0= IF DROP EXIT THEN
  MASER-COST use-energy
  \ Cancel any active beam first
  cancel-beam
  \ Beam origin: offset 5px from ship center along firing angle
  \ so the beam path doesn't overlap the 7x5 ship sprite
  DUP 5 angle-dx SHIP-POS C@ + beam-x1 !
  DUP 5 angle-dy SHIP-POS 1 + C@ + beam-y1 !
  \ Calculate and clamp endpoint
  DUP 140 angle-dx SHIP-POS C@ + beam-x2 !
  140 angle-dy SHIP-POS 1 + C@ + beam-y2 !
  clamp-beam
  \ Trace path into buffer (saves underlying pixels)
  beam-x1 @ beam-y1 @ beam-x2 @ beam-y2 @ BEAM-PATH beam-trace
  beam-total !
  \ Truncate at first non-black pixel (star, sprite, border)
  BEAM-PATH beam-total @ beam-find-obstacle beam-total !
  \ Check for Jovian hits along the path
  BEAM-PATH beam-total @ beam-check-path-hits
  DUP 0 < 0= IF
    \ Hit found — truncate beam at hit pixel
    beam-hit-idx !
    bchk-hitpx @ 1 + beam-total !   \ truncate path at hit point
  ELSE
    DROP  -1 beam-hit-idx !
  THEN
  \ Start bolt animation
  0 beam-head !  0 beam-tail !
  \ Firing reveals player + emotion reaction
  jov-reveal-all
  qjovians @ ?DUP IF 0 DO
    JOV-DMG I + C@ IF
      I 4 * JOV-GENOME + C@ 5 RSHIFT  \ aggression 0-7
      4 < IF -2 ELSE 2 THEN
      I jov-emotion-stim
    THEN
  LOOP THEN ;

\ ── Beam tick: advance bolt animation ─────────────────────────────────

: tick-beam-erase  ( -- )
  beam-total @ 0= IF EXIT THEN
  beam-head @ beam-total @ < IF
    \ Head still advancing — erase to keep bolt at BOLT-LEN
    beam-head @ BOLT-LEN - beam-tail @ > IF
      BEAM-PATH beam-tail @
      beam-head @ BOLT-LEN - beam-tail @ - BOLT-SPEED min
      0 beam-draw-slice
      beam-tail @ beam-head @ BOLT-LEN - beam-tail @ - BOLT-SPEED min +
      beam-tail !
    THEN
  ELSE
    \ Head reached end — drain remaining visible bolt
    beam-tail @ beam-head @ < IF
      BEAM-PATH beam-tail @
      beam-head @ beam-tail @ - BOLT-SPEED min
      0 beam-draw-slice
      beam-tail @ beam-head @ beam-tail @ - BOLT-SPEED min +
      beam-tail !
    THEN
  THEN ;

: tick-beam-draw  ( -- )
  beam-total @ 0= IF EXIT THEN
  \ Draw head pixels (new bolt front)
  beam-head @ beam-total @ < IF
    BEAM-PATH beam-head @
    beam-total @ beam-head @ - BOLT-SPEED min
    1 beam-draw-slice                \ color 1 = blue
    beam-head @ beam-total @ beam-head @ - BOLT-SPEED min +
    beam-head !
  THEN
  \ If tail caught up to head, beam is done
  beam-tail @ beam-head @ < 0= IF
    beam-head @ beam-total @ < 0= IF
      0 beam-total !                 \ beam fully erased
    THEN
  THEN ;

\ ── Apply maser hit (deferred until bolt reaches target) ──────────────

: apply-beam-hit  ( -- )
  beam-hit-idx @ 0 < IF EXIT THEN
  \ Only apply when head has reached the end (target)
  beam-head @ beam-total @ < IF EXIT THEN
  \ Apply damage
  JOV-DMG beam-hit-idx @ + C@
  maser-dmg - DUP 0 < IF DROP 0 THEN
  JOV-DMG beam-hit-idx @ + C!
  \ If dead, cancel beams, explode, refresh
  JOV-DMG beam-hit-idx @ + C@ 0= IF
    cancel-beam cancel-jbeam
    beam-hit-idx @ jov-spr
    JOV-POS beam-hit-idx @ 2 * + C@ beam-hit-idx @ jov-draw-dx -
    JOV-POS beam-hit-idx @ 2 * + 1 + C@ beam-hit-idx @ jov-draw-dy -
    spr-erase-box
    JOV-POS beam-hit-idx @ 2 * + C@
    JOV-POS beam-hit-idx @ 2 * + 1 + C@
    explode-jovian
    proximity-damage
    refresh-after-kill
  THEN
  -1 beam-hit-idx ! ;

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
          \ Kill Jovian — erase sprite, explode, full refresh
          0 JOV-DMG msl-hi @ + C!
          msl-hi @ jov-spr
          JOV-POS msl-hi @ 2 * + C@ msl-hi @ jov-draw-dx -
          JOV-POS msl-hi @ 2 * + 1 + C@ msl-hi @ jov-draw-dy -
          spr-erase-box
          JOV-POS msl-hi @ 2 * + C@
          JOV-POS msl-hi @ 2 * + 1 + C@
          explode-jovian
          proximity-damage
          refresh-after-kill
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
  msl-dx @ msl-x +!
  msl-dy @ msl-y +!
  1 msl-frame +!
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
  -1 pmissiles +!
  \ Start outside ship sprite (offset 8px along firing angle)
  DUP 8 angle-dx SHIP-POS C@ + 7 LSHIFT msl-x !
  DUP 8 angle-dy SHIP-POS 1 + C@ + 7 LSHIFT msl-y !
  \ Compute dx/dy from angle
  DUP MSL-SPEED angle-dx msl-dx !
  MSL-SPEED angle-dy msl-dy !
  0 msl-frame !
  1 msl-active !  1 msl-dirty !
  msl-scrx msl-px !  msl-scry msl-py !
  save-msl-bg
  jov-reveal-all ;                \ missile launch reveals player

\ ── Command dispatch ───────────────────────────────────────────────────

\ ── Hyperdrive (command 2) ─────────────────────────────────────────────
\ Enter 2-digit destination: tens=col, ones=row.  e.g. 35 = col 3, row 5.
\ Energy cost = 5 * Manhattan distance.  Damaged warp drive may mis-jump.

: warp-cost  ( col row -- cost )
  prow @ - abs SWAP pcol @ - abs + DUP + DUP 10 > IF DROP 10 THEN ;

: do-warp  ( -- )
  cmd-val @ 10 /MOD                  \ ( row col )
  \ Validate coordinates
  OVER 8 < OVER 8 < AND 0= IF 2DROP EXIT THEN
  \ Check if already there
  DUP pcol @ = OVER prow @ = AND IF 2DROP EXIT THEN
  \ Calculate and pay energy cost
  2DUP warp-cost
  DUP penergy @ > IF 2DROP DROP EXIT THEN   \ not enough energy
  use-energy
  \ Damaged warp: random chance of mis-jump (land in adjacent quadrant)
  pdmg-warp @ 50 < IF
    8 rnd 4 < IF                     \ 50% chance at <50% health
      SWAP 1 + 7 AND SWAP            \ shift col by 1
    THEN
  THEN
  \ Save mood before leaving quadrant
  mood-save
  \ Clear beams and missile
  cancel-jbeam cancel-beam
  msl-active @ IF msl-erase 0 msl-active ! THEN
  \ Expand new quadrant and redraw
  rg-pcls
  SWAP expand-quadrant
  safe-spawn
  draw-quadrant
  draw-panel
  0 docked !  0 prev-docked !
  0 jov-moved !  0 base-attack !
  jbeam-cooldown jbeam-cool !
  0 msl-dirty ! ;

\ ── Self-destruct (command 7, code 123) ──────────────────────────────
\ State-driven countdown that runs inside the game loop.
\ sd-active: 0=off, 5/4/3/2/1=current count.  sd-timer counts down
\ 60 frames (~1 sec) per step.  Typing 7123 again cancels.

VARIABLE sd-active                    \ 0=off, 5..1=countdown
VARIABLE sd-timer                     \ frames left in current count
VARIABLE sd-cancel                    \ cancel sequence progress (0-3)

\ Show current countdown digit in command area
: sd-show  ( -- )
  clear-cmd-area  17 18 at-xy
  sd-active @ CHAR 0 + rg-emit ;

\ Abort: clear countdown, show ABORT
: sd-abort  ( -- )
  0 sd-active !
  clear-cmd-area  17 18 at-xy
  S" ABORT" rg-type ;

\ Detonate: explode ship with proximity damage
: sd-detonate  ( -- )
  0 sd-active !
  cancel-jbeam cancel-beam
  msl-active @ IF msl-erase 0 msl-active ! THEN
  restore-ship-bg
  SHIP-POS C@ SHIP-POS 1 + C@
  explode-destruct
  proximity-damage
  2 death-cause !
  0 penergy ! ;

\ Check one key against cancel sequence 7-1-2-3
\ Wrong key resets progress
: sd-check-key  ( char -- )
  sd-cancel @ 0 = IF DUP CHAR 7 = IF DROP 1 sd-cancel ! EXIT THEN THEN
  sd-cancel @ 1 = IF DUP CHAR 1 = IF DROP 2 sd-cancel ! EXIT THEN THEN
  sd-cancel @ 2 = IF DUP CHAR 2 = IF DROP 3 sd-cancel ! EXIT THEN THEN
  sd-cancel @ 3 = IF     CHAR 3 = IF   sd-abort         EXIT THEN THEN
  DROP 0 sd-cancel ! ;

\ Called each game loop frame when sd-active > 0
: tick-destruct  ( -- )
  sd-active @ 0= IF EXIT THEN
  sd-timer @ 1 - DUP sd-timer !
  0= IF
    \ Timer expired — advance countdown
    sd-active @ 1 = IF sd-detonate EXIT THEN
    -1 sd-active +!
    30 sd-timer !
    sd-show
  THEN ;

\ Start self-destruct countdown
: do-destruct  ( -- )
  cmd-val @ 123 <> IF EXIT THEN
  \ If already counting down, treat as cancel attempt
  sd-active @ IF sd-abort EXIT THEN
  5 sd-active !  30 sd-timer !  0 sd-cancel !
  sd-show ;

\ ── Damage report (command 1) ─────────────────────────────────────────
\ Count remaining Jovians: scan galaxy + adjust for current quadrant kills.
\ Count remaining bases: scan galaxy.

: count-jovians  ( -- n )
  0  64 0 DO GALAXY I + C@ q-jovians + LOOP
  \ Adjust for current quadrant: subtract packed count, add living count
  pcol @ prow @ gal@ q-jovians -
  qjovians @ ?DUP IF 0 DO
    JOV-DMG I + C@ IF 1 + THEN
  LOOP THEN + ;

: count-bases  ( -- n )
  0  64 0 DO GALAXY I + C@ q-base? IF 1 + THEN LOOP ;

: do-damage-report  ( -- )
  clear-tactical
  \ Header: Jovians and bases remaining
  2 2 at-xy  count-jovians rg-u.
  S"  JOVIANS LEFT" rg-type
  2 4 at-xy  count-bases rg-u.
  S"  BASES LEFT" rg-type
  \ DAMAGE heading
  2 7 at-xy  S" DAMAGE" rg-type
  \ Five systems with percentages
  2 9 at-xy   S" ION ENGINES" rg-type     pdmg-ion @ 28 rg-u.r
  2 10 at-xy  S" HYPERDRIVE" rg-type      pdmg-warp @ 28 rg-u.r
  2 11 at-xy  S" SCANNERS" rg-type        pdmg-scan @ 28 rg-u.r
  2 12 at-xy  S" DEFLECTORS" rg-type      pdmg-defl @ 28 rg-u.r
  2 13 at-xy  S" MASERS" rg-type          pdmg-masr @ 28 rg-u.r
  \ Wait for key, then restore tactical view
  KEY DROP
  clear-tactical
  draw-quadrant ;

\ ── Long range scan (command 3) ───────────────────────────────────────
\ Display 8x8 galaxy map showing Jovians, bases, storms, player position.
\ Cell format: B=base, 1-3=jovians, M=storm, E=player quadrant.
\ Each cell is 3 chars wide, row labels at col 1, col headers at col 4.

VARIABLE sg-row                   \ scan grid: outer loop row

: do-scan  ( -- )
  clear-tactical
  4 1 at-xy  S" LONG RANGE SCAN" rg-type
  \ Column headers: row 3, starting col 4
  8 0 DO
    I 3 * 5 + 3 at-xy  I CHAR 0 + rg-emit
  LOOP
  \ Galaxy grid: rows 4-11
  8 0 DO    \ row
    I sg-row !
    1 I 4 + at-xy  I CHAR 0 + rg-emit    \ row label
    8 0 DO  \ col
      I 3 * 4 + sg-row @ 4 + at-xy
      \ Check if this is the player's quadrant
      I pcol @ =  sg-row @ prow @ =  AND IF
        CHAR E rg-emit
      ELSE
        I sg-row @ gal@ DUP q-storm? IF
          DROP CHAR M rg-emit
        ELSE
          DUP q-base? IF CHAR B rg-emit THEN
          q-jovians DUP IF CHAR 0 + rg-emit ELSE DROP THEN
        THEN
      THEN
    LOOP
  LOOP
  KEY DROP
  clear-tactical
  draw-quadrant ;

: exec-command  ( -- )
  cmd-num @ 1 = IF do-damage-report THEN
  cmd-num @ 2 = IF do-warp THEN
  cmd-num @ 3 = IF do-scan THEN
  cmd-num @ 4 = IF
    cmd-val @ DUP 100 > IF DROP 100 THEN
    DUP pdmg-defl @ > IF DROP pdmg-defl @ THEN pshields !
    draw-panel
  THEN
  cmd-num @ 5 = IF cmd-val @ fire-maser THEN
  cmd-num @ 6 = IF cmd-val @ fire-missile THEN
  cmd-num @ 7 = IF do-destruct THEN
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
    1 cmd-digits +!
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
      DUP sd-active @ IF sd-check-key ELSE DROP THEN
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
      safe-spawn
    THEN
  LOOP ;

\ ── Title screen + level select ────────────────────────────────────────

: title-screen  ( -- )
  rg-pcls
  \ SPACE WARP centered on row 5
  11 5 at-xy
  S" SPACE WARP" rg-type
  \ LEVEL 1-9 on row 9
  11 9 at-xy
  S" LEVEL 1-9" rg-type
  \ Version in lower right
  27 18 at-xy
  S" V0.9" rg-type
  \ Read level key (1-9)
  BEGIN KEY DUP CHAR 1 < OVER CHAR 9 > OR IF DROP 0 ELSE 1 THEN UNTIL
  CHAR 0 - glevel ! ;

\ ── Mission briefing ──────────────────────────────────────────────────

: briefing  ( -- )
  rg-pcls
  \ FROM UP COMMAND
  2 2 at-xy
  S" FROM UP COMMAND" rg-type
  \ DESTROY
  2 5 at-xy
  S" DESTROY " rg-type
  gjovians @ rg-u.
  \ JOVIAN SHIPS
  2 7 at-xy
  S" JOVIAN SHIPS" rg-type
  \ PROTECT
  2 9 at-xy
  S" PROTECT " rg-type
  gbases @ rg-u.
  S"  BASES" rg-type
  \ GOOD LUCK
  14 13 at-xy
  S" GOOD LUCK" rg-type
  KEY DROP ;

\ ── Base destruction by Jovians ──────────────────────────────────────
\ When any Jovian is within 30px of the base, base-attack increments.
\ At 180 frames (~3 seconds) the base is destroyed.

: destroy-base  ( -- )
  cancel-jbeam cancel-beam
  \ Cancel active missile
  msl-active @ IF
    SPR-MSL1 msl-px @ 2 - msl-py @ 2 - spr-erase-box
    0 msl-active !
  THEN
  \ Erase base sprite
  SPR-BASE BASE-POS C@ 3 - BASE-POS 1 + C@ 2 - spr-erase-box
  \ Clear quadrant state
  0 qbase !
  \ Clear base bit in galaxy byte
  pcol @ prow @ gal@ $FB AND pcol @ prow @ gal!
  \ Explode at base position
  BASE-POS C@ BASE-POS 1 + C@ explode-base
  refresh-after-kill
  draw-panel
  1 check-win !
  \ Undock if docked
  docked @ IF 0 docked ! THEN
  0 base-attack ! ;

VARIABLE jnb-result
: jov-near-base?  ( -- flag )
  qbase @ 0= IF 0 EXIT THEN
  0 jnb-result !
  qjovians @ ?DUP IF 0 DO
    JOV-DMG I + C@ IF
      JOV-POS I 2 * + C@ BASE-POS C@ - abs
      JOV-POS I 2 * + 1 + C@ BASE-POS 1 + C@ - abs + 30 < IF
        1 jnb-result !
      THEN
    THEN
  LOOP THEN
  jnb-result @ ;

: tick-base-attack  ( -- )
  jov-near-base? IF
    base-attack @ 1 + DUP base-attack !
    180 = IF destroy-base THEN
  ELSE
    0 base-attack !
  THEN ;

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
  MOOD-GRID 64 8 FILL           \ init mood grid to neutral

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
  0 beam-total !  -1 beam-hit-idx !
  0 jbeam-total !  0 jbeam-hit-ship !  jbeam-cooldown jbeam-cool !
  0 msl-active !  0 msl-dirty !
  0 docked !  0 prev-docked !  0 death-cause !
  0 sd-active !  0 base-attack !
  0 jov-moved !
  0 check-win !
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
    tick-missile
    tick-jovians jov-check-regen
    tick-base-attack
    jov-gravity
    tick-jbeam
    tick-destruct
    tick-stardate
    process-key
    VSYNC

    \ ── LAYER 2: Erase beam tails (restore saved pixels) ──
    \ Erase Jovian beam first (drawn last = erased first)
    tick-jbeam-erase
    tick-beam-erase
    \ Force sprite refresh when beams active (beam restore may
    \ write stale sprite data if ship/Jovians moved since trace)
    beam-total @ jbeam-total @ OR IF 1 moved ! THEN

    \ ── LAYER 1: Sprite bg-save/restore cycle ──
    moved @ jov-moved @ OR msl-dirty @ OR IF
      \ 1. Erase missile at old position
      msl-active @ IF
        SPR-MSL1 msl-px @ 2 - msl-py @ 2 - spr-erase-box
      THEN
      \ 2. Restore ship and Jovian backgrounds
      restore-ship-bg
      restore-jov-bgs
      \ 3. Redraw stars (fix any black spots from erases)
      draw-stars
      \ 4. Save and draw Jovians at current positions
      jov-moved @ IF save-jov-oldpos THEN
      save-jov-bgs
      draw-jovians-live
      \ 5. Save and draw ship
      save-ship-bg
      draw-ship
      \ 6. Draw missile on top (after ship bg-save to avoid capture)
      msl-dirty @ IF
        msl-scrx msl-px !  msl-scry msl-py !
        0 msl-dirty !
      THEN
      msl-active @ IF
        msl-spr msl-px @ 2 - msl-py @ 2 - spr-draw
      THEN
      0 jov-moved !
    THEN

    \ ── LAYER 2: Advance beam heads (draw new pixels) ──
    tick-beam-draw
    tick-jbeam-draw

    \ ── Apply deferred hit damage ──
    apply-beam-hit
    apply-jbeam-hit
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
    \ ── Win/lose checks (only after a kill) ──
    check-win @ IF
      0 check-win !
      count-jovians 0= IF
        cancel-jbeam cancel-beam
        clear-tactical
        2 3 at-xy  S" ALL " rg-type  gjovians @ rg-u.
        S"  JOVIANS" rg-type
        2 5 at-xy  S" DESTROYED" rg-type
        2 8 at-xy  S" THE UP SYSTEM" rg-type
        2 10 at-xy S" IS SAVED" rg-type
        0 18 at-xy S" AGAIN?" rg-type
        KEY DROP
        main EXIT
      THEN
      count-bases 0= IF
        cancel-jbeam cancel-beam
        clear-tactical
        2 3 at-xy  S" ALL BASES" rg-type
        2 5 at-xy  S" DESTROYED" rg-type
        2 8 at-xy  S" THE UP SYSTEM" rg-type
        2 10 at-xy S" WILL FALL" rg-type
        0 18 at-xy S" AGAIN?" rg-type
        KEY DROP
        main EXIT
      THEN
    THEN

    \ ── Death check: energy depleted ──
    penergy @ 0= IF
      cancel-jbeam cancel-beam
      SHIP-POS C@ SHIP-POS 1 + C@
      death-cause @ 1 = IF
        \ Black hole — ship vanishes, no explosion
        restore-ship-bg
        2DROP
        0 17 at-xy
        S" BLACK HOLE" rg-type
      ELSE death-cause @ 2 = IF
        \ Self-destruct — explosion already happened
        2DROP
        0 17 at-xy
        S" DESTROYED" rg-type
      ELSE
        \ Energy depleted or star collision — ship explodes
        restore-ship-bg
        explode-ship
        0 17 at-xy
        S" DESTROYED" rg-type
      THEN THEN
      \ AGAIN? prompt — any key restarts
      0 18 at-xy
      S" AGAIN?" rg-type
      KEY DROP
      main EXIT
    THEN
  AGAIN ;

main
