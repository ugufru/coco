\ print.fs — number printing utilities
\
\ Provides: u., .
\ Requires: kernel primitives NEGATE, ?DUP, DUP, IF, /MOD, +, EMIT, 0, <
\
\ NEGATE and ?DUP are kernel primitives (6809 assembly).

: u.     ( u -- )  10 /MOD ?dup IF u. THEN  CHAR 0 + EMIT ;
: .      ( n -- )  DUP 0 < IF negate CHAR - EMIT THEN  u. ;
