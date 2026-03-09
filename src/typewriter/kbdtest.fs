\ kbdtest.fs — show hex code of each keypress
\
\ Press any key — shows its hex ASCII value on screen.
\ Uses KEY (blocking), so it waits for each press.

: NIBBLE  ( n -- )  DUP 10 < IF CHAR 0 + ELSE 10 - CHAR A + THEN EMIT ;
: HEX.   ( n -- )  16 /MOD NIBBLE NIBBLE ;
: SPC    $20 EMIT ;

: cls  512 0 DO  $60 $0400 I + C!  LOOP  0 0 AT ;

: KBDTEST
  cls
  BEGIN
    KEY
    DUP EMIT
    CHAR = EMIT
    HEX.
    SPC
  AGAIN ;

KBDTEST
