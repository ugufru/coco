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
\ Galaxy: 8x8 = 64 quadrants, 1 byte each at GALAXY ($8000).
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

\ Game data at $8000+ (all-RAM region, accessible after kernel boot).
$8000 CONSTANT GALAXY          \ 64 bytes: 8x8 quadrant data

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


\ ── Game variables ───────────────────────────────────────────────────────

VARIABLE glevel                \ difficulty level (1-10)
VARIABLE gjovians              \ total jovians in galaxy (decrements on kill)
VARIABLE gjovians0             \ initial total (for win screen)
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
$8040 CONSTANT STAR-POS        \ 5 stars x 2 bytes = 10 bytes
$804A CONSTANT JOV-POS         \ 3 jovians x 2 bytes = 6 bytes
$8050 CONSTANT BASE-POS        \ 1 base x 2 bytes = 2 bytes
$8052 CONSTANT BHOLE-POS       \ 1 black hole x 2 bytes = 2 bytes
$8054 CONSTANT SHIP-POS        \ player ship x 2 bytes = 2 bytes

\ Jovian damage (3 bytes, one per jovian: 100=full health, 0=dead)
$8056 CONSTANT JOV-DMG         \ 3 bytes

\ Quadrant object counts (from the packed byte, cached for speed)
VARIABLE qstars                \ star count in current quadrant
VARIABLE qjovians              \ jovian count in current quadrant
VARIABLE qbase                 \ base present? (0 or 1)
VARIABLE qbhole                \ black hole present? (0 or 1)

\ Shadow bytes at fixed addresses for CODE word access
$80B0 CONSTANT QCOUNTS         \ 4 bytes: nstars, njovians, hasbase, hasbhole
: sync-qcounts  ( -- )
  qstars @ QCOUNTS C!
  qjovians @ QCOUNTS 1 + C!
  qbase @ QCOUNTS 2 + C!
  qbhole @ QCOUNTS 3 + C! ;

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

$8082 CONSTANT SPR-SHIP           \ Endever: blue chevron (12 bytes)
$808E CONSTANT SPR-JOV            \ Jovian: red diamond (12 bytes)
$809A CONSTANT SPR-BASE           \ UP base: blue cross (12 bytes)
$80A6 CONSTANT SPR-MSL1           \ Missile frame 1: + shape (5 bytes)
$80AB CONSTANT SPR-MSL2           \ Missile frame 2: x shape (5 bytes)

\ ── Jovian AI data structures ────────────────────────────────────────────
\ Per-Jovian sprite + bg buffers (packed before GALAXY at $8000)
$80F0 CONSTANT JOV-BG0          \ 28 bytes: bg save buffer Jovian 0 (4x7)
$810C CONSTANT JOV-BG1          \ 28 bytes: bg save buffer Jovian 1
$8128 CONSTANT JOV-BG2          \ 28 bytes: bg save buffer Jovian 2
$8200 CONSTANT JOV-SPR0         \ 23 bytes: generated sprite Jovian 0
$8217 CONSTANT JOV-SPR1         \ 23 bytes: generated sprite Jovian 1
$822E CONSTANT JOV-SPR2         \ 23 bytes: generated sprite Jovian 2
$8245 CONSTANT JOV-EMCOL        \ 3 bytes: cached emotion color band

$80BE CONSTANT JOV-STATE        \ 3 bytes: 0=idle, 1=attack, 2=flee
$80C2 CONSTANT JOV-TICK         \ 3 bytes: per-Jovian frame counter
$80C6 CONSTANT JOV-OLDX         \ 3 bytes: previous x per Jovian
$80C9 CONSTANT JOV-OLDY         \ 3 bytes: previous y per Jovian

\ ── Genome data (AI diversity system) ──────────────────────────────────
\ 4 bytes per Jovian: behavior(2) + appearance(1) + emotion|origin(1)
$80CE CONSTANT JOV-GENOME       \ 12 bytes: 3 Jovians x 4 bytes
\ Intent output from jov-think: dx, dy, flags per Jovian
$80DA CONSTANT JOV-INTENT       \ 9 bytes: 3 Jovians x 3 bytes
\ Sprite generation workspace
$80E4 CONSTANT JOV-SPRWORK      \ 12 bytes: scratch for sprite gen
\ Quadrant mood grid (8x8 sectors, emotion persistence)
$8EB4 CONSTANT MOOD-GRID        \ 64 bytes: mood per sector

: jov-spr  ( i -- addr )  23 * JOV-SPR0 + ;

\ Dynamic centering offsets from sprite header
: jov-draw-dx  ( i -- dx )  jov-spr C@ 1 RSHIFT ;
: jov-draw-dy  ( i -- dy )  jov-spr 1 + C@ 1 RSHIFT ;

: init-sprites  ( -- )  sprite-data SPR-SHIP 46 CMOVE ;

\ ── Random position within tactical view ─────────────────────────────────
\ Returns x in 4-123, y in 4-139 (away from borders).


: rnd-x  ( -- x )  64 rnd 30 + ;     \ 30-93 (x: 128px wide viewport)
: rnd-y  ( -- y )  128 rnd 8 + ;     \ 8-135 (y: 144px tall viewport)

\ ── Galaxy generation ────────────────────────────────────────────────────
\ Single-pass generation: iterate all 64 quadrants, roll dice for each.
\ No retry loops — guaranteed to terminate.

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
  sync-qcounts

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

  \ Generate base position (away from gravity sources)
  qbase @ IF
    0 ss-safe !
    6 0 DO
      ss-safe @ 0= IF
        rnd-x BASE-POS C!
        rnd-y BASE-POS 1 + C!
        1 ss-safe !
        qstars @ ?DUP IF 0 DO
          BASE-POS STAR-POS I 2 * + mdist
          35 < IF 0 ss-safe ! THEN
        LOOP THEN
        qbhole @ IF
          BASE-POS BHOLE-POS mdist
          35 < IF 0 ss-safe ! THEN
        THEN
      THEN
    LOOP
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
      SHIP-POS STAR-POS I 2 * + mdist
      35 < IF 0 ss-safe ! THEN
    LOOP THEN
    qbhole @ IF
      SHIP-POS BHOLE-POS mdist
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

\ Count living Jovians (always checks all 3 slots)
: jov-alive?  ( -- flag )
  JOV-DMG C@ JOV-DMG 1 + C@ OR JOV-DMG 2 + C@ OR ;

\ Any living Jovian in attack (1) or flee (2) state?
: jov-engaged?  ( -- flag )
  0  3 0 DO
    JOV-DMG I + C@ IF
      JOV-STATE I + C@ IF 1 OR THEN
    THEN
  LOOP ;

