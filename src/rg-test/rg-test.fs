\ rg-test.fs — Test app for RG6 artifact-color pixel and sprite libraries
\
\ Tests rg-pset, rg-line, and sprite draw/erase.
\ Press any key between tests.  Run in XRoar with NTSC artifacts.

INCLUDE ../../forth/lib/vdg.fs
INCLUDE ../../forth/lib/screen.fs
INCLUDE ../../forth/lib/rg-pixel.fs
INCLUDE ../../forth/lib/datawrite.fs
INCLUDE ../../forth/lib/sprite.fs
INCLUDE ../../forth/lib/trig.fs

VARIABLE row
VARIABLE bc

: draw-row  ( y color -- )
  bc ! row !
  128 0 DO  I row @ bc @ rg-pset  LOOP ;

: draw-bar  ( y-start y-end color -- )
  bc !  1 + SWAP
  DO  I bc @ draw-row  LOOP ;

\ ── Sprite data ──────────────────────────────────────────────────────────
\ Stored at $5800 (well above app code, below VRAM at $3000... wait,
\ $5800 > $3000.  Use $4800 which is in free RAM above app.)

$4800 CONSTANT SPR-SHIP        \ Endever sprite: 8x7, blue
$4812 CONSTANT SPR-JOVIAN      \ Jovian sprite: 8x5, red

: init-sprites
  \ ── Endever "V" shape (8 wide x 7 tall, blue=color 1) ──
  \ Color 1 = 01 in bit pairs.  4 artifact pixels per byte.
  \ Bit layout: [p0(7-6) p1(5-4) p2(3-2) p3(1-0)]
  SPR-SHIP tp !
  8 tb 7 tb                    \ 8 wide, 7 tall (2 bytes per row)
  \ Row 0: __B___B_  (blue at x=2, x=5)
  $04 tb $10 tb
  \ Row 1: __B___B_
  $04 tb $10 tb
  \ Row 2: ___B_B__  (blue at x=3, x=4)
  $01 tb $40 tb
  \ Row 3: ___BB___
  $01 tb $40 tb
  \ Row 4: ____B___  (blue at x=4)
  $00 tb $40 tb
  \ Row 5: ___B_B__
  $01 tb $40 tb
  \ Row 6: __B___B_
  $04 tb $10 tb

  \ ── Jovian "<*>" shape (8 wide x 5 tall, red=color 2) ──
  \ Color 2 = 10 in bit pairs.
  SPR-JOVIAN tp !
  8 tb 5 tb                    \ 8 wide, 5 tall (2 bytes per row)
  \ Row 0: ___RR___  (red at x=3,4)
  $02 tb $80 tb
  \ Row 1: _R_RR_R_  (red at x=1,3,4,6)
  $22 tb $88 tb
  \ Row 2: RR_RR_RR  (red at x=0,1,3,4,6,7)
  $A2 tb $8A tb
  \ Row 3: _R_RR_R_
  $22 tb $88 tb
  \ Row 4: ___RR___
  $02 tb $80 tb ;

\ ── Test 1: Color bars ──────────────────────────────────────────────────

: test-bars  ( -- )
  20 29 3 draw-bar             \ white
  50 59 1 draw-bar             \ blue
  80 89 2 draw-bar ;           \ red

\ ── Test 2: Lines ────────────────────────────────────────────────────────

: test-lines  ( -- )
  64 96 127 96  3 rg-line      \ right (white)
  64 96 0   96  3 rg-line      \ left
  64 96 64  0   3 rg-line      \ up
  64 96 64  191 3 rg-line      \ down
  64 96 127 0   1 rg-line      \ diag (blue)
  64 96 0   191 1 rg-line
  64 96 0   0   1 rg-line
  64 96 127 191 1 rg-line
  64 96 127 80  2 rg-line      \ shallow (red)
  64 96 0   112 2 rg-line
  64 96 127 130 2 rg-line
  64 96 0   62  2 rg-line ;

\ ── Test 3: Sprites ──────────────────────────────────────────────────────

: test-sprites  ( -- )
  \ Draw ship at a few positions
  SPR-SHIP 20 30 spr-draw
  SPR-SHIP 60 80 spr-draw
  SPR-SHIP 100 50 spr-draw

  \ Draw Jovians
  SPR-JOVIAN 40 120 spr-draw
  SPR-JOVIAN 80 140 spr-draw
  SPR-JOVIAN 110 100 spr-draw

  \ Draw a tactical border
  0 0   127 0   3 rg-line
  127 0 127 160 3 rg-line
  127 160 0 160 3 rg-line
  0 160 0   0   3 rg-line ;

\ ── Test 4: Sprite erase ────────────────────────────────────────────────

: test-erase  ( -- )
  \ Erase middle ship and middle Jovian
  SPR-SHIP 60 80 spr-erase
  SPR-JOVIAN 80 140 spr-erase ;

\ ── Test 5: Angle-based lines (maser simulation) ────────────────────────
\ Draw lines from center at every 30 degrees using angle-dx/angle-dy.

VARIABLE ax  VARIABLE ay  VARIABLE fc

: fire-line  ( angle color -- )
  fc !
  DUP 60 angle-dx 64 + ax !
  60 angle-dy 96 + ay !
  64 96 ax @ ay @ fc @ rg-line ;

: test-angles  ( -- )
  init-sin
  \ Fire blue lines every 30 degrees
  0   1 fire-line
  30  1 fire-line
  60  1 fire-line
  90  1 fire-line
  120 1 fire-line
  150 1 fire-line
  180 1 fire-line
  210 1 fire-line
  240 1 fire-line
  270 1 fire-line
  300 1 fire-line
  330 1 fire-line
  \ Red line at 45 degrees
  45  2 fire-line ;

\ ── Main ─────────────────────────────────────────────────────────────────

: main  ( -- )
  rg-init
  init-sprites

  test-bars
  KEY DROP  rg-pcls

  test-lines
  KEY DROP  rg-pcls

  test-sprites
  KEY DROP

  test-erase
  KEY DROP  rg-pcls

  test-angles
  KEY DROP

  reset-text
  HALT ;

main
