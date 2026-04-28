\ bounce.fs — Bouncing ball sprite demo for the TRS-80 Color Computer
\
\ Controls:
\   Up/Down arrows: adjust blanking offset live
\   Space:          cycle mode 0/1/2
\   BREAK:          exit to BASIC
\
\ Mode 0: free-running (no sync)
\ Mode 1: VSYNC only (draw in blanking window)
\ Mode 2: VSYNC + HSYNC tracking
\
\ Build:   make
\ Load:    LOADM"BOUNCE":EXEC

INCLUDE ../../forth/lib/vdg.fs
INCLUDE ../../forth/lib/screen.fs
INCLUDE ../../forth/lib/rg-pixel.fs
INCLUDE ../../forth/lib/sprite.fs
INCLUDE ../../forth/lib/font5x7.fs
INCLUDE ../../forth/lib/bye.fs

\ ── rg-char ( char cx cy -- ) from rg-text.fs ──────────────────────
\ Render a font glyph into RG-mode VRAM.  Uses kernel config vars $75-$7B.
CODE rg-char
        PSHS    X
        LDA     1,U             ; A = cy
        LDB     VAR_RGROWH      ; B = row height
        MUL
        TFR     B,A             ; A = pixel row
        LDB     VAR_RGBPR       ; B = bytes per VRAM row
        MUL
        ADDB    3,U             ; add cx
        ADCA    #0
        ADDD    VAR_RGVRAM
        PSHS    D               ; save dest
        LDA     5,U             ; A = char
        CMPA    VAR_RGCHARMIN
        BHS     @over
        LDA     VAR_RGCHARMIN
@over   SUBA    VAR_RGCHARMIN
        LDB     VAR_RGGLYPHSZ
        MUL
        ADDD    VAR_RGFONT
        TFR     D,Y             ; Y = glyph source
        LDB     VAR_RGNROWS     ; B = row counter
        LDX     ,S++            ; X = dest
@copy   LDA     ,Y+
        STA     ,X
        LDA     VAR_RGBPR
        LEAX    A,X
        DECB
        BNE     @copy
        LEAU    6,U
        PULS    X
        ;NEXT
;CODE

\ ── Ball sprite data at $8000 ────────────────────────────────────────

$4000 CONSTANT BALL-SPR        \ ROM mode: $8000 is BASIC ROM, relocate to app data area
VARIABLE bp
: b,  ( byte -- )  bp @ C!  1 bp +! ;

: init-ball-spr  ( -- )
  BALL-SPR bp !
  7 b, 7 b,
  $0F b, $C0 b,
  $3F b, $F0 b,
  $FB b, $FC b,
  $FF b, $FC b,
  $FF b, $FC b,
  $3F b, $F0 b,
  $0F b, $C0 b, ;

\ ── Ball table ───────────────────────────────────────────────────────
\ 4 balls × 12 bytes at $8020.
\ Per ball: +0=x(2) +2=y(2) +4=dx(2) +6=dy(2) +8=ox(2) +10=oy(2)

4 CONSTANT NBALLS
$4020 CONSTANT BTBL

VARIABLE use-wpr       \ mode: 0/1/2
VARIABLE bl-offset     \ blanking offset (tunable)

: ball>  ( n -- addr )  12 * BTBL + ;

: init-one  ( x y dx dy n -- )
  ball> >R
  R@ 6 + !  R@ 4 + !          \ dy, dx
  DUP R@ 2 + !  R@ 10 + !     \ y → y and oy
  DUP R@ !  R> 8 + ! ;         \ x → x and ox

\ ── Init ─────────────────────────────────────────────────────────────

: init-bounce  ( -- )
  set-sam-v set-sam-f set-pia
  $6000 rg-init-at             \ VRAM at $6000-$77FF (32K ROM mode)
  init-font                    \ font goes to kernel font-base ($5800 ROM, $9000 all-RAM)
  0 KVAR-RGCHARMIN C!  7 KVAR-RGGLYPHSZ C!  7 KVAR-RGNROWS C!  32 KVAR-RGBPR C!  8 KVAR-RGROWH C!
  init-ball-spr
  $6000 6144 0 FILL
  60  90  2  1  0 init-one
  30  40  3  2  1 init-one
 100 130 -1 -2  2 init-one
  80  60 -2  3  3 init-one
  2 use-wpr !
  70 bl-offset ! ;

\ ── Movement ─────────────────────────────────────────────────────────

