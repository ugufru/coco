\ rg-test.fs — Minimal test for RG6 artifact-color pixel library
\
\ Draws simple patterns to verify rg-pset works in RG6 mode.
\ Press any key to exit.  Run in XRoar with NTSC artifact coloring.

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

: main  ( -- )
  rg-init

  \ White bar at rows 20-29
  20 29 3 draw-bar

  \ Blue bar at rows 50-59
  50 59 1 draw-bar

  \ Red bar at rows 80-89
  80 89 2 draw-bar

  \ Single white pixels at corners
  0   0   3 rg-pset
  127 0   3 rg-pset
  0   191 3 rg-pset
  127 191 3 rg-pset

  KEY DROP
  reset-text
  HALT ;

main
