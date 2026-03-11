\ print.fs — number printing utilities
\
\ Provides: negate, ?dup, u., .
\ Requires: kernel primitives DUP, SWAP, -, IF, /MOD, +, EMIT, 0, <

: negate  ( n -- -n )  0 SWAP - ;
: ?dup   ( x -- x x | 0 )  DUP IF DUP THEN ;
: u.     ( u -- )  10 /MOD ?dup IF u. THEN  CHAR 0 + EMIT ;
: .      ( n -- )  DUP 0 < IF negate CHAR - EMIT THEN  u. ;
