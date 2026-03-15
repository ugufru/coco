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

\ ══════════════════════════════════════════════════════════════════════════
\  GALAXY DATA MODEL
\ ══════════════════════════════════════════════════════════════════════════
\
\ Galaxy: 8x8 = 64 quadrants, 1 byte each at GALAXY ($5800).
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

\ Game data starts at $6800 (above VRAM which ends at $67FF).
$6800 CONSTANT GALAXY          \ 64 bytes: 8x8 quadrant data

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
$6840 CONSTANT STAR-POS        \ 5 stars x 2 bytes = 10 bytes
$684A CONSTANT JOV-POS         \ 3 jovians x 2 bytes = 6 bytes
$6850 CONSTANT BASE-POS        \ 1 base x 2 bytes = 2 bytes
$6852 CONSTANT BHOLE-POS       \ 1 black hole x 2 bytes = 2 bytes
$6854 CONSTANT SHIP-POS        \ player ship x 2 bytes = 2 bytes

\ Jovian damage (3 bytes, one per jovian: 100=full health, 0=dead)
$6856 CONSTANT JOV-DMG         \ 3 bytes

\ Quadrant object counts (from the packed byte, cached for speed)
VARIABLE qstars                \ star count in current quadrant
VARIABLE qjovians              \ jovian count in current quadrant
VARIABLE qbase                 \ base present? (0 or 1)
VARIABLE qbhole                \ black hole present? (0 or 1)

\ SOS alert state
VARIABLE sos-active
VARIABLE sos-col
VARIABLE sos-row

\ ── Sprite data ──────────────────────────────────────────────────────────
\ 7x5 pixel sprites in 2bpp artifact-color format.
\ Built at init time using datawrite helpers (tb).

$6900 CONSTANT SPR-SHIP           \ Endever: blue chevron (12 bytes)
$690C CONSTANT SPR-JOV            \ Jovian: red diamond (12 bytes)
$6918 CONSTANT SPR-BASE           \ UP base: blue cross (12 bytes)

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
  $05 tb $40 tb ;

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
  0 gtime ! ;

\ ══════════════════════════════════════════════════════════════════════════
\  STATUS PANEL
\ ══════════════════════════════════════════════════════════════════════════

\ Override rg-char for 8-row glyphs with 10-pixel row spacing
: rg-char  ( char cx cy -- )
  10 * cb @ * SWAP + cv @ +      \ dest = vram + cy*10*bpr + cx
  SWAP glyph-addr SWAP           \ ( glyph dest )
  8 0 DO
    OVER I + C@
    OVER I cb @ * + C!
  LOOP DROP DROP ;

: init-text  ( -- )
  init-font
  rv @ cv !
  32 cb !
  $F8 set-pia ;          \ CSS=1: buff/white for NTSC artifacts

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
  qjovians @ IF
    3 - tcx !
    CHAR R rg-emit CHAR E rg-emit CHAR D rg-emit
  ELSE
    5 - tcx !
    CHAR G rg-emit CHAR R rg-emit CHAR E rg-emit CHAR E rg-emit CHAR N rg-emit
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

: draw-quadrant  ( -- )
  draw-border draw-stars draw-jovians draw-base draw-ship ;

\ ══════════════════════════════════════════════════════════════════════════
\  SHIP MOVEMENT (arrow keys via direct matrix scan)
\ ══════════════════════════════════════════════════════════════════════════

\ Scan arrow keys (all on column 3) and move ship.
\ Bounds: x 4-123, y 4-139 (inside tactical border).

VARIABLE moved                    \ flag: did ship move this frame?
7 CONSTANT SHIP-DX                \ pixels per step (ship width)
5 CONSTANT SHIP-DY                \ pixels per step (ship height)

: move-ship  ( -- )
  0 moved !
  \ Arrow keys: all on row 3 ($08), different columns
  KB-C3 KBD-SCAN $08 AND IF       \ UP: col 3, row 3
    SHIP-POS 1 + C@ SHIP-DY 4 + > IF
      SHIP-POS 1 + C@ SHIP-DY - SHIP-POS 1 + C!
      1 moved !
    THEN
  THEN
  KB-C4 KBD-SCAN $08 AND IF       \ DN: col 4, row 3
    SHIP-POS 1 + C@ 139 SHIP-DY - < IF
      SHIP-POS 1 + C@ SHIP-DY + SHIP-POS 1 + C!
      1 moved !
    THEN
  THEN
  KB-C5 KBD-SCAN $08 AND IF       \ LT: col 5, row 3
    SHIP-POS C@ SHIP-DX 4 + > IF
      SHIP-POS C@ SHIP-DX - SHIP-POS C!
      1 moved !
    THEN
  THEN
  KB-C6 KBD-SCAN $08 AND IF       \ RT: col 6, row 3
    SHIP-POS C@ 123 SHIP-DX - < IF
      SHIP-POS C@ SHIP-DX + SHIP-POS C!
      1 moved !
    THEN
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
30 CONSTANT MASER-DMG             \ damage per hit

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
      MASER-DMG - DUP 0 < IF DROP 0 THEN
      JOV-DMG hc-i @ + C!
      \ If dead, erase sprite
      JOV-DMG hc-i @ + C@ 0= IF
        SPR-JOV
        hc-jx @ 3 - hc-jy @ 2 -
        spr-erase-box
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
  \ Erase any existing beam first
  beam-timer @ IF erase-beam THEN
  \ Save ship position as beam origin
  SHIP-POS C@ beam-x1 !  SHIP-POS 1 + C@ beam-y1 !
  \ Calculate and clamp endpoint
  DUP 140 angle-dx beam-x1 @ + beam-x2 !
  140 angle-dy beam-y1 @ + beam-y2 !
  clamp-beam
  \ Draw blue beam and start timer
  beam-x1 @ beam-y1 @
  beam-x2 @ beam-y2 @
  1 rg-line
  BEAM-FRAMES beam-timer !
  \ Check for Jovian hits
  check-hits ;

: tick-beam  ( -- )
  beam-timer @ IF
    beam-timer @ 1 - beam-timer !
    beam-timer @ 0= IF erase-beam THEN
  THEN ;

\ ── Command dispatch ───────────────────────────────────────────────────

: exec-command  ( -- )
  cmd-num @ 5 = IF cmd-val @ fire-maser THEN
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

: main  ( -- )
  rg-init
  init-text
  init-sin
  init-sprites
  init-player
  12345 seed !
  0 sos-active !

  \ Generate galaxy and enter starting quadrant
  1 gen-galaxy
  find-base-quadrant

  \ Draw initial tactical view and status panel
  draw-quadrant
  draw-panel
  0 cmd-state !  0 prev-key !
  0 beam-timer !

  \ Game loop
  BEGIN
    save-ship-pos
    move-ship
    tick-beam
    process-key
    moved @ IF
      VSYNC                       \ sync to blank before redraw
      erase-ship
      draw-ship
    THEN
    VSYNC
  AGAIN ;

main
