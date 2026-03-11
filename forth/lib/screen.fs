\ screen.fs — VDG screen utilities and vsync
\
\ Provides: vsync, cls-black, cls-green
\ Requires: kernel primitives C@, AND, DROP, AT
\
\ vsync waits for the VDG vertical sync signal (60 Hz).
\ PIA0 CB1 flag ($FF03 bit 7) is set by VDG; reading $FF02 clears it.

: vsync  ( -- )
  BEGIN  $FF03 C@ $80 AND  UNTIL
  $FF02 C@ DROP ;

: cls-black  ( -- )
  512 0 DO  $80 $0400 I + C!  LOOP ;

: cls-green  ( -- )
  512 0 DO  $60 $0400 I + C!  LOOP
  0 0 AT ;
