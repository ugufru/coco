\ src/kaleidoscope/kaleidoscope.fs — four-way symmetric pattern generator
\
\ Draws random colored pixels in semigraphics-4 mode with four-way
\ symmetry around the screen center, creating evolving kaleidoscope
\ patterns.  Press BREAK to exit back to BASIC.
\
\ Semigraphics-4 maps a 64x32 pixel grid onto the 32x16 VDG text
\ display.  Each byte in video RAM ($0400-$05FF):
\   bit 7     = 1  (semigraphics flag)
\   bits 6-4  = color (0-7 maps to green/yellow/blue/red/buff/cyan/magenta/orange)
\   bit 3     = bottom-right element
\   bit 2     = bottom-left element
\   bit 1     = top-right element
\   bit 0     = top-left element
\
\ Build:   make
\ Load:    LOADM"KALEIDSC":EXEC

\ ── Shared libraries ──────────────────────────────────────────────────

INCLUDE ../../forth/lib/rng.fs
INCLUDE ../../forth/lib/screen.fs
INCLUDE ../../forth/lib/bye.fs

\ ── Semigraphics-4 pixel operations ───────────────────────────────────

VARIABLE xr    \ x remainder (0 or 1) after dividing by 2
VARIABLE yr    \ y remainder (0 or 1) after dividing by 2
VARIABLE sg-c  \ color for sg4-set

\ Calculate video RAM address and set xr/yr remainders
: sg4-addr  ( x y -- addr )
  2 /MOD SWAP yr !  32 *       \ ( x  row-offset )
  SWAP
  2 /MOD SWAP xr !  +          \ ( byte-offset )
  $0400 + ;

\ Build the pixel mask from xr/yr
\   (0,0)=bit0  (1,0)=bit1  (0,1)=bit2  (1,1)=bit3
: sg4-mask  ( -- mask )
  yr @ IF 4 ELSE 1 THEN
  xr @ IF 2 * THEN ;

\ Set a pixel: merge new pixel bit and color into existing byte
: sg4-set  ( x y color -- )
  sg-c !                        \ save color
  sg4-addr                      \ ( addr )
  sg4-mask                      \ ( addr mask )
  OVER C@  $0F AND  OR          \ ( addr  old-pixels | new-bit )
  sg-c @ DUP + DUP + DUP + DUP + OR  \ ( addr  pixels-with-color )  color<<4
  $80 OR                        \ ( addr  final-byte )
  SWAP C! ;

\ Clear a pixel: remove one pixel bit, keep everything else
: sg4-reset  ( x y -- )
  sg4-addr                      \ ( addr )
  sg4-mask                      \ ( addr mask )
  $FF SWAP -                    \ ( addr  AND-mask )  — clears just that bit
  OVER C@  AND                  \ ( addr  new-byte )
  SWAP C! ;

\ ── Title screen ──────────────────────────────────────────────────────

: .title  ( -- )
  10 7 AT
  75 EMIT 65 EMIT 76 EMIT 69 EMIT 73 EMIT 68 EMIT       \ KALEID
  79 EMIT 83 EMIT 67 EMIT 79 EMIT 80 EMIT 69 EMIT       \ OSCOPE
  9 9 AT
  80 EMIT 82 EMIT 69 EMIT 83 EMIT 83 EMIT 32 EMIT       \ PRESS_
  65 EMIT 78 EMIT 89 EMIT 32 EMIT 75 EMIT 69 EMIT       \ ANY KE
  89 EMIT ;                                               \ Y

\ ── Kaleidoscope logic ────────────────────────────────────────────────
\ Four-way symmetry around the screen center (32, 16).
\ Random point (dx, dy) maps to four quadrants:
\   (32+dx, 16+dy)  (31-dx, 16+dy)
\   (32+dx, 15-dy)  (31-dx, 15-dy)

VARIABLE dx    VARIABLE dy    VARIABLE dc

: pick-point  ( -- )
  32 rnd dx !
  16 rnd dy ! ;

: plot4  ( -- )
  8 rnd dc !
  32 dx @ +  16 dy @ +  dc @  sg4-set
  31 dx @ -  16 dy @ +  dc @  sg4-set
  32 dx @ +  15 dy @ -  dc @  sg4-set
  31 dx @ -  15 dy @ -  dc @  sg4-set ;

: erase4  ( -- )
  32 dx @ +  16 dy @ +  sg4-reset
  31 dx @ -  16 dy @ +  sg4-reset
  32 dx @ +  15 dy @ -  sg4-reset
  31 dx @ -  15 dy @ -  sg4-reset ;

: step  ( -- )
  pick-point
  4 rnd 0= IF  erase4  ELSE  plot4  THEN ;

\ ── Main ──────────────────────────────────────────────────────────────

: kaleidoscope  ( -- )
  \ Title screen — seed RNG by counting VSYNC frames until keypress
  cls-green
  .title
  0 seed !
  BEGIN
    vsync
    seed @ 1 + seed !
    KEY?
  UNTIL

  \ Wait for key release before starting
  BEGIN  vsync  KEY? 0=  UNTIL

  \ Run the pattern (BREAK to exit)
  cls-black
  BEGIN
    $FF $FFD7 C!                  \ SAM double speed (~1.78 MHz)
    step step step step
    step step step step
    step step step step
    step step step step
    $FF $FFD6 C!                  \ SAM normal speed (display refresh)
    vsync
    key? IF key $03 = IF exit-basic THEN THEN
  0 UNTIL ;

kaleidoscope
