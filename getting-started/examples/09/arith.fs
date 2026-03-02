\ Chapter 9 example — Numbers on Screen

: NEGATE   0 SWAP - ;
: ?DUP     DUP IF DUP THEN ;

: U.  ( u -- )
  10 /MOD
  ?DUP IF U. THEN
  CHAR 0 + EMIT ;

: .  ( n -- )
  DUP 0 < IF NEGATE CHAR - EMIT THEN
  U. ;

: MAIN
  6 7 *   . CR     \ 42
  -3 4 *  . CR     \ -12
  1000    . CR     \ 1000
  -1      . CR ;   \ -1

MAIN HALT
