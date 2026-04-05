\ screen.fs — VDG screen utilities and vsync
\
\ Provides: vsync, wait-past-row, count-blanking, cls-black, cls-green
\ Requires: kernel primitives (vsync, wait-past-row, count-blanking are
\           now kernel CODE words; cls-black/cls-green are Forth)
\
\ vsync waits for the VDG vertical sync signal (60 Hz).
\ PIA0 CB1 flag ($FF03 bit 7) is set by VDG; reading $FF02 clears it.

\ vsync ( -- )           — kernel primitive
\ wait-past-row ( row -- ) — kernel primitive
\ count-blanking ( -- n )  — kernel primitive

: cls-black  ( -- )
  512 0 DO  $80 $0400 I + C!  LOOP ;

: cls-green  ( -- )
  512 0 DO  $60 $0400 I + C!  LOOP
  0 0 AT ;