\ ── Shared string words (dedup #268) ──
: s-destroyed  S" DESTROYED" rg-type ;
: s-again      S" AGAIN?" rg-type ;
: s-upsys      S" THE UP SYSTEM" rg-type ;
: s-command    S" COMMAND" rg-type ;

: draw-panel  ( -- )
  clear-panel

  \ Row 15: STARDATE      n  MISSILES      nn
  0 15 at-xy  S" STARDATE" rg-type  gtime @ 14 rg-u.r
  17 15 at-xy  S" MISSILES" rg-type  pmissiles @ 32 rg-u.r

  \ Row 16: QUADRANT    n n  ENERGY       nnn
  0 16 at-xy  S" QUADRANT" rg-type
  11 16 at-xy  pcol @ rg-u.  CHAR , rg-emit  prow @ rg-u.
  17 16 at-xy  S" ENERGY" rg-type  penergy @ 32 rg-u.r

  \ Row 17: COND/SOS left, SHIELDS right
  -1 prev-cond !  update-cond
  17 17 at-xy  S" SHIELDS" rg-type  pshields @ 32 rg-u.r

  \ Row 18: COMMAND prompt
  17 18 at-xy  s-command ;

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
  127 143 0   143 3 rg-line ;       \ bottom separator only

\ draw-stars CODE word (#249) — inlines rg-pset loop with fast twinkle.
CODE draw-stars
        PSHS    X               ; save IP
        LDB     $80B0           ; nstars
        LBEQ    @done
        LDX     #$8040          ; STAR-POS
@lp     PSHS    B               ; save counter
        ; ── Compute VRAM byte address: rv + y*32 + x/4 ──
        LDB     1,X             ; B = y
        LDA     #32
        MUL                     ; D = y * 32
        ADDD    FVAR_rv         ; D += VRAM base
        TFR     D,Y             ; Y = row start
        LDA     ,X              ; A = x
        LSRA
        LSRA                    ; A = x / 4
        LEAY    A,Y             ; Y = VRAM byte address
        ; ── Shift count: 6 - (x%4)*2 ──
        LDA     ,X              ; A = x
        ANDA    #3
        ASLA
        NEGA
        ADDA    #6              ; A = shift count
        PSHS    A               ; save shift count
        ; ── Fixed color from position: ((x + y) & 2) + 1 → 1 or 3 ──
        LDA     ,X              ; star_x
        ADDA    1,X             ; + star_y
        ANDA    #2              ; 0 or 2
        INCA                    ; 1 or 3
        ; ── Shift color into pixel position ──
        LDB     ,S              ; B = shift count
        BEQ     @ns
@sh     ASLA
        DECB
        BNE     @sh
@ns     PSHS    A               ; save shifted color
        ; ── Build clear mask and write pixel ──
        LDA     #3
        LDB     1,S             ; shift count
        BEQ     @nm
@sm     ASLA
        DECB
        BNE     @sm
@nm     COMA                    ; clear mask
        ANDA    ,Y              ; clear old pixel bits
        ORA     ,S              ; OR in new color
        STA     ,Y              ; write to VRAM
        LEAS    2,S             ; pop shifted color + shift count
        LEAX    2,X             ; next star
        PULS    B               ; restore counter
        DECB
        BNE     @lp
@done   PULS    X
        ;NEXT
;CODE

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


\ ── Background save/restore for flicker-free ship movement ────────────
\ Save 4 bytes × 5 rows of VRAM under the sprite bounding box.
\ Restore to erase without a black flash.
$805A CONSTANT SHIP-BG              \ 20-byte save buffer

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
$806E CONSTANT MSL-BG                \ 20-byte save buffer

: save-msl-bg  ( -- )
  MSL-BG msl-scrx 1 - msl-scry 1 - bg-save ;

: restore-msl-bg  ( -- )
  MSL-BG msl-px @ 1 - msl-py @ 1 - bg-restore ;

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
        ADDD    #$8200          ; JOV-SPR0
        STD     4,U             ; spr addr
        TFR     D,Y             ; Y = spr header
        LDA     ,S+             ; i
        ASLA                    ; A = i*2
        PSHS    A               ; save i*2
        LDX     #$804A          ; JOV-POS
        ; x = JOV-POS[i*2] - width/2
        LDB     ,Y              ; width
        LSRB
        NEGB
        ADDB    A,X             ; B = pos_x - width/2
        BCS     @xok
        CLRB                    ; clamp underflow to 0
@xok    CLRA
        STD     2,U             ; x
        ; y = JOV-POS[i*2+1] - height/2
        LDA     ,S+             ; i*2
        INCA                    ; i*2+1
        LDB     1,Y             ; height
        LSRB
        NEGB
        ADDB    A,X             ; B = pos_y - height/2
        BCS     @yok
        CLRB                    ; clamp underflow to 0
@yok    CLRA
        STD     ,U              ; y
        PULS    X
        ;NEXT
;CODE

CODE save-jov-oldpos-n   \ ( n -- )  copy JOV-POS to JOV-OLDX/Y for n Jovians
        PSHS    X               ; save IP
        LDB     1,U             ; B = count
        LEAU    2,U             ; pop
        TSTB
        BEQ     @done
        LDX     #$804A          ; JOV-POS
        LDY     #$80C6          ; JOV-OLDX
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

\ save-jov-bgs / restore-jov-bgs — CODE with inline bg calc + clamp
\ Iterates living Jovians, computes centered screen coords from JOV-POS,
\ saves/restores 4x7 VRAM bytes to/from per-Jovian bg buffers.

CODE save-jov-bgs   \ ( -- )
        PSHS    X
        LDA     $80B1           ; njovians
        TSTA
        LBEQ    @sdone
        CLRB                    ; B = j (Jovian index)
@sloop  PSHS    D               ; S+0=A(count) S+1=B(j)
        ; alive check: JOV-DMG[j]
        LDX     #$8056          ; JOV-DMG
        LDA     B,X
        BEQ     @snext
        ; buf = j * 28 + JOV-BG0
        LDA     1,S             ; j
        LDB     #28
        MUL
        ADDD    #$80F0
        PSHS    D               ; S+0..1=buf
        ; sprite header = j * 23 + JOV-SPR0
        LDA     3,S             ; j
        LDB     #23
        MUL
        ADDD    #$8200
        TFR     D,Y             ; Y = spr header
        ; screen_x = JOV-POS[j*2] - width/2
        LDA     3,S             ; j
        ASLA                    ; j*2
        LDX     #$804A          ; JOV-POS
        LDB     ,Y              ; width
        LSRB
        NEGB
        ADDB    A,X             ; pos_x - width/2
        BCS     @sxok
        CLRB
@sxok   PSHS    B               ; S+0=sx
        ; screen_y = JOV-POS[j*2+1] - height/2
        INCA                    ; j*2+1
        LDB     1,Y             ; height
        LSRB
        NEGB
        ADDB    A,X             ; pos_y - height/2
        BCS     @syok
        CLRB
@syok   ; VRAM addr = RGVRAM + sy*32 + sx/4
        LDA     #32
        MUL                     ; D = sy * 32
        ADDD    VAR_RGVRAM
        TFR     D,Y             ; Y = row base
        LDA     ,S+             ; A = sx, pop
        LSRA
        LSRA                    ; A = sx/4
        LEAY    A,Y             ; Y = VRAM addr
        LDX     ,S++            ; X = buf, pop
        ; save 4x7
        LDB     #7
@srow   LDA     ,Y
        STA     ,X+
        LDA     1,Y
        STA     ,X+
        LDA     2,Y
        STA     ,X+
        LDA     3,Y
        STA     ,X+
        LEAY    32,Y
        DECB
        BNE     @srow
@snext  PULS    D
        INCB                    ; next j
        DECA                    ; count--
        BNE     @sloop
@sdone  PULS    X
        ;NEXT
;CODE

CODE restore-jov-bgs   \ ( -- )
        PSHS    X
        LDA     $80B1           ; njovians
        TSTA
        LBEQ    @rdone
        CLRB                    ; B = j
@rloop  PSHS    D               ; S+0=A(count) S+1=B(j)
        ; alive check
        LDX     #$8056          ; JOV-DMG
        LDA     B,X
        BEQ     @rnext
        ; buf = j * 28 + JOV-BG0
        LDA     1,S             ; j
        LDB     #28
        MUL
        ADDD    #$80F0
        PSHS    D               ; S+0..1=buf
        ; sprite header = j * 23 + JOV-SPR0
        LDA     3,S             ; j
        LDB     #23
        MUL
        ADDD    #$8200
        TFR     D,Y             ; Y = spr header
        ; oldx = JOV-OLDX[j] - width/2
        LDA     3,S             ; j
        LDX     #$80C6          ; JOV-OLDX
        LDB     ,Y              ; width
        LSRB
        NEGB
        ADDB    A,X             ; oldx - width/2
        BCS     @rxok
        CLRB
@rxok   PSHS    B               ; S+0=sx
        ; oldy = JOV-OLDY[j] - height/2
        LDX     #$80C9          ; JOV-OLDY
        LDB     1,Y             ; height
        LSRB
        NEGB
        ADDB    A,X             ; oldy - height/2
        BCS     @ryok
        CLRB
@ryok   ; VRAM addr
        LDA     #32
        MUL
        ADDD    VAR_RGVRAM
        TFR     D,Y
        LDA     ,S+             ; sx, pop
        LSRA
        LSRA
        LEAY    A,Y
        LDX     ,S++            ; buf, pop
        ; restore 4x7
        LDB     #7
@rrow   LDA     ,X+
        STA     ,Y
        LDA     ,X+
        STA     1,Y
        LDA     ,X+
        STA     2,Y
        LDA     ,X+
        STA     3,Y
        LEAY    32,Y
        DECB
        BNE     @rrow
@rnext  PULS    D
        INCB
        DECA
        BNE     @rloop
@rdone  PULS    X
        ;NEXT
;CODE



: save-jov-oldpos  ( -- )  qjovians @ save-jov-oldpos-n ;

\ max-draw-y ( -- max )  Highest Y across ship + living Jovians (old + new)
\ Used by HSYNC beam-chasing: wait for beam to pass this row before drawing.
CODE max-draw-y
        PSHS    X
        ; Start with ship Y and old ship Y
        LDA     $8055           ; SHIP-POS+1 (ship y)
        LDB     FVAR_old_sy+1   ; old-sy (low byte)
        PSHS    B
        CMPA    ,S+             ; compare A with old-sy
        BHS     @s1
        TFR     B,A
@s1     ; Check living Jovians: current Y and old Y
        LDB     $80B1           ; njovians
        TSTB
        BEQ     @sdone
        PSHS    A               ; S+0=max, save across loop
        CLRB                    ; j = 0
@jlp    LDX     #$8056          ; JOV-DMG
        TST     B,X             ; alive?
        BEQ     @jnxt
        ; JOV-POS y = $804A + j*2 + 1
        PSHS    B               ; save j
        ASLB                    ; j*2
        INCB                    ; j*2+1
        LDX     #$804A          ; JOV-POS
        LDA     B,X             ; JOV-POS[j*2+1] = current y
        CMPA    1,S             ; compare with max (below saved j on stack)
        BLS     @j2
        STA     1,S             ; update max
@j2     LDB     ,S              ; j (original)
        LDX     #$80C9          ; JOV-OLDY
        LDA     B,X             ; JOV-OLDY[j]
        CMPA    1,S             ; compare with max
        BLS     @j3
        STA     1,S             ; update max
@j3     PULS    B               ; restore j
@jnxt   INCB
        CMPB    $80B1           ; j vs njovians
        BNE     @jlp
        PULS    A               ; A = max
@sdone  ; A = max Y; add sprite height margin (+7)
        ADDA    #7
        BCC     @noc
        LDA     #191            ; clamp to screen bottom
@noc    CLRB
        EXG     A,B             ; D = 0:maxY (big-endian 16-bit)
        STD     ,--U            ; push result
        PULS    X
        ;NEXT
;CODE

: draw-jovians-live  ( -- )
  qjovians @ ?DUP IF 0 DO
    JOV-DMG I + C@ IF I jov-spr-xy spr-draw THEN
  LOOP THEN ;

: init-jovian-ai  ( -- )
  qjovians @ ?DUP IF 0 DO
    I jbg-i !
    0 JOV-STATE jbg-i @ + C!
    I JOV-TICK jbg-i @ + C!          \ stagger: 0, 1, 2 — avoid simultaneous ticks
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

CODE jov-emo@   \ ( i -- e )
        LDA     1,U             ; A = i
        LDB     #4
        MUL
        ADDD    #$80D1          ; JOV-GENOME + 3
        TFR     D,Y
        LDA     ,Y
        LSRA
        LSRA
        LSRA
        LSRA                    ; A = emotion 0-15
        TFR     A,B
        CLRA
        STD     ,U
        ;NEXT
;CODE

CODE jov-emotion!   \ ( e i -- )
        LDA     1,U             ; A = i
        LDB     #4
        MUL
        ADDD    #$80D1          ; JOV-GENOME + 3
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

CODE jov-emotion-base   \ ( i -- e )  aggression-derived baseline
        LDA     1,U             ; A = i
        LDB     #4
        MUL
        ADDD    #$80CE          ; JOV-GENOME
        TFR     D,Y
        LDA     ,Y              ; byte 0
        LSRA
        LSRA
        LSRA
        LSRA
        LSRA                    ; A = aggression 0-7
        ASLA                    ; A = aggression * 2
        ADDA    #8              ; A = aggression * 2 + 8
        CMPA    #15
        BLS     @ok
        LDA     #15
@ok     TFR     A,B
        CLRA
        STD     ,U
        ;NEXT
;CODE

\ Drift 1 step toward baseline
: jov-emotion-decay  ( i -- )
  DUP jov-emo@ OVER jov-emotion-base  \ ( i cur base )
  2DUP = IF 2DROP DROP ELSE
  < IF 1 ELSE -1 THEN
  OVER jov-emo@ + SWAP jov-emotion! THEN ;

\ Apply stimulus (signed delta) to one Jovian
: jov-emotion-stim  ( delta i -- )
  DUP jov-emo@ ROT + SWAP jov-emotion! ;

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

60 CONSTANT EMOTION-DECAY-RATE    \ frames between decay ticks (tier-2: /2)
VARIABLE emotion-timer            \ frame counter for decay

\ ── Procedural Jovian sprite generation ────────────────────────────────
\ Each Jovian gets a unique sprite from its genome appearance seed.
\ Shape from seed, color from emotion band, density from origin.

CODE jov-color-band   \ ( emo -- band )  0=fear, 1=neutral, 2=rage
        LDA     1,U             ; A = emotion
        CMPA    #5
        BLO     @fear
        CMPA    #11
        BLO     @neut
        LDB     #2              ; rage
        BRA     @done
@fear   LDB     #0
        BRA     @done
@neut   LDB     #1
@done   CLRA
        STD     ,U              ; replace TOS
        ;NEXT
;CODE

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
        ADDD    #$8200          ; JOV-SPR0
        STD     $80E4           ; SPRWORK+0 = sprite addr
        ; --- Genome addr = i * 4 + JOV-GENOME ---
        LDA     ,S+             ; restore i
        LDB     #4
        MUL
        ADDD    #$80CE          ; JOV-GENOME
        TFR     D,Y             ; Y = genome ptr
        ; --- Seed (byte 2) → PRNG state, width, height ---
        LDA     2,Y             ; appearance seed
        STA     $80E9           ; PRNG state
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
@wdn    STA     $80EA           ; width
        LDX     $80E4
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
@hdn    STA     $80EB           ; height
        STA     1,X             ; sprite header byte 1
        ; Half-width = (width + 1) / 2
        LDA     $80EA
        INCA
        LSRA
        STA     $80E7           ; half_width
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
@cldn   STA     $80E6           ; 2bpp color
        ; === Clear sprite data ===
        LDX     $80E4
        LEAX    2,X
        LDA     $80EA
        ADDA    #3
        LSRA
        LSRA                    ; A = bpr
        LDB     $80EB
        MUL
        TSTB
        BEQ     @clrdn
@clrlp  CLR     ,X+
        DECB
        BNE     @clrlp
@clrdn
        ; === Per-row pixel generation ===
        CLR     $80ED           ; row = 0
@rowlp  LDB     $80E9           ; PRNG state
        LDA     #5
        MUL
        ADDB    #3
        STB     $80E9
        LDA     $80ED
        STA     $80EF           ; row for @setpx
        CLR     $80EC           ; col = 0
@collp  LDA     $80E9           ; state
        LDB     $80EC
        BEQ     @nsh
@shlp   LSRA
        DECB
        BNE     @shlp
@nsh    BITA    #$01
        BEQ     @nopx
        LDA     $80EC
        STA     $80EE           ; col for @setpx
        BSR     @setpx
        LDA     $80E7           ; half_width
        DECA
        CMPA    $80EC           ; center?
        BEQ     @nopx
        LDA     $80EA           ; width
        DECA
        SUBA    $80EC           ; mirror col
        STA     $80EE
        BSR     @setpx
@nopx   INC     $80EC
        LDA     $80EC
        CMPA    $80E7
        BCS     @collp
        INC     $80ED
        LDA     $80ED
        CMPA    $80EB
        BCS     @rowlp
        ; === Center column guarantee ===
        LDA     $80E7
        DECA
        STA     $80EE
        LDA     $80EB
        LSRA
        STA     $80EF
        BSR     @setpx
        ;
        PULS    X
        ;NEXT
        ;
@setpx  LDA     $80EA
        ADDA    #3
        LSRA
        LSRA
        LDB     $80EF
        MUL
        PSHS    D
        LDB     $80EE
        LSRB
        LSRB
        CLRA
        ADDD    ,S++
        LDX     $80E4
        LEAX    2,X
        LEAX    D,X
        LDA     $80EE
        ANDA    #$03
        NEGA
        ADDA    #3
        ASLA
        LDB     $80E6
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
    I jov-emo@ jov-color-band
    I JOV-EMCOL + C!
  LOOP THEN ;

\ Check if any Jovian's emotion crossed a color band → regenerate sprite
: jov-check-regen  ( -- )
  qjovians @ ?DUP IF 0 DO
    JOV-DMG I + C@ IF
      I jov-emo@ jov-color-band DUP
      I JOV-EMCOL + C@ <> IF
        I gen-jov-sprite
        I JOV-EMCOL + C!
      ELSE DROP THEN
    THEN
  LOOP THEN ;

\ ── Quadrant mood persistence ──────────────────────────────────────────
\ MOOD-GRID (64 bytes at $8EB4): one byte per sector, 0-15 scale.
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
      SWAP I jov-emo@ + SWAP 1 +
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

450 CONSTANT STARDATE-FRAMES      \ ~60 seconds per stardate (tier-3: /8)
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

\ Condition state: 0=green 1=yellow 2=red 3=docked 4=sos
VARIABLE prev-cond
: cond-state  ( -- n )
  sos-active @ IF 4 ELSE
  docked @ IF 3 ELSE
  jov-alive? 0= IF 0 ELSE
  jov-engaged? IF 2 ELSE 1
  THEN THEN THEN THEN ;

\ Redraw condition area (left half of row 17) — only on change
: update-cond  ( -- )
  cond-state DUP prev-cond @ = IF DROP ELSE
    DUP prev-cond !
    0 17 at-xy  14 0 DO $20 rg-emit LOOP
    0 17 at-xy
    DUP 4 = IF S" SOS-BASE" rg-type
      11 17 at-xy sos-col @ rg-u. CHAR , rg-emit sos-row @ rg-u.
    ELSE S" COND" rg-type
      DUP 3 = IF 8 17 at-xy S" DOCKED" rg-type ELSE
      DUP 0 = IF 9 17 at-xy S" GREEN" rg-type ELSE
      DUP 2 = IF 11 17 at-xy S" RED" rg-type ELSE
      8 17 at-xy S" YELLOW" rg-type
      THEN THEN THEN
    THEN DROP
  THEN ;

\ Check off-screen bases: any quadrant with both Jovians and a base
\ (not the player's quadrant) threatens that base each stardate.
: check-sos  ( -- )
  0 sos-active !
  64 0 DO
    GALAXY I + C@
    DUP q-base? IF DUP q-jovians IF
      I 8 /MOD                       \ ( qbyte col row )
      OVER pcol @ = OVER prow @ = AND IF
        2DROP                         \ skip current quadrant
      ELSE
        sos-row !  sos-col !          \ record threatened base
        1 sos-active !
        8 rnd 0= IF
          GALAXY I + C@ $FB AND GALAXY I + C!
          -1 gbases +!
        THEN
      THEN
    THEN THEN
    DROP                              \ always drop qbyte
  LOOP
  update-cond ;

\ ── Galaxy migration system (#160, #186, #187) ────────────────────────────
\ Jovians move between quadrants: drift toward bases, reinforce combat,
\ migrate toward high-mood zones.  All share migrate-jovian as core op.

VARIABLE mg-sc                        \ migration source col
VARIABLE mg-sr                        \ migration source row
VARIABLE mg-dc                        \ migration dest col
VARIABLE mg-dr                        \ migration dest row

\ migrate-jovian: move one Jovian from src to dst in galaxy bytes
\ Returns 1 on success, 0 if src empty or dst full.
: migrate-jovian  ( sc sr dc dr -- flag )
  mg-dr !  mg-dc !  mg-sr !  mg-sc !
  mg-sc @ mg-sr @ gal@ q-jovians 0=
  mg-dc @ mg-dr @ gal@ q-jovians 3 = OR IF
    0                                 \ src empty or dst full
  ELSE
    mg-sc @ mg-sr @ gal@ 1 -
    mg-sc @ mg-sr @ gal!              \ decrement src
    mg-dc @ mg-dr @ gal@ 1 +
    mg-dc @ mg-dr @ gal!              \ increment dst
    1
  THEN ;

\ Find nearest base quadrant (Manhattan distance from col,row)
\ Returns col row of nearest base, or -1 -1 if none.
VARIABLE fnb-bc                       \ best col
VARIABLE fnb-br                       \ best row
VARIABLE fnb-bd                       \ best distance

VARIABLE fnb-col                     \ search col (input)
VARIABLE fnb-row                     \ search row (input)

: find-nearest-base  ( col row -- bcol brow )
  fnb-row !  fnb-col !
  -1 fnb-bc !  -1 fnb-br !  99 fnb-bd !
  64 0 DO
    GALAXY I + C@ q-base? IF
      I 8 /MOD                        \ ( ic ir )
      fnb-row @ -                     \ ( ic drow )
      DUP 0 < IF NEGATE THEN           \ |drow|
      SWAP fnb-col @ -                \ ( |drow| dcol )
      DUP 0 < IF NEGATE THEN           \ |dcol|
      +                               \ Manhattan distance
      DUP fnb-bd @ < IF
        fnb-bd !
        I 8 /MOD fnb-br ! fnb-bc !
      ELSE DROP THEN
    THEN
  LOOP
  fnb-bc @ fnb-br @ ;

\ Step one grid cell toward target.  Cols differ → step col, else step row.
VARIABLE st-tc                        \ target col
VARIABLE st-tr                        \ target row

: step-toward  ( col row tcol trow -- ncol nrow )
  st-tr !  st-tc !                    \ ( col row )
  OVER st-tc @ <> IF                  \ cols differ?
    SWAP                              \ ( row col )
    DUP st-tc @ < IF 1 + ELSE 1 - THEN
    SWAP                              \ ( ncol row )
  ELSE                                \ same col → step row
    DUP st-tr @ < IF 1 + ELSE 1 - THEN
  THEN ;

\ ── Spawn reinforcement into current quadrant ────────────────────────────
\ When a Jovian migrates into the player's quadrant, spawn it on-screen.

VARIABLE free-slot                    \ scratch for find-free-slot

: find-free-slot  ( -- i | -1 )
  -1 free-slot !
  3 0 DO
    free-slot @ -1 = IF
      JOV-DMG I + C@ 0= IF I free-slot ! THEN
    THEN
  LOOP
  free-slot @ ;

\ Spawn a Jovian at screen edge based on source direction.
\ dir: 0=left, 1=right, 2=above, 3=below
VARIABLE sp-x                        \ spawn edge x
VARIABLE sp-y                        \ spawn edge y

: set-edge-pos  ( dir -- )
  DUP 0 = IF DROP   4 sp-x !  rnd-y sp-y ! ELSE
  DUP 1 = IF DROP 123 sp-x !  rnd-y sp-y ! ELSE
  DUP 2 = IF DROP rnd-x sp-x !    4 sp-y ! ELSE
             DROP rnd-x sp-x !  136 sp-y !
  THEN THEN THEN ;

VARIABLE spawn-pending                \ deferred spawn dir+1, or 0

: do-spawn  ( dir -- )
  find-free-slot DUP -1 = IF 2DROP ELSE
    SWAP DUP set-edge-pos             \ first attempt
    sp-x @ SHIP-POS C@ - abs
    sp-y @ SHIP-POS 1 + C@ - abs + 10 < IF
      DUP set-edge-pos               \ retry once if too close
    THEN DROP                         \ ( slot )
    DUP sp-x @ SWAP 2 * JOV-POS + C!    \ store x
    DUP sp-y @ SWAP 2 * JOV-POS + 1 + C!  \ store y
    DUP 100 SWAP JOV-DMG + C!        \ full health
    DUP gen-genome                    \ generate genome
    DUP 12 SWAP jov-emotion-stim      \ high emotion (rage)
    DUP 0 SWAP JOV-STATE + C!        \ ATTACK state
    DUP 0 SWAP JOV-TICK + C!         \ reset tick
    DUP DUP 2 * JOV-POS + C@ SWAP JOV-OLDX + C!
    DUP DUP 2 * JOV-POS + 1 + C@ SWAP JOV-OLDY + C!
    DUP gen-jov-sprite                \ generate sprite
    DUP DUP jov-emo@ jov-color-band SWAP JOV-EMCOL + C!
    DROP
    qjovians @ 3 < IF 1 qjovians +!
      qjovians @ QCOUNTS 1 + C! THEN
  THEN ;

\ Queue a spawn for deferred execution (after refresh-after-kill is available)
: spawn-reinforcement  ( dir -- )
  1 + spawn-pending ! ;

\ Direction a reinforcement arrives from, based on mg-sc/mg-sr (set by
\ migrate-jovian).  0=left 1=right 2=above 3=below.
: src-dir  ( -- dir )
  mg-sr @ prow @ < IF 2 ELSE         \ src above → from above
  mg-sr @ prow @ > IF 3 ELSE         \ src below → from below
  mg-sc @ pcol @ < IF 0              \ src left → from left
  ELSE 1 THEN                        \ src right → from right
  THEN THEN ;

\ ── #160: Stardate drift — Jovians creep toward bases ─────────────────────
VARIABLE drift-sc                     \ source col
VARIABLE drift-sr                     \ source row
VARIABLE drift-found                  \ search flag

: pick-occupied  ( -- flag )
  0 drift-found !
  16 0 DO
    drift-found @ 0= IF
      64 rnd 8 /MOD                   \ ( col row )
      2DUP prow @ = SWAP pcol @ = AND 0= IF
        2DUP gal@ q-jovians IF
          drift-sr !  drift-sc !
          1 drift-found !
        ELSE 2DROP THEN
      ELSE 2DROP THEN
    THEN
  LOOP
  drift-found @ ;

: tick-drift  ( -- )
  pick-occupied 0= IF ELSE
    drift-sc @ drift-sr @ find-nearest-base   \ ( bcol brow )
    DUP -1 = IF 2DROP ELSE
      st-tr !  st-tc !                \ reuse step-toward vars for base coords
      drift-sc @ drift-sr @
      st-tc @ st-tr @
      step-toward                     \ ( ncol nrow )
      \ migrate-jovian needs ( sc sr dc dr )
      >R >R                           \ save ncol nrow on return stack
      drift-sc @ drift-sr @
      R> R>                           \ ( dsc dsr ncol nrow )
      migrate-jovian                  \ ( flag )
      DUP IF
        \ Did it arrive in player's quadrant?
        mg-dc @ pcol @ = mg-dr @ prow @ = AND IF
          src-dir spawn-reinforcement
        THEN
      THEN DROP
    THEN
  THEN ;

\ ── #186: Reinforcements — adjacent Jovians warp in on kill ───────────────
VARIABLE reinf-found                  \ flag: found a source

: try-adjacent  ( acol arow -- )
  reinf-found @ IF 2DROP ELSE
    2DUP 0 < SWAP 0 < OR IF 2DROP ELSE
    2DUP 7 > SWAP 7 > OR IF 2DROP ELSE
      2DUP gal@ q-jovians IF
        \ Mood-based chance: mood/16 probability (hot = more likely)
        2DUP mood-addr C@ 1 +         \ mood+1 (1-16)
        16 rnd < IF                   \ roll under mood → reinforce
          pcol @ prow @               \ ( acol arow pcol prow = sc sr dc dr )
          migrate-jovian IF
            1 reinf-found !
          THEN
        ELSE 2DROP THEN
      ELSE 2DROP THEN
    THEN THEN
  THEN ;

: tick-reinforcement  ( -- )
  0 reinf-found !
  \ Check 4 adjacent quadrants
  pcol @ 1 - prow @     try-adjacent  \ left
  pcol @ 1 + prow @     try-adjacent  \ right
  pcol @     prow @ 1 - try-adjacent  \ above
  pcol @     prow @ 1 + try-adjacent  \ below
  reinf-found @ IF
    \ Spawn the reinforcement — find which direction it came from
    src-dir
    spawn-reinforcement
  THEN ;

\ ── #187: Galactic migration — aggressive Jovians drift to hot zones ──────
VARIABLE hot-col                      \ hottest quadrant col
VARIABLE hot-row                      \ hottest quadrant row
VARIABLE hot-mood                     \ highest mood found
VARIABLE calm-sc                      \ calm source col
VARIABLE calm-sr                      \ calm source row
VARIABLE calm-found                   \ flag

: find-hottest  ( -- )
  0 hot-mood !
  64 0 DO
    MOOD-GRID I + C@ DUP hot-mood @ > IF
      hot-mood !
      I 8 /MOD hot-row ! hot-col !
    ELSE DROP THEN
  LOOP ;

: pick-calm-source  ( -- flag )
  0 calm-found !
  16 0 DO
    calm-found @ 0= IF
      64 rnd 8 /MOD                   \ ( col row )
      2DUP prow @ = SWAP pcol @ = AND 0= IF
        2DUP gal@ q-jovians IF
          2DUP mood-addr C@ 10 < IF
            calm-sr !  calm-sc !
            1 calm-found !
          ELSE 2DROP THEN
        ELSE 2DROP THEN
      ELSE 2DROP THEN
    THEN
  LOOP
  calm-found @ ;

: tick-migration  ( -- )
  gtime @ 1 AND 0= IF                \ every 2nd stardate
    find-hottest
    hot-mood @ 10 > IF                \ only if somewhere is actually hot
      pick-calm-source IF
        calm-sc @ calm-sr @
        hot-col @ hot-row @
        step-toward                   \ ( ncol nrow )
        >R >R calm-sc @ calm-sr @ R> R>
        migrate-jovian                \ ( flag )
        DUP IF
          mg-dc @ pcol @ = mg-dr @ prow @ = AND IF
            src-dir
            spawn-reinforcement
          THEN
        THEN DROP
      THEN
    THEN
  THEN ;

\ Stardate tick: increment gtime, decay mood grid, check SOS
: tick-stardate  ( -- )
  stardate-timer @ 1 + DUP STARDATE-FRAMES < IF
    stardate-timer !
  ELSE
    DROP 0 stardate-timer !
    1 gtime +!
    9 15 at-xy  gtime @ 14 rg-u.r    \ update stardate display
    mood-decay-all
    check-sos
  THEN ;

\ Migration timer: independent of stardate, fires every ~15 seconds.
\ Called every 8th frame, so 112 ticks × 8 frames = 896 frames ≈ 15s.
112 CONSTANT MIGRATE-FRAMES
VARIABLE migrate-timer

: tick-migrate  ( -- )
  migrate-timer @ 1 + DUP MIGRATE-FRAMES < IF
    migrate-timer !
  ELSE
    DROP 0 migrate-timer !
    tick-drift
    tick-migration
  THEN ;

\ ── Detection & awareness ──────────────────────────────────────────────
\ Jovians start idle (JOV-STATE=0) on quadrant entry.  Every 30 frames,
\ idle Jovians roll detection: (pilot_skill + emotion) * 4 >= distance.
\ On detection → JOV-STATE=1 (attack) + distance-scaled alarm stimulus.
\ Firing maser/missile instantly reveals player to all Jovians.

15 CONSTANT DETECT-RATE            \ frames between detection rolls (tier-2: /2)

\ Manhattan distance from Jovian i to player
: jov-player-dist  ( i -- d )
  2 * JOV-POS + SHIP-POS mdist ;

\ Detection range from genome: (pilot_skill + emotion) * 4
: jov-detect-range  ( i -- r )
  DUP 4 * JOV-GENOME + C@ 7 AND  \ pilot_skill (0-7)
  SWAP jov-emo@               \ emotion (0-15)
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
  DUP 1 SWAP JOV-STATE + C!      \ set JOV-STATE = attack (0→1)
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
VARIABLE think-slot              \ 0-2: whose turn to think this even frame

\ ── Jovian AI (genome-driven) ──────────────────────────────────────────
\ jov-think CODE word computes intent (proposed position + flags) from
\ genome data, current positions, and target selection.
\ apply-intent applies obstacle avoidance and writes final position.
\
\ Intent buffer (JOV-INTENT + i*3): nx, ny, flags
\   flags bit 0 = targets base
\   flags bit 1 = base-stop (within 30px, hold position)


\ Per-Jovian tick threshold from genome (lower = faster)
\ jov-threshold removed (#166) — inlined into tick-jovians-inner CODE.
\ jov-avoid-dist removed (#243) — inlined into jov-gravity-pull CODE.
\ jov-blocked? removed (#241) — inlined into apply-intent CODE.

\ ── apply-intent CODE word (#241) ─────────────────────────────────────
\ ( i -- )  Read JOV-INTENT[i], apply 3-tier obstacle fallback, write
\ JOV-POS[i].  Sets jov-moved if position changed.  Inlines full
\ jov-blocked? logic as @chk subroutine.
CODE apply-intent
        PSHS    X               ; save IP
        ; Pop arg: i (Jovian index)
        LDB     1,U             ; B = i
        LEAU    2,U             ; pop 1 cell
        PSHS    B               ; S+0=i, S+1..2=saved IP

        ; Intent addr: X = JOV-INTENT + i*3 = $80DA + i*3
        LDA     #3
        MUL                     ; D = i*3 (in B since i<3)
        LDX     #$80DA
        ABX                     ; X = intent ptr

        ; Check base-stop (flags bit 1)
        LDA     2,X             ; flags
        ANDA    #2
        LBNE    @done           ; base-stop, no move

        ; Read proposed nx, ny
        LDA     ,X              ; nx
        LDB     1,X             ; ny
        PSHS    D               ; S+0=nx, S+1=ny, S+2=i, S+3..4=IP

        ; Pos addr: Y = JOV-POS + i*2 = $804A + i*2
        LDA     2,S             ; i
        ASLA                    ; i*2
        LDY     #$804A
        LEAY    A,Y             ; Y = &JOV-POS[i] (preserved across @chk)

        ; Save current pos for comparison
        LDA     ,Y              ; cur_x
        LDB     1,Y             ; cur_y
        PSHS    D               ; S+0=cur_x, S+1=cur_y, S+2=nx, S+3=ny, S+4=i, S+5..6=IP

        ; ── Tier 1: try (nx, ny) ──
        LDA     2,S             ; nx
        LDB     3,S             ; ny
        LBSR    @chk            ; A = blocked?
        TSTA
        BEQ     @apply          ; clear → apply nx, ny

        ; ── Tier 2: try (nx, cur_y) ──
        LDA     2,S             ; nx
        LDB     1,S             ; cur_y
        LBSR    @chk
        TSTA
        BEQ     @t2ok

        ; ── Tier 3: try (cur_x, ny) ──
        LDA     ,S              ; cur_x
        LDB     3,S             ; ny
        LBSR    @chk
        TSTA
        BEQ     @t3ok

        ; All blocked → stay put
        LEAS    5,S             ; pop cur_x, cur_y, nx, ny, i
        PULS    X
        ;NEXT

@t2ok   ; Tier 2 clear: use nx, keep cur_y
        LDA     2,S             ; nx
        LDB     1,S             ; cur_y (keep old y)
        BRA     @apply2
@t3ok   ; Tier 3 clear: use cur_x, ny
        LDA     ,S              ; cur_x (keep old x)
        LDB     3,S             ; ny
@apply2 STA     2,S             ; final_x → nx slot
        STB     3,S             ; final_y → ny slot
@apply  ; Apply position: write nx, ny to JOV-POS[i] if changed
        LDA     2,S             ; final_x
        LDB     3,S             ; final_y
        CMPA    ,S              ; changed x?
        BNE     @wr
        CMPB    1,S             ; changed y?
        BEQ     @nowr
@wr     STA     ,Y              ; write new x
        STB     1,Y             ; write new y
        LDD     #1
        STD     FVAR_jov_moved  ; jov-moved = 1
@nowr   LEAS    5,S             ; pop cur_x, cur_y, nx, ny, i
        PULS    X
        ;NEXT

@done   LEAS    1,S             ; pop i
        PULS    X
        ;NEXT

        ; ── @chk: obstacle check subroutine ──────────────────
        ; A = candidate x, B = candidate y
        ; Uses i from stack (offset depends on LBSR return addr)
        ; Returns A = 1 if blocked, 0 if clear.
        ; Preserves Y (pos addr).  Clobbers B, X.
        ; Stack at entry: S+0..2=LBSR ret, S+3=cur_x, S+4=cur_y,
        ;   S+5=nx, S+6=ny, S+7=i, S+8..9=saved IP
@chk    PSHS    D               ; S+0=cx, S+1=cy; ret at S+2..4
        ; S+0=cx, S+1=cy, S+2..4=ret, S+5=cur_x, S+6=cur_y,
        ;   S+7=nx, S+8=ny, S+9=i, S+10..11=IP

        ; Compute avoid_dist from genome[i*4] & 7 + 6
        LDA     9,S             ; i
        LDB     #4
        MUL
        LDX     #$80CE          ; JOV-GENOME
        ABX
        LDA     ,X
        ANDA    #7
        ADDA    #6              ; A = avoid_dist
        PSHS    A               ; S+0=avoid, S+1=cx, S+2=cy

        ; -- Stars --
        LDB     $80B0           ; nstars
        LBEQ    @cbh
        LDX     #$8040
@cslp   PSHS    B               ; save counter
        ; S: cnt(0) avoid(1) cx(2) cy(3)
        LDA     ,X              ; star_x
        SUBA    2,S             ; - cx
        BCC     @cs1
        NEGA
@cs1    TFR     A,B
        LDA     1,X             ; star_y
        SUBA    3,S             ; - cy
        BCC     @cs2
        NEGA
@cs2    PSHS    B
        ADDA    ,S+             ; A = manhattan
        CMPA    1,S             ; vs avoid_dist
        BLO     @csbl
        LEAX    2,X
        LDB     ,S+             ; pop counter
        DECB
        BNE     @cslp
        BRA     @cbh
@csbl   LEAS    1,S             ; pop counter
        LBRA    @cblk

        ; -- Black hole --
@cbh    LDA     $80B3
        BEQ     @cbs
        LDX     #$8052
        LBSR    @cmd
        CMPA    #15
        LBLO    @cblk

        ; -- Base --
@cbs    LDA     $80B2
        BEQ     @csh
        LDX     #$8050
        LBSR    @cmd
        CMPA    #5
        LBLO    @cblk

        ; -- Ship --
@csh    LDX     #$8054
        LBSR    @cmd
        CMPA    #8
        LBLO    @cblk

        ; -- Other Jovians --
        LDA     $80B1
        BEQ     @cclr
        LDB     #0              ; j
@cjlp   CMPB    9,S             ; j == i? (S+9=i; LBSR ret is 2 bytes not 3)
        BEQ     @cjnx
        PSHS    B
        LDX     #$8056
        ABX
        LDA     ,X
        BEQ     @cjdd
        LDB     ,S              ; j
        ASLB
        LDX     #$804A
        ABX
        LDA     ,X              ; jov_x
        SUBA    2,S             ; - cx (S+0=j, S+1=avoid, S+2=cx)
        BCC     @cj1
        NEGA
@cj1    TFR     A,B
        LDA     1,X
        SUBA    3,S             ; - cy
        BCC     @cj2
        NEGA
@cj2    PSHS    B
        ADDA    ,S+
        CMPA    #8
        PULS    B               ; restore j (no CC change)
        BLO     @cblk
        BRA     @cjnx
@cjdd   PULS    B
@cjnx   INCB
        CMPB    $80B1
        BLO     @cjlp

@cclr   LEAS    3,S             ; pop avoid, cx, cy
        CLRA                    ; not blocked
        RTS

@cblk   LEAS    3,S             ; pop avoid, cx, cy
        LDA     #1              ; blocked
        RTS

        ; -- Manhattan subroutine for @chk --
        ; X = ptr to (ox, oy).  Returns A = dist.
        ; Stack: S+0..1=ret, S+2=avoid, S+3=cx, S+4=cy
@cmd    LDA     ,X
        SUBA    3,S             ; - cx
        BCC     @cm1
        NEGA
@cm1    TFR     A,B
        LDA     1,X
        SUBA    4,S             ; - cy
        BCC     @cm2
        NEGA
@cm2    PSHS    B
        ADDA    ,S+
        RTS
;CODE

VARIABLE base-attack              \ frame counter for base destruction

\ ── jov-think: genome-driven intent computation (6809 CODE) ────────────
\ ( i qbase -- )
\ Reads: JOV-POS, JOV-DMG, SHIP-POS, BASE-POS, JOV-GENOME
\ Writes: JOV-INTENT + i*3 = { proposed_x, proposed_y, flags }
\
\ Target selection:
\   Jovian 0 → base (if exists), DMG < 50 → base, else → ship
\ Base-stop: within 30px manhattan of base → hold position
\ Ship engagement: emotion-driven preferred range
\   range = 20 + (15 - emotion) * 3  (rage=20px, fear=65px)
\   dist < range → move away (caution)
\   dist = range → hold position
\   dist > range → approach

CODE jov-think  ( i qbase -- )
        PSHS    X               ; save IP
        LDA     1,U             ; A = qbase (low byte)
        LDB     3,U             ; B = i (low byte)
        LEAU    4,U             ; pop 2 args
        PSHS    D               ; [S+0]=qbase, [S+1]=i

        ; Compute intent addr: Y = $80DA + i*3
        LDA     #3
        MUL                     ; D = i*3
        ADDD    #$80DA
        TFR     D,Y             ; Y = intent output

        ; Compute pos addr: X = $804A + i*2
        LDB     1,S             ; B = i
        ASLB
        LDX     #$804A
        ABX                     ; X = &JOV-POS[i]

        ; Save current position
        LDA     ,X              ; cx
        LDB     1,X             ; cy
        PSHS    D               ; [S+0]=cx [S+1]=cy [S+2]=qbase [S+3]=i

        ; --- Target selection ---
        LDX     #$8054          ; default: SHIP-POS
        LDA     2,S             ; qbase
        BEQ     @calc           ; no base, target ship
        LDA     3,S             ; i
        BEQ     @tbase          ; i==0, target base
        LDX     #$8056          ; JOV-DMG
        LDA     A,X             ; DMG[i]
        CMPA    #50
        BHS     @calc           ; healthy, target ship

@tbase  LDX     #$8050          ; BASE-POS
        BRA     @calc2
@calc   LDX     #$8054          ; SHIP-POS
@calc2

        ; --- Flags: bit 0 = targets_base ---
        CLR     2,Y             ; flags = 0
        CMPX    #$8050
        BNE     @ship
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
        LBHS    @twd
        ; Within 30px, stay put
        LBRA    @hold

        ; --- Ship engagement: emotion-driven range ---
@ship   ; Compute manhattan dist to ship
        LDA     ,X              ; tx (ship x)
        SUBA    ,S              ; tx - cx
        BPL     @sax
        NEGA
@sax    TFR     A,B             ; B = |tx-cx|
        LDA     1,X             ; ty (ship y)
        SUBA    1,S             ; ty - cy
        BPL     @say
        NEGA
@say    PSHS    B               ; save |dx|
        ADDA    ,S+             ; A = manhattan dist
        PSHS    A               ; save dist on stack
        ; Compute preferred range: 20 + (15 - emotion) * 3
        LDA     4,S             ; i (S: dist,cx,cy,qbase,i)
        LDB     #4
        MUL
        ADDD    #$80D1          ; JOV-GENOME + 3
        TFR     D,X
        LDA     ,X              ; genome byte 3
        LSRA
        LSRA
        LSRA
        LSRA                    ; A = emotion 0-15
        LDB     #3
        EORA    #$0F            ; A = 15 - emotion
        MUL                     ; B = (15-emotion)*3
        ADDB    #20             ; B = preferred range
        ; Restore ship pos pointer
        LDX     #$8054          ; SHIP-POS
        ; Compare dist vs preferred range (+-3 dead zone)
        ; A = dist (on stack), B = range
        LDA     ,S+             ; A = dist, pop it
        ; Check approach: dist > range+3
        PSHS    A               ; save dist
        SUBB    ,S              ; B = range - dist
        LBLT    @chka            ; range < dist, check approach
        CMPB    #3              ; range - dist > 3?
        LBHI    @chkr            ; yes: dist < range-3, retreat
        PULS    A               ; dead zone: pop dist
        LBRA    @hold
@chka   PULS    A               ; pop dist
        NEGB                    ; B = dist - range
        CMPB    #3              ; dist - range > 3?
        LBHI    @twd            ; yes: approach
        LBRA    @hold           ; no: dead zone
@chkr   PULS    A               ; pop dist
        ; dist < range-3: move AWAY from target
        ; --- Retreat x: cx + sign(cx - tx), clamped ---
        LDA     ,S              ; cx
        CMPA    ,X              ; vs tx
        BEQ     @rkx
        BHI     @rincx          ; cx > tx: flee right
        DECA                    ; cx < tx: flee left
        CMPA    #4
        BHS     @rsx
        LDA     ,S
        BRA     @rsx
@rincx  INCA
        CMPA    #123
        BLS     @rsx
        LDA     ,S
@rsx    STA     ,Y              ; intent.nx
        BRA     @rny
@rkx    LDA     ,S
        STA     ,Y
        ; --- Retreat y: cy + sign(cy - ty), clamped ---
@rny    LDA     1,S             ; cy
        CMPA    1,X             ; vs ty
        BEQ     @rky
        BHI     @rincy          ; cy > ty: flee down
        DECA
        CMPA    #4
        BHS     @rsy
        LDA     1,S
        BRA     @rsy
@rincy  INCA
        CMPA    #136
        BLS     @rsy
        LDA     1,S
@rsy    STA     1,Y             ; intent.ny
        BRA     @done
@rky    LDA     1,S
        STA     1,Y
        BRA     @done

        ; --- Hold position ---
@hold   LDA     ,S              ; cx
        STA     ,Y              ; intent.nx = cx
        LDA     1,S             ; cy
        STA     1,Y             ; intent.ny = cy
        LDA     2,Y
        ORA     #$02            ; flags |= hold
        STA     2,Y
        BRA     @done

        ; --- Approach: 1px step toward target, clamped ---
@twd    LDA     ,X              ; tx
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
        CMPA    #136
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

CODE jov-flee   \ ( i -- )  write flee intent to JOV-INTENT
        PSHS    X
        LDA     1,U             ; A = i
        LEAU    2,U             ; pop
        PSHS    A               ; save i
        ; Intent addr: Y = JOV-INTENT + i*3
        LDB     #3
        MUL                     ; D = i*3
        ADDD    #$80DA
        TFR     D,Y             ; Y = intent
        ; Pos addr: X = JOV-POS + i*2
        LDA     ,S              ; i
        ASLA
        LDX     #$804A
        LEAX    A,X             ; X = JOV-POS + i*2
        ; Clear flags
        CLR     2,Y
        PULS    A               ; done with i
        ; Flee x: move away from SHIP-POS
        LDA     ,X              ; A = cx
        CMPA    $8054           ; vs ship_x
        BEQ     @kx
        BHI     @fx1
        DECA                    ; flee left
        CMPA    #4
        BHS     @sx
        LDA     #4
        BRA     @sx
@fx1    INCA
        CMPA    #123
        BLS     @sx
        LDA     #123
@sx     STA     ,Y              ; intent.nx
        BRA     @ny
@kx     LDA     ,X
        STA     ,Y
        ; Flee y
@ny     LDA     1,X             ; A = cy
        CMPA    $8055           ; vs ship_y
        BEQ     @ky
        BHI     @fy1
        DECA
        CMPA    #4
        BHS     @sy
        LDA     #4
        BRA     @sy
@fy1    INCA
        CMPA    #136
        BLS     @sy
        LDA     #139
@sy     STA     1,Y             ; intent.ny
        BRA     @done
@ky     LDA     1,X
        STA     1,Y
@done   PULS    X
        ;NEXT
;CODE

: jov-flee-check  ( i -- )  \ DMG below 50 → flee + fear
  DUP JOV-DMG + C@ 50 < IF
    2 OVER JOV-STATE + C!     \ flee state
    0 SWAP jov-emotion!       \ fear → sprite turns blue
  ELSE DROP THEN ;

\ apply-intent is now a CODE word defined above (#241).
\ Signature changed from ( -- ) to ( i -- ).
\ jtk-nx, jtk-ny no longer needed (intent read inline).

VARIABLE detect-timer              \ frame counter for detection rolls

\ jov-threshold removed (#166) — inlined into tick-jovians-inner CODE.

\ ── tick-jovians-inner CODE word (slot-based scheduling) ─────────────
\ ( -- mask )  Slot-based think scheduling.  Each even frame, advance
\   think-slot (0->1->2->0..) wrapping at njovians.  Only the Jovian at
\   that slot is considered.  If alive+aware, increment its skip counter
\   (JOV-TICK).  Compute skip factor from genome:
\     skip = (12 - (pilot_skill + speed_mod)) >> 1, clamped [1,6]
\   If counter >= skip: fire (reset counter, return mask bit).
\   Otherwise: return 0.  At most 1 Jovian thinks per even frame.
CODE tick-jovians-inner
        PSHS    X               ; save IP
        ; -- Any Jovians? --
        LDA     $80B1           ; njovians
        BEQ     @zero
        ; -- Advance slot: (slot + 1) % njovians --
        LDB     FVAR_think_slot+1   ; current slot (low byte)
        INCB
        PSHS    A               ; push njovians
        CMPB    ,S+             ; slot vs njovians (pop)
        BLO     @w1
        CLRB
@w1     STB     FVAR_think_slot+1   ; B = i (this frame's Jovian)
        ; -- Alive? JOV-DMG[i] > 0 --
        LDX     #$8056          ; JOV-DMG
        ABX
        TST     ,X
        BEQ     @zero
        ; -- Aware? JOV-STATE[i] != 0 --
        LDB     FVAR_think_slot+1
        LDX     #$80BE          ; JOV-STATE
        ABX
        TST     ,X
        BEQ     @zero
        ; -- Advance skip counter: JOV-TICK[i] += 1 --
        LDB     FVAR_think_slot+1
        LDX     #$80C2          ; JOV-TICK (repurposed as skip counter)
        ABX                     ; X = &JOV-TICK[i]
        INC     ,X
        ; -- Compute skip factor from genome --
        ; skip = (12 - (skill + speed_mod)) >> 1, min 1
        LDB     FVAR_think_slot+1
        LDA     #4
        MUL                     ; D = i*4 (in B since i<4)
        LDY     #$80CE          ; JOV-GENOME
        LEAY    B,Y
        LDA     ,Y              ; byte 0
        ANDA    #7              ; pilot_skill (0-7)
        LDB     1,Y             ; byte 1
        LSRB
        LSRB
        LSRB
        LSRB
        ANDB    #3              ; speed_mod (0-3)
        PSHS    A
        ADDB    ,S+             ; B = skill + speed (0-10)
        LDA     #12
        PSHS    B
        SUBA    ,S+             ; A = 12 - sum (2..12)
        LSRA                    ; A = skip factor (1..6)
        BNE     @c1
        LDA     #1              ; clamp min (safety)
@c1     ; -- Fire if counter >= skip --
        CMPA    ,X              ; skip vs counter (X = &JOV-TICK[i])
        BHI     @zero           ; counter < skip: not yet
        ; -- Fire: reset counter, build mask --
        CLR     ,X              ; JOV-TICK[i] = 0
        LDB     FVAR_think_slot+1   ; i (0, 1, or 2)
        LDA     #1
        BRA     @sh
@sl     LSLA
@sh     DECB
        BPL     @sl             ; A = 1 << i
        TFR     A,B
        CLRA                    ; D = 0:mask
        BRA     @push
@zero   CLRA
        CLRB                    ; D = 0
@push   LEAU    -2,U
        STD     ,U              ; push mask
        PULS    X               ; restore IP
        ;NEXT
;CODE

\ Dispatch think/flee for Jovians whose threshold fired.
\ Timers for detection and emotion decay.
: tick-jovians  ( -- )
  tick-jovians-inner
  3 0 DO
    DUP 1 AND IF
      I jbg-i !
      JOV-STATE I + C@ 2 = IF
        I jov-flee  I apply-intent
      ELSE
        I qbase @ jov-think
        I apply-intent
      THEN
    THEN
    1 RSHIFT
  LOOP DROP
  detect-timer @ 1 + DUP 15 < IF
    detect-timer !
  ELSE
    DROP 0 detect-timer !
    jov-detect-tick
  THEN
  emotion-timer @ 1 + DUP 60 < IF
    emotion-timer !
  ELSE
    DROP 0 emotion-timer !
    jov-emotion-decay-all
  THEN ;

\ ── Jovian gravity — split into contact + pull CODE words (#243) ──────
\ jov-contact: CODE word, scans all alive Jovians for contact kills
\   (mdist < 3 to any star or black hole).  Returns index of first
\   Jovian to kill, or -1 if none.  Runs every frame (safety-critical).
\ jov-gravity-pull: CODE word, applies 1px drift toward nearby stars
\   and black hole, gated by grav-tick & 3 = 0.  Odd frames only.
\ jov-contact-check: Forth wrapper, loops jov-contact + jov-kill.

\ ── jov-contact CODE word ────────────────────────────────────────────
\ ( -- idx | -1 )  Scan all alive Jovians for obstacle contact.
\ Returns index of first Jovian with mdist < 3 to any star or black hole.
\ Returns -1 if no contact.  Caller handles the kill in Forth.
CODE jov-contact
        PSHS    X               ; save IP
        LDA     $80B1           ; njovians (QCOUNTS shadow)
        BEQ     @none
        CLRB                    ; B = i (Jovian index)
@lp     PSHS    B               ; save i
        LDX     #$8056          ; JOV-DMG
        ABX
        TST     ,X
        BEQ     @nx             ; dead, skip

        ; Get Jovian position addr -> X
        LDB     ,S              ; i
        ASLB                    ; i*2
        LDX     #$804A          ; JOV-POS
        ABX                     ; X = &JOV-POS[i]

        ; -- Check black hole --
        LDA     $80B3           ; hasbhole
        BEQ     @stars
        ; mdist(JOV-POS[i], BHOLE-POS)
        LDA     ,X              ; jov_x
        SUBA    $8052           ; - bhole_x
        BCC     @bh1
        NEGA
@bh1    TFR     A,B
        LDA     1,X             ; jov_y
        SUBA    $8053           ; - bhole_y
        BCC     @bh2
        NEGA
@bh2    PSHS    B
        ADDA    ,S+             ; A = mdist
        CMPA    #3
        BLO     @hit            ; contact!

        ; -- Check stars --
@stars  LDB     $80B0           ; nstars
        BEQ     @nx
        LDY     #$8040          ; STAR-POS
@slp    PSHS    B               ; save star counter
        LDA     ,X              ; jov_x
        SUBA    ,Y              ; - star_x
        BCC     @s1
        NEGA
@s1     TFR     A,B
        LDA     1,X             ; jov_y
        SUBA    1,Y             ; - star_y
        BCC     @s2
        NEGA
@s2     PSHS    B
        ADDA    ,S+             ; A = mdist
        CMPA    #3
        BLO     @shit           ; star contact!
        LEAY    2,Y             ; next star
        LDB     ,S+             ; pop star counter
        DECB
        BNE     @slp
        BRA     @nx

@shit   LEAS    1,S             ; pop star counter
@hit    LDB     ,S+             ; pop i = kill index
        CLRA
        LEAU    -2,U            ; push result
        STD     ,U
        PULS    X
        ;NEXT

@nx     PULS    B               ; restore i
        INCB
        CMPB    $80B1
        BLO     @lp
@none   LDD     #$FFFF          ; -1 = no contact
        LEAU    -2,U
        STD     ,U
        PULS    X
        ;NEXT
;CODE

\ Forth wrapper: loop jov-contact + jov-kill until no more contacts
: jov-contact-check  ( -- )
  BEGIN jov-contact DUP 0 < IF DROP EXIT THEN
    jbg-i ! jov-kill
  AGAIN ;

\ ── jov-gravity-pull CODE word ───────────────────────────────────────
\ ( -- )  Apply gravity pull toward stars and black hole for all alive
\ Jovians.  Gated: entire word skips if grav-tick & 3 != 0.
\ Inlines pilot avoid_dist from genome.  Sets jov-moved if any moved.
CODE jov-gravity-pull
        PSHS    X               ; save IP
        ; Gate: skip entirely if not a qualifying frame
        LDA     FVAR_grav_tick+1 ; low byte of grav-tick
        ANDA    #3
        LBNE    @done

        LDA     $80B1           ; njovians
        LBEQ    @done
        CLRB                    ; B = i
@lp     PSHS    B               ; save i
        LDX     #$8056          ; JOV-DMG
        ABX
        TST     ,X
        LBEQ    @nx             ; dead, skip

        ; Get Jovian pos addr -> Y (preserved across pulls)
        LDB     ,S              ; i
        ASLB
        LDY     #$804A          ; JOV-POS
        LEAY    B,Y             ; Y = &JOV-POS[i]

        ; Compute avoid_dist from genome: pilot_skill & 7 + 6
        LDB     ,S              ; i
        LDA     #4
        MUL                     ; D = i*4 (in B since i<3)
        LDX     #$80CE          ; JOV-GENOME
        ABX
        LDA     ,X
        ANDA    #7
        ADDA    #6              ; A = avoid_dist (6..13)
        PSHS    A               ; S+0=avoid, S+1=i

        ; -- Black hole pull --
        LDA     $80B3           ; hasbhole
        BEQ     @sp
        ; mdist to bhole
        LDA     ,Y              ; jov_x
        SUBA    $8052
        BCC     @bp1
        NEGA
@bp1    TFR     A,B
        LDA     1,Y             ; jov_y
        SUBA    $8053
        BCC     @bp2
        NEGA
@bp2    PSHS    B
        ADDA    ,S+             ; A = mdist
        CMPA    #20
        BHS     @sp             ; outside well, no pull
        ; Pull toward bhole
        LDA     $8052           ; bhole_x = tx
        LDB     $8053           ; bhole_y = ty
        LBSR    @pull

        ; -- Star pull --
@sp     LDB     $80B0           ; nstars
        LBEQ    @nx2
        LDX     #$8040          ; STAR-POS
@splp   PSHS    B               ; save star counter
        ; S: cnt(0) avoid(1) i(2)
        ; mdist to this star
        LDA     ,Y              ; jov_x
        SUBA    ,X              ; - star_x
        BCC     @sp1
        NEGA
@sp1    TFR     A,B
        LDA     1,Y             ; jov_y
        SUBA    1,X             ; - star_y
        BCC     @sp2
        NEGA
@sp2    PSHS    B
        ADDA    ,S+             ; A = mdist
        ; Skip contact range (handled by jov-contact)
        CMPA    #3
        BLO     @snx            ; contact range, skip (jov-contact handles)
        ; Check avoid_dist: if dist >= avoid_dist, no pull
        CMPA    1,S             ; vs avoid_dist
        BHS     @snx            ; outside avoidance zone, no pull
        ; Distance tiers
        CMPA    #6
        BLO     @spull          ; close: always pull
        ; Far (6..avoid_dist): only every other qualifying frame
        LDA     FVAR_grav_tick+1
        ANDA    #1
        BNE     @snx            ; skip this qualifying frame
@spull  PSHS    X               ; save star ptr
        LDA     ,X              ; star_x = tx
        LDB     1,X             ; star_y = ty
        BSR     @pull
        PULS    X               ; restore star ptr
@snx    LEAX    2,X             ; next star
        LDB     ,S+             ; pop star counter
        DECB
        BNE     @splp

@nx2    LEAS    1,S             ; pop avoid_dist
@nx     PULS    B               ; pop i
        INCB
        CMPB    $80B1
        LBLO    @lp
@done   PULS    X
        ;NEXT

        ; ── @pull subroutine ────────────────────────────────────
        ; Pull (Y) toward (A=tx, B=ty) by 1px per axis.
        ; Sets jov-moved = 1 if any axis moved.  Preserves Y.
@pull   PSHS    D               ; S+0=tx, S+1=ty
        ; -- X axis --
        LDA     ,Y              ; cur_x
        CMPA    ,S              ; vs tx
        BEQ     @py
        BHI     @pxd
        INCA                    ; move toward (cur < target)
        BRA     @pxs
@pxd    DECA                    ; move toward (cur > target)
@pxs    CMPA    #2              ; clamp lower
        BHS     @pxnl
        LDA     #2
@pxnl   CMPA    #125            ; clamp upper
        BLS     @pxnh
        LDA     #125
@pxnh   STA     ,Y
        LDD     #1
        STD     FVAR_jov_moved
        LDA     ,S              ; reload tx (clobbered by LDD #1)
@py     ; -- Y axis --
        LDA     1,Y             ; cur_y
        CMPA    1,S             ; vs ty
        BEQ     @pd
        BHI     @pyd
        INCA
        BRA     @pys
@pyd    DECA
@pys    CMPA    #2
        BHS     @pynl
        LDA     #2
@pynl   CMPA    #139
        BLS     @pynh
        LDA     #139
@pynh   STA     1,Y
        LDD     #1
        STD     FVAR_jov_moved
@pd     LEAS    2,S             ; pop tx, ty
        RTS
;CODE

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
  cancel-beam cancel-jbeam cancel-msl
  clear-tactical
  draw-border draw-stars draw-storm-stars draw-event-horizon
  draw-base
  save-jov-oldpos
  save-jov-bgs
  save-ship-pos save-ship-bg
  draw-jovians-live
  draw-ship
  0 moved !  0 jov-moved ! ;

\ Execute deferred Jovian spawn (requires refresh-after-kill)
: check-spawn  ( -- )
  spawn-pending @ ?DUP IF
    1 - do-spawn
    0 spawn-pending !
    1 jov-moved !
    cancel-jbeam cancel-beam
    cancel-msl
    refresh-after-kill
  THEN ;

\ Kill Jovian (index in jbg-i) — erase sprite, zero health, explode
VARIABLE check-win                \ flag: a kill happened, check win/lose

: jov-kill  ( -- )
  JOV-DMG jbg-i @ + C@ IF
    jbg-i @ jov-spr-xy spr-erase-box
    0 JOV-DMG jbg-i @ + C!
    -1 gjovians +!
    1 check-win !
    JOV-POS jbg-i @ 2 * + C@
    JOV-POS jbg-i @ 2 * + 1 + C@
    explode-jovian
    proximity-damage
    3 jov-emotion-all              \ fellow killed: rage/panic
    refresh-after-kill
    tick-reinforcement              \ adjacent Jovians may warp in (#186)
    check-spawn                     \ execute deferred spawn if queued
    update-cond
    \ Clear SOS if we just saved this base
    sos-active @ IF
      pcol @ sos-col @ = prow @ sos-row @ = AND IF
        0 sos-active !  update-cond
      THEN
    THEN
  THEN ;

\ jov-gravity-one / jov-gravity / jov-pull removed (#243).
\ Replaced by jov-contact CODE + jov-gravity-pull CODE above.

\ ── Explosion effects ────────────────────────────────────────────────
\ Animated expanding ring explosion.  Each frame generates dots along
\ a ring at increasing radius, cycling white→red→fade.  Uses a buffer
\ at $8734 for up to 32 (x,y) pairs = 64 bytes.  Clamped to screen.
\ Game loop pauses during the explosion (synchronous).

$8734 CONSTANT EXPLBUF               \ explosion dot buffer (x,y pairs)
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
        LDX     #$804A          ; JOV-POS (fixed address)
        LDY     #$8056          ; JOV-DMG (fixed address)
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
        -1 gjovians +!
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
VARIABLE jbeam-pending             \ 0=none, >0=raw trace count awaiting resolve
5 CONSTANT JBEAM-DMG              \ energy damage to player per hit
20 CONSTANT JBEAM-SYS-DMG         \ system damage per hit (out of 100)

\ Fire cooldown: 150 - (level * 14), min 24 frames
\ Level 1: ~136 frames (2.3s), Level 5: ~80 frames (1.3s), Level 9: ~24 frames (0.4s)
: jbeam-cooldown  ( -- n )
  150 glevel @ 14 * - DUP 24 < IF DROP 24 THEN ;

CODE clamp-jbeam   \ ( -- )  Clamp jbeam x1/y1/x2/y2 to screen bounds
        PSHS    X
        ; x1: [1, 125]
        LDD     FVAR_jbeam_x1
        CMPD    #1
        BGE     @x1h
        LDD     #1
@x1h    CMPD    #125
        BLE     @x1ok
        LDD     #125
@x1ok   STD     FVAR_jbeam_x1
        ; y1: [1, 141]
        LDD     FVAR_jbeam_y1
        CMPD    #1
        BGE     @y1h
        LDD     #1
@y1h    CMPD    #141
        BLE     @y1ok
        LDD     #141
@y1ok   STD     FVAR_jbeam_y1
        ; x2: [1, 126]
        LDD     FVAR_jbeam_x2
        CMPD    #1
        BGE     @x2h
        LDD     #1
@x2h    CMPD    #126
        BLE     @x2ok
        LDD     #126
@x2ok   STD     FVAR_jbeam_x2
        ; y2: [1, 142]
        LDD     FVAR_jbeam_y2
        CMPD    #1
        BGE     @y2h
        LDD     #1
@y2h    CMPD    #142
        BLE     @y2ok
        LDD     #142
@y2ok   STD     FVAR_jbeam_y2
        PULS    X
        ;NEXT
;CODE

\ Check if player ship bbox overlaps any pixel in the Jovian beam path
\ Ship sprite is 7x5 centered at SHIP-POS: x+-3, y+-2
CODE jbeam-ship-hit?   \ ( -- flag )
        PSHS    X
        LDD     FVAR_jbeam_total
        BEQ     @nohit          ; no pixels -> 0
        TFR     D,Y             ; Y = count
        LDX     #$8474          ; JBEAM-PATH
        LDA     $8054           ; SHIP-POS x
        LDB     $8055           ; SHIP-POS y
        PSHS    D               ; S+0=ship_x, S+1=ship_y
@lp     LDA     ,X              ; pixel_x
        SUBA    ,S              ; pixel_x - ship_x
        BCC     @xp
        NEGA                    ; abs
@xp     CMPA    #4
        BHS     @nx             ; |dx| >= 4, skip
        LDA     1,X             ; pixel_y
        SUBA    1,S             ; pixel_y - ship_y
        BCC     @yp
        NEGA
@yp     CMPA    #3
        BHS     @nx             ; |dy| >= 3, skip
        ; Hit!
        LEAS    2,S             ; pop ship pos
        LDD     #1
        BRA     @push
@nx     LEAX    3,X             ; next pixel (3 bytes/entry)
        LEAY    -1,Y
        BNE     @lp
        LEAS    2,S             ; pop ship pos
@nohit  CLRA
        CLRB
@push   LEAU    -2,U
        STD     ,U
        PULS    X
        ;NEXT
;CODE

\ Pick a random living Jovian: attack state, or idle + in detect range.
\ Returns index (0-2) or -1 if none found.  Picks first match from a
\ random starting index, wrapping around.  Inlines jov-detect? logic:
\   detect_range = (pilot_skill + emotion) * 4
\   dist = mdist(JOV-POS[i], SHIP-POS)
\   detect if range > dist
VARIABLE pj-result
CODE pick-jovian   \ ( -- i|-1 )
        PSHS    X
        LDA     $80B1           ; njovians
        BEQ     @none
        ; Random start: (seed low byte) mod njovians
        LDB     FVAR_seed+1     ; low byte of seed
        ANDB    #$03            ; 0-3
        PSHS    A
        CMPB    ,S+             ; B >= njovians?
        BLO     @rok
        CLRB
@rok    ; A = njovians (loop count), B = start index
        PSHS    A               ; S+0 = remaining count
@lp     PSHS    B               ; S+0=i, S+1=count
        ; -- Alive? JOV-DMG[i] > 0 --
        LDX     #$8056
        ABX
        TST     ,X
        BEQ     @nx
        ; -- State: JOV-STATE[i] --
        LDB     ,S              ; i
        LDX     #$80BE
        ABX
        LDA     ,X              ; A = state
        CMPA    #1              ; attack?
        BEQ     @found
        TSTA                    ; idle (0)?
        BNE     @nx
        ; -- Inline jov-detect? --
        ; range = (pilot_skill + emotion) * 4
        LDB     ,S              ; i
        LDA     #4
        MUL                     ; B = i*4
        LDX     #$80CE          ; JOV-GENOME
        ABX
        LDA     ,X              ; byte 0
        ANDA    #7              ; pilot_skill (0-7)
        LDB     3,X             ; byte 3
        LSRB
        LSRB
        LSRB
        LSRB                    ; emotion (0-15)
        PSHS    A
        ADDB    ,S+             ; B = skill + emotion
        ASLB
        ASLB                    ; B = detect range
        ; mdist(JOV-POS[i], SHIP-POS)
        PSHS    B               ; S+0=range, S+1=i, S+2=count
        LDB     1,S             ; i
        ASLB
        LDX     #$804A          ; JOV-POS
        ABX
        LDA     ,X              ; jov_x
        SUBA    $8054           ; - ship_x
        BCC     @d1
        NEGA
@d1     LDB     1,X             ; jov_y
        SUBB    $8055           ; - ship_y
        BCC     @d2
        NEGB
@d2     PSHS    A
        ADDB    ,S+             ; B = mdist
        LDA     ,S+             ; A = range (pop); S+0=i, S+1=count
        PSHS    B
        CMPA    ,S+             ; range - dist
        BLS     @nx             ; range <= dist: not detected
        ; -- Found (attack or detected idle) --
@found  LDB     ,S              ; i
        STB     FVAR_pj_result+1  ; save for cooldown scaling
        LEAS    2,S             ; pop i + count
        CLRA                    ; D = 0:i
        BRA     @push
@nx     PULS    B               ; pop i; S+0=count
        INCB
        CMPB    $80B1           ; >= njovians?
        BLO     @nw
        CLRB
@nw     DEC     ,S              ; count--
        BNE     @lp
        LEAS    1,S             ; pop count
@none   LDD     #$FFFF          ; -1
@push   LEAU    -2,U
        STD     ,U
        PULS    X
        ;NEXT
;CODE

\ ── fire-jbeam split: trace (phase 1) + resolve (phase 2) ──────────────
\ Phase 1: cancel old beam, compute direction, trace path into buffer.
\   Stores raw trace count in jbeam-pending (>0 = awaiting resolve).
\   jbeam-total stays 0 so no bolt animation starts yet.
\   Sets cooldown immediately so the timer starts ticking.
: fire-jbeam-trace  ( i target -- )
  cancel-jbeam
  SWAP
  2 * JOV-POS + DUP C@ jbeam-x1 !
  1 + C@ jbeam-y1 !
  DUP C@ jbeam-x1 @ - 4 *
  SWAP 1 + C@ jbeam-y1 @ - 4 *
  OVER jbeam-x1 @ + jbeam-x2 !
  DUP  jbeam-y1 @ + jbeam-y2 !
  SWAP DUP 0= IF DROP ELSE 0 < IF -5 ELSE 5 THEN jbeam-x1 @ + jbeam-x1 ! THEN
  DUP 0= IF DROP ELSE 0 < IF -5 ELSE 5 THEN jbeam-y1 @ + jbeam-y1 ! THEN
  clamp-jbeam
  jbeam-x1 @ jbeam-y1 @ jbeam-x2 @ jbeam-y2 @ JBEAM-PATH beam-trace
  jbeam-pending !
  jbeam-cooldown
  pj-result @ DUP 0 < 0= IF
    jov-emo@ 5 * 140 SWAP -
    * 100 /MOD SWAP DROP
  ELSE DROP THEN
  jbeam-cool ! ;

\ Phase 2: truncate at obstacles, check ship hit, start bolt animation.
: fire-jbeam-resolve  ( -- )
  JBEAM-PATH jbeam-pending @ beam-find-obstacle jbeam-total !
  jbeam-ship-hit? jbeam-hit-ship !
  JBEAM-PATH jbeam-total @ beam-scrub-sprites
  0 jbeam-head !  0 jbeam-tail !
  0 jbeam-pending ! ;

\ Synchronous fire (both phases at once) — used by tick-base-attack
: fire-jbeam  ( i target -- )
  fire-jbeam-trace fire-jbeam-resolve ;

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

\ Tick: resolve pending beam, or cooldown toward next shot.
\ Phase 1 (trace) and phase 2 (resolve) run on consecutive frames.
: tick-jbeam  ( -- )
  jbeam-pending @ IF
    fire-jbeam-resolve
  ELSE
    jbeam-cool @ ?DUP IF
      1 - jbeam-cool !
    ELSE
      docked @ 0= base-attack @ 0= AND IF
        pick-jovian DUP 0 < IF DROP ELSE SHIP-POS fire-jbeam-trace THEN
      THEN
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
$8190 CONSTANT FSTAR-POS          \ 25 × 3 = 75 bytes
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
$81DC CONSTANT SPIRAL-POS         \ 32 × 2 = 64 bytes
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
VARIABLE frame-tick               \ main loop frame counter
VARIABLE move-count               \ counts moves; energy charged every 4th
VARIABLE prev-energy              \ last displayed energy (for dirty check)
VARIABLE prev-missiles            \ last displayed missile count
VARIABLE prev-docked              \ last displayed dock state
10 CONSTANT MASER-COST            \ energy per maser fire

: use-energy  ( cost -- )
  penergy @ SWAP - DUP 0 < IF DROP 0 THEN penergy ! ;

VARIABLE was-near-base

\ move-ship CODE word: keyboard scan + try-move + collision checks
\ Inlines: ship-dx, ship-dy, ship-on-base?, ship-jov-blocked?, try-move.
\ Scans 4 arrow key columns via PIA0 ($FF00/$FF02) directly.
\ Uses Y register for axis address (preserved across subroutines).
\ Reads QCOUNTS shadow bytes at $80B0 for base/Jovian presence.
CODE move-ship
        PSHS    X               ; save IP

        ; -- Clear moved flag --
        LDD     #0
        STD     FVAR_moved

        ; -- Check penergy > 0 --
        LDD     FVAR_penergy
        LBEQ    @exit

        ; -- Compute was-near-base = ship-on-base? --
        LBSR    @onbase
        TFR     A,B             ; result in B (low byte)
        CLRA                    ; high byte = 0
        STD     FVAR_was_near_base  ; big-endian: 0:result

        ; -- Compute speed from pdmg-ion --
        ; > 67 -> 3, > 34 -> 2, else 1
        LDA     FVAR_pdmg_ion+1
        CMPA    #68
        BLO     @sp2
        LDA     #3
        BRA     @spd
@sp2    CMPA    #35
        BLO     @sp1
        LDA     #2
        BRA     @spd
@sp1    LDA     #1
@spd    PSHS    A               ; S+0=speed, S+1..2=saved IP

        ; -- Scan UP: col 3 ($F7), row 3 ($08) --
        LDA     #$F7
        STA     $FF02
        LDA     $FF00
        COMA
        ANDA    #$08
        BEQ     @noup
        LDY     #$8055          ; SHIP-POS+1 (Y axis)
        LDA     ,S              ; speed
        NEGA                    ; up = negative
        LDB     #139
        LBSR    @try
@noup
        ; -- Scan DN: col 4 ($EF), row 3 --
        LDA     #$EF
        STA     $FF02
        LDA     $FF00
        COMA
        ANDA    #$08
        BEQ     @nodn
        LDY     #$8055
        LDA     ,S              ; speed (positive = down)
        LDB     #139
        LBSR    @try
@nodn
        ; -- Scan LT: col 5 ($DF), row 3 --
        LDA     #$DF
        STA     $FF02
        LDA     $FF00
        COMA
        ANDA    #$08
        BEQ     @nolt
        LDY     #$8054          ; SHIP-POS (X axis)
        LDA     ,S
        NEGA                    ; left = negative
        LDB     #123
        LBSR    @try
@nolt
        ; -- Scan RT: col 6 ($BF), row 3 --
        LDA     #$BF
        STA     $FF02
        LDA     $FF00
        COMA
        ANDA    #$08
        BEQ     @nort
        LDY     #$8054
        LDA     ,S              ; speed (positive = right)
        LDB     #123
        LBSR    @try
@nort
        ; -- Deselect keyboard columns --
        LDA     #$FF
        STA     $FF02

        ; -- Pop speed --
        LEAS    1,S

        ; -- Update move-count / energy drain --
        LDD     FVAR_moved
        BEQ     @exit
        LDA     FVAR_move_count+1
        INCA
        CMPA    #32
        BNE     @svmc
        ; Every 32 moves: drain 1 energy
        CLRA
        LDD     FVAR_penergy
        SUBD    #1
        BPL     @eok
        LDD     #0
@eok    STD     FVAR_penergy
        CLRA                    ; A = 0 for move_count
@svmc   TFR     A,B             ; count in B (low byte)
        CLRA                    ; high byte = 0
        STD     FVAR_move_count

@exit   PULS    X               ; restore IP
        ;NEXT

        ; ── try-move subroutine ──────────────────────────────
        ; Y = axis address (SHIP-POS or SHIP-POS+1)
        ; A = signed delta, B = max bound
        ; Preserves Y.  Modifies A, B, X.
@try    PSHS    D               ; S+0=delta, S+1=max
        LDA     ,Y              ; current pos
        ADDA    ,S              ; + delta = new
        CMPA    #4
        BLO     @tund           ; < 4: out of bounds
        CMPA    1,S             ; > max?
        BHI     @tund
        ; Write tentative position
        STA     ,Y
        ; Check ship-on-base?
        LBSR    @onbase         ; A = 0/1 (Y preserved)
        TSTA
        BEQ     @tjov           ; not on base
        LDA     FVAR_was_near_base+1
        BNE     @tjov           ; already near, allow escape
        ; Undo: newly on base
        LDA     ,Y
        SUBA    ,S              ; new - delta = old
        STA     ,Y
        LEAS    2,S
        RTS
@tjov   ; Check ship-jov-blocked?
        LBSR    @sjblk          ; A = 0/1 (Y preserved)
        TSTA
        BEQ     @tok            ; not blocked
        ; Undo: blocked by Jovian
        LDA     ,Y
        SUBA    ,S
        STA     ,Y
        LEAS    2,S
        RTS
@tok    LEAS    2,S
        LDD     #1
        STD     FVAR_moved
        RTS
@tund   LEAS    2,S
        RTS

        ; ── ship-on-base? subroutine ─────────────────────────
        ; Returns A = 1 if within 5px on both axes, else 0.
        ; Does not modify Y.
@onbase LDA     $80B2           ; QCOUNTS hasbase
        BEQ     @ob0
        LDA     $8054           ; ship_x
        SUBA    $8050           ; - base_x
        BCC     @ob1
        NEGA
@ob1    CMPA    #5
        BHS     @ob0            ; |dx| >= 5
        LDA     $8055           ; ship_y
        SUBA    $8051           ; - base_y
        BCC     @ob2
        NEGA
@ob2    CMPA    #5
        BHS     @ob0            ; |dy| >= 5
        LDA     #1
        RTS
@ob0    CLRA
        RTS

        ; ── ship-jov-blocked? subroutine ─────────────────────
        ; Returns A = 1 if ship within 8px manhattan of any live
        ; Jovian, else 0.  Does not modify Y.
@sjblk  LDA     $80B1           ; QCOUNTS njovians
        BEQ     @sj0
        LDB     #0              ; j = 0
@sjlp   PSHS    B               ; save j
        LDX     #$8056          ; JOV-DMG
        ABX
        LDA     ,X
        BEQ     @sjdd           ; dead, skip
        ; Manhattan: |ship_x - jov_x| + |ship_y - jov_y|
        LDB     ,S              ; j
        ASLB
        LDX     #$804A          ; JOV-POS
        ABX
        LDA     $8054           ; ship_x
        SUBA    ,X              ; - jov_x
        BCC     @sja
        NEGA
@sja    TFR     A,B             ; B = |dx|
        LDA     $8055           ; ship_y
        SUBA    1,X             ; - jov_y
        BCC     @sjc
        NEGA
@sjc    PSHS    B
        ADDA    ,S+             ; A = manhattan
        CMPA    #8
        PULS    B               ; restore j (no CC change)
        BLO     @sjht           ; blocked!
        BRA     @sjnx
@sjdd   PULS    B               ; dead: pop j
@sjnx   INCB
        CMPB    $80B1           ; j < njovians?
        BLO     @sjlp
@sj0    CLRA                    ; not blocked
        RTS
@sjht   LDA     #1              ; blocked
        RTS
;CODE

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

VARIABLE grav-tick


\ ── ship-gravity CODE word (#242) ────────────────────────────────────────
\ Replaces gravity-well + star-gravity + xyn-pull + xy-pull + star-pull.
\ ( -- )  Increments grav-tick.  Pulls ship toward black hole (tiered:
\ <6=2px always, 6-30=1px gated) and stars (<5=1px every qualifying
\ frame, 5-10=every other).  Sets moved flag if ship position changed.
\ @pull subroutine: caller pushes step byte, then BSR with A=tx, B=ty.
CODE ship-gravity
        PSHS    X               ; save IP
        ; Increment grav-tick
        LDD     FVAR_grav_tick
        ADDD    #1
        STD     FVAR_grav_tick

        LDY     #$8054          ; Y = SHIP-POS (preserved across pulls)

        ; ── Black hole gravity ──
        LDA     $80B3           ; hasbhole
        LBEQ    @stars
        ; mdist(SHIP-POS, BHOLE-POS)
        LDA     ,Y              ; ship_x
        SUBA    $8052           ; - bhole_x
        BCC     @bm1
        NEGA
@bm1    TFR     A,B
        LDA     1,Y             ; ship_y
        SUBA    $8053           ; - bhole_y
        BCC     @bm2
        NEGA
@bm2    PSHS    B
        ADDA    ,S+             ; A = mdist
        CMPA    #6
        BLO     @bclose
        ; 6-30: gated 1px
        CMPA    #31
        BHS     @stars          ; outside well
        LDA     FVAR_grav_tick+1
        ANDA    #3
        BNE     @stars
        LDB     #1
        PSHS    B               ; push step=1
        LDA     $8052
        LDB     $8053
        BSR     @pull
        LEAS    1,S             ; pop step
        BRA     @stars
@bclose ; <6: 2px always (inescapable)
        LDB     #2
        PSHS    B               ; push step=2
        LDA     $8052
        LDB     $8053
        BSR     @pull
        LEAS    1,S             ; pop step

        ; ── Star gravity ──
@stars  LDA     FVAR_grav_tick+1
        ANDA    #3
        LBNE    @done           ; skip entirely on 3/4 frames
        LDB     $80B0           ; nstars
        LBEQ    @done
        LDX     #$8040          ; STAR-POS
@slp    PSHS    B               ; save counter
        ; mdist(SHIP-POS, star)
        LDA     ,Y              ; ship_x
        SUBA    ,X              ; - star_x
        BCC     @sm1
        NEGA
@sm1    TFR     A,B
        LDA     1,Y             ; ship_y
        SUBA    1,X             ; - star_y
        BCC     @sm2
        NEGA
@sm2    PSHS    B
        ADDA    ,S+             ; A = mdist
        CMPA    #11
        BHS     @snx            ; >10: outside range
        CMPA    #5
        BLO     @spull          ; <5: always pull
        ; 5-10: every other qualifying frame
        LDA     FVAR_grav_tick+1
        ANDA    #1
        BNE     @snx
@spull  PSHS    X               ; save star ptr
        LDB     #1
        PSHS    B               ; push step=1
        LDA     ,X              ; star_x = tx
        LDB     1,X             ; star_y = ty
        BSR     @pull
        LEAS    1,S             ; pop step
        PULS    X               ; restore star ptr
@snx    LEAX    2,X             ; next star
        PULS    B               ; restore counter
        DECB
        BNE     @slp

@done   PULS    X
        ;NEXT

        ; ── @pull subroutine ────────────────────────────────────
        ; Pull ship (at Y) toward target by step px per axis.
        ; Entry: A=tx, B=ty.  Step at 4,S (pushed by caller before BSR).
        ; Stack: S+0..1=ret addr, S+2=step (caller pushed before BSR)
        ; ... but after PSHS D: S+0=tx, S+1=ty, S+2..3=ret, S+4=step
        ; Sets FVAR_moved = 1 if any axis moved.  Preserves Y.
@pull   PSHS    D               ; S+0=tx, S+1=ty
        ; -- X axis --
        LDA     ,Y              ; cur_x
        CMPA    ,S              ; vs tx
        BEQ     @py
        BHI     @pxd
        ADDA    4,S             ; cur_x + step
        BRA     @pxs
@pxd    SUBA    4,S             ; cur_x - step
@pxs    CMPA    #2
        BHS     @pxnl
        LDA     #2
@pxnl   CMPA    #125
        BLS     @pxnh
        LDA     #125
@pxnh   STA     ,Y
        LDD     #1
        STD     FVAR_moved
@py     ; -- Y axis --
        LDA     1,Y             ; cur_y
        CMPA    1,S             ; vs ty
        BEQ     @pd
        BHI     @pyd
        ADDA    4,S             ; cur_y + step
        BRA     @pys
@pyd    SUBA    4,S             ; cur_y - step
@pys    CMPA    #2
        BHS     @pynl
        LDA     #2
@pynl   CMPA    #139
        BLS     @pynh
        LDA     #139
@pynh   STA     1,Y
        LDD     #1
        STD     FVAR_moved
@pd     LEAS    2,S             ; pop tx, ty
        RTS
;CODE

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
  penergy @ 20 < IF                    \ 0-19%: +4 every frame
    4 penergy +!
  ELSE penergy @ 40 < IF               \ 20-39%: +2 every frame
    2 penergy +!
  ELSE penergy @ 60 < IF               \ 40-59%: +1 every frame
    1 penergy +!
  ELSE penergy @ 80 < IF               \ 60-79%: +1 every 2 frames
    dock-tick @ 1 AND 0= IF 1 penergy +! THEN
  ELSE                                  \ 80-99%: +1 every 4 frames
    dock-tick @ 3 AND 0= IF 1 penergy +! THEN
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

\ (update-cond moved earlier — before check-sos)

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

\ Show rejection feedback in command area (#238)
: cmd-reject  ( addr len -- )
  clear-cmd-area  17 18 at-xy  rg-type  2 cmd-state ! ;

: draw-cmd-prompt  ( -- )
  clear-cmd-area
  17 18 at-xy
  s-command ;

\ ══════════════════════════════════════════════════════════════════════════
\  BEAM SYSTEM — Pixel-save/restore for artifact-free rendering
\ ══════════════════════════════════════════════════════════════════════════
\
\ Beams save what's underneath pixel-by-pixel and erase by restoring.
\ Animation: a fixed-length "bolt" travels from ship to target.
\ Layer 2: always drawn last, always erased first.

\ ── Path buffers (in free RAM $821C+) ──────────────────────────────────
\ 3 bytes per pixel × 200 max pixels = 600 bytes each

$821C CONSTANT BEAM-PATH           \ player maser path buffer (600 bytes)
$8474 CONSTANT JBEAM-PATH          \ Jovian beam path buffer (600 bytes)

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


VARIABLE bchk-hitpx                \ pixel index where hit was found

\ Walk path buffer, check each pixel against Jovian bounding boxes.
\ Returns Jovian index of first hit, or -1.  Stores hit pixel index
\ to bchk-hitpx for beam truncation.
CODE beam-check-path-hits   \ ( buf count -- hit-idx|-1 )
        PSHS    X
        LDD     ,U              ; count
        LDX     2,U             ; buf
        LEAU    4,U             ; pop 2 args
        CMPD    #0
        BEQ     @nohit
        LDA     $80B1           ; njovians
        BEQ     @nohit
        ; Stack: njovians, pixel_count(16)
        PSHS    D               ; S+0..1 = pixel count
        LDA     $80B1
        PSHS    A               ; S+0=njovians, S+1..2=count
        CLR     FVAR_bchk_hitpx
        CLR     FVAR_bchk_hitpx+1   ; pixel_idx = 0
@plp    ; -- outer: iterate pixels --
        CLRA                    ; A = j (Jovian index)
@jlp    PSHS    A               ; save j; S+0=j, S+1=njov, S+2..3=count
        ; alive? JOV-DMG[j]
        LDY     #$8056
        LEAY    A,Y
        TST     ,Y
        BEQ     @jnx
        ; bbox check: |pixel_x - jov_x| < 4 AND |pixel_y - jov_y| < 3
        LDA     ,S              ; j
        ASLA                    ; j*2
        LDY     #$804A          ; JOV-POS
        LEAY    A,Y
        LDA     ,X              ; pixel_x
        SUBA    ,Y              ; - jov_x
        BCC     @cx
        NEGA
@cx     CMPA    #4
        BHS     @jnx
        LDA     1,X             ; pixel_y
        SUBA    1,Y             ; - jov_y
        BCC     @cy
        NEGA
@cy     CMPA    #3
        BHS     @jnx
        ; HIT! j = ,S
        LDB     ,S              ; j = hit Jovian
        LEAS    4,S             ; pop j + njov + count
        CLRA                    ; D = 0:j (positive = hit)
        BRA     @push
@jnx    PULS    A               ; restore j
        INCA
        CMPA    ,S              ; >= njovians?
        BLO     @jlp
        ; No hit at this pixel, advance
        LEAX    3,X             ; next pixel entry
        INC     FVAR_bchk_hitpx+1   ; pixel_idx++
        ; Decrement count
        LDD     1,S             ; pixel_count (16-bit)
        SUBD    #1
        STD     1,S
        BNE     @plp
        LEAS    3,S             ; pop njov + count
@nohit  LDD     #$FFFF          ; -1 (no hit)
@push   LEAU    -2,U
        STD     ,U
        PULS    X
        ;NEXT
;CODE

\ ── Clamp beam coordinates to tactical view ──────────────────────────

\ Scan path buffer for first non-black pixel (obstacle)
\ Scan path buffer for first non-black saved pixel (obstacle).
\ Returns index of first obstacle, or count if path is clear.
CODE beam-find-obstacle   \ ( buf count -- index )
        PSHS    X
        LDD     ,U              ; count
        LDX     2,U             ; buf
        LEAU    4,U             ; pop 2 args
        CMPD    #0
        BEQ     @push           ; count=0 -> return 0
        TFR     D,Y             ; Y = remaining
        CLRA
        CLRB                    ; D = 0 (pixel index)
@lp     TST     2,X             ; saved_color byte (3rd byte of entry)
        BNE     @push           ; non-zero = obstacle found, D = index
        LEAX    3,X             ; next entry
        ADDD    #1              ; index++
        LEAY    -1,Y
        BNE     @lp
        ; No obstacle: D = count (iterated all)
@push   LEAU    -2,U
        STD     ,U
        PULS    X
        ;NEXT
;CODE

CODE clamp-beam   \ ( -- )  Clamp beam x1/y1/x2/y2 to screen bounds
        PSHS    X
        LDD     FVAR_beam_x1
        CMPD    #1
        BGE     @x1h
        LDD     #1
@x1h    CMPD    #125
        BLE     @x1ok
        LDD     #125
@x1ok   STD     FVAR_beam_x1
        LDD     FVAR_beam_y1
        CMPD    #1
        BGE     @y1h
        LDD     #1
@y1h    CMPD    #141
        BLE     @y1ok
        LDD     #141
@y1ok   STD     FVAR_beam_y1
        LDD     FVAR_beam_x2
        CMPD    #1
        BGE     @x2h
        LDD     #1
@x2h    CMPD    #126
        BLE     @x2ok
        LDD     #126
@x2ok   STD     FVAR_beam_x2
        LDD     FVAR_beam_y2
        CMPD    #1
        BGE     @y2h
        LDD     #1
@y2h    CMPD    #142
        BLE     @y2ok
        LDD     #142
@y2ok   STD     FVAR_beam_y2
        PULS    X
        ;NEXT
;CODE

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

\ ── beam-scrub-pos ( buf count cx cy -- ) ─────────────────────────────
\ Zero saved_color for beam pixels within ±4 of cx and ±3 of cy.
\ Buffer format: 3 bytes per pixel (x, y, saved_color).
CODE beam-scrub-pos
        PSHS    X
        LDD     4,U             ; D = count
        BEQ     @done
        TFR     D,Y             ; Y = count
        LDX     6,U             ; X = buf
        ; Compute bounds from cx, cy
        LDA     3,U             ; cx
        SUBA    #4
        PSHS    A               ; S+0 = x_min
        ADDA    #8
        PSHS    A               ; S+0 = x_max, S+1 = x_min
        LDA     1,U             ; cy
        SUBA    #3
        PSHS    A               ; S+0 = y_min
        ADDA    #6
        PSHS    A               ; S+0 = y_max, S+1 = y_min, S+2 = x_max, S+3 = x_min
        LEAU    8,U             ; pop 4 args
@lp     LDA     ,X              ; pixel x
        CMPA    3,S             ; < x_min?
        BLO     @skip
        CMPA    2,S             ; > x_max?
        BHI     @skip
        LDA     1,X             ; pixel y
        CMPA    1,S             ; < y_min?
        BLO     @skip
        CMPA    ,S              ; > y_max?
        BHI     @skip
        CLR     2,X             ; zero saved_color
@skip   LEAX    3,X             ; next pixel
        LEAY    -1,Y
        BNE     @lp
        LEAS    4,S             ; pop bounds
@done   PULS    X
        ;NEXT
;CODE

\ ── beam-scrub-sprites ( buf count -- ) ──────────────────────────────
\ Scrub saved_color for all dynamic sprites: ship + living Jovians.
: beam-scrub-sprites  ( buf count -- )
  2DUP SHIP-POS C@ SHIP-POS 1 + C@ beam-scrub-pos
  qjovians @ ?DUP IF 0 DO
    JOV-DMG I + C@ IF
      2DUP JOV-POS I 2 * + C@ JOV-POS I 2 * + 1 + C@ beam-scrub-pos
    THEN
  LOOP THEN
  2DROP ;

\ ── Fire maser ─────────────────────────────────────────────────────────
\ Trace path, detect hits, start bolt animation.

: fire-maser  ( angle -- )
  penergy @ MASER-COST 1 + < IF DROP EXIT THEN
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
  \ Scrub sprite pixels from saved backgrounds (after obstacle/hit detection)
  BEAM-PATH beam-total @ beam-scrub-sprites
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
  beam-hit-idx @ jov-flee-check
  \ If dead, cancel beams, explode, refresh
  JOV-DMG beam-hit-idx @ + C@ 0= IF
    -1 gjovians +!
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
256 CONSTANT MSL-SPEED           \ 2 pixels per frame (fixed-point /128)
5 CONSTANT MSL-COST              \ energy per missile

: msl-spr  ( -- addr )
  msl-frame @ 1 RSHIFT 1 AND IF SPR-MSL2 ELSE SPR-MSL1 THEN ;

: msl-scrx  ( -- x )  msl-x @ 7 RSHIFT ;
: msl-scry  ( -- y )  msl-y @ 7 RSHIFT ;

: msl-kill  ( -- )  \ deactivate missile + trigger full redraw
  msl-active @ IF
    SPR-MSL1 msl-px @ 1 - msl-py @ 1 - spr-erase-box
    0 msl-active !  1 jov-moved !
  THEN ;
: cancel-msl  ( -- )  msl-kill ;

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
          -1 gjovians +!
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
    msl-kill  draw-border
    EXIT
  THEN
  \ Check Jovian hit (refresh-after-kill already redraws everything)
  msl-hit? IF
    msl-kill  0 msl-dirty !
    EXIT
  THEN
  1 msl-dirty ! ;

: fire-missile  ( angle -- )
  pmissiles @ 0= IF DROP EXIT THEN
  penergy @ MSL-COST 1 + < IF DROP EXIT THEN
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
  2DUP pcol @ = SWAP prow @ = AND IF 2DROP EXIT THEN
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
  \ Update galaxy byte: count living Jovians
  0 qjovians @ ?DUP IF 0 DO
    JOV-DMG I + C@ IF 1 + THEN
  LOOP THEN
  pcol @ prow @ gal@ $FC AND OR  \ replace low 2 bits with living count
  pcol @ prow @ gal!
  \ Save mood before leaving quadrant
  mood-save
  \ Clear beams and missile
  cancel-jbeam cancel-beam
  cancel-msl
  \ Expand new quadrant and redraw
  rg-pcls
  SWAP expand-quadrant
  safe-spawn
  draw-quadrant
  \ Clear SOS if we just arrived at the threatened base
  sos-active @ IF
    pcol @ sos-col @ = prow @ sos-row @ = AND IF
      0 sos-active !
    THEN
  THEN
  draw-panel
  0 docked !  0 prev-docked !
  0 jov-moved !  0 base-attack !  0 think-slot !
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
  cancel-msl
  0 17 at-xy  14 0 DO $20 rg-emit LOOP
  0 17 at-xy  s-destroyed
  restore-ship-bg
  SHIP-POS C@ SHIP-POS 1 + C@
  explode-destruct
  proximity-damage
  clear-tactical
  draw-border draw-stars draw-storm-stars draw-event-horizon
  draw-base draw-jovians-live
  0 17 at-xy  s-destroyed
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
  LOOP THEN ;

: count-bases  ( -- n )
  0  64 0 DO GALAXY I + C@ q-base? IF 1 + THEN LOOP ;

VARIABLE overlay                  \ 0=tactical, 1=damage, 2=scan

: draw-damage  ( -- )
  clear-tactical
  2 2 at-xy  count-jovians rg-u.
  S"  JOVIANS LEFT" rg-type
  2 4 at-xy  count-bases rg-u.
  S"  BASES LEFT" rg-type
  2 7 at-xy  S" DAMAGE" rg-type
  2 9 at-xy   S" ION ENGINES" rg-type     pdmg-ion @ 28 rg-u.r
  2 10 at-xy  S" HYPERDRIVE" rg-type      pdmg-warp @ 28 rg-u.r
  2 11 at-xy  S" SCANNERS" rg-type        pdmg-scan @ 28 rg-u.r
  2 12 at-xy  S" DEFLECTORS" rg-type      pdmg-defl @ 28 rg-u.r
  2 13 at-xy  S" MASERS" rg-type          pdmg-masr @ 28 rg-u.r ;

\ Overwrite just the changing values (no clear, no flicker) (#231)
: update-damage  ( -- )
  2 2 at-xy  count-jovians rg-u.  S"  " rg-type
  2 4 at-xy  count-bases rg-u.    S"  " rg-type
  9 tcy !  pdmg-ion @  28 rg-u.r
  10 tcy !  pdmg-warp @ 28 rg-u.r
  11 tcy !  pdmg-scan @ 28 rg-u.r
  12 tcy !  pdmg-defl @ 28 rg-u.r
  13 tcy !  pdmg-masr @ 28 rg-u.r ;

: do-damage-report  ( -- )
  draw-damage 1 overlay ! ;

\ ── Long range scan (command 3) ───────────────────────────────────────
\ Display 8x8 galaxy map showing Jovians, bases, storms, player position.
\ Cell format: B=base, 1-3=jovians, M=storm, E=player quadrant.
\ Each cell is 3 chars wide, row labels at col 1, col headers at col 4.

VARIABLE sg-row                   \ scan grid: outer loop row

: draw-scan  ( -- )
  clear-tactical
  4 1 at-xy  S" LONG RANGE SCAN" rg-type
  8 0 DO
    I 3 * 5 + 3 at-xy  I CHAR 0 + rg-emit
  LOOP
  8 0 DO
    I sg-row !
    1 I 4 + at-xy  I CHAR 0 + rg-emit
    8 0 DO
      I 3 * 4 + sg-row @ 4 + at-xy
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
  LOOP ;

: do-scan  ( -- )
  draw-scan 2 overlay ! ;

: exec-command  ( -- )
  cmd-num @ 1 = IF
    overlay @ 1 = IF dismiss-overlay ELSE do-damage-report THEN
  THEN
  cmd-num @ 2 = IF
    0 overlay ! do-warp
  THEN
  cmd-num @ 3 = IF
    overlay @ 2 = IF dismiss-overlay ELSE do-scan THEN
  THEN
  cmd-num @ 4 = IF
    cmd-val @ DUP 100 > IF DROP 100 THEN
    DUP pdmg-defl @ > IF DROP pdmg-defl @ THEN pshields !
    draw-panel
  THEN
  cmd-num @ 5 = IF
    penergy @ 2 > IF cmd-val @ fire-maser
    ELSE S" NO ENERGY" cmd-reject THEN
  THEN
  cmd-num @ 6 = IF
    pmissiles @ IF cmd-val @ fire-missile
    ELSE S" NO MISSILES" cmd-reject THEN
  THEN
  cmd-num @ 7 = IF do-destruct THEN
  cmd-state @ 2 = IF 0 cmd-state ! EXIT THEN  \ feedback shown, keep it
  0 cmd-state !
  sd-active @ 0= IF draw-cmd-prompt THEN ;

: cmd-start  ( cmd -- )
  cmd-num !
  \ Commands 1, 3: immediate (station toggle)
  cmd-num @ 1 = IF exec-command EXIT THEN
  cmd-num @ 3 = IF exec-command EXIT THEN
  \ Commands 5, 6, 7: return to tactical first (need viewport)
  overlay @ IF
    cmd-num @ 5 < 0= IF dismiss-overlay THEN
  THEN
  \ Others: show command name, start digit collection
  1 cmd-state !
  0 cmd-val !  0 cmd-digits !
  clear-cmd-area
  17 18 at-xy
  cmd-num @ 2 = IF S" WARP?" rg-type EXIT THEN
  cmd-num @ 4 = IF S" SHLD?" rg-type EXIT THEN
  cmd-num @ 5 = IF S" MASR?" rg-type EXIT THEN
  cmd-num @ 6 = IF S" MISS?" rg-type EXIT THEN
  S" DEST?" rg-type ;

: cmd-add-digit  ( digit -- )
  cmd-digits @ 3 < IF
    DUP cmd-val @ 10 * + cmd-val !
    1 cmd-digits +!
    \ Position cursor after command name prompt
    cmd-digits @ 21 + 18 at-xy
    CHAR 0 + rg-emit
    \ Auto-execute: warp after 2 digits (#235), destruct after 3 (#236)
    cmd-num @ 2 = cmd-digits @ 2 = AND IF exec-command THEN
    cmd-num @ 7 = cmd-digits @ 3 = AND IF exec-command THEN
  ELSE
    DROP
  THEN ;

\ Handle key during digit collection
: process-cmd-input  ( key -- )
  DUP $30 < IF
    DUP $0D = IF DROP exec-command
    ELSE $0C = IF 0 cmd-state ! draw-cmd-prompt THEN THEN
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
VARIABLE key-latch                \ latched keypress (survives between polls)

\ Sample keyboard — if a key is down, latch it for later processing.
\ Called at multiple points in the game loop to catch brief taps.
: latch-key  ( -- )
  KEY? ?DUP IF key-latch ! THEN ;

: dismiss-overlay  ( -- )
  0 overlay !
  refresh-after-kill ;

: process-key  ( -- )
  latch-key
  key-latch @
  0 key-latch !                   \ consume latch
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
    SPR-MSL1 msl-px @ 1 - msl-py @ 1 - spr-erase-box
    0 msl-active !
  THEN
  \ Erase base sprite
  SPR-BASE BASE-POS C@ 3 - BASE-POS 1 + C@ 2 - spr-erase-box
  \ Clear quadrant state
  0 qbase !  0 QCOUNTS 2 + C!
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
: jov-near-base?  ( -- idx|-1 )
  qbase @ 0= IF -1 EXIT THEN
  -1 jnb-result !
  qjovians @ ?DUP IF 0 DO
    JOV-DMG I + C@ IF
      JOV-POS I 2 * + BASE-POS mdist 30 < IF
        I jnb-result !
      THEN
    THEN
  LOOP THEN
  jnb-result @ ;

: tick-base-attack  ( -- )
  jov-near-base? DUP 0 < IF DROP
    0 base-attack !
  ELSE
    base-attack @ 1 + DUP base-attack !
    DUP 180 = IF DROP destroy-base
    ELSE
      \ Fire beam at base every 60 frames
      60 /MOD SWAP DROP 0= IF
        jbeam-cool @ 0= IF BASE-POS fire-jbeam
        ELSE DROP THEN
      ELSE DROP THEN
    THEN
  THEN ;

: main  ( -- )
  $8000 4096 0 FILL               \ clear game data region ($8000-$8FFF)
  rg-init
  init-text
  init-sin
  12345 seed !
  0 sos-active !

  \ Title + level select
  title-screen

  \ Generate galaxy with selected level
  glevel @ gen-galaxy
  gjovians @ gjovians0 !         \ save initial count for win screen
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
  0 cmd-state !  0 prev-key !  0 key-latch !
  0 beam-total !  -1 beam-hit-idx !
  0 jbeam-total !  0 jbeam-hit-ship !  0 jbeam-pending !  jbeam-cooldown jbeam-cool !
  0 msl-active !  0 msl-dirty !
  0 docked !  0 prev-docked !  0 death-cause !
  0 sd-active !  0 base-attack !
  0 jov-moved !  0 spawn-pending !  0 migrate-timer !  -1 prev-cond !  0 overlay !  0 think-slot !
  0 frame-tick !
  0 check-win !
  100 prev-energy !
  10 prev-missiles !

  \ Game loop
  BEGIN
    1 frame-tick +!

    \ ── Every frame: player input + fast systems ──
    overlay @ 0= IF
      save-ship-pos
      move-ship
    THEN
    process-key
    overlay @ 0= IF
      tick-missile
      tick-jbeam
    THEN

    overlay @ 0= IF
      jov-contact-check               \ every frame: kill Jovians touching stars/bhole (#243)
      frame-tick @ 1 AND 0= IF
        \ ── Even frames: ship physics + AI thinking ──
        ship-gravity
        tick-jovians
      ELSE
        \ ── Odd frames: collisions + gravity pull ──
        check-collisions               \ ship vs star/bhole (1-frame delay OK)
        jov-gravity-pull               \ gated by grav-tick & 3 internally (#243)
      THEN
    THEN
    \ ── Background tasks: run even during overlays (#232) ──
    frame-tick @ 1 AND IF
      frame-tick @ 7 AND DUP 3 = IF
        jov-check-regen  check-dock  tick-dock  tick-base-attack
      THEN 5 = IF
        tick-stardate  tick-migrate  check-spawn  update-cond
      THEN
    THEN
    tick-destruct

    VSYNC
    overlay @ 0= IF max-draw-y wait-past-row THEN

    overlay @ IF
      \ Overlay active: refresh values ~1/sec without clearing (#231)
      overlay @ 1 = IF
        frame-tick @ 63 AND 0= IF update-damage THEN
      THEN
    ELSE

    \ ── LAYER 2: Erase beam tails (paint black + redraw stars) ──
    tick-jbeam-erase
    tick-beam-erase
    beam-total @ jbeam-total @ OR IF 1 moved !  draw-stars THEN

    \ ── LAYER 1: Sprite rendering (split cycle) ──
    jov-moved @ IF
      \ Full cycle: ship + Jovians + stars
      msl-active @ IF
        SPR-MSL1 msl-px @ 1 - msl-py @ 1 - spr-erase-box
      THEN
      restore-ship-bg
      restore-jov-bgs
      draw-stars
      save-jov-oldpos save-jov-bgs
      save-ship-bg
      draw-jovians-live
      draw-ship
      msl-dirty @ IF
        msl-scrx msl-px !  msl-scry msl-py !
        0 msl-dirty !
      THEN
      msl-active @ IF
        msl-spr msl-px @ 1 - msl-py @ 1 - spr-draw
      THEN
      0 jov-moved !
    ELSE moved @ msl-dirty @ OR IF
      \ Ship/missile only: skip Jovian bg ops + stars
      msl-active @ IF
        SPR-MSL1 msl-px @ 1 - msl-py @ 1 - spr-erase-box
      THEN
      restore-ship-bg
      save-ship-bg draw-ship
      msl-dirty @ IF
        msl-scrx msl-px !  msl-scry msl-py !
        0 msl-dirty !
      THEN
      msl-active @ IF
        msl-spr msl-px @ 1 - msl-py @ 1 - spr-draw
      THEN
    THEN THEN

    \ ── LAYER 2: Advance beam heads (draw new pixels) ──
    tick-beam-draw
    tick-jbeam-draw

    \ ── Apply deferred hit damage ──
    apply-beam-hit
    apply-jbeam-hit
    \ ── Win/lose checks (after any kill, one-shot) ──
    check-win @ IF
      0 check-win !                  \ clear immediately — don't re-check every frame
      count-jovians 0= IF
        cancel-jbeam cancel-beam
        clear-tactical
        2 3 at-xy  S" ALL " rg-type  gjovians0 @ rg-u.
        S"  JOVIANS" rg-type
        2 5 at-xy  s-destroyed
        2 8 at-xy  s-upsys
        2 10 at-xy S" IS SAVED" rg-type
        \ Rating: stardates / level → lower is better
        2 13 at-xy S" RATING  " rg-type
        gtime @ glevel @ /MOD SWAP DROP  \ stardates per level
        DUP 2 < IF DROP S" ADMIRAL" ELSE
        DUP 4 < IF DROP S" COMMANDER" ELSE
        DUP 7 < IF DROP S" CAPTAIN" ELSE
        DROP S" ENSIGN"
        THEN THEN THEN rg-type
        0 18 at-xy s-again
        KEY DROP
        main EXIT
      THEN
      count-bases 0= IF
        cancel-jbeam cancel-beam
        clear-tactical
        2 3 at-xy  S" ALL BASES" rg-type
        2 5 at-xy  s-destroyed
        2 8 at-xy  s-upsys
        2 10 at-xy S" WILL FALL" rg-type
        0 18 at-xy s-again
        KEY DROP
        main EXIT
      THEN
    THEN

    \ ── Death check: energy depleted ──
    penergy @ 0= IF
      cancel-jbeam cancel-beam
      SHIP-POS C@ SHIP-POS 1 + C@
      0 17 at-xy  14 0 DO $20 rg-emit LOOP
      death-cause @ 1 = IF
        \ Black hole — ship vanishes, no explosion
        restore-ship-bg
        clear-tactical
        draw-border draw-stars draw-storm-stars draw-event-horizon
        draw-base draw-jovians-live
        2DROP
        0 17 at-xy
        S" BLACK HOLE" rg-type
      ELSE death-cause @ 2 = IF
        \ Self-destruct — explosion already happened
        2DROP
        0 17 at-xy
        s-destroyed
      ELSE
        \ Energy depleted or star collision — ship explodes
        0 17 at-xy  s-destroyed
        restore-ship-bg
        explode-ship
        clear-tactical
        draw-border draw-stars draw-storm-stars draw-event-horizon
        draw-base draw-jovians-live
      THEN THEN
      \ AGAIN? prompt — any key restarts
      0 18 at-xy
      s-again
      KEY DROP
      main EXIT
    THEN
    THEN                          \ close overlay IF/ELSE
    \ ── Panel updates: run even during overlays ──
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
  AGAIN ;

main
