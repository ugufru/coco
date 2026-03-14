\ rg-test.fs — Test app for RG6 artifact-color pixel library
\
\ Tests rg-pset, rg-hline, and rg-line with visual verification.
\ Press any key between tests.  Run in XRoar with NTSC artifacts.

INCLUDE ../../forth/lib/vdg.fs
INCLUDE ../../forth/lib/screen.fs
INCLUDE ../../forth/lib/rg-pixel.fs

VARIABLE row
VARIABLE bc

\ Draw one full-width row of artifact pixels in a given color
: draw-row  ( y color -- )
  bc ! row !
  128 0 DO
    I row @ bc @ rg-pset
  LOOP ;

\ Draw a horizontal bar from y-start to y-end (inclusive)
: draw-bar  ( y-start y-end color -- )
  bc !
  1 + SWAP
  DO
    I bc @ draw-row
  LOOP ;

\ ── Test 1: Color bars ──────────────────────────────────────────────────

: test-bars  ( -- )
  20 29 3 draw-bar             \ white bar
  50 59 1 draw-bar             \ blue bar
  80 89 2 draw-bar             \ red bar
  \ Corner pixels
  0   0   3 rg-pset
  127 0   3 rg-pset
  0   191 3 rg-pset
  127 191 3 rg-pset ;

\ ── Test 2: Lines in all directions ─────────────────────────────────────

: test-lines  ( -- )
  \ White lines from center outward (starburst pattern)
  64 96 127 96  3 rg-line      \ right
  64 96 0   96  3 rg-line      \ left
  64 96 64  0   3 rg-line      \ up
  64 96 64  191 3 rg-line      \ down

  \ Blue diagonal lines
  64 96 127 0   1 rg-line      \ upper-right
  64 96 0   191 1 rg-line      \ lower-left
  64 96 0   0   1 rg-line      \ upper-left
  64 96 127 191 1 rg-line      \ lower-right

  \ Red lines at shallow angles
  64 96 127 80  2 rg-line      \ slight up-right
  64 96 0   112 2 rg-line      \ slight down-left
  64 96 127 130 2 rg-line      \ slight down-right
  64 96 0   62  2 rg-line ;    \ slight up-left

\ ── Test 3: Border rectangle ────────────────────────────────────────────

: test-border  ( -- )
  0   0   127 0   3 rg-line   \ top
  127 0   127 143 3 rg-line   \ right
  127 143 0   143 3 rg-line   \ bottom
  0   143 0   0   3 rg-line ; \ left

\ ── Main ─────────────────────────────────────────────────────────────────

: main  ( -- )
  rg-init

  test-bars
  KEY DROP  rg-pcls

  test-lines
  KEY DROP  rg-pcls

  test-border
  KEY DROP

  reset-text
  HALT ;

main