: move-one  ( addr -- )
  >R
  R@ @ R@ 8 + !               \ ox = x
  R@ 2 + @ R@ 10 + !          \ oy = y
  R@ @ R@ 4 + @ +
  DUP 4 < IF DROP 4  R@ 4 + @ NEGATE R@ 4 + ! THEN
  DUP 123 > IF DROP 123  R@ 4 + @ NEGATE R@ 4 + ! THEN
  R@ !
  R@ 2 + @ R@ 6 + @ +
  DUP 4 < IF DROP 4  R@ 6 + @ NEGATE R@ 6 + ! THEN
  DUP 170 > IF DROP 170  R@ 6 + @ NEGATE R@ 6 + ! THEN
  R> 2 + ! ;

VARIABLE draw-idx              \ which ball to update this frame

\ ── wait-past-row with tunable offset ────────────────────────────────

CODE wait-past-row-var   \ ( row offset -- )
        PSHS    X
        LDB     3,U             ; row
        ADDB    1,U             ; + offset
        LEAU    4,U             ; pop 2 args
        BEQ     @done
        LDA     $FF00           ; clear stale HSYNC
@wt     LDA     $FF01
        BPL     @wt
        LDA     $FF00
        DECB
        BNE     @wt
@done   PULS    X
        ;NEXT
;CODE

\ ── Render ───────────────────────────────────────────────────────────

: erase-one  ( addr -- )
  >R  BALL-SPR R@ 8 + @ 3 - R> 10 + @ 3 - spr-erase-box ;

: draw-one  ( addr -- )
  >R  BALL-SPR R@ @ 3 - R> 2 + @ 3 - spr-draw ;

: draw-ball-n  ( n -- )
  ball> >R
  use-wpr @ 2 = IF
    R@ 10 + @ R@ 2 + @ max 4 + bl-offset @ wait-past-row-var
  THEN
  R@ erase-one
  R> draw-one ;

\ ── HUD: show offset + mode as digits at bottom of screen ────────────
\ Character row 23 (pixel row 184).  Font index: 0=space, 27-36=0-9.

VARIABLE hx                   \ HUD cursor column

: hud-ch  ( font-idx -- )  hx @ 23 rg-char  1 hx +! ;
: hud-digit  ( d -- )  27 + hud-ch ;

: draw-hud  ( -- )
  0 hx !
  bl-offset @ 100 /MOD hud-digit
  10 /MOD hud-digit hud-digit
  0 hud-ch
  use-wpr @ hud-digit ;

\ ── Input ────────────────────────────────────────────────────────────
\ Up arrow: row 3, column mask ~$08 -> strobe $F7, check bit 3
\ Down arrow: row 4, column mask ~$10 -> strobe $EF, check bit 4
\ Space: row 0 in column ~$02 -> strobe $FD, check bit 0?
\ Actually just use: strobe all ($00), check for any non-arrow key

: check-keys  ( -- )
  \ Up arrow
  $F7 KBD-SCAN 8 AND IF
    bl-offset @ 1 + 250 min bl-offset !
  THEN
  \ Down arrow
  $EF KBD-SCAN 8 AND IF
    bl-offset @ 1 - 0 max bl-offset !
  THEN
  \ Space bar (column $FD, row bit 0 among others)
  0 KBD-SCAN DUP 0 <> IF
    \ Skip if it was an arrow we already handled
    DROP
  ELSE DROP THEN ;

\ Mode select: 0/1/2 keys — each in its own column, all row 4 ($10)
: check-mode  ( -- )
  $FE KBD-SCAN $10 AND IF  0 use-wpr !  THEN
  $FD KBD-SCAN $10 AND IF  1 use-wpr !  THEN
  $FB KBD-SCAN $10 AND IF  2 use-wpr !  THEN ;

\ ── Main loop ────────────────────────────────────────────────────────

: check-quit  ( -- )
  key? IF key $03 = IF exit-basic THEN THEN ;

: bounce  ( -- )
  init-bounce
  0 draw-idx !
  BEGIN
    draw-idx @ ball> move-one
    draw-idx @ 1 + ball> move-one
    use-wpr @ IF VSYNC THEN
    draw-idx @ draw-ball-n
    draw-idx @ 1 + draw-ball-n
    check-keys
    check-mode
    check-quit
    draw-hud
    draw-idx @ 2 + DUP NBALLS = IF DROP 0 THEN draw-idx !
  AGAIN ;

bounce
