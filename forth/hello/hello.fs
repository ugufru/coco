\ hello.fs — minimal test program for the CoCo Forth kernel

: SPACE  32 EMIT ;
: DOT    CHAR . EMIT ;

: BARE
  CHAR B EMIT  CHAR A EMIT  CHAR R EMIT  CHAR E EMIT ;

: NAKED
  CHAR N EMIT  CHAR A EMIT  CHAR K EMIT  CHAR E EMIT  CHAR D EMIT ;

: FORTH
  CHAR F EMIT  CHAR O EMIT  CHAR R EMIT  CHAR T EMIT  CHAR H EMIT ;

: digit  ( n -- )  CHAR 0 + EMIT ;

: version  ( ver -- )
  DUP 8 RSHIFT digit DOT $FF AND digit ;

BARE SPACE NAKED SPACE FORTH SPACE KERN-VERSION version HALT
